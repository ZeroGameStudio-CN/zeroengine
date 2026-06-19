# ZeroEngine.Dialog API 文档

> **用途**: 本文档面向AI助手，提供Dialog（对话系统）模块的快速参考。
> **版本**: v1.4.0+ (集成 v1.9.0+)
> **最后更新**: 2026-01-03

---

A flexible dialogue system supporting branching dialogs, conditions, variables, and multiple backends.

## Features

- **Node-Based Graph**: Visual dialog authoring with branching and conditions (v1.4.0+)
- **Variable System**: Local and global variables with typed values (v1.4.0+)
- **Condition Parser**: Expression-based conditions (`gold >= 100 && hasKey`) (v1.4.0+)
- **Provider Pattern**: Swap backends freely (Graph, SO, Ink, custom)
- **Typewriter Effect**: Built-in with configurable speed
- **Localization**: Auto-resolve `LocalizationKey` via `LocalizationManager`
- **Callbacks**: External event hooks for game integration

---

## Quick Start (v1.4.0+)

### 1. Create a Dialog Graph
Right-click -> **ZeroEngine** -> **Dialog** -> **Dialog Graph**

### 2. Use DialogRunner (Recommended)
```csharp
public DialogGraphSO dialogGraph;
public DialogRunner runner;

void Start()
{
    runner.OnLineDisplay += HandleLine;
    runner.OnChoicesAvailable += HandleChoices;
    runner.OnDialogEnd += HandleEnd;

    runner.StartDialog(dialogGraph);
}

void HandleLine(DialogLine line)
{
    speakerText.text = line.Speaker;
    // Typewriter handled automatically
}

void HandleChoices(List<DialogChoice> choices)
{
    // Create choice buttons
    for (int i = 0; i < choices.Count; i++)
    {
        var index = i;
        var btn = CreateButton(choices[i].Text);
        btn.interactable = choices[i].IsEnabled;
        btn.onClick.AddListener(() => runner.SelectChoice(index));
    }
}

void HandleEnd(string endTag)
{
    Debug.Log($"Dialog ended with tag: {endTag}");
}
```

### 3. Player Input
```csharp
// Advance or skip typewriter
runner.Advance();

// Select choice (usually via button)
runner.SelectChoice(0);
```

---

## Node Types (v1.4.0+)

| Node Type | Description |
|-----------|-------------|
| `Start` | Entry point of dialog |
| `Text` | Display dialogue line with speaker, portrait, voice |
| `Choice` | Present multiple choice options |
| `Condition` | Branch based on expression (true/false paths) |
| `SetVariable` | Set/modify a variable value |
| `Random` | Random branch with weights |
| `Callback` | Trigger external game event |
| `End` | Exit point with optional tag |

---

## Variable System (v1.4.0+)

### Variable Types
- `bool`, `int`, `float`, `string`

### Scopes
- **Local**: Cleared when dialog ends
- **Global**: Persists across dialogs (prefix with `$`)

### Usage in Code
```csharp
// Set variables
runner.SetVariable("gold", 100);
runner.SetVariable("$playerName", "Hero");  // Global

// Get variables
int gold = runner.GetVariable<int>("gold");
string name = runner.GetVariable<string>("$playerName");
```

### Text Substitution
```
Speaker: "Hello, {playerName}! You have {gold} gold."
```

---

## Condition Expressions (v1.4.0+)

### Supported Operators
| Operator | Description |
|----------|-------------|
| `==` | Equal |
| `!=` | Not equal |
| `<`, `<=` | Less than |
| `>`, `>=` | Greater than |
| `&&` | Logical AND |
| `||` | Logical OR |
| `!` | Negation |

### Examples
```
hasKey                      # Variable is truthy
!hasKey                     # Variable is falsy
gold >= 100                 # Numeric comparison
name == "Hero"              # String comparison
gold >= 100 && hasKey       # Combined conditions
isVIP || gold >= 1000       # OR condition
```

---

## DialogRunner Events

| Event | Parameters | Description |
|-------|------------|-------------|
| `OnDialogStart` | - | Dialog started |
| `OnDialogEnd` | `string endTag` | Dialog ended |
| `OnLineDisplay` | `DialogLine line` | Line to display |
| `OnTypewriterUpdate` | `string text, float progress` | Typewriter tick |
| `OnTypewriterComplete` | - | Typewriter finished |
| `OnChoicesAvailable` | `List<DialogChoice>` | Choices ready |
| `OnChoiceSelected` | `int index, DialogChoice` | Choice was selected |
| `OnCallback` | `string id, string param` | External callback |
| `OnVariableChanged` | `string name, old, new` | Variable changed |

---

## Legacy: DialogManager + DialogueSO

Simple linear dialogues still supported:

```csharp
// Create DialogueSO asset
// Use DialogManager singleton
DialogManager.Instance.StartDialogue(myDialogue);
DialogManager.Instance.DisplayNext();
DialogManager.Instance.SelectChoice(0);
```

---

## Ink Integration

Requires `com.inklestudios.ink-unity-integration` package.

```csharp
var provider = new InkDialogProvider(inkJsonAsset);
DialogManager.Instance.StartDialogue(provider);
```

---

## Integration with Other Systems (v1.9.0+)

### DialogIntegrationHandler & DialogCallbackIds

Built-in callback handlers for Quest, Inventory, Currency, and Events.

```csharp
public static class DialogCallbackIds
{
    // Quest
    public const string AcceptQuest = "Quest.Accept";
    public const string CompleteQuest = "Quest.Complete";
    public const string SubmitQuest = "Quest.Submit";
    public const string AbandonQuest = "Quest.Abandon";

    // Inventory
    public const string GiveItem = "Inventory.Give";
    public const string TakeItem = "Inventory.Take";
    public const string CheckItem = "Inventory.Check";

    // Currency
    public const string GiveGold = "Currency.Give";
    public const string TakeGold = "Currency.Take";

    // Events
    public const string TriggerEvent = "Event.Trigger";
    public const string PlaySound = "Audio.Play";
}
```

### Usage in DialogCallbackNode

```csharp
// In your dialog graph, add CallbackNode with:
CallbackId: "Quest.Accept"
Parameter: "main_quest_001"

// Or:
CallbackId: "Inventory.Give"
Parameter: "health_potion:3"  // Format: itemId:amount

CallbackId: "Currency.Take"
Parameter: "100"  // Amount only
```

### DialogIntegrationUtils

Helper methods for conditions and state queries:

```csharp
// Check quest state
if (DialogIntegrationUtils.HasActiveQuest("main_quest_001")) { }

// Check inventory
bool hasKey = DialogIntegrationUtils.HasItem("key_item", 1);
int count = DialogIntegrationUtils.GetItemCount("health_potion");

// Sync state to dialog variables (for use in conditions)
DialogIntegrationUtils.SyncVariablesToDialog(context.Variables);
// Now use: quest_main_quest_001 == true in conditions
```

---

## DialogBoxUI & DialogUIConnector (v1.9.0+)

Enhanced UI components for dialog display.

### DialogBoxUI Features

```csharp
public class DialogBoxUI : MonoBehaviour
{
    // Display modes
    public enum PortraitDisplayMode
    {
        Single,      // Single portrait slot
        LeftRight    // Left/right slots for multi-character dialog
    }

    // Public API
    public void Show();
    public void Hide();
    public void ShowLine(DialogLine line);
    public void UpdateText(string text);
    public void ShowChoices(List<DialogChoice> choices);
    public void SetContinueIndicatorVisible(bool visible);
    public void RegisterPortrait(string key, Sprite sprite);
    public void Continue();

    // Events
    public event Action OnContinueClicked;
    public event Action<int> OnChoiceSelected;
}
```

### DialogUIConnector

Automatically connects DialogRunner to DialogBoxUI:

```csharp
[RequireComponent(typeof(DialogRunner))]
public class DialogUIConnector : MonoBehaviour
{
    [SerializeField] private DialogRunner _dialogRunner;
    [SerializeField] private DialogBoxUI _dialogBoxUI;
    [SerializeField] private bool _autoShow = true;   // Auto-show on dialog start
    [SerializeField] private bool _autoHide = true;   // Auto-hide on dialog end
}
```

### Setup Example

```csharp
// In your scene:
// 1. Create GameObject with DialogRunner component
// 2. Create UI Prefab with DialogBoxUI component
// 3. Add DialogUIConnector to DialogRunner GameObject
// 4. Assign references in inspector
// 5. Connector handles all event wiring automatically
```

---

## Architecture Overview

```
DialogRunner (MonoBehaviour, recommended)
    ├── DialogGraphProvider (IDialogProvider)
    │      └── DialogGraphContext
    │             ├── DialogVariables (local + global)
    │             └── DialogNode execution
    │                   ├── TextNode
    │                   ├── ChoiceNode (+ DialogConditionParser)
    │                   ├── ConditionNode (+ DialogConditionParser)
    │                   ├── SetVariableNode
    │                   ├── RandomNode
    │                   ├── CallbackNode
    │                   └── EndNode
    │
    └── DialogUIConnector (v1.9.0+)
           └── DialogBoxUI (v1.9.0+)
                  ├── PortraitDisplayMode (Single/LeftRight)
                  ├── Text rendering + Typewriter
                  └── Choice UI

Integration Layer (v1.9.0+)
    └── DialogIntegrationHandler
           ├── DialogCallbackIds (constants)
           ├── DialogIntegrationUtils (helpers)
           └── System callbacks:
                  ├── Quest (Accept/Complete/Submit/Abandon)
                  ├── Inventory (Give/Take/Check)
                  ├── Currency (Give/Take)
                  └── Events (Trigger/PlaySound)

DialogManager (Singleton, legacy)
    └── IDialogProvider
           ├── DialogueSOProvider (simple)
           ├── DialogGraphProvider (v1.4.0+)
           ├── InkDialogProvider (ink)
           └── XNodeDialogProvider (xnode)
```

---

## File Structure

```
Runtime/Dialog/
├── Core/
│   ├── DialogGraphContext.cs     # Execution context
│   ├── DialogGraphSO.cs          # Graph ScriptableObject
│   └── DialogRunner.cs           # High-level runner
├── Nodes/
│   └── DialogNode.cs             # All node types
├── Variables/
│   └── DialogVariables.cs        # Variable system
├── Conditions/
│   └── DialogCondition.cs        # Condition expressions
├── Config/
│   └── DialogueSO.cs             # Legacy linear dialogue
├── Providers/
│   ├── IDialogProvider.cs        # Provider interface
│   ├── DialogGraphProvider.cs    # Graph provider
│   ├── DialogueSOProvider.cs     # SO provider
│   └── InkDialogProvider.cs      # Ink provider
├── UI/
│   ├── DialogUI.cs               # Sample UI (legacy)
│   ├── DialogBoxUI.cs            # Enhanced UI (v1.9.0+)
│   └── DialogUIConnector.cs      # Runner<->UI connector (v1.9.0+)
├── Integration/  # (v1.9.0+)
│   └── DialogIntegrationHandler.cs  # Quest/Inventory/Currency handlers
└── DialogSaveAdapter.cs          # Save system integration
```

---

## Migration from v1.0

1. **DialogueSO** still works - no changes needed
2. For advanced features, create **DialogGraphSO**
3. Use **DialogRunner** instead of **DialogManager** for new code
4. Variable conditions now support full expressions instead of just truthiness

---

## CSV Export Tool

Export all `DialogueSO` assets to CSV for translation.

**Usage**: Menu -> **ZeroEngine** -> **Dialog** -> **Export to CSV**
