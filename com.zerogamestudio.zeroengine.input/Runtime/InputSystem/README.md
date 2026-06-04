# ZeroEngine.InputSystem API 文档

> **用途**: 本文档面向AI助手，提供InputSystem模块的快速参考。
> **版本**: v2.0.0+
> **最后更新**: 2026-06-04

---

## 目录结构

```
InputSystem/
├── InputManager.cs                    # 兼容输入管理器（单例）
├── InputActionKey.cs                  # action/binding 标识和结果 DTO
├── InputActionLookup.cs               # 安全查询 action/binding
├── InputControlSchemeResolver.cs      # control path 到 binding group 的解析
├── InputActionCatalog.cs              # 项目动作 catalog 和验证
├── InputBindingDisplayService.cs      # 绑定显示名
├── InputBindingOverrideService.cs     # override 应用、重置、保存、加载
├── InputBindingConflictValidator.cs   # 冲突检测
├── InputRebindService.cs              # 交互式改键封装
└── InputSettingsModelBuilder.cs       # 设置页 binding row 模型
```

---

## 依赖

- **Unity Input System Package**: 需要安装 `com.unity.inputsystem`

---

## InputManager

**用途**: 全局输入管理器，管理 InputActionAsset 和 Action Maps

```csharp
public class InputManager : Singleton<InputManager>
{
    [SerializeField] InputActionAsset _inputActionAsset;
    
    // Action Maps
    public InputActionMap PlayerActions { get; }  // 游戏操作
    public InputActionMap UIActions { get; }      // UI操作
    
    // 启用/禁用
    void EnableAllActions();
    void DisableAllActions();
    
    // 模式切换
    void SwitchToGameplayMode();  // 禁用UI，启用Player
    void SwitchToUIMode();        // 禁用Player，启用UI
}
```

## InputActionKey / InputBindingKey

**用途**: 用稳定字符串标识项目 action，避免 UI 和持久化层直接持有运行时对象引用。

```csharp
var actionKey = new InputActionKey("Player", "Interact");
var bindingKey = new InputBindingKey("Player", "Interact", "Keyboard&Mouse");
```

## InputActionLookup

**用途**: 查找 Action 或某个 binding group 的 Binding。查找失败返回诊断。

```csharp
var action = InputActionLookup.FindAction(inputActions, actionKey);
var binding = InputActionLookup.FindBinding(inputActions, bindingKey);
```

## InputControlSchemeResolver

**用途**: 将常见 Unity control path 解析为设置系统使用的 binding group。

```csharp
var group = InputControlSchemeResolver.ResolveBindingGroup("<Gamepad>/buttonSouth");
```

## InputActionCatalogValidator

**用途**: 验证项目侧 action metadata 是否能对应到真实 `InputActionAsset`。

```csharp
var catalog = new[]
{
    new InputActionCatalogEntry(
        "interact",
        new InputActionKey("Player", "Interact"),
        new[] { "Keyboard&Mouse", "Gamepad" },
        "Gameplay",
        required: true,
        configurable: true,
        displayNameKey: "input.interact",
        categoryKey: "input.category.gameplay",
        sortOrder: 0)
};

var validation = InputActionCatalogValidator.Validate(inputActions, catalog);
```

## InputSettingsModelBuilder

**用途**: 从 catalog 和当前 action asset 生成设置 UI 可消费的 rows。项目侧仍负责本地化和具体界面。

```csharp
var model = InputSettingsModelBuilder.Build(inputActions, catalog);
foreach (var row in model.Rows)
{
    Debug.Log($"{row.DisplayNameKey} {row.BindingGroup}: {row.DisplayName}");
}
```

## InputBindingDisplayService

**用途**: 获取 UI 可展示的绑定名和实际 effective path。

```csharp
var display = InputBindingDisplayService.GetDisplayName(inputActions, bindingKey);
if (display.Success)
{
    Debug.Log($"{display.DisplayName}: {display.EffectivePath}");
}
```

## InputBindingOverrideService

**用途**: 应用、重置、保存和加载改键覆盖。保存格式使用 map/action/binding group，不依赖某个 asset 实例的运行时对象引用。

```csharp
InputBindingOverrideService.ApplyOverride(inputActions, bindingKey, "<Keyboard>/f");
var json = InputBindingOverrideService.SaveOverridesAsJson(inputActions);
InputBindingOverrideService.LoadOverridesFromJson(inputActions, json);
InputBindingOverrideService.ResetBinding(inputActions, bindingKey);
InputBindingOverrideService.ResetAll(inputActions);
```

## InputBindingConflictValidator

**用途**: 检查同一 conflict scope 和 binding group 下是否存在重复 effective path。

```csharp
var conflicts = InputBindingConflictValidator.Validate(inputActions, new[]
{
    new InputBindingConflictDescriptor(
        new InputBindingKey("Player", "Interact", "Keyboard&Mouse"),
        "Gameplay"),
    new InputBindingConflictDescriptor(
        new InputBindingKey("Player", "Cancel", "Keyboard&Mouse"),
        "Gameplay")
});
```

## InputRebindService

**用途**: 启动 `PerformInteractiveRebinding`，统一取消键、排除控制和释放逻辑。

```csharp
using var result = InputRebindService.Start(
    inputActions,
    bindingKey,
    InputRebindOptions.Default);

if (!result.Success)
{
    Debug.LogWarning(result.Diagnostic);
}
```

---

## InputActionAsset 配置

需要在 InputActionAsset 中创建以下 Action Maps：

| Action Map | 用途 |
|------------|------|
| `Player` | 游戏角色控制（移动、攻击等） |
| `UI` | 界面操作（导航、确认等） |

---

## 使用示例

```csharp
// 1. 获取Action并绑定
var moveAction = InputManager.Instance.PlayerActions.FindAction("Move");
moveAction.performed += ctx => {
    Vector2 input = ctx.ReadValue<Vector2>();
    // 处理移动
};

// 2. 切换输入模式
InputManager.Instance.SwitchToUIMode();    // 打开菜单时
InputManager.Instance.SwitchToGameplayMode();  // 关闭菜单时

// 3. 禁用所有输入（过场动画等）
InputManager.Instance.DisableAllActions();
```

---

## 设计说明

- **Action Maps分离**: Player和UI互斥，避免输入冲突
- **自动启用**: OnEnable时自动启用所有Actions
- **Inspector配置**: InputActionAsset通过Inspector分配
- **项目边界**: 本包不内置项目 action 名、本地化、设置 UI 或偏好存储位置
- **旧输入隔离**: 新能力只依赖 Unity Input System，不提供 `UnityEngine.Input` 兼容层
