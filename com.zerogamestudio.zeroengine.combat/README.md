# ZeroEngine.Combat

战斗系统框架包。

## 版本
- **整合候选版本**: 2.1.0（尚未发布）
- **依赖**: ZeroEngine.Core, ZeroEngine.Data, ZeroEngine.Economy，以及实际使用的 Physics、Audio、Animation、Particle System 内置模块。

保留 P6 已采用的 `ZeroEngine.Combat.Effects` 效果参数合同；本包定义参数，不依赖项目专属演出或补间插件。

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
