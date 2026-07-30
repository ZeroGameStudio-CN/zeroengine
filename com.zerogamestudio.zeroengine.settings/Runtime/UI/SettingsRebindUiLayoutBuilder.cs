using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.PlayerSettings.UI
{
    public sealed class SettingsRebindUiShell
    {
        internal SettingsRebindUiShell(
            RectTransform root,
            RectTransform deviceTabs,
            RectTransform viewport,
            RectTransform rows,
            RectTransform footer,
            ScrollRect scrollRect,
            Text statusText)
        {
            Root = root;
            DeviceTabs = deviceTabs;
            Viewport = viewport;
            Rows = rows;
            Footer = footer;
            ScrollRect = scrollRect;
            StatusText = statusText;
        }

        public RectTransform Root { get; }
        public RectTransform DeviceTabs { get; }
        public RectTransform Viewport { get; }
        public RectTransform Rows { get; }
        public RectTransform Footer { get; }
        public ScrollRect ScrollRect { get; }
        public Text StatusText { get; }
    }

    public sealed class SettingsRebindUiRow
    {
        internal SettingsRebindUiRow(
            RectTransform root,
            Button bindingButton,
            Text bindingText,
            Button resetButton)
        {
            Root = root;
            BindingButton = bindingButton;
            BindingText = bindingText;
            ResetButton = resetButton;
        }

        public RectTransform Root { get; }
        public Button BindingButton { get; }
        public Text BindingText { get; }
        public Button ResetButton { get; }
    }

    public sealed class SettingsRebindUiLayoutBuilder
    {
        private const float RowHeight = 56f;
        private readonly RectTransform _host;
        private readonly SettingsUiStyle _style;

        public SettingsRebindUiLayoutBuilder(
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

        public SettingsRebindUiLayoutBuilder(RectTransform host, SettingsUiStyle style)
        {
            _host = host != null ? host : throw new ArgumentNullException(nameof(host));
            _style = style ?? throw new ArgumentNullException(nameof(style));
            _style.Font = SettingsUiStyle.ResolveFont(_style.Font);
        }

        public SettingsRebindUiShell BuildShell(
            string title,
            string status)
        {
            RectTransform root = CreateStretchRect(
                "Rebind UI",
                _host,
                Vector2.zero,
                Vector2.zero);
            Image panel = root.gameObject.AddComponent<Image>();
            ApplyImage(panel, _style.PanelSprite, _style.PanelColor, false);

            CreateText(
                "Rebind Title",
                root,
                title,
                28,
                _style.TextColor,
                TextAnchor.MiddleCenter,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(12f, -62f),
                new Vector2(-12f, -12f),
                FontStyle.Bold);
            Text statusText = CreateText(
                "Rebind Status",
                root,
                status,
                14,
                _style.MutedTextColor,
                TextAnchor.MiddleCenter,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(12f, -94f),
                new Vector2(-12f, -64f));

            RectTransform deviceTabs = CreateAnchoredRect(
                "Rebind Device Tabs",
                root,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -102f),
                new Vector2(-24f, 40f));
            HorizontalLayoutGroup tabsLayout =
                deviceTabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureHorizontalLayout(tabsLayout, 8f);

            RectTransform viewport = CreateStretchRect(
                "Binding Rows Viewport",
                root,
                new Vector2(12f, 72f),
                new Vector2(-12f, -150f));
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform rows = CreateAnchoredRect(
                "Binding Rows",
                viewport,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                Vector2.zero,
                Vector2.zero);
            VerticalLayoutGroup rowsLayout =
                rows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.padding = new RectOffset(8, 8, 8, 8);
            rowsLayout.spacing = 8f;
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            ContentSizeFitter rowsFitter = rows.gameObject.AddComponent<ContentSizeFitter>();
            rowsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
            scrollRect.content = rows;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 32f;
            viewport.gameObject.AddComponent<SettingsUiSelectionScroller>()
                .Initialize(scrollRect);

            RectTransform footer = CreateAnchoredRect(
                "Rebind Footer",
                root,
                Vector2.zero,
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 10f),
                new Vector2(-24f, 50f));
            HorizontalLayoutGroup footerLayout =
                footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureHorizontalLayout(footerLayout, 12f);

            return new SettingsRebindUiShell(
                root,
                deviceTabs,
                viewport,
                rows,
                footer,
                scrollRect,
                statusText);
        }

        public Button CreateDeviceTab(
            SettingsRebindUiShell shell,
            string name,
            string label)
        {
            if (shell == null)
            {
                throw new ArgumentNullException(nameof(shell));
            }

            Button button = CreateButton(name, shell.DeviceTabs, label, false);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 96f;
            layout.flexibleWidth = 1f;
            return button;
        }

        public SettingsRebindUiRow CreateBindingRow(
            SettingsRebindUiShell shell,
            string name,
            string actionLabel,
            string resetLabel)
        {
            if (shell == null)
            {
                throw new ArgumentNullException(nameof(shell));
            }

            RectTransform row = CreateAnchoredRect(
                $"{name} Rebind Row",
                shell.Rows,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, RowHeight));
            Image rowImage = row.gameObject.AddComponent<Image>();
            ApplyImage(rowImage, _style.RowSprite, _style.RowColor, false);
            LayoutElement rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.minHeight = 48f;
            rowElement.preferredHeight = RowHeight;
            rowElement.flexibleWidth = 1f;

            HorizontalLayoutGroup rowLayout =
                row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 6, 6);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            Text action = CreateText(
                $"{name} Action Label",
                row,
                actionLabel,
                16,
                _style.TextColor,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f));
            LayoutElement actionLayout = action.gameObject.AddComponent<LayoutElement>();
            actionLayout.minWidth = 120f;
            actionLayout.flexibleWidth = 1.2f;

            Button bindingButton = CreateButton(
                $"{name} Binding",
                row,
                string.Empty,
                false);
            LayoutElement bindingLayout =
                bindingButton.gameObject.AddComponent<LayoutElement>();
            bindingLayout.minWidth = 140f;
            bindingLayout.flexibleWidth = 1f;

            Button resetButton = CreateButton(
                $"{name} Reset Binding",
                row,
                resetLabel,
                false);
            LayoutElement resetLayout =
                resetButton.gameObject.AddComponent<LayoutElement>();
            resetLayout.minWidth = 72f;
            resetLayout.preferredWidth = 80f;
            resetLayout.flexibleWidth = 0f;

            return new SettingsRebindUiRow(
                row,
                bindingButton,
                bindingButton.GetComponentInChildren<Text>(),
                resetButton);
        }

        public Button CreateFooterButton(
            SettingsRebindUiShell shell,
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

        public static void Rebuild(
            SettingsRebindUiShell shell,
            IReadOnlyList<SettingsRebindUiRow> rows = null)
        {
            if (shell == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (rows != null)
            {
                foreach (SettingsRebindUiRow row in rows)
                {
                    if (row != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(row.Root);
                    }
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(shell.Rows);
            LayoutRebuilder.ForceRebuildLayoutImmediate(shell.DeviceTabs);
            LayoutRebuilder.ForceRebuildLayoutImmediate(shell.Footer);
            LayoutRebuilder.ForceRebuildLayoutImmediate(shell.Root);
            Canvas.ForceUpdateCanvases();
        }

        private Button CreateButton(
            string name,
            Transform parent,
            string label,
            bool primary)
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
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f),
                FontStyle.Bold);
            return button;
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
            RectTransform rect = CreateStretchRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
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

        private static void ConfigureHorizontalLayout(
            HorizontalLayoutGroup layout,
            float spacing)
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

        private static void ApplyImage(
            Image image,
            Sprite sprite,
            Color color,
            bool raycastTarget)
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
            return CreateStretchRect(
                name,
                parent,
                Vector2.zero,
                Vector2.one,
                offsetMin,
                offsetMax);
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
    }
}
