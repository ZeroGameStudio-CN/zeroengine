using UnityEngine;
using System.Collections.Generic;
using TMPro;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程系统全局配置 (v1.14.0+)
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialConfig", menuName = "ZeroEngine/Tutorial/Tutorial Config")]
    public class TutorialConfigSO : ScriptableObject
    {
        #region General Settings

        [Header("General")]
        [Tooltip("启用教程系统")]
        public bool EnableTutorials = true;

        [Tooltip("新玩家自动开始教程")]
        public bool AutoStartForNewPlayers = true;

        [Tooltip("允许跳过教程")]
        public bool AllowSkipping = true;

        public bool UseUnscaledTime = true;
        public TMP_FontAsset DefaultFont;
        public Vector2 ReferenceResolution = new Vector2(1920, 1080);
        public TutorialFocusStyle FocusStyle = new TutorialFocusStyle();
        public List<TutorialMedia> Media = new List<TutorialMedia>();

        public TutorialMedia FindMedia(string id) => Media.Find(media => media != null && media.Id == id);

        #endregion

        #region UI Settings

        [Header("UI")]
        [Tooltip("对话 UI 预制体")]
        public GameObject DialogueUIPrefab;

        [Tooltip("高亮遮罩预制体")]
        public GameObject HighlightMaskPrefab;

        [Tooltip("箭头指示器预制体")]
        public GameObject ArrowIndicatorPrefab;

        [Tooltip("进度指示器预制体")]
        public GameObject ProgressIndicatorPrefab;

        [Header("UI Animation")]
        [Tooltip("UI 淡入时间")]
        public float UIFadeInDuration = 0.3f;

        [Tooltip("UI 淡出时间")]
        public float UIFadeOutDuration = 0.2f;

        [Tooltip("高亮脉冲速度")]
        public float HighlightPulseSpeed = 2f;

        [Tooltip("高亮脉冲幅度")]
        public float HighlightPulseAmount = 0.1f;

        #endregion

        #region Dialogue Settings

        [Header("Dialogue")]
        [Tooltip("默认打字机速度 (字符/秒)")]
        public float DefaultTypewriterSpeed = 30f;

        [Tooltip("对话显示延迟")]
        public float DialogueShowDelay = 0.2f;

        [Tooltip("确认按键")]
        public KeyCode DialogueConfirmKey = KeyCode.Space;

        [Tooltip("跳过按键")]
        public KeyCode SkipKey = KeyCode.Escape;

        [Tooltip("对话音效")]
        public AudioClip DialogueSound;

        [Tooltip("确认音效")]
        public AudioClip ConfirmSound;

        #endregion

        #region Highlight Settings

        [Header("Highlight")]
        [Tooltip("高亮颜色")]
        public Color HighlightColor = new Color(1f, 0.8f, 0f, 1f);

        [Tooltip("遮罩颜色")]
        public Color MaskColor = new Color(0f, 0f, 0f, 0.7f);

        [Tooltip("高亮边缘宽度")]
        public float HighlightBorderWidth = 4f;

        [Tooltip("高亮边距")]
        public float HighlightPadding = 10f;

        #endregion

        #region Arrow Settings

        [Header("Arrow Indicator")]
        [Tooltip("箭头颜色")]
        public Color ArrowColor = Color.yellow;

        [Tooltip("箭头大小")]
        public float ArrowSize = 50f;

        [Tooltip("箭头动画速度")]
        public float ArrowBounceSpeed = 3f;

        [Tooltip("箭头动画幅度")]
        public float ArrowBounceAmount = 10f;

        #endregion

        #region Progress Settings

        [Header("Progress")]
        [Tooltip("显示进度指示器")]
        public bool ShowProgressIndicator = true;

        [Tooltip("进度指示器位置")]
        public ProgressPosition ProgressPosition = ProgressPosition.TopRight;

        [Tooltip("进度颜色")]
        public Color ProgressColor = new Color(0.2f, 0.8f, 0.2f, 1f);

        #endregion

        #region Audio Settings

        [Header("Audio")]
        [Tooltip("步骤完成音效")]
        public AudioClip StepCompleteSound;

        [Tooltip("教程完成音效")]
        public AudioClip TutorialCompleteSound;

        [Tooltip("音效音量")]
        [Range(0f, 1f)]
        public float SoundVolume = 1f;

        #endregion

        #region Timing Settings

        [Header("Timing")]
        [Tooltip("步骤之间的延迟")]
        public float StepTransitionDelay = 0.5f;

        [Tooltip("高亮目标查找重试间隔")]
        public float TargetSearchInterval = 0.5f;

        [Tooltip("高亮目标查找最大尝试次数")]
        public int MaxTargetSearchAttempts = 10;

        #endregion

        #region Validation

        private void OnValidate()
        {
            DefaultTypewriterSpeed = Mathf.Max(1, DefaultTypewriterSpeed);
            UIFadeInDuration = Mathf.Max(0, UIFadeInDuration);
            UIFadeOutDuration = Mathf.Max(0, UIFadeOutDuration);
            HighlightBorderWidth = Mathf.Max(0, HighlightBorderWidth);
            HighlightPadding = Mathf.Max(0, HighlightPadding);
            ArrowSize = Mathf.Max(10, ArrowSize);
            StepTransitionDelay = Mathf.Max(0, StepTransitionDelay);
            SoundVolume = Mathf.Clamp01(SoundVolume);
        }

        #endregion
    }

    /// <summary>
    /// 进度指示器位置
    /// </summary>
    public enum ProgressPosition
    {
        TopLeft,
        TopCenter,
        TopRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
}
