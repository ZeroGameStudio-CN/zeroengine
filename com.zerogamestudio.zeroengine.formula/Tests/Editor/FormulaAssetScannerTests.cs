using System;
using System.Collections.Generic;
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

        private static void SetFormulaAsset(FormulaAsset formula, float initialValue, IEnumerable<FormulaStep> steps)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, initialValue);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(formula, new List<FormulaStep>(steps));
        }
    }
}
