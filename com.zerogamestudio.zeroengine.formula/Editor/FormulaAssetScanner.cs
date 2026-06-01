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
            var report = ScanAll("Assets");
            var message = $"ZeroEngine Formula scan complete. Assets={report.AssetCount}, Errors={report.ErrorCount}, Warnings={report.WarningCount}";
            if (report.HasErrors) Debug.LogError(message);
            else Debug.Log(message);
        }

        public static FormulaAssetScanReport ScanAll(string searchRoot)
        {
            var report = new FormulaAssetScanReport();
            var guids = AssetDatabase.FindAssets("t:FormulaAsset", new[] { searchRoot });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                report.AssetCount++;
                ScanFormula(path, formula, report);
            }

            return report;
        }

        private static void ScanFormula(string path, FormulaAsset formula, FormulaAssetScanReport report)
        {
            if (!formula)
            {
                report.AddIssue(FormulaAssetScanSeverity.Error, path, "Formula asset failed to load.");
                return;
            }

            FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
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
    }
}
