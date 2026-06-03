using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaRenamePlan
    {
        public FormulaRenamePlan(
            string currentPath,
            string newPath,
            string formulaGuid,
            IReadOnlyList<FormulaAssetReference> references,
            IReadOnlyList<string> blockingIssues)
        {
            CurrentPath = currentPath ?? string.Empty;
            NewPath = newPath ?? string.Empty;
            FormulaGuid = formulaGuid ?? string.Empty;
            References = references ?? Array.Empty<FormulaAssetReference>();
            BlockingIssues = blockingIssues ?? Array.Empty<string>();
        }

        public string CurrentPath { get; }
        public string NewPath { get; }
        public string FormulaGuid { get; }
        public IReadOnlyList<FormulaAssetReference> References { get; }
        public IReadOnlyList<string> BlockingIssues { get; }
        public bool CanApply => BlockingIssues.Count == 0;
    }

    public static class FormulaRenamePlanner
    {
        public static FormulaRenamePlan CreateDryRun(
            string currentPath,
            string newAssetName,
            string formulaGuid,
            IReadOnlyList<FormulaAssetReference> references,
            bool addressablesSyncSupported)
        {
            var blockingIssues = new List<string>();
            var normalizedCurrentPath = NormalizePath(currentPath);
            var trimmedName = (newAssetName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmedName))
                blockingIssues.Add("新公式名称不能为空。");

            var extension = Path.GetExtension(normalizedCurrentPath);
            var directory = Path.GetDirectoryName(normalizedCurrentPath)?.Replace('\\', '/') ?? string.Empty;
            var newPath = string.IsNullOrEmpty(trimmedName)
                ? normalizedCurrentPath
                : $"{directory}/{trimmedName}{extension}";

            IReadOnlyList<FormulaAssetReference> copiedReferences = references == null
                ? Array.Empty<FormulaAssetReference>()
                : new List<FormulaAssetReference>(references).AsReadOnly();

            if (!addressablesSyncSupported
                && copiedReferences.Any(reference => string.Equals(reference.ReferenceKind, "addressables", StringComparison.OrdinalIgnoreCase)))
            {
                blockingIssues.Add("检测到 Addressables 引用，但当前项目未声明自动同步能力。");
            }

            return new FormulaRenamePlan(
                normalizedCurrentPath,
                newPath,
                formulaGuid,
                copiedReferences,
                blockingIssues.AsReadOnly());
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
