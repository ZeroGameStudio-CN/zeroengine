using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        private static readonly Regex GuidPattern = new(
            @"(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IEnumerable<FormulaAssetReference> FindGuidReferences(
            string formulaGuid,
            IEnumerable<FormulaReferenceTextDocument> documents,
            FormulaReferenceSearchOptions options)
        {
            if (string.IsNullOrWhiteSpace(formulaGuid) || documents == null)
                yield break;

            foreach (var reference in FindGuidReferences(new[] { formulaGuid }, documents, options))
                yield return reference;
        }

        public static IEnumerable<FormulaAssetReference> FindGuidReferences(
            IEnumerable<string> formulaGuids,
            IEnumerable<FormulaReferenceTextDocument> documents,
            FormulaReferenceSearchOptions options)
        {
            if (formulaGuids == null || documents == null)
                yield break;

            var canonicalGuids = formulaGuids
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(guid => guid, guid => guid, StringComparer.OrdinalIgnoreCase);
            if (canonicalGuids.Count == 0)
                yield break;

            foreach (var document in documents)
            {
                if (document == null)
                    continue;

                var assetPath = NormalizePath(document.AssetPath);
                if (!IsPathIncluded(assetPath, options))
                    continue;

                foreach (var guid in FindKnownFormulaGuids(document.Text, canonicalGuids))
                    yield return new FormulaAssetReference(assetPath, guid, "guid");
            }
        }

        internal static IReadOnlyList<string> FindKnownFormulaGuids(
            string text,
            IReadOnlyDictionary<string, string> canonicalGuids)
        {
            if (string.IsNullOrEmpty(text) || canonicalGuids == null || canonicalGuids.Count == 0)
                return Array.Empty<string>();

            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in GuidPattern.Matches(text))
            {
                if (canonicalGuids.TryGetValue(match.Value, out var canonicalGuid))
                    matches.Add(canonicalGuid);
            }

            foreach (var pair in canonicalGuids)
            {
                if (IsUnityGuid(pair.Key))
                    continue;
                if (text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(pair.Value);
            }

            return matches.OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool IsUnityGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32)
                return false;

            foreach (var character in value)
            {
                if ((character < '0' || character > '9')
                    && (character < 'a' || character > 'f')
                    && (character < 'A' || character > 'F'))
                    return false;
            }

            return true;
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

        internal static string NormalizePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .TrimEnd('/');
        }
    }
}
