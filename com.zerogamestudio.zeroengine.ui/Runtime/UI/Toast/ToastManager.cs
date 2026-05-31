using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Toast
{
    public sealed class ToastManager
    {
        private readonly List<ToastHandle> active = new List<ToastHandle>();
        private readonly Queue<ToastRequest> queued = new Queue<ToastRequest>();
        private readonly Dictionary<string, ToastHandle> dedupeLookup = new Dictionary<string, ToastHandle>();
        private int nextId = 1;
        private ToastSettings settings;
        private IToastTextResolver resolver;
        private IToastPresenter presenter;
        private bool warnedMissingPresenter;

        public int ActiveCount => active.Count;
        public int QueuedCount => queued.Count;

        public event Action<ToastHandle> ToastShown;
        public event Action<ToastHandle, ToastDismissReason> ToastDismissed;

        public void Configure(ToastSettings toastSettings, IToastTextResolver textResolver, IToastPresenter toastPresenter)
        {
            settings = toastSettings;
            resolver = textResolver;
            presenter = toastPresenter;
            warnedMissingPresenter = false;
        }

        public ToastHandle Show(ToastRequest request)
        {
            if (request == null || !request.HasText) return null;
            EnsureSettings();
            if (presenter == null && Application.isPlaying)
                ToastRuntimeBootstrap.EnsurePresenter(settings);

            var duplicate = FindDuplicate(request);
            if (duplicate != null)
                return HandleDuplicate(duplicate, request);

            if (active.Count >= settings.MaxVisible && !HandleOverflow(request))
                return null;

            return ShowNow(request);
        }

        public void ClearAll()
        {
            queued.Clear();
            for (int i = active.Count - 1; i >= 0; i--)
                DismissInternal(active[i], ToastDismissReason.Cleared);

            presenter?.ClearAll();
        }

        public void ClearGroup(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey)) return;
            RemoveQueuedGroup(groupKey);

            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (string.Equals(active[i].Request.GroupKey, groupKey, StringComparison.Ordinal))
                    DismissInternal(active[i], ToastDismissReason.Cleared);
            }
        }

        private ToastHandle ShowNow(ToastRequest request)
        {
            var handle = new ToastHandle(nextId++, request, DismissInternal);
            active.Add(handle);
            RegisterDedupe(handle);

            if (presenter != null)
                presenter.ShowToast(handle, ResolveText(request), ResolveStyle(request), ResolveTimings(request));
            else
                WarnMissingPresenter();

            Reposition();
            ToastShown?.Invoke(handle);
            return handle;
        }

        private bool HandleOverflow(ToastRequest incoming)
        {
            switch (settings.OverflowPolicy)
            {
                case ToastOverflowPolicy.DropIncoming:
                    return false;
                case ToastOverflowPolicy.Queue:
                    if (settings.MaxQueued <= 0) return false;
                    if (queued.Count >= settings.MaxQueued) queued.Dequeue();
                    queued.Enqueue(incoming);
                    return false;
                case ToastOverflowPolicy.ReplaceLowestPriority:
                    var lowest = FindLowestPriority();
                    if (lowest != null && lowest.Request.Priority <= incoming.Priority)
                    {
                        DismissInternal(lowest, ToastDismissReason.Overflow);
                        return true;
                    }
                    return false;
                case ToastOverflowPolicy.DropOldest:
                default:
                    if (active.Count > 0) DismissInternal(active[0], ToastDismissReason.Overflow);
                    return true;
            }
        }

        private ToastHandle HandleDuplicate(ToastHandle existing, ToastRequest request)
        {
            switch (settings.DuplicatePolicy)
            {
                case ToastDuplicatePolicy.IgnoreDuplicate:
                    return existing;
                case ToastDuplicatePolicy.ReplaceExisting:
                    DismissInternal(existing, ToastDismissReason.Replaced);
                    return ShowNow(request);
                case ToastDuplicatePolicy.RefreshExisting:
                    UnregisterDedupe(existing);
                    existing.ReplaceRequest(request);
                    RegisterDedupe(existing);
                    presenter?.RefreshToast(existing, ResolveText(request), ResolveStyle(request), ResolveTimings(request));
                    return existing;
                case ToastDuplicatePolicy.StackDuplicate:
                default:
                    return ShowNow(request);
            }
        }

        private ToastHandle FindDuplicate(ToastRequest request)
        {
            if (settings.DuplicatePolicy == ToastDuplicatePolicy.StackDuplicate) return null;
            if (string.IsNullOrWhiteSpace(request.DedupeKey)) return null;
            return dedupeLookup.TryGetValue(request.DedupeKey, out var handle) ? handle : null;
        }

        private ToastHandle FindLowestPriority()
        {
            ToastHandle lowest = null;
            for (int i = 0; i < active.Count; i++)
            {
                if (lowest == null || active[i].Request.Priority < lowest.Request.Priority)
                    lowest = active[i];
            }

            return lowest;
        }

        private void DismissInternal(ToastHandle handle, ToastDismissReason reason)
        {
            if (handle == null) return;
            if (!active.Remove(handle)) return;

            UnregisterDedupe(handle);
            handle.MarkDismissed();
            presenter?.DismissToast(handle, reason);
            handle.Request.OnDismissed?.Invoke(handle);
            ToastDismissed?.Invoke(handle, reason);
            Reposition();
            TryDrainQueue();
        }

        private void TryDrainQueue()
        {
            while (queued.Count > 0 && active.Count < settings.MaxVisible)
                ShowNow(queued.Dequeue());
        }

        private void Reposition()
        {
            var anchorCounts = new Dictionary<ToastAnchor, int>();
            for (int i = 0; i < active.Count; i++)
            {
                var anchor = active[i].Request.Anchor;
                anchorCounts.TryGetValue(anchor, out var anchorIndex);
                anchorCounts[anchor] = anchorIndex + 1;

                var signedIndex = IsBottomAnchor(anchor) ? -anchorIndex : anchorIndex;
                presenter?.RepositionToast(active[i], signedIndex, settings.Spacing);
            }
        }

        private void RemoveQueuedGroup(string groupKey)
        {
            if (queued.Count == 0) return;

            var kept = new Queue<ToastRequest>(queued.Count);
            while (queued.Count > 0)
            {
                var request = queued.Dequeue();
                if (!string.Equals(request.GroupKey, groupKey, StringComparison.Ordinal))
                    kept.Enqueue(request);
            }

            while (kept.Count > 0)
                queued.Enqueue(kept.Dequeue());
        }

        private void RegisterDedupe(ToastHandle handle)
        {
            var key = handle.Request.DedupeKey;
            if (!string.IsNullOrWhiteSpace(key)) dedupeLookup[key] = handle;
        }

        private void UnregisterDedupe(ToastHandle handle)
        {
            var key = handle.Request.DedupeKey;
            if (!string.IsNullOrWhiteSpace(key) && dedupeLookup.TryGetValue(key, out var current) && current == handle)
                dedupeLookup.Remove(key);
        }

        private string ResolveText(ToastRequest request)
        {
            return ToastSettings.ResolveText(request, resolver);
        }

        private ToastStyle ResolveStyle(ToastRequest request)
        {
            var style = settings.GetStyle(request.Severity);
            if (!request.OverrideColor.HasValue)
                return style;

            return new ToastStyle
            {
                Severity = style.Severity,
                BackgroundColor = style.BackgroundColor,
                AccentColor = request.OverrideColor.Value,
                TextColor = style.TextColor,
                Icon = style.Icon,
                Duration = style.Duration
            };
        }

        private ToastAnimationTimings ResolveTimings(ToastRequest request)
        {
            var timings = settings.AnimationTimings;
            timings.holdSeconds = request.Duration >= 0f ? request.Duration : settings.GetDuration(request.Severity);
            return timings;
        }

        private void EnsureSettings()
        {
            if (settings != null) return;
            settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
        }

        private static bool IsBottomAnchor(ToastAnchor anchor)
        {
            return anchor == ToastAnchor.BottomLeft
                   || anchor == ToastAnchor.BottomCenter
                   || anchor == ToastAnchor.BottomRight;
        }

        private void WarnMissingPresenter()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (warnedMissingPresenter || !Application.isPlaying) return;
            warnedMissingPresenter = true;
            Debug.LogWarning("[ZeroEngine.Toast] Toast was shown before a ToastRootPresenter was configured. Add ToastRootPresenter to an active UI Canvas.");
#endif
        }
    }

    internal static class ToastRuntimeBootstrap
    {
        private const string RootName = "ZeroEngine Toast Canvas";

        public static void EnsurePresenter(ToastSettings settings)
        {
            if (UnityEngine.Object.FindFirstObjectByType<ToastRootPresenter>() != null) return;

            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var itemPrefab = CreateItemPrefab(root.transform);
            itemPrefab.gameObject.SetActive(false);

            var topCenter = CreateContainer(root.transform, "TopCenter", ToastAnchor.TopCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), itemPrefab);
            var topRight = CreateContainer(root.transform, "TopRight", ToastAnchor.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), itemPrefab);
            var bottomCenter = CreateContainer(root.transform, "BottomCenter", ToastAnchor.BottomCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), itemPrefab);

            var presenter = root.AddComponent<ToastRootPresenter>();
            presenter.Configure(settings, new[] { topCenter, topRight, bottomCenter });
        }

        private static ToastContainer CreateContainer(Transform parent, string name, ToastAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, ToastItemView itemPrefab)
        {
            var containerObject = new GameObject(name, typeof(RectTransform), typeof(ToastContainer));
            containerObject.transform.SetParent(parent, false);

            var rect = (RectTransform)containerObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = Vector2.zero;

            var container = containerObject.GetComponent<ToastContainer>();
            container.Configure(anchor, rect, itemPrefab);
            return container;
        }

        private static ToastItemView CreateItemPrefab(Transform parent)
        {
            var root = new GameObject("ToastItemView", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button), typeof(ToastItemView));
            root.transform.SetParent(parent, false);

            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(300f, 100f);

            var background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.88235295f);

            var button = root.GetComponent<Button>();
            button.targetGraphic = background;

            var indicatorObject = new GameObject("Indicator", typeof(RectTransform), typeof(Image));
            indicatorObject.transform.SetParent(root.transform, false);
            var indicatorRect = (RectTransform)indicatorObject.transform;
            indicatorRect.anchorMin = new Vector2(0f, 0f);
            indicatorRect.anchorMax = new Vector2(0f, 1f);
            indicatorRect.pivot = new Vector2(0f, 0.5f);
            indicatorRect.anchoredPosition = Vector2.zero;
            indicatorRect.sizeDelta = new Vector2(10f, 0f);
            var indicator = indicatorObject.GetComponent<Image>();
            indicator.color = new Color(1f, 0f, 0.01f, 1f);
            indicator.raycastTarget = false;

            var textObject = new GameObject("Alert Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 6f);
            textRect.offsetMax = new Vector2(-12f, -6f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 34f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            var view = root.GetComponent<ToastItemView>();
            view.Configure(root.GetComponent<CanvasGroup>(), background, indicator, null, text, button);
            return view;
        }
    }
}
