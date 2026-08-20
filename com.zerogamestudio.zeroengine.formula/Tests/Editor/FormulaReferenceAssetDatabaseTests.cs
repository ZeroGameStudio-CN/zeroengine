using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaReferenceAssetDatabaseTests
    {
        [Test]
        public void IsSupportedTextAssetPath_AllowsUnityTextAssets()
        {
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Data/Formula.asset"));
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Data/Scene.unity"));
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/AddressableAssetsData/Groups.json"));
        }

        [Test]
        public void IsSupportedTextAssetPath_RejectsBinaryAssets()
        {
            Assert.IsFalse(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Art/Icon.png"));
            Assert.IsFalse(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Audio/Hit.wav"));
            Assert.IsFalse(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Data/Formula.asset.meta"));
        }

        [Test]
        public void ReferenceIndexBuild_ReadsEachChangedDocumentOnceAndFindsAllKnownGuids()
        {
            const string firstGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string secondGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var snapshots = new[]
            {
                new FormulaReferenceFileSnapshot("Assets/Data/One.asset", "one", 10, 100),
                new FormulaReferenceFileSnapshot("Assets/Data/Two.asset", "two", 20, 200),
            };
            var textByPath = new Dictionary<string, string>
            {
                ["one"] = $"first {firstGuid} second {secondGuid}",
                ["two"] = $"duplicate {firstGuid} {firstGuid}",
            };
            var readCount = 0;

            var index = FormulaReferenceIndexCache.Build(
                7,
                "profile",
                snapshots,
                new[] { firstGuid, secondGuid },
                null,
                false,
                path =>
                {
                    readCount++;
                    return textByPath[path];
                });

            Assert.AreEqual(2, readCount);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    $"Assets/Data/One.asset:{firstGuid}",
                    $"Assets/Data/One.asset:{secondGuid}",
                    $"Assets/Data/Two.asset:{firstGuid}",
                },
                index.CreateReferences().Select(reference => $"{reference.AssetPath}:{reference.FormulaGuid}").ToArray());
        }

        [Test]
        public void ReferenceIndexBuild_ReusesUnchangedDocumentsAndDropsDeletedOnes()
        {
            const string guid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var initial = FormulaReferenceIndexCache.Build(
                1,
                "profile",
                new[]
                {
                    new FormulaReferenceFileSnapshot("Assets/Data/Keep.asset", "keep", 10, 100),
                    new FormulaReferenceFileSnapshot("Assets/Data/Delete.asset", "delete", 20, 200),
                },
                new[] { guid },
                null,
                false,
                _ => guid);

            var readCount = 0;
            var rebuilt = FormulaReferenceIndexCache.Build(
                2,
                "profile",
                new[] { new FormulaReferenceFileSnapshot("Assets/Data/Keep.asset", "keep", 10, 100) },
                new[] { guid },
                initial,
                false,
                _ =>
                {
                    readCount++;
                    return string.Empty;
                });

            Assert.AreEqual(0, readCount);
            Assert.AreEqual(1, rebuilt.Documents.Count);
            Assert.IsTrue(rebuilt.Documents.ContainsKey("Assets/Data/Keep.asset"));
            Assert.IsFalse(rebuilt.Documents.ContainsKey("Assets/Data/Delete.asset"));
            Assert.AreEqual(1, rebuilt.CreateReferences().Count);
        }
    }
}
