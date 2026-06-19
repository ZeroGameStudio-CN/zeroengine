using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ZeroEngine.Core;

#if ZEROENGINE_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace ZeroEngine.UI
{
    /// <summary>
    /// UI管理器 - 工业级UI框架核心
    ///
    /// 特性:
    /// 1. 多层级管理（Background/Main/Screen/Popup/Overlay/Top/System）
    /// 2. 面板栈管理（支持入栈/出栈、暂停/恢复）
    /// 3. 灵活的显示模式（Normal/HideOthers/Stack/Singleton）
    /// 4. 可配置的动画系统
    /// 5. 焦点管理（手柄/键盘导航支持）
    /// 6. 遮罩管理
    /// 7. 资源异步加载与缓存
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        #region Static Cache - 静态缓存 (性能优化)

        // 缓存所有 UILayer 值（避免每次 Enum.GetValues 分配）
        private static readonly UILayer[] AllLayers = (UILayer[])Enum.GetValues(typeof(UILayer));

        // 缓存降序排列的 UILayer（用于 UpdateTopView）
        private static readonly UILayer[] LayersSortedDesc = CreateSortedLayersArray();

        // 复用列表（用于 RemoveFromStack，避免每次分配 Stack）
        private static readonly List<UIViewBase> TempViewList = new(8);

        private static UILayer[] CreateSortedLayersArray()
        {
            var layers = (UILayer[])Enum.GetValues(typeof(UILayer));
            Array.Sort(layers, (a, b) => ((int)b).CompareTo((int)a));
            return layers;
        }

        /// <summary>
        /// 泛型类型名称缓存（避免 typeof(T).Name 重复分配）
        /// </summary>
        private static class ViewNameCache<T> where T : UIViewBase
        {
            public static readonly string Name = typeof(T).Name;
        }

        /// <summary>
        /// 条件化日志输出（仅在 Editor 和 Development Build 中执行）
        /// Release 构建中此方法调用会被编译器完全移除
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogDebug(string message)
        {
            Debug.Log(message);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }

        #endregion

        #region Fields

        [Header("Layer Containers")]
        [SerializeField] private Transform backgroundLayer;
        [SerializeField] private Transform mainLayer;
        [SerializeField] private Transform screenLayer;
        [SerializeField] private Transform popupLayer;
        [SerializeField] private Transform overlayLayer;
        [SerializeField] private Transform topLayer;
        [SerializeField] private Transform systemLayer;

        [Header("Mask")]
        [SerializeField] private GameObject maskPrefab;

        [Header("Settings")]
        [Tooltip("Enable ESC key to close top view")]
        [SerializeField] private bool enableESCClose = true;

        // 所有已注册的视图配置
        private Dictionary<string, UIViewConfig> _viewConfigs = new();

        // 已加载的视图实例
        private Dictionary<string, UIViewBase> _viewInstances = new();

        // 各层级的视图栈
        private Dictionary<UILayer, Stack<UIViewBase>> _layerStacks = new();

        // 遮罩实例池
        private Dictionary<UILayer, GameObject> _maskInstances = new();

        // 当前最顶层视图
        private UIViewBase _topView;

        // 事件
        public event Action<string> OnViewOpened;
        public event Action<string> OnViewClosed;

        /// <summary>
        /// ESC关闭请求事件（可由外部输入系统触发）
        /// </summary>
        public event Action OnCancelInputRequested;

        /// <summary>
        /// 游戏暂停请求事件（当视图配置需要暂停游戏时触发）
        /// </summary>
        public event Action<bool> OnPauseRequested;

        #endregion

        #region Properties

        /// <summary>
        /// 是否有任何UI打开
        /// </summary>
        public bool HasAnyViewOpen
        {
            get
            {
                foreach (var stack in _layerStacks.Values)
                {
                    if (stack.Count > 0) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 当前打开的UI数量
        /// </summary>
        public int OpenViewCount
        {
            get
            {
                int count = 0;
                foreach (var stack in _layerStacks.Values)
                {
                    count += stack.Count;
                }
                return count;
            }
        }

        #endregion

        #region Initialization

        protected override void Awake()
        {
            base.Awake();
            InitializeLayers();
        }

        private void Update()
        {
            // 处理ESC键关闭
#if ENABLE_INPUT_SYSTEM
            if (enableESCClose && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleCancelInput();
            }
#else
            if (enableESCClose && Input.GetKeyDown(KeyCode.Escape))
            {
                HandleCancelInput();
            }
#endif
        }

        private void InitializeLayers()
        {
            // 初始化每个层级的栈（使用缓存数组，避免 Enum.GetValues 分配）
            foreach (var layer in AllLayers)
            {
                _layerStacks[layer] = new Stack<UIViewBase>();
            }

            // 确保 UIManager 自身有正确的 UI 组件
            EnsureRootCanvas();

            // 如果没有设置层级容器，自动创建
            if (backgroundLayer == null)
                backgroundLayer = CreateLayerContainer("BackgroundLayer", (int)UILayer.Background);
            if (mainLayer == null)
                mainLayer = CreateLayerContainer("MainLayer", (int)UILayer.Main);
            if (screenLayer == null)
                screenLayer = CreateLayerContainer("ScreenLayer", (int)UILayer.Screen);
            if (popupLayer == null)
                popupLayer = CreateLayerContainer("PopupLayer", (int)UILayer.Popup);
            if (overlayLayer == null)
                overlayLayer = CreateLayerContainer("OverlayLayer", (int)UILayer.Overlay);
            if (topLayer == null)
                topLayer = CreateLayerContainer("TopLayer", (int)UILayer.Top);
            if (systemLayer == null)
                systemLayer = CreateLayerContainer("SystemLayer", (int)UILayer.System);
        }

        private Transform CreateLayerContainer(string name, int sortOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var rectTransform = go.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortOrder;

            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            return go.transform;
        }

        /// <summary>
        /// 确保 UIManager 有根 Canvas 组件（自动创建）
        /// </summary>
        private void EnsureRootCanvas()
        {
            // 检查是否已有 Canvas
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                // 添加 RectTransform（Canvas 需要）
                if (GetComponent<RectTransform>() == null)
                {
                    gameObject.AddComponent<RectTransform>();
                }

                // 创建根 Canvas
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 0;

                // 添加 CanvasScaler（自适应分辨率）
                var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                // 添加 GraphicRaycaster（UI 交互需要）
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                LogDebug("[UIManager] Auto-created root Canvas");
            }

            // 设置 UI Layer
            gameObject.layer = LayerMask.NameToLayer("UI");
        }

        #endregion

        #region Config Registration - 配置注册

        /// <summary>
        /// 注册视图配置
        /// </summary>
        public void RegisterView(UIViewConfig config)
        {
            if (string.IsNullOrEmpty(config.viewName))
            {
                Debug.LogError("[UIManager] View name is empty!");
                return;
            }

            if (_viewConfigs.ContainsKey(config.viewName))
            {
                LogWarning($"[UIManager] View '{config.viewName}' already registered, overwriting...");
            }

            _viewConfigs[config.viewName] = config;
        }

        /// <summary>
        /// 批量注册
        /// </summary>
        public void RegisterViews(IEnumerable<UIViewConfig> configs)
        {
            foreach (var config in configs)
            {
                RegisterView(config);
            }
        }

        /// <summary>
        /// 注销视图配置
        /// </summary>
        public void UnregisterView(string viewName)
        {
            _viewConfigs.Remove(viewName);
        }

        /// <summary>
        /// 从 Prefab 直接注册（无需配置文件）
        /// </summary>
        public void RegisterViewFromPrefab(GameObject prefab, UIViewConfig config = null)
        {
            var view = prefab.GetComponent<UIViewBase>();
            if (view == null)
            {
                Debug.LogError($"[UIManager] Prefab does not contain UIViewBase: {prefab.name}");
                return;
            }

            config ??= new UIViewConfig { viewName = view.ViewName };
            config.viewName = view.ViewName;
            config.prefab = prefab;

            RegisterView(config);
        }

        #endregion

        #region Open/Close - 打开/关闭

        /// <summary>
        /// 打开视图
        /// </summary>
        public async Task<UIViewBase> OpenAsync(string viewName, UIOpenArgs args = null)
        {
            args ??= new UIOpenArgs();

            // 检查配置
            if (!_viewConfigs.TryGetValue(viewName, out var config))
            {
                Debug.LogError($"[UIManager] View config not found: {viewName}");
                return null;
            }

            // 单例模式检查
            if (config.showMode == UIShowMode.Singleton && _viewInstances.ContainsKey(viewName))
            {
                var existing = _viewInstances[viewName];
                if (existing.IsVisible)
                {
                    LogWarning($"[UIManager] Singleton view '{viewName}' is already open");
                    return existing;
                }
            }

            // 获取或创建视图实例
            var view = await GetOrCreateViewAsync(viewName, config);
            if (view == null) return null;

            // 处理显示模式
            await HandleShowMode(view, config);

            // 显示遮罩
            if (config.showMask)
            {
                ShowMask(config.layer, config.maskColor, config.maskClickClose ? () => Close(viewName) : null);
            }

            // 请求暂停游戏
            if (config.pauseGame)
            {
                OnPauseRequested?.Invoke(true);
            }

            // 打开视图
            await view.InternalOpenAsync(args);

            // 入栈
            _layerStacks[config.layer].Push(view);
            _topView = view;

            OnViewOpened?.Invoke(viewName);
            LogDebug($"[UIManager] Opened: {viewName}");

            return view;
        }

        /// <summary>
        /// 打开视图（泛型版本）
        /// </summary>
        public async Task<T> OpenAsync<T>(UIOpenArgs args = null) where T : UIViewBase
        {
            var viewName = ViewNameCache<T>.Name;  // 使用缓存，避免重复分配
            var view = await OpenAsync(viewName, args);
            return view as T;
        }

        /// <summary>
        /// 同步打开（不等待动画）
        /// </summary>
        public void Open(string viewName, UIOpenArgs args = null)
        {
            _ = OpenAsync(viewName, args);
        }

        /// <summary>
        /// 同步打开（泛型）
        /// </summary>
        public void Open<T>(UIOpenArgs args = null) where T : UIViewBase
        {
            _ = OpenAsync<T>(args);
        }

        /// <summary>
        /// 关闭视图
        /// </summary>
        public async Task CloseAsync(string viewName, UICloseArgs args = null)
        {
            args ??= new UICloseArgs();

            if (!_viewInstances.TryGetValue(viewName, out var view))
            {
                LogWarning($"[UIManager] View not found: {viewName}");
                return;
            }

            if (!view.IsVisible && !args.Force)
            {
                return;
            }

            var config = view.Config;

            // 关闭视图
            await view.InternalCloseAsync(args);

            // 从栈中移除
            RemoveFromStack(view, config.layer);

            // 隐藏遮罩
            if (config.showMask)
            {
                HideMask(config.layer);
            }

            // 恢复游戏
            if (config.pauseGame)
            {
                OnPauseRequested?.Invoke(false);
            }

            // 处理关闭模式
            await HandleCloseMode(view, config);

            // 恢复上一个视图
            ResumeTopView(config.layer);

            OnViewClosed?.Invoke(viewName);
            LogDebug($"[UIManager] Closed: {viewName}");
        }

        /// <summary>
        /// 关闭视图（泛型版本）
        /// </summary>
        public async Task CloseAsync<T>(UICloseArgs args = null) where T : UIViewBase
        {
            await CloseAsync(ViewNameCache<T>.Name, args);
        }

        /// <summary>
        /// 同步关闭
        /// </summary>
        public void Close(string viewName, UICloseArgs args = null)
        {
            _ = CloseAsync(viewName, args);
        }

        /// <summary>
        /// 同步关闭（泛型）
        /// </summary>
        public void Close<T>(UICloseArgs args = null) where T : UIViewBase
        {
            _ = CloseAsync<T>(args);
        }

        /// <summary>
        /// 关闭最顶层视图
        /// </summary>
        public void CloseTop()
        {
            if (_topView != null && _topView.Config.allowESCClose)
            {
                Close(_topView.ViewName);
            }
        }

        /// <summary>
        /// 关闭所有视图
        /// </summary>
        public async Task CloseAllAsync(UILayer? layer = null)
        {
            if (layer.HasValue)
            {
                // 关闭指定层级
                var stack = _layerStacks[layer.Value];
                while (stack.Count > 0)
                {
                    var view = stack.Peek();
                    await CloseAsync(view.ViewName, UICloseArgs.Create());
                }
            }
            else
            {
                // 关闭所有层级
                foreach (var kvp in _layerStacks)
                {
                    while (kvp.Value.Count > 0)
                    {
                        var view = kvp.Value.Peek();
                        await CloseAsync(view.ViewName, UICloseArgs.Create());
                    }
                }
            }
        }

        /// <summary>
        /// 关闭所有视图（同步）
        /// </summary>
        public void CloseAll(UILayer? layer = null)
        {
            _ = CloseAllAsync(layer);
        }

        #endregion

        #region View Management - 视图管理

        private async Task<UIViewBase> GetOrCreateViewAsync(string viewName, UIViewConfig config)
        {
            // 检查缓存
            if (_viewInstances.TryGetValue(viewName, out var cachedView))
            {
                return cachedView;
            }

            // 加载资源
            var container = GetLayerContainer(config.layer);
            GameObject prefab = null;

#if ZEROENGINE_ADDRESSABLES
            // 优先使用 AssetReference (Addressables)
            if (config.prefabReference != null && config.prefabReference.RuntimeKeyIsValid())
            {
                prefab = await LoadViewPrefabAsync(config.prefabReference);
            }
            else
#endif
            if (config.prefab != null)
            {
                // 使用直接引用的 Prefab
                prefab = config.prefab;
            }
            else if (!string.IsNullOrEmpty(config.resourcePath))
            {
                Debug.LogError($"[UIManager] Resources path loading is not supported for view '{viewName}'. Assign a prefab reference or Addressables reference.");
            }

            if (prefab == null)
            {
                Debug.LogError($"[UIManager] Failed to load view prefab for: {viewName}");
                return null;
            }

            // 实例化
            var go = Instantiate(prefab, container);
            go.name = viewName;

            var view = go.GetComponent<UIViewBase>();
            if (view == null)
            {
                Debug.LogError($"[UIManager] View component not found on: {viewName}");
                Destroy(go);
                return null;
            }

            // 初始化
            view.InternalInit(config);

            // 缓存
            if (config.cache)
            {
                _viewInstances[viewName] = view;
            }

            return view;
        }

#if ZEROENGINE_ADDRESSABLES
        /// <summary>
        /// 使用 Addressables AssetReference 加载 Prefab
        /// </summary>
        private async Task<GameObject> LoadViewPrefabAsync(AssetReferenceGameObject prefabRef)
        {
            if (prefabRef == null || !prefabRef.RuntimeKeyIsValid())
                return null;

            var handle = prefabRef.LoadAssetAsync<GameObject>();
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                return handle.Result;
            }

            Debug.LogError($"[UIManager] Addressables load failed for: {prefabRef.RuntimeKey}");
            return null;
        }
#endif

        private Transform GetLayerContainer(UILayer layer)
        {
            return layer switch
            {
                UILayer.Background => backgroundLayer,
                UILayer.Main => mainLayer,
                UILayer.Screen => screenLayer,
                UILayer.Popup => popupLayer,
                UILayer.Overlay => overlayLayer,
                UILayer.Top => topLayer,
                UILayer.System => systemLayer,
                _ => screenLayer
            };
        }

        private async Task HandleShowMode(UIViewBase view, UIViewConfig config)
        {
            switch (config.showMode)
            {
                case UIShowMode.HideOthers:
                    // 隐藏同层其他视图
                    foreach (var v in _layerStacks[config.layer])
                    {
                        if (v != view && v.IsVisible)
                        {
                            v.InternalPause();
                        }
                    }
                    break;

                case UIShowMode.Stack:
                    // 暂停栈顶视图
                    if (_layerStacks[config.layer].Count > 0)
                    {
                        var top = _layerStacks[config.layer].Peek();
                        if (top != view)
                        {
                            top.InternalPause();
                        }
                    }
                    break;
            }

            await Task.CompletedTask;
        }

        private async Task HandleCloseMode(UIViewBase view, UIViewConfig config)
        {
            switch (config.closeMode)
            {
                case UICloseMode.Hide:
                    // 保留实例，只隐藏
                    break;

                case UICloseMode.Destroy:
                    // 销毁实例
                    _viewInstances.Remove(view.ViewName);
                    Destroy(view.gameObject);
                    break;

                case UICloseMode.Pool:
                    // 回池（需要实现对象池）
                    view.gameObject.SetActive(false);
                    break;
            }

            await Task.CompletedTask;
        }

        private void RemoveFromStack(UIViewBase view, UILayer layer)
        {
            var stack = _layerStacks[layer];

            // 使用静态复用列表，避免每次分配临时 Stack
            TempViewList.Clear();

            // 收集需要保留的视图
            while (stack.Count > 0)
            {
                var v = stack.Pop();
                if (v != view)
                {
                    TempViewList.Add(v);
                }
            }

            // 逆序压回栈（保持原顺序）
            for (int i = TempViewList.Count - 1; i >= 0; i--)
            {
                stack.Push(TempViewList[i]);
            }

            // 更新顶层视图
            UpdateTopView();
        }

        private void UpdateTopView()
        {
            _topView = null;

            // 使用预排序的缓存数组，从高到低遍历层级（零 GC 分配）
            foreach (var layer in LayersSortedDesc)
            {
                if (_layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0)
                {
                    _topView = stack.Peek();
                    return;
                }
            }
        }

        private void ResumeTopView(UILayer layer)
        {
            if (_layerStacks[layer].Count > 0)
            {
                var top = _layerStacks[layer].Peek();
                top.InternalResume();
            }
        }

        #endregion

        #region Mask - 遮罩管理

        private void ShowMask(UILayer layer, Color color, Action onClick)
        {
            if (maskPrefab == null) return;

            if (!_maskInstances.TryGetValue(layer, out var mask))
            {
                mask = Instantiate(maskPrefab, GetLayerContainer(layer));
                _maskInstances[layer] = mask;
            }

            mask.transform.SetAsLastSibling();
            mask.transform.SetSiblingIndex(mask.transform.GetSiblingIndex() - 1); // 在视图下方

            var image = mask.GetComponent<UnityEngine.UI.Image>();
            if (image != null) image.color = color;

            var button = mask.GetComponent<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                if (onClick != null) button.onClick.AddListener(() => onClick());
            }

            mask.SetActive(true);
        }

        private void HideMask(UILayer layer)
        {
            if (_maskInstances.TryGetValue(layer, out var mask))
            {
                mask.SetActive(false);
            }
        }

        #endregion

        #region Query - 查询

        /// <summary>
        /// 获取视图实例
        /// </summary>
        public T GetView<T>() where T : UIViewBase
        {
            if (_viewInstances.TryGetValue(ViewNameCache<T>.Name, out var view))
            {
                return view as T;
            }
            return null;
        }

        /// <summary>
        /// 获取视图实例
        /// </summary>
        public UIViewBase GetView(string viewName)
        {
            if (_viewInstances.TryGetValue(viewName, out var view))
            {
                return view;
            }
            return null;
        }

        /// <summary>
        /// 检查视图是否打开
        /// </summary>
        public bool IsOpen(string viewName)
        {
            if (_viewInstances.TryGetValue(viewName, out var view))
            {
                return view.IsVisible;
            }
            return false;
        }

        /// <summary>
        /// 检查视图是否打开（泛型）
        /// </summary>
        public bool IsOpen<T>() where T : UIViewBase
        {
            return IsOpen(ViewNameCache<T>.Name);
        }

        /// <summary>
        /// 获取最顶层视图
        /// </summary>
        public UIViewBase GetTopView() => _topView;

        /// <summary>
        /// 获取指定层级的栈顶视图
        /// </summary>
        public UIViewBase GetTopView(UILayer layer)
        {
            if (_layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0)
            {
                return stack.Peek();
            }
            return null;
        }

        /// <summary>
        /// 获取指定层级的所有视图
        /// </summary>
        public IEnumerable<UIViewBase> GetViewsInLayer(UILayer layer)
        {
            if (_layerStacks.TryGetValue(layer, out var stack))
            {
                return stack;
            }
            return Array.Empty<UIViewBase>();
        }

        #endregion

        #region Input Handling - 输入处理

        private void HandleCancelInput()
        {
            // 通知外部
            OnCancelInputRequested?.Invoke();
            // ESC键关闭顶层
            CloseTop();
        }

        /// <summary>
        /// 外部触发取消输入（用于自定义输入系统）
        /// </summary>
        public void TriggerCancelInput()
        {
            HandleCancelInput();
        }

        #endregion

        #region Preload - 预加载

        /// <summary>
        /// 预加载视图（不显示）
        /// </summary>
        public async Task PreloadAsync(string viewName)
        {
            if (!_viewConfigs.TryGetValue(viewName, out var config))
            {
                LogWarning($"[UIManager] View config not found for preload: {viewName}");
                return;
            }

            if (_viewInstances.ContainsKey(viewName))
            {
                return; // 已加载
            }

            var view = await GetOrCreateViewAsync(viewName, config);
            if (view != null)
            {
                view.gameObject.SetActive(false);
                LogDebug($"[UIManager] Preloaded: {viewName}");
            }
        }

        /// <summary>
        /// 预加载视图（泛型）
        /// </summary>
        public async Task PreloadAsync<T>() where T : UIViewBase
        {
            await PreloadAsync(ViewNameCache<T>.Name);
        }

        /// <summary>
        /// 批量预加载
        /// </summary>
        public async Task PreloadAsync(params string[] viewNames)
        {
            var tasks = new List<Task>();
            foreach (var viewName in viewNames)
            {
                tasks.Add(PreloadAsync(viewName));
            }
            await Task.WhenAll(tasks);
        }

        #endregion
    }
}
