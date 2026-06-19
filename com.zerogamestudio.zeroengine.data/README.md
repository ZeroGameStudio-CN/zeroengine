# ZeroEngine.Data

数值系统核心包，包含属性系统和 Buff 系统。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, ZeroEngine.Persistence

## Designer Config Validation

`ZeroEngine.Data.Editor` provides `DataConfigValidator` for buff data and math
formulas. It reports duplicate or missing Buff IDs, invalid buff timing/stack
settings, empty stat modifiers, and formula steps that divide by constant zero.

## 包含模块

### StatSystem (属性系统)
- `Stat` - 属性类 (基础值、修正器、约束)
- `StatModifier` - 属性修正器 (Flat/PercentAdd/PercentMult)
- `StatController` - 属性控制器组件
- `StatConfigSO` - 属性配置 ScriptableObject

### BuffSystem (Buff 系统)
- `Buff` - Buff 基类 (持续时间、层数、效果)
- `BuffController` - Buff 控制器组件
- `BuffDataSO` - Buff 数据 ScriptableObject
- `StatModifierBuff` - 属性修正 Buff

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

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
