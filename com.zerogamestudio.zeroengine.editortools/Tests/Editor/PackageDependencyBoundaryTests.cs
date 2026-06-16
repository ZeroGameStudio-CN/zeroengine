using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.EditorTools.Tests
{
    [TestFixture]
    public sealed class PackageDependencyBoundaryTests
    {
        private static readonly IReadOnlyDictionary<string, string> ExternalAssemblyPackages =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Unity.Addressables"] = "com.unity.addressables",
                ["Unity.InputSystem"] = "com.unity.inputsystem",
                ["Unity.Localization"] = "com.unity.localization",
                ["Unity.Netcode.Runtime"] = "com.unity.netcode.gameobjects",
                ["Unity.Networking.Transport"] = "com.unity.transport",
                ["Unity.ResourceManager"] = "com.unity.addressables",
                ["Unity.Services.Authentication"] = "com.unity.services.authentication",
                ["Unity.Services.Core"] = "com.unity.services.core",
                ["Unity.Services.Lobbies"] = "com.unity.services.lobby",
                ["Unity.Services.Relay"] = "com.unity.services.relay",
                ["Unity.TextMeshPro"] = "com.unity.textmeshpro",
                ["UnityEditor.TestRunner"] = "com.unity.test-framework",
                ["UnityEngine.TestRunner"] = "com.unity.test-framework",
                ["Unity.Timeline"] = "com.unity.timeline",
                ["Unity.ugui"] = "com.unity.ugui",
            };

        private static readonly HashSet<string> BuiltInUnityReferences =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "UnityEngine.CoreModule",
            };

        private static readonly IReadOnlyDictionary<string, string> OptionalProviderDefines =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CrashKonijn.Goap.Core"] = "CRASHKONIJN_GOAP",
                ["CrashKonijn.Goap.Runtime"] = "CRASHKONIJN_GOAP",
                ["ES3"] = "ES3",
                ["Steamworks.NET.SteamManager"] = "STEAMWORKS_NET",
                ["com.rlabrecque.steamworks.net"] = "STEAMWORKS_NET",
                ["XNode"] = "XNODE_PRESENT",
            };

        [Test]
        public void PackageManifests_DeclareStrongAsmdefPackageDependencies()
        {
            var packages = FindZeroGameStudioPackages();
            var assemblyToPackage = BuildAssemblyPackageMap(packages);
            var violations = new List<string>();

            foreach (var package in packages)
            {
                var currentPackage = Path.GetFileName(package);
                var packageJsonPath = Path.Combine(package, "package.json");
                var dependencies = ReadPackageDependencies(packageJsonPath);

                foreach (var asmdefPath in EnumeratePublishedAsmdefs(package))
                {
                    var asmdef = ReadAsmdef(asmdefPath);

                    foreach (var reference in asmdef.References)
                    {
                        if (ShouldIgnoreReference(reference))
                        {
                            continue;
                        }

                        if (IsAllowedOptionalProviderReference(reference, asmdef) ||
                            IsAllowedOptInTestFrameworkReference(reference, asmdef))
                        {
                            continue;
                        }

                        if (!TryResolvePackage(reference, assemblyToPackage, out var requiredPackage))
                        {
                            violations.Add(
                                $"{currentPackage}: {RelativePath(asmdefPath)} references {reference} but the assembly is not mapped to a package dependency or allowlisted.");
                            continue;
                        }

                        if (string.Equals(currentPackage, requiredPackage, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!dependencies.ContainsKey(requiredPackage))
                        {
                            if (IsAllowedOptInLegacyReference(asmdef, currentPackage, requiredPackage))
                            {
                                continue;
                            }

                            violations.Add(
                                $"{currentPackage}: {RelativePath(asmdefPath)} references {reference} but package.json does not depend on {requiredPackage}.");
                        }
                    }
                }
            }

            Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
        }

        private static IReadOnlyList<string> FindZeroGameStudioPackages()
        {
            var packagesRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages"));
            return Directory.EnumerateDirectories(packagesRoot, "com.zerogamestudio.*", SearchOption.TopDirectoryOnly)
                .Where(path => File.Exists(Path.Combine(path, "package.json")))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyDictionary<string, string> BuildAssemblyPackageMap(IEnumerable<string> packages)
        {
            var assemblyToPackage = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var package in packages)
            {
                foreach (var asmdefPath in EnumeratePublishedAsmdefs(package))
                {
                    var asmdef = ReadAsmdef(asmdefPath);
                    if (!string.IsNullOrEmpty(asmdef.Name))
                    {
                        assemblyToPackage[asmdef.Name] = Path.GetFileName(package);
                    }
                }
            }

            return assemblyToPackage;
        }

        private static IEnumerable<string> EnumeratePublishedAsmdefs(string packageRoot)
        {
            return Directory.EnumerateFiles(packageRoot, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyDictionary<string, string> ReadPackageDependencies(string packageJsonPath)
        {
            var json = File.ReadAllText(packageJsonPath);
            var body = MatchObjectBody(json, "dependencies");
            return Regex.Matches(body, "\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<version>[^\"]*)\"")
                .Cast<Match>()
                .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["version"].Value, StringComparer.Ordinal);
        }

        private static AsmdefInfo ReadAsmdef(string asmdefPath)
        {
            var json = File.ReadAllText(asmdefPath);
            return new AsmdefInfo(
                MatchStringValue(json, "name"),
                MatchStringArray(json, "references"),
                MatchStringArray(json, "defineConstraints"),
                MatchBoolValue(json, "autoReferenced", true));
        }

        private static bool TryResolvePackage(
            string assemblyReference,
            IReadOnlyDictionary<string, string> assemblyToPackage,
            out string packageName)
        {
            packageName = null;

            if (assemblyToPackage.TryGetValue(assemblyReference, out packageName))
            {
                return true;
            }

            if (ExternalAssemblyPackages.TryGetValue(assemblyReference, out packageName))
            {
                return true;
            }

            return false;
        }

        private static bool ShouldIgnoreReference(string assemblyReference)
        {
            return string.IsNullOrEmpty(assemblyReference) ||
                assemblyReference.StartsWith("GUID:", StringComparison.Ordinal) ||
                BuiltInUnityReferences.Contains(assemblyReference);
        }

        private static bool IsAllowedOptionalProviderReference(string assemblyReference, AsmdefInfo asmdef)
        {
            return OptionalProviderDefines.TryGetValue(assemblyReference, out var requiredDefine) &&
                asmdef.DefineConstraints.Contains(requiredDefine);
        }

        private static bool IsAllowedOptInTestFrameworkReference(string assemblyReference, AsmdefInfo asmdef)
        {
            return !asmdef.AutoReferenced &&
                asmdef.DefineConstraints.Contains("UNITY_INCLUDE_TESTS") &&
                (string.Equals(assemblyReference, "UnityEngine.TestRunner", StringComparison.Ordinal) ||
                    string.Equals(assemblyReference, "UnityEditor.TestRunner", StringComparison.Ordinal));
        }

        private static bool IsAllowedOptInLegacyReference(
            AsmdefInfo asmdef,
            string currentPackage,
            string requiredPackage)
        {
            if (!string.Equals(currentPackage, "com.zerogamestudio.zeroengine.modsystem", StringComparison.Ordinal) ||
                asmdef.AutoReferenced ||
                !asmdef.DefineConstraints.Contains("ZEROENGINE_MODSYSTEM_LEGACY") ||
                !asmdef.Name.StartsWith("ZeroEngine.ModSystem.", StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(requiredPackage, "com.zerogamestudio.zeroengine", StringComparison.Ordinal) ||
                string.Equals(requiredPackage, "com.zerogamestudio.zeroengine.combat", StringComparison.Ordinal) ||
                string.Equals(requiredPackage, "com.zerogamestudio.zeroengine.data", StringComparison.Ordinal) ||
                string.Equals(requiredPackage, "com.zerogamestudio.zeroengine.economy", StringComparison.Ordinal);
        }

        private static string MatchStringValue(string json, string fieldName)
        {
            var match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"");
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        private static IReadOnlyList<string> MatchStringArray(string json, string fieldName)
        {
            var body = MatchArrayBody(json, fieldName);
            return Regex.Matches(body, "\"(?<value>[^\"]*)\"")
                .Cast<Match>()
                .Select(match => match.Groups["value"].Value)
                .ToArray();
        }

        private static string MatchArrayBody(string json, string fieldName)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\\[(?<body>.*?)\\]",
                RegexOptions.Singleline);
            return match.Success ? match.Groups["body"].Value : string.Empty;
        }

        private static bool MatchBoolValue(string json, string fieldName, bool defaultValue)
        {
            var match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);
            return match.Success ? bool.Parse(match.Groups["value"].Value) : defaultValue;
        }

        private static string MatchObjectBody(string json, string fieldName)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\\{{(?<body>.*?)\\}}",
                RegexOptions.Singleline);
            return match.Success ? match.Groups["body"].Value : string.Empty;
        }

        private static string RelativePath(string path)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return path.Substring(projectRoot.Length + 1);
        }

        private sealed class AsmdefInfo
        {
            public AsmdefInfo(
                string name,
                IReadOnlyList<string> references,
                IReadOnlyList<string> defineConstraints,
                bool autoReferenced)
            {
                Name = name;
                References = references;
                DefineConstraints = defineConstraints;
                AutoReferenced = autoReferenced;
            }

            public string Name { get; }
            public IReadOnlyList<string> References { get; }
            public IReadOnlyList<string> DefineConstraints { get; }
            public bool AutoReferenced { get; }
        }
    }
}
