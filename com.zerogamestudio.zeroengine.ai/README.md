# ZeroEngine.AI

AI 决策系统包，包含 FSM、行为树、效用 AI、GOAP 和 NPC 日程系统。

## 版本
- **当前版本**: 2.0.1
- **依赖**: ZeroEngine.Core
- **可选依赖**: crashkonijn/GOAP

## 包含模块

### FSM (有限状态机)
- `StateMachine` - 状态机
- `IState` - 状态接口
- `StateBase` - 状态基类

### BehaviorTree (行为树)
- `BehaviorTree` - 行为树
- `BTNode` - 节点基类
- `Sequence/Selector/Parallel` - 组合节点
- `Repeater/Inverter/Conditional` - 装饰节点
- `ActionNode/WaitNode` - 叶节点
- `Blackboard` - 黑板数据

### AI.Common (通用)
- `IAIBrain` - AI 大脑接口
- `AIContext` - AI 上下文
- `AIBlackboard` - AI 黑板
- `AIAgent` - AI 代理组件

### AI.UtilityAI (效用 AI)
- `UtilityBrain` - 效用 AI 大脑
- `UtilityAction` - 效用行动
- `Consideration` - 考量因素
- `ResponseCurve` - 响应曲线

### AI.GOAP (GOAP 桥接)
- `GOAPBridge` - GOAP 适配器
- `ZeroGOAPAgent` - GOAP Agent 包装

### AI.NPCSchedule (NPC 日程)
- `NPCScheduleController` - 日程控制器
- `ScheduleEntry` - 日程条目
- `ScheduleAction` - 日程行动

## 快速使用

### FSM
```csharp
using ZeroEngine.FSM;

var fsm = new StateMachine();
fsm.AddState("Idle", new IdleState());
fsm.AddState("Chase", new ChaseState());
fsm.SetState("Idle");
fsm.Update();
```

### BehaviorTree
```csharp
using ZeroEngine.BehaviorTree;

var tree = new BehaviorTree();
tree.Root = new Sequence(
    new CheckEnemyInRange(),
    new AttackEnemy()
);
tree.Tick();
```

### UtilityAI
```csharp
using ZeroEngine.AI.UtilityAI;

var brain = GetComponent<UtilityBrain>();
brain.AddAction(new IdleAction());
brain.AddAction(new PatrolAction());
brain.AddAction(new AttackAction { Priority = 0.8f });
```

### NPCSchedule
```csharp
using ZeroEngine.AI.NPCSchedule;

var controller = GetComponent<NPCScheduleController>();
controller.OnScheduleChanged += entry =>
    Debug.Log($"Now: {entry.Action.Name}");
```

消费项目通过 AI 黑板的 `BlackboardKeys.CurrentHour`、`CurrentDayOfWeek` 和
`CurrentSeason` 提供游戏时间。AI 包不直接依赖 World 包。

## 条件编译

| 宏 | 说明 |
|----|------|
| `GOAP_INSTALLED` | 启用 crashkonijn/GOAP 集成 |
