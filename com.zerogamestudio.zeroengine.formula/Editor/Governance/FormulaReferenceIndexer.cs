using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaReferenceTextDocument
    {
        public FormulaReferenceTextDocument(string assetPath, string text)
        {
            AssetPath = assetPath ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string AssetPath { get; }
        public string Text { get; }
    }

    public sealed class FormulaReferenceSearchOptions
    {
        public FormulaReferenceSearchOptions(
            IReadOnlyList<string> referenceRoots,
            IReadOnlyList<string> excludedReferenceRoots)
        {
            ReferenceRoots = referenceRoots == null
                ? Array.Empty<string>()
                : new List<string>(referenceRoots).AsReadOnly();
            ExcludedReferenceRoots = excludedReferenceRoots == null
                ? Array.Empty<string>()
                : new List<string>(excludedReferenceRoots).AsReadOnly();
        }

        public IReadOnlyList<string> ReferenceRoots { get; }
        public IReadOnlyList<string> ExcludedReferenceRoots { get; }
    }

    public sealed class FormulaAssetReference
    {
        public FormulaAssetReference(string assetPath, string formulaGuid, string referenceKind)
        {
            AssetPath = assetPath ?? string.Empty;
            FormulaGuid = formulaGuid ?? string.Empty;
            ReferenceKind = referenceKind ?? string.Empty;
        }

        public string AssetPath { get; }
        public string FormulaGuid { get; }
        public string ReferenceKind { get; }
    }

    public static class FormulaReferenceIndexer
    {
        public static IEnumerable<FormulaAssetReference> FindGuidReferences(
            string formulaGuid,
            IEnumerable<FormulaReferenceTextDocument> documents,
            FormulaReferenceSearchOptions options)
        {
            if (string.IsNullOrWhiteSpace(formulaGuid) || documents == null)
                yield break;

            foreach (var document in documents)
            {
                if (document == null)
                    continue;

                var assetPath = NormalizePath(document.AssetPath);
                if (!IsPathIncluded(assetPath, options))
                    continue;

                if (document.Text.IndexOf(formulaGuid, StringComparison.Ordinal) < 0)
                    continue;

                yield return new FormulaAssetReference(assetPath, formulaGuid, "guid");
            }
        }

        public static bool IsPathIncluded(string assetPath, FormulaReferenceSearchOptions options)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            var excludedRoots = options?.ExcludedReferenceRoots ?? Array.Empty<string>();
            foreach (var excludedRoot in excludedRoots)
            {
                if (IsUnderRoot(assetPath, excludedRoot))
                    return false;
            }

            var referenceRoots = options?.ReferenceRoots ?? Array.Empty<string>();
            if (referenceRoots.Count == 0)
                return true;

            foreach (var referenceRoot in referenceRoots)
            {
                if (IsUnderRoot(assetPath, referenceRoot))
                    return true;
            }

            return false;
        }

        private static bool IsUnderRoot(string assetPath, string root)
        {
            var normalizedRoot = NormalizePath(root);
            if (string.IsNullOrEmpty(normalizedRoot))
                return false;

            return string.Equals(assetPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .TrimEnd('/');
        }
    }
}
