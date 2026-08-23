using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ZeroEngine.Core;
using Stopwatch = System.Diagnostics.Stopwatch;

#if ZEROENGINE_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace ZeroEngine.UI
{
    /// <summary>
    /// 通用 UI 管理器：维护层级、窗口栈、遮罩、资源缓存和请求顺序。
    /// 输入由宿主输入系统通过 TriggerCancelInput 或 TryHandleCancelInput 注入。
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        private static readonly UILayer[] AllLayers = (UILayer[])Enum.GetValues(typeof(UILayer));
        private static readonly UILayer[] LayersSortedDesc = CreateSortedLayersArray();
        private static readonly List<UIViewBase> TempViewList = new(8);

        private static UILayer[] CreateSortedLayersArray()
        {
            var layers = (UILayer[])Enum.GetValues(typeof(UILayer));
            Array.Sort(layers, (a, b) => ((int)b).CompareTo((int)a));
            return layers;
        }

        private static class ViewNameCache<T> where T : UIViewBase
        {
            public static readonly string Name = typeof(T).Name;
        }

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
        [Tooltip("Enable the host-triggered cancel request.")]
        [SerializeField] private bool enableESCClose = true;

        private readonly Dictionary<string, UIViewConfig> _viewConfigs = new();
        private readonly Dictionary<string, UIViewBase> _viewInstances = new();
        private readonly Dictionary<UILayer, Stack<UIViewBase>> _layerStacks = new();
        private readonly Dictionary<UILayer, GameObject> _maskInstances = new();
        private readonly Dictionary<UILayer, UIViewBase> _maskOwners = new();

#if ZEROENGINE_ADDRESSABLES
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _prefabHandles = new();
        private readonly Dictionary<string, string> _viewHandleKeys = new();
#endif

        private readonly Dictionary<string, PendingOpenRequest> _pendingOpenTasks = new();
        private readonly Dictionary<string, PendingCloseRequest> _pendingCloseTasks = new();
        private int _sessionViewGeneration;
        private bool _isDestroying;
        private UIViewBase _topView;
        private IUIManagerHooks _hooks;

        private sealed class PendingOpenRequest
        {
            public PendingOpenRequest(bool acceptsImplicitJoin, int sessionViewGeneration)
            {
                AcceptsImplicitJoin = acceptsImplicitJoin;
                SessionViewGeneration = sessionViewGeneration;
                Completion = new TaskCompletionSource<UIViewBase>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public bool AcceptsImplicitJoin { get; }
            public int SessionViewGeneration { get; }
            public TaskCompletionSource<UIViewBase> Completion { get; }
        }

        private sealed class PendingCloseRequest
        {
            public TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public event Action<string> OnViewOpened;
        public event Action<string> OnViewClosed;
        public event Action OnCancelInputRequested;
        public event Action<bool> OnPauseRequested;

        /// <summary>暂停和日志的项目无关集成 hook。</summary>
        public IUIManagerHooks Hooks
        {
            get => _hooks;
            set => _hooks = value;
        }

        public void SetHooks(IUIManagerHooks hooks)
        {
            Hooks = hooks;
        }

        public bool HasAnyViewOpen
        {
            get
            {
                foreach (var stack in _layerStacks.Values)
                {
                    if (stack.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int OpenViewCount
        {
            get
            {
                var count = 0;
                foreach (var stack in _layerStacks.Values)
                {
                    count += stack.Count;
                }

                return count;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                return;
            }

            InitializeLayers();
        }

        protected override void OnDestroy()
        {
            _isDestroying = true;
            _sessionViewGeneration++;
            CompletePendingRequests();
            ReleasePrefabHandles();
            base.OnDestroy();
        }

        private void InitializeLayers()
        {
            _layerStacks.Clear();
            foreach (var layer in AllLayers)
            {
                _layerStacks[layer] = new Stack<UIViewBase>();
            }

            EnsureRootCanvas();
            backgroundLayer ??= CreateLayerContainer("BackgroundLayer", (int)UILayer.Background);
            mainLayer ??= CreateLayerContainer("MainLayer", (int)UILayer.Main);
            screenLayer ??= CreateLayerContainer("ScreenLayer", (int)UILayer.Screen);
            popupLayer ??= CreateLayerContainer("PopupLayer", (int)UILayer.Popup);
            overlayLayer ??= CreateLayerContainer("OverlayLayer", (int)UILayer.Overlay);
            topLayer ??= CreateLayerContainer("TopLayer", (int)UILayer.Top);
            systemLayer ??= CreateLayerContainer("SystemLayer", (int)UILayer.System);
        }

        private Transform CreateLayerContainer(string name, int sortOrder)
        {
            var go = UIRuntimeObjectFactory.CreateFallbackObject(name, transform, typeof(RectTransform));
            var rectTransform = go.GetComponent<RectTransform>();
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

        private void EnsureRootCanvas()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                if (GetComponent<RectTransform>() == null)
                {
                    gameObject.AddComponent<RectTransform>();
                }

                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 0;

                var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                gameObject.layer = uiLayer;
            }
        }

        #region Registration

        public void RegisterView(UIViewConfig config)
        {
            if (config == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewNameEmpty());
                return;
            }

            var viewNameIsEmpty = string.IsNullOrEmpty(config.viewName);
            var alreadyRegistered = !viewNameIsEmpty && _viewConfigs.ContainsKey(config.viewName);
            var registrationDecision = UIManagerViewRegistrationPolicy.Resolve(
                viewNameIsEmpty: viewNameIsEmpty,
                alreadyRegistered: alreadyRegistered);

            if (registrationDecision.LogViewNameEmpty)
            {
                LogUIManager(UIManagerLogPolicy.ViewNameEmpty());
            }

            if (registrationDecision.ReturnAfterEmptyName)
            {
                return;
            }

            if (registrationDecision.LogViewAlreadyRegistered)
            {
                LogUIManager(UIManagerLogPolicy.ViewAlreadyRegistered(config.viewName));
            }

            if (registrationDecision.StoreConfig)
            {
                _viewConfigs[config.viewName] = config;
            }
        }

        public void RegisterViews(IEnumerable<UIViewConfig> configs)
        {
            if (configs == null)
            {
                return;
            }

            foreach (var config in configs)
            {
                RegisterView(config);
            }
        }

        public void UnregisterView(string viewName)
        {
            _viewConfigs.Remove(viewName);
        }

        public void RegisterViewFromPrefab(GameObject prefab, UIViewConfig config = null)
        {
            if (prefab == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewPrefabLoadFailed(string.Empty));
                return;
            }

            var view = prefab.GetComponent<UIViewBase>();
            if (view == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewComponentNotFound(prefab.name));
                return;
            }

            config ??= new UIViewConfig { viewName = view.ViewName };
            config.viewName = view.ViewName;
            config.prefab = prefab;
            RegisterView(config);
        }

        private void LogUIManager(UIManagerLogDecision decision)
        {
            if (!decision.ShouldLog)
            {
                return;
            }

            if (_hooks != null)
            {
                _hooks.Log(decision.Level, decision.Message);
                return;
            }

            var message = $"[UIManager] {decision.Message}";
            switch (decision.Level)
            {
                case UIManagerLogLevel.Error:
                    Debug.LogError(message, this);
                    break;
                case UIManagerLogLevel.Warning:
                    Debug.LogWarning(message, this);
                    break;
                case UIManagerLogLevel.Info:
                    Debug.Log(message, this);
                    break;
            }
        }

        private void LogInfo(string message)
        {
            LogUIManager(UIManagerLogPolicy.Info(message));
        }

        private void RequestPause(bool pause)
        {
            OnPauseRequested?.Invoke(pause);
            _hooks?.RequestPause(pause);
        }

        #endregion

        #region Open and close

        public Task<UIViewBase> OpenAsync(string viewName, UIOpenArgs args = null)
        {
            return OpenAsync(viewName, args, _sessionViewGeneration);
        }

        private async Task<UIViewBase> OpenAsync(
            string viewName,
            UIOpenArgs args,
            int requestSessionViewGeneration)
        {
            if (string.IsNullOrWhiteSpace(viewName) || _isDestroying)
            {
                return null;
            }

            if (_pendingCloseTasks.TryGetValue(viewName, out var pendingClose))
            {
                try
                {
                    await pendingClose.Completion.Task;
                }
                catch
                {
                    // The next open still owns an independent recovery attempt.
                }

                return await OpenAsync(viewName, args, requestSessionViewGeneration);
            }

            var isImplicitRequest = args == null;
            if (_pendingOpenTasks.TryGetValue(viewName, out var pendingRequest))
            {
                if (isImplicitRequest
                    && pendingRequest.AcceptsImplicitJoin
                    && pendingRequest.SessionViewGeneration == requestSessionViewGeneration)
                {
                    return await pendingRequest.Completion.Task;
                }

                try
                {
                    await pendingRequest.Completion.Task;
                }
                catch
                {
                    // Explicit arguments must get their own serialized attempt.
                }

                return await OpenAsync(viewName, args, requestSessionViewGeneration);
            }

            var currentRequest = new PendingOpenRequest(isImplicitRequest, requestSessionViewGeneration);
            _pendingOpenTasks[viewName] = currentRequest;
            _ = CompleteOpenAsync();
            return await currentRequest.Completion.Task;

            async Task CompleteOpenAsync()
            {
                try
                {
                    currentRequest.Completion.TrySetResult(await OpenCoreAsync());
                }
                catch (Exception exception)
                {
                    currentRequest.Completion.TrySetException(exception);
                }
                finally
                {
                    if (_pendingOpenTasks.TryGetValue(viewName, out var registeredRequest)
                        && ReferenceEquals(registeredRequest, currentRequest))
                    {
                        _pendingOpenTasks.Remove(viewName);
                    }
                }
            }

            async Task<UIViewBase> OpenCoreAsync()
            {
                args ??= new UIOpenArgs();
                if (!_viewConfigs.TryGetValue(viewName, out var config) || config == null)
                {
                    LogUIManager(UIManagerLogPolicy.ViewConfigNotFound(viewName));
                    return null;
                }

                if (!CanContinueViewOperation(config, requestSessionViewGeneration))
                {
                    return null;
                }

                var isSingletonRequest = config.showMode == UIShowMode.Singleton;
                if (isSingletonRequest && _viewInstances.TryGetValue(viewName, out var existing) && existing != null)
                {
                    var openRequestDecision = UIManagerOpenRequestPolicy.Resolve(
                        config.showMode,
                        hasExistingInstance: true,
                        existingInstanceVisible: existing.IsVisible);
                    if (openRequestDecision.ReturnExistingVisibleSingleton)
                    {
                        LogUIManager(UIManagerLogPolicy.SingletonViewAlreadyOpen(viewName));
                        return existing;
                    }
                }

                var view = await GetOrCreateViewAsync(viewName, config, requestSessionViewGeneration);
                if (view == null)
                {
                    return null;
                }

                if (!CanContinueViewOperation(config, requestSessionViewGeneration))
                {
                    ReleaseSupersededOpen(viewName, view);
                    return null;
                }

                await HandleShowMode(view, config);
                if (!CanContinueViewOperation(config, requestSessionViewGeneration))
                {
                    ReleaseSupersededOpen(viewName, view);
                    return null;
                }

                var modalSideEffectDecision = UIManagerModalSideEffectPolicy.Resolve(
                    showMask: config.showMask,
                    pauseGame: config.pauseGame);

                if (modalSideEffectDecision.ShowMask)
                {
                    ShowMask(config.layer, config.maskColor, config.maskClickClose ? () => Close(view) : null, view);
                }

                if (modalSideEffectDecision.PauseGame)
                {
                    RequestPause(true);
                }

                await view.InternalOpenAsync(args);
                if (view == null
                    || !view.IsVisible
                    || !CanContinueViewOperation(config, requestSessionViewGeneration))
                {
                    ReleaseSupersededOpen(viewName, view);
                    return null;
                }

                if (!_layerStacks[config.layer].Contains(view))
                {
                    _layerStacks[config.layer].Push(view);
                }

                _topView = view;
                OnViewOpened?.Invoke(viewName);
                LogInfo($"Opened: {viewName}");
                return view;
            }
        }

        public async Task<T> OpenAsync<T>(UIOpenArgs args = null) where T : UIViewBase
        {
            var view = await OpenAsync(ViewNameCache<T>.Name, args);
            return view as T;
        }

        public void Open(string viewName, UIOpenArgs args = null)
        {
            _ = OpenAsync(viewName, args);
        }

        public void Open<T>(UIOpenArgs args = null) where T : UIViewBase
        {
            _ = OpenAsync<T>(args);
        }

        public async Task CloseAsync(string viewName, UICloseArgs args = null)
        {
            await QueueCloseAsync(viewName, null, args);
        }

        public async Task CloseAsync(UIViewBase view, UICloseArgs args = null)
        {
            if (view != null)
            {
                await QueueCloseAsync(view.ViewName, view, args);
            }
        }

        private async Task QueueCloseAsync(string viewName, UIViewBase requestedView, UICloseArgs args)
        {
            if (string.IsNullOrWhiteSpace(viewName) || _isDestroying)
            {
                return;
            }

            if (_pendingCloseTasks.TryGetValue(viewName, out var pendingRequest))
            {
                await pendingRequest.Completion.Task;
                return;
            }

            var currentRequest = new PendingCloseRequest();
            _pendingCloseTasks[viewName] = currentRequest;
            _ = CompleteCloseAsync();
            await currentRequest.Completion.Task;

            async Task CompleteCloseAsync()
            {
                try
                {
                    if (_pendingOpenTasks.TryGetValue(viewName, out var pendingOpen))
                    {
                        try
                        {
                            await pendingOpen.Completion.Task;
                        }
                        catch
                        {
                            // Resolve the final registered instance even after a failed open.
                        }
                    }

                    var targetView = requestedView;
                    if (targetView == null && !_viewInstances.TryGetValue(viewName, out targetView))
                    {
                        LogUIManager(UIManagerLogPolicy.ViewNotFound(viewName));
                        currentRequest.Completion.TrySetResult(true);
                        return;
                    }

                    await CloseCoreAsync(targetView, args);
                    currentRequest.Completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    currentRequest.Completion.TrySetException(exception);
                }
                finally
                {
                    if (_pendingCloseTasks.TryGetValue(viewName, out var registeredRequest)
                        && ReferenceEquals(registeredRequest, currentRequest))
                    {
                        _pendingCloseTasks.Remove(viewName);
                    }
                }
            }
        }

        private async Task CloseCoreAsync(UIViewBase view, UICloseArgs args)
        {
            if (view == null)
            {
                return;
            }

            args ??= new UICloseArgs();
            if (!view.IsVisible && view.State != UIViewState.Paused && !args.Force)
            {
                return;
            }

            var viewName = view.ViewName;
            var config = view.Config;
            if (config == null)
            {
                return;
            }

            var requestSessionViewGeneration = _sessionViewGeneration;
            await view.InternalCloseAsync(args);
            if (view == null || !CanContinueViewOperation(config, requestSessionViewGeneration))
            {
                return;
            }

            RemoveFromStack(view, config.layer);
            var modalSideEffectDecision = UIManagerModalSideEffectPolicy.Resolve(
                showMask: config.showMask,
                pauseGame: config.pauseGame);
            if (modalSideEffectDecision.HideMask)
            {
                RefreshMask(config.layer);
            }

            if (modalSideEffectDecision.ResumeGame)
            {
                RequestPause(false);
            }

            await HandleCloseMode(view, config);
            ResumeTopView(config.layer);

            OnViewClosed?.Invoke(viewName);
            LogInfo($"Closed: {viewName}");
        }

        public async Task CloseAsync<T>(UICloseArgs args = null) where T : UIViewBase
        {
            await CloseAsync(ViewNameCache<T>.Name, args);
        }

        public void Close(string viewName, UICloseArgs args = null)
        {
            _ = CloseAsync(viewName, args);
        }

        public void Close(UIViewBase view, UICloseArgs args = null)
        {
            _ = CloseAsync(view, args);
        }

        public void Close<T>(UICloseArgs args = null) where T : UIViewBase
        {
            _ = CloseAsync<T>(args);
        }

        public void CloseTop()
        {
            var topView = _topView;
            var closeTopDecision = UIManagerCloseTopPolicy.Resolve(
                hasTopView: topView != null,
                allowEscClose: topView != null && topView.Config != null && topView.Config.allowESCClose);
            if (closeTopDecision.CloseTopView)
            {
                Close(topView);
            }
        }

        public async Task CloseAllAsync(UILayer? layer = null)
        {
            if (layer.HasValue)
            {
                var stack = _layerStacks[layer.Value];
                while (stack.Count > 0)
                {
                    await CloseAsync(stack.Peek(), UICloseArgs.Create());
                }

                return;
            }

            foreach (var pair in _layerStacks)
            {
                while (pair.Value.Count > 0)
                {
                    await CloseAsync(pair.Value.Peek(), UICloseArgs.Create());
                }
            }
        }

        public void CloseAll(UILayer? layer = null)
        {
            _ = CloseAllAsync(layer);
        }

        #endregion

        #region View and prefab management

        private async Task<UIViewBase> GetOrCreateViewAsync(
            string viewName,
            UIViewConfig config,
            int requestSessionViewGeneration)
        {
            if (_viewInstances.TryGetValue(viewName, out var cachedView))
            {
                if (cachedView != null)
                {
                    return cachedView;
                }

                _viewInstances.Remove(viewName);
            }

            var container = GetLayerContainer(config.layer);
            GameObject prefab = null;

#if ZEROENGINE_ADDRESSABLES
            var hasPrefabReference = config.prefabReference != null;
            var runtimeKeyIsValid = hasPrefabReference && config.prefabReference.RuntimeKeyIsValid();
            var prefabReferenceDecision = UIManagerPrefabReferencePolicy.Resolve(
                hasPrefabReference: hasPrefabReference,
                runtimeKeyIsValid: runtimeKeyIsValid);
            if (prefabReferenceDecision.LoadPrefab)
            {
                _viewHandleKeys[viewName] = config.prefabReference.RuntimeKey.ToString();
                prefab = await LoadViewPrefabAsync(config.prefabReference);
            }
#endif

            if (prefab == null)
            {
                prefab = config.prefab;
            }

            if (prefab == null && !string.IsNullOrEmpty(config.resourcePath))
            {
                prefab = await LoadViewPrefabFromResourcesAsync(config.resourcePath);
            }

            if (!CanContinueViewOperation(config, requestSessionViewGeneration))
            {
                ReleasePrefabHandleForView(viewName);
                return null;
            }

            if (prefab == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewPrefabLoadFailed(viewName));
                return null;
            }

            var go = CreateRuntimeGameObject(prefab, container);
            go.name = viewName;
            var view = go.GetComponent<UIViewBase>();
            if (view == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewComponentNotFound(viewName));
                DestroyRuntimeGameObject(go);
                ReleasePrefabHandleForView(viewName);
                return null;
            }

            view.InternalInit(config);
            _viewInstances[viewName] = view;
            return view;
        }

        private Task<GameObject> LoadViewPrefabFromResourcesAsync(string assetPath)
        {
            var completion = new TaskCompletionSource<GameObject>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var request = Resources.LoadAsync<GameObject>(assetPath);
            request.completed += _ => completion.TrySetResult(request.asset as GameObject);
            return completion.Task;
        }

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

#if ZEROENGINE_ADDRESSABLES
        private async Task<GameObject> LoadViewPrefabAsync(AssetReferenceGameObject prefabRef)
        {
            var hasPrefabReference = prefabRef != null;
            var runtimeKeyIsValid = hasPrefabReference && prefabRef.RuntimeKeyIsValid();
            var prefabReferenceDecision = UIManagerPrefabReferencePolicy.Resolve(
                hasPrefabReference: hasPrefabReference,
                runtimeKeyIsValid: runtimeKeyIsValid);
            if (!prefabReferenceDecision.LoadPrefab)
            {
                return null;
            }

            var handleKey = prefabRef.RuntimeKey.ToString();
            if (TryGetCachedPrefab(handleKey, out var cachedPrefab))
            {
                return cachedPrefab;
            }

            var existingPrefab = await TryGetPrefabFromExistingReferenceHandleAsync(prefabRef, handleKey);
            if (existingPrefab != null)
            {
                return existingPrefab;
            }

            AsyncOperationHandle<GameObject> handle = default;
            var loadStartTimestamp = Stopwatch.GetTimestamp();
            var loadSucceeded = false;
            try
            {
                try
                {
                    if (_hooks is IUIManagerPrefabLoadHooks prefabLoadHooks)
                    {
                        await prefabLoadHooks.PreparePrefabLoadAsync(handleKey);
                        if (_isDestroying)
                        {
                            return null;
                        }

                        existingPrefab = await TryGetPrefabFromExistingReferenceHandleAsync(prefabRef, handleKey);
                        if (_isDestroying)
                        {
                            return null;
                        }

                        if (existingPrefab != null)
                        {
                            loadSucceeded = true;
                            return existingPrefab;
                        }
                    }

                    handle = prefabRef.LoadAssetAsync<GameObject>();
                    await handle.Task;
                    if (_isDestroying)
                    {
                        ReleaseAddressableHandleIfValid(handle);
                        return null;
                    }

                    var loadOperationSucceeded = handle.Status == AsyncOperationStatus.Succeeded;
                    var loadResultDecision = UIManagerPrefabLoadResultPolicy.Resolve(
                        loadSucceeded: loadOperationSucceeded);
                    if (loadResultDecision.CacheLoadedHandle)
                    {
                        _prefabHandles[handleKey] = handle;
                    }

                    if (loadResultDecision.MarkLoadSucceeded)
                    {
                        loadSucceeded = true;
                    }

                    if (loadResultDecision.UseLoadedPrefab)
                    {
                        return handle.Result;
                    }
                }
                catch (Exception exception)
                {
                    LogUIManager(UIManagerLogPolicy.AddressablesLoadFailed(
                        prefabRef.RuntimeKey.ToString(), exception.Message));
                }

                LogUIManager(UIManagerLogPolicy.AddressablesLoadFailed(prefabRef.RuntimeKey.ToString()));
                var failedHandleIsValid = handle.IsValid();
                var failureReleaseDecision = UIManagerPrefabLoadFailureReleasePolicy.Resolve(
                    handleIsValid: failedHandleIsValid);
                if (failureReleaseDecision.ReleaseHandle)
                {
                    Addressables.Release(handle);
                }

                return null;
            }
            finally
            {
                if (_hooks is IUIManagerPrefabLoadHooks prefabLoadHooks)
                {
                    var elapsedTicks = Stopwatch.GetTimestamp() - loadStartTimestamp;
                    var duration = TimeSpan.FromSeconds((double)elapsedTicks / Stopwatch.Frequency);
                    prefabLoadHooks.RecordPrefabLoad(handleKey, duration, loadSucceeded);
                }
            }
        }

        private bool TryGetCachedPrefab(string handleKey, out GameObject prefab)
        {
            prefab = null;
            var hasHandle = _prefabHandles.TryGetValue(handleKey, out var cachedHandle);
            var handleIsValid = hasHandle && cachedHandle.IsValid();
            var loadSucceeded = handleIsValid && cachedHandle.Status == AsyncOperationStatus.Succeeded;
            var cachedPrefab = loadSucceeded ? cachedHandle.Result : null;
            var cacheDecision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: hasHandle,
                handleIsValid: handleIsValid,
                loadSucceeded: loadSucceeded,
                hasPrefabResult: cachedPrefab != null);
            if (!cacheDecision.UseCachedPrefab)
            {
                return false;
            }

            prefab = cachedPrefab;
            return true;
        }

        private async Task<GameObject> TryGetPrefabFromExistingReferenceHandleAsync(
            AssetReferenceGameObject prefabRef,
            string handleKey)
        {
            var existingHandle = prefabRef.OperationHandle;
            var handleIsValid = existingHandle.IsValid();
            var existingHandleDecision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: handleIsValid,
                loadSucceeded: false,
                hasPrefabResult: false);
            if (!existingHandleDecision.AwaitExistingHandle)
            {
                return null;
            }

            try
            {
                await existingHandle.Task;
            }
            catch
            {
                return null;
            }

            if (_isDestroying)
            {
                ReleaseAddressableHandleIfValid(existingHandle);
                return null;
            }

            var loadSucceeded = existingHandle.Status == AsyncOperationStatus.Succeeded;
            var existingPrefab = loadSucceeded ? existingHandle.Result as GameObject : null;
            existingHandleDecision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: handleIsValid,
                loadSucceeded: loadSucceeded,
                hasPrefabResult: existingPrefab != null);
            if (!existingHandleDecision.UseExistingPrefab)
            {
                return null;
            }

            _prefabHandles[handleKey] = existingHandle.Convert<GameObject>();
            return existingPrefab;
        }

        private static void ReleaseAddressableHandleIfValid(AsyncOperationHandle handle)
        {
            var handleIsValid = handle.IsValid();
            var releaseDecision = UIManagerPrefabHandlesReleasePolicy.Resolve(
                handleIsValid: handleIsValid);
            if (releaseDecision.ReleaseHandle)
            {
                Addressables.Release(handle);
            }
        }
#endif

        private void ReleasePrefabHandleForView(string viewName)
        {
#if ZEROENGINE_ADDRESSABLES
            if (!_viewHandleKeys.TryGetValue(viewName, out var handleKey))
            {
                return;
            }

            var handleUsedByOtherView = IsPrefabHandleUsedByOtherView(viewName, handleKey);
            var hasCachedHandle = _prefabHandles.TryGetValue(handleKey, out var handle);
            var cachedHandleIsValid = hasCachedHandle && handle.IsValid();
            var releaseDecision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: true,
                handleUsedByOtherView: handleUsedByOtherView,
                hasCachedHandle: hasCachedHandle,
                cachedHandleIsValid: cachedHandleIsValid);

            if (releaseDecision.RemoveViewHandleKey)
            {
                _viewHandleKeys.Remove(viewName);
            }

            if (releaseDecision.RemoveCachedHandle)
            {
                _prefabHandles.Remove(handleKey);
            }

            if (releaseDecision.ReleaseCachedHandle)
            {
                Addressables.Release(handle);
            }
#endif
        }

#if ZEROENGINE_ADDRESSABLES
        private bool IsPrefabHandleUsedByOtherView(string viewName, string handleKey)
        {
            foreach (var viewHandleKey in _viewHandleKeys)
            {
                if (viewHandleKey.Key != viewName && viewHandleKey.Value == handleKey)
                {
                    return true;
                }
            }

            return false;
        }
#endif

        private void ReleasePrefabHandles()
        {
#if ZEROENGINE_ADDRESSABLES
            foreach (var handle in _prefabHandles.Values)
            {
                var handleIsValid = handle.IsValid();
                var releaseDecision = UIManagerPrefabHandlesReleasePolicy.Resolve(
                    handleIsValid: handleIsValid);
                if (releaseDecision.ReleaseHandle)
                {
                    Addressables.Release(handle);
                }
            }

            _prefabHandles.Clear();
            _viewHandleKeys.Clear();
#endif
        }

        public void ReleaseSessionViews()
        {
            _sessionViewGeneration++;
            var viewNames = new List<string>(_viewInstances.Keys);
            foreach (var viewName in viewNames)
            {
                if (!_viewInstances.TryGetValue(viewName, out var view) || view == null)
                {
                    continue;
                }

                var config = view.Config;
                if (config == null)
                {
                    continue;
                }

                var releaseDecision = UIManagerSessionViewReleasePolicy.Resolve(
                    hasView: true,
                    isResident: config.lifetime == UIViewLifetime.Resident,
                    showMask: config.showMask);
                if (!releaseDecision.ReleaseView)
                {
                    continue;
                }

                if (releaseDecision.RemoveInstance)
                {
                    _viewInstances.Remove(viewName);
                }

                if (releaseDecision.RemoveFromStack)
                {
                    RemoveFromStack(view, config.layer);
                }

                if (releaseDecision.HideMask)
                {
                    RefreshMask(config.layer);
                }

                if (releaseDecision.DestroyInstance)
                {
                    DestroyViewInstance(view);
                }

                if (releaseDecision.ReleasePrefabHandle)
                {
                    ReleasePrefabHandleForView(viewName);
                }
            }

            UpdateTopView();
        }

        private bool CanContinueViewOperation(UIViewConfig config, int requestSessionViewGeneration)
        {
            return !_isDestroying
                && config != null
                && (config.lifetime == UIViewLifetime.Resident
                    || requestSessionViewGeneration == _sessionViewGeneration);
        }

        private void ReleaseSupersededOpen(string viewName, UIViewBase view)
        {
            if (_viewInstances.TryGetValue(viewName, out var registeredView)
                && ReferenceEquals(registeredView, view))
            {
                _viewInstances.Remove(viewName);
                if (view != null)
                {
                    DestroyViewInstance(view);
                }
            }

            if (view != null && view.Config != null)
            {
                if (view.Config.showMask)
                {
                    RefreshMask(view.Config.layer);
                }

                if (view.Config.pauseGame)
                {
                    RequestPause(false);
                }
            }

            ReleasePrefabHandleForView(viewName);
        }

        private async Task HandleShowMode(UIViewBase view, UIViewConfig config)
        {
            var decision = UIManagerShowActionPolicy.Resolve(config.showMode);
            if (decision.PauseVisibleLayerSiblings)
            {
                foreach (var sibling in _layerStacks[config.layer])
                {
                    if (sibling != view && sibling != null && sibling.IsVisible)
                    {
                        sibling.InternalPause();
                    }
                }
            }

            if (decision.PauseTopView && _layerStacks[config.layer].Count > 0)
            {
                var top = _layerStacks[config.layer].Peek();
                if (top != view && top != null)
                {
                    top.InternalPause();
                }
            }

            await Task.CompletedTask;
        }

        private async Task HandleCloseMode(UIViewBase view, UIViewConfig config)
        {
            var decision = UIManagerCloseActionPolicy.Resolve(
                config.lifetime,
                config.closeMode);

            if (decision.RemoveInstance
                && _viewInstances.TryGetValue(view.ViewName, out var registeredView)
                && ReferenceEquals(registeredView, view))
            {
                _viewInstances.Remove(view.ViewName);
            }

            if (decision.ReleasePrefabHandle)
            {
                ReleasePrefabHandleForView(view.ViewName);
            }

            if (decision.DestroyInstance)
            {
                DestroyViewInstance(view);
            }

            if (decision.DeactivateInstance && view != null)
            {
                view.gameObject.SetActive(false);
            }

            await Task.CompletedTask;
        }

        private static void DestroyViewInstance(UIViewBase view)
        {
            if (view != null)
            {
                UnityEngine.Object.Destroy(view.gameObject);
            }
        }

        private static void DestroyRuntimeGameObject(GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
            }
        }

        private static GameObject CreateRuntimeGameObject(GameObject prefab, Transform parent)
        {
            return UIRuntimeObjectFactory.CreateChild(prefab, parent);
        }

        private void RemoveFromStack(UIViewBase view, UILayer layer)
        {
            if (!_layerStacks.TryGetValue(layer, out var stack))
            {
                return;
            }

            TempViewList.Clear();
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current != view)
                {
                    TempViewList.Add(current);
                }
            }

            for (var index = TempViewList.Count - 1; index >= 0; index--)
            {
                stack.Push(TempViewList[index]);
            }

            UpdateTopView();
        }

        private void UpdateTopView()
        {
            _topView = null;
            foreach (var layer in UIManagerLayerTraversalPolicy.GetTopViewSearchOrder())
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
            if (_layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0)
            {
                stack.Peek()?.InternalResume();
            }

            UpdateTopView();
        }

        #endregion

        #region Masks

        private void ShowMask(UILayer layer, Color color, Action onClick, UIViewBase ownerView)
        {
            var hasMaskPrefab = maskPrefab != null;
            GameObject mask = null;
            var hasExistingMask = hasMaskPrefab
                && _maskInstances.TryGetValue(layer, out mask)
                && mask != null;
            var createDecision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: hasMaskPrefab,
                hasExistingMask: hasExistingMask,
                hasImage: false,
                hasButton: false,
                hasClickAction: onClick != null);
            if (!createDecision.UseMask)
            {
                return;
            }

            if (createDecision.CreateMask)
            {
                mask = CreateRuntimeGameObject(maskPrefab, GetLayerContainer(layer));
                _maskInstances[layer] = mask;
            }

            var image = mask.GetComponent<UnityEngine.UI.Image>();
            var button = mask.GetComponent<UnityEngine.UI.Button>();
            var actionDecision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab,
                hasExistingMask: true,
                hasImage: image != null,
                hasButton: button != null,
                hasClickAction: onClick != null);

            if (actionDecision.PositionMask)
            {
                mask.transform.SetAsLastSibling();
                if (ownerView != null
                    && ownerView.transform.parent == mask.transform.parent)
                {
                    mask.transform.SetSiblingIndex(ownerView.transform.GetSiblingIndex());
                }
                else
                {
                    mask.transform.SetSiblingIndex(mask.transform.GetSiblingIndex() - 1);
                }
            }

            if (actionDecision.ApplyColor)
            {
                image.color = color;
            }

            if (actionDecision.ClearClickListeners)
            {
                button.onClick.RemoveAllListeners();
            }

            if (actionDecision.AddClickListener)
            {
                button.onClick.AddListener(() => onClick());
            }

            if (actionDecision.ActivateMask)
            {
                mask.SetActive(true);
            }

            _maskOwners[layer] = ownerView;
        }

        private void HideMask(UILayer layer)
        {
            if (_maskInstances.TryGetValue(layer, out var mask) && mask != null)
            {
                mask.SetActive(false);
            }

            _maskOwners.Remove(layer);
        }

        private void RefreshMask(UILayer layer)
        {
            if (_layerStacks.TryGetValue(layer, out var stack))
            {
                foreach (var view in stack)
                {
                    if (view == null
                        || view.Config == null
                        || !view.Config.showMask
                        || (view.State != UIViewState.Opening
                            && view.State != UIViewState.Opened
                            && view.State != UIViewState.Paused))
                    {
                        continue;
                    }

                    ShowMask(
                        layer,
                        view.Config.maskColor,
                        view.Config.maskClickClose ? () => Close(view) : null,
                        view);
                    return;
                }
            }

            HideMask(layer);
        }

        public UIViewBase GetMaskOwner(UILayer layer)
        {
            return _maskOwners.TryGetValue(layer, out var owner) ? owner : null;
        }

        #endregion

        #region Queries and input

        public T GetView<T>() where T : UIViewBase
        {
            return _viewInstances.TryGetValue(ViewNameCache<T>.Name, out var view) ? view as T : null;
        }

        public UIViewBase GetView(string viewName)
        {
            return _viewInstances.TryGetValue(viewName, out var view) ? view : null;
        }

        public bool IsOpen(string viewName)
        {
            return _viewInstances.TryGetValue(viewName, out var view) && view != null && view.IsVisible;
        }

        public bool IsOpen<T>() where T : UIViewBase
        {
            return IsOpen(ViewNameCache<T>.Name);
        }

        public UIViewBase GetTopView() => _topView;

        public UIViewBase GetTopView(UILayer layer)
        {
            return _layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0 ? stack.Peek() : null;
        }

        public IEnumerable<UIViewBase> GetViewsInLayer(UILayer layer)
        {
            return _layerStacks.TryGetValue(layer, out var stack) ? stack : Array.Empty<UIViewBase>();
        }

        public bool TryHandleCancelInput()
        {
            if (_topView == null)
            {
                UpdateTopView();
            }

            var topView = _topView;
            var topViewConsumedInput = topView != null && topView.TryHandleCancelInput();
            var cancelInputDecision = UIManagerCancelInputPolicy.Resolve(
                hasTopView: topView != null,
                topViewConsumedInput: topViewConsumedInput,
                allowEscClose: topView != null && topView.Config != null && topView.Config.allowESCClose);
            if (cancelInputDecision.CloseTopView)
            {
                CloseTop();
            }

            return cancelInputDecision.ConsumeInput;
        }

        public void ToggleView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                return;
            }

            var isOpen = IsOpen(viewName);
            var toggleDecision = UIManagerViewTogglePolicy.Resolve(isOpen);
            if (toggleDecision.CloseView)
            {
                Close(viewName);
                return;
            }

            if (toggleDecision.OpenView)
            {
                Open(viewName);
            }
        }

        private void HandleCancelInput()
        {
            if (!enableESCClose)
            {
                return;
            }

            OnCancelInputRequested?.Invoke();
            TryHandleCancelInput();
        }

        /// <summary>由宿主输入系统调用；UIManager 不读取旧输入 API。</summary>
        public void TriggerCancelInput()
        {
            HandleCancelInput();
        }

        #endregion

        #region Preload and session cleanup

        public async Task PreloadAsync(string viewName)
        {
            if (!_viewConfigs.TryGetValue(viewName, out var config) || config == null)
            {
                LogUIManager(UIManagerLogPolicy.ViewConfigNotFound(viewName));
                return;
            }

            if (_viewInstances.ContainsKey(viewName))
            {
                return;
            }

            var view = await GetOrCreateViewAsync(viewName, config, _sessionViewGeneration);
            if (view != null)
            {
                view.gameObject.SetActive(false);
                LogInfo($"Preloaded: {viewName}");
            }
        }

        public async Task PreloadAsync<T>() where T : UIViewBase
        {
            await PreloadAsync(ViewNameCache<T>.Name);
        }

        public async Task PreloadAsync(params string[] viewNames)
        {
            if (viewNames == null || viewNames.Length == 0)
            {
                return;
            }

            var tasks = new List<Task>(viewNames.Length);
            foreach (var viewName in viewNames)
            {
                tasks.Add(PreloadAsync(viewName));
            }

            await Task.WhenAll(tasks);
        }

        private void CompletePendingRequests()
        {
            foreach (var request in _pendingOpenTasks.Values)
            {
                request.Completion.TrySetResult(null);
            }

            foreach (var request in _pendingCloseTasks.Values)
            {
                request.Completion.TrySetResult(true);
            }

            _pendingOpenTasks.Clear();
            _pendingCloseTasks.Clear();
        }

        #endregion
    }
}
