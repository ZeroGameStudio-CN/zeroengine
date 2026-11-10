using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class MultiplayerPackageBoundaryTests
    {
        private static string PackageRoot
        {
            get
            {
                PackageInfo packageInfo = PackageInfo.FindForAssembly(
                    typeof(MultiplayerPackageBoundaryTests).Assembly);
                Assert.That(packageInfo, Is.Not.Null);
                Assert.That(
                    packageInfo.name,
                    Is.EqualTo("com.zerogamestudio.zeroengine.multiplayer"));
                return packageInfo.assetPath;
            }
        }

        [Test]
        public void CoreSource_HasNoUnitySdkOrConsumerDependency()
        {
            string coreRoot = Path.Combine(PackageRoot, "Runtime", "Core");
            string[] runtimeFiles = Directory.GetFiles(
                coreRoot,
                "*.cs",
                SearchOption.AllDirectories);
            Assert.That(runtimeFiles, Is.Not.Empty);

            string source = string.Join("\n", runtimeFiles.Select(File.ReadAllText));
            Assert.That(source, Does.Not.Contain("UnityEngine"));
            Assert.That(source, Does.Not.Contain("FishNet"));
            Assert.That(source, Does.Not.Contain("Steamworks"));
            Assert.That(source, Does.Not.Contain("Unity.Netcode"));
            Assert.That(source, Does.Not.Contain("Unity.Services"));
            Assert.That(source, Does.Not.Contain("POB"));
            Assert.That(source, Does.Not.Contain("GalleryKeeper"));

            string asmdef = File.ReadAllText(Path.Combine(
                coreRoot,
                "ZeroEngine.Multiplayer.Core.asmdef"));
            Assert.That(asmdef, Does.Contain("\"references\": []"));
            Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"));
        }

        [Test]
        public void UnityConfiguration_IsInDedicatedAssembly()
        {
            string configurationRoot = Path.Combine(PackageRoot, "Runtime", "Configuration");
            Assert.That(
                File.Exists(Path.Combine(configurationRoot, "MultiplayerSessionConfig.cs")),
                Is.True);

            string asmdef = File.ReadAllText(Path.Combine(
                configurationRoot,
                "ZeroEngine.Multiplayer.Configuration.asmdef"));
            Assert.That(asmdef, Does.Contain("ZeroEngine.Multiplayer.Core"));
            Assert.That(asmdef, Does.Contain("\"noEngineReferences\": false"));
        }

        [Test]
        public void OptionalSdkAssemblies_AreVersionGated()
        {
            AssertVersionGate(
                Path.Combine("Runtime", "FishNet", "ZeroEngine.Multiplayer.FishNet.asmdef"),
                "com.firstgeargames.fishnet",
                "ZEROENGINE_MULTIPLAYER_FISHNET");
            AssertVersionGate(
                Path.Combine("Runtime", "Steam", "ZeroEngine.Multiplayer.Steam.asmdef"),
                "com.rlabrecque.steamworks.net",
                "ZEROENGINE_MULTIPLAYER_STEAMWORKS");
        }

        [Test]
        public void PackageManifest_KeepsNetworkingSdksOptional()
        {
            string manifest = File.ReadAllText(Path.Combine(PackageRoot, "package.json"));
            Assert.That(
                Regex.IsMatch(manifest, "\\\"dependencies\\\"\\s*:\\s*\\{\\s*\\}"),
                Is.True);
        }

        private static void AssertVersionGate(
            string relativeAsmdefPath,
            string packageName,
            string define)
        {
            string asmdef = File.ReadAllText(Path.Combine(PackageRoot, relativeAsmdefPath));
            Assert.That(asmdef, Does.Contain(packageName));
            Assert.That(asmdef, Does.Contain(define));
            Assert.That(asmdef, Does.Contain("\"defineConstraints\""));
            Assert.That(asmdef, Does.Contain("\"versionDefines\""));
        }
    }
}
