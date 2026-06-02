using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaEditorPreviewTests
    {
        [Test]
        public void TryEvaluate_WithProfileProvider_UsesProfilePreviewValue()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("preview.value")),
                });

                var profile = new FormulaEditorProfile(
                    "test",
                    "测试公式",
                    string.Empty,
                    string.Empty,
                    "测试公式",
                    new[]
                    {
                        new FormulaProviderDescriptor(
                            "preview.value",
                            "预览值",
                            "测试",
                            "预览 provider。",
                            4f,
                            Array.Empty<FormulaParameterDescriptor>()),
                    },
                    Array.Empty<FormulaPreviewInputDescriptor>());

                var success = FormulaEditorPreview.TryEvaluate(
                    formula,
                    profile,
                    FormulaDictionaryEvaluationContext.Empty,
                    out var value,
                    out var report);

                Assert.IsTrue(success, string.Join("\n", report.Diagnostics));
                Assert.AreEqual(5f, value);
                Assert.AreEqual(0, report.Diagnostics.Count);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        private static void SetFormulaAsset(FormulaAsset formula, float initialValue, FormulaStep[] steps)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, initialValue);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(formula, new System.Collections.Generic.List<FormulaStep>(steps));
        }
    }
}
