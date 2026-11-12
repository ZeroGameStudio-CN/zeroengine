using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModSystemPackageBoundaryTests
    {
        private static readonly string PackageRoot = Path.GetFullPath(
            PackageInfo.FindForAssembly(typeof(ModLoadReport).Assembly).resolvedPath);

        [Test]
        public void CoreAssembly_DoesNotReferenceGameplayOrProjectPackages()
        {
            string coreAsmdef = File.ReadAllText(Path.Combine(PackageRoot, "Runtime/ZeroEngine.ModSystem.asmdef"));

            Assert.That(coreAsmdef, Does.Not.Contain("ZeroEngine.Combat"));
            Assert.That(coreAsmdef, Does.Not.Contain("ZeroEngine.Data"));
            Assert.That(coreAsmdef, Does.Not.Contain("ZeroEngine.Economy"));
            Assert.That(coreAsmdef, Does.Not.Contain("ZeroEngine.TCE"));
            Assert.That(coreAsmdef, Does.Not.Contain("POB"));
            Assert.That(coreAsmdef, Does.Not.Contain("Steamworks"));
        }

        [Test]
        public void CoreSources_DoNotReferenceProjectUiPersistenceOrSteamTypes()
        {
            string[] forbidden =
            {
                "POB.",
                "P5.",
                "TMPro",
                "UnityEngine.UI",
                "ES3",
                "Steamworks"
            };
            string[] sources = Directory.GetFiles(
                Path.Combine(PackageRoot, "Runtime/Core"),
                "*.cs",
                SearchOption.AllDirectories);

            foreach (string source in sources)
            {
                string text = File.ReadAllText(source);
                foreach (string token in forbidden)
                    Assert.That(text, Does.Not.Contain(token), $"{source} contains forbidden token {token}");
            }
        }

        [Test]
        public void LegacyAssembly_IsOnlyPlaceForBroadTypeRegistry()
        {
            Assert.That(Directory.Exists(Path.Combine(PackageRoot, "Runtime/Legacy")), Is.False);
            var legacyPackage = PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => package.name == "com.zerogamestudio.zeroengine");
            if (legacyPackage == null)
                Assert.Ignore("Legacy ownership is checked when the compatibility bundle is installed.");
            string legacyRoot = Path.Combine(legacyPackage.resolvedPath, "Runtime/ModSystem");
            string registryPath = Path.Combine(legacyRoot, "ZeroEngineTypeRegistry.cs");
            string legacyAsmdef = File.ReadAllText(Path.Combine(legacyRoot, "ZeroEngine.ModSystem.asmdef"));

            Assert.That(File.Exists(registryPath), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyRoot, "ModLoader.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyRoot, "ModHotReloader.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyRoot, "ModSystemIntegration.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyRoot, "Scripting/LuaScriptRunner.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyRoot, "Scripting/ModScriptManager.cs")), Is.True);
            Assert.That(legacyAsmdef, Does.Contain("\"name\": \"ZeroEngine.ModSystem.Legacy\""));
            Assert.That(legacyAsmdef, Does.Contain("\"ZeroEngine.ModSystem\""));
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Combat"));
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Data"));
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Economy"));
        }

        [Test]
        public void InstalledLegacyBundle_DoesNotDuplicateNeutralManifestOrAssembly()
        {
            var legacyPackage = PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => package.name == "com.zerogamestudio.zeroengine");
            if (legacyPackage == null)
                Assert.Ignore("The compatibility bundle is not installed in this package-only fixture.");
            string oldRuntimeRoot = Path.Combine(legacyPackage.resolvedPath, "Runtime/ModSystem");

            string legacyAsmdef = File.ReadAllText(Path.Combine(oldRuntimeRoot, "ZeroEngine.ModSystem.asmdef"));
            Assert.That(legacyAsmdef, Does.Contain("\"name\": \"ZeroEngine.ModSystem.Legacy\""));
            Assert.That(File.Exists(Path.Combine(oldRuntimeRoot, "ModManifest.cs")), Is.False);
            Assert.That(typeof(ModManifest).Assembly.GetName().Name, Is.EqualTo("ZeroEngine.ModSystem"));
        }

        [Test]
        public void LegacyEditorAssembly_IsOwnedOnlyByCompatibilityBundle()
        {
            Assert.That(Directory.Exists(Path.Combine(PackageRoot, "Editor/Legacy")), Is.False);
            var legacyPackage = PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => package.name == "com.zerogamestudio.zeroengine");
            if (legacyPackage == null)
                Assert.Ignore("Legacy authoring ownership requires the compatibility bundle.");
            string legacyEditor = Path.Combine(legacyPackage.resolvedPath, "Editor/ModSystem");
            string editorAsmdefPath = Path.Combine(legacyEditor, "ZeroEngine.ModSystem.Editor.asmdef");
            string editorAsmdef = File.ReadAllText(editorAsmdefPath);

            Assert.That(File.Exists(Path.Combine(legacyEditor, "ModCreatorWindow.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyEditor, "ModExporter.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyEditor, "ModValidatorWindow.cs")), Is.True);
            Assert.That(editorAsmdef, Does.Contain("ZeroEngine.ModSystem"));
            Assert.That(editorAsmdef, Does.Contain("ZeroEngine.ModSystem.Legacy"));
        }

        [Test]
        public void SteamAssembly_ReferencesSteamworksWhenVersionDefineEnablesSteam()
        {
            string steamAsmdefPath = Path.Combine(PackageRoot, "Runtime/Steam/ZeroEngine.ModSystem.Steam.asmdef");
            string steamAsmdef = File.ReadAllText(steamAsmdefPath);
            string steamworksApi = File.ReadAllText(Path.Combine(PackageRoot, "Runtime/Steam/SteamworksWorkshopApi.cs"));
            string steamSource = File.ReadAllText(Path.Combine(PackageRoot, "Runtime/Steam/SteamWorkshopModSource.cs"));

            Assert.That(steamAsmdef, Does.Contain("ZeroEngine.ModSystem"));
            Assert.That(steamAsmdef, Does.Contain("com.rlabrecque.steamworks.net"));
            Assert.That(steamAsmdef, Does.Contain("STEAMWORKS_NET"));
            Assert.That(steamAsmdef, Does.Contain("\"includePlatforms\""));
            Assert.That(steamAsmdef, Does.Contain("Editor"));
            Assert.That(steamAsmdef, Does.Contain("WindowsStandalone64"));
            Assert.That(steamAsmdef, Does.Contain("LinuxStandalone64"));
            Assert.That(steamAsmdef, Does.Contain("macOSStandalone"));
            Assert.That(steamAsmdef, Does.Not.Contain("Android"));
            Assert.That(steamworksApi, Does.Contain("#if STEAMWORKS_NET && !UNITY_ANDROID"));
            Assert.That(steamworksApi, Does.Not.Contain("2372330"));
            Assert.That(steamSource, Does.Not.Contain("RuntimeInitializeOnLoadMethod"));
            Assert.That(steamSource, Does.Not.Contain("ModSourceRegistry.Register"));
        }

        [Test]
        public void PackageManifest_DoesNotRequireGameplayLegacyBundleOrEditorUi()
        {
            string manifest = File.ReadAllText(Path.Combine(PackageRoot, "package.json"));
            Match dependencies = Regex.Match(
                manifest,
                "\\\"dependencies\\\"\\s*:\\s*\\{(?<body>[^}]*)\\}");

            Assert.That(dependencies.Success, Is.True);
            string dependencyBody = dependencies.Groups["body"].Value;
            string[] zeroEngineDependencies = Regex.Matches(
                    dependencyBody,
                    "\\\"(?<name>com\\.zerogamestudio\\.[^\\\"]+)\\\"\\s*:")
                .Cast<Match>()
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            Assert.That(
                zeroEngineDependencies,
                Is.Empty);
            Assert.That(dependencyBody, Does.Contain("com.unity.nuget.newtonsoft-json"));
        }

        [Test]
        public void HiddenPackageFolders_DoNotShipUnityMetaFiles()
        {
            string[] hiddenFolderMetaFiles = Directory
                .EnumerateFiles(PackageRoot, "*.meta", SearchOption.AllDirectories)
                .Where(path => path.Replace('\\', '/').Contains("~/"))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(hiddenFolderMetaFiles, Is.Empty);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Documentation~.meta")), Is.False);
        }
    }
}
