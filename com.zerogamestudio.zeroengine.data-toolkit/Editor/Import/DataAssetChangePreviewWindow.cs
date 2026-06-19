using System;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetChangePreviewWindow : EditorWindow
    {
        private DataAssetChangePreview preview = DataAssetChangePreview.Empty;
        private Action<DataAssetChangePreview> applyCallback;
        private Vector2 scroll;

        public static void ShowPreview(
            string title,
            DataAssetChangePreview preview,
            Action<DataAssetChangePreview> applyCallback)
        {
            var window = GetWindow<DataAssetChangePreviewWindow>(true, string.IsNullOrWhiteSpace(title) ? "Data Change Preview" : title);
            window.preview = preview ?? DataAssetChangePreview.Empty;
            window.applyCallback = applyCallback;
            window.minSize = new Vector2(780f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Dry-run Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(preview.Summary, preview.HasBlockingIssues ? MessageType.Warning : MessageType.Info);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var change in preview.Changes)
            {
                DrawChange(change);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(!preview.HasPendingChanges);
                if (GUILayout.Button("Apply Changes", GUILayout.Width(140f), GUILayout.Height(28f)))
                {
                    applyCallback?.Invoke(preview);
                    Close();
                }

                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Close", GUILayout.Width(90f), GUILayout.Height(28f)))
                {
                    Close();
                }
            }
        }

        private static void DrawChange(DataAssetFieldChange change)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(change.AssetName, EditorStyles.boldLabel, GUILayout.Width(180f));
                    EditorGUILayout.LabelField(change.FieldPath, GUILayout.Width(180f));
                    EditorGUILayout.LabelField(change.State.ToString(), GUILayout.Width(120f));
                    EditorGUILayout.LabelField(change.Message);
                }

                EditorGUILayout.LabelField("Path", change.AssetPath);
                EditorGUILayout.LabelField("Current", change.CurrentValue);
                EditorGUILayout.LabelField("New", change.NewValue);
            }
        }
    }
}
