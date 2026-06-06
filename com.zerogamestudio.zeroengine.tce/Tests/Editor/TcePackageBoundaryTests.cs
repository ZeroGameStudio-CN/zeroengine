using System.IO;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TcePackageBoundaryTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce";

        [Test]
        public void RuntimeAsmdef_HasNoProjectOrHeavyThirdPartyReferences()
        {
            string asmdef = File.ReadAllText($"{PackagePath}/Runtime/ZeroEngine.TCE.asmdef");

            StringAssert.DoesNotContain("POB", asmdef);
            StringAssert.DoesNotContain("Sirenix", asmdef);
            StringAssert.DoesNotContain("DOTween", asmdef);
            StringAssert.DoesNotContain("Unity.InputSystem", asmdef);
            StringAssert.DoesNotContain("MoreMountains", asmdef);
            StringAssert.DoesNotContain("PixelCrushers", asmdef);
        }

        [Test]
        public void RuntimeSource_HasNoPobNamespacesOrBusinessTypes()
        {
            foreach (string file in Directory.GetFiles($"{PackagePath}/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);

                StringAssert.DoesNotContain("using POB", source, file);
                StringAssert.DoesNotContain("namespace POB", source, file);
                StringAssert.DoesNotContain("POB.", source, file);
                StringAssert.DoesNotContain("AbilityDataSO", source, file);
                StringAssert.DoesNotContain("CoreSystem", source, file);
            }
        }
    }
}
