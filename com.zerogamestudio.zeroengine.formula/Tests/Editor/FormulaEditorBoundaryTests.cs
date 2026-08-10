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
            StringAssert.Contains("FormulaEditorLabels.Evaluate", source);
            StringAssert.Contains("FormulaEditorLabels.EvaluateTooltip", source);
            StringAssert.Contains("FormulaEditorLabels.StudioTooltip", source);
            StringAssert.Contains("FormulaEditorGUILayout.DrawReport", source);
            StringAssert.Contains("FormulaEditorLabels.Diagnostics", guiSource);
            StringAssert.Contains("FormulaEditorLabels.StepTrace", guiSource);
            StringAssert.Contains("FormulaEditorLabels.FilterTooltip", guiSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Succeeded\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Result\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Diagnostics\"", combinedSource);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Steps\"", combinedSource);
            StringAssert.DoesNotContain("GUILayout.Button(\"Evaluate\"", combinedSource);
        }

        [Test]
        public void FormulaStudio_PreservesRoutesButUsesOneNormalWindowHost()
        {
            var workbenchSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaWorkbenchWindow.cs"));
            var catalogSource = File.ReadAllText(Path.Combine(GetPackageRoot(), "Editor", "FormulaCatalogWindow.cs"));

            StringAssert.Contains("FormulaStudioPage.Workbench", workbenchSource);
            StringAssert.Contains("FormulaStudioPage.Catalog", workbenchSource);
            StringAssert.Contains("FormulaCatalogPane", workbenchSource);
            StringAssert.Contains("FormulaWorkbenchWindow.OpenCatalogWithProfile(profile);", catalogSource);
            StringAssert.DoesNotContain("GetWindow<FormulaCatalogWindow>", catalogSource);
            StringAssert.Contains("ZeroEngine/Formula/Formula Catalog", catalogSource);
            StringAssert.Contains("ZeroEngine/Formula/Formula Workbench", workbenchSource);
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
