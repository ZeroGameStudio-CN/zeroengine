using UnityEngine.UIElements;

namespace ZeroEngine.EditorUI
{
    public static class EditorUiElements
    {
        public static void ApplyWindowRoot(VisualElement root)
        {
            EditorUiPalette palette = EditorUiPalette.Current;
            root.style.paddingLeft = EditorUiTokens.SpaceMd;
            root.style.paddingRight = EditorUiTokens.SpaceMd;
            root.style.paddingTop = EditorUiTokens.SpaceMd;
            root.style.paddingBottom = EditorUiTokens.SpaceMd;
            root.style.backgroundColor = palette.Surface;
            root.style.color = palette.Text;
        }

        public static VisualElement CreateHeader(string title, string subtitle = null)
        {
            EditorUiPalette palette = EditorUiPalette.Current;
            var header = new VisualElement { name = "zeroengine-editor-ui-header" };
            ApplyPanel(header);
            header.style.borderTopWidth = 3f;
            header.style.borderTopColor = palette.Accent;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = EditorUiTokens.HeaderTitleSize;
            titleLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            titleLabel.style.color = palette.Text;
            header.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
                subtitleLabel.style.color = palette.MutedText;
                subtitleLabel.style.marginTop = EditorUiTokens.SpaceXs;
                header.Add(subtitleLabel);
            }

            return header;
        }

        public static void ApplyPanel(VisualElement element)
        {
            EditorUiPalette palette = EditorUiPalette.Current;
            element.style.paddingLeft = EditorUiTokens.SpaceMd;
            element.style.paddingRight = EditorUiTokens.SpaceMd;
            element.style.paddingTop = EditorUiTokens.SpaceSm;
            element.style.paddingBottom = EditorUiTokens.SpaceSm;
            element.style.marginBottom = EditorUiTokens.SpaceSm;
            element.style.backgroundColor = palette.RaisedSurface;
            element.style.borderLeftWidth = 1f;
            element.style.borderRightWidth = 1f;
            element.style.borderTopWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftColor = palette.Border;
            element.style.borderRightColor = palette.Border;
            element.style.borderTopColor = palette.Border;
            element.style.borderBottomColor = palette.Border;
        }

        public static void ApplyToolbar(VisualElement element)
        {
            ApplyPanel(element);
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.minHeight = EditorUiTokens.ToolbarHeight;
        }

        public static void ApplySelected(VisualElement element, bool selected)
        {
            EditorUiPalette palette = EditorUiPalette.Current;
            element.style.backgroundColor = selected ? palette.Selection : palette.RaisedSurface;
        }

        public static void ApplyStatus(Label label, EditorUiStatus status)
        {
            label.style.color = EditorUiGUILayout.StatusColor(status);
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        }
    }
}
