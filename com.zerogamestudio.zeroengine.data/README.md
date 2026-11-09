# ZeroEngine.Data

数值系统核心包，包含属性系统和 Buff 系统。

## 版本
- **当前版本**: 2.2.0（源码候选；发布与采用以明确 Git pin 为准）
- **依赖**: ZeroEngine.Core、ZeroEngine.Formula；两个纯内核程序集可分别引用。

## 包含模块

### StatSystem (属性系统)
- `ZeroEngine.StatSystem.Core` - 无 Unity/Formula 依赖的叶子程序集：显式顺序/分组数值策略、重复输入视图、稳定批量删除、来源清理与计算版本。
- `Stat` - 属性类 (基础值、修正器、约束)
- `StatModifier` - 属性修正器 (Flat/PercentAdd/PercentMult)
- `StatController` - 属性控制器组件
- `StatConfigSO` - 属性配置 ScriptableObject

### Stat 内核与旧接口兼容

直接消费内核时引用 `ZeroEngine.StatSystem.Core`，实现 `IStatModifierAdapter<TModifier,TSource>`，以自己的枚举/字典定义属性键。来源 token、修正对象及宿主存储不迁移。`EvaluateOrdered` 按 float32 顺序计算连续 PercentAdd 分组，乘算使用 `1 + value`；`EvaluateGrouped` 保持旧 Data 的分组求和、原值乘算、Clamp 后 Round(4)。调用方明确选择，旧 `ZeroEngine.StatSystem.Stat` 默认仍走后者。

`RepeatedModifiers<T>` 只压缩临时输入，宿主存储保留逻辑条目和原排序。`RemoveBatch` 对重复引用/互斥类型顺序组做一次稳定压缩；同组不同引用可能近似重叠时回退原贪心匹配，并返回 `usedGreedyFallback`。匹配器必须保证匹配仅发生在同一 `(TypeId, Order)` 分区内。无匹配不写回列表，不破坏已有读取器。跳过排序只适用于已排序且同 Order 为同一引用的列表，其余沿用原 `List.Sort`。

`StatCalculationVersion` 记录每个属性的输入版本与真实计算次数，不扫描修正，也不缓存公开可变输入的指纹。旧接口保留字段/集合；经公开字段、保留的可变列表或修正对象直接写入后，宿主必须调用 `NotifyModifiersChanged()`，不要依赖集合 getter 自动追踪。基础值初始化、正常增删和来源清理会维护失效。`StatCurrentValue` 只提供比例 Ceil/旧差值策略的算术；事件顺序及非有限值诊断由宿主保留。

内核不执行 Formula、伤害、随机数、事件或存档。公式快照/动态评估由 adapter 的 `Value` 决定；旧 Data 仍在脏值计算时调用 `GetValue`。POB 的兼容策略不成为其他消费者的默认值。

### BuffSystem (Buff 系统)
- `BuffData` / `BuffHandler` / `BuffReceiver` - 保持现有聚合 Buff 接口和执行顺序。
- `ZeroEngine.BuffSystem.Core` - 同包内不引用 Unity 或项目的叶子程序集，提供来源叠层、来源生命周期子集、独立来源组、状态快照、显式时钟计算和激活世代。
- 项目适配只直接引用该叶子即可；Core、伤害、技能、渲染、存档格式和玩家身份留在项目。状态快照只复制数据，不解析资产或执行技能。

### Buff 运行内核接入

```csharp
using ZeroEngine.BuffSystem.Runtime;

var state = new BuffStackState();
state.AddStacks("equipment", 2, maximum: 5, sourceLifetime: true);
var snapshot = state.Capture(); // 独立复制，不借用运行时字典
state.RemoveStacks("equipment", 1);
state.Restore(snapshot.SourceStacks, snapshot.SourceLifetimeStacks);
```

宿主通过 `IBuffStackObserver` 接收逐层前置/后置和减层回调，按自己的既有顺序处理表现、属性和能力。回调允许同步清理；每次新增前及前置回调后重新核对宿主存活。达到上限的重新施加、批量后的属性应用和能力安装属于宿主策略。

未提供 observer 时，加层按来源提交一次 delta，减层按来源压缩；提供 observer 时继续逐层回调和读取重入状态。公开来源字典保持可变，TotalStacks 始终读取真实字典；不引入隐式总层数缓存。来源期子集、旧 signed 输入、独立快照及原时钟语义保持。

持续时间由宿主选择 owner/global delta；绝对 Tick 锚点与累积 delta Tick 都保持各自旧语义，不自动补跳。现有 ZE 聚合接口仍先到期再 Tick，POB 适配仍先 Tick 再到期。旧定义、API、字段和资产 GUID 不迁移。

通过官方组织仓库的 Git UPM URL 锁定已验证提交；临时 `file:` 仅用于本地验证。包测试需要在验证工程的 `testables` 中包含 `com.zerogamestudio.zeroengine.data`，运行 `ZeroEngine.Data.Tests`；普通消费无需打开测试。

## 快速使用

### StatSystem
```csharp
using ZeroEngine.StatSystem;

// 初始化属性
statController.InitStat("hp", 100, min: 0, max: 999);
statController.InitStat("atk", 10);

// 添加修正器
var modifier = new StatModifier(5, ModifierType.Flat, source: weapon);
statController.GetStat("atk").AddModifier(modifier);

// 监听变化
statController.OnAnyStatChanged += (statId, oldVal, newVal) =>
    Debug.Log($"{statId}: {oldVal} -> {newVal}");
```

### BuffSystem
```csharp
using ZeroEngine.BuffSystem;

// 应用 Buff
buffController.ApplyBuff(poisonBuffData, stacks: 1);

// 检查 Buff
if (buffController.HasBuff("poison"))
{
    var buff = buffController.GetBuff("poison");
    Debug.Log($"Poison stacks: {buff.StackCount}");
}

// 移除 Buff
buffController.RemoveBuff("poison");
```

## 存档集成

```csharp
// 导出
var saveData = statController.ExportSaveData();

// 导入
statController.ImportSaveData(saveData);
```
