using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ZeroEngine.UI.Combat
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class StackMarkerView : MonoBehaviour
    {
        private const int AutomaticMarkerLimit = 5;
        private const int DefaultMarksPerLine = 4;
        private const int MaxPerStackColumns = 6;

        public const int MaxMarksPerRow = 8;

        private const float MarkSize = 24f;
        private const float MarkSpacing = 26f;
        private const float LineSpacing = 27f;

        public enum LayoutMode
        {
            Automatic,
            PerStack,
            Wrapped,
            IconWithCount
        }

        public readonly struct Row
        {
            public string Key { get; }
            public Sprite Icon { get; }
            public GameObject Prefab { get; }
            public int Count { get; }
            public int Capacity { get; }
            public LayoutMode Mode { get; }
            public int MarksPerLine { get; }
            public GameObject GainFeedback { get; }
            public GameObject SpendFeedback { get; }

            public Row(string key, Sprite icon, int count, int capacity, LayoutMode mode = LayoutMode.Automatic,
                int marksPerLine = DefaultMarksPerLine, GameObject prefab = null, GameObject gainFeedback = null, GameObject spendFeedback = null)
            {
                Key = key;
                Icon = icon;
                Prefab = prefab;
                Count = count;
                Capacity = capacity;
                Mode = mode;
                MarksPerLine = marksPerLine == 0 ? DefaultMarksPerLine : Mathf.Clamp(marksPerLine, 1, MaxMarksPerRow);
                GainFeedback = gainFeedback;
                SpendFeedback = spendFeedback;
            }

            public Row WithCount(int count) => WithCounts(count, Capacity);

            public Row WithCounts(int count, int capacity) =>
                new(Key, Icon, count, capacity, Mode, MarksPerLine, Prefab, GainFeedback, SpendFeedback);
        }

        public event Action<GameObject, Vector3> FeedbackRequested;

        private readonly struct Cue
        {
            public readonly GameObject Prefab;
            public readonly Vector2 Offset;

            public Cue(GameObject prefab, Vector2 offset)
            {
                Prefab = prefab;
                Offset = offset;
            }
        }

        private struct LayoutBounds
        {
            public Vector2 Min { get; private set; }
            public Vector2 Max { get; private set; }
            private bool hasValue;

            public void Include(Vector2 min, Vector2 max)
            {
                Min = hasValue ? Vector2.Min(Min, min) : min;
                Max = hasValue ? Vector2.Max(Max, max) : max;
                hasValue = true;
            }

            public void IncludeMarker(Vector2 position)
            {
                var radius = Vector2.one * (MarkSize * .5f);
                Include(position - radius, position + radius);
            }
        }

        private readonly List<VisualRow> views = new();
        private readonly HashSet<string> seenKeys = new(StringComparer.Ordinal);
        private readonly Queue<Cue> cues = new();
        private Func<GameObject, IEnumerable<IDisposable>> acquirePrefabLifetimes;
        private Canvas canvas;
        private Transform worldRoot;
        private Transform stagingRoot;
        private LayoutBounds layoutBounds;
        private int visibleRows;

        public static StackMarkerView Create(Canvas canvas, string name = "Stack Markers",
            Func<GameObject, IEnumerable<IDisposable>> acquirePrefabLifetimes = null)
        {
            if (!canvas) throw new ArgumentNullException(nameof(canvas));

            var target = canvas.rootCanvas;
            var frame = (RectTransform)target.transform;
            var container = new GameObject(name, typeof(RectTransform)) { layer = target.gameObject.layer };
            container.SetActive(false);
            var rect = (RectTransform)container.transform;
            rect.SetParent(frame, false);
            rect.anchorMin = rect.anchorMax = frame.pivot;
            rect.sizeDelta = Vector2.zero;
            var view = container.AddComponent<StackMarkerView>();
            view.canvas = target;
            view.acquirePrefabLifetimes = acquirePrefabLifetimes;
            return view;
        }

        public void SetRows(IReadOnlyList<Row> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            seenKeys.Clear();
            layoutBounds = default;
            int usedLines = 0;
            visibleRows = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Capacity < 1 || row.Count <= 0 || (!row.Prefab && !row.Icon)) continue;

                var key = !string.IsNullOrEmpty(row.Key) && seenKeys.Add(row.Key) ? row.Key : null;
                var visual = UseVisualRow(key);
                var count = Mathf.Min(row.Capacity, row.Count);
                var plan = new LayoutPlan(row, count, usedLines);
                visual.Draw(transform, row, plan, count, ref layoutBounds);

                var lastMark = plan.Position(count - 1);
                if (key != null)
                {
                    if (count > visual.Count) RequestFeedback(row.GainFeedback, lastMark);
                    else if (count < visual.Count) RequestFeedback(row.SpendFeedback, visual.LastMark);
                }
                visual.Count = count;
                visual.LastMark = lastMark;
                visual.SpendFeedback = row.SpendFeedback;
                usedLines += plan.Lines;
            }

            for (int i = visibleRows; i < views.Count; i++)
            {
                var visual = views[i];
                if (visual.Key != null) RequestFeedback(visual.SpendFeedback, visual.LastMark);
                visual.Clear();
            }

            if (visibleRows == 0)
            {
                SetVisible(false);
                ReleaseWorldRoot();
            }
        }

        private VisualRow UseVisualRow(string key)
        {
            // A new row may take a free slot, but must leave existing keyed rows available for this snapshot.
            int free = -1;
            for (int i = visibleRows; i < views.Count; i++)
            {
                if (string.Equals(views[i].Key, key, StringComparison.Ordinal)) return UseSlot(i);
                if (views[i].Key == null && free < 0) free = i;
            }

            if (free < 0)
            {
                free = views.Count;
                views.Add(new VisualRow());
            }
            var visual = UseSlot(free);
            visual.Clear();
            visual.Key = key;
            return visual;
        }

        private VisualRow UseSlot(int index)
        {
            var view = views[index];
            views[index] = views[visibleRows];
            views[visibleRows++] = view;
            return view;
        }

        public void Place(Camera camera, Vector3 worldAnchor, float verticalOffset = 0f)
        {
            if ((visibleRows == 0 && cues.Count == 0) || !camera || !camera.isActiveAndEnabled)
            {
                Hide();
                return;
            }

            if (!canvas)
            {
                var parentCanvas = GetComponentInParent<Canvas>(true);
                if (parentCanvas) canvas = parentCanvas.rootCanvas;
            }

            if (!canvas || !canvas.isActiveAndEnabled || transform.parent is not RectTransform frame)
            {
                Hide();
                return;
            }

            var screenPoint = camera.WorldToScreenPoint(worldAnchor);
            var canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (screenPoint.z <= 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, screenPoint, canvasCamera, out var local) ||
                !frame.rect.Contains(local))
            {
                Hide();
                return;
            }

            var bounds = layoutBounds;
            foreach (var cue in cues) bounds.IncludeMarker(cue.Offset);
            FitToFrame(frame.rect, local, verticalOffset, bounds);
            if (visibleRows > 0 && !gameObject.activeSelf) gameObject.SetActive(true);
            for (int i = 0; i < visibleRows; i++)
                views[i].Place(this, camera, canvasCamera, screenPoint.z);

            SetVisible(visibleRows > 0);
            PlayFeedback(camera, canvasCamera, screenPoint.z);
        }

        private void FitToFrame(Rect frame, Vector2 anchor, float verticalOffset, LayoutBounds bounds)
        {
            var size = bounds.Max - bounds.Min;
            var scale = Mathf.Min(1f, Mathf.Min(frame.width / size.x, frame.height / size.y));
            var y = Mathf.Round(anchor.y) + verticalOffset;

            var position = new Vector3(
                Mathf.Clamp(Mathf.Round(anchor.x), frame.xMin - bounds.Min.x * scale, frame.xMax - bounds.Max.x * scale),
                Mathf.Clamp(y, frame.yMin - bounds.Min.y * scale, frame.yMax - bounds.Max.y * scale),
                0f);
            if (transform.localPosition != position) transform.localPosition = position;
            var localScale = new Vector3(scale, scale, 1f);
            if (transform.localScale != localScale) transform.localScale = localScale;
            if (worldRoot && worldRoot.localScale.x != scale) worldRoot.localScale = Vector3.one * scale;
        }

        public void Hide()
        {
            cues.Clear();
            SetVisible(false);
        }

        public void ClearRows()
        {
            Hide();
            ReleaseRows();
        }

        private void OnDisable()
        {
            if (worldRoot) worldRoot.gameObject.SetActive(false);
        }

        private void OnDestroy() => ReleaseRows();

        private void ReleaseRows()
        {
            foreach (var row in views) row.Clear();
            visibleRows = 0;
            layoutBounds = default;
            cues.Clear();
            ReleaseWorldRoot();
        }

        private void ReleaseWorldRoot()
        {
            if (!worldRoot) return;
            var root = worldRoot.gameObject;
            worldRoot = null;
            root.SetActive(false);
            Destroy(root);
        }

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (worldRoot && worldRoot.gameObject.activeSelf != visible) worldRoot.gameObject.SetActive(visible);
        }

        private void RequestFeedback(GameObject prefab, Vector2 offset)
        {
            if (prefab && FeedbackRequested != null) cues.Enqueue(new Cue(prefab, offset));
        }

        private void PlayFeedback(Camera camera, Camera canvasCamera, float depth)
        {
            if (cues.Count == 0) return;
            var handlers = FeedbackRequested?.GetInvocationList();
            if (handlers == null)
            {
                cues.Clear();
                return;
            }

            for (int pending = cues.Count; pending > 0 && cues.Count > 0; pending--)
            {
                var cue = cues.Dequeue();
                var point = AtDepth(camera, canvasCamera, depth, transform.TransformPoint(cue.Offset));
                foreach (var handler in handlers)
                {
                    try { ((Action<GameObject, Vector3>)handler)(cue.Prefab, point); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            }
        }

        private Transform GetWorldRoot()
        {
            if (worldRoot) return worldRoot;
            var container = new GameObject("World Stack Markers");
            container.SetActive(false);
            worldRoot = container.transform;
            worldRoot.localScale = Vector3.one * transform.localScale.x;
            return worldRoot;
        }

        private Transform GetStagingRoot()
        {
            if (stagingRoot) return stagingRoot;
            var container = new GameObject("Stack Marker Prefab Staging");
            container.SetActive(false);
            stagingRoot = container.transform;
            stagingRoot.SetParent(transform, false);
            return stagingRoot;
        }

        private static bool IsWorldPrefab(GameObject prefab) => prefab &&
            (prefab.transform is not RectTransform ||
             (prefab.TryGetComponent<Canvas>(out var ownCanvas) && ownCanvas.renderMode == RenderMode.WorldSpace));

        private static Vector3 AtDepth(Camera camera, Camera canvasCamera, float depth, Vector3 canvasPoint)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(canvasCamera, canvasPoint);
            return camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        }

        private readonly struct LayoutPlan
        {
            private const float CounterWidth = 114f;
            public readonly int Lines;
            public readonly bool Compact;
            private readonly int columns;
            private readonly int capacity;
            private readonly int firstLine;
            private readonly int extraLines;
            private readonly int splitAfter;
            private readonly float counterWidth;

            public LayoutPlan(Row row, int count, int firstLine)
            {
                Lines = 1;
                this.firstLine = firstLine;
                capacity = row.Capacity;
                Compact = row.Mode == LayoutMode.IconWithCount ||
                    (row.Mode == LayoutMode.Automatic && capacity > AutomaticMarkerLimit);

                var balancedLines = (capacity - 1) / MaxPerStackColumns + 1;
                columns = row.Mode switch
                {
                    LayoutMode.PerStack => capacity / balancedLines,
                    LayoutMode.Wrapped => row.MarksPerLine,
                    _ => capacity
                };
                extraLines = row.Mode == LayoutMode.PerStack ? capacity % balancedLines : 0;
                splitAfter = columns * extraLines + extraLines;
                counterWidth = Compact ? Mathf.Min(CounterWidth, 15f + capacity.ToString().Length * 13f) : 0f;
                if (!Compact) Lines = LocateLine(count - 1, out _, out _) + 1;
            }

            public Vector2 Position(int index)
            {
                if (Compact) return new Vector2(-counterWidth * .5f, firstLine * LineSpacing);
                int line = LocateLine(index, out var column, out var lineCapacity);
                return new Vector2((column - (lineCapacity - 1) * .5f) * MarkSpacing,
                    (firstLine + line) * LineSpacing);
            }

            private int LocateLine(int index, out int column, out int lineCapacity)
            {
                if (index < splitAfter)
                {
                    lineCapacity = columns + 1;
                    column = index % lineCapacity;
                    return index / lineCapacity;
                }

                int relative = index - splitAfter;
                column = relative % columns;
                lineCapacity = Mathf.Min(columns, capacity - (index - column));
                return extraLines + relative / columns;
            }

            public Vector2 LabelPosition => Position(0) + new Vector2(MarkSize * .5f + 4f, 0f);
        }

        private sealed class VisualRow
        {
            private readonly List<Marker> marks = new();
            private TextMeshProUGUI label;
            private int labelCount = -1;
            private int labelCapacity;
            private float labelWidth;
            private int activeMarks;

            public string Key { get; set; }
            public int Count { get; set; }
            public Vector2 LastMark { get; set; }
            public GameObject SpendFeedback { get; set; }

            public void Draw(Transform parent, Row row, LayoutPlan plan, int count, ref LayoutBounds bounds)
            {
                var previousMarks = activeMarks;
                activeMarks = plan.Compact ? 1 : count;
                for (int i = 0; i < activeMarks; i++)
                {
                    if (i == marks.Count) marks.Add(new Marker(parent));
                    var position = plan.Position(i);
                    marks[i].Bind(row.Icon, row.Prefab, position);
                    bounds.IncludeMarker(position);
                }
                for (int i = activeMarks; i < previousMarks; i++) marks[i].Release();

                if (!plan.Compact)
                {
                    if (label) label.gameObject.SetActive(false);
                    return;
                }

                if (!label) label = NewLabel(parent);
                if (labelCount != count)
                {
                    labelCount = count;
                    label.text = "×" + count;
                }
                if (labelCapacity != row.Capacity)
                {
                    labelCapacity = row.Capacity;
                    labelWidth = label.GetPreferredValues("×" + row.Capacity).x;
                }

                var labelPosition = plan.LabelPosition;
                if (label.rectTransform.anchoredPosition != labelPosition)
                    label.rectTransform.anchoredPosition = labelPosition;
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                var halfHeight = label.rectTransform.sizeDelta.y * .5f;
                bounds.Include(labelPosition - new Vector2(0f, halfHeight),
                    labelPosition + new Vector2(Mathf.Max(labelWidth, label.preferredWidth), halfHeight));
            }

            public void Place(StackMarkerView owner, Camera camera, Camera canvasCamera, float depth)
            {
                for (int i = 0; i < activeMarks; i++) marks[i].Place(owner, camera, canvasCamera, depth);
            }

            public void Clear()
            {
                for (int i = 0; i < activeMarks; i++) marks[i].Release();
                activeMarks = 0;
                Key = null;
                Count = 0;
                SpendFeedback = null;
                if (label) label.gameObject.SetActive(false);
            }

            private static TextMeshProUGUI NewLabel(Transform parent)
            {
                var node = new GameObject("Stack Count", typeof(RectTransform), typeof(TextMeshProUGUI));
                node.layer = parent.gameObject.layer;
                var text = node.GetComponent<TextMeshProUGUI>();
                text.rectTransform.SetParent(parent, false);
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.one * .5f;
                text.rectTransform.pivot = new Vector2(0f, .5f);
                text.rectTransform.sizeDelta = new Vector2(128f, 28f);
                text.fontSize = 22;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.raycastTarget = false;
                return text;
            }
        }

        private sealed class Marker
        {
            private readonly RectTransform anchor;
            private Image image;
            private Sprite icon;
            private GameObject template;
            private GameObject instance;
            private List<IDisposable> lifetimes;
            private bool world;
            private bool failed;

            public Marker(Transform parent)
            {
                var node = new GameObject("Stack Marker", typeof(RectTransform)) { layer = parent.gameObject.layer };
                anchor = (RectTransform)node.transform;
                anchor.SetParent(parent, false);
                anchor.anchorMin = anchor.anchorMax = Vector2.one * .5f;
                anchor.sizeDelta = Vector2.one * MarkSize;
            }

            public void Bind(Sprite sprite, GameObject prefab, Vector2 position)
            {
                if (template != prefab)
                {
                    DestroyPrefab();
                    template = prefab;
                    world = IsWorldPrefab(prefab);
                    failed = false;
                }

                icon = sprite;
                if (anchor.anchoredPosition != position) anchor.anchoredPosition = position;
                if (!anchor.gameObject.activeSelf) anchor.gameObject.SetActive(true);
                if (!template || failed) ShowSprite();
                else if (image && image.enabled) image.enabled = false;
            }

            public void Place(StackMarkerView owner, Camera camera, Camera canvasCamera, float depth)
            {
                if (!template || failed) return;
                if (!instance)
                {
                    // A prefab can destroy its own root. Its leases still belong to the old instance.
                    DestroyPrefab();
                    try { CreatePrefab(owner); }
                    catch (Exception exception)
                    {
                        DestroyPrefab();
                        failed = true;
                        Debug.LogException(exception, owner);
                        ShowSprite();
                        return;
                    }
                }
                if (world) instance.transform.position = AtDepth(camera, canvasCamera, depth, anchor.position);
                if (!instance.activeSelf) instance.SetActive(true);
            }

            private void CreatePrefab(StackMarkerView owner)
            {
                // Acquire the lease before a new prefab can play or return itself to a pool.
                instance = Instantiate(template, owner.GetStagingRoot(), false);
                instance.SetActive(false);
                var acquired = owner.acquirePrefabLifetimes?.Invoke(instance);
                if (acquired != null)
                    foreach (var lifetime in acquired)
                        if (lifetime != null)
                            (lifetimes ??= new List<IDisposable>()).Add(lifetime);

                instance.transform.SetParent(world ? owner.GetWorldRoot() : anchor, false);
                if (world) return;

                var rect = (RectTransform)instance.transform;
                // Resolve stretched anchors against the marker slot before switching to centered anchors.
                var size = rect.rect.size;
                rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
                rect.sizeDelta = size;

                var scale = rect.localScale;
                var rotation = rect.localRotation;
                var widthAxis = rotation * new Vector3(size.x * scale.x, 0f, 0f);
                var heightAxis = rotation * new Vector3(0f, size.y * scale.y, 0f);
                var width = Mathf.Abs(widthAxis.x) + Mathf.Abs(heightAxis.x);
                var height = Mathf.Abs(widthAxis.y) + Mathf.Abs(heightAxis.y);
                var fit = Mathf.Min(1f, Mathf.Min(MarkSize / width, MarkSize / height));

                rect.localScale = scale * fit;
                var center = rotation * Vector3.Scale(rect.rect.center, rect.localScale);
                rect.anchoredPosition = -center;
                foreach (var graphic in instance.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }

            private void ShowSprite()
            {
                if (!icon)
                {
                    if (image) image.enabled = false;
                    return;
                }
                if (!image)
                {
                    image = anchor.gameObject.AddComponent<Image>();
                    image.raycastTarget = false;
                    image.preserveAspect = true;
                }
                if (image.sprite != icon) image.sprite = icon;
                if (!image.enabled) image.enabled = true;
            }

            public void Release()
            {
                DestroyPrefab();
                template = null;
                icon = null;
                failed = false;
                if (image) image.sprite = null;
                if (anchor && anchor.gameObject.activeSelf) anchor.gameObject.SetActive(false);
            }

            private void DestroyPrefab()
            {
                if (instance && instance.activeSelf) instance.SetActive(false);
                if (lifetimes != null)
                {
                    foreach (var lifetime in lifetimes)
                    {
                        try { lifetime.Dispose(); }
                        catch (Exception exception) { Debug.LogException(exception); }
                    }
                    lifetimes.Clear();
                }
                if (instance) Destroy(instance);
                instance = null;
            }
        }
    }
}
