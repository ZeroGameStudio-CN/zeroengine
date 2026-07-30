using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.PlayerSettings.UI
{
    public sealed class SettingsUiShell
    {
        internal SettingsUiShell(
            RectTransform root,
            RectTransform tabBar,
            RectTransform content,
            RectTransform footer)
        {
            Root = root;
            TabBar = tabBar;
            Content = content;
            Footer = footer;
        }

        public RectTransform Root { get; }
        public RectTransform TabBar { get; }
        public RectTransform Content { get; }
        public RectTransform Footer { get; }
    }

    public sealed class SettingsUiCategoryView
    {
        private readonly List<Selectable> _selectables = new();

        internal SettingsUiCategoryView(GameObject root, RectTransform content, ScrollRect scrollRect)
        {
            Root = root;
            Content = content;
            ScrollRect = scrollRect;
        }

        public GameObject Root { get; }
        public RectTransform Content { get; }
        public ScrollRect ScrollRect { get; }
        public IReadOnlyList<Selectable> Selectables => _selectables;

        internal void Add(Selectable selectable) => _selectables.Add(selectable);
    }

    public sealed class SettingsUiLayoutBuilder
    {
        private const float RowHeight = 56f;
        private readonly RectTransform _host;
        private readonly SettingsUiStyle _style;

        public SettingsUiLayoutBuilder(
            RectTransform host,
            Font fallbackFont = null,
            SettingsUiTheme theme = null)
            : this(
                host,
                theme != null
                    ? theme.Resolve(fallbackFont)
                    : SettingsUiStyle.CreateFallback(fallbackFont))
        {
        }

        public SettingsUiLayoutBuilder(RectTransform host, SettingsUiStyle style)
        {
            _host = host != null ? host : throw new ArgumentNullException(nameof(host));
            _style = style ?? throw new ArgumentNullException(nameof(style));
            _style.Font = SettingsUiStyle.ResolveFont(_style.Font);
        }

        public SettingsUiShell BuildShell(string title, string subtitle)
        {
            RectTransform root = CreateStretchRect("Settings UI", _host, Vector2.zero, Vector2.zero);
            Image panel = root.gameObject.AddComponent<Image>();
            ApplyImage(panel, _style.PanelSprite, _style.PanelColor, false);

            RectTransform header = CreateAnchoredRect(
                "Header",
                root,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -4f),
                new Vector2(-24f, 76f));
            CreateText(
                "Title",
                header,
                title,
                28,
                _style.TextColor,
                TextAnchor.MiddleCenter,
                new Vector2(0f, 0.42f),
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                FontStyle.Bold);
            CreateText(
                "Subtitle",
                header,
                subtitle,
                14,
                _style.MutedTextColor,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                new Vector2(1f, 0.42f),
                Vector2.zero,
                Vector2.zero);

            RectTransform tabBar = CreateAnchoredRect(
                "Tabs",
                root,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -86f),
                new Vector2(-24f, 44f));
            HorizontalLayoutGroup tabLayout = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureHorizontalLayout(tabLayout, 8f);

            RectTransform content = CreateStretchRect(
                "Category Content",
                root,
                new Vector2(12f, 64f),
                new Vector2(-12f, -138f));

            RectTransform footer = CreateAnchoredRect(
                "Footer",
                root,
                Vector2.zero,
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 4f),
                new Vector2(-24f, 52f));
            HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureHorizontalLayout(footerLayout, 12f);

            return new SettingsUiShell(root, tabBar, content, footer);
        }

        public Button CreateTab(SettingsUiShell shell, string name, string label)
        {
            if (shell == null)
            {
                throw new ArgumentNullException(nameof(shell));
            }

            Button button = CreateButton(name, shell.TabBar, label, false);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 72f;
            layout.flexibleWidth = 1f;
            return button;
        }

        public SettingsUiCategoryView CreateCategory(SettingsUiShell shell, string name)
        {
            if (shell == null)
            {
                throw new ArgumentNullException(nameof(shell));
            }

            RectTransform root = CreateStretchRect(name, shell.Content, Vector2.zero, Vector2.zero);
            Image viewportImage = root.gameObject.AddComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
            root.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateAnchoredRect(
                "Rows",
                root,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                Vector2.zero);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = root.gameObject.AddComponent<ScrollRect>();
            scrollRect.content = content;
            scrollRect.viewport = root;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 32f;
            root.gameObject.AddComponent<SettingsUiSelectionScroller>().Initialize(scrollRect);
            return new SettingsUiCategoryView(root.gameObject, content, scrollRect);
        }

        public Slider CreateSliderRow(
            SettingsUiCategoryView category,
            string name,
            string label,
            out Text valueText)
        {
            RectTransform row = CreateRow(category, name);
            CreateColumnText(
                $"{name} Label",
                row,
                label,
                _style.TextColor,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0f),
                new Vector2(0.30f, 1f),
                new Vector2(12f, 0f),
                new Vector2(-8f, 0f));

            RectTransform sliderRoot = CreateStretchRect(
                name,
                row,
                new Vector2(0.32f, 0.18f),
                new Vector2(0.82f, 0.82f),
                new Vector2(4f, 0f),
                new Vector2(-4f, 0f));
            Slider slider = CreateSlider(sliderRoot);
            valueText = CreateColumnText(
                $"{name} Value",
                row,
                string.Empty,
                _style.AccentColor,
                TextAnchor.MiddleRight,
                new Vector2(0.84f, 0f),
                Vector2.one,
                new Vector2(4f, 0f),
                new Vector2(-12f, 0f),
                FontStyle.Bold);
            category.Add(slider);
            return slider;
        }

        public Toggle CreateToggleRow(SettingsUiCategoryView category, string name, string label)
        {
            RectTransform row = CreateRow(category, name);
            CreateColumnText(
                $"{name} Label",
                row,
                label,
                _style.TextColor,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                new Vector2(0.84f, 1f),
                new Vector2(12f, 0f),
                new Vector2(-8f, 0f));

            RectTransform toggleRoot = CreateAnchoredRect(
                name,
                row,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-12f, 0f),
                new Vector2(38f, 38f));
            Toggle toggle = CreateToggle(toggleRoot);
            category.Add(toggle);
            return toggle;
        }

        public Button CreateChoiceRow(
            SettingsUiCategoryView category,
            string name,
            string label,
            out Text valueText)
        {
            RectTransform row = CreateRow(category, name);
            CreateColumnText(
                $"{name} Label",
                row,
                label,
                _style.TextColor,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                new Vector2(0.36f, 1f),
                new Vector2(12f, 0f),
                new Vector2(-8f, 0f));
            RectTransform buttonHost = CreateStretchRect(
                $"{name} Host",
                row,
                new Vector2(0.38f, 0.12f),
                new Vector2(1f, 0.88f),
                new Vector2(4f, 0f),
                new Vector2(-12f, 0f));
            Button button = CreateButton(name, buttonHost, string.Empty, false);
            StretchToParent((RectTransform)button.transform);
            valueText = button.GetComponentInChildren<Text>();
            category.Add(button);
            return button;
        }

        public Button CreateActionRow(
            SettingsUiCategoryView category,
            string name,
            string label,
            string actionLabel,
            bool primary = false)
        {
            Button button = CreateChoiceRow(category, name, label, out Text valueText);
            valueText.text = actionLabel;
            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                image.color = primary ? _style.PrimaryButtonColor : _style.ButtonColor;
            }
            return button;
        }

        public Button CreateFooterButton(
            SettingsUiShell shell,
            string name,
            string label,
            bool primary)
        {
            if (shell == null)
            {
                throw new ArgumentNullException(nameof(shell));
            }

            Button button = CreateButton(name, shell.Footer, label, primary);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 120f;
            layout.flexibleWidth = 1f;
            return button;
        }

        public static void Rebuild(SettingsUiShell shell, params SettingsUiCategoryView[] categories)
        {
            if (shell == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (categories != null)
            {
                foreach (SettingsUiCategoryView category in categories)
                {
                    if (category != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(category.Content);
                    }
                }
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(shell.Root);
            Canvas.ForceUpdateCanvases();
        }

        private RectTransform CreateRow(SettingsUiCategoryView category, string name)
        {
            if (category == null)
            {
                throw new ArgumentNullException(nameof(category));
            }

            RectTransform row = CreateAnchoredRect(
                $"{name} Row",
                category.Content,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, RowHeight));
            Image image = row.gameObject.AddComponent<Image>();
            ApplyImage(image, _style.RowSprite, _style.RowColor, false);
            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 48f;
            layout.preferredHeight = RowHeight;
            layout.flexibleWidth = 1f;
            return row;
        }

        private Slider CreateSlider(RectTransform root)
        {
            RectTransform track = CreateStretchRect(
                "Track",
                root,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            track.sizeDelta = new Vector2(0f, 8f);
            Image trackImage = track.gameObject.AddComponent<Image>();
            ApplyImage(trackImage, _style.SliderTrackSprite, _style.SliderTrackColor, false);

            RectTransform fillArea = CreateStretchRect(
                "Fill Area",
                root,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f));
            RectTransform fill = CreateStretchRect("Fill", fillArea, Vector2.zero, Vector2.zero);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            ApplyImage(fillImage, _style.SliderFillSprite, _style.AccentColor, false);

            RectTransform handleArea = CreateStretchRect(
                "Handle Slide Area",
                root,
                Vector2.zero,
                Vector2.one,
                new Vector2(9f, 0f),
                new Vector2(-9f, 0f));
            RectTransform handle = CreateAnchoredRect(
                "Handle",
                handleArea,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(22f, 22f));
            Image handleImage = handle.gameObject.AddComponent<Image>();
            ApplyImage(handleImage, _style.SliderHandleSprite, _style.TextColor, true);

            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = CreateSelectableColors();
            return slider;
        }

        private Toggle CreateToggle(RectTransform root)
        {
            Image background = root.gameObject.AddComponent<Image>();
            ApplyImage(background, _style.ToggleBackgroundSprite, _style.ButtonColor, true);
            RectTransform checkmark = CreateStretchRect(
                "Checkmark",
                root,
                new Vector2(7f, 7f),
                new Vector2(-7f, -7f));
            Image checkmarkImage = checkmark.gameObject.AddComponent<Image>();
            ApplyImage(checkmarkImage, _style.ToggleCheckmarkSprite, _style.AccentColor, false);

            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmarkImage;
            toggle.colors = CreateSelectableColors();
            return toggle;
        }

        private Button CreateButton(string name, Transform parent, string label, bool primary)
        {
            RectTransform root = CreateAnchoredRect(
                name,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(160f, 44f));
            Image image = root.gameObject.AddComponent<Image>();
            ApplyImage(
                image,
                _style.ButtonSprite,
                primary ? _style.PrimaryButtonColor : _style.ButtonColor,
                true);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CreateSelectableColors();
            CreateText(
                "Label",
                root,
                label,
                16,
                _style.TextColor,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 4f),
                new Vector2(-10f, -4f),
                FontStyle.Bold);
            return button;
        }

        private Text CreateColumnText(
            string name,
            Transform parent,
            string value,
            Color color,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            FontStyle fontStyle = FontStyle.Normal)
        {
            return CreateText(
                name,
                parent,
                value,
                16,
                color,
                alignment,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax,
                fontStyle);
        }

        private Text CreateText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            Color color,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            FontStyle fontStyle = FontStyle.Normal)
        {
            RectTransform rect = CreateStretchRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = value ?? string.Empty;
            text.font = _style.Font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(10, fontSize - 6);
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void ConfigureHorizontalLayout(HorizontalLayoutGroup layout, float spacing)
        {
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        private static ColorBlock CreateSelectableColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.06f, 0.90f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.78f, 0.70f, 0.58f, 1f);
            colors.disabledColor = new Color(0.38f, 0.35f, 0.32f, 0.62f);
            colors.colorMultiplier = 1f;
            return colors;
        }

        private static void ApplyImage(Image image, Sprite sprite, Color color, bool raycastTarget)
        {
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycastTarget;
        }

        private static RectTransform CreateAnchoredRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static RectTransform CreateStretchRect(
            string name,
            Transform parent,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            return CreateStretchRect(name, parent, Vector2.zero, Vector2.one, offsetMin, offsetMax);
        }

        private static RectTransform CreateStretchRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            RectTransform rect = CreateAnchoredRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }
}
