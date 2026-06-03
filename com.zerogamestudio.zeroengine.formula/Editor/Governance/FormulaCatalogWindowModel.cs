using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZeroEngine.Formula.Editor
{
    public enum FormulaCatalogWindowFilter
    {
        All,
        Errors,
        Warnings,
        MissingCatalog,
        Unreferenced,
    }

    public sealed class FormulaCatalogAssetRecord
    {
        public FormulaCatalogAssetRecord(string assetPath, string formulaGuid, string displayName, FormulaAsset formula)
        {
            AssetPath = assetPath ?? string.Empty;
            FormulaGuid = formulaGuid ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Formula = formula;
        }

        public string AssetPath { get; }
        public string FormulaGuid { get; }
        public string DisplayName { get; }
        public FormulaAsset Formula { get; }
    }

    public sealed class FormulaCatalogWindowRow
    {
        public FormulaCatalogWindowRow(
            FormulaCatalogAssetRecord asset,
            FormulaCatalogEntry catalogEntry,
            int referenceCount,
            IReadOnlyList<FormulaAssetScanIssue> issues)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            CatalogEntry = catalogEntry;
            ReferenceCount = referenceCount;
            Issues = issues == null
                ? Array.Empty<FormulaAssetScanIssue>()
                : new List<FormulaAssetScanIssue>(issues).AsReadOnly();
        }

        public FormulaCatalogAssetRecord Asset { get; }
        public FormulaCatalogEntry CatalogEntry { get; }
        public int ReferenceCount { get; }
        public IReadOnlyList<FormulaAssetScanIssue> Issues { get; }
        public string AssetPath => Asset.AssetPath;
        public string FormulaGuid => Asset.FormulaGuid;
        public string DisplayName => Asset.DisplayName;
        public FormulaAsset Formula => Asset.Formula;
        public bool HasCatalogEntry => CatalogEntry != null;
        public FormulaCatalogStatus Status => CatalogEntry?.Status ?? FormulaCatalogStatus.Draft;
        public string Title => string.IsNullOrEmpty(CatalogEntry?.Title) ? DisplayName : CatalogEntry.Title;
        public string Purpose => CatalogEntry?.Purpose ?? string.Empty;
        public int ErrorCount => Issues.Count(issue => issue.Severity == FormulaAssetScanSeverity.Error);
        public int WarningCount => Issues.Count(issue => issue.Severity == FormulaAssetScanSeverity.Warning);
    }

    public static class FormulaCatalogWindowModel
    {
        public static IReadOnlyList<FormulaCatalogWindowRow> BuildRows(
            IEnumerable<FormulaCatalogAssetRecord> assets,
            FormulaCatalogLookup catalogLookup,
            IReadOnlyList<FormulaAssetReference> references,
            FormulaAssetScanReport scanReport)
        {
            var issuesByPath = (scanReport?.Issues ?? Array.Empty<FormulaAssetScanIssue>())
                .GroupBy(issue => issue.AssetPath ?? string.Empty)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<FormulaAssetScanIssue>)group.ToList().AsReadOnly());
            var referenceCountsByGuid = (references ?? Array.Empty<FormulaAssetReference>())
                .Where(reference => reference != null && !string.IsNullOrEmpty(reference.FormulaGuid))
                .GroupBy(reference => reference.FormulaGuid)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var rows = new List<FormulaCatalogWindowRow>();
            foreach (var asset in assets ?? Array.Empty<FormulaCatalogAssetRecord>())
            {
                FormulaCatalogEntry catalogEntry = null;
                if (catalogLookup != null)
                    catalogLookup.TryGetEntry(asset.Formula, asset.FormulaGuid, out catalogEntry);
                issuesByPath.TryGetValue(asset.AssetPath, out var issues);
                referenceCountsByGuid.TryGetValue(asset.FormulaGuid, out var referenceCount);
                rows.Add(new FormulaCatalogWindowRow(asset, catalogEntry, referenceCount, issues));
            }

            return rows
                .OrderByDescending(row => row.ErrorCount)
                .ThenByDescending(row => row.WarningCount)
                .ThenBy(row => row.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        public static bool MatchesFilter(FormulaCatalogWindowRow row, FormulaCatalogWindowFilter filter)
        {
            if (row == null)
                return false;

            switch (filter)
            {
                case FormulaCatalogWindowFilter.Errors:
                    return row.ErrorCount > 0;
                case FormulaCatalogWindowFilter.Warnings:
                    return row.WarningCount > 0;
                case FormulaCatalogWindowFilter.MissingCatalog:
                    return !row.HasCatalogEntry;
                case FormulaCatalogWindowFilter.Unreferenced:
                    return row.ReferenceCount == 0;
                default:
                    return true;
            }
        }

        public static FormulaCatalogEntry CreateDraftEntry(
            FormulaAsset formula,
            string formulaGuid,
            string assetPath)
        {
            var title = formula != null && !string.IsNullOrWhiteSpace(formula.FormulaName)
                ? formula.FormulaName
                : Path.GetFileNameWithoutExtension(assetPath ?? string.Empty);
            return new FormulaCatalogEntry(
                formula,
                formulaGuid,
                title,
                string.Empty,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                FormulaCatalogStatus.Draft,
                FormulaResultRange.None,
                string.Empty);
        }
    }
}
