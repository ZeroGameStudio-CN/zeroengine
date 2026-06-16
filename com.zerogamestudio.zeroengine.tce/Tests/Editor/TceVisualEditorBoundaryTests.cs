using System.IO;
using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceVisualEditorBoundaryTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce";

        [Test]
        public void RuntimeAsmdef_HasNoEditorOrProjectReferences()
        {
            string asmdef = File.ReadAllText($"{PackagePath}/Runtime/ZeroEngine.TCE.asmdef");

            StringAssert.DoesNotContain("UnityEditor", asmdef);
            StringAssert.DoesNotContain("POB", asmdef);
            StringAssert.DoesNotContain("P5", asmdef);
            StringAssert.DoesNotContain("ZeroEngine.Combat", asmdef);
        }

        [Test]
        public void EditorAsmdef_DoesNotReferenceProjectAssemblies()
        {
            string asmdef = File.ReadAllText($"{PackagePath}/Editor/ZeroEngine.TCE.Editor.asmdef");

            StringAssert.DoesNotContain("POB", asmdef);
            StringAssert.DoesNotContain("P5", asmdef);
            StringAssert.DoesNotContain("ZeroEngine.Combat", asmdef);
        }

        [Test]
        public void VisualEditorSources_DoNotReferenceProjectOrModSystemAssemblies()
        {
            string[] files =
            {
                $"{PackagePath}/Editor/VisualEditor/TceEditorWindow.cs",
                $"{PackagePath}/Editor/VisualEditor/TceValidationPanel.cs",
                $"{PackagePath}/Editor/VisualEditor/TcePreviewRunner.cs",
                $"{PackagePath}/Editor/VisualEditor/TceComponentPalette.cs"
            };

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain("ModSystem", source, file);
                StringAssert.DoesNotContain("ZeroEngine.Gameplay", source, file);
                StringAssert.DoesNotContain("ZeroEngine.Combat", source, file);
                StringAssert.DoesNotContain("POB", source, file);
                StringAssert.DoesNotContain("P5", source, file);
            }
        }

        [Test]
        public void ValidationPanelFocus_TriggerPath_ReturnsTriggerSelection()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "triggers[2].Duration", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out TceGraphLane lane, out int index, out string fieldPath);

            Assert.IsTrue(focused);
            Assert.AreEqual(TceGraphLane.Trigger, lane);
            Assert.AreEqual(2, index);
            Assert.AreEqual("Duration", fieldPath);
        }

        [Test]
        public void ValidationPanelFocus_GraphLevelPath_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.MissingTrigger, "triggers", "missing");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }

        [Test]
        public void ValidationPanelFocus_NegativeIndex_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "conditions[-1].Duration", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }

        [Test]
        public void ValidationPanelFocus_InvalidSuffix_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "effects[0]Message", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }
    }
}
