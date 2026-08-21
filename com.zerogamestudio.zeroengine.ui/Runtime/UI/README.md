# ZeroEngine.UI API 文档

> **用途**: 本文档面向AI助手，提供UI模块的快速参考。
> **版本**: v1.6.1+
> **最后更新**: 2026-01-02

---

## 目录结构

```
UI/
├── Core/                     # 工业级 UI 框架核心 (v1.6.0+)
│   ├── UIManager.cs          # UI 管理器 (MonoSingleton)
│   ├── UIViewBase.cs         # 视图基类
│   ├── UIDefines.cs          # 枚举/配置类定义
│   ├── UIUtility.cs          # 工具类/扩展方法
│   └── UIViewDatabase.cs     # ScriptableObject 配置数据库
└── MVVM/                     # MVVM UI 框架
    ├── README.md             # 详细 API 文档
    ├── MVVMView.cs           # View 基类
    ├── ViewModelBase.cs      # ViewModel 基类
    ├── BindableProperty.cs   # 可绑定属性
    └── RelayCommand.cs       # 命令封装
```

---

## 子模块

### Core (v1.6.0+)

工业级、项目无关的 UI 管理框架。

**特性**:
- 7 层级系统: Background/Main/Screen/Popup/Overlay/Top/System
- 面板栈管理 (入栈/出栈/暂停/恢复)
- 4 种显示模式: Normal/HideOthers/Stack/Singleton
- 3 种关闭模式: Hide/Destroy/Pool
- 内置动画系统: Fade/Scale/SlideLeft/SlideRight/SlideTop/SlideBottom/Custom
- 遮罩管理 (可配置颜色/点击关闭)
- 取消输入由宿主通过 TriggerCancelInput 或 TryHandleCancelInput 显式转发
- 焦点管理 (手柄/键盘导航支持)
- 资源异步加载 (Addressables 可选)
- 预加载 API
- session/resident/evictable 生命周期与 generation 清理
- IUIManagerHooks 注入暂停和日志实现

### MVVM

完整的 MVVM UI 框架，详见 [MVVM/README.md](MVVM/README.md)

**核心类**:

| 类名 | 用途 |
|------|------|
| `BindableProperty<T>` | 可观察属性，值变更时触发事件 |
| `ViewModelBase` | ViewModel 基类，提供生命周期管理 |
| `BindingContext` | UI 绑定上下文，管理 View-ViewModel 绑定 |
| `RelayCommand` | 命令封装，支持 CanExecute 检查 |
| `MVVMView<T>` | View 基类，泛型参数为 ViewModel 类型 |

---

## Core 模块 API 参考 (v1.6.0+)

### UIManager.cs

**用途**: UI 管理器核心 - 工业级 UI 框架入口

```csharp
// 单例访问
UIManager.Instance

// 注册视图配置
void RegisterView(UIViewConfig config)
void RegisterViews(IEnumerable<UIViewConfig> configs)
void RegisterViewFromPrefab(GameObject prefab, UIViewConfig config = null)
void UnregisterView(string viewName)

// 打开/关闭视图
Task<UIViewBase> OpenAsync(string viewName, UIOpenArgs args = null)
Task<T> OpenAsync<T>(UIOpenArgs args = null) where T : UIViewBase
void Open(string viewName, UIOpenArgs args = null)
void Open<T>(UIOpenArgs args = null) where T : UIViewBase

Task CloseAsync(string viewName, UICloseArgs args = null)
Task CloseAsync<T>(UICloseArgs args = null) where T : UIViewBase
void Close(string viewName, UICloseArgs args = null)
void Close<T>(UICloseArgs args = null) where T : UIViewBase
void CloseTop()
Task CloseAllAsync(UILayer? layer = null)
void CloseAll(UILayer? layer = null)

// 查询
T GetView<T>() where T : UIViewBase
UIViewBase GetView(string viewName)
bool IsOpen(string viewName)
bool IsOpen<T>() where T : UIViewBase
UIViewBase GetTopView()
UIViewBase GetTopView(UILayer layer)
IEnumerable<UIViewBase> GetViewsInLayer(UILayer layer)

// 预加载
Task PreloadAsync(string viewName)
Task PreloadAsync<T>() where T : UIViewBase
Task PreloadAsync(params string[] viewNames)

// 输入
void TriggerCancelInput()

// 属性
bool HasAnyViewOpen { get; }
int OpenViewCount { get; }

// 事件
event Action<string> OnViewOpened
event Action<string> OnViewClosed
event Action OnCancelInputRequested
event Action<bool> OnPauseRequested
```

### UIViewBase.cs

**用途**: 视图基类 - 所有 UI 面板继承此类

**生命周期**: OnCreate -> OnOpen -> OnResume -> OnPause -> OnClose -> OnDestroy

```csharp
// 属性
string ViewName { get; }              // 视图唯一标识 (默认为类名)
UIViewConfig Config { get; }          // 视图配置
UIViewState State { get; }            // 当前状态
bool IsVisible { get; }               // 是否可见
CanvasGroup CanvasGroup { get; }      // CanvasGroup 组件
RectTransform RectTransform { get; }  // RectTransform

// 生命周期 (子类重写)
protected virtual void OnCreate() { }           // 首次创建
protected virtual void OnOpen() { }             // 每次打开
protected virtual void OnResume() { }           // 从暂停恢复
protected virtual void OnPause() { }            // 被覆盖暂停
protected virtual void OnClose() { }            // 关闭
protected virtual void OnViewDestroy() { }      // 销毁
public virtual void Refresh() { }               // 刷新数据
protected virtual void OnLocalizationChanged() { }  // 本地化变更

// 异步初始化
public virtual Task OnCreateAsync()

// 动画 (子类可重写)
protected virtual Task PlayCustomOpenAnimation()
protected virtual Task PlayCustomCloseAnimation()

// 公共 API
void Close()
void CloseWithResult(object result)
T GetData<T>()                        // 获取打开时传入的数据
void RestoreFocus()
void SaveLastSelected()
```

### UIDefines.cs

**用途**: 枚举和配置类定义

```csharp
// 层级 (数值越大渲染越靠前)
enum UILayer {
    Background = 0,   // 背景层
    Main = 100,       // 主界面层 (HUD)
    Screen = 200,     // 全屏面板层 (背包、角色面板)
    Popup = 300,      // 弹窗层 (确认框)
    Overlay = 400,    // 叠加层 (Loading)
    Top = 500,        // 顶层 (调试控制台)
    System = 600      // 系统层 (断网提示)
}

// 显示模式
enum UIShowMode {
    Normal,           // 普通模式：可与其他面板共存
    HideOthers,       // 隐藏同层其他面板
    Stack,            // 入栈模式：关闭时恢复上一个
    Singleton         // 单例模式：全局只能存在一个
}

// 关闭模式
enum UICloseMode {
    Hide,             // 隐藏（保留实例）
    Destroy,          // 销毁（释放内存）
    Pool              // 回池（对象池复用）
}

// 动画类型
enum UIAnimationType {
    None, Fade, Scale,
    SlideLeft, SlideRight, SlideTop, SlideBottom,
    Custom
}

// 视图状态
enum UIViewState {
    None, Created, Opening, Opened, Paused, Closing, Closed
}

// 视图配置
[Serializable]
class UIViewConfig {
    string viewName;
    GameObject prefab;
    // Addressables (条件编译)
    AssetReferenceGameObject prefabReference;
    string resourcePath;

    UILayer layer;
    UIShowMode showMode;
    UICloseMode closeMode;

    bool fullScreen;
    bool showMask;
    bool maskClickClose;
    Color maskColor;

    bool blockInput;
    bool pauseGame;
    bool allowESCClose;

    UIAnimationType openAnimation;
    UIAnimationType closeAnimation;
    float animationDuration;

    bool preload;
    bool cache;
}

// 打开参数
class UIOpenArgs {
    object Data { get; set; }
    Action OnOpened { get; set; }
    Action OnClosed { get; set; }
    bool Immediate { get; set; }

    static UIOpenArgs Create(object data = null)
    UIOpenArgs WithCallback(Action onOpened, Action onClosed)
    UIOpenArgs SetImmediate(bool immediate = true)
}

// 关闭参数
class UICloseArgs {
    object Result { get; set; }
    bool Immediate { get; set; }
    bool Force { get; set; }

    static UICloseArgs Create(object result = null)
}
```

### UIViewDatabase.cs

**用途**: ScriptableObject 配置数据库，批量管理 UI 视图配置

```csharp
[CreateAssetMenu(fileName = "UIViewDatabase", menuName = "ZeroEngine/UI/View Database")]
class UIViewDatabase : ScriptableObject {
    List<UIViewEntry> views;

    IEnumerable<UIViewConfig> GetAllConfigs()
    UIViewConfig GetConfig(string viewName)
    void RegisterToManager(UIManager manager)
    void RegisterToManager()  // 使用单例

    // 编辑器工具
    [ContextMenu("Validate Entries")]
    [ContextMenu("Sort by Layer")]
    [ContextMenu("Auto-Find Prefabs")]
}
```

### UIUtility.cs

**用途**: 静态工具类和扩展方法

```csharp
static class UIUtility {
    // CanvasGroup
    void ShowCanvasGroup(CanvasGroup cg, bool interactable = true)
    void HideCanvasGroup(CanvasGroup cg)
    void SetCanvasGroupVisible(CanvasGroup cg, bool visible, bool interactable = true)

    // RectTransform
    void SetStretch(RectTransform rect)
    void SetCenter(RectTransform rect, Vector2 size)
    Vector2 WorldToCanvasPosition(Canvas canvas, Camera camera, Vector3 worldPosition)
    Vector2 ScreenToCanvasPosition(Canvas canvas, Vector2 screenPosition)

    // Layout
    void ForceRebuildLayout(RectTransform rect)
    void MarkLayoutForRebuild(RectTransform rect)

    // ScrollRect
    void ScrollToTop(ScrollRect scrollRect)
    void ScrollToBottom(ScrollRect scrollRect)
    void ScrollToChild(ScrollRect scrollRect, RectTransform child)

    // Color
    void SetAlpha(Graphic graphic, float alpha)
    void SetAlpha(CanvasGroup cg, float alpha)
    Color ParseHexColor(string hex)
    string ColorToHex(Color color, bool includeAlpha = true)

    // Safe Area
    void ApplySafeArea(RectTransform rect)
    (Vector2 min, Vector2 max) GetSafeAreaAnchors()

    // Raycast
    bool IsPointerOverUI()
    bool IsPointerOverUI(Vector2 screenPosition)
}

// 扩展方法
static class UIExtensions {
    T GetOrAddComponent<T>(this GameObject go)
    T GetOrAddComponent<T>(this Component comp)
    void SetActiveSafe(this GameObject go, bool active)
    void DestroyAllChildren(this Transform transform)
    void DestroyAllChildrenImmediate(this Transform transform)
    void SetStretch(this RectTransform rect)
    void SetCenter(this RectTransform rect, Vector2 size)
    void ForceRebuildLayout(this RectTransform rect)
}
```

---

## 快速示例

### Core 框架示例 (v1.6.0+)

```csharp
using ZeroEngine.UI;
using UnityEngine;
using UnityEngine.UI;

// 1. 创建自定义视图
public class InventoryView : UIViewBase
{
    [SerializeField] private Text titleText;
    [SerializeField] private Button closeButton;

    protected override void OnCreate()
    {
        closeButton.onClick.AddListener(Close);
    }

    protected override void OnOpen()
    {
        var data = GetData<InventoryData>();
        if (data != null)
        {
            titleText.text = data.Title;
        }
    }

    protected override void OnClose()
    {
        // 清理
    }
}

// 2. 使用 UIViewDatabase 注册 (推荐)
// 在 ScriptableObject 中配置视图

// 3. 运行时使用
public class GameUI : MonoBehaviour
{
    [SerializeField] private UIViewDatabase viewDatabase;

    void Start()
    {
        // 注册所有视图配置
        viewDatabase.RegisterToManager();

        // 监听事件
        UIManager.Instance.OnViewOpened += OnViewOpened;
        UIManager.Instance.OnPauseRequested += OnPauseRequested;
    }

    // 打开视图
    public async void OpenInventory()
    {
        var view = await UIManager.Instance.OpenAsync<InventoryView>(
            UIOpenArgs.Create(new InventoryData { Title = "背包" })
                .WithCallback(
                    onOpened: () => Debug.Log("背包已打开"),
                    onClosed: () => Debug.Log("背包已关闭")
                )
        );
    }

    // 打开弹窗
    public void ShowConfirmDialog()
    {
        UIManager.Instance.Open("ConfirmDialog",
            UIOpenArgs.Create(new DialogData { Message = "确定退出?" }));
    }

    // 查询
    public void CheckUI()
    {
        if (UIManager.Instance.IsOpen<InventoryView>())
        {
            var view = UIManager.Instance.GetView<InventoryView>();
            view.Refresh();
        }
    }

    // 预加载
    public async void PreloadViews()
    {
        await UIManager.Instance.PreloadAsync("InventoryView", "ShopView", "SkillView");
    }

    private void OnViewOpened(string viewName) => Debug.Log($"[UI] Opened: {viewName}");
    private void OnPauseRequested(bool pause) => Time.timeScale = pause ? 0 : 1;
}
```

### MVVM 示例

```csharp
// 1. 定义 ViewModel
public class PlayerViewModel : ViewModelBase
{
    public BindableProperty<string> Name = new();
    public BindableProperty<int> Level = new();
    public RelayCommand LevelUpCommand;

    public override void Initialize()
    {
        LevelUpCommand = new RelayCommand(
            () => Level.Value++,
            () => Level.Value < 100
        );
    }
}

// 2. 在 View 中绑定
public class PlayerView : MonoBehaviour
{
    [SerializeField] Text nameText;
    [SerializeField] Button levelUpButton;

    private BindingContext _context = new();
    private PlayerViewModel _vm = new();

    void Start()
    {
        _vm.Initialize();
        _context.BindText(nameText, _vm.Name);
        _context.BindCommand(levelUpButton, _vm.LevelUpCommand);
    }

    void OnDestroy()
    {
        _context.Dispose();
        _vm.Dispose();
    }
}
```

---

## 编译宏

| 宏定义 | 效果 |
|--------|------|
| `ZEROENGINE_ADDRESSABLES` | 启用 Addressables 资源加载支持 (Core) |
| `ODIN_INSPECTOR` | 启用 Odin Inspector 编辑器增强 (Core) |
| `TEXTMESHPRO_ENABLED` | 启用 TMP_InputField / TMP_Dropdown 绑定支持 (MVVM) |
| `ZEROENGINE_DEBUG` | 启用 UIManager 调试日志 (Core) |

---

## 性能优化 (v1.6.1+)

UI Core 模块已针对零 GC 分配进行优化，适用于高频调用场景。

### 优化内容

| 优化项 | 原 GC | 优化后 | 说明 |
|--------|-------|--------|------|
| `UpdateTopView()` | ~120 bytes/次 | 0 | 缓存 UILayer 数组，避免 `Enum.GetValues()` |
| `RemoveFromStack()` | ~64+ bytes/次 | 0 | 静态 TempViewList 替代临时 Stack |
| `IsPointerOverUI()` | ~80+ bytes/次 | 0 | SharedRaycastResults 静态列表缓存 |
| `typeof(T).Name` | ~40 bytes/次 | 0 | `ViewNameCache<T>` 泛型静态类缓存 |
| 调试日志 | 运行时开销 | 0 | `[Conditional]` 属性，Release 编译移除 |

### 缓存机制

```csharp
// UIManager 内部缓存
private static readonly UILayer[] AllLayers;        // 所有层级数组 (一次性分配)
private static readonly UILayer[] LayersSortedDesc; // 降序层级数组 (一次性分配)
private static readonly List<UIViewBase> TempViewList; // 临时操作列表 (重用)

// UIUtility 内部缓存
private static readonly List<RaycastResult> SharedRaycastResults; // Raycast 结果缓存

// 泛型类型名称缓存
private static class ViewNameCache<T> where T : UIViewBase
{
    public static readonly string Name = typeof(T).Name;
}
```

### 条件化调试日志

调试日志仅在定义 `ZEROENGINE_DEBUG` 宏时编译：

```csharp
// 在 Player Settings > Scripting Define Symbols 添加:
// ZEROENGINE_DEBUG

// 代码中使用:
[System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
private static void LogDebug(string message) => Debug.Log($"[UIManager] {message}");
```

Release 构建时这些方法调用将被完全移除，不产生任何运行时开销。

### 性能评分

- **v1.6.0**: 6/10 (存在热路径 GC 分配)
- **v1.6.1+**: 8+/10 (所有热路径零 GC)
