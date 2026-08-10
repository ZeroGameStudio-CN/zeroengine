using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI
{
    public enum EditorUiStatus
    {
        Neutral,
        InProgress,
        Success,
        Warning,
        Error
    }

    public static class EditorUiGUILayout
    {
        public static void Header(string title, string subtitle, string context = null)
        {
            EditorUiStyles.EnsureCurrent();
            AccentLine(EditorUiPalette.Current.Accent, 3f);
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
            {
                GUILayout.Label(title, EditorUiStyles.HeaderTitle);
                if (!string.IsNullOrWhiteSpace(subtitle))
                    GUILayout.Label(subtitle, EditorUiStyles.HeaderSubtitle);
                if (!string.IsNullOrWhiteSpace(context))
                    GUILayout.Label(context, EditorStyles.miniLabel);
            }
        }

        public static IDisposable Section(string title, string subtitle = null, params GUILayoutOption[] options)
        {
            EditorUiStyles.EnsureCurrent();
            return new SectionScope(title, subtitle, options);
        }

        public static void SectionHeader(string title, string subtitle = null)
        {
            EditorUiStyles.EnsureCurrent();
            AccentLine(EditorUiPalette.Current.Accent, EditorUiTokens.AccentLineHeight);
            GUILayout.Label(title, EditorUiStyles.SectionTitle);
            if (!string.IsNullOrWhiteSpace(subtitle))
                GUILayout.Label(subtitle, EditorUiStyles.HeaderSubtitle);
        }

        public static void AccentLine(Color color, float height = EditorUiTokens.AccentLineHeight)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
        }

        public static void StatusLabel(string text, EditorUiStatus status, params GUILayoutOption[] options)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = StatusColor(status);
            GUILayout.Label(text, EditorStyles.miniBoldLabel, options);
            GUI.contentColor = previous;
        }

        public static void EmptyState(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        public static void ValidationSummary(string message, EditorUiStatus status)
        {
            MessageType messageType = status == EditorUiStatus.Error
                ? MessageType.Error
                : status == EditorUiStatus.Warning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(message, messageType);
        }

        public static bool PrimaryButton(string label, params GUILayoutOption[] options)
        {
            EditorUiStyles.EnsureCurrent();
            return GUILayout.Button(label, EditorUiStyles.PrimaryButton, options);
        }

        public static bool SelectionButton(string label, bool selected, params GUILayoutOption[] options)
        {
            Color previous = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = EditorUiPalette.Current.Selection;
            bool clicked = GUILayout.Button(
                label,
                selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                options);
            GUI.backgroundColor = previous;
            return clicked;
        }

        public static bool DestructiveButton(string label, params GUILayoutOption[] options)
        {
            EditorUiStyles.EnsureCurrent();
            return GUILayout.Button(label, EditorUiStyles.DestructiveButton, options);
        }

        public static Color StatusColor(EditorUiStatus status)
        {
            EditorUiPalette palette = EditorUiPalette.Current;
            switch (status)
            {
                case EditorUiStatus.Success:
                    return palette.Success;
                case EditorUiStatus.Warning:
                    return palette.Warning;
                case EditorUiStatus.Error:
                    return palette.Error;
                case EditorUiStatus.InProgress:
                    return palette.Accent;
                default:
                    return palette.MutedText;
            }
        }

        private sealed class SectionScope : IDisposable
        {
            public SectionScope(string title, string subtitle, GUILayoutOption[] options)
            {
                EditorGUILayout.BeginVertical(EditorUiStyles.Card, options);
                GUILayout.Label(title, EditorUiStyles.SectionTitle);
                if (!string.IsNullOrWhiteSpace(subtitle))
                    GUILayout.Label(subtitle, EditorUiStyles.HeaderSubtitle);
                EditorGUILayout.Space(EditorUiTokens.SpaceXs);
            }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
            }
        }
    }
}
