# ZeroEngine.AutoBattle

无引擎依赖的自动战斗内核，为回合制战术游戏提供整数格遍历与确定性决策选择。

## 公共能力

- `TacticalGridPosition`：整数坐标和值语义。
- `TacticalGrid`：阻挡/占用状态、固定顺序的 BFS 可达范围和 row-major 攻击范围。
- `TacticalDecisionPlanner`：按 stable actor order 枚举、过滤和排序动作，并在无合法攻击时执行移动评分回退。
- `TacticalGridTraversalScratch`、`TacticalDecisionScratch<TActor, TPayload>`：由调用方持有并可复用的 scratch buffer。

planner 只处理调用方提供的值类型快照与策略，不读取 Unity scene、ScriptableObject、全局随机状态或项目专属战斗类型。

## 依赖

此包没有 ZeroEngine 或 Unity 运行时依赖，runtime assembly 设置为 `noEngineReferences=true`。

## 示例

```csharp
var grid = new TacticalGrid(12, 12);
var gridScratch = new TacticalGridTraversalScratch();
var reachable = new List<TacticalGridPosition>();
grid.CollectReachable(new TacticalGridPosition(2, 2), 3, reachable, gridScratch);

var plannerScratch = new TacticalDecisionScratch<MyActor, MyPayload>();
var result = TacticalDecisionPlanner.Decide(
    grid,
    actor,
    actors,
    moveBudget: 3,
    policy,
    plannerScratch);
```

## 版本历史

### 2.0.0

- 删除依赖 Unity/ZeroEngine 的 v1 自动战斗、单位、技能、阵型和 AI 配置原型。
- 新增 headless tactical grid 与确定性 decision planner 公共合同。
