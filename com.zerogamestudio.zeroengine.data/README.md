# ZeroEngine.Data

数值系统核心包，包含属性系统和 Buff 系统。

## 版本
- **当前版本**: 2.1.0
- **依赖**: ZeroEngine.Core

## 包含模块

### StatSystem (属性系统)
- `Stat` - 属性类 (基础值、修正器、约束)
- `StatModifier` - 属性修正器 (Flat/PercentAdd/PercentMult)
- `StatController` - 属性控制器组件
- `StatConfigSO` - 属性配置 ScriptableObject

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
