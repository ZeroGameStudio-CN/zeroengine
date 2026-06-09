using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    public static class TceValidationPanel
    {
        public static void DrawIssues(IReadOnlyList<TceValidationIssue> issues)
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
