using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
            var folderName = "__FormulaAssetScannerTests_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            var assetPath = folder + "/InvalidFormula.asset";

            try
            {
                var folderGuid = AssetDatabase.CreateFolder("Assets", folderName);
                Assert.IsFalse(string.IsNullOrEmpty(folderGuid));

                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Nested(null)),
                });
                AssetDatabase.CreateAsset(formula, assetPath);
                AssetDatabase.SaveAssets();

                var report = FormulaAssetScanner.ScanAll(folder);

                Assert.AreEqual(1, report.ErrorCount);
                Assert.IsTrue(report.HasErrors);
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                else if (formula != null)
                    UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void Scan_WithProfileProvider_UsesPreviewProvider()
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            var folderName = "__FormulaAssetScannerTests_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            var assetPath = folder + "/ProfileFormula.asset";

            try
            {
                var folderGuid = AssetDatabase.CreateFolder("Assets", folderName);
                Assert.IsFalse(string.IsNullOrEmpty(folderGuid));

                SetFormulaAsset(formula, 1f, new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("test.value")),
                });
                AssetDatabase.CreateAsset(formula, assetPath);
                AssetDatabase.SaveAssets();

                var profile = new FormulaEditorProfile(
                    "test",
                    "测试公式",
                    folder,
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

                var report = FormulaAssetScanner.Scan(folder, profile);

                Assert.AreEqual(1, report.AssetCount);
                Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues.Select(i => i.ToString())));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                else if (formula != null)
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
