# ZeroEngine.UI

工业级 UI 框架包。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, Addressables, Input System, TextMeshPro

## Designer Config Validation

`ZeroEngine.UI.Editor` provides `UIConfigValidator` for UI view databases and
toast settings. It reports duplicate view names, missing prefab references,
invalid animation durations, duplicate toast severities, and invalid toast
durations.

## 包含模块

### UI.Core
- `UIManager` - UI 管理器 (7 层级系统)
- `UIViewBase` - 视图基类
- 面板栈、遮罩、动画

### UI.MVVM (可选)
- MVVM 数据绑定框架

### UI.Toast
- `Toast` - gameplay notification facade
- `ToastManager` - queue, overflow, duplicate, priority, and group policy runtime
- `ToastRootPresenter` - default presenter router for multiple anchor lanes
- `ToastContainer` - default UGUI anchor container
- `ToastItemView` - default TextMeshPro item view
- `ToastSettings` - style and behavior defaults

The toast system is project-neutral. Games should create a local adapter for localization and call-site semantics.

## 快速使用

```csharp
using ZeroEngine.UI;

// 打开视图
var view = await UIManager.Instance.OpenAsync<InventoryView>();

// 关闭视图
UIManager.Instance.Close<InventoryView>();

// 监听
UIManager.Instance.OnViewOpened += name => Debug.Log(name);
```

## 条件编译

| 宏 | 说明 |
|----|------|
| `ZEROENGINE_ADDRESSABLES` | Addressables 加载 |
| `ODIN_INSPECTOR` | Reserved for a future optional Odin adapter assembly |

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
