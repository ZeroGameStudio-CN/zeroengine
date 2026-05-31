using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor
{
    internal static class AbilityAuthoringStyles
    {
        public const float AssetListWidth = 360f;
        public const float AssetCardHeight = 64f;
        public const float IconSize = 40f;
        public const float RowGap = 6f;

        private static GUIStyle _panel;
        private static GUIStyle _assetTitle;
        private static GUIStyle _assetSubtitle;
        private static GUIStyle _sectionHeader;
        private static GUIStyle _chip;
        private static GUIStyle _pill;
        private static GUIStyle _componentHeader;
        private static GUIStyle _componentDescription;
        private static GUIStyle _componentCard;
        private static GUIStyle _emptyState;
        private static GUIStyle _toolbarDescription;
        private static GUIStyle _headerTitle;
        private static GUIStyle _headerSubtitle;

        private static Texture2D _panelTexture;
        private static Texture2D _componentTexture;

        public static GUIStyle Panel => _panel ??= new GUIStyle
        {
            padding = new RectOffset(10, 10, 8, 8),
            margin = new RectOffset(0, 0, 3, 6),
            normal = { background = PanelTexture }
        };

        public static GUIStyle AssetTitle => _assetTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static GUIStyle AssetSubtitle => _assetSubtitle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static GUIStyle SectionHeader => _sectionHeader ??= new GUIStyle(EditorStyles.foldoutHeader)
        {
            fixedHeight = 26f,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(8, 6, 3, 3),
            margin = new RectOffset(0, 0, 7, 3)
        };

        public static GUIStyle Chip => _chip ??= new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 22f,
            padding = new RectOffset(10, 10, 2, 2),
            margin = new RectOffset(0, 6, 2, 2)
        };

        public static GUIStyle Pill => _pill ??= new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 20f,
            padding = new RectOffset(8, 8, 1, 1),
            margin = new RectOffset(4, 0, 0, 0)
        };

        public static GUIStyle ComponentHeader => _componentHeader ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static GUIStyle ComponentDescription => _componentDescription ??= new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static GUIStyle ComponentCard => _componentCard ??= new GUIStyle
        {
            padding = new RectOffset(9, 9, 7, 7),
            margin = new RectOffset(0, 0, 4, 6),
            normal = { background = ComponentTexture }
        };

        public static GUIStyle EmptyState => _emptyState ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 26f,
            margin = new RectOffset(0, 0, 4, 4)
        };

        public static GUIStyle ToolbarDescription => _toolbarDescription ??= new GUIStyle(EditorStyles.miniLabel)
        {
            clipping = TextClipping.Clip,
            margin = new RectOffset(8, 0, 1, 0)
        };

        public static GUIStyle HeaderTitle => _headerTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static GUIStyle HeaderSubtitle => _headerSubtitle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            clipping = TextClipping.Clip,
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static Color BackgroundColor => EditorGUIUtility.isProSkin
            ? new Color(0.18f, 0.18f, 0.18f)
            : new Color(0.82f, 0.82f, 0.82f);

        public static Color PanelColor => EditorGUIUtility.isProSkin
            ? new Color(0.23f, 0.23f, 0.23f)
            : new Color(0.93f, 0.93f, 0.93f);

        public static Color ComponentColor => EditorGUIUtility.isProSkin
            ? new Color(0.27f, 0.27f, 0.27f)
            : new Color(0.97f, 0.97f, 0.97f);

        public static Color SelectedColor => EditorGUIUtility.isProSkin
            ? new Color(0.25f, 0.32f, 0.42f)
            : new Color(0.73f, 0.82f, 0.94f);

        private static Texture2D PanelTexture => _panelTexture ??= CreateTexture(PanelColor);

        private static Texture2D ComponentTexture => _componentTexture ??= CreateTexture(ComponentColor);

        public static void DrawPanel(Action content, params GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope(Panel, options))
            {
                content?.Invoke();
            }
        }

        public static void DrawEmptyState(string message)
        {
            EditorGUILayout.LabelField(message, EmptyState);
        }

        public static Color StatusColor(AbilityAuthoringValidationStatus status)
        {
            return status switch
            {
                AbilityAuthoringValidationStatus.Error => new Color(0.88f, 0.22f, 0.18f),
                AbilityAuthoringValidationStatus.Warning => new Color(0.95f, 0.68f, 0.18f),
                _ => new Color(0.25f, 0.72f, 0.38f)
            };
        }

        private static Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
