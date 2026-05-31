using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public readonly struct DataAuthoringChangeTableResult
    {
        public DataAuthoringChangeTableResult(IReadOnlyList<TabularImportChange> rows, int totalCount)
        {
            Rows = rows ?? Array.Empty<TabularImportChange>();
            TotalCount = Math.Max(0, totalCount);
        }

        public IReadOnlyList<TabularImportChange> Rows { get; }
        public int TotalCount { get; }
        public bool HasOverflow => TotalCount > Rows.Count;
    }

    public static class DataAuthoringChangeTable
    {
        public static DataAuthoringChangeTableResult Build(
            IEnumerable<TabularImportChange> changes,
            int maxRows,
            string searchText = null)
        {
            var query = changes ?? Array.Empty<TabularImportChange>();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                query = query.Where(change => Matches(change, search));
            }

            var sorted = query
                .OrderBy(change => change.SheetName, StringComparer.Ordinal)
                .ThenBy(change => change.RowNumber)
                .ThenBy(change => change.StableId, StringComparer.Ordinal)
                .ThenBy(change => change.FieldPath, StringComparer.Ordinal)
                .ThenBy(change => change.Kind.ToString(), StringComparer.Ordinal)
                .ToArray();
            return new DataAuthoringChangeTableResult(
                sorted.Take(Math.Max(0, maxRows)).ToArray(),
                sorted.Length);
        }

        public static void Draw(
            DataAuthoringChangeTableResult result,
            Action<TabularImportChange> pingAction = null,
            DataAuthoringChangeTableLabels labels = null)
        {
            var resolvedLabels = labels ?? DataAuthoringChangeTableLabels.Default;
            var rows = result.Rows ?? Array.Empty<TabularImportChange>();
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(resolvedLabels.NoChanges, MessageType.Info);
                return;
            }

            DrawHeader(pingAction != null, resolvedLabels);
            foreach (var change in rows)
            {
                DrawRow(change, pingAction, resolvedLabels);
            }
        }

        public static bool Matches(TabularImportChange change, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return Contains(change.Kind.ToString(), searchText)
                || Contains(change.SheetName, searchText)
                || Contains(change.RowNumber.ToString(CultureInfo.InvariantCulture), searchText)
                || Contains(change.ColumnName, searchText)
                || Contains(change.AssetPath, searchText)
                || Contains(change.StableId, searchText)
                || Contains(change.FieldPath, searchText)
                || Contains(change.OldValue, searchText)
                || Contains(change.NewValue, searchText);
        }

        private static void DrawHeader(bool hasAction, DataAuthoringChangeTableLabels labels)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(labels.Kind, EditorStyles.miniBoldLabel, GUILayout.Width(86f));
                GUILayout.Label(labels.Sheet, EditorStyles.miniBoldLabel, GUILayout.Width(92f));
                GUILayout.Label(labels.Row, EditorStyles.miniBoldLabel, GUILayout.Width(40f));
                GUILayout.Label(labels.StableId, EditorStyles.miniBoldLabel, GUILayout.Width(112f));
                GUILayout.Label(labels.Asset, EditorStyles.miniBoldLabel, GUILayout.MinWidth(150f));
                GUILayout.Label(labels.Field, EditorStyles.miniBoldLabel, GUILayout.Width(112f));
                GUILayout.Label(labels.Old, EditorStyles.miniBoldLabel, GUILayout.Width(96f));
                GUILayout.Label(labels.New, EditorStyles.miniBoldLabel, GUILayout.Width(96f));
                if (hasAction)
                {
                    GUILayout.Label(string.Empty, GUILayout.Width(64f));
                }
            }
        }

        private static void DrawRow(
            TabularImportChange change,
            Action<TabularImportChange> pingAction,
            DataAuthoringChangeTableLabels labels)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(change.Kind.ToString(), GUILayout.Width(86f));
                EditorGUILayout.LabelField(change.SheetName, GUILayout.Width(92f));
                EditorGUILayout.LabelField(change.RowNumber.ToString(CultureInfo.InvariantCulture), GUILayout.Width(40f));
                EditorGUILayout.LabelField(change.StableId, EditorStyles.miniLabel, GUILayout.Width(112f));
                EditorGUILayout.SelectableLabel(change.AssetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(150f));
                EditorGUILayout.LabelField(change.FieldPath, EditorStyles.miniLabel, GUILayout.Width(112f));
                EditorGUILayout.LabelField(change.OldValue, EditorStyles.miniLabel, GUILayout.Width(96f));
                EditorGUILayout.LabelField(change.NewValue, EditorStyles.miniLabel, GUILayout.Width(96f));
                if (pingAction != null)
                {
                    if (GUILayout.Button(new GUIContent(labels.Ping, labels.PingTooltip), GUILayout.Width(64f)))
                    {
                        pingAction(change);
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
