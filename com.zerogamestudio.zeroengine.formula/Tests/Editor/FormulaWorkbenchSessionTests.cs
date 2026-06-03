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
    public sealed class FormulaWorkbenchSessionTests
    {
        [Test]
        public void EvaluateBatch_IncludesCurrentInputAndPreviewCaseAssets()
        {
            var formula = CreateCoinFormula();
            var asset = ScriptableObject.CreateInstance<FormulaPreviewCaseAsset>();

            try
            {
                asset.Initialize(
                    "asset-high",
                    "高金币",
                    string.Empty,
                    new[] { new FormulaPreviewValue("coin", 25f) });
                var session = new FormulaWorkbenchSession();
                session.AddPreviewCaseAsset(asset);

                var report = session.EvaluateBatch(
                    formula,
                    CreateCoinProfile(),
                    new FormulaPreviewValueSet(new[] { new FormulaPreviewValue("coin", 10f) }));

                Assert.That(report.Results.Count, Is.EqualTo(2));
                Assert.That(report.Results[0].Case.Id, Is.EqualTo(FormulaWorkbenchSession.CurrentPreviewCaseId));
                Assert.That(report.Results[0].Value, Is.EqualTo(10f));
                Assert.That(report.Results[1].Case.Id, Is.EqualTo("asset-high"));
                Assert.That(report.Results[1].Value, Is.EqualTo(25f));
            }
            finally
            {
                if (asset != null)
                    UnityObject.DestroyImmediate(asset);
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void ExportBatchReports_UseSharedPreviewExporter()
        {
            var formula = CreateCoinFormula();

            try
            {
                var session = new FormulaWorkbenchSession();
                var report = session.EvaluateBatch(
                    formula,
                    CreateCoinProfile(),
                    new FormulaPreviewValueSet(new[] { new FormulaPreviewValue("coin", 10f) }));

                var json = session.ExportBatchJson(report);
                var markdown = session.ExportBatchMarkdown(report);

                StringAssert.Contains(FormulaWorkbenchSession.CurrentPreviewCaseId, json);
                StringAssert.Contains("\"result\":10", json);
                StringAssert.Contains(FormulaWorkbenchSession.CurrentPreviewCaseId, markdown);
                StringAssert.Contains("Result 10", markdown);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void BuildCurve_UsesSessionCurveSettings()
        {
            var formula = CreateCoinFormula();

            try
            {
                var session = new FormulaWorkbenchSession();
                session.SetCurve("coin", 0f, 20f, 3);

                var curve = session.BuildCurve(
                    formula,
                    CreateCoinProfile(),
                    new FormulaPreviewValueSet(null));

                Assert.That(curve.InputKey, Is.EqualTo("coin"));
                Assert.That(curve.Points.Select(point => point.Result).ToArray(), Is.EqualTo(new[] { 0f, 10f, 20f }));
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
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
    }
}
