using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI
{
    public static class EditorUiStyles
    {
        private static bool _initialized;
        private static bool _proSkin;

        public static GUIStyle HeaderTitle { get; private set; }
        public static GUIStyle HeaderSubtitle { get; private set; }
        public static GUIStyle SectionTitle { get; private set; }
        public static GUIStyle Card { get; private set; }
        public static GUIStyle Metric { get; private set; }
        public static GUIStyle PrimaryButton { get; private set; }
        public static GUIStyle DestructiveButton { get; private set; }

        public static void EnsureCurrent()
        {
            bool isProSkin = EditorGUIUtility.isProSkin;
            if (!RequiresRebuild(_initialized, _proSkin, isProSkin))
                return;

            _initialized = true;
            _proSkin = isProSkin;
            EditorUiPalette palette = EditorUiPalette.Current;

            HeaderTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = EditorUiTokens.HeaderTitleSize,
                wordWrap = true,
                margin = new RectOffset(0, 0, 0, 2)
            };
            HeaderTitle.normal.textColor = palette.Text;

            HeaderSubtitle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                margin = new RectOffset(0, 0, 0, 0)
            };
            HeaderSubtitle.normal.textColor = palette.MutedText;

            SectionTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = EditorUiTokens.SectionTitleSize,
                wordWrap = true
            };
            SectionTitle.normal.textColor = palette.Text;

            Card = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 4, 6)
            };

            Metric = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 4, 4),
                alignment = TextAnchor.MiddleLeft
            };

            PrimaryButton = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = EditorUiTokens.PrimaryButtonHeight
            };

            DestructiveButton = new GUIStyle(PrimaryButton);
            DestructiveButton.normal.textColor = palette.Error;
        }

        internal static bool RequiresRebuild(bool initialized, bool cachedProSkin, bool currentProSkin)
        {
            return !initialized || cachedProSkin != currentProSkin;
        }
    }
}
