using System;

namespace ZeroEngine.UI.Toast
{
    public enum ToastSeverity
    {
        Info,
        Success,
        Warning,
        Error,
        Critical
    }

    public enum ToastPriority
    {
        Low = 0,
        Normal = 100,
        High = 200,
        Critical = 300
    }

    public enum ToastAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    public enum ToastOverflowPolicy
    {
        DropOldest,
        Queue,
        ReplaceLowestPriority,
        DropIncoming
    }

    public enum ToastDuplicatePolicy
    {
        StackDuplicate,
        IgnoreDuplicate,
        RefreshExisting,
        ReplaceExisting
    }

    public enum ToastDismissReason
    {
        Expired,
        Clicked,
        Cleared,
        Replaced,
        Overflow,
        OwnerDisabled
    }

    [Serializable]
    public struct ToastAnimationTimings
    {
        public float fadeInSeconds;
        public float holdSeconds;
        public float fadeOutSeconds;
        public float moveSeconds;

        public static ToastAnimationTimings Default => new ToastAnimationTimings
        {
            fadeInSeconds = 0.16f,
            holdSeconds = 2f,
            fadeOutSeconds = 0.22f,
            moveSeconds = 0.16f
        };
    }
}
