using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaReferenceAssetDatabase
    {
        private static readonly HashSet<string> SupportedTextExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".asset",
                ".prefab",
                ".unity",
                ".mat",
                ".json",
                ".asmdef",
                ".asmref",
                ".txt",
                ".md",
                ".meta",
                ".uxml",
                ".uss",
            };

        public static bool IsSupportedTextAssetPath(string assetPath)
        {
            return SupportedTextExtensions.Contains(Path.GetExtension(assetPath ?? string.Empty));
        }

        public static IReadOnlyList<FormulaAssetReference> FindGuidReferences(
            string formulaGuid,
            FormulaEditorProfile profile)
        {
            var documents = CollectTextDocuments(profile);
            var options = new FormulaReferenceSearchOptions(
                profile?.ReferenceRoots,
                profile?.ExcludedReferenceRoots);
            return new List<FormulaAssetReference>(
                FormulaReferenceIndexer.FindGuidReferences(formulaGuid, documents, options));
        }

        public static IReadOnlyList<FormulaReferenceTextDocument> CollectTextDocuments(FormulaEditorProfile profile)
        {
            var documents = new List<FormulaReferenceTextDocument>();
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!IsSupportedTextAssetPath(assetPath))
                    continue;

                if (!IsIncluded(assetPath, profile))
                    continue;

                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                    continue;

                documents.Add(new FormulaReferenceTextDocument(assetPath, File.ReadAllText(fullPath)));
            }

            return documents;
        }

        private static bool IsIncluded(string assetPath, FormulaEditorProfile profile)
        {
            var options = new FormulaReferenceSearchOptions(
                profile?.ReferenceRoots,
                profile?.ExcludedReferenceRoots);
            return FormulaReferenceIndexer.IsPathIncluded(assetPath, options);
        }
    }
}
