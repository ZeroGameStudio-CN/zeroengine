# Tutorial System (v1.14.0+)

教程引导系统，提供新手教程、系统引导和步骤提示功能。

## 快速开始

### 1. 创建教程序列

在 Project 窗口右键 > Create > ZeroEngine > Tutorial > Tutorial Sequence：

```yaml
Tutorial Sequence SO:
  Sequence Id: "onboarding_001"
  Display Name: "新手引导"
  Category: Onboarding
  Auto Start: true
  Skippable: true
  Steps:
    - Dialogue Step:
        Speaker Name: "向导"
        Dialogue Text: "欢迎来到游戏世界！"
        Wait For Confirm: true
    - Highlight Step:
        Target Path: "MainMenu/PlayButton"
        Highlight Type: Circle
        Hint Text: "点击这里开始游戏"
        Wait For Click: true
```

### 2. 注册并启动

```csharp
// 设置教程上下文中的玩家对象
TutorialManager.Instance.Player = playerGameObject;

// 在 Inspector 中注册，或代码注册
TutorialManager.Instance.RegisterSequence(myTutorialSO);

// 手动启动
TutorialManager.Instance.StartSequence("onboarding_001");

// 或让系统自动检测启动
TutorialManager.Instance.CheckAutoStart();
```

### 3. 监听事件

```csharp
TutorialManager.Instance.OnSequenceStarted += seq =>
    Debug.Log($"Tutorial started: {seq.DisplayName}");

TutorialManager.Instance.OnSequenceCompleted += seq =>
    Debug.Log($"Tutorial completed: {seq.SequenceId}");

TutorialManager.Instance.OnStepStarted += (step, index) =>
    Debug.Log($"Step {index}: {step.StepType}");
```

## 核心 API

### TutorialManager

教程系统核心管理器，使用 `MonoSingleton<T>` + `ISaveable`。

```csharp
// 状态
bool isRunning = TutorialManager.Instance.IsRunning;
var currentSeq = TutorialManager.Instance.CurrentSequence;
var currentStep = TutorialManager.Instance.CurrentStep;
int stepIndex = TutorialManager.Instance.CurrentStepIndex;

// 控制
TutorialManager.Instance.StartSequence("tutorial_001");
TutorialManager.Instance.SkipCurrentStep();
TutorialManager.Instance.SkipCurrentSequence();
TutorialManager.Instance.CompleteCurrentStep();

// 查询
bool completed = TutorialManager.Instance.IsSequenceCompleted("tutorial_001");
bool skipped = TutorialManager.Instance.IsSequenceSkipped("tutorial_001");
bool any = TutorialManager.Instance.HasCompletedAny("t1", "t2", "t3");

// 变量
TutorialManager.Instance.SetGlobalVariable("gold", 100);
int gold = TutorialManager.Instance.GetGlobalVariable<int>("gold");

// 标记 (用于 FirstTimeCondition)
TutorialManager.Instance.TriggerMarker("first_shop_visit");
bool triggered = TutorialManager.Instance.HasTriggered("first_shop_visit");

// 重置
TutorialManager.Instance.ResetProgress();
TutorialManager.Instance.ResetProgress("tutorial_001");
```

### TutorialSequenceSO

教程序列 ScriptableObject：

```csharp
[CreateAssetMenu(menuName = "ZeroEngine/Tutorial/Tutorial Sequence")]
public class TutorialSequenceSO : ScriptableObject
{
    public string SequenceId;
    public string DisplayName;
    public TutorialCategory Category;
    public bool AutoStart = true;
    public bool Skippable = true;
    public bool Replayable = false;
    public int Priority = 0;

    [SerializeReference]
    public List<TutorialCondition> StartConditions;

    [SerializeReference]
    public List<TutorialStep> Steps;

    [SerializeReference]
    public List<TutorialReward> CompletionRewards;

    public string[] Prerequisites;
    public string NextSequenceId;
}
```

### TutorialContext

教程执行上下文：

```csharp
// 在步骤中访问
public override void OnEnter(TutorialContext ctx)
{
    // 当前信息
    var sequence = ctx.CurrentSequence;
    var step = ctx.CurrentStep;
    float progress = ctx.GetProgress();

    // 变量
    ctx.SetVariable("visited", true);
    bool visited = ctx.GetVariable<bool>("visited");

    // UI 目标。路径相对 TutorialUIManager 的 UI Container，也可以先 RegisterTarget。
    ctx.RegisterTarget("play_button", playButton.gameObject);
    var button = ctx.FindUITarget("play_button");

    // 玩家引用
    var player = ctx.Player;
}
```

## 步骤类型

### DialogueStep (对话)

```csharp
[Serializable]
public class DialogueStep : TutorialStep
{
    public string SpeakerName;
    public string DialogueText;
    public Sprite SpeakerIcon;
    public DialoguePosition Position;
    public float TypewriterSpeed = 30f;
    public bool WaitForConfirm = true;
    public KeyCode ConfirmKey = KeyCode.Space;
}
```

### HighlightStep (高亮)

```csharp
[Serializable]
public class HighlightStep : TutorialStep
{
    public string TargetPath;
    public HighlightType HighlightType;
    public string HintText;
    public Vector2 HintOffset;
    public bool WaitForClick = true;
    public float Timeout = 0f;
}
```

### WaitInputStep (等待输入) - v1.14.0+

```csharp
[Serializable]
public class WaitInputStep : TutorialStep
{
    public KeyCode[] RequiredKeys;
    public bool RequireAll = false;
}
```

### WaitInteractionStep (等待交互) - v1.14.0+

```csharp
[Serializable]
public class WaitInteractionStep : TutorialStep
{
    public string InteractableId;
    public InteractionType RequiredType;
}
```

### WaitEventStep (等待事件) - v1.14.0+

```csharp
[Serializable]
public class WaitEventStep : TutorialStep
{
    public string EventKey;
    public string ExpectedValue;
}
```

### MoveToStep (移动引导) - v1.14.0+

```csharp
[Serializable]
public class MoveToStep : TutorialStep
{
    public Vector3 TargetPosition;
    public string TargetObjectPath; // TutorialContext.RegisterTarget ID
    public float ArrivalDistance = 2f;
    public bool ShowArrow = true;
}
```

### DelayStep (延时)

```csharp
[Serializable]
public class DelayStep : TutorialStep
{
    public float Duration = 1f;
}
```

### CallbackStep (回调)

```csharp
[Serializable]
public class CallbackStep : TutorialStep
{
    public string CallbackId;
    public bool WaitForComplete = false;
}
```

## 条件类型

### 内置条件

```csharp
// 首次触发
new FirstTimeCondition { Key = "first_login" }

// 等级要求
new LevelCondition { MinLevel = 5, MaxLevel = 10 }

// 教程完成
new TutorialCompletedCondition
{
    RequiredTutorialIds = new[] { "basic_001", "basic_002" },
    RequireAll = true
}

// 场景条件
new SceneCondition
{
    AllowedScenes = new[] { "MainMenu", "Tutorial" }
}

// 任务状态
new QuestCondition
{
    QuestId = "quest_001",
    RequiredState = QuestRequiredState.Completed
}

// 变量条件
new VariableCondition
{
    VariableKey = "gold",
    Operator = ComparisonOperator.GreaterOrEqual,
    CompareValue = "100"
}
```

### 自定义条件

```csharp
[Serializable]
public class CustomCondition : TutorialCondition
{
    public string CustomValue;

    public override string ConditionType => "Custom";

    public override bool IsSatisfied(TutorialContext ctx)
    {
        // 自定义检查逻辑
        return true;
    }
}
```

## 奖励类型

```csharp
// 物品奖励
new ItemTutorialReward { ItemId = "sword_001", Amount = 1 }

// 成就奖励
new AchievementTutorialReward { AchievementId = "first_tutorial" }

// 自定义奖励
[Serializable]
public class CurrencyReward : TutorialReward
{
    public string CurrencyType;
    public int Amount;

    public override string RewardType => "Currency";

    public override void Grant()
    {
        // 发放货币
    }
}
```

## UI 系统

教程 UI 使用 `UI.Core` 框架，继承 `UIViewBase`：

```csharp
// TutorialUIManager 自动管理 UI 组件
TutorialUIManager.Instance.ShowDialogue(dialogueStep, onComplete, onConfirm);
TutorialUIManager.Instance.HideDialogue();

TutorialUIManager.Instance.ShowHighlight(target, type, hint, offset, onClick);
TutorialUIManager.Instance.HideHighlight();

TutorialUIManager.Instance.ShowProgress(current, total);
TutorialUIManager.Instance.HideProgress();
```

## 高亮类型

```csharp
public enum HighlightType
{
    Circle,         // 圆形高亮
    Rectangle,      // 矩形高亮
    Finger,         // 手指指示
    Arrow,          // 箭头指向
    Pulse,          // 脉冲效果
    None            // 无高亮
}
```

## 事件

```csharp
// TutorialManager 事件
OnSequenceStarted       // 教程开始
OnSequenceCompleted     // 教程完成
OnSequenceSkipped       // 教程跳过
OnStepStarted           // 步骤开始
OnStepCompleted         // 步骤完成
OnStepSkipped           // 步骤跳过
```

## 存档集成

`TutorialManager` 实现 `ISaveable` 接口，自动保存：
- 已完成的教程列表
- 已跳过的教程列表
- 当前进行中的教程
- 全局变量

```csharp
// 自动集成 SaveSlotManager
// 无需额外代码
```

## 配置

使用 `TutorialConfigSO` 配置全局参数：

```yaml
Tutorial Config SO:
  Enable Tutorials: true
  Auto Start For New Players: true
  Allow Skipping: true

  # UI 设置
  Dialogue UI Prefab: ...
  Highlight Mask Prefab: ...
  UI Fade In Duration: 0.3

  # 对话设置
  Default Typewriter Speed: 30
  Dialogue Confirm Key: Space
  Skip Key: Escape

  # 高亮设置
  Highlight Color: Yellow
  Mask Color: Black (70% alpha)
```

## 性能优化

1. **目标缓存**: UI 目标查找结果缓存
2. **延迟加载**: UI 预制体按需实例化
3. **对象池**: 高亮 UI 使用对象池
4. **GC 优化**: 使用 `readonly struct` 传递事件

## 最佳实践

1. 使用 `Priority` 控制多个可用教程的触发顺序
2. 使用 `Prerequisites` 确保教程按正确顺序执行
3. 使用 `MutuallyExclusive` 处理分支教程
4. 为关键步骤设置 `Timeout` 避免玩家卡住
5. 使用 `CallbackStep` 处理复杂的游戏逻辑
