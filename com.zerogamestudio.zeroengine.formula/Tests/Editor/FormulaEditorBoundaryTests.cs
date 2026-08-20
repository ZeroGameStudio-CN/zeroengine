using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    [Category("ZeroEngine.Formula.SourceCheck")]
    public sealed class FormulaEditorBoundaryTests
    {
        private const string PackageName = "com.zerogamestudio.zeroengine.formula";

        [Test]
        public void EditorSources_DoNotReferencePobOrOdin()
        {
            var editorDirectory = Path.Combine(GetPackageRoot(), "Editor");
            var files = Directory.GetFiles(editorDirectory, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(files.Length, 0);

            var source = string.Join("\n", files.Select(File.ReadAllText));

            StringAssert.DoesNotContain("using POB", source);
            StringAssert.DoesNotContain("namespace POB", source);
            StringAssert.DoesNotContain("POB.", source);
            StringAssert.DoesNotContain("Sirenix", source);
            StringAssert.DoesNotContain("Odin", source);
        }

        [Test]
        public void Workbench_UsesChineseGenericLabels()
        {
            var source = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaWorkbenchWindow.cs"));
            var guiSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaEditorGUILayout.cs"));
            var combinedSource = source + "\n" + guiSource;

            StringAssert.Contains("FormulaEditorLabels.Formula", source);
            StringAssert.Contains("FormulaEditorLabels.EvaluatePreviewCases", source);
            StringAssert.Contains("FormulaEditorLabels.EvaluatePreviewCasesTooltip", source);
            StringAssert.Contains("FormulaEditorLabels.StudioTooltip", source);
            StringAssert.Contains("DrawFormulaReport", source);
            StringAssert.Contains("EvaluateFormula", source);
            StringAssert.Contains("CollectPreviewFields", source);
            StringAssert.Contains("FormulaEditorLabels.Diagnostics", guiSource);
            StringAssert.Contains("FormulaEditorLabels.StepTrace", guiSource);
            StringAssert.Contains("FormulaEditorLabels.FilterTooltip", guiSource);
            StringAssert.DoesNotContain("DrawTrendAnalysis", source);
            StringAssert.DoesNotContain("DrawCurvePreview", source);
            StringAssert.DoesNotContain("DrawPreviewResults", source);
            StringAssert.DoesNotContain("lastBatchJson", source);
            StringAssert.DoesNotContain("lastBatchMarkdown", source);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Succeeded\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Result\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Diagnostics\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Steps\"", combinedSource);
            StringAssert.DoesNotContain("GUILayout.Button(\"Evaluate\"", combinedSource);
        }

        [Test]
        public void FormulaStudio_PreservesTypedRoutesButUsesOneNormalWindowHost()
        {
            var workbenchSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaWorkbenchWindow.cs"));
            var catalogSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaCatalogWindow.cs"));
            var providerSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaToolActionProvider.cs"));

            StringAssert.Contains("FormulaStudioPage.Workbench", workbenchSource);
            StringAssert.Contains("FormulaStudioPage.Catalog", workbenchSource);
            StringAssert.Contains("FormulaCatalogPane", workbenchSource);
            StringAssert.Contains("FormulaWorkbenchWindow.OpenCatalogWithProfile(profile);", catalogSource);
            StringAssert.DoesNotContain("GetWindow<FormulaCatalogWindow>", catalogSource);
            StringAssert.Contains("case \"formula-catalog\"", providerSource);
            StringAssert.Contains("case \"formula-workbench\"", providerSource);
            StringAssert.DoesNotContain("[MenuItem(", catalogSource);
            StringAssert.DoesNotContain("[MenuItem(", workbenchSource);
        }

        [Test]
        public void FormulaCatalog_FirstPaintDoesNotSynchronouslyScanProjectDocuments()
        {
            var source = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaCatalogPane.cs"));

            StringAssert.DoesNotContain("CollectTextDocuments", source);
            StringAssert.DoesNotContain("FormulaAssetScanner.Scan(profile)", source);
            StringAssert.DoesNotContain("FormulaEditorLabels.Refresh", source);
            StringAssert.DoesNotContain("FormulaEditorLabels.UpdateIndex", source);
            StringAssert.Contains("Task.Run", source);
            StringAssert.Contains("FormulaReferenceIndexCache", source);
            StringAssert.Contains("CollectCandidateAssetPaths(profile)", source);
            StringAssert.Contains("CollectFileSnapshots(candidatePaths)", source);
            StringAssert.DoesNotContain("CollectFileSnapshots(profile)", source);
            StringAssert.Contains("generation != observedGeneration", source);
            StringAssert.Contains("当前项目尚未接入公式适配器", source);
            StringAssert.Contains("IsConfiguredProfile", source);
            StringAssert.DoesNotContain("FormulaEditorLabels.AdvancedMaintenance", source);
            StringAssert.DoesNotContain("FormulaEditorLabels.RebuildIndex", source);
            StringAssert.Contains("RequestRebuild(profile, true)", source);
            StringAssert.Contains("EnsureMissingCatalogEntries", source);
            StringAssert.DoesNotContain("DrawMarkdown", source);
            StringAssert.DoesNotContain("FormulaEditorLabels.Ping", source);
            StringAssert.Contains("FormulaEditorLabels.FormulaCardTooltip", source);
            StringAssert.Contains("Selection.activeObject = row.Formula", source);
            StringAssert.Contains("currentEvent.clickCount >= 2", source);
            StringAssert.Contains("GUIStyle.none", source);
            StringAssert.Contains("GUI.skin.verticalScrollbar", source);
        }

        private static string GetPackageRoot()
        {
            var packageDirectory = Path.Combine("Packages", PackageName);
            if (Directory.Exists(packageDirectory))
                return packageDirectory;

            if (Directory.Exists(PackageName))
                return PackageName;

            var packageCache = Path.Combine("Library", "PackageCache");
            if (Directory.Exists(packageCache))
            {
                var cachedPackage = Directory.GetDirectories(packageCache, PackageName + "@*").FirstOrDefault();
                if (!string.IsNullOrEmpty(cachedPackage))
                    return cachedPackage;
            }

            Assert.Fail("Cannot locate ZeroEngine formula package root.");
            return null;
        }
    }
}
