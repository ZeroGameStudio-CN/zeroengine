# ZeroEngine.Core

ZeroEngine 框架的核心基础设施包。

## 版本
- **当前版本**: 2.3.0
- **最小 Unity**: 2022.3 LTS

## 包含模块

### Core (核心)
- `Singleton<T>` - 非持久单例基类
- `MonoSingleton<T>` - Singleton 别名
- `PersistentSingleton<T>` - 跨场景持久单例
- `EventManager` - 事件总线系统
- `GameEvents` - 预定义事件常量
- `ServiceRegistry` - 类型化服务注册；SubsystemRegistration 时自动清除旧场景实例

### Pool (对象池)
- `PoolManager` - 对象池管理器 (PersistentSingleton)
- `PoolSpawnLease` - 指向一次准确借出的值类型句柄；旧代次／复制／重复归还不会回收新使用者
- `IPoolable` - 可池化对象接口
- `PoolType` - 池分类枚举

### 精确借用归属（2.3 候选）

`PoolManager.SpawnLease(prefab, position, rotation, poolType, autoActive)` 在借出时签发句柄。
使用 `lease.IsCurrent`／`lease.Instance` 核对写入权，使用 `lease.TryDespawn()` 或 `Dispose()` 归还。
`autoActive: false` 的准备阶段仍持有借用；实例先回池后由任意 Spawn 路径再次取出，旧句柄立即失效。
复制的句柄共享一次归还权；销毁实例的归还仍清除该代次账目，失效归还不走 raw Despawn 的销毁 fallback。
不存在事后从裸 GameObject 猜测／认领旧借用的 Capture API。

已有 Spawn／泛型／异步签名保留并更新同一 active 记录，不新增第二份全局对象表或每对象 watcher。
原 raw Despawn 仍指向当前借出；需要保护晚到回调的消费者应持有句柄，不能只保存 GameObject。
用户回调在归还期间重借对象时，旧归还不会在回调之后关闭新对象；清池先退役旧账目快照。
PersistentSingleton 退出／销毁与 PoolManager base 清理沿 POB Core 2.2.2 的现有修复对齐，
其余包、TCE、玩法和消费者正式 pin 不在本次候选中改动。

候选已在 Unity 6000.6.4f1 实际 Package Manager 解析后完成 11 项包内 PlayMode，以及两个真实消费者的三条复用回归和六条正常／退出对照。普通预览、Timeline、命中、取消与原池销毁均分别核对；原短观察窗口对照已单独修正并复核，失败记录不混算通过。上述为本地候选证明，正式 Git 包采用与发布另行管理。

### Utils (工具)
- `ZeroLog` - 统一日志系统
- `ZeroLogChannel` - 可注入 sink、等级过滤和结构化 entry 的实例日志通道
- `DebugUtils` - 条件编译调试工具
- `DateUtils` - 时间戳/格式化工具
- `ZeroEase` - 缓动枚举 (DOTween 解耦)
- `DOTweenAdapter` - DOTween 反射适配器
- `UIUtils` - UI 工具 (淡入淡出等)

### Performance (性能)
- `ZeroGC` - 零 GC 统一入口 API
- `ListPool<T>` - List 对象池
- `DictionaryPool<K,V>` - Dictionary 对象池
- `StringBuilderPool` - StringBuilder 对象池
- `PooledList<T>` - IDisposable List 包装器
- `PooledDictionary<K,V>` - IDisposable Dictionary 包装器
- `FormattedValueCache` - 格式化值缓存

## 快速使用

### Singleton
```csharp
using ZeroEngine;

public class GameManager : Singleton<GameManager>
{
    protected override void OnSingletonAwake() { }
}

public class AudioManager : PersistentSingleton<AudioManager>
{
    // 跨场景持久化
}
```

### EventManager
```csharp
using ZeroEngine;

// 订阅
EventManager.Subscribe<int>("Score", OnScoreChanged);

// 触发
EventManager.Trigger("Score", 100);

// 取消订阅
EventManager.Unsubscribe<int>("Score", OnScoreChanged);
```

### PoolManager
```csharp
using ZeroEngine.Pool;

// 生成
var bullet = PoolManager.Instance.Spawn(bulletPrefab, pos, rot, PoolType.Projectiles);

// 回收
PoolManager.Instance.Despawn(bullet);
// 或
bullet.Despawn();
```

### ZeroGC
```csharp
using ZeroEngine.Performance;

// 自动归还 List
using var list = ZeroGC.GetList<int>();
list.Add(1);

// 格式化缓存
string hp = ZeroGC.CacheFormattedValue("hp", 100, v => $"{v} HP");
```

## 条件编译

| 宏 | 触发条件 | 影响 |
|----|----------|------|
| `UNITASK_ENABLED` | 安装 UniTask | PoolManager 异步预加载 |
| `ADDRESSABLES_ENABLED` | 安装 Addressables | PoolManager 异步生成 |
| `ENABLE_INPUT_SYSTEM` | 安装 Input System | UIUtils 输入检测 |

## 依赖

- 无外部依赖 (可选: UniTask, Addressables, Input System)

## 许可

ZeroGameStudio Internal Use
