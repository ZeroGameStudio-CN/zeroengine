# ZeroEngine.Dungeon

地下城探索框架，为 Roguelike 类游戏提供节点地图和自动探索系统。

## 功能特性

- **节点地图** (`Map/`)
  - `NodeMap` - 节点地图管理
  - `MapNode` - 地图节点（战斗、商店、事件等）
  - `NodeMapGenerator` - 程序化地图生成

- **探索系统** (`Exploration/`)
  - `ExplorationAI` - 自动探索AI，支持多种策略

- **事件系统** (`Events/`)
  - `EventNodeBase` - 事件节点基类
  - `EventOption` - 事件选项
  - `EventResult` - 事件结果

## 依赖

- `com.zerogamestudio.zeroengine.core` >= 2.0.0
- `com.zerogamestudio.zeroengine.data` >= 2.0.0

## 快速开始

```csharp
// 创建地图生成器
var config = new NodeMapGeneratorConfig
{
    MapDepth = 8,
    PathCount = 3
};
var generator = new NodeMapGenerator(config);

// 生成地图
var map = generator.Generate("dungeon_1", floor: 1);

// 创建探索AI
var ai = new ExplorationAI();
ai.SetStrategy(ExplorationStrategy.Balanced);

// 选择下一个节点
var nextNode = ai.SelectNextNode(map, currentHealthPercent: 0.8f);
map.MoveToNode(nextNode);
```

## 节点类型

| 类型 | 描述 |
|------|------|
| `Start` | 起始节点 |
| `Battle` | 普通战斗 |
| `Elite` | 精英战斗 |
| `Boss` | BOSS战斗 |
| `Shop` | 商店 |
| `Rest` | 休息点 |
| `Event` | 随机事件 |
| `Treasure` | 宝箱 |
| `PlayerTeam` | 玩家队伍（异步PvP） |

## 探索策略

| 策略 | 描述 |
|------|------|
| `Balanced` | 平衡型，综合评估 |
| `Aggressive` | 激进型，优先战斗 |
| `Conservative` | 稳健型，优先休息/商店 |
| `Explorer` | 探索型，优先事件 |

## 版本历史

### 1.0.0
- 初始版本
- 节点地图系统
- 程序化地图生成
- 探索AI
- 事件框架

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
