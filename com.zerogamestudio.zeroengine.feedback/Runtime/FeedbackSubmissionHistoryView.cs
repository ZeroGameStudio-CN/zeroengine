using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using ZGS.Analytics;

namespace ZeroEngine.Feedback
{
    /// <summary>Small read-only receipt view; retries always address the original submission.</summary>
    public sealed class FeedbackSubmissionHistoryView : MonoBehaviour
    {
        private FeedbackUiConfiguration configuration;
        private FeedbackUiTheme theme;
        private bool ownsTheme;
        private RectTransform content;
        private FeedbackSubmissionResult? localFailure;
        private readonly List<UnityEngine.UI.Button> retryButtons = new();
        private Action close;
        private UnityEngine.EventSystems.EventSystem eventSystem;
        private GameObject previousSelection;
        private string Language => configuration.LanguageCodeResolver?.Invoke() ?? CultureInfo.CurrentUICulture.Name;
        private string Text(FeedbackDetailTextId id) => FeedbackTextCatalog.ResolveDetail(id, Language);

        internal static FeedbackSubmissionHistoryView Create(RectTransform parent,
            FeedbackUiConfiguration configuration, FeedbackSubmissionResult? localFailure, Action close)
        {
            RectTransform root = Rect("FeedbackSubmissionDetails", parent);
            Stretch(root);
            var overlay = root.gameObject.AddComponent<UnityEngine.UI.Image>();
            overlay.color = new Color(0, 0, 0, 0.72f);
            var view = root.gameObject.AddComponent<FeedbackSubmissionHistoryView>();
            view.configuration = configuration;
            view.theme = configuration.Theme != null ? configuration.Theme : FeedbackUiTheme.CreateRuntimeDefault();
            view.ownsTheme = configuration.Theme == null;
            view.localFailure = localFailure;
            view.close = close;
            view.eventSystem = UnityEngine.EventSystems.EventSystem.current;
            view.previousSelection = view.eventSystem != null ? view.eventSystem.currentSelectedGameObject : null;
            view.Build();
            view.Refresh();
            FeedbackSubmissionService.StatusChanged += view.OnStatusChanged;
            return view;
        }

        private void Build()
        {
            RectTransform safeArea = Rect("SafeArea", (RectTransform)transform);
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = Screen.width > 0 && Screen.height > 0
                ? new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height) : Vector2.zero;
            safeArea.anchorMax = Screen.width > 0 && Screen.height > 0
                ? new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height) : Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            RectTransform panel = Rect("Panel", safeArea);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(720, 680);
            panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = theme.PanelColor;

            RectTransform heading = Rect("Heading", panel);
            heading.anchorMin = new Vector2(0, 1);
            heading.anchorMax = Vector2.one;
            heading.offsetMin = new Vector2(24, -78);
            heading.offsetMax = new Vector2(-24, -18);
            Label(heading, Text(FeedbackDetailTextId.Recent), 30, false);

            RectTransform scrollRoot = Rect("ScrollView", panel);
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(24, 86);
            scrollRoot.offsetMax = new Vector2(-24, -86);
            var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            RectTransform viewport = Rect("Viewport", scrollRoot);
            Stretch(viewport);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            Vertical(content, 18);
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            RectTransform footer = Rect("Footer", panel);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1, 0);
            footer.offsetMin = new Vector2(24, 18);
            footer.offsetMax = new Vector2(-24, 70);
            UnityEngine.UI.Button button = Button(footer, Text(FeedbackDetailTextId.Close), 52);
            Stretch((RectTransform)button.transform);
            button.onClick.AddListener(() => close());
            if (eventSystem != null) eventSystem.SetSelectedGameObject(button.gameObject);
        }

        private void OnStatusChanged(FeedbackSubmissionStatus _) => Refresh();

        private void Update()
        {
            foreach (UnityEngine.UI.Button button in retryButtons)
                if (button != null) button.interactable = !FeedbackUploadQueue.IsProcessing;
        }

        public void Refresh()
        {
            if (content == null) return;
            retryButtons.Clear();
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            IReadOnlyList<FeedbackSubmissionStatus> records = FeedbackSubmissionService.GetRecentSubmissions();
            bool hasLocalFailure = localFailure.HasValue && !localFailure.Value.AcceptedLocally &&
                localFailure.Value.Failure != FeedbackSubmissionFailure.None;
            if (hasLocalFailure)
            {
                FeedbackSubmissionResult failed = localFailure.Value;
                AddRow(FailureTitle(failed.FailureCode), failed.SubmissionId,
                    FailureHint(failed.FailureCode), false);
            }
            if (records.Count == 0 && !hasLocalFailure)
                Label(content, Text(FeedbackUploadQueue.StorageReadable
                    ? FeedbackDetailTextId.Empty : FeedbackDetailTextId.RecordsUnavailable), 24);
            foreach (FeedbackSubmissionStatus record in records)
            {
                string status = record.State == FeedbackDeliveryState.Received
                    ? FeedbackTextCatalog.ResolveSubmissionStatus(FeedbackTextId.Uploaded, Language)
                    : record.State == FeedbackDeliveryState.Failed ? FailureTitle(record.FailureCode)
                    : Text(FeedbackDetailTextId.Pending);
                string date = record.CreatedAt > 0 && record.CreatedAt < 253402300799
                    ? DateTimeOffset.FromUnixTimeSeconds(record.CreatedAt).LocalDateTime.ToString("g") + "  " : string.Empty;
                string explanation = record.State == FeedbackDeliveryState.Received
                    ? (record.PartialAttachments ? Text(FeedbackDetailTextId.Partial) : string.Empty)
                    : record.State == FeedbackDeliveryState.Stopped ? Text(FeedbackDetailTextId.Stopped)
                    : string.IsNullOrEmpty(record.FailureCode) ? Text(FeedbackDetailTextId.Saved)
                    : FailureHint(record.FailureCode);
                if (record.State == FeedbackDeliveryState.NeedsAction && !string.IsNullOrEmpty(record.FailureCode))
                    explanation += " (" + record.FailureCode + ")";
                AddRow(date + status, record.SubmissionId, explanation,
                    record.State != FeedbackDeliveryState.Received && record.State != FeedbackDeliveryState.Stopped);
            }
        }

        private string FailureTitle(string code)
        {
            string label = FeedbackTextCatalog.ResolveSubmissionStatus(FeedbackTextId.UploadFailed, Language);
            return label + (Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "（" + code + "）" : " (" + code + ")");
        }

        private string FailureHint(string code)
        {
            FeedbackDetailTextId id;
            switch (code)
            {
                case FeedbackFailureCodes.Certificate: id = FeedbackDetailTextId.Connection; break;
                case FeedbackFailureCodes.QueuePersistence: id = FeedbackDetailTextId.Storage; break;
                case FeedbackFailureCodes.FileRead: id = FeedbackDetailTextId.File; break;
                case FeedbackFailureCodes.QueueFull: id = FeedbackDetailTextId.QueueFull; break;
                case "F413": id = FeedbackDetailTextId.TooLarge; break;
                case FeedbackFailureCodes.Connection:
                case FeedbackFailureCodes.Timeout:
                case FeedbackFailureCodes.ResponseProcessing: id = FeedbackDetailTextId.Saved; break;
                default: id = FeedbackDetailTextId.Server; break;
            }
            return Text(id);
        }

        private void AddRow(string title, string id, string explanation, bool retry)
        {
            RectTransform row = Rect("Submission", content);
            Vertical(row, 6);
            Label(row, title, 24);
            if (!string.IsNullOrEmpty(id)) Label(row, id, 19);
            if (!string.IsNullOrEmpty(explanation)) Label(row, explanation, 21);
            if (string.IsNullOrEmpty(id)) return;
            RectTransform actions = Rect("Actions", row);
            var layout = actions.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            Button(actions, Text(FeedbackDetailTextId.CopyNumber), 44).onClick.AddListener(() => GUIUtility.systemCopyBuffer = id);
            if (retry)
            {
                UnityEngine.UI.Button button = Button(actions, Text(FeedbackDetailTextId.Retry), 44);
                button.onClick.AddListener(() => { FeedbackSubmissionService.RetrySubmission(id); Refresh(); });
                retryButtons.Add(button);
            }
        }

        private TMP_Text Label(RectTransform parent, string text, float size, bool layout = true)
        {
            RectTransform rect = Rect("Text", parent);
            if (!layout) Stretch(rect);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = theme.Font != null ? theme.Font : TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.color = theme.TextColor;
            label.richText = false;
            label.raycastTarget = false;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            if (layout) rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = size + 8;
            return label;
        }

        private UnityEngine.UI.Button Button(RectTransform parent, string text, float height)
        {
            RectTransform rect = Rect("Button", parent);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = theme.SecondaryColor;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var element = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = height;
            element.preferredWidth = 180;
            Label(rect, text, 22, false).alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static RectTransform Rect(string name, RectTransform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Vertical(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
        }

        internal void ReleaseSelection()
        {
            if (eventSystem != null && eventSystem.currentSelectedGameObject != null &&
                eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
                eventSystem.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
                    ? previousSelection : null);
        }

        private void OnDestroy()
        {
            FeedbackSubmissionService.StatusChanged -= OnStatusChanged;
            ReleaseSelection();
            if (ownsTheme && theme != null) Destroy(theme);
        }
    }
}
