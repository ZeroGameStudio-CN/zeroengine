using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Dlc.Editor
{
    public enum DlcValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class DlcValidationIssue
    {
        public DlcValidationIssue(DlcValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public DlcValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class DlcConfigValidator
    {
        public static IReadOnlyList<DlcValidationIssue> Validate(IEnumerable<ContentPackCatalog> catalogs = null)
        {
            var catalogList = catalogs != null ? catalogs.ToList() : LoadAssets<ContentPackCatalog>().ToList();
            var issues = new List<DlcValidationIssue>();

            foreach (var catalog in catalogList)
                ValidateCatalog(issues, catalog);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static void ValidateCatalog(List<DlcValidationIssue> issues, ContentPackCatalog catalog)
        {
            if (catalog == null)
                return;

            if (catalog.ContentPacks == null || catalog.ContentPacks.Count == 0)
            {
                Add(issues, DlcValidationSeverity.Warning, catalog, "ContentPacks", "Content pack catalog is empty.");
                return;
            }

            var seenIds = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < catalog.ContentPacks.Count; i++)
            {
                var pack = catalog.ContentPacks[i];
                string path = $"ContentPacks[{i}]";
                if (pack == null)
                {
                    Add(issues, DlcValidationSeverity.Error, catalog, path, "Content pack entry is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(pack.ContentPackId))
                {
                    Add(issues, DlcValidationSeverity.Error, catalog, $"{path}.ContentPackId", "Content pack must have a stable ID.");
                }
                else if (!seenIds.Add(pack.ContentPackId))
                {
                    Add(issues, DlcValidationSeverity.Error, catalog, $"{path}.ContentPackId", $"Duplicate content pack ID '{pack.ContentPackId}'.");
                }

                if (string.IsNullOrWhiteSpace(pack.DisplayName))
                    Add(issues, DlcValidationSeverity.Error, catalog, $"{path}.DisplayName", "Content pack must have a display name.");
                if (!pack.IncludedInBaseGame && string.IsNullOrWhiteSpace(pack.RequiredDlcId))
                    Add(issues, DlcValidationSeverity.Error, catalog, $"{path}.RequiredDlcId", "Paid or optional content packs must declare the required DLC ID.");
                if (pack.IncludedInBaseGame && !string.IsNullOrWhiteSpace(pack.RequiredDlcId))
                    Add(issues, DlcValidationSeverity.Warning, catalog, $"{path}.RequiredDlcId", "Base-game content should not declare a required DLC ID.");
            }
        }

        private static void Add(List<DlcValidationIssue> issues, DlcValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new DlcValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
