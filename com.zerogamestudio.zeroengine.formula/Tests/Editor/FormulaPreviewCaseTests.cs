using System;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaPreviewCaseTests
    {
        [Test]
        public void ValueSet_CopiesValuesAndFindsByKey()
        {
            var source = new[]
            {
                new FormulaPreviewValue("coin", 100f),
                new FormulaPreviewValue("levelRoomCount", 3f),
            }.ToList();

            var valueSet = new FormulaPreviewValueSet(source);
            source.Clear();

            Assert.AreEqual(2, valueSet.Values.Count);
            Assert.IsTrue(valueSet.TryGetValue("coin", out var coin));
            Assert.AreEqual(100f, coin);
            Assert.IsFalse(valueSet.TryGetValue("missing", out _));
        }

        [Test]
        public void PreviewCase_NormalizesMetadata()
        {
            var previewCase = new FormulaPreviewCase(
                "high-coin",
                "高金币",
                new FormulaPreviewValueSet(new[]
                {
                    new FormulaPreviewValue("coin", 250f),
                }),
                "高金币样例");

            Assert.AreEqual("high-coin", previewCase.Id);
            Assert.AreEqual("高金币", previewCase.DisplayName);
            Assert.AreEqual("高金币样例", previewCase.Description);
            Assert.IsTrue(previewCase.Values.TryGetValue("coin", out var coin));
            Assert.AreEqual(250f, coin);
        }

        [Test]
        public void RuntimeSnapshot_StoresProfileSourceTimestampAndValues()
        {
            var snapshot = new FormulaRuntimeSnapshot(
                "pob",
                "当前玩家",
                "2026-06-03T10:30:00Z",
                new FormulaPreviewValueSet(new[]
                {
                    new FormulaPreviewValue("totalRoomCount", 12f),
                    new FormulaPreviewValue("coin", 75f),
                }));

            Assert.AreEqual("pob", snapshot.ProfileId);
            Assert.AreEqual("当前玩家", snapshot.SourceLabel);
            Assert.AreEqual("2026-06-03T10:30:00Z", snapshot.CapturedAtUtc);
            Assert.IsTrue(snapshot.Values.TryGetValue("totalRoomCount", out var rooms));
            Assert.AreEqual(12f, rooms);
        }

        [Test]
        public void Constructors_NormalizeNulls()
        {
            var value = new FormulaPreviewValue(null, 1f);
            var previewCase = new FormulaPreviewCase(null, null, null, null);
            var snapshot = new FormulaRuntimeSnapshot(null, null, null, null);

            Assert.AreEqual(string.Empty, value.Key);
            Assert.AreEqual(string.Empty, previewCase.Id);
            Assert.AreEqual(string.Empty, previewCase.DisplayName);
            Assert.AreEqual(0, previewCase.Values.Values.Count);
            Assert.AreEqual(string.Empty, snapshot.ProfileId);
            Assert.AreEqual(string.Empty, snapshot.SourceLabel);
            Assert.AreEqual(string.Empty, snapshot.CapturedAtUtc);
            Assert.AreEqual(0, snapshot.Values.Values.Count);
        }
    }
}
