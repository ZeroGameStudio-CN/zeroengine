using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaPreviewCaseAssetTests
    {
        [Test]
        public void CreatePreviewCase_RoundTripsSerializedValues()
        {
            var asset = ScriptableObject.CreateInstance<FormulaPreviewCaseAsset>();

            try
            {
                asset.Initialize(
                    "coin-high",
                    "高金币",
                    "用于金币收益公式回归。",
                    new[]
                    {
                        new FormulaPreviewValue("coin", 250f),
                        new FormulaPreviewValue("levelRoomCount", 4f),
                    });

                var previewCase = asset.CreatePreviewCase();

                Assert.That(previewCase.Id, Is.EqualTo("coin-high"));
                Assert.That(previewCase.DisplayName, Is.EqualTo("高金币"));
                Assert.That(previewCase.Description, Is.EqualTo("用于金币收益公式回归。"));
                Assert.That(previewCase.Values.TryGetValue("coin", out var coin), Is.True);
                Assert.That(coin, Is.EqualTo(250f));
                Assert.That(previewCase.Values.TryGetValue("levelRoomCount", out var rooms), Is.True);
                Assert.That(rooms, Is.EqualTo(4f));
            }
            finally
            {
                if (asset != null)
                    UnityObject.DestroyImmediate(asset);
            }
        }
    }
}
