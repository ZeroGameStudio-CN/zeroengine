using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程 UI 管理器 (v1.14.0+)
    /// 统一管理所有教程相关 UI 元素
    /// </summary>
    public class TutorialUIManager : MonoSingleton<TutorialUIManager>
    {
        #region Events

        /// <summary>对话框点击继续</summary>
        public event Action OnDialogueContinue;

        /// <summary>选择选项</summary>
        public event Action<int> OnChoiceSelected;

        /// <summary>跳过按钮点击</summary>
        public event Action OnSkipClicked;

        /// <summary>高亮目标点击</summary>
        public event Action OnHighlightTargetClicked;

        #endregion

        #region Configuration

        [Header("Configuration")]
        [SerializeField] private TutorialConfigSO _config;

        [Header("UI Containers")]
        [SerializeField] private Transform _uiContainer;
        [SerializeField] private Transform _worldSpaceContainer;

        #endregion

        #region Runtime

        // UI 实例
        private TutorialDialogueView _dialogueView;
        private TutorialHighlightView _highlightView;
        private TutorialArrowUI _arrowIndicator;
        private TutorialProgressView _progressView;

        // 状态
        private bool _isInitialized;

        public TutorialConfigSO Config => _config;

        #endregion

        #region Initialization

        protected override void Awake()
        {
            base.Awake();
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (_isInitialized) return;

            // 创建 UI 容器
            if (_uiContainer == null)
            {
                var canvas = FindOrCreateCanvas();
                _uiContainer = canvas.transform;
            }

            // 初始化各 UI 组件
            InitializeDialogueView();
            InitializeHighlightView();
            InitializeArrowIndicator();
            InitializeProgressView();

            _isInitialized = true;
        }

        private Canvas FindOrCreateCanvas()
        {
            // 尝试找到现有的 Tutorial Canvas
            var existing = GameObject.Find("TutorialCanvas");
            if (existing != null)
            {
                var canvas = existing.GetComponent<Canvas>();
                if (canvas != null) return canvas;
            }

            // 创建新的 Canvas
            var go = new GameObject("TutorialCanvas");
            go.transform.SetParent(transform);

            var newCanvas = go.AddComponent<Canvas>();
            newCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            newCanvas.sortingOrder = 1000; // 确保在最上层

            go.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
                UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            return newCanvas;
        }

        private void InitializeDialogueView()
        {
            if (_config?.DialogueUIPrefab != null)
            {
                var go = Instantiate(_config.DialogueUIPrefab, _uiContainer);
                _dialogueView = go.GetComponent<TutorialDialogueView>();
                _dialogueView?.Initialize(this);
                go.SetActive(false);
            }
        }

        private void InitializeHighlightView()
        {
            if (_config?.HighlightMaskPrefab != null)
            {
                var go = Instantiate(_config.HighlightMaskPrefab, _uiContainer);
                _highlightView = go.GetComponent<TutorialHighlightView>();
                _highlightView?.Initialize(this);
                go.SetActive(false);
            }
        }

        private void InitializeArrowIndicator()
        {
            if (_config?.ArrowIndicatorPrefab != null)
            {
                var go = Instantiate(_config.ArrowIndicatorPrefab);
                _arrowIndicator = go.GetComponent<TutorialArrowUI>();
                _arrowIndicator?.Initialize(this);
                go.SetActive(false);
            }
        }

        private void InitializeProgressView()
        {
            if (_config?.ProgressIndicatorPrefab != null)
            {
                var go = Instantiate(_config.ProgressIndicatorPrefab, _uiContainer);
                _progressView = go.GetComponent<TutorialProgressView>();
                _progressView?.Initialize(this);
                go.SetActive(false);
            }
        }

        #endregion

        #region Dialogue API

        /// <summary>
        /// 显示对话
        /// </summary>
        public void ShowDialogue(string text, string speakerName = null, Sprite speakerAvatar = null)
        {
            if (_dialogueView == null)
            {
                Debug.LogWarning("[Tutorial] Dialogue view not initialized");
                return;
            }

            _dialogueView.gameObject.SetActive(true);
            _dialogueView.ShowDialogue(text, speakerName, speakerAvatar);
        }

        /// <summary>
        /// 显示带选项的对话
        /// </summary>
        public void ShowDialogueWithChoices(string text, string[] choices, string speakerName = null)
        {
            if (_dialogueView == null)
            {
                Debug.LogWarning("[Tutorial] Dialogue view not initialized");
                return;
            }

            _dialogueView.gameObject.SetActive(true);
            _dialogueView.ShowDialogueWithChoices(text, choices, speakerName);
        }

        /// <summary>
        /// 隐藏对话
        /// </summary>
        public void HideDialogue()
        {
            if (_dialogueView != null)
            {
                _dialogueView.Hide();
            }
        }

        /// <summary>
        /// 跳过当前打字机效果
        /// </summary>
        public void SkipTypewriter()
        {
            _dialogueView?.SkipTypewriter();
        }

        #endregion

        #region Prompt API (简化对话)

        /// <summary>
        /// 显示简单提示
        /// </summary>
        public void ShowPrompt(string text, Action onConfirm)
        {
            ShowDialogue(text);
            // 注册一次性回调
            void HandleContinue()
            {
                OnDialogueContinue -= HandleContinue;
                onConfirm?.Invoke();
            }
            OnDialogueContinue += HandleContinue;
        }

        /// <summary>
        /// 隐藏提示
        /// </summary>
        public void HidePrompt()
        {
            HideDialogue();
        }

        #endregion

        #region Highlight API

        /// <summary>
        /// 高亮 UI 元素
        /// </summary>
        public void ShowHighlight(RectTransform target, HighlightType type, string hintText = null)
        {
            if (_highlightView == null)
            {
                Debug.LogWarning("[Tutorial] Highlight view not initialized");
                return;
            }

            _highlightView.gameObject.SetActive(true);
            _highlightView.ShowHighlight(target, type, hintText);
        }

        /// <summary>
        /// 通过路径高亮 UI 元素
        /// </summary>
        public void ShowHighlightByPath(string targetPath, HighlightType type, string hintText = null)
        {
            var target = FindUIByPath(targetPath);
            if (target != null)
            {
                ShowHighlight(target, type, hintText);
            }
            else
            {
                Debug.LogWarning($"[Tutorial] Cannot find UI element: {targetPath}");
            }
        }

        /// <summary>
        /// 隐藏高亮
        /// </summary>
        public void HideHighlight()
        {
            if (_highlightView != null)
            {
                _highlightView.Hide();
            }
        }

        /// <summary>
        /// 设置高亮是否等待点击
        /// </summary>
        public void SetHighlightWaitForClick(bool wait)
        {
            _highlightView?.SetWaitForClick(wait);
        }

        #endregion

        #region Arrow API

        /// <summary>
        /// 显示世界空间箭头
        /// </summary>
        public void ShowWorldArrow(Vector3 targetPosition)
        {
            if (_arrowIndicator == null)
            {
                Debug.LogWarning("[Tutorial] Arrow indicator not initialized");
                return;
            }

            _arrowIndicator.gameObject.SetActive(true);
            _arrowIndicator.SetTarget(targetPosition);
        }

        /// <summary>
        /// 更新箭头目标位置
        /// </summary>
        public void UpdateWorldArrow(Vector3 targetPosition)
        {
            _arrowIndicator?.SetTarget(targetPosition);
        }

        /// <summary>
        /// 隐藏世界空间箭头
        /// </summary>
        public void HideWorldArrow()
        {
            if (_arrowIndicator != null)
            {
                _arrowIndicator.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 显示屏幕空间箭头指向目标
        /// </summary>
        public void ShowScreenArrow(Vector3 worldTarget)
        {
            if (_arrowIndicator == null) return;

            _arrowIndicator.gameObject.SetActive(true);
            _arrowIndicator.SetTarget(worldTarget);
            _arrowIndicator.SetMode(TutorialArrowUI.ArrowMode.ScreenEdge);
        }

        #endregion

        #region Progress API

        /// <summary>
        /// 显示进度
        /// </summary>
        public void ShowProgress(int current, int total)
        {
            if (_progressView == null || !(_config?.ShowProgressIndicator ?? false))
            {
                return;
            }

            _progressView.gameObject.SetActive(true);
            _progressView.SetProgress(current, total);
        }

        /// <summary>
        /// 更新进度
        /// </summary>
        public void UpdateProgress(float progress)
        {
            _progressView?.SetProgress(progress);
        }

        /// <summary>
        /// 设置进度文本
        /// </summary>
        public void SetProgressText(string text)
        {
            _progressView?.SetText(text);
        }

        /// <summary>
        /// 隐藏进度
        /// </summary>
        public void HideProgress()
        {
            if (_progressView != null)
            {
                _progressView.gameObject.SetActive(false);
            }
        }

        #endregion

        #region Event Triggers (内部调用)

        internal void TriggerDialogueContinue()
        {
            OnDialogueContinue?.Invoke();
        }

        internal void TriggerChoiceSelected(int index)
        {
            OnChoiceSelected?.Invoke(index);
        }

        internal void TriggerSkipClicked()
        {
            OnSkipClicked?.Invoke();
        }

        internal void TriggerHighlightTargetClicked()
        {
            OnHighlightTargetClicked?.Invoke();
        }

        #endregion

        #region Utility

        /// <summary>
        /// 通过路径查找 UI 元素
        /// </summary>
        public RectTransform FindUIByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 尝试直接 Find
            var go = GameObject.Find(path);
            if (go != null)
            {
                return go.GetComponent<RectTransform>();
            }

            // 尝试从场景中的 Canvas 查找
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                var child = canvas.transform.Find(path);
                if (child != null)
                {
                    return child.GetComponent<RectTransform>();
                }
            }

            return null;
        }

        /// <summary>
        /// 隐藏所有 Tutorial UI
        /// </summary>
        public void HideAll()
        {
            HideDialogue();
            HideHighlight();
            HideWorldArrow();
            HideProgress();
        }

        /// <summary>
        /// 播放音效
        /// </summary>
        public void PlaySound(AudioClip clip)
        {
            if (clip == null || _config == null) return;

            // 使用 AudioSource.PlayClipAtPoint 或集成 AudioManager
            AudioSource.PlayClipAtPoint(clip, Camera.main?.transform.position ?? Vector3.zero, _config.SoundVolume);
        }

        /// <summary>
        /// 播放对话音效
        /// </summary>
        public void PlayDialogueSound()
        {
            PlaySound(_config?.DialogueSound);
        }

        /// <summary>
        /// 播放确认音效
        /// </summary>
        public void PlayConfirmSound()
        {
            PlaySound(_config?.ConfirmSound);
        }

        /// <summary>
        /// 播放步骤完成音效
        /// </summary>
        public void PlayStepCompleteSound()
        {
            PlaySound(_config?.StepCompleteSound);
        }

        /// <summary>
        /// 播放教程完成音效
        /// </summary>
        public void PlayTutorialCompleteSound()
        {
            PlaySound(_config?.TutorialCompleteSound);
        }

        #endregion

        #region Configuration

        /// <summary>
        /// 设置配置
        /// </summary>
        public void SetConfig(TutorialConfigSO config)
        {
            _config = config;

            // 重新初始化 UI
            if (_isInitialized)
            {
                CleanupUI();
                _isInitialized = false;
                InitializeUI();
            }
        }

        private void CleanupUI()
        {
            if (_dialogueView != null) Destroy(_dialogueView.gameObject);
            if (_highlightView != null) Destroy(_highlightView.gameObject);
            if (_arrowIndicator != null) Destroy(_arrowIndicator.gameObject);
            if (_progressView != null) Destroy(_progressView.gameObject);

            _dialogueView = null;
            _highlightView = null;
            _arrowIndicator = null;
            _progressView = null;
        }

        #endregion

        protected override void OnDestroy()
        {
            CleanupUI();
            base.OnDestroy();
        }
    }
}
