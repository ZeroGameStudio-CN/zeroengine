using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZGS.Analytics;

namespace ZeroEngine.Feedback
{
    public sealed class DefaultFeedbackPanelView : MonoBehaviour
    {
        private FeedbackUiConfiguration _configuration;
        private FeedbackUiTheme _theme;
        private FeedbackSubmissionController _controller;
        [SerializeField] private TMP_InputField _descriptionInput;
        [SerializeField] private TMP_InputField _contactInput;
        [SerializeField] private RectTransform _attachmentArea;
        [SerializeField] private Button _attachmentButton;
        [SerializeField] private TMP_Text _attachmentSummary;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private RectTransform _safeAreaRoot;
        private readonly List<string> _attachments = new();
        [SerializeField] private bool _built;
        private Rect _lastSafeArea;

        public bool IsSubmitting => _controller != null && _controller.IsSubmitting;

        internal static DefaultFeedbackPanelView Create(
            RectTransform parent,
            FeedbackUiConfiguration configuration)
        {
            var root = new GameObject(
                "FeedbackPanel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(DefaultFeedbackPanelView));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);

            var view = root.GetComponent<DefaultFeedbackPanelView>();
            view.Initialize(configuration);
            return view;
        }

        internal void Initialize(FeedbackUiConfiguration configuration)
        {
            _configuration = configuration ?? new FeedbackUiConfiguration();
            _theme = _configuration.Theme != null
                ? _configuration.Theme
                : FeedbackUiTheme.CreateRuntimeDefault();
            _controller = new FeedbackSubmissionController(_configuration);

            if (!_built)
                BuildLayout();

            BindEvents();
            _attachmentArea.gameObject.SetActive(_configuration.AttachmentPicker != null);
            ApplyTexts();
            UpdateAttachmentSummary();
            UpdateSendState();
        }

        private void Start()
        {
            if (!_built)
                Initialize(new FeedbackUiConfiguration());

            FocusDescription();
        }

        private void Update()
        {
            if (_lastSafeArea != Screen.safeArea)
                ApplySafeArea();
        }

        public void FocusDescription()
        {
            if (isActiveAndEnabled)
                StartCoroutine(FocusNextFrame());
        }

        private IEnumerator FocusNextFrame()
        {
            yield return null;
            _descriptionInput?.Select();
            _descriptionInput?.ActivateInputField();
        }

        private void BuildLayout()
        {
            _built = true;
            var overlay = GetComponent<Image>();
            overlay.color = _theme.OverlayColor;
            overlay.raycastTarget = true;

            _safeAreaRoot = CreateRect("SafeArea", transform);
            Stretch(_safeAreaRoot);

            RectTransform panel = CreateRect("Panel", _safeAreaRoot);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(720f, 720f);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = _theme.PanelSprite;
            panelImage.color = _theme.PanelColor;

            TMP_Text title = CreateText("Title", panel, 36f, TextAlignmentOptions.MidlineLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(_theme.Padding, -78f);
            title.rectTransform.offsetMax = new Vector2(-_theme.Padding, -18f);

            RectTransform scrollRoot = CreateRect("FormScroll", panel);
            scrollRoot.anchorMin = Vector2.zero;
            scrollRoot.anchorMax = Vector2.one;
            scrollRoot.offsetMin = new Vector2(_theme.Padding, 92f);
            scrollRoot.offsetMax = new Vector2(-_theme.Padding, -86f);
            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", scrollRoot);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            scrollRect.viewport = viewport;

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = _theme.Spacing;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = content;

            _descriptionInput = CreateInput("Description", content, true, 260f);
            _contactInput = CreateInput("Contact", content, false, 72f);

            _attachmentArea = CreateRect("Attachments", content);
            var attachmentLayout = _attachmentArea.gameObject.AddComponent<VerticalLayoutGroup>();
            attachmentLayout.spacing = 8f;
            attachmentLayout.childControlWidth = true;
            attachmentLayout.childForceExpandWidth = true;
            attachmentLayout.childControlHeight = true;
            attachmentLayout.childForceExpandHeight = false;
            _attachmentArea.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _attachmentButton = CreateButton("AddAttachment", _attachmentArea, _theme.SecondaryColor);
            _attachmentSummary = CreateText("AttachmentSummary", _attachmentArea, 22f, TextAlignmentOptions.TopLeft);
            _attachmentSummary.enableWordWrapping = true;
            _attachmentSummary.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
            _attachmentArea.gameObject.SetActive(_configuration.AttachmentPicker != null);

            RectTransform footer = CreateRect("Footer", panel);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.offsetMin = new Vector2(_theme.Padding, 18f);
            footer.offsetMax = new Vector2(-_theme.Padding, 78f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = _theme.Spacing;
            footerLayout.childControlWidth = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandHeight = true;

            _cancelButton = CreateButton("Cancel", footer, _theme.SecondaryColor);
            _sendButton = CreateButton("Send", footer, _theme.PrimaryColor);

            ApplySafeArea();
        }

        private void BindEvents()
        {
            _descriptionInput.onValueChanged.RemoveListener(HandleDescriptionChanged);
            _descriptionInput.onValueChanged.AddListener(HandleDescriptionChanged);
            _attachmentButton.onClick.RemoveListener(PickAttachments);
            _attachmentButton.onClick.AddListener(PickAttachments);
            _cancelButton.onClick.RemoveListener(FeedbackPanel.Close);
            _cancelButton.onClick.AddListener(FeedbackPanel.Close);
            _sendButton.onClick.RemoveListener(Submit);
            _sendButton.onClick.AddListener(Submit);
        }

        private void HandleDescriptionChanged(string _)
        {
            UpdateSendState();
        }

        private TMP_InputField CreateInput(
            string name,
            Transform parent,
            bool multiline,
            float preferredHeight)
        {
            RectTransform root = CreateRect(name, parent);
            var background = root.gameObject.AddComponent<Image>();
            background.sprite = _theme.PanelSprite;
            background.color = _theme.InputColor;
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;

            RectTransform viewport = CreateRect("Text Area", root);
            Stretch(viewport, new Vector2(16f, 10f), new Vector2(-16f, -10f));
            viewport.gameObject.AddComponent<RectMask2D>();

            TMP_Text placeholder = CreateText("Placeholder", viewport, 24f, TextAlignmentOptions.TopLeft);
            Stretch(placeholder.rectTransform);
            placeholder.color = new Color(_theme.TextColor.r, _theme.TextColor.g, _theme.TextColor.b, 0.52f);

            TMP_Text text = CreateText("Text", viewport, 24f, TextAlignmentOptions.TopLeft);
            Stretch(text.rectTransform);
            text.enableWordWrapping = true;

            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            return input;
        }

        private Button CreateButton(string name, Transform parent, Color color)
        {
            RectTransform root = CreateRect(name, parent);
            var image = root.gameObject.AddComponent<Image>();
            image.sprite = _theme.ButtonSprite;
            image.color = color;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 140f;
            layout.preferredHeight = 60f;
            layout.flexibleWidth = 1f;

            TMP_Text label = CreateText("Label", root, 24f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, new Vector2(12f, 6f), new Vector2(-12f, -6f));
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return button;
        }

        private TMP_Text CreateText(
            string name,
            Transform parent,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_theme.Font != null)
                text.font = _theme.Font;
            text.fontSize = fontSize;
            text.color = _theme.TextColor;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            return text;
        }

        private void ApplyTexts()
        {
            TextFor(transform.Find("SafeArea/Panel/Title"), FeedbackTextId.Title);
            PlaceholderFor(_descriptionInput, FeedbackTextId.Description);
            PlaceholderFor(_contactInput, FeedbackTextId.ContactOptional);
            ButtonText(_attachmentButton, FeedbackTextId.AttachmentOptional);
            ButtonText(_sendButton, FeedbackTextId.Send);
            ButtonText(_cancelButton, FeedbackTextId.Cancel);
        }

        private void PickAttachments()
        {
            if (IsSubmitting || _configuration.AttachmentPicker == null)
                return;

            int remaining = _configuration.AttachmentLimit - _attachments.Count;
            if (remaining <= 0)
                return;

            _configuration.AttachmentPicker.PickAttachments(remaining, selected =>
            {
                if (selected == null)
                    return;

                foreach (string path in selected)
                {
                    if (_attachments.Count >= _configuration.AttachmentLimit)
                        break;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        !_attachments.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        _attachments.Add(path);
                    }
                }

                UpdateAttachmentSummary();
            });
        }

        private void Submit()
        {
            if (IsSubmitting || string.IsNullOrWhiteSpace(_descriptionInput.text))
                return;

            StartCoroutine(SubmitRoutine());
        }

        private IEnumerator SubmitRoutine()
        {
            SetSubmitting(true);
            FeedbackSubmissionResult result = default;
            yield return _controller.Submit(
                new FeedbackFormData
                {
                    Description = _descriptionInput.text,
                    Contact = _contactInput.text,
                    Attachments = _attachments.ToArray()
                },
                value => result = value);

            if (result.AcceptedLocally)
                FeedbackPanel.CloseAfterAccepted();
            else
                SetSubmitting(false);
        }

        private void SetSubmitting(bool submitting)
        {
            _descriptionInput.interactable = !submitting;
            _contactInput.interactable = !submitting;
            _attachmentButton.interactable = !submitting &&
                                               _attachments.Count < _configuration.AttachmentLimit;
            _cancelButton.interactable = !submitting;
            _sendButton.interactable = !submitting && !string.IsNullOrWhiteSpace(_descriptionInput.text);
        }

        private void UpdateSendState()
        {
            if (_sendButton != null)
                _sendButton.interactable = !IsSubmitting && !string.IsNullOrWhiteSpace(_descriptionInput.text);
        }

        private void UpdateAttachmentSummary()
        {
            if (_attachmentSummary == null)
                return;

            _attachmentSummary.text = _attachments.Count == 0
                ? string.Empty
                : string.Join(" · ", _attachments.Select(Path.GetFileName));
            _attachmentSummary.gameObject.SetActive(_attachments.Count > 0);
            if (_attachmentButton != null)
                _attachmentButton.interactable = _attachments.Count < _configuration.AttachmentLimit;
        }

        private void ApplySafeArea()
        {
            if (_safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            _lastSafeArea = Screen.safeArea;
            _safeAreaRoot.anchorMin = new Vector2(
                _lastSafeArea.xMin / Screen.width,
                _lastSafeArea.yMin / Screen.height);
            _safeAreaRoot.anchorMax = new Vector2(
                _lastSafeArea.xMax / Screen.width,
                _lastSafeArea.yMax / Screen.height);
            _safeAreaRoot.offsetMin = Vector2.zero;
            _safeAreaRoot.offsetMax = Vector2.zero;
        }

        private void TextFor(Transform target, FeedbackTextId id)
        {
            if (target != null)
                target.GetComponent<TMP_Text>().text = FeedbackTextCatalog.Resolve(id, _configuration.TextResolver);
        }

        private void PlaceholderFor(TMP_InputField input, FeedbackTextId id)
        {
            if (input?.placeholder is TMP_Text placeholder)
                placeholder.text = FeedbackTextCatalog.Resolve(id, _configuration.TextResolver);
        }

        private void ButtonText(Button button, FeedbackTextId id)
        {
            if (button != null)
                button.GetComponentInChildren<TMP_Text>().text =
                    FeedbackTextCatalog.Resolve(id, _configuration.TextResolver);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2? offsetMin = null,
            Vector2? offsetMax = null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
        }
    }
}
