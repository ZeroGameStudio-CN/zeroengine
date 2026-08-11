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
            string registryPath = Path.Combine(PackageRoot, "Runtime/Legacy/ZeroEngineTypeRegistry.cs");
            string legacyAsmdef = File.ReadAllText(Path.Combine(PackageRoot, "Runtime/Legacy/ZeroEngine.ModSystem.Legacy.asmdef"));

            Assert.That(File.Exists(registryPath), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Runtime/Legacy/ModLoader.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Runtime/Legacy/ModHotReloader.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Runtime/Legacy/ModSystemIntegration.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Runtime/Legacy/Scripting/LuaScriptRunner.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Runtime/Legacy/Scripting/ModScriptManager.cs")), Is.True);
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Combat"));
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Data"));
            Assert.That(legacyAsmdef, Does.Contain("ZeroEngine.Economy"));
            Assert.That(legacyAsmdef, Does.Contain("\"defineConstraints\""));
            Assert.That(legacyAsmdef, Does.Contain("\"ZEROENGINE_MODSYSTEM_LEGACY\""));
        }

        [Test]
        public void OldBasePackage_DoesNotKeepDuplicateRuntimeAssemblies()
        {
            string oldRuntimeRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine/Runtime/ModSystem"));
            if (!Directory.Exists(oldRuntimeRoot))
                Assert.Pass("The legacy package is not installed, so duplicate runtime assemblies cannot be present.");

            string[] remainingEntries = Directory.GetFileSystemEntries(oldRuntimeRoot);

            Assert.That(File.Exists(Path.Combine(oldRuntimeRoot, "ZeroEngine.ModSystem.asmdef")), Is.False);
            Assert.That(File.Exists(Path.Combine(oldRuntimeRoot, "ModLoader.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(oldRuntimeRoot, "ModManifest.cs")), Is.False);
            Assert.That(remainingEntries, Is.EquivalentTo(new[]
            {
                Path.Combine(oldRuntimeRoot, "README.md"),
                Path.Combine(oldRuntimeRoot, "README.md.meta")
            }));
        }

        [Test]
        public void LegacyEditorAssembly_MovedIntoStandalonePackage()
        {
            string editorAsmdefPath = Path.Combine(PackageRoot, "Editor/Legacy/ZeroEngine.ModSystem.Editor.asmdef");
            string editorAsmdef = File.ReadAllText(editorAsmdefPath);

            Assert.That(File.Exists(Path.Combine(PackageRoot, "Editor/Legacy/ModCreatorWindow.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Editor/Legacy/ModExporter.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(PackageRoot, "Editor/Legacy/ModValidatorWindow.cs")), Is.True);
            Assert.That(editorAsmdef, Does.Contain("ZeroEngine.ModSystem"));
            Assert.That(editorAsmdef, Does.Contain("ZeroEngine.ModSystem.Legacy"));
            Assert.That(editorAsmdef, Does.Contain("\"defineConstraints\""));
            Assert.That(editorAsmdef, Does.Contain("\"ZEROENGINE_MODSYSTEM_LEGACY\""));
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
        public void PackageManifest_AllowsOnlySharedEditorUiSiblingDependency()
        {
            string manifest = File.ReadAllText(Path.Combine(PackageRoot, "package.json"));
            Match dependencies = Regex.Match(
                manifest,
                "\\\"dependencies\\\"\\s*:\\s*\\{(?<body>[^}]*)\\}");

            Assert.That(manifest, Does.Contain("\"version\": \"0.3.0\""));
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
                Is.EquivalentTo(new[] { "com.zerogamestudio.zeroengine.editor-ui" }));
            Assert.That(
                dependencyBody,
                Does.Contain("\"com.zerogamestudio.zeroengine.editor-ui\": \"1.3.0\""));
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
