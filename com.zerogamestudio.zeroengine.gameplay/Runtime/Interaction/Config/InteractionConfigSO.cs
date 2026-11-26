using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互系统全局配置 (v1.14.0+)
    /// </summary>
    [CreateAssetMenu(fileName = "InteractionConfig", menuName = "ZeroEngine/Gameplay/Interaction/Interaction Config")]
    public class InteractionConfigSO : ScriptableObject
    {
        #region Detection Settings

        [Header("Detection")]
        [Tooltip("默认检测半径")]
        public float DefaultDetectionRadius = 5f;

        [Tooltip("默认交互距离")]
        public float DefaultInteractionDistance = 2f;

        [Tooltip("检测频率 (每秒次数)")]
        [Range(1, 60)]
        public int DetectionRate = 10;

        #endregion

        #region Input Settings

        [Header("Input")]
        [Tooltip("交互按键 (旧输入系统)")]
        public KeyCode InteractKey = KeyCode.E;

        [Tooltip("使用新输入系统")]
        public bool UseNewInputSystem = false;

        [Tooltip("交互 Action 名称 (新输入系统)")]
        public string InteractActionName = "Interact";

        #endregion

        #region Visual Settings

        [Header("Visual Feedback")]
        [Tooltip("启用轮廓高亮")]
        public bool EnableOutline = true;

        [Tooltip("高亮颜色")]
        public Color OutlineColor = Color.yellow;

        [Tooltip("高亮宽度")]
        [Range(0, 10)]
        public float OutlineWidth = 3f;

        #endregion

        #region UI Settings

        [Header("UI")]
        [Tooltip("交互提示预制体")]
        public GameObject PromptUIPrefab;

        [Tooltip("提示显示延迟 (秒)")]
        public float PromptShowDelay = 0f;

        [Tooltip("提示淡入时间 (秒)")]
        public float PromptFadeInDuration = 0.2f;

        [Tooltip("提示淡出时间 (秒)")]
        public float PromptFadeOutDuration = 0.15f;

        [Tooltip("提示偏移 (屏幕空间)")]
        public Vector2 PromptOffset = new Vector2(0, 50);

        #endregion

        #region Hint Templates

        [Header("Hint Templates")]
        [Tooltip("交互提示模板 (按类型)")]
        public List<InteractionHintTemplate> HintTemplates = new()
        {
            new InteractionHintTemplate { Type = InteractionType.Pickup, Template = "[E] Pick up {0}" },
            new InteractionHintTemplate { Type = InteractionType.Talk, Template = "[E] Talk to {0}" },
            new InteractionHintTemplate { Type = InteractionType.Open, Template = "[E] Open {0}" },
            new InteractionHintTemplate { Type = InteractionType.Use, Template = "[E] Use {0}" },
            new InteractionHintTemplate { Type = InteractionType.Examine, Template = "[E] Examine {0}" },
            new InteractionHintTemplate { Type = InteractionType.Activate, Template = "[E] Activate {0}" },
            new InteractionHintTemplate { Type = InteractionType.Enter, Template = "[E] Enter {0}" },
            new InteractionHintTemplate { Type = InteractionType.Craft, Template = "[E] Craft at {0}" },
            new InteractionHintTemplate { Type = InteractionType.Custom, Template = "[E] Interact with {0}" }
        };

        // 缓存模板查找
        private Dictionary<InteractionType, string> _templateCache;

        /// <summary>
        /// 获取指定类型的提示模板
        /// </summary>
        public string GetHintTemplate(InteractionType type)
        {
            if (_templateCache == null)
            {
                _templateCache = new Dictionary<InteractionType, string>();
                foreach (var hint in HintTemplates)
                {
                    _templateCache[hint.Type] = hint.Template;
                }
            }

            if (_templateCache.TryGetValue(type, out var template))
            {
                return template;
            }
            return "[E] Interact with {0}";
        }

        /// <summary>
        /// 格式化交互提示
        /// </summary>
        public string FormatHint(InteractionType type, string displayName)
        {
            return string.Format(GetHintTemplate(type), displayName);
        }

        #endregion

        #region Audio Settings

        [Header("Audio")]
        [Tooltip("交互成功音效")]
        public AudioClip InteractSuccessSound;

        [Tooltip("交互失败音效")]
        public AudioClip InteractFailedSound;

        [Tooltip("获得焦点音效")]
        public AudioClip FocusSound;

        #endregion

        #region Validation

        private void OnValidate()
        {
            DefaultDetectionRadius = Mathf.Max(0, DefaultDetectionRadius);
            DefaultInteractionDistance = Mathf.Max(0, DefaultInteractionDistance);
            DetectionRate = Mathf.Clamp(DetectionRate, 1, 60);
            PromptShowDelay = Mathf.Max(0, PromptShowDelay);
            PromptFadeInDuration = Mathf.Max(0, PromptFadeInDuration);
            PromptFadeOutDuration = Mathf.Max(0, PromptFadeOutDuration);

            // 清除缓存以便重新构建
            _templateCache = null;
        }

        #endregion
    }

    /// <summary>
    /// 交互提示模板
    /// </summary>
    [Serializable]
    public struct InteractionHintTemplate
    {
        public InteractionType Type;
        public string Template;
    }
}
