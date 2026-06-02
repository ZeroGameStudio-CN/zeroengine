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

    public static class FormulaAssetScanner
    {
        [MenuItem("ZeroEngine/Formula/Scan Formula Assets", priority = 130)]
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

        public static FormulaAssetScanReport Scan(string searchRoot, FormulaEditorProfile profile)
        {
            var report = new FormulaAssetScanReport();
            var root = string.IsNullOrEmpty(searchRoot) ? "Assets" : searchRoot;
            var registry = FormulaEditorPreview.CreateRegistry(profile);
            var context = FormulaEditorPreview.CreateContext(profile);
            var guids = AssetDatabase.FindAssets("t:FormulaAsset", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                report.AssetCount++;
                ScanFormula(path, formula, profile, context, registry, report);
            }

            return report;
        }

        private static void ScanFormula(
            string path,
            FormulaAsset formula,
            FormulaEditorProfile profile,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaAssetScanReport report)
        {
            if (!formula)
            {
                report.AddIssue(FormulaAssetScanSeverity.Error, path, "Formula asset failed to load.");
                return;
            }

            ScanQualityRules(path, formula, profile, report);

            FormulaEvaluator.TryEvaluate(
                formula,
                context,
                registry,
                out _,
                out var evalReport);

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
        }
    }
}
