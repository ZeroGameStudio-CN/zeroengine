using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 等待事件步骤 (v1.14.0+)
    /// 等待特定游戏事件发生
    /// </summary>
    [Serializable]
    public class WaitEventStep : TutorialStep
    {
        public override bool SupportsAutomaticCompletion => false;
        [Header("Event Settings")]
        [Tooltip("事件键")]
        public string EventKey;

        [Tooltip("期望的值 (留空表示任意值)")]
        public string ExpectedValue;

        [Tooltip("提示文本")]
        [TextArea(1, 2)]
        public string PromptText;

        [Tooltip("超时时间 (秒，0 无限等待)")]
        public float Timeout = 0f;

        // 运行时状态
        [NonSerialized]
        private bool _eventReceived;

        // 静态事件触发器
        [NonSerialized] private IDisposable subscription;

        public override string StepType => "WaitEvent";

        public override void OnEnter(TutorialContext ctx)
        {
            _eventReceived = false;

            // 注册事件监听
            subscription = ctx.Focus.Signals.Subscribe(HandleEvent);

            // 显示提示
            if (!string.IsNullOrEmpty(PromptText))
            {
                TutorialUIManager.Instance?.ShowPrompt(PromptText, null);
            }
        }

        public override void OnUpdate(TutorialContext ctx)
        {
            if (_eventReceived) return;

            // 超时检查
            if (Timeout > 0 && ctx.StepElapsedTime >= Timeout)
            {
                ctx.Fail("The required event timed out.");
            }
        }

        public override void OnExit(TutorialContext ctx)
        {
            subscription?.Dispose(); subscription = null;
            UnityEngine.Object.FindObjectOfType<TutorialUIManager>()?.HidePrompt();
        }

        public override bool IsCompleted(TutorialContext ctx)
        {
            return _eventReceived;
        }

        private void HandleEvent(string key, string value)
        {
            if (key != EventKey) return;

            if (string.IsNullOrEmpty(ExpectedValue) || value == ExpectedValue)
            {
                _eventReceived = true;
            }
        }

        public override string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(PromptText))
            {
                return PromptText;
            }

            return string.IsNullOrEmpty(ExpectedValue)
                ? $"Wait for event: {EventKey}"
                : $"Wait for event: {EventKey}={ExpectedValue}";
        }

        public override bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(EventKey))
            {
                error = "Event key is required";
                return false;
            }
            error = null;
            return true;
        }

        #region Static API

        /// <summary>
        /// 触发教程事件 (供游戏逻辑调用)
        /// </summary>
        public static void TriggerEvent(string key, string value = null)
        {
            var manager = UnityEngine.Object.FindObjectOfType<TutorialManager>();
            if (manager && manager.Enabled && manager.IsRunning) manager.Context.Signal(key, value ?? string.Empty);
        }

        /// <summary>
        /// 触发教程事件 (整数值)
        /// </summary>
        public static void TriggerEvent(string key, int value)
        {
            TriggerEvent(key, value.ToString());
        }

        /// <summary>
        /// 触发教程事件 (布尔值)
        /// </summary>
        public static void TriggerEvent(string key, bool value)
        {
            TriggerEvent(key, value.ToString());
        }

        #endregion
    }
}
