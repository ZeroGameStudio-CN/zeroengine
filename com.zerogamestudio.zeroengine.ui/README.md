# ZeroEngine.UI

工业级 UI 框架包。

## 版本
- **当前版本**: 2.0.2
- **依赖**: ZeroEngine.Core, TextMeshPro

2.0.2 将 Dashboard 安装入口、tooltip 与写入确认统一为简体中文，不改原菜单路径。

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
| `ODIN_INSPECTOR` | Odin 编辑器支持 |

## ZeroEngine Dashboard

可选 Dashboard 会在 `ZGS > 工作台 > 系统与安装` 的高级工具中注册本包 typed 安装动作，并按 `project-write` 要求确认；实际安装、Undo 和资源写入安全仍由本包负责，本包不依赖 Dashboard。
