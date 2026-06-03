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

        [Test]
        public void EvaluateCases_WithProviderParameterPreviewOverride_UsesScopedSnapshotValue()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            var source = FormulaValueSource.Provider("stat.current", FormulaParameter.Int("statType", 5));

            try
            {
                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, source),
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
                            "stat.current",
                            "当前属性",
                            "属性",
                            "属性预览 provider。",
                            10f,
                            new[]
                            {
                                new FormulaParameterDescriptor(
                                    "statType",
                                    "属性类型",
                                    FormulaEditorParameterKind.Enum,
                                    true,
                                    "属性类型。"),
                            }),
                    },
                    Array.Empty<FormulaPreviewInputDescriptor>());

                var previewCase = new FormulaPreviewCase(
                    "runtime",
                    "运行时",
                    new FormulaPreviewValueSet(new[]
                    {
                        new FormulaPreviewValue("provider:stat.current|statType=i:5", 42f),
                    }),
                    string.Empty);

                var report = FormulaPreviewRunner.EvaluateCases(formula, profile, new[] { previewCase });

                Assert.AreEqual(1, report.Results.Count);
                Assert.IsTrue(report.Results[0].Succeeded, string.Join("\n", report.Results[0].Report.Diagnostics));
                Assert.AreEqual(43f, report.Results[0].Value);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void PreviewState_ResetToDefaults_RestoresProfileDefaultValues()
        {
            var profile = new FormulaEditorProfile(
                "test",
                "测试公式",
                string.Empty,
                string.Empty,
                "测试公式",
                Array.Empty<FormulaProviderDescriptor>(),
                new[]
                {
                    new FormulaPreviewInputDescriptor(
                        "level",
                        "等级",
                        FormulaPreviewInputKind.Int,
                        5f,
                        "玩家等级"),
                    new FormulaPreviewInputDescriptor(
                        "ratio",
                        "倍率",
                        FormulaPreviewInputKind.Float,
                        1.5f,
                        "倍率"),
                });
            var state = new FormulaEditorPreviewState();
            state.SetValue("level", 99f);
            state.SetValue("ratio", 8f);

            state.ResetToDefaults(profile);
            var values = state.ToValueSet(profile);

            Assert.AreEqual(5f, values.TryGetValue("level", out var level) ? level : -1f);
            Assert.AreEqual(1.5f, values.TryGetValue("ratio", out var ratio) ? ratio : -1f);
        }

        private static void SetFormulaAsset(FormulaAsset formula, float initialValue, FormulaStep[] steps)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, initialValue);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(formula, new System.Collections.Generic.List<FormulaStep>(steps));
        }
    }
}
