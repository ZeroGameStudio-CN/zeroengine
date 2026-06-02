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

            StringAssert.Contains("FormulaEditorLabels.Formula", source);
            StringAssert.Contains("FormulaEditorLabels.Evaluate", source);
            StringAssert.Contains("FormulaEditorLabels.Diagnostics", source);
            StringAssert.Contains("FormulaEditorLabels.StepTrace", source);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Succeeded\"", source);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Result\"", source);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Diagnostics\"", source);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(\"Steps\"", source);
            StringAssert.DoesNotContain("GUILayout.Button(\"Evaluate\"", source);
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
