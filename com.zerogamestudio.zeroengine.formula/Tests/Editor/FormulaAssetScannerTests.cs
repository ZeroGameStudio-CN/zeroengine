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
    public sealed class FormulaAssetScannerTests
    {
        [Test]
        public void ScanFormula_WithNullNestedFormula_ReportsError()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Nested(null)),
                });

                var report = FormulaAssetScanner.ScanAsset("Assets/Test/InvalidFormula.asset", formula, null);

                Assert.AreEqual(1, report.AssetCount);
                Assert.AreEqual(1, report.ErrorCount);
                Assert.IsTrue(report.HasErrors);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void Scan_WithProfileProvider_UsesPreviewProvider()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("test.value")),
                });

                var profile = new FormulaEditorProfile(
                    "test",
                    "测试公式",
                    "Assets/Test",
                    string.Empty,
                    "测试公式",
                    new[]
                    {
                        new FormulaProviderDescriptor(
                            "test.value",
                            "测试值",
                            "测试",
                            "扫描预览值",
                            2f,
                            Array.Empty<FormulaParameterDescriptor>()),
                    },
                    Array.Empty<FormulaPreviewInputDescriptor>());

                var report = FormulaAssetScanner.ScanAsset("Assets/Test/ProfileFormula.asset", formula, profile);

                Assert.AreEqual(1, report.AssetCount);
                Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues.Select(i => i.ToString())));
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void Scan_WithTemporaryAssetName_ReportsWarning()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            formula.name = "New Math Formula";

            try
            {
                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Constant(1f)),
                });

                var profile = new FormulaEditorProfile(
                    "test",
                    "测试公式",
                    "Assets/Test",
                    string.Empty,
                    "测试公式",
                    Array.Empty<FormulaProviderDescriptor>(),
                    Array.Empty<FormulaPreviewInputDescriptor>(),
                    new FormulaAssetQualityRules(true, new[] { "New Math Formula" }));

                var report = FormulaAssetScanner.ScanAsset("Assets/Test/New Math Formula.asset", formula, profile);

                Assert.AreEqual(1, report.AssetCount);
                Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues.Select(i => i.ToString())));
                Assert.AreEqual(1, report.WarningCount);
                StringAssert.Contains("临时命名", report.Issues.Single().Message);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void Scan_WithNoSteps_ReportsWarning()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();

            try
            {
                SetFormulaAsset(formula, 1f, Array.Empty<FormulaStep>());

                var profile = new FormulaEditorProfile(
                    "test",
                    "测试公式",
                    "Assets/Test",
                    string.Empty,
                    "测试公式",
                    Array.Empty<FormulaProviderDescriptor>(),
                    Array.Empty<FormulaPreviewInputDescriptor>(),
                    new FormulaAssetQualityRules(true, Array.Empty<string>()));

                var report = FormulaAssetScanner.ScanAsset("Assets/Test/EmptyFormula.asset", formula, profile);

                Assert.AreEqual(1, report.AssetCount);
                Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues.Select(i => i.ToString())));
                Assert.AreEqual(1, report.WarningCount);
                StringAssert.Contains("没有配置步骤", report.Issues.Single().Message);
            }
            finally
            {
                if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        private static void SetFormulaAsset(FormulaAsset formula, float initialValue, IEnumerable<FormulaStep> steps)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, initialValue);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(formula, new List<FormulaStep>(steps));
        }
    }
}
