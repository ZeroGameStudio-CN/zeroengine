using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.EditorTools.Tests
{
    [TestFixture]
    public sealed class EditorToolsPackageSourceTests
    {
        [Test]
        public void EditorToolsPackage_DoesNotReferenceP5Assemblies()
        {
            var packageRoot = FindPackageRoot();
            var source = string.Join("\n", Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

            var forbiddenReferences = new[]
            {
                "ZGS.Character",
                "ZGS.Battle",
                "ZGS.UI",
                "ZGS.Data",
                "SkillData",
                "BattleUnit"
            };

            foreach (var forbiddenReference in forbiddenReferences)
            {
                Assert.That(source, Does.Not.Contain(forbiddenReference), $"EditorTools package must not reference P5 type or assembly {forbiddenReference}.");
            }
        }

        [Test]
        public void EditorToolWindow_UsesChineseLabelsAndTooltips()
        {
            var source = File.ReadAllText(Path.Combine(FindPackageRoot(), "Editor", "EditorToolWindow.cs"));

            Assert.That(source, Does.Contain("new GUIContent"));
            Assert.That(source, Does.Contain("\"项目\""));
            Assert.That(source, Does.Contain("\"生成器\""));
            Assert.That(source, Does.Contain("\"校验\""));
            Assert.That(source, Does.Contain("\"命令\""));
            Assert.That(source, Does.Contain("\"测试运行器\""));
            Assert.That(source, Does.Contain("\"没有已注册的编辑器工具项目。\""));
            Assert.That(source, Does.Not.Contain("\"Project\""));
            Assert.That(source, Does.Not.Contain("\"Generators\""));
            Assert.That(source, Does.Not.Contain("\"Validation\""));
            Assert.That(source, Does.Not.Contain("\"Commands\""));
            Assert.That(source, Does.Not.Contain("\"Test Runner\""));
            Assert.That(source, Does.Not.Contain("\"No editor tool project profile is registered.\""));
            Assert.That(source, Does.Not.Contain("[MenuItem("), "Editor Tools is embedded in the unified Dashboard and must not restore a standalone top-level menu.");
        }

        [Test]
        public void EditorToolWindow_ImplementsWorkspaceEmbeddingAndState()
        {
            var interfaces = typeof(EditorToolWindow).GetInterfaces();

            Assert.That(interfaces.Any(type => type.FullName == "ZeroEngine.EditorUI.IEditorWorkspaceEmbeddedView"), Is.True);
            Assert.That(interfaces.Any(type => type.FullName == "ZeroEngine.EditorUI.IEditorWorkspaceStatefulView"), Is.True);
        }

        private static string FindPackageRoot()
        {
            var directory = new DirectoryInfo(Application.dataPath);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Packages", "com.zerogamestudio.zeroengine.editortools");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not locate com.zerogamestudio.zeroengine.editortools package root.");
            return null;
        }
    }
}
