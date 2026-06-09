using System.IO;
using NUnit.Framework;

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
    }
}
