using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    [CustomEditor(typeof(TceGraphAsset))]
    public sealed class TceGraphAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var graphAsset = (TceGraphAsset)target;
            TceGraphAssetMigration.MigrateToCurrent(graphAsset);

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(TceGraphSerializedAccess.DisplayNameProperty));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(TceGraphSerializedAccess.CategoryProperty));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(TceGraphSerializedAccess.DescriptionProperty));

            if (GUILayout.Button("Open TCE Graph Editor"))
                TceEditorWindow.Open(graphAsset);

            DrawIssues(TceGraphAssetValidator.Validate(graphAsset));
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawIssues(IReadOnlyList<TceValidationIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Validation passed.", MessageType.Info);
                return;
            }

            foreach (TceValidationIssue issue in issues)
                EditorGUILayout.HelpBox($"{issue.Code} {issue.Path}: {issue.Message}", MessageType.Error);
        }
    }
}
