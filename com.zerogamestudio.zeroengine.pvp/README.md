# ZeroEngine.PvP

异步PvP框架，支持队伍快照、匹配和排名系统。

## 功能特性

- **快照系统** (`Snapshot/`)
  - `TeamSnapshot` - 队伍快照，包含单位、阵型、AI配置
  - `UnitSnapshot` - 单位快照
  - `FormationSnapshot` - 阵型快照

- **匹配系统** (`Matching/`)
  - `MatchingService` - 匹配服务，按战力匹配对手

- **排名系统** (`Ranking/`)
  - `RankingData` - 排名数据，包含段位、积分、胜率
  - `SeasonConfig` - 赛季配置

## 依赖

- `com.zerogamestudio.zeroengine.core` >= 2.0.0

## 快速开始

```csharp
// 创建队伍快照
var snapshot = new TeamSnapshot
{
    PlayerId = "player_001",
    PlayerName = "玩家A",
    TotalPower = 10000
};

// 添加单位快照
snapshot.Units.Add(new UnitSnapshot
{
    UnitId = "unit_001",
    TemplateId = "warrior",
    Level = 50,
    MaxHealth = 1000,
    Attack = 150
});

// 匹配对手
var matchingService = new MatchingService();
matchingService.AddCandidate(snapshot);
var opponent = matchingService.FindOpponent(playerPower: 9500);

// 排名系统
var rankingData = new RankingData
{
    PlayerId = "player_001",
    Score = 1500
};
rankingData.RecordAttack(won: true, scoreChange: 30);
```

## 匹配规则

| 规则 | 描述 |
|------|------|
| 战力匹配 | 匹配相近战力的队伍 (±10%) |
| 层级匹配 | 地下城层级越高，匹配范围越宽 |
| 随机因子 | 小概率遇到高/低战力队伍 |

## 段位系统

| 段位 | 积分范围 |
|------|----------|
| Bronze | 0 - 999 |
| Silver | 1000 - 1999 |
| Gold | 2000 - 2999 |
| Platinum | 3000 - 3999 |
| Diamond | 4000 - 4999 |
| Master | 5000+ |

## 版本历史

### 1.0.1
- 删除未使用的 AutoBattle 假依赖。

### 1.0.0
- 初始版本
- 队伍快照系统
- 匹配服务
- 排名系统
