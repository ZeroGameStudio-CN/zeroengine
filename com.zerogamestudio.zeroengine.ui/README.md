# ZeroEngine.UI

工业级、项目无关的 UI 框架包。

## 版本
- **当前版本**: 2.1.0
- **依赖**: ZeroEngine.Core, TextMeshPro

2.1.0 在保持既有 UIManager/UIViewBase API 兼容的基础上，提供并发 open/close 请求串行化、
session generation 清理、按生命周期管理的 Addressables handle cache、modal mask owner/排序
刷新，以及可注入的暂停/日志 hook。未注入日志 hook 时，UIManager 会按日志级别回退到带
`[UIManager]` 上下文的 Unity Debug 输出；取消输入由宿主显式转发。

## 包含模块

### UI.Core
- `UIManager` - UI 管理器 (7 层级系统、窗口栈、遮罩和资源生命周期)
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

// 宿主输入系统在收到取消动作时显式转发
UIManager.Instance.TriggerCancelInput();

// 暂停和日志由宿主注入，不依赖项目服务定位器
UIManager.Instance.Hooks = new UIManagerHooks(
    pause: paused => { /* host time service */ },
    log: (level, message) => { /* host logger */ });
```

## 条件编译

| 宏 | 说明 |
|----|------|
| `ZEROENGINE_ADDRESSABLES` | Addressables 加载 |
| `ODIN_INSPECTOR` | Odin 编辑器支持 |

## ZeroEngine Dashboard

可选 Dashboard 会在 `ZGS > 工作台 > 系统与安装` 的高级工具中注册本包 typed 安装动作，并按 `project-write` 要求确认；实际安装、Undo 和资源写入安全仍由本包负责，本包不依赖 Dashboard。
