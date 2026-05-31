using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public readonly struct DataAuthoringIssueTableResult
    {
        public DataAuthoringIssueTableResult(IReadOnlyList<DataAuthoringIssue> rows, int totalCount)
        {
            Rows = rows ?? Array.Empty<DataAuthoringIssue>();
            TotalCount = Math.Max(0, totalCount);
        }

        public IReadOnlyList<DataAuthoringIssue> Rows { get; }
        public int TotalCount { get; }
        public bool HasOverflow => TotalCount > Rows.Count;
    }

    public static class DataAuthoringIssueTable
    {
        public static DataAuthoringIssueTableResult Build(
            IEnumerable<DataAuthoringIssue> issues,
            int maxRows,
            string searchText = null)
        {
            return Build(issues, maxRows, searchText, null);
        }

        public static DataAuthoringIssueTableResult Build(
            IEnumerable<DataAuthoringIssue> issues,
            int maxRows,
            string searchText,
            DataAuthoringIssueTableLabels labels)
        {
            var query = issues ?? Array.Empty<DataAuthoringIssue>();
            var resolvedLabels = labels ?? DataAuthoringIssueTableLabels.Default;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                query = query.Where(issue => Matches(issue, search, resolvedLabels));
            }

            var sorted = query
                .OrderBy(GetSeveritySortOrder)
                .ThenBy(issue => issue.AssetType, StringComparer.Ordinal)
                .ThenBy(issue => issue.StableId, StringComparer.Ordinal)
                .ThenBy(issue => issue.AssetPath, StringComparer.Ordinal)
                .ThenBy(issue => issue.FieldPath, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray();
            return new DataAuthoringIssueTableResult(
                sorted.Take(Math.Max(0, maxRows)).ToArray(),
                sorted.Length);
        }

        public static void Draw(
            DataAuthoringIssueTableResult result,
            Action<DataAuthoringIssue> pingAction = null,
            DataAuthoringIssueTableLabels labels = null)
        {
            var resolvedLabels = labels ?? DataAuthoringIssueTableLabels.Default;
            var rows = result.Rows ?? Array.Empty<DataAuthoringIssue>();
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(resolvedLabels.NoIssues, MessageType.Info);
                return;
            }

            DrawHeader(pingAction != null, resolvedLabels);
            foreach (var issue in rows)
            {
                DrawRow(issue, pingAction, resolvedLabels);
            }
        }

        public static bool Matches(DataAuthoringIssue issue, string searchText)
        {
            return Matches(issue, searchText, null);
        }

        public static bool Matches(DataAuthoringIssue issue, string searchText, DataAuthoringIssueTableLabels labels)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            var resolvedLabels = labels ?? DataAuthoringIssueTableLabels.Default;
            return Contains(issue.Severity.ToString(), searchText)
                || Contains(resolvedLabels.SeverityLabels.Format(issue.Severity), searchText)
                || Contains(issue.AssetType, searchText)
                || Contains(issue.StableId, searchText)
                || Contains(issue.AssetPath, searchText)
                || Contains(issue.FieldPath, searchText)
                || Contains(issue.Message, searchText);
        }

        private static int GetSeveritySortOrder(DataAuthoringIssue issue)
        {
            return issue.Severity switch
            {
                DataAuthoringIssueSeverity.Error => 0,
                DataAuthoringIssueSeverity.Warning => 1,
                _ => 2
            };
        }

        private static void DrawHeader(bool hasAction, DataAuthoringIssueTableLabels labels)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(labels.Severity, EditorStyles.miniBoldLabel, GUILayout.Width(58f));
                GUILayout.Label(labels.Group, EditorStyles.miniBoldLabel, GUILayout.Width(92f));
                GUILayout.Label(labels.StableId, EditorStyles.miniBoldLabel, GUILayout.Width(112f));
                GUILayout.Label(labels.Asset, EditorStyles.miniBoldLabel, GUILayout.MinWidth(160f));
                GUILayout.Label(labels.Field, EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                GUILayout.Label(labels.Message, EditorStyles.miniBoldLabel, GUILayout.MinWidth(180f));
                if (hasAction)
                {
                    GUILayout.Label(labels.Action, EditorStyles.miniBoldLabel, GUILayout.Width(64f));
                }
            }
        }

        private static void DrawRow(
            DataAuthoringIssue issue,
            Action<DataAuthoringIssue> pingAction,
            DataAuthoringIssueTableLabels labels)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(labels.SeverityLabels.Format(issue.Severity), GUILayout.Width(58f));
                EditorGUILayout.LabelField(issue.AssetType, GUILayout.Width(92f));
                EditorGUILayout.LabelField(issue.StableId, EditorStyles.miniLabel, GUILayout.Width(112f));
                EditorGUILayout.SelectableLabel(issue.AssetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(160f));
                EditorGUILayout.LabelField(issue.FieldPath, EditorStyles.miniLabel, GUILayout.Width(120f));
                EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(180f));
                if (pingAction != null)
                {
                    if (GUILayout.Button(new GUIContent(labels.Ping, labels.PingTooltip), GUILayout.Width(64f)))
                    {
                        pingAction(issue);
                    }
                }
            }
        }

        private static bool Contains(string value, string searchText)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
