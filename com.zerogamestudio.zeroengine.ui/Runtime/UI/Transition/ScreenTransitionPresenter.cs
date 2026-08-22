using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.Core;

namespace ZeroEngine.UI
{
    /// <summary>
    /// The single reusable screen transition owner. It only owns the visual
    /// overlay and the request lifecycle; scene and gameplay work stays in the
    /// injected blackout action.
    /// </summary>
    public sealed class ScreenTransitionPresenter : MonoBehaviour, IScreenTransitionService
    {
        [Header("Transition Defaults")]
        [SerializeField] private float _defaultDuration = 0.4f;
        [SerializeField] private Color _fadeColor = Color.black;
        [SerializeField] private ScreenTransitionStyle _defaultStyle = ScreenTransitionStyle.Fade;
        [SerializeField] private ScreenTransitionEasing _fadeEasing = ScreenTransitionEasing.InOutSine;
        [SerializeField] private int _sortingOrder = 9999;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _transitionImage;
        private ScreenTransitionHooks _hooks;
        private IUnscaledAnimationDriver _animationDriver;
        private CancellationTokenSource _lifetimeCts;
        private IDisposable _activeInputLease;
        private bool _activeLeaseAcquired;
        private bool _initialized;
        private bool _isTransitioning;
        private bool _isDestroying;
        private bool _serviceRegistered;
        private bool _activeRequestBlocksRaycasts;

        public bool IsTransitioning => _isTransitioning;
        public float DefaultDurationSeconds => _defaultDuration;
        public Color FadeColor => _fadeColor;
        public ScreenTransitionStyle DefaultStyle => _defaultStyle;
        public ScreenTransitionEasing FadeEasing => _fadeEasing;

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            _isDestroying = true;
            try
            {
                _lifetimeCts?.Cancel();
            }
            catch
            {
                // Cancellation still marks the source cancelled; a faulty
                // callback must not skip visual and lease cleanup below.
            }

            try
            {
                CancelActiveAnimation();
            }
            catch
            {
                // A test or host driver cannot prevent destruction cleanup.
            }
            ClearVisualPreservingRaycast(_activeRequestBlocksRaycasts);
            try
            {
                ReleaseInputLease();
            }
            catch
            {
                // Keep registry and cancellation cleanup deterministic even if
                // a host-owned lease has a faulty Dispose implementation.
            }
            _isTransitioning = false;

            if (_serviceRegistered)
            {
                ServiceRegistry.Unregister<IScreenTransitionService>(this);
                _serviceRegistered = false;
            }

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }

        /// <summary>
        /// Installs or replaces host hooks. This is safe before or after Awake,
        /// and replacing hooks during a request does not affect its lease.
        /// </summary>
        public void ConfigureHooks(ScreenTransitionHooks hooks)
        {
            ThrowIfDestroying();
            _hooks = hooks;
        }

        /// <summary>
        /// Idempotent composition-root entry point. The presenter owns its
        /// registry lifetime so an installer never has to race presenter Awake.
        /// </summary>
        public void Initialize()
        {
            ThrowIfDestroying();
            if (_initialized)
            {
                return;
            }

            ValidateSerializedDefaults();
            _lifetimeCts = new CancellationTokenSource();
            _animationDriver ??= new UnityUnscaledAnimationDriver(() => this != null && !_isDestroying);
            CreateOverlay();
            _initialized = true;
            ServiceRegistry.Register<IScreenTransitionService>(this);
            _serviceRegistered = true;
        }

        public Task<ScreenTransitionResult> RunAsync(
            ScreenTransitionRequest request,
            Func<CancellationToken, Task> blackoutAction,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request, blackoutAction);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroying();
            Initialize();

            if (_isTransitioning)
            {
                EmitLog(new ScreenTransitionLogEntry(
                    ScreenTransitionLogLevel.Warning,
                    request.Style,
                    "Transition request ignored because another transition is active."));
                EmitTelemetry(new ScreenTransitionTelemetry(
                    request.Style,
                    ResolveDuration(request),
                    ScreenTransitionStatus.Busy,
                    cancelled: false));
                return Task.FromResult(new ScreenTransitionResult(ScreenTransitionStatus.Busy));
            }

            _isTransitioning = true;
            _activeRequestBlocksRaycasts = request.BlockRaycasts;
            return RunCoreAsync(request, blackoutAction, cancellationToken);
        }

        public void SetCovered()
        {
            ThrowIfDestroying();
            if (_isTransitioning)
            {
                throw new InvalidOperationException("Cannot set a covered state while a transition is active.");
            }

            Initialize();
            SetCoveredVisual(true);
        }

        public void SetClear()
        {
            ThrowIfDestroying();
            if (_isTransitioning)
            {
                throw new InvalidOperationException("Cannot set a clear state while a transition is active.");
            }

            Initialize();
            ClearVisualPreservingRaycast(true);
        }

        internal void SetAnimationDriver(IUnscaledAnimationDriver animationDriver)
        {
            if (animationDriver == null)
            {
                throw new ArgumentNullException(nameof(animationDriver));
            }

            if (_isTransitioning)
            {
                throw new InvalidOperationException("Cannot replace the animation driver while a transition is active.");
            }

            _animationDriver = animationDriver;
        }

        private async Task<ScreenTransitionResult> RunCoreAsync(
            ScreenTransitionRequest request,
            Func<CancellationToken, Task> blackoutAction,
            CancellationToken cancellationToken)
        {
            var duration = ResolveDuration(request);
            using var transitionCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            var token = transitionCts.Token;
            ExceptionDispatchInfo failure = null;
            var cancelled = false;

            try
            {
                AcquireInputLeaseIfRequested(request);
                SetCoveredVisual(request.BlockRaycasts);

                await AnimateAsync(request.Style, duration, cover: true, token);
                token.ThrowIfCancellationRequested();
                await blackoutAction(token);
                token.ThrowIfCancellationRequested();
                await AnimateAsync(request.Style, duration, cover: false, token);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException exception)
            {
                cancelled = true;
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                ExceptionDispatchInfo cleanupFailure = null;
                try
                {
                    CancelActiveAnimation();
                    ClearVisualPreservingRaycast(request.BlockRaycasts);
                    ReleaseInputLease();
                }
                catch (Exception exception)
                {
                    cleanupFailure = ExceptionDispatchInfo.Capture(exception);
                }

                if (failure == null && cleanupFailure != null)
                {
                    failure = cleanupFailure;
                }

                _isTransitioning = false;
                try
                {
                    EmitTelemetry(new ScreenTransitionTelemetry(
                        request.Style,
                        duration,
                        ScreenTransitionStatus.Completed,
                        cancelled,
                        failure?.SourceException));
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = ExceptionDispatchInfo.Capture(exception);
                    }
                }
                _activeRequestBlocksRaycasts = false;
            }

            if (failure != null)
            {
                failure.Throw();
            }

            return new ScreenTransitionResult(ScreenTransitionStatus.Completed);
        }

        private async Task AnimateAsync(
            ScreenTransitionStyle style,
            float duration,
            bool cover,
            CancellationToken cancellationToken)
        {
            await _animationDriver.AnimateAsync(
                duration,
                progress => ApplyVisualProgress(style, cover, progress),
                cancellationToken);
        }

        private void AcquireInputLeaseIfRequested(ScreenTransitionRequest request)
        {
            if (!request.LockInput)
            {
                return;
            }

            _activeLeaseAcquired = true;
            _activeInputLease = _hooks?.AcquireInputLockLease?.Invoke();
        }

        private void ReleaseInputLease()
        {
            if (!_activeLeaseAcquired)
            {
                return;
            }

            _activeLeaseAcquired = false;
            var lease = _activeInputLease;
            _activeInputLease = null;
            lease?.Dispose();
        }

        private void CancelActiveAnimation()
        {
            if (_animationDriver is ICancelableUnscaledAnimationDriver cancelable)
            {
                cancelable.Cancel();
            }
        }

        private void CreateOverlay()
        {
            if (_canvasGroup != null && _transitionImage != null)
            {
                return;
            }

            var canvasObject = new GameObject("ScreenTransitionCanvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasObject.AddComponent<CanvasGroup>();
            _canvasGroup.interactable = false;

            var imageObject = new GameObject("ScreenTransitionImage");
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rectTransform = imageObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            _transitionImage = imageObject.AddComponent<Image>();
            _transitionImage.color = _fadeColor;
            _transitionImage.raycastTarget = true;
            ClearVisualPreservingRaycast(true);
        }

        private void SetCoveredVisual(bool blockRaycasts)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = false;
            ResetImageTransform();
            if (blockRaycasts)
            {
                _canvasGroup.blocksRaycasts = true;
            }
        }

        private void ClearVisualPreservingRaycast(bool changeRaycasts)
        {
            if (_canvasGroup == null)
            {
                return;
            }

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            ResetImageTransform();
            if (changeRaycasts)
            {
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void ApplyVisualProgress(
            ScreenTransitionStyle style,
            bool cover,
            float progress)
        {
            if (_canvasGroup == null || _transitionImage == null)
            {
                throw new InvalidOperationException("The transition overlay is not available.");
            }

            progress = EvaluateProgress(style, cover, progress);
            switch (style)
            {
                case ScreenTransitionStyle.CircleWipe:
                    _canvasGroup.alpha = 1f;
                    _transitionImage.transform.localScale = Vector3.Lerp(
                        cover ? Vector3.zero : Vector3.one * 3f,
                        cover ? Vector3.one * 3f : Vector3.zero,
                        progress);
                    break;
                case ScreenTransitionStyle.DiamondWipe:
                    _canvasGroup.alpha = 1f;
                    _transitionImage.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    _transitionImage.transform.localScale = Vector3.Lerp(
                        cover ? Vector3.zero : Vector3.one * 4f,
                        cover ? Vector3.one * 4f : Vector3.zero,
                        progress);
                    break;
                case ScreenTransitionStyle.Fade:
                case ScreenTransitionStyle.Dissolve:
                    _canvasGroup.alpha = cover ? progress : 1f - progress;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown transition style.");
            }
        }

        private void ResetImageTransform()
        {
            if (_transitionImage == null)
            {
                return;
            }

            _transitionImage.transform.localScale = Vector3.one;
            _transitionImage.transform.localRotation = Quaternion.identity;
        }

        private float EvaluateProgress(
            ScreenTransitionStyle style,
            bool cover,
            float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (_fadeEasing == ScreenTransitionEasing.Linear)
            {
                return progress;
            }

            if (style == ScreenTransitionStyle.CircleWipe || style == ScreenTransitionStyle.DiamondWipe)
            {
                return cover
                    ? 1f - Mathf.Pow(1f - progress, 2f)
                    : Mathf.Pow(progress, 2f);
            }

            return 0.5f * (1f - Mathf.Cos(Mathf.PI * progress));
        }

        private float ResolveDuration(ScreenTransitionRequest request)
        {
            return request.DurationSeconds ?? _defaultDuration;
        }

        private void ValidateRequest(
            ScreenTransitionRequest request,
            Func<CancellationToken, Task> blackoutAction)
        {
            if (blackoutAction == null)
            {
                throw new ArgumentNullException(nameof(blackoutAction));
            }

            if (!Enum.IsDefined(typeof(ScreenTransitionStyle), request.Style))
            {
                throw new ArgumentOutOfRangeException(nameof(request), request.Style, "Unknown transition style.");
            }

            if (request.DurationSeconds.HasValue
                && (!IsFinite(request.DurationSeconds.Value) || request.DurationSeconds.Value < 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.DurationSeconds,
                    "Transition duration must be finite and non-negative.");
            }
        }

        private void ValidateSerializedDefaults()
        {
            if (!IsFinite(_defaultDuration) || _defaultDuration < 0f)
            {
                throw new InvalidOperationException("The default transition duration must be finite and non-negative.");
            }

            if (!Enum.IsDefined(typeof(ScreenTransitionStyle), _defaultStyle))
            {
                throw new InvalidOperationException("The default transition style is invalid.");
            }

            if (!Enum.IsDefined(typeof(ScreenTransitionEasing), _fadeEasing))
            {
                throw new InvalidOperationException("The transition easing is invalid.");
            }
        }

        private void EmitLog(ScreenTransitionLogEntry entry)
        {
            _hooks?.Log?.Invoke(entry);
        }

        private void EmitTelemetry(ScreenTransitionTelemetry telemetry)
        {
            _hooks?.Telemetry?.Invoke(telemetry);
        }

        private void ThrowIfDestroying()
        {
            if (_isDestroying || this == null)
            {
                throw new ObjectDisposedException(nameof(ScreenTransitionPresenter));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
