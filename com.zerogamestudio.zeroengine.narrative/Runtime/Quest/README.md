# ZeroEngine Quest

ZeroEngine Quest is a condition-driven quest runtime. Quest data is authored in
`QuestConfigSO`, registered through `QuestProvider` or `QuestManager`, and
advanced by condition events.

The package does not depend on a project-specific localization framework.
Projects should store stable quest text keys or fallback text in their own data
and resolve those keys in the project layer.

## Data Model

`QuestConfigSO` contains:

- Basic identity: `questId`, `questName`, `description`
- Progress rules: `Conditions`
- Completion rewards: `Rewards`
- Lifecycle controls: `autoSubmit`, `repetitionLimit`, `lifecycle`
- Optional NPC/dialogue references: `providerNpcId`, `submitNpcId`,
  `completionDialogue`

All quest progress is stored in `QuestRuntimeData.Progress`. Condition classes
use `QuestRuntimeData.AddProgress()` and `QuestRuntimeData.GetProgress()` as the
stable API for condition progress.

## Conditions

All conditions inherit from `QuestCondition`.

| Condition | Event | Key fields |
| --- | --- | --- |
| `KillCondition` | `Quest.EntityKilled` | `TargetId`, `RequiredCount` |
| `CollectCondition` | `Quest.ItemObtained` | `ItemId`, `RequiredCount`, `ConsumeOnComplete` |
| `InteractCondition` | `Quest.Interacted` | `TargetId`, `RequiredCount`, `InteractionType` |
| `ReachCondition` | `Quest.LocationReached` | `LocationId`, `TargetPosition`, `TriggerRadius` |
| `SurviveCondition` | `Quest.SurviveCompleted` | `StageId`, `RequiredCount` |
| `CustomCondition` | custom event string | `EventType`, `TargetId`, `RequiredCount` |

Multiple conditions are combined with AND semantics. A quest becomes successful
only when every visible runtime condition reports completion.

## Rewards

All rewards inherit from `QuestReward`.

| Reward | Key fields |
| --- | --- |
| `ExpReward` | `Amount`, `ExpType` |
| `CurrencyReward` | `Amount`, `CurrencyType` |
| `ItemReward` | `ItemId`, `Amount` |

Rewards are granted by normal quest submission. If `autoSubmit` is enabled,
`QuestManager` submits the quest immediately after all conditions complete.

## Runtime API

Core entry points:

```csharp
QuestManager.Instance.AcceptQuest(questId);
QuestManager.Instance.ProcessConditionEvent(eventType, data);
QuestManager.Instance.SubmitQuest(questId);
QuestManager.Instance.AbandonQuest(questId);
```

Common queries:

```csharp
QuestManager.Instance.GetConfig(questId);
QuestManager.Instance.GetRuntimeData(questId);
QuestManager.Instance.HasActiveQuest(questId);
QuestManager.Instance.GetQuestState(questId);
```

Useful events:

```csharp
QuestManager.OnQuestAccepted
QuestManager.OnQuestCompleted
QuestManager.OnQuestSubmitted
QuestManager.OnQuestAbandoned
QuestManager.OnConditionProgress
QuestManager.OnConditionCompleted
```

## Trigger Components

The package includes trigger actions and conditions that can be used from
ZeroEngine Trigger graphs:

- `AcceptQuestAction` accepts a quest by id.
- `CompleteObjectiveAction` sends a condition event through
  `QuestManager.ProcessConditionEvent`.
- `HasQuestCondition` checks whether a quest is active.
- `QuestStateCondition` checks the current quest state.

Project layers may wrap these components with project-specific validation,
localization, UI refresh, or save behavior.

## Event Data

Condition events use `ConditionEventData`:

```csharp
var data = new ConditionEventData(targetId, amount)
{
    Position = worldPosition
};

QuestManager.Instance.ProcessConditionEvent(QuestEvents.EntityKilled, data);
```

Use constants from `QuestEvents` for built-in event names:

```csharp
QuestEvents.EntityKilled
QuestEvents.ItemObtained
QuestEvents.Interacted
QuestEvents.LocationReached
QuestEvents.SurviveCompleted
QuestEvents.QuestSubmitted
QuestEvents.QuestAbandoned
```

## Authoring Notes

- Keep `questId` stable after shipping.
- Prefer stable localization keys in the project layer, with fallback text for
  development builds.
- Use `Persistent` for cross-run quests and `PerRun` for run-scoped quests.
- Use `repetitionLimit = 1` for one-time quests and `0` for unlimited repeats.
- Register quest configs before accepting or restoring quest state.
