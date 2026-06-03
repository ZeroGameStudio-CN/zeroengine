using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaCatalogTests
    {
        [Test]
        public void Entry_NormalizesMetadata()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                var entry = new FormulaCatalogEntry(
                    formula,
                    "guid-1",
                    "金币成长",
                    "根据金币计算成长倍率",
                    "系统策划",
                    "倍率",
                    new[] { "经济", "成长" },
                    FormulaCatalogStatus.Active,
                    new FormulaResultRange(true, 0f, 10f),
                    "用于测试");

                Assert.AreSame(formula, entry.Formula);
                Assert.AreEqual("guid-1", entry.FormulaGuid);
                Assert.AreEqual("金币成长", entry.Title);
                Assert.AreEqual("根据金币计算成长倍率", entry.Purpose);
                Assert.AreEqual("系统策划", entry.Owner);
                Assert.AreEqual("倍率", entry.Unit);
                Assert.AreEqual(2, entry.Tags.Count);
                Assert.AreEqual("经济", entry.Tags[0]);
                Assert.AreEqual(FormulaCatalogStatus.Active, entry.Status);
                Assert.IsTrue(entry.ExpectedRange.Enabled);
                Assert.AreEqual("用于测试", entry.Notes);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void Lookup_FindsEntryByFormulaObjectOrGuid()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            var other = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                var lookup = new FormulaCatalogLookup(new[]
                {
                    new FormulaCatalogEntry(
                        formula,
                        "guid-1",
                        "金币成长",
                        "根据金币计算成长倍率",
                        "系统策划",
                        "倍率",
                        Enumerable.Empty<string>(),
                        FormulaCatalogStatus.Active,
                        FormulaResultRange.None,
                        string.Empty),
                });

                Assert.IsTrue(lookup.TryGetEntry(formula, string.Empty, out var byFormula));
                Assert.AreEqual("金币成长", byFormula.Title);
                Assert.IsTrue(lookup.TryGetEntry(other, "guid-1", out var byGuid));
                Assert.AreEqual("金币成长", byGuid.Title);
                Assert.IsFalse(lookup.TryGetEntry(other, "missing", out _));
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
                if (other != null)
                    UnityObject.DestroyImmediate(other);
            }
        }
    }
}
