using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public enum FormulaAssetScanSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public sealed class FormulaAssetScanIssue
    {
        public FormulaAssetScanIssue(FormulaAssetScanSeverity severity, string assetPath, string message)
        {
            Severity = severity;
            AssetPath = assetPath;
            Message = message;
        }

        public FormulaAssetScanSeverity Severity { get; }
        public string AssetPath { get; }
        public string Message { get; }

        public override string ToString() => $"[{Severity}] {AssetPath}: {Message}";
    }

    public sealed class FormulaAssetScanReport
    {
        private readonly List<FormulaAssetScanIssue> issues = new();

        public int AssetCount { get; set; }
        public IReadOnlyList<FormulaAssetScanIssue> Issues => issues;
        public bool HasErrors => issues.Any(i => i.Severity == FormulaAssetScanSeverity.Error);
        public int ErrorCount => issues.Count(i => i.Severity == FormulaAssetScanSeverity.Error);
        public int WarningCount => issues.Count(i => i.Severity == FormulaAssetScanSeverity.Warning);

        public void AddIssue(FormulaAssetScanSeverity severity, string assetPath, string message)
        {
            issues.Add(new FormulaAssetScanIssue(severity, assetPath, message));
        }
    }

    public sealed class FormulaAssetScanContext
    {
        private readonly IReadOnlyDictionary<string, string> formulaGuidsByAssetPath;

        public FormulaAssetScanContext(
            FormulaCatalogLookup catalogLookup,
            IReadOnlyList<FormulaAssetReference> references,
            IReadOnlyDictionary<string, string> formulaGuidsByAssetPath)
        {
            CatalogLookup = catalogLookup;
            References = references ?? System.Array.Empty<FormulaAssetReference>();
            this.formulaGuidsByAssetPath = formulaGuidsByAssetPath
                ?? new Dictionary<string, string>();
        }

        public FormulaCatalogLookup CatalogLookup { get; }
        public IReadOnlyList<FormulaAssetReference> References { get; }

        public string GetFormulaGuid(string assetPath)
        {
            return formulaGuidsByAssetPath.TryGetValue(assetPath ?? string.Empty, out var guid)
                ? guid
                : string.Empty;
        }
    }

    public static class FormulaAssetScanner
    {
        public static void RunMenu()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            var report = Scan(profile);
            var message = $"ZeroEngine 公式扫描完成。Profile={profile.DisplayName} ({profile.ProfileId}), Assets={report.AssetCount}, Errors={report.ErrorCount}, Warnings={report.WarningCount}";
            if (report.HasErrors) Debug.LogError(message);
            else Debug.Log(message);
        }

        public static FormulaAssetScanReport ScanAll(string searchRoot)
        {
            return Scan(searchRoot, null);
        }

        public static FormulaAssetScanReport Scan(FormulaEditorProfile profile)
        {
            var searchRoot = string.IsNullOrEmpty(profile?.DefaultSearchRoot)
                ? "Assets"
                : profile.DefaultSearchRoot;
            return Scan(searchRoot, profile);
        }

        public static FormulaAssetScanReport ScanAsset(
            string assetPath,
            FormulaAsset formula,
            FormulaEditorProfile profile,
            FormulaAssetScanContext scanContext = null)
        {
            var report = new FormulaAssetScanReport { AssetCount = 1 };
            ScanFormula(
                string.IsNullOrEmpty(assetPath) ? "<formula>" : assetPath,
                formula,
                profile,
                FormulaEditorPreview.CreateContext(profile),
                FormulaEditorPreview.CreateRegistry(profile),
                report,
                scanContext);
            return report;
        }

        public static FormulaAssetScanReport Scan(string searchRoot, FormulaEditorProfile profile)
        {
            var report = new FormulaAssetScanReport();
            var root = string.IsNullOrEmpty(searchRoot) ? "Assets" : searchRoot;
            var registry = FormulaEditorPreview.CreateRegistry(profile);
            var context = FormulaEditorPreview.CreateContext(profile);
            var scanContext = CreateAssetDatabaseScanContext(profile);
            var guids = AssetDatabase.FindAssets("t:FormulaAsset", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                report.AssetCount++;
                ScanFormula(path, formula, profile, context, registry, report, scanContext);
            }

            return report;
        }

        private static void ScanFormula(
            string path,
            FormulaAsset formula,
            FormulaEditorProfile profile,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaAssetScanReport report,
            FormulaAssetScanContext scanContext)
        {
            if (!formula)
            {
                report.AddIssue(FormulaAssetScanSeverity.Error, path, "Formula asset failed to load.");
                return;
            }

            FormulaEvaluator.TryEvaluate(
                formula,
                context,
                registry,
                FormulaEditorPreview.CreateRandomSource(),
                out var value,
                out var evalReport);

            ScanQualityRules(path, formula, profile, value, scanContext, report);

            foreach (var diagnostic in evalReport.Diagnostics)
            {
                var severity = diagnostic.Severity == FormulaDiagnosticSeverity.Error
                    ? FormulaAssetScanSeverity.Error
                    : FormulaAssetScanSeverity.Warning;
                report.AddIssue(severity, path, diagnostic.Message);
            }
        }

        private static void ScanQualityRules(
            string path,
            FormulaAsset formula,
            FormulaEditorProfile profile,
            float value,
            FormulaAssetScanContext scanContext,
            FormulaAssetScanReport report)
        {
            var rules = profile?.QualityRules ?? FormulaAssetQualityRules.None;
            if (rules.WarnOnEmptySteps && formula.StepCount == 0)
            {
                report.AddIssue(
                    FormulaAssetScanSeverity.Warning,
                    path,
                    "公式没有配置步骤，只会返回初始值。");
            }

            foreach (var pattern in rules.TemporaryNamePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)
                    || formula.FormulaName.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                report.AddIssue(
                    FormulaAssetScanSeverity.Warning,
                    path,
                    $"公式名称像临时命名：{formula.FormulaName}。请改成表达用途的名称。");
                break;
            }

            var formulaGuid = scanContext?.GetFormulaGuid(path) ?? string.Empty;
            FormulaCatalogEntry catalogEntry = null;
            var hasCatalogEntry = scanContext?.CatalogLookup?.TryGetEntry(formula, formulaGuid, out catalogEntry) == true;
            if (rules.WarnOnMissingCatalogEntry && !hasCatalogEntry)
            {
                report.AddIssue(
                    FormulaAssetScanSeverity.Warning,
                    path,
                    "公式缺少目录信息：请在 Formula Catalog 中补充用途、Owner、单位和标签。");
            }

            if (rules.WarnOnUnreferencedFormula
                && !string.IsNullOrEmpty(formulaGuid)
                && scanContext?.References.Any(reference => reference.FormulaGuid == formulaGuid) != true)
            {
                report.AddIssue(
                    FormulaAssetScanSeverity.Warning,
                    path,
                    "公式没有被任何配置引用，请确认是否为废弃或占位资产。");
            }

            if (hasCatalogEntry
                && catalogEntry.ExpectedRange.Enabled
                && (value < catalogEntry.ExpectedRange.Min || value > catalogEntry.ExpectedRange.Max))
            {
                report.AddIssue(
                    FormulaAssetScanSeverity.Warning,
                    path,
                    $"公式预览结果 {value} 超出目录期望范围 [{catalogEntry.ExpectedRange.Min}, {catalogEntry.ExpectedRange.Max}]。");
            }
        }

        private static FormulaAssetScanContext CreateAssetDatabaseScanContext(FormulaEditorProfile profile)
        {
            if (profile == null)
                return null;

            FormulaCatalogLookup catalogLookup = null;
            if (!string.IsNullOrEmpty(profile.CatalogAssetPath))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<FormulaCatalog>(profile.CatalogAssetPath);
                catalogLookup = catalog?.CreateLookup();
            }

            var formulaGuidsByPath = new Dictionary<string, string>();
            var formulaGuids = AssetDatabase.FindAssets("t:FormulaAsset", new[] { string.IsNullOrEmpty(profile.DefaultSearchRoot) ? "Assets" : profile.DefaultSearchRoot });
            foreach (var guid in formulaGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    formulaGuidsByPath[path] = guid;
            }

            var references = new List<FormulaAssetReference>();
            var referenceDocuments = FormulaReferenceAssetDatabase.CollectTextDocuments(profile);
            var referenceSearchOptions = new FormulaReferenceSearchOptions(
                profile.ReferenceRoots,
                profile.ExcludedReferenceRoots);
            foreach (var pair in formulaGuidsByPath)
            {
                references.AddRange(FormulaReferenceIndexer.FindGuidReferences(pair.Value, referenceDocuments, referenceSearchOptions));
            }

            return new FormulaAssetScanContext(catalogLookup, references, formulaGuidsByPath);
        }
    }
}
