using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互提示 UI (v1.14.0+)
    /// 显示当前可交互目标的提示信息
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        [Tooltip("提示文本")]
        private Text _promptText;

        [SerializeField]
        [Tooltip("提示文本 (TextMeshPro)")]
        private TMPro.TextMeshProUGUI _promptTextTMP;

        [SerializeField]
        [Tooltip("图标")]
        private Image _iconImage;

        [SerializeField]
        [Tooltip("背景")]
        private Image _backgroundImage;

        [SerializeField]
        [Tooltip("Canvas Group (用于淡入淡出)")]
        private CanvasGroup _canvasGroup;

        [Header("Settings")]
        [SerializeField]
        [Tooltip("跟随目标位置")]
        private bool _followTarget = true;

        [SerializeField]
        [Tooltip("位置偏移 (World Space)")]
        private Vector3 _worldOffset = new Vector3(0, 2f, 0);

        [SerializeField]
        [Tooltip("位置偏移 (Screen Space)")]
        private Vector2 _screenOffset = Vector2.zero;

        [SerializeField]
        [Tooltip("淡入时间")]
        private float _fadeInDuration = 0.2f;

        [SerializeField]
        [Tooltip("淡出时间")]
        private float _fadeOutDuration = 0.15f;

        [Header("Interaction Detector")]
        [SerializeField]
        [Tooltip("交互检测器 (留空则自动查找)")]
        private InteractionDetector _detector;

        #endregion

        #region Private Fields

        private IInteractable _currentTarget;
        private Camera _mainCamera;
        private RectTransform _rectTransform;
        private Canvas _canvas;

        private bool _isShowing;
        private float _fadeProgress;
        private bool _isFading;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            _mainCamera = Camera.main;

            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }

            // 初始隐藏
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0;
            }
            gameObject.SetActive(false);
        }

        private void Start()
        {
            // 自动查找检测器
            if (_detector == null)
            {
                _detector = FindFirstObjectByType<InteractionDetector>();
            }

            if (_detector != null)
            {
                _detector.OnTargetChanged += OnTargetChanged;
            }
        }

        private void OnDestroy()
        {
            if (_detector != null)
            {
                _detector.OnTargetChanged -= OnTargetChanged;
            }
        }

        private void Update()
        {
            // 处理淡入淡出
            if (_isFading)
            {
                UpdateFade();
            }

            // 跟随目标
            if (_isShowing && _followTarget && _currentTarget != null)
            {
                UpdatePosition();
            }
        }

        private void LateUpdate()
        {
            // 在 LateUpdate 中再次更新位置，确保跟随正确
            if (_isShowing && _followTarget && _currentTarget != null)
            {
                UpdatePosition();
            }
        }

        #endregion

        #region Event Handlers

        private void OnTargetChanged(IInteractable target)
        {
            if (target != null)
            {
                Show(target);
            }
            else
            {
                Hide();
            }
        }

        #endregion

        #region Show/Hide

        /// <summary>
        /// 显示交互提示
        /// </summary>
        public void Show(IInteractable target)
        {
            if (target == null) return;

            _currentTarget = target;
            _isShowing = true;

            // 更新内容
            UpdateContent();

            // 更新位置
            UpdatePosition();

            // 显示并淡入
            gameObject.SetActive(true);
            StartFade(true);
        }

        /// <summary>
        /// 隐藏交互提示
        /// </summary>
        public void Hide()
        {
            if (!_isShowing) return;

            _isShowing = false;
            StartFade(false);
        }

        /// <summary>
        /// 立即隐藏 (无动画)
        /// </summary>
        public void HideImmediate()
        {
            _isShowing = false;
            _isFading = false;
            _currentTarget = null;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0;
            }
            gameObject.SetActive(false);
        }

        #endregion

        #region Content Update

        private void UpdateContent()
        {
            if (_currentTarget == null) return;

            // 获取提示文本
            string hint = _currentTarget.GetInteractionHint();

            // 检查是否可交互，添加不可交互原因
            if (_detector != null)
            {
                var ctx = new InteractionContext(
                    _detector.gameObject,
                    _detector.DetectionCenter,
                    _detector.GetDistanceTo(_currentTarget)
                );

                if (!_currentTarget.CanInteract(ctx))
                {
                    string reason = _currentTarget.GetCannotInteractReason(ctx);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        hint = $"<color=#888888>{hint}</color>\n<color=#ff6666>{reason}</color>";
                    }
                }
            }

            // 设置文本
            if (_promptTextTMP != null)
            {
                _promptTextTMP.text = hint;
            }
            else if (_promptText != null)
            {
                _promptText.text = hint;
            }

            // TODO: 根据交互类型设置图标
        }

        #endregion

        #region Position Update

        private void UpdatePosition()
        {
            if (_currentTarget == null || _mainCamera == null) return;

            // 获取目标世界位置
            Vector3 worldPos = _currentTarget.GetInteractionPosition() + _worldOffset;

            // 转换为屏幕位置
            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);

            // 检查是否在屏幕前方
            if (screenPos.z < 0)
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 0;
                }
                return;
            }

            // 应用屏幕偏移
            screenPos.x += _screenOffset.x;
            screenPos.y += _screenOffset.y;

            // 根据 Canvas 模式设置位置
            if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _rectTransform.position = screenPos;
            }
            else if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.transform as RectTransform,
                    screenPos,
                    _canvas.worldCamera,
                    out Vector2 localPoint
                );
                _rectTransform.localPosition = localPoint;
            }
            else
            {
                _rectTransform.position = screenPos;
            }
        }

        #endregion

        #region Fade Animation

        private void StartFade(bool fadeIn)
        {
            _isFading = true;
            _fadeProgress = fadeIn ? 0 : 1;
        }

        private void UpdateFade()
        {
            if (_canvasGroup == null)
            {
                _isFading = false;
                return;
            }

            float duration = _isShowing ? _fadeInDuration : _fadeOutDuration;
            if (duration <= 0)
            {
                _canvasGroup.alpha = _isShowing ? 1 : 0;
                _isFading = false;
                if (!_isShowing)
                {
                    gameObject.SetActive(false);
                    _currentTarget = null;
                }
                return;
            }

            float delta = Time.deltaTime / duration;

            if (_isShowing)
            {
                _fadeProgress += delta;
                _canvasGroup.alpha = Mathf.Clamp01(_fadeProgress);
                if (_fadeProgress >= 1)
                {
                    _isFading = false;
                }
            }
            else
            {
                _fadeProgress -= delta;
                _canvasGroup.alpha = Mathf.Clamp01(_fadeProgress);
                if (_fadeProgress <= 0)
                {
                    _isFading = false;
                    gameObject.SetActive(false);
                    _currentTarget = null;
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 设置交互检测器
        /// </summary>
        public void SetDetector(InteractionDetector detector)
        {
            // 移除旧监听
            if (_detector != null)
            {
                _detector.OnTargetChanged -= OnTargetChanged;
            }

            _detector = detector;

            // 添加新监听
            if (_detector != null)
            {
                _detector.OnTargetChanged += OnTargetChanged;
            }
        }

        /// <summary>
        /// 强制刷新内容
        /// </summary>
        public void RefreshContent()
        {
            if (_currentTarget != null)
            {
                UpdateContent();
            }
        }

        #endregion
    }
}
