using System.IO;
using NUnit.Framework;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationPackageBoundaryTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce.presentation";

        [Test]
        public void RuntimeAsmdef_ReferencesTceOnly()
        {
            string asmdef = File.ReadAllText($"{PackagePath}/Runtime/ZeroEngine.TCE.Presentation.asmdef");

            StringAssert.Contains("\"ZeroEngine.TCE\"", asmdef);
            StringAssert.DoesNotContain("POB", asmdef);
            StringAssert.DoesNotContain("DOTween", asmdef);
            StringAssert.DoesNotContain("Sirenix", asmdef);
            StringAssert.DoesNotContain("Spine", asmdef);
            StringAssert.DoesNotContain("ZeroEngine.Combat", asmdef);
            StringAssert.DoesNotContain("ZeroEngine.Gameplay", asmdef);
        }

        [Test]
        public void RuntimeSource_HasNoProjectOrHeavyThirdPartyReferences()
        {
            foreach (string file in Directory.GetFiles($"{PackagePath}/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);

                StringAssert.DoesNotContain("using POB", source, file);
                StringAssert.DoesNotContain("namespace POB", source, file);
                StringAssert.DoesNotContain("POB.", source, file);
                StringAssert.DoesNotContain("DG.Tweening", source, file);
                StringAssert.DoesNotContain("Sirenix", source, file);
                StringAssert.DoesNotContain("Spine.Unity", source, file);
                StringAssert.DoesNotContain("DamageInfo", source, file);
                StringAssert.DoesNotContain("Projectile", source, file);
                StringAssert.DoesNotContain("Weapon", source, file);
            }
        }

        [Test]
        public void PackageManifest_DeclaresRequiredSamples()
        {
            string manifest = File.ReadAllText($"{PackagePath}/package.json");

            StringAssert.Contains("\"Minimal AfterImage\"", manifest);
            StringAssert.Contains("\"Soul Ghost\"", manifest);
            StringAssert.Contains("\"Adapter Template\"", manifest);
        }
    }
}
