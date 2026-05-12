using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.UI.Toast
{
    [CreateAssetMenu(fileName = "ToastSettings", menuName = "ZeroEngine/UI/Toast Settings")]
    public sealed class ToastSettings : ScriptableObject
    {
        [SerializeField] private int maxVisible = 5;
        [SerializeField] private int maxQueued = 12;
        [SerializeField] private float showInterval = 0.5f;
        [SerializeField] private float spacing = 112f;
        [SerializeField] private ToastOverflowPolicy overflowPolicy = ToastOverflowPolicy.Queue;
        [SerializeField] private ToastDuplicatePolicy duplicatePolicy = ToastDuplicatePolicy.RefreshExisting;
        [SerializeField] private ToastAnchor defaultAnchor = ToastAnchor.TopRight;
        [SerializeField] private ToastAnimationTimings animationTimings = ToastAnimationTimings.Default;
        [SerializeField] private List<ToastStyle> styles = new List<ToastStyle>();

        public int MaxVisible => Mathf.Max(1, maxVisible);
        public int MaxQueued => Mathf.Max(0, maxQueued);
        public float ShowInterval => Mathf.Max(0f, showInterval);
        public float Spacing => Mathf.Max(1f, spacing);
        public ToastOverflowPolicy OverflowPolicy => overflowPolicy;
        public ToastDuplicatePolicy DuplicatePolicy => duplicatePolicy;
        public ToastAnchor DefaultAnchor => defaultAnchor;
        public ToastAnimationTimings AnimationTimings => animationTimings;
        public IReadOnlyList<ToastStyle> Styles => styles;

        public ToastStyle GetStyle(ToastSeverity severity)
        {
            EnsureDefaultStyles();

            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] != null && styles[i].Severity == severity)
                    return styles[i];
            }

            return styles.Count > 0 ? styles[0] : new ToastStyle();
        }

        public float GetDuration(ToastSeverity severity)
        {
            var style = GetStyle(severity);
            return Mathf.Max(0.1f, style.Duration);
        }

        public void ResetToDefaults()
        {
            maxVisible = 5;
            maxQueued = 12;
            showInterval = 0.5f;
            spacing = 112f;
            overflowPolicy = ToastOverflowPolicy.Queue;
            duplicatePolicy = ToastDuplicatePolicy.RefreshExisting;
            defaultAnchor = ToastAnchor.TopRight;
            animationTimings = ToastAnimationTimings.Default;
            styles = new List<ToastStyle>();
            EnsureDefaultStyles();
        }

        public static string ResolveText(ToastRequest request, IToastTextResolver resolver)
        {
            if (request == null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(request.TextKey) && resolver != null)
            {
                var resolved = resolver.ResolveToastText(request);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            if (!string.IsNullOrWhiteSpace(request.Message))
                return request.Message;

            return request.TextKey ?? string.Empty;
        }

        private void OnValidate()
        {
            maxVisible = Mathf.Max(1, maxVisible);
            maxQueued = Mathf.Max(0, maxQueued);
            showInterval = Mathf.Max(0f, showInterval);
            spacing = Mathf.Max(1f, spacing);
            EnsureDefaultStyles();
        }

        private void EnsureDefaultStyles()
        {
            styles ??= new List<ToastStyle>();
            AddMissingStyle(ToastSeverity.Info, new Color(0.18f, 0.42f, 0.86f, 1f), 2f);
            AddMissingStyle(ToastSeverity.Success, new Color(0.17f, 0.64f, 0.35f, 1f), 2f);
            AddMissingStyle(ToastSeverity.Warning, new Color(0.92f, 0.63f, 0.16f, 1f), 2.4f);
            AddMissingStyle(ToastSeverity.Error, new Color(0.86f, 0.25f, 0.22f, 1f), 2.8f);
            AddMissingStyle(ToastSeverity.Critical, new Color(0.72f, 0.12f, 0.14f, 1f), 3.5f);
        }

        private void AddMissingStyle(ToastSeverity severity, Color accent, float duration)
        {
            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] != null && styles[i].Severity == severity)
                    return;
            }

            styles.Add(new ToastStyle
            {
                Severity = severity,
                AccentColor = accent,
                Duration = duration
            });
        }
    }
}
