using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests.Editor
{
    internal sealed class EditorUiGalleryWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _details;

        [MenuItem("ZeroEngine Tests/Editor UI Gallery")]
        private static void Open()
        {
            var window = GetWindow<EditorUiGalleryWindow>("Editor UI Gallery");
            window.minSize = new Vector2(640f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorUiGUILayout.CompactHeader(
                "ZeroEngine Editor UI",
                "Shared hierarchy, semantic status, spacing, and theme palettes.",
                "Test assembly only",
                () => EditorUiGUILayout.Chip(EditorUiGUILayout.ResponsiveMode(position.width).ToString()));

            using (EditorUiGUILayout.Section("Compact rows", "Dashboard-style hierarchy and progressive disclosure"))
            {
                EditorUiGUILayout.ActionRow(
                    "Formula Studio",
                    "Catalog and Workbench share one normal window host.",
                    () =>
                    {
                        EditorUiGUILayout.PrimaryButton("Catalog");
                        GUILayout.Button("Workbench");
                    });
                EditorUiGUILayout.Chip("POB");
                _details = EditorUiGUILayout.Disclosure(_details, "Details");
                if (_details)
                    EditorGUILayout.SelectableLabel("module/entry · ZeroEngine/Menu/Path", EditorStyles.miniLabel);
            }

            using (EditorUiGUILayout.Section("Actions", "Primary, secondary, destructive, and validation states"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorUiGUILayout.PrimaryButton("Primary");
                    GUILayout.Button("Secondary");
                    EditorUiGUILayout.DestructiveButton("Destructive");
                }

                EditorUiGUILayout.StatusLabel("Success · ready", EditorUiStatus.Success);
                EditorUiGUILayout.StatusLabel("Warning · review", EditorUiStatus.Warning);
                EditorUiGUILayout.StatusLabel("Error · blocked", EditorUiStatus.Error);
                EditorUiGUILayout.ValidationSummary("Validation summary", EditorUiStatus.Warning);
                EditorUiGUILayout.EmptyState("No items match the current filter.");
            }

            using (EditorUiGUILayout.Section("Palette previews", "Custom colors only; built-in controls use the current Editor theme"))
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPalette("Light", EditorUiPalette.ResolveForSkin(false));
                DrawPalette("Dark", EditorUiPalette.ResolveForSkin(true));
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawPalette(string label, EditorUiPalette palette)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(250f)))
            {
                GUILayout.Label(label, EditorStyles.boldLabel);
                DrawSwatch("Surface", palette.Surface);
                DrawSwatch("Raised", palette.RaisedSurface);
                DrawSwatch("Accent", palette.Accent);
                DrawSwatch("Success", palette.Success);
                DrawSwatch("Warning", palette.Warning);
                DrawSwatch("Error", palette.Error);
            }
        }

        private static void DrawSwatch(string label, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect swatch = new Rect(rect.x, rect.y + 2f, 44f, rect.height - 4f);
            EditorGUI.DrawRect(swatch, color);
            EditorGUI.LabelField(new Rect(rect.x + 52f, rect.y, rect.width - 52f, rect.height), label);
        }
    }
}
