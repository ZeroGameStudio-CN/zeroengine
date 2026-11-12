using System.Linq;
using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaReferenceIndexerTests
    {
        [Test]
        public void FindGuidReferences_ReturnsMatchingDocuments()
        {
            var documents = new[]
            {
                new FormulaReferenceTextDocument(
                    "Assets/Assets/_Data/Ability.asset",
                    "%YAML\n  m_FileID: 11400000, guid: abc123, type: 2"),
                new FormulaReferenceTextDocument(
                    "Assets/AddressableAssetsData/AssetGroups/Math.asset",
                    "m_Address: Assets/Assets/_Data/Math/Test.asset\nm_GUID: abc123"),
                new FormulaReferenceTextDocument(
                    "Assets/Assets/_Data/Other.asset",
                    "guid: def456"),
            };
            var options = new FormulaReferenceSearchOptions(
                new[] { "Assets/Assets/_Data", "Assets/AddressableAssetsData" },
                new[] { "Library", "Temp" });

            var references = FormulaReferenceIndexer
                .FindGuidReferences("abc123", documents, options)
                .ToArray();

            Assert.AreEqual(2, references.Length);
            Assert.AreEqual("Assets/Assets/_Data/Ability.asset", references[0].AssetPath);
            Assert.AreEqual("guid", references[0].ReferenceKind);
            Assert.AreEqual("abc123", references[0].FormulaGuid);
            Assert.AreEqual("Assets/AddressableAssetsData/AssetGroups/Math.asset", references[1].AssetPath);
        }

        [Test]
        public void FindGuidReferences_SkipsExcludedRoots()
        {
            var documents = new[]
            {
                new FormulaReferenceTextDocument(
                    "Library/PackageCache/com.example/Generated.asset",
                    "guid: abc123"),
                new FormulaReferenceTextDocument(
                    "Assets/Assets/_Data/Ability.asset",
                    "guid: abc123"),
            };
            var options = new FormulaReferenceSearchOptions(
                new[] { "Assets", "Library" },
                new[] { "Library" });

            var references = FormulaReferenceIndexer
                .FindGuidReferences("abc123", documents, options)
                .ToArray();

            Assert.AreEqual(1, references.Length);
            Assert.AreEqual("Assets/Assets/_Data/Ability.asset", references[0].AssetPath);
        }

        [Test]
        public void FindGuidReferences_IgnoresEmptyGuid()
        {
            var documents = new[]
            {
                new FormulaReferenceTextDocument(
                    "Assets/Assets/_Data/Ability.asset",
                    "guid: abc123"),
            };
            var options = new FormulaReferenceSearchOptions(
                new[] { "Assets" },
                new[] { "Library" });

            var references = FormulaReferenceIndexer
                .FindGuidReferences(" ", documents, options)
                .ToArray();

            Assert.AreEqual(0, references.Length);
        }

        [Test]
        public void FindGuidReferences_MatchesAllKnownGuidsInOneDocumentWithoutDuplicates()
        {
            const string firstGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string secondGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var documents = new[]
            {
                new FormulaReferenceTextDocument(
                    "Assets/Data/Combined.asset",
                    $"{firstGuid}\n{secondGuid}\n{firstGuid}"),
            };

            var references = FormulaReferenceIndexer.FindGuidReferences(
                    new[] { firstGuid, secondGuid },
                    documents,
                    new FormulaReferenceSearchOptions(new[] { "Assets" }, null))
                .ToArray();

            Assert.AreEqual(2, references.Length);
            CollectionAssert.AreEquivalent(
                new[] { firstGuid, secondGuid },
                references.Select(reference => reference.FormulaGuid).ToArray());
        }
    }
}
