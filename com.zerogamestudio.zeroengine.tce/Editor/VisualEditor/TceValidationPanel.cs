using System.Collections.Generic;
using System.Globalization;
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

        public static bool TryGetFocus(TceValidationIssue issue, out TceGraphLane lane, out int index, out string fieldPath)
        {
            lane = TceGraphLane.Trigger;
            index = -1;
            fieldPath = string.Empty;

            string path = issue.Path ?? string.Empty;
            string lanePrefix = null;

            if (path.StartsWith("triggers[", System.StringComparison.Ordinal))
            {
                lane = TceGraphLane.Trigger;
                lanePrefix = "triggers[";
            }
            else if (path.StartsWith("conditions[", System.StringComparison.Ordinal))
            {
                lane = TceGraphLane.Condition;
                lanePrefix = "conditions[";
            }
            else if (path.StartsWith("effects[", System.StringComparison.Ordinal))
            {
                lane = TceGraphLane.Effect;
                lanePrefix = "effects[";
            }

            if (lanePrefix == null)
                return false;

            int closeBracket = path.IndexOf(']', lanePrefix.Length);
            if (closeBracket < 0)
                return false;

            string indexText = path.Substring(lanePrefix.Length, closeBracket - lanePrefix.Length);
            if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                return false;

            if (closeBracket + 1 >= path.Length)
            {
                fieldPath = string.Empty;
                return true;
            }

            if (path[closeBracket + 1] != '.')
                return false;

            fieldPath = path.Substring(closeBracket + 2);

            return true;
        }
    }
}
