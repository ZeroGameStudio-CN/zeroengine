using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ZeroEngine.AiSupport.Editor
{
    /// <summary>Read-only snapshot of the packages actually registered in this Editor.</summary>
    public static class SkillPackageSnapshot
    {
        [Serializable]
        private sealed class Snapshot
        {
            public int schemaVersion = 1;
            public string projectPath, unityVersion, manifestSha256, lockSha256;
            public InstalledPackage[] packages;
        }

        [Serializable]
        private sealed class InstalledPackage
        {
            public string name, version, source, resolvedPath, gitHash;
        }

        // Returns JSON only. The caller chooses an external snapshot destination
        // and the project's normal CLI/Editor execution route.
        public static string CreateJson()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var value = new Snapshot {
                projectPath = project,
                unityVersion = Application.unityVersion,
                manifestSha256 = Hash(Path.Combine(project, "Packages/manifest.json")),
                lockSha256 = Hash(Path.Combine(project, "Packages/packages-lock.json")),
                packages = PackageInfo.GetAllRegisteredPackages()
                    .Where(package => package.name.StartsWith("com.zerogamestudio.", StringComparison.Ordinal))
                    .OrderBy(package => package.name, StringComparer.Ordinal)
                    .Select(package => new InstalledPackage {
                        name = package.name,
                        version = package.version,
                        source = package.source.ToString(),
                        resolvedPath = Path.GetFullPath(package.resolvedPath),
                        gitHash = package.source == PackageSource.Git ? package.git?.hash ?? string.Empty : string.Empty
                    }).ToArray()
            };
            return JsonUtility.ToJson(value, true);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
