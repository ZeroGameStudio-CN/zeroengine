using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaScannerCliOptions
    {
        public FormulaScannerCliOptions(
            string profileId,
            string jsonReportPath,
            string markdownReportPath,
            bool failOnWarning,
            bool failOnMissingCatalog)
        {
            ProfileId = profileId ?? string.Empty;
            JsonReportPath = jsonReportPath ?? string.Empty;
            MarkdownReportPath = markdownReportPath ?? string.Empty;
            FailOnWarning = failOnWarning;
            FailOnMissingCatalog = failOnMissingCatalog;
        }

        public string ProfileId { get; }
        public string JsonReportPath { get; }
        public string MarkdownReportPath { get; }
        public bool FailOnWarning { get; }
        public bool FailOnMissingCatalog { get; }
    }

    public sealed class FormulaScannerCliResult
    {
        public FormulaScannerCliResult(int exitCode, int assetCount, int errorCount, int warningCount)
        {
            ExitCode = exitCode;
            AssetCount = assetCount;
            ErrorCount = errorCount;
            WarningCount = warningCount;
        }

        public int ExitCode { get; }
        public int AssetCount { get; }
        public int ErrorCount { get; }
        public int WarningCount { get; }
    }

    public static class FormulaScannerCli
    {
        public static void Run()
        {
            var exitCode = 0;
            try
            {
                var options = ParseArgs(Environment.GetCommandLineArgs());
                var profile = ResolveProfile(options.ProfileId);
                var report = FormulaAssetScanner.Scan(profile);
                WriteReports(report, options);
                var result = EvaluateExitCode(report, options);
                exitCode = result.ExitCode;
                var message = $"Formula scanner CLI complete. Profile={profile.ProfileId}, Assets={result.AssetCount}, Errors={result.ErrorCount}, Warnings={result.WarningCount}, ExitCode={result.ExitCode}";
                if (result.ExitCode == 0)
                    Debug.Log(message);
                else
                    Debug.LogError(message);
            }
            catch (Exception exception)
            {
                exitCode = 10;
                Debug.LogError($"Formula scanner CLI failed: {exception.Message}");
            }

            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }

        public static FormulaScannerCliOptions ParseArgs(string[] args)
        {
            var profileId = string.Empty;
            var jsonPath = string.Empty;
            var markdownPath = string.Empty;
            var failOnWarning = false;
            var failOnMissingCatalog = false;

            for (var index = 0; index < (args?.Length ?? 0); index++)
            {
                var arg = args[index] ?? string.Empty;
                switch (arg)
                {
                    case "-formulaProfile":
                        profileId = ReadNext(args, ref index);
                        break;
                    case "-formulaReportJson":
                        jsonPath = ReadNext(args, ref index);
                        break;
                    case "-formulaReportMarkdown":
                        markdownPath = ReadNext(args, ref index);
                        break;
                    case "-formulaFailOnWarning":
                        failOnWarning = true;
                        break;
                    case "-formulaFailOnMissingCatalog":
                        failOnMissingCatalog = true;
                        break;
                }
            }

            return new FormulaScannerCliOptions(
                profileId,
                jsonPath,
                markdownPath,
                failOnWarning,
                failOnMissingCatalog);
        }

        public static FormulaScannerCliResult EvaluateExitCode(
            FormulaAssetScanReport report,
            FormulaScannerCliOptions options)
        {
            var errorCount = report?.ErrorCount ?? 0;
            var warningCount = report?.WarningCount ?? 0;
            var exitCode = 0;
            if (errorCount > 0)
                exitCode = 1;
            else if (warningCount > 0 && options?.FailOnWarning == true)
                exitCode = 2;
            else if (options?.FailOnMissingCatalog == true && HasMissingCatalogWarning(report))
                exitCode = 3;

            return new FormulaScannerCliResult(
                exitCode,
                report?.AssetCount ?? 0,
                errorCount,
                warningCount);
        }

        public static void WriteReports(
            FormulaAssetScanReport report,
            FormulaScannerCliOptions options)
        {
            if (options == null)
                return;

            WriteTextIfRequested(options.JsonReportPath, FormulaAssetScanReportExporter.ToJson(report));
            WriteTextIfRequested(options.MarkdownReportPath, FormulaAssetScanReportExporter.ToMarkdown(report));
        }

        private static FormulaEditorProfile ResolveProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return FormulaEditorProfileRegistry.ActiveProfile;

            foreach (var profile in FormulaEditorProfileRegistry.RegisteredProfiles)
            {
                if (string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    FormulaEditorProfileRegistry.SetActiveProfile(profile.ProfileId);
                    return profile;
                }
            }

            throw new InvalidOperationException($"Formula profile '{profileId}' is not registered.");
        }

        private static bool HasMissingCatalogWarning(FormulaAssetScanReport report)
        {
            if (report == null)
                return false;

            foreach (var issue in report.Issues)
            {
                if (issue.Severity == FormulaAssetScanSeverity.Warning
                    && issue.Message.IndexOf("缺少目录信息", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string ReadNext(string[] args, ref int index)
        {
            if (args == null || index + 1 >= args.Length)
                return string.Empty;

            index++;
            return args[index] ?? string.Empty;
        }

        private static void WriteTextIfRequested(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, text ?? string.Empty);
        }
    }
}
