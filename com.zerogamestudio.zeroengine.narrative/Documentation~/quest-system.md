# ZeroEngine Quest System

## Package Responsibility

ZeroEngine.Narrative owns the reusable quest core:

- `QuestConfigSO`
- `QuestManager`
- `QuestRuntimeData`
- `QuestCondition`
- `QuestReward`
- `QuestEvents`
- package validation
- generic service contracts

The package is intentionally independent from project-specific stacks such as
Addressables, Dialogue System, Odin, Quantum Console, ES3, and project UI.

## Project Responsibility

Projects provide adapters for config loading, localization, reward integration,
UI, triggers, and gameplay-specific unlock rules.

Projects can use Unity Localization, Dialogue System, custom CSV tables, or any
other localization stack by implementing `IQuestLocalizationService`.

## Config Loading

Projects can register a config source:

```csharp
public sealed class MyQuestConfigSource : IQuestConfigSource
{
    public IReadOnlyList<QuestConfigSO> LoadConfigs()
    {
        return loadedQuestConfigs;
    }
}

QuestServiceRegistry.SetConfigSource(source);
QuestManager.Instance.ReloadConfigsFromSource();
```

Projects that load configs asynchronously can also directly call
`QuestManager.RegisterConfig(config)` after each config is available.

If no project config source returns configs, `QuestManager` falls back to
`Resources.LoadAll<QuestConfigSO>("Quests")` for simple projects and samples.

When a project explicitly registers a custom config source, an empty result is
treated as authoritative. In that mode `QuestManager` does not fall back to
Resources, which lets production projects keep a single config source such as
Addressables.

## Localization

The package does not depend on Unity Localization, Dialogue System, or any
project-specific table format.

Projects implement `IQuestLocalizationService` when they need localized quest
title, description, condition, or reward text. The default service returns safe
fallback text from the quest asset itself.

## Rewards

`QuestReward` remains the base data shape. By default, rewards call their own
`Grant()` method. Projects can route reward granting through
`IQuestRewardService` to connect inventory, economy, analytics, or platform
systems without changing quest config schema.

## Save

Save backend abstraction is not part of this RPG-ready foundation pass.

`QuestManager` still exposes `QuestSystemSaveData` through its existing
save/load methods. Each project decides how to persist that data through its
own save stack.

Add a save abstraction only in a separate plan that explicitly covers existing
`ISaveable`, `SaveSlotManager`, project save events, no-config behavior, and
backward compatibility.

## Editor Primitives

The package provides dependency-light editor primitives:

- `QuestConfigValidator`
- `QuestEditorQuestIdProvider`
- `QuestIdDropdownAttribute`
- `QuestStringDropdownAttribute`
- `QuestStringDropdownProviderRegistry`
- `QuestStringDropdownDrawer`
- `DialogQuestCondition`

These tools use Unity Editor APIs only. Rich project windows can build on top
of them, but should stay in the project layer. Projects may register NPC,
entity, item, and location dropdown sources without changing serialized quest
fields; missing dropdown values are preserved as plain strings.
