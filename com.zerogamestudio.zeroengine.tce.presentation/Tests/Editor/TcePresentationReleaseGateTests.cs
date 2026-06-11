using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationReleaseGateTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce.presentation";

        [Test]
        public void PackageManifest_DeclaresGraduatedVersionAndOnlyTceDependency()
        {
            string manifest = File.ReadAllText($"{PackagePath}/package.json");
            PackageInfo packageInfo = PackageInfo.FindForAssetPath($"{PackagePath}/package.json");

            Assert.IsNotNull(packageInfo);
            Assert.AreEqual("0.2.0", packageInfo.version);
            Assert.AreEqual(1, packageInfo.dependencies.Length);
            Assert.AreEqual("com.zerogamestudio.zeroengine.tce", packageInfo.dependencies[0].name);
            Assert.AreEqual("0.1.0", packageInfo.dependencies[0].version);
            StringAssert.DoesNotContain("POB", manifest);
            StringAssert.DoesNotContain("DOTween", manifest);
            StringAssert.DoesNotContain("Sirenix", manifest);
            StringAssert.DoesNotContain("Spine", manifest);
        }

        [Test]
        public void ReleaseGateRunbook_ListsRequiredGraduationChecks()
        {
            string document = File.ReadAllText($"{PackagePath}/Documentation~/release-gates.md");

            StringAssert.Contains("ZeroEngine.TCE.Presentation.Tests.Editor", document);
            StringAssert.Contains("ZeroEngine.TCE.Tests.Editor", document);
            StringAssert.Contains("total > 0", document);
            StringAssert.Contains("failed = 0", document);
            StringAssert.Contains("rg -n", document);
            StringAssert.Contains("cm status --short", document);
            StringAssert.Contains("manage_packages get_package_info com.zerogamestudio.zeroengine.tce.presentation", document);
            StringAssert.Contains("release evidence", document);
        }

        [Test]
        public void ApiCompatibilityDocument_ListsStableSurfaceAndBreakingChangePolicy()
        {
            string document = File.ReadAllText($"{PackagePath}/Documentation~/api-compatibility.md");

            StringAssert.Contains("ITcePresentationSource", document);
            StringAssert.Contains("ITcePresentationPlayer", document);
            StringAssert.Contains("TcePresentationHandle", document);
            StringAssert.Contains("TceMeshSnapshot", document);
            StringAssert.Contains("TceSpriteSnapshot", document);
            StringAssert.Contains("TceSpriteLayerSnapshot", document);
            StringAssert.Contains("StaticSnapshot = 0", document);
            StringAssert.Contains("MeshSnapshot = 1", document);
            StringAssert.Contains("LayeredSpriteSnapshot = 2", document);
            StringAssert.Contains("SoulGhost = 3", document);
            StringAssert.Contains("zeroengine.tce.presentation.effect.spawn_snapshot", document);
            StringAssert.Contains("zeroengine.tce.presentation.effect.spawn_soul_ghost", document);
            StringAssert.Contains("major", document);
            StringAssert.Contains("migration", document);
        }

        [Test]
        public void Readme_DefinesVisualOnlyBoundaryWithoutPobGameplay()
        {
            string document = File.ReadAllText($"{PackagePath}/README.md");

            StringAssert.Contains("visual-only", document);
            StringAssert.Contains("POB adapters", document);
            StringAssert.DoesNotContain("DamageInfo", document);
            StringAssert.DoesNotContain("Projectile", document);
            StringAssert.DoesNotContain("Weapon", document);
        }
    }
}
