# ZeroEngine.AutoBattle

自动战斗框架，为战棋/策略类游戏提供格子战斗系统。

## 功能特性

- **格子系统** (`Grid/`)
  - `GridBoard` - 棋盘管理，支持任意尺寸
  - `GridCell` - 单个格子，支持地形效果
  - `IBattleUnit` - 战斗单位接口

- **战斗系统** (`Battle/`)
  - `AutoBattleManager` - 自动战斗流程控制
  - `BattleUnitBase` - 战斗单位基类

- **技能系统** (`Skill/`)
  - `SkillData` - 技能数据基类
  - `SkillSlotManager` - 技能槽位管理

- **AI系统** (`AI/`)
  - `UnitAIConfig` - 单位AI配置（目标选择、技能策略等）

- **阵型系统** (`Formation/`)
  - `FormationData` - 阵型数据
  - `FormationSlot` - 阵型槽位

## 依赖

- `com.zerogamestudio.zeroengine.core` >= 2.0.0
- `com.zerogamestudio.zeroengine.combat` >= 2.0.0
- `com.zerogamestudio.zeroengine.data` >= 2.0.0
- `com.zerogamestudio.zeroengine.ai` >= 2.0.0

## 快速开始

```csharp
// 创建战斗管理器（5x5 棋盘）
var battleManager = new AutoBattleManager(5, 5);

// 进入准备阶段
battleManager.EnterPreparation();

// 添加单位
battleManager.AddPlayerUnit(playerUnit, new Vector2Int(0, 2));
battleManager.AddEnemyUnit(enemyUnit, new Vector2Int(4, 2));

// 开始战斗
battleManager.StartBattle();

// 每帧更新
void Update()
{
    battleManager.Tick(Time.deltaTime);
}
```

## 版本历史

### 1.0.0
- 初始版本
- 格子棋盘系统
- 自动战斗流程
- 技能槽位系统
- AI配置系统
- 阵型系统

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
