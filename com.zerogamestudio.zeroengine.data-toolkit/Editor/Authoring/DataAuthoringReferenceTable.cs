using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public readonly struct DataAuthoringReferenceRow
    {
        public DataAuthoringReferenceRow(
            string assetPath,
            string referenceKind,
            string assetType = null,
            string stableId = null,
            string details = null)
        {
            AssetPath = assetPath ?? string.Empty;
            ReferenceKind = string.IsNullOrWhiteSpace(referenceKind) ? "Reference" : referenceKind.Trim();
            AssetType = assetType ?? string.Empty;
            StableId = stableId ?? string.Empty;
            Details = details ?? string.Empty;
        }

        public string AssetPath { get; }
        public string ReferenceKind { get; }
        public string AssetType { get; }
        public string StableId { get; }
        public string Details { get; }
    }

    public readonly struct DataAuthoringReferenceTableResult
    {
        public DataAuthoringReferenceTableResult(IReadOnlyList<DataAuthoringReferenceRow> rows, int totalCount)
        {
            Rows = rows ?? Array.Empty<DataAuthoringReferenceRow>();
            TotalCount = Math.Max(0, totalCount);
        }

        public IReadOnlyList<DataAuthoringReferenceRow> Rows { get; }
        public int TotalCount { get; }
        public bool HasOverflow => TotalCount > Rows.Count;
    }

    public static class DataAuthoringReferenceTable
    {
        public static DataAuthoringReferenceTableResult Build(
            IEnumerable<DataAuthoringReferenceRow> rows,
            int maxRows,
            string searchText = null)
        {
            var query = rows ?? Array.Empty<DataAuthoringReferenceRow>();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                query = query.Where(row => Matches(row, search));
            }

            var sorted = query
                .Where(row => !string.IsNullOrWhiteSpace(row.AssetPath))
                .OrderBy(row => row.AssetPath, StringComparer.Ordinal)
                .ThenBy(row => row.ReferenceKind, StringComparer.Ordinal)
                .ToArray();
            return new DataAuthoringReferenceTableResult(
                sorted.Take(Math.Max(0, maxRows)).ToArray(),
                sorted.Length);
        }

        public static void Draw(
            DataAuthoringReferenceTableResult result,
            Action<DataAuthoringReferenceRow> pingAction = null,
            string pingLabel = "Ping")
        {
            var rows = result.Rows ?? Array.Empty<DataAuthoringReferenceRow>();
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No references.", MessageType.Info);
                return;
            }

            DrawHeader(pingAction != null);
            foreach (var row in rows)
            {
                DrawRow(row, pingAction, pingLabel);
            }
        }

        public static bool Matches(DataAuthoringReferenceRow row, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return Contains(row.AssetPath, searchText)
                || Contains(row.ReferenceKind, searchText)
                || Contains(row.AssetType, searchText)
                || Contains(row.StableId, searchText)
                || Contains(row.Details, searchText);
        }

        private static void DrawHeader(bool hasAction)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("类型", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
                GUILayout.Label("资产", EditorStyles.miniBoldLabel, GUILayout.MinWidth(220f));
                GUILayout.Label("备注", EditorStyles.miniBoldLabel, GUILayout.Width(140f));
                if (hasAction)
                {
                    GUILayout.Label(string.Empty, GUILayout.Width(64f));
                }
            }
        }

        private static void DrawRow(
            DataAuthoringReferenceRow row,
            Action<DataAuthoringReferenceRow> pingAction,
            string pingLabel)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(row.ReferenceKind, GUILayout.Width(72f));
                EditorGUILayout.SelectableLabel(row.AssetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(220f));
                EditorGUILayout.LabelField(BuildDetails(row), EditorStyles.miniLabel, GUILayout.Width(140f));
                if (pingAction != null)
                {
                    if (GUILayout.Button(new GUIContent(pingLabel, "Locate the referencing asset."), GUILayout.Width(64f)))
                    {
                        pingAction(row);
                    }
                }
            }
        }

        private static string BuildDetails(DataAuthoringReferenceRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.Details))
            {
                return row.Details;
            }

            if (!string.IsNullOrWhiteSpace(row.StableId))
            {
                return row.StableId;
            }

            return row.AssetType;
        }

        private static bool Contains(string value, string searchText)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
