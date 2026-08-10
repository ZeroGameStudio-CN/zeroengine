using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI
{
    public readonly struct EditorUiPalette
    {
        private EditorUiPalette(
            Color surface,
            Color raisedSurface,
            Color border,
            Color text,
            Color mutedText,
            Color accent,
            Color success,
            Color warning,
            Color error,
            Color selection)
        {
            Surface = surface;
            RaisedSurface = raisedSurface;
            Border = border;
            Text = text;
            MutedText = mutedText;
            Accent = accent;
            Success = success;
            Warning = warning;
            Error = error;
            Selection = selection;
        }

        public Color Surface { get; }
        public Color RaisedSurface { get; }
        public Color Border { get; }
        public Color Text { get; }
        public Color MutedText { get; }
        public Color Accent { get; }
        public Color Success { get; }
        public Color Warning { get; }
        public Color Error { get; }
        public Color Selection { get; }

        public static EditorUiPalette Current => ResolveForSkin(EditorGUIUtility.isProSkin);

        internal static EditorUiPalette ResolveForSkin(bool isProSkin)
        {
            return isProSkin
                ? new EditorUiPalette(
                    new Color(0.18f, 0.19f, 0.21f, 1f),
                    new Color(0.23f, 0.24f, 0.27f, 1f),
                    new Color(0.55f, 0.57f, 0.62f, 1f),
                    new Color(0.90f, 0.91f, 0.93f, 1f),
                    new Color(0.72f, 0.76f, 0.82f, 1f),
                    new Color(0.36f, 0.67f, 1f, 1f),
                    new Color(0.48f, 0.86f, 0.56f, 1f),
                    new Color(1f, 0.72f, 0.30f, 1f),
                    new Color(1f, 0.48f, 0.45f, 1f),
                    new Color(0.22f, 0.43f, 0.68f, 1f))
                : new EditorUiPalette(
                    new Color(0.79f, 0.79f, 0.79f, 1f),
                    new Color(0.88f, 0.88f, 0.88f, 1f),
                    new Color(0.35f, 0.35f, 0.35f, 1f),
                    new Color(0.12f, 0.13f, 0.15f, 1f),
                    new Color(0.28f, 0.31f, 0.36f, 1f),
                    new Color(0.06f, 0.30f, 0.55f, 1f),
                    new Color(0.04f, 0.38f, 0.14f, 1f),
                    new Color(0.50f, 0.25f, 0f, 1f),
                    new Color(0.58f, 0.08f, 0.06f, 1f),
                    new Color(0.58f, 0.72f, 0.88f, 1f));
        }
    }
}
