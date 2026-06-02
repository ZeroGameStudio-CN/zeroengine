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
            var registry = CreatePreviewRegistry(profile);
            var guids = AssetDatabase.FindAssets("t:FormulaAsset", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                report.AssetCount++;
                ScanFormula(path, formula, registry, report);
            }

            return report;
        }

        private static void ScanFormula(
            string path,
            FormulaAsset formula,
            FormulaProviderRegistry registry,
            FormulaAssetScanReport report)
        {
            if (!formula)
            {
                report.AddIssue(FormulaAssetScanSeverity.Error, path, "Formula asset failed to load.");
                return;
            }

            FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
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

        private static FormulaProviderRegistry CreatePreviewRegistry(FormulaEditorProfile profile)
        {
            if (profile == null || profile.Providers.Count == 0)
                return FormulaProviderRegistry.Empty;

            var registry = new FormulaProviderRegistry();
            foreach (var provider in profile.Providers)
                registry.Register(new ProfilePreviewFormulaProvider(provider));

            return registry;
        }

        private sealed class ProfilePreviewFormulaProvider : IFormulaValueProvider
        {
            private readonly FormulaProviderDescriptor descriptor;

            public ProfilePreviewFormulaProvider(FormulaProviderDescriptor descriptor)
            {
                this.descriptor = descriptor;
            }

            public string Id => descriptor.Id;

            public bool TryGetValue(
                FormulaProviderRequest request,
                IFormulaEvaluationContext context,
                out float value,
                FormulaDiagnosticSink diagnostics)
            {
                _ = context;

                foreach (var parameter in descriptor.Parameters)
                {
                    if (!parameter.Required || HasParameter(request, parameter))
                        continue;

                    value = 0f;
                    diagnostics.Add(
                        FormulaDiagnosticSeverity.Error,
                        FormulaDiagnosticCode.InvalidParameter,
                        $"{descriptor.DisplayName} 缺少参数：{parameter.DisplayName} ({parameter.Key})");
                    return false;
                }

                value = descriptor.PreviewValue;
                return true;
            }

            private static bool HasParameter(
                FormulaProviderRequest request,
                FormulaParameterDescriptor parameter)
            {
                switch (parameter.Kind)
                {
                    case FormulaEditorParameterKind.String:
                        return request.TryGetString(parameter.Key, out _);
                    case FormulaEditorParameterKind.Int:
                    case FormulaEditorParameterKind.Enum:
                        return request.TryGetInt(parameter.Key, out _);
                    case FormulaEditorParameterKind.Float:
                        return request.TryGetFloat(parameter.Key, out _);
                    case FormulaEditorParameterKind.Bool:
                        return request.TryGetBool(parameter.Key, out _);
                    case FormulaEditorParameterKind.Object:
                        return request.TryGetObject(parameter.Key, out _);
                    default:
                        return false;
                }
            }
        }
    }
}
