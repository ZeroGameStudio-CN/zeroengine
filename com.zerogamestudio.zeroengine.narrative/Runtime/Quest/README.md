# ZeroEngine.Quest API 文档

> **用途**: 本文档面向AI助手，提供Quest模块的快速参考。
> **版本**: v1.2.0+
> **最后更新**: 2025-12-31

---

## 目录结构

```
Quest/
├── Data/
│   ├── QuestConfigSO.cs      # 任务配置
│   └── DialogueConfigSO.cs   # 对话配置
├── Conditions/               # v1.2.0+ 条件系统
│   ├── QuestCondition.cs     # 条件基类
│   ├── QuestEvents.cs        # 事件常量
│   ├── KillCondition.cs      # 击杀条件
│   ├── CollectCondition.cs   # 收集条件
│   ├── InteractCondition.cs  # 交互条件
│   └── ReachCondition.cs     # 到达条件
├── Rewards/                  # v1.2.0+ 奖励系统
│   ├── QuestReward.cs        # 奖励基类
│   ├── ExpReward.cs          # 经验奖励
│   ├── CurrencyReward.cs     # 货币奖励
│   └── ItemReward.cs         # 物品奖励
├── QuestManager.cs           # 任务管理器（单例）
├── QuestState.cs             # 任务状态
└── ...
```

---

## QuestConfigSO.cs

**用途**: 任务配置数据

```csharp
[CreateAssetMenu(menuName = "ZeroEngine/Quest/Quest Config")]
public class QuestConfigSO : ScriptableObject
{
    public string QuestId;
    public string QuestName;
    [TextArea] public string Description;

    // Legacy system (v1.0)
    public List<QuestObjective> Objectives;
    public List<QuestEventConfig> EventConfigs;

    // New condition system (v1.2.0+)
    [SerializeReference]
    public List<QuestCondition> Conditions;

    // New reward system (v1.2.0+)
    [SerializeReference]
    public List<QuestReward> Rewards;

    public string[] PrerequisiteQuestIds;
    public bool IsRepeatable;

    // 检测使用哪个系统
    public bool UsesNewConditionSystem => Conditions?.Count > 0;
    public bool UsesNewRewardSystem => Rewards?.Count > 0;
}
```

---

## 条件系统 (v1.2.0+)

### QuestCondition 基类

```csharp
[Serializable]
public abstract class QuestCondition
{
    public string Description;
    public bool IsHidden;

    public abstract string ConditionType { get; }
    public abstract bool IsSatisfied(QuestRuntimeData runtime);
    public abstract int GetCurrentProgress(QuestRuntimeData runtime);
    public abstract int GetTargetProgress();
    public abstract string GetProgressKey();
    public abstract bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData);
}
```

### 内置条件类型

| 条件类 | 用途 | 监听事件 |
|--------|------|----------|
| `KillCondition` | 击杀指定敌人 | `Quest.EntityKilled` |
| `CollectCondition` | 收集指定物品 | `Quest.ItemObtained` |
| `InteractCondition` | 与NPC/物体交互 | `Quest.Interacted` |
| `ReachCondition` | 到达指定位置 | `Quest.LocationReached` |

### QuestEvents 常量

```csharp
public static class QuestEvents
{
    // 条件事件
    public const string EntityKilled = "Quest.EntityKilled";
    public const string ItemObtained = "Quest.ItemObtained";
    public const string Interacted = "Quest.Interacted";
    public const string LocationReached = "Quest.LocationReached";

    // 状态事件
    public const string QuestAccepted = "Quest.Accepted";
    public const string QuestCompleted = "Quest.Completed";
    public const string QuestFailed = "Quest.Failed";
    public const string QuestAbandoned = "Quest.Abandoned";
    public const string ConditionProgress = "Quest.ConditionProgress";
    public const string ConditionCompleted = "Quest.ConditionCompleted";
}
```

### ConditionEventData

```csharp
public struct ConditionEventData
{
    public string TargetId;
    public int Amount;
    public string LocationId;
    public Vector3 Position;
    public object CustomData;
}
```

---

## 奖励系统 (v1.2.0+)

### QuestReward 基类

```csharp
[Serializable]
public abstract class QuestReward
{
    public string Description;

    public abstract string RewardType { get; }
    public abstract void Grant();
    public abstract string GetDisplayText();
}
```

### 内置奖励类型

| 奖励类 | 用途 | 字段 |
|--------|------|------|
| `ExpReward` | 经验值奖励 | `Amount`, `ExpType` |
| `CurrencyReward` | 货币奖励 | `Amount`, `CurrencyType` (Gold/Gem/Token) |
| `ItemReward` | 物品奖励 | `ItemId`, `Amount` |

---

## Legacy: QuestObjective

```csharp
[Serializable]
public class QuestObjective
{
    public string ObjectiveId;
    public ObjectiveType Type;      // Kill, Collect, Talk, etc.
    public string TargetId;         // 目标ID
    public int RequiredAmount;
    public string Description;
}

public enum ObjectiveType
{
    Kill,
    Collect,
    Talk,
    Visit,
    Custom
}
```

---

## QuestManager.cs

**用途**: 任务管理单例

```csharp
public class QuestManager : Singleton<QuestManager>
{
    // 事件 (v1.2.0+)
    event Action<string, int, int> OnConditionProgress;  // questId, conditionIndex, progress
    event Action<string, int> OnConditionCompleted;      // questId, conditionIndex

    // 接受/提交/放弃任务
    bool AcceptQuest(string questId);
    bool SubmitQuest(string questId);
    bool AbandonQuest(string questId);  // v1.2.0+

    // 进度更新 - Legacy
    void UpdateQuestProgress(ObjectiveType type, string targetId, int amount = 1);

    // 进度更新 - v1.2.0+ 条件系统
    void ProcessConditionEvent(string eventType, ConditionEventData data);

    // 查询
    QuestConfigSO GetConfig(string questId);
    bool HasActiveQuest(string questId);
    QuestState GetQuestState(string questId);
    int GetQuestCompletionCount(string questId);
    List<ActiveQuestData> GetActiveQuests();
    (int current, int target) GetConditionProgress(string questId, int conditionIndex);  // v1.2.0+

    // 存档
    QuestSaveData GetSaveData();
    void LoadSaveData(QuestSaveData data);
}
```

---

## QuestState

```csharp
public enum QuestState
{
    NotStarted,
    InProgress,
    Completed,
    Failed
}
```

---

## 自动事件监听

QuestManager自动监听以下事件来更新任务进度：

| 事件 | 对应目标类型 |
|------|------------|
| `GameEvents.ItemObtained` | `ObjectiveType.Collect` (Legacy) |
| `"Entity.Killed"` | `ObjectiveType.Kill` (Legacy) |
| `QuestEvents.EntityKilled` | `KillCondition` (v1.2.0+) |
| `QuestEvents.ItemObtained` | `CollectCondition` (v1.2.0+) |
| `QuestEvents.Interacted` | `InteractCondition` (v1.2.0+) |
| `QuestEvents.LocationReached` | `ReachCondition` (v1.2.0+) |

---

## 使用示例

### Legacy 系统

```csharp
// 接受任务
QuestManager.Instance.AcceptQuest("kill_10_slimes");

// 查询状态
var state = QuestManager.Instance.GetQuestState("kill_10_slimes");

// 手动更新进度（如对话）
QuestManager.Instance.UpdateQuestProgress(ObjectiveType.Talk, "npc_elder", 1);

// 提交任务
QuestManager.Instance.SubmitQuest("kill_10_slimes");
```

### v1.2.0+ 条件系统

```csharp
// 接受任务
QuestManager.Instance.AcceptQuest("hunt_boss");

// 触发条件事件（击杀）
QuestManager.Instance.ProcessConditionEvent(QuestEvents.EntityKilled, new ConditionEventData
{
    TargetId = "boss_dragon",
    Amount = 1
});

// 触发条件事件（收集）
QuestManager.Instance.ProcessConditionEvent(QuestEvents.ItemObtained, new ConditionEventData
{
    TargetId = "dragon_scale",
    Amount = 5
});

// 触发条件事件（到达位置）
QuestManager.Instance.ProcessConditionEvent(QuestEvents.LocationReached, new ConditionEventData
{
    LocationId = "dragon_lair",
    Position = transform.position
});

// 查询条件进度
var (current, target) = QuestManager.Instance.GetConditionProgress("hunt_boss", 0);
Debug.Log($"Progress: {current}/{target}");

// 监听条件事件
QuestManager.Instance.OnConditionProgress += (questId, idx, progress) =>
{
    Debug.Log($"Quest {questId} condition {idx}: {progress}");
};

// 放弃任务
QuestManager.Instance.AbandonQuest("hunt_boss");
```

### 自定义条件

```csharp
[Serializable]
public class CustomCondition : QuestCondition
{
    public string CustomTargetId;
    public int RequiredValue;

    public override string ConditionType => "Custom";

    public override bool IsSatisfied(QuestRuntimeData runtime)
    {
        return GetCurrentProgress(runtime) >= RequiredValue;
    }

    public override int GetCurrentProgress(QuestRuntimeData runtime)
    {
        runtime.Progress.TryGetValue(GetProgressKey(), out int progress);
        return progress;
    }

    public override int GetTargetProgress() => RequiredValue;

    public override string GetProgressKey() => $"custom_{CustomTargetId}";

    public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
    {
        if (eventType != "Custom.Event") return false;
        if (eventData is ConditionEventData data && data.TargetId == CustomTargetId)
        {
            string key = GetProgressKey();
            runtime.Progress.TryGetValue(key, out int current);
            runtime.Progress[key] = current + data.Amount;
            return true;
        }
        return false;
    }
}
```

### 自定义奖励

```csharp
[Serializable]
public class SkillPointReward : QuestReward
{
    public int Amount = 1;

    public override string RewardType => "SkillPoint";

    public override void Grant()
    {
        // 调用游戏系统授予技能点
        PlayerManager.Instance.AddSkillPoints(Amount);
    }

    public override string GetDisplayText()
    {
        return $"技能点 x{Amount}";
    }
}
```
