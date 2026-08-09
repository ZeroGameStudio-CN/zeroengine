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
    public sealed class FormulaCurvePreviewTests
    {
        [Test]
        public void BuildCurve_EvaluatesInclusiveSamples()
        {
            var formula = CreateCoinFormula();

            try
            {
                var curve = FormulaCurvePreview.BuildCurve(
                    formula,
                    CreateCoinProfile(),
                    new FormulaPreviewValueSet(null),
                    "coin",
                    0f,
                    20f,
                    3);

                Assert.That(curve.Points.Select(point => point.Input).ToArray(), Is.EqualTo(new[] { 0f, 10f, 20f }));
                Assert.That(curve.Points.Select(point => point.Result).ToArray(), Is.EqualTo(new[] { 0f, 10f, 20f }));
                Assert.That(curve.Succeeded, Is.True);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void BuildCurve_ClampsSampleCountToTwo()
        {
            var formula = CreateCoinFormula();

            try
            {
                var curve = FormulaCurvePreview.BuildCurve(
                    formula,
                    CreateCoinProfile(),
                    new FormulaPreviewValueSet(null),
                    "coin",
                    5f,
                    15f,
                    1);

                Assert.That(curve.Points.Count, Is.EqualTo(2));
                Assert.That(curve.Points[0].Input, Is.EqualTo(5f));
                Assert.That(curve.Points[1].Input, Is.EqualTo(15f));
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void BuildCurve_NullFormulaReturnsFailedPoints()
        {
            var curve = FormulaCurvePreview.BuildCurve(
                null,
                CreateCoinProfile(),
                new FormulaPreviewValueSet(null),
                "coin",
                0f,
                10f,
                2);

            Assert.That(curve.Succeeded, Is.False);
            Assert.That(curve.Points.Count, Is.EqualTo(2));
            Assert.That(curve.Points.All(point => point.Succeeded), Is.False);
            Assert.That(curve.Points.All(point => point.Report != null), Is.True);
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
