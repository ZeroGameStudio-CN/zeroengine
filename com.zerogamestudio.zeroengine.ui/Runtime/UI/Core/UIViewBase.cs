using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

#if DOTWEEN
using DG.Tweening;
#endif

namespace ZeroEngine.UI
{
    /// <summary>
    /// UI 视图基类。生命周期由 UIManager 串行驱动，动画过渡使用独立取消令牌。
    /// </summary>
    public abstract class UIViewBase : MonoBehaviour
    {
        private string _cachedViewName;
        private CancellationTokenSource _transitionCts = new();
        private CancellationToken _activeTransitionToken;
        private GameObject _lastSelected;
        private GameObject _pendingFocusTarget;
        private bool _destroyed;

        public virtual string ViewName => _cachedViewName ??= GetType().Name;
        public UIViewConfig Config { get; private set; }
        public UIViewState State { get; private set; } = UIViewState.None;
        protected UIOpenArgs OpenArgs { get; private set; }
        protected CanvasGroup CanvasGroup { get; private set; }
        protected RectTransform RectTransform { get; private set; }
        public bool IsVisible => State == UIViewState.Opened || State == UIViewState.Opening;

        [SerializeField] protected GameObject defaultSelected;

        internal void InternalInit(UIViewConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _destroyed = false;
            CanvasGroup = gameObject.GetOrAddComponent<CanvasGroup>();
            RectTransform = GetComponent<RectTransform>();
            SetVisible(false, true);
            State = UIViewState.Created;
        }

        public virtual Task OnCreateAsync()
        {
            return Task.CompletedTask;
        }

        protected virtual void OnCreate() { }
        protected virtual void OnOpen() { }
        protected virtual void OnResume() { }
        protected virtual void OnPause() { }
        protected virtual void OnClose() { }
        protected virtual void OnViewDestroy() { }
        public virtual void Refresh() { }
        protected virtual void OnLocalizationChanged() { }

        internal async Task InternalOpenAsync(UIOpenArgs args)
        {
            if (_destroyed)
            {
                return;
            }

            args ??= new UIOpenArgs();
            OpenArgs = args;

            if (State == UIViewState.Created)
            {
                await OnCreateAsync();
                if (_destroyed)
                {
                    return;
                }

                OnCreate();
            }

            var token = BeginTransition();
            State = UIViewState.Opening;
            gameObject.SetActive(true);

            try
            {
                _activeTransitionToken = token;
                if (!args.Immediate && Config.openAnimation != UIAnimationType.None)
                {
                    await AwaitAnimationAsync(PlayOpenAnimation(token), token);
                }
                else
                {
                    SetVisible(true, true);
                }

                token.ThrowIfCancellationRequested();
                State = UIViewState.Opened;
                OnOpen();
                RestoreFocus();
                args.OnOpened?.Invoke();
            }
            catch (OperationCanceledException)
            {
                if (!_destroyed)
                {
                    SetVisible(false, true);
                    State = UIViewState.Closed;
                    gameObject.SetActive(false);
                }
            }
        }

        internal async Task InternalCloseAsync(UICloseArgs args)
        {
            if (_destroyed || State == UIViewState.Closed || State == UIViewState.Closing)
            {
                return;
            }

            args ??= new UICloseArgs();
            var token = BeginTransition();
            State = UIViewState.Closing;
            SaveLastSelected();
            OnClose();

            try
            {
                _activeTransitionToken = token;
                if (!args.Immediate && Config.closeAnimation != UIAnimationType.None)
                {
                    await AwaitAnimationAsync(PlayCloseAnimation(token), token);
                }
                else
                {
                    SetVisible(false, true);
                }

                token.ThrowIfCancellationRequested();
                State = UIViewState.Closed;
                gameObject.SetActive(false);
                OpenArgs?.OnClosed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                if (!_destroyed)
                {
                    SetVisible(false, true);
                    State = UIViewState.Closed;
                    gameObject.SetActive(false);
                    OpenArgs?.OnClosed?.Invoke();
                }
            }
        }

        internal void InternalPause()
        {
            if (_destroyed || State != UIViewState.Opened)
            {
                return;
            }

            SaveLastSelected();
            State = UIViewState.Paused;
            OnPause();
            if (CanvasGroup != null)
            {
                CanvasGroup.interactable = false;
                CanvasGroup.blocksRaycasts = false;
            }
        }

        internal void InternalResume()
        {
            if (_destroyed || State != UIViewState.Paused)
            {
                return;
            }

            State = UIViewState.Opened;
            OnResume();
            if (CanvasGroup != null)
            {
                CanvasGroup.interactable = true;
                CanvasGroup.blocksRaycasts = Config != null && Config.blockInput;
            }

            RestoreFocus();
        }

        internal void InternalDestroy()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            KillActiveAnimations();
            OnViewDestroy();
            State = UIViewState.None;
        }

        #region Animation

        private CancellationToken BeginTransition()
        {
            KillActiveAnimations();
            _transitionCts.Dispose();
            _transitionCts = new CancellationTokenSource();
            return _transitionCts.Token;
        }

        /// <summary>取消当前过渡；重复 open/close 或销毁时都会调用。</summary>
        protected void KillActiveAnimations()
        {
            _transitionCts.Cancel();
#if DOTWEEN
            DOTween.Kill(transform);
            if (CanvasGroup != null)
            {
                DOTween.Kill(CanvasGroup);
            }

            if (RectTransform != null)
            {
                DOTween.Kill(RectTransform);
            }
#endif
        }

        private static async Task AwaitAnimationAsync(Task animation, CancellationToken token)
        {
            if (animation == null)
            {
                return;
            }

            if (!token.CanBeCanceled)
            {
                await animation;
                return;
            }

            var cancellation = Task.Delay(Timeout.Infinite, token);
            var completed = await Task.WhenAny(animation, cancellation);
            if (completed != animation)
            {
                throw new OperationCanceledException(token);
            }

            await animation;
        }

        protected virtual Task PlayOpenAnimation()
        {
            return PlayOpenAnimationCore(_activeTransitionToken);
        }

        protected virtual Task PlayOpenAnimation(CancellationToken token)
        {
            // Calling the legacy overload keeps existing subclass overrides source compatible.
            return PlayOpenAnimation();
        }

        private async Task PlayOpenAnimationCore(CancellationToken token)
        {
            SetVisible(true, false);
            switch (Config.openAnimation)
            {
                case UIAnimationType.Fade:
                    await AnimateFade(0f, 1f, Config.animationDuration, token);
                    break;
                case UIAnimationType.Scale:
                    await AnimateScale(Vector3.zero, Vector3.one, Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideLeft:
                    await AnimateSlide(new Vector2(-Screen.width, 0), Vector2.zero, Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideRight:
                    await AnimateSlide(new Vector2(Screen.width, 0), Vector2.zero, Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideTop:
                    await AnimateSlide(new Vector2(0, Screen.height), Vector2.zero, Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideBottom:
                    await AnimateSlide(new Vector2(0, -Screen.height), Vector2.zero, Config.animationDuration, token);
                    break;
                case UIAnimationType.Custom:
                    await PlayCustomOpenAnimation(token);
                    break;
            }

            token.ThrowIfCancellationRequested();
            SetVisible(true, true);
        }

        protected virtual Task PlayCustomOpenAnimation() => Task.CompletedTask;

        protected virtual Task PlayCustomOpenAnimation(CancellationToken token)
        {
            return PlayCustomOpenAnimation();
        }

        protected virtual Task PlayCloseAnimation()
        {
            return PlayCloseAnimationCore(_activeTransitionToken);
        }

        protected virtual Task PlayCloseAnimation(CancellationToken token)
        {
            return PlayCloseAnimation();
        }

        private async Task PlayCloseAnimationCore(CancellationToken token)
        {
            switch (Config.closeAnimation)
            {
                case UIAnimationType.Fade:
                    await AnimateFade(1f, 0f, Config.animationDuration, token);
                    break;
                case UIAnimationType.Scale:
                    await AnimateScale(Vector3.one, Vector3.zero, Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideLeft:
                    await AnimateSlide(Vector2.zero, new Vector2(-Screen.width, 0), Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideRight:
                    await AnimateSlide(Vector2.zero, new Vector2(Screen.width, 0), Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideTop:
                    await AnimateSlide(Vector2.zero, new Vector2(0, Screen.height), Config.animationDuration, token);
                    break;
                case UIAnimationType.SlideBottom:
                    await AnimateSlide(Vector2.zero, new Vector2(0, -Screen.height), Config.animationDuration, token);
                    break;
                case UIAnimationType.Custom:
                    await PlayCustomCloseAnimation(token);
                    break;
            }

            token.ThrowIfCancellationRequested();
            SetVisible(false, true);
        }

        protected virtual Task PlayCustomCloseAnimation() => Task.CompletedTask;

        protected virtual Task PlayCustomCloseAnimation(CancellationToken token)
        {
            return PlayCustomCloseAnimation();
        }

        private async Task AnimateFade(float from, float to, float duration, CancellationToken token)
        {
            if (CanvasGroup == null)
            {
                return;
            }

            CanvasGroup.alpha = from;
            duration = Mathf.Max(0f, duration);
            if (duration <= 0f)
            {
                CanvasGroup.alpha = to;
                return;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                CanvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            CanvasGroup.alpha = to;
        }

        private async Task AnimateScale(Vector3 from, Vector3 to, float duration, CancellationToken token)
        {
            var targetTransform = transform;
            if (targetTransform == null)
            {
                return;
            }

            targetTransform.localScale = from;
            duration = Mathf.Max(0f, duration);
            if (duration <= 0f)
            {
                targetTransform.localScale = to;
                return;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                var t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                targetTransform.localScale = Vector3.Lerp(from, to, t);
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            targetTransform.localScale = to;
        }

        private async Task AnimateSlide(Vector2 from, Vector2 to, float duration, CancellationToken token)
        {
            if (RectTransform == null)
            {
                return;
            }

            RectTransform.anchoredPosition = from;
            duration = Mathf.Max(0f, duration);
            if (duration <= 0f)
            {
                RectTransform.anchoredPosition = to;
                return;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                var t = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
                RectTransform.anchoredPosition = Vector2.Lerp(from, to, t);
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            RectTransform.anchoredPosition = to;
        }

        private static float EaseOutBack(float t)
        {
            return 1 + 2.70158f * Mathf.Pow(t - 1, 3) + 1.70158f * Mathf.Pow(t - 1, 2);
        }

        private static float EaseOutCubic(float t)
        {
            return 1 - Mathf.Pow(1 - t, 3);
        }

        #endregion

        #region Visibility and focus

        protected void SetVisible(bool visible, bool immediate = false)
        {
            if (CanvasGroup == null)
            {
                return;
            }

            if (immediate)
            {
                CanvasGroup.alpha = visible ? 1f : 0f;
            }

            CanvasGroup.interactable = visible;
            CanvasGroup.blocksRaycasts = visible && (Config == null || Config.blockInput);
        }

        public virtual void RestoreFocus()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            if (CanSelect(_lastSelected))
            {
                SetSelected(_lastSelected);
                return;
            }

            if (CanSelect(defaultSelected))
            {
                SetSelected(defaultSelected);
                return;
            }

            foreach (var selectable in GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
            {
                if (CanSelect(selectable))
                {
                    SetSelected(selectable.gameObject);
                    return;
                }
            }
        }

        public void SaveLastSelected()
        {
            var current = EventSystem.current?.currentSelectedGameObject;
            if (current != null && current.transform.IsChildOf(transform))
            {
                _lastSelected = current;
            }
        }

        protected void SetSelected(GameObject go)
        {
            if (!CanSelect(go))
            {
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                ScheduleFocusRetry(go);
                return;
            }

            if (!eventSystem.alreadySelecting)
            {
                eventSystem.SetSelectedGameObject(go);
            }

            ScheduleFocusRetry(go);
        }

        protected void ClearSelected()
        {
            if (EventSystem.current != null && !EventSystem.current.alreadySelecting)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private static bool CanSelect(GameObject go)
        {
            return go != null && go.activeInHierarchy && CanSelect(go.GetComponent<UnityEngine.UI.Selectable>());
        }

        private static bool CanSelect(UnityEngine.UI.Selectable selectable)
        {
            return selectable != null
                && selectable.gameObject.activeInHierarchy
                && selectable.IsInteractable();
        }

        private void ScheduleFocusRetry(GameObject target)
        {
            if (_pendingFocusTarget == target)
            {
                return;
            }

            _pendingFocusTarget = target;
            _ = RestoreFocusNextFrameAsync(target);
        }

        private async Task RestoreFocusNextFrameAsync(GameObject target)
        {
            try
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await Task.Yield();
                    if (this == null || _pendingFocusTarget != target || !CanSelect(target))
                    {
                        return;
                    }

                    var eventSystem = EventSystem.current;
                    if (eventSystem == null || eventSystem.alreadySelecting)
                    {
                        continue;
                    }

                    var current = eventSystem.currentSelectedGameObject;
                    if (current == null || !CanSelect(current))
                    {
                        eventSystem.SetSelectedGameObject(target);
                    }

                    return;
                }
            }
            finally
            {
                if (this != null && _pendingFocusTarget == target)
                {
                    _pendingFocusTarget = null;
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>让顶层 View 优先处理取消输入。</summary>
        public virtual bool TryHandleCancelInput()
        {
            return false;
        }

        public void Close()
        {
            UIManager.Instance?.Close(this);
        }

        public void CloseWithResult(object result)
        {
            UIManager.Instance?.Close(this, UICloseArgs.Create(result));
        }

        protected T GetData<T>()
        {
            return OpenArgs?.Data is T data ? data : default;
        }

        #endregion

        protected virtual void OnDestroy()
        {
            InternalDestroy();
            _transitionCts.Dispose();
        }
    }

#if DOTWEEN
    /// <summary>DOTween 兼容桥接。</summary>
    public static class DOTweenExtensions
    {
        public static Task AsTask(this Tween tween)
        {
            if (tween == null || tween.IsComplete())
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>();
            tween.OnComplete(() => completion.TrySetResult(true));
            tween.OnKill(() => completion.TrySetResult(false));
            return completion.Task;
        }
    }
#endif
}
