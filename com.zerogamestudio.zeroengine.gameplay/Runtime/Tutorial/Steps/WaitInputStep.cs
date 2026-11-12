using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 等待输入步骤 (v1.14.0+)
    /// 等待玩家按下指定按键
    /// </summary>
    [Serializable]
    public class WaitInputStep : TutorialStep
    {
        public override bool SupportsAutomaticCompletion => false;
        [Header("Input Settings")]
        [Tooltip("需要按下的按键列表")]
        public KeyCode[] RequiredKeys = new[] { KeyCode.Space };

        [Tooltip("需要全部按下 (否则任一即可)")]
        public bool RequireAll = false;

        [Tooltip("提示文本")]
        [TextArea(1, 2)]
        public string PromptText;

        [Tooltip("超时时间 (秒，0 无限等待)")]
        public float Timeout = 0f;

        [Tooltip("显示按键图标")]
        public bool ShowKeyIcon = true;

        // 运行时状态
        [NonSerialized]
        private bool _inputReceived;

        public override string StepType => "WaitInput";

        public override void OnEnter(TutorialContext ctx)
        {
            _inputReceived = false;

            // 显示提示
            if (!string.IsNullOrEmpty(PromptText))
            {
                TutorialUIManager.Instance?.ShowDialogue($"{PromptText} [{GetKeyDisplayText()}]");
            }
        }

        public override void OnUpdate(TutorialContext ctx)
        {
            if (_inputReceived) return;

            // 超时检查
            if (Timeout > 0 && ctx.StepElapsedTime >= Timeout)
            {
                ctx.Fail("The required input timed out.");
                return;
            }

            // 检测输入
            if (RequireAll)
            {
                bool allPressed = true;
                foreach (var key in RequiredKeys)
                {
                    if (!TutorialInputHelper.GetKey(key))
                    {
                        allPressed = false;
                        break;
                    }
                }
                _inputReceived = allPressed;
            }
            else
            {
                foreach (var key in RequiredKeys)
                {
                    if (TutorialInputHelper.GetKeyDown(key))
                    {
                        _inputReceived = true;
                        break;
                    }
                }
            }
        }

        public override void OnExit(TutorialContext ctx)
        {
            TutorialUIManager.Instance?.HidePrompt();
        }

        public override bool IsCompleted(TutorialContext ctx)
        {
            return _inputReceived;
        }

        public override string GetDisplayText()
        {
            return !string.IsNullOrEmpty(PromptText)
                ? PromptText
                : $"Press {GetKeyDisplayText()}";
        }

        private string GetKeyDisplayText()
        {
            if (RequiredKeys == null || RequiredKeys.Length == 0)
            {
                return "any key";
            }

            var keyNames = new string[RequiredKeys.Length];
            for (int i = 0; i < RequiredKeys.Length; i++)
            {
                keyNames[i] = RequiredKeys[i].ToString();
            }

            return RequireAll
                ? string.Join(" + ", keyNames)
                : string.Join(" or ", keyNames);
        }

        public override bool Validate(out string error)
        {
            if (RequiredKeys == null || RequiredKeys.Length == 0)
            {
                error = "At least one key is required";
                return false;
            }
            error = null;
            return true;
        }
    }
}
