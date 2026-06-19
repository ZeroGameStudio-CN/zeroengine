# ZeroEngine.Combat

战斗系统框架包。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, ZeroEngine.Data, ZeroEngine.Economy

## 包含模块

### Combat (战斗核心)
- `CombatManager` - 战斗管理器
- `ICombatant` - 战斗单位接口
- `DamageType` - 伤害类型
- `DamageCalculator` - 伤害计算器
- `TargetSelector` - 目标选择器
- `HealthComponent` - 生命值组件

### AbilitySystem (技能系统)
- `AbilityManager` - 技能管理器
- `AbilityDataSO` - 技能数据
- TCE 模式 (Trigger-Condition-Effect)

### Projectile (弹道系统)
- `ProjectileBase` - 弹道基类
- `ProjectilePool` - 弹道对象池

### Spawner (生成器)
- `SpawnerBase` - 生成器基类
- `WaveSpawner` - 波次生成器

## 配置校验

`ZeroEngine.Combat.Editor` 提供 `CombatConfigValidator`，用于在 Editor 测试或配置发布流程中检查：

- `AbilityDataSO` 的技能名重复、空描述、非法冷却/等级、空 Trigger/Condition/Effect 和无效 TCE 参数。
- `ProjectileDataSO` 的弹道 ID、显示名、Prefab、速度、生命周期、轨迹参数、暴击率、AOE、对象池参数。
- `SpawnDataSO` 的生成 ID、显示名、间隔、随机范围、条目 Prefab、权重、数量、缩放和重复 ID。

## 快速使用

```csharp
using ZeroEngine.Combat;

// 造成伤害
var damage = DamageData.Physical(50f, attacker);
target.TakeDamage(damage);

// 通过管理器
CombatManager.Instance.DealDamage(damage, target);

// 目标选择
var selector = new TargetSelector(config);
var target = selector.SelectTarget(candidates, origin);
```

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
