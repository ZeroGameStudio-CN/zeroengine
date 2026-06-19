# Interaction System (v1.14.0+)

可交互对象系统，提供统一的交互检测、条件验证和事件管理。

## 快速开始

### 1. 创建可交互对象

继承 `InteractableBase` 实现自定义交互：

```csharp
using ZeroEngine.Interaction;

public class MyChest : InteractableBase
{
    [SerializeField] private InventoryItemSO[] _lootItems;
    private bool _isOpened = false;

    protected override void Awake()
    {
        base.Awake();
        _interactionType = InteractionType.Open;
        _displayName = "Treasure Chest";
    }

    protected override InteractionResult ExecuteInteraction(InteractionContext ctx)
    {
        if (_isOpened)
        {
            return InteractionResult.Failed(this, "Already opened");
        }

        _isOpened = true;

        // 发放奖励
        foreach (var item in _lootItems)
        {
            InventoryManager.Instance.AddItem(item);
        }

        // 播放开箱动画...

        return InteractionResult.Succeeded(this);
    }
}
```

### 2. 设置玩家检测器

在玩家角色上添加 `InteractionDetector` 组件：

```csharp
// Inspector 设置
InteractionDetector:
  Detection Radius: 5
  Interaction Distance: 2
  Detection Rate: 10
  Interact Key: E
```

### 3. 添加 UI 提示

添加 `InteractionPromptUI` 组件到 Canvas：

将玩家上的 `InteractionDetector` 拖到 `InteractionPromptUI` 的 Detector
字段，或在初始化代码中调用 `promptUI.SetDetector(detector)`。

```csharp
// 或在代码中监听事件
detector.OnTargetChanged += target =>
{
    if (target != null)
    {
        promptUI.Show(target);
    }
    else
    {
        promptUI.Hide();
    }
};
```

## 核心 API

### InteractionManager

交互系统核心管理器，使用 `MonoSingleton<T>` 模式。

```csharp
// 注册/注销可交互对象 (通常自动完成)
InteractionManager.Instance.RegisterInteractable(interactable);
InteractionManager.Instance.UnregisterInteractable(interactable);

// 查询
var nearby = InteractionManager.Instance.GetNearbyInteractables(position, radius);
var nearest = InteractionManager.Instance.GetNearestInteractable(position);
var byId = InteractionManager.Instance.FindById("chest_001");
var byType = InteractionManager.Instance.GetInteractablesByType(InteractionType.Talk);

// 交互
var result = InteractionManager.Instance.TryInteract(target, playerGameObject);
if (result.Success)
{
    Debug.Log("Interaction succeeded!");
}

// 焦点管理
InteractionManager.Instance.SetFocusTarget(target);
InteractionManager.Instance.ClearFocus();

// 事件
InteractionManager.Instance.OnInteractionCompleted += args =>
{
    Debug.Log($"Interacted with {args.Interactable.DisplayName}");
};
```

### IInteractable 接口

所有可交互对象必须实现此接口：

```csharp
public interface IInteractable
{
    string InteractableId { get; }
    string DisplayName { get; }
    InteractionType Type { get; }
    InteractionPriority Priority { get; }
    bool IsEnabled { get; }

    string GetInteractionHint();
    bool CanInteract(InteractionContext ctx);
    string GetCannotInteractReason(InteractionContext ctx);
    InteractionResult OnInteract(InteractionContext ctx);

    void OnFocus();
    void OnUnfocus();
    Vector3 GetInteractionPosition();
    GameObject GameObject { get; }
}
```

### InteractableBase 基类

提供 `IInteractable` 的默认实现，推荐继承此类：

```csharp
public class MyInteractable : InteractableBase
{
    protected override InteractionResult ExecuteInteraction(InteractionContext ctx)
    {
        // 实现交互逻辑
        return InteractionResult.Succeeded(this);
    }
}
```

## 交互条件

使用 `[SerializeReference]` 多态条件：

```csharp
// 在 Inspector 中添加条件
[SerializeReference]
public List<InteractionCondition> _conditions;

// 内置条件类型
- ItemRequiredCondition   // 需要道具
- QuestStateCondition     // 任务状态
- TimeCondition           // 时间范围
- RelationshipCondition   // 好感度等级
- DistanceCondition       // 距离限制
- CooldownCondition       // 冷却时间

// 自定义条件
[Serializable]
public class MyCondition : InteractionCondition
{
    public override string ConditionType => "MyCondition";

    public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
    {
        // 检查条件
        return true;
    }

    public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
    {
        return "Condition not met";
    }
}
```

## 交互类型

```csharp
public enum InteractionType
{
    Pickup,      // 拾取物品
    Talk,        // 对话
    Open,        // 打开 (宝箱、门)
    Use,         // 使用
    Examine,     // 检查
    Activate,    // 激活 (机关、开关)
    Enter,       // 进入 (传送点)
    Craft,       // 制作
    Custom       // 自定义
}
```

## 事件

```csharp
// InteractionManager 事件
OnInteractableEnterRange  // 对象进入检测范围
OnInteractableExitRange   // 对象离开检测范围
OnInteractionStarted      // 交互开始
OnInteractionCompleted    // 交互完成
OnInteractionFailed       // 交互失败
OnFocusTargetChanged      // 焦点变更

// InteractionDetector 事件
OnTargetChanged           // 当前目标变更
OnInteractableEnterRange  // 进入范围 (本地)
OnInteractableExitRange   // 离开范围 (本地)
OnInteractRequested       // 交互请求

// InteractableBase 事件
OnFocused                 // 获得焦点
OnUnfocused               // 失去焦点
OnInteracted              // 交互完成
```

## Quest 系统集成

交互完成时自动触发任务事件：

```csharp
// 自动触发 QuestEvents.Interacted 事件
// 在 QuestConfigSO 中使用 InteractCondition 监听

// 任务条件配置
[SerializeReference]
public List<QuestCondition> Conditions = new()
{
    new InteractCondition
    {
        TargetId = "npc_merchant",
        RequiredCount = 1,
        InteractionType = InteractionType.Talk
    }
};
```

## 配置

使用 `InteractionConfigSO` 配置全局参数：

```csharp
// 创建: Assets > Create > ZeroEngine > Interaction > Interaction Config

InteractionConfigSO:
  Default Detection Radius: 5
  Default Interaction Distance: 2
  Detection Rate: 10
  Interact Key: E
  Enable Outline: true
  Outline Color: Yellow
  Hint Templates:
    - Pickup: "[E] Pick up {0}"
    - Talk: "[E] Talk to {0}"
    ...
```

## 性能优化

1. **检测频率**: 默认 10 次/秒，可根据需求调整
2. **空间查询**: 使用 HashSet + 距离排序，O(n) 复杂度
3. **GC 优化**:
   - 使用 `readonly struct` 传递事件数据
   - 复用列表避免每帧分配
   - 静态比较器缓存

## 预设交互类型 (v1.14.0+)

| 类 | 用途 |
|---|---|
| `InteractableItem` | 可拾取物品 |
| `InteractableNPC` | NPC 对话 |
| `InteractableDoor` | 门/传送点 |
| `InteractableChest` | 宝箱 |
| `InteractableSwitch` | 开关/机关 |
| `InteractableCraftStation` | 工作台 |
