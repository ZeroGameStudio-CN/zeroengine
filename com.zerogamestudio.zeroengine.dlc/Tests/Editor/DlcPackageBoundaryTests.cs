using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ZeroEngine.Tests.Dlc
{
    [TestFixture]
    public sealed class DlcPackageBoundaryTests
    {
        private static string PackageRoot
        {
            get
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(DlcPackageBoundaryTests).Assembly);
                Assert.That(packageInfo, Is.Not.Null);
                Assert.That(packageInfo.name, Is.EqualTo("com.zerogamestudio.zeroengine.dlc"));
                return packageInfo.assetPath;
            }
        }

        [Test]
        public void RuntimeSource_DoesNotReferenceProjectOrPlatformSpecificApis()
        {
            var runtimeFiles = Directory.GetFiles(Path.Combine(PackageRoot, "Runtime"), "*.cs", SearchOption.AllDirectories);
            Assert.That(runtimeFiles, Is.Not.Empty);

            var source = string.Join("\n", runtimeFiles.Select(File.ReadAllText));
            Assert.That(source, Does.Not.Contain("Steamworks"));
            Assert.That(source, Does.Not.Contain("ISteamApps"));
            Assert.That(source, Does.Not.Contain("UnityEditor"));
            Assert.That(source, Does.Not.Contain("UnityEngine.AddressableAssets"));
            Assert.That(source, Does.Not.Contain("ZGS."));
            Assert.That(source, Does.Not.Contain("POB"));
        }

        [Test]
        public void PackageManifest_HasNoStorefrontOrAddressablesDependency()
        {
            var manifest = File.ReadAllText(Path.Combine(PackageRoot, "package.json"));
            Assert.That(manifest, Does.Contain("\"name\": \"com.zerogamestudio.zeroengine.dlc\""));
            Assert.That(manifest, Does.Not.Contain("Steamworks"));
            Assert.That(manifest, Does.Not.Contain("ISteamApps"));
            Assert.That(manifest, Does.Not.Contain("UnityEngine.AddressableAssets"));
            Assert.That(manifest, Does.Not.Contain("com.unity.addressables"));
            Assert.That(manifest, Does.Not.Contain("ZGS."));
            Assert.That(manifest, Does.Not.Contain("POB"));
        }
    }
}
