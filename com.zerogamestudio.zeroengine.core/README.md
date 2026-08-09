# ZeroEngine.Core

ZeroEngine 框架的核心基础设施包。

## 版本
- **当前版本**: 2.0.0
- **最小 Unity**: 2022.3 LTS

## 包含模块

### Core (核心)
- `Singleton<T>` - 非持久单例基类
- `MonoSingleton<T>` - Singleton 别名
- `PersistentSingleton<T>` - 跨场景持久单例
- `EventManager` - 事件总线系统
- `GameEvents` - 预定义事件常量

### Pool (对象池)
- `PoolManager` - 对象池管理器 (PersistentSingleton)
- `IPoolable` - 可池化对象接口
- `PoolType` - 池分类枚举

### Utils (工具)
- `ZeroLog` - 统一日志系统
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
