using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaPreviewRunnerTests
    {
        [Test]
        public void EvaluateCases_ReturnsOneResultPerCase()
        {
            var formula = CreateCoinFormula();

            try
            {
                var report = FormulaPreviewRunner.EvaluateCases(
                    formula,
                    CreateCoinProfile(),
                    new[]
                    {
                        CreateCase("low", 10f),
                        CreateCase("high", 25f),
                    });

                Assert.AreEqual(2, report.Results.Count);
                Assert.AreEqual("low", report.Results[0].Case.Id);
                Assert.AreEqual("high", report.Results[1].Case.Id);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void EvaluateCases_UsesPreviewCaseOverrides()
        {
            var formula = CreateCoinFormula();

            try
            {
                var report = FormulaPreviewRunner.EvaluateCases(
                    formula,
                    CreateCoinProfile(),
                    new[]
                    {
                        CreateCase("low", 10f),
                        CreateCase("high", 25f),
                    });

                Assert.AreEqual(10f, report.Results[0].Value);
                Assert.AreEqual(25f, report.Results[1].Value);
                Assert.IsTrue(report.Results.All(result => result.Succeeded));
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void CreateCaseFromSnapshot_CopiesSnapshotValues()
        {
            var snapshot = new FormulaRuntimeSnapshot(
                "pob",
                "当前玩家",
                "2026-06-03T10:30:00Z",
                new FormulaPreviewValueSet(new[]
                {
                    new FormulaPreviewValue("coin", 75f),
                }));

            var previewCase = FormulaPreviewRunner.CreateCaseFromSnapshot(
                "runtime",
                "运行时",
                snapshot);

            Assert.AreEqual("runtime", previewCase.Id);
            Assert.AreEqual("运行时", previewCase.DisplayName);
            Assert.IsTrue(previewCase.Values.TryGetValue("coin", out var coin));
            Assert.AreEqual(75f, coin);
            StringAssert.Contains("当前玩家", previewCase.Description);
        }

        private static FormulaAsset CreateCoinFormula()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, 0f);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(
                formula,
                new List<FormulaStep>
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("resource.coin")),
                });
            return formula;
        }

        private static FormulaEditorProfile CreateCoinProfile()
        {
            return new FormulaEditorProfile(
                "test",
                "测试公式",
                string.Empty,
                string.Empty,
                "测试公式",
                new[]
                {
                    new FormulaProviderDescriptor(
                        "resource.coin",
                        "金币",
                        "资源",
                        "金币预览。",
                        0f,
                        Array.Empty<FormulaParameterDescriptor>(),
                        "coin"),
                },
                new[]
                {
                    new FormulaPreviewInputDescriptor(
                        "coin",
                        "金币",
                        FormulaPreviewInputKind.Int,
                        0f,
                        "金币"),
                });
        }

        private static FormulaPreviewCase CreateCase(string id, float coin)
        {
            return new FormulaPreviewCase(
                id,
                id,
                new FormulaPreviewValueSet(new[]
                {
                    new FormulaPreviewValue("coin", coin),
                }),
                string.Empty);
        }
    }
}
