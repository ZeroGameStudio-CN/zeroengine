using System;
using UnityEngine;

namespace ZeroEngine.UI.Toast
{
    [Serializable]
    public sealed class ToastRequest
    {
        public string Message;
        public string TextKey;
        public string DedupeKey;
        public string GroupKey;
        public ToastSeverity Severity = ToastSeverity.Info;
        public ToastPriority Priority = ToastPriority.Normal;
        public ToastAnchor Anchor = ToastAnchor.TopRight;
        public float Duration = -1f;
        public Sprite Icon;
        public Color? OverrideColor;
        public bool PauseWithGameTime;
        public bool DismissOnClick = true;
        public Action<ToastHandle> OnClick;
        public Action<ToastHandle> OnDismissed;

        public bool HasText => !string.IsNullOrWhiteSpace(Message) || !string.IsNullOrWhiteSpace(TextKey);

        public static ToastRequest Text(string message)
        {
            return new ToastRequest { Message = message };
        }

        public static ToastRequest Key(string textKey)
        {
            return new ToastRequest { TextKey = textKey };
        }

        public ToastRequest WithSeverity(ToastSeverity severity)
        {
            Severity = severity;
            return this;
        }

        public ToastRequest WithPriority(ToastPriority priority)
        {
            Priority = priority;
            return this;
        }

        public ToastRequest WithDuration(float seconds)
        {
            Duration = seconds;
            return this;
        }

        public ToastRequest WithGroup(string groupKey)
        {
            GroupKey = groupKey;
            return this;
        }

        public ToastRequest WithDedupe(string dedupeKey)
        {
            DedupeKey = dedupeKey;
            return this;
        }

        public ToastRequest At(ToastAnchor anchor)
        {
            Anchor = anchor;
            return this;
        }

        public ToastRequest WithIcon(Sprite icon)
        {
            Icon = icon;
            return this;
        }

        public ToastRequest OnClicked(Action<ToastHandle> callback)
        {
            OnClick = callback;
            return this;
        }

        public ToastRequest OnClosed(Action<ToastHandle> callback)
        {
            OnDismissed = callback;
            return this;
        }
    }
}
