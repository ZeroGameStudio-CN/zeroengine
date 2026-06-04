# ZeroEngine.Input

ZeroEngine 可复用输入设置内核，基于 Unity Input System 提供 Action 查询、按设备组显示绑定、改键覆盖、覆盖持久化、冲突检测和交互式改键封装。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, Unity Input System

## 包含模块

### InputSystem
- `InputManager` - 兼容用输入管理器，持有 `InputActionAsset` 并控制 Player/UI Action Map。
- `InputActionKey` / `InputBindingKey` - 稳定的 map/action/binding-group 标识。
- `InputActionLookup` - 安全查询 Action 和 Binding，失败返回诊断而不是抛异常。
- `InputControlSchemeResolver` - 将常见 control path 解析到 `Keyboard&Mouse` 或 `Gamepad` binding group。
- `InputActionCatalogEntry` / `InputActionCatalogValidator` - 项目侧动作元数据和治理验证。
- `InputBindingDisplayService` - 生成键鼠/手柄绑定显示名和 effective path。
- `InputBindingOverrideService` - 应用、重置、保存和加载 binding override。
- `InputBindingConflictValidator` - 按 binding group 和 conflict scope 检测同键冲突。
- `InputRebindService` - 启动 Unity Input System 的 `PerformInteractiveRebinding` 并返回可释放操作。
- `InputSettingsModelBuilder` - 从项目 catalog 生成设置 UI 可消费的 binding rows。

## 快速使用

```csharp
using ZeroEngine.InputSystem;

var key = new InputBindingKey("Player", "Interact", "Keyboard&Mouse");
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

// 治理验证
var validation = InputActionCatalogValidator.Validate(inputActions, catalog);

// 设置页模型
var settingsModel = InputSettingsModelBuilder.Build(inputActions, catalog);

// 改键
var change = InputBindingOverrideService.ApplyOverride(inputActions, key, "<Keyboard>/f");
if (!change.Success)
{
    UnityEngine.Debug.LogWarning(change.Diagnostic);
}

// 显示当前绑定
var display = InputBindingDisplayService.GetDisplayName(inputActions, key);
UnityEngine.Debug.Log(display.DisplayName);

// 保存 / 加载 override
var json = InputBindingOverrideService.SaveOverridesAsJson(inputActions);
InputBindingOverrideService.LoadOverridesFromJson(inputActions, json);

// 交互式改键
using var rebind = InputRebindService.Start(inputActions, key, InputRebindOptions.Default);
```

## 边界

- 本包只提供可跨项目复用的输入设置内核。
- 项目侧负责自己的 `InputActionAsset`、本地化文案、设置 UI、玩家偏好存储位置和具体 gameplay facade。
- 本包不包含项目专属 action 名、界面布局、真实设备图标资源、玩家存档实现或旧 `UnityEngine.Input` 兼容层。
