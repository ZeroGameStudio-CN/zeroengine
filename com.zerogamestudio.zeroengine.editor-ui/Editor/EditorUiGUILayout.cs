using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI
{
    public enum EditorUiResponsiveMode
    {
        Compact,
        Standard,
        Wide
    }

    public enum EditorUiActionRowMode
    {
        Inline,
        Stacked
    }

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
            CompactHeader(title, subtitle, context);
        }

        public static void CompactHeader(string title, string subtitle, string context = null, Action drawTrailing = null)
        {
            EditorUiStyles.EnsureCurrent();
            using (new EditorGUILayout.HorizontalScope(EditorUiStyles.CompactHeader))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(title, EditorUiStyles.HeaderTitle);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        GUILayout.Label(subtitle, EditorUiStyles.HeaderSubtitle);
                    if (!string.IsNullOrWhiteSpace(context))
                        GUILayout.Label(context, EditorStyles.miniLabel);
                }

                if (drawTrailing != null)
                {
                    GUILayout.FlexibleSpace();
                    drawTrailing();
                }
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
            return PrimaryButton(new GUIContent(label), options);
        }

        public static bool PrimaryButton(GUIContent content, params GUILayoutOption[] options)
        {
            EditorUiStyles.EnsureCurrent();
            return GUILayout.Button(content, EditorUiStyles.PrimaryButton, options);
        }

        public static bool SelectionButton(string label, bool selected, params GUILayoutOption[] options)
        {
            return SelectionButton(new GUIContent(label), selected, options);
        }

        public static bool SelectionButton(GUIContent content, bool selected, params GUILayoutOption[] options)
        {
            Color previous = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = EditorUiPalette.Current.Selection;
            bool clicked = GUILayout.Button(
                content,
                selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                options);
            GUI.backgroundColor = previous;
            return clicked;
        }

        public static bool DestructiveButton(string label, params GUILayoutOption[] options)
        {
            return DestructiveButton(new GUIContent(label), options);
        }

        public static bool DestructiveButton(GUIContent content, params GUILayoutOption[] options)
        {
            EditorUiStyles.EnsureCurrent();
            return GUILayout.Button(content, EditorUiStyles.DestructiveButton, options);
        }

        public static void ActionRow(string title, string description, Action drawTrailing = null)
        {
            ActionRow(
                new GUIContent(title, description),
                new GUIContent(description, description),
                drawTrailing);
        }

        public static void ActionRow(GUIContent title, GUIContent description, Action drawTrailing = null)
        {
            ActionRow(title, description, drawTrailing, EditorUiActionRowMode.Inline);
        }

        public static void ActionRow(
            GUIContent title,
            GUIContent description,
            Action drawTrailing,
            EditorUiActionRowMode mode)
        {
            EditorUiStyles.EnsureCurrent();
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.ActionRow))
            {
                if (mode == EditorUiActionRowMode.Inline)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawActionRowText(title, description);
                        if (drawTrailing != null)
                        {
                            GUILayout.Space(EditorUiTokens.SpaceMd);
                            drawTrailing();
                        }
                    }
                    return;
                }

                DrawActionRowText(title, description);
                if (drawTrailing != null)
                {
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        drawTrailing();
                    }
                }
            }
        }

        public static EditorUiActionRowMode ResolveActionRowMode(
            float availableWidth,
            float trailingWidth,
            float minimumContentWidth = EditorUiTokens.ActionRowMinimumContentWidth)
        {
            float required = Math.Max(0f, trailingWidth) +
                             Math.Max(0f, minimumContentWidth) +
                             EditorUiTokens.SpaceMd;
            return availableWidth >= required
                ? EditorUiActionRowMode.Inline
                : EditorUiActionRowMode.Stacked;
        }

        public static void Chip(string text, params GUILayoutOption[] options)
        {
            Chip(new GUIContent(text), options);
        }

        public static void Chip(GUIContent content, params GUILayoutOption[] options)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.text))
                return;
            EditorUiStyles.EnsureCurrent();
            GUILayout.Label(content, EditorUiStyles.Chip, options);
        }

        public static bool Disclosure(bool expanded, string label)
        {
            return Disclosure(expanded, new GUIContent(label));
        }

        public static bool Disclosure(bool expanded, GUIContent content)
        {
            return EditorGUILayout.Foldout(expanded, content, true);
        }

        public static EditorUiResponsiveMode ResponsiveMode(float width)
        {
            if (width < EditorUiTokens.CompactBreakpoint)
                return EditorUiResponsiveMode.Compact;
            return width >= EditorUiTokens.WideBreakpoint
                ? EditorUiResponsiveMode.Wide
                : EditorUiResponsiveMode.Standard;
        }

        public static IDisposable ConstrainedContent(float maxWidth = EditorUiTokens.FormContentMaxWidth)
        {
            return new ConstrainedContentScope(maxWidth);
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

        private static void DrawActionRowText(GUIContent title, GUIContent description)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                GUILayout.Label(title, EditorStyles.boldLabel);
                if (description != null && !string.IsNullOrWhiteSpace(description.text))
                    GUILayout.Label(description, EditorStyles.wordWrappedMiniLabel);
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

        private sealed class ConstrainedContentScope : IDisposable
        {
            public ConstrainedContentScope(float maxWidth)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth), GUILayout.ExpandWidth(true));
            }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
