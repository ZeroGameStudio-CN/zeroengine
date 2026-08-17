using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ZeroEngine.Editor.Dashboard
{
    internal static class DashboardCatalogSession
    {
        private static DashboardCatalog _catalog;

        static DashboardCatalogSession()
        {
            UnityEditor.PackageManager.Events.registeredPackages += _ => Invalidate();
        }

        internal static bool TryGet(out DashboardCatalog catalog)
        {
            catalog = _catalog;
            return catalog != null;
        }

        internal static void Store(DashboardCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        internal static void Invalidate()
        {
            _catalog = null;
        }
    }

    internal static class DashboardCatalogDiscovery
    {
        internal const string DescriptorFileName = "ZeroEngineDashboardModule.json";
        private static readonly Regex ManifestDependencyPattern = new Regex(
            "\\\"(?<name>com\\.zerogamestudio\\.[^\\\"]+)\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"",
            RegexOptions.Compiled);

        internal static DashboardCatalog Discover()
        {
            PackageManagerPackageInfo[] registeredPackages =
                PackageManagerPackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageManagerPackageInfo>();
            IReadOnlyDictionary<string, string> requestedPackageIds = ReadRequestedPackageIds();
            var installedPackages = registeredPackages
                .Where(package => package != null && !string.IsNullOrEmpty(package.name))
                .Select(package => new DashboardInstalledPackage(
                    package.name,
                    package.version,
                    package.resolvedPath,
                    package.displayName,
                    RequestedPackageId(package, requestedPackageIds),
                    package.isDirectDependency || requestedPackageIds.ContainsKey(package.name),
                    (package.dependencies ?? Array.Empty<UnityEditor.PackageManager.DependencyInfo>())
                    .Where(dependency => !string.IsNullOrEmpty(dependency.name))
                    .Select(dependency => dependency.name)
                    .ToArray()))
                .ToArray();
            var sources = new List<DashboardDescriptorSource>();

            foreach (PackageManagerPackageInfo package in registeredPackages
                         .Where(package => package != null && !string.IsNullOrEmpty(package.name))
                         .OrderBy(package => package.name, StringComparer.Ordinal))
            {
                string descriptorPath = Path.Combine(package.resolvedPath, "Editor", DescriptorFileName);
                if (!File.Exists(descriptorPath))
                    continue;
                sources.Add(ReadSource(
                    DashboardSourceKind.Package,
                    descriptorPath,
                    package.resolvedPath,
                    package.name,
                    package.version));
            }

            foreach (string assetPath in FindProjectDescriptorPaths())
            {
                string absolutePath = Path.GetFullPath(assetPath);
                sources.Add(ReadSource(
                    DashboardSourceKind.Project,
                    absolutePath,
                    Path.GetDirectoryName(absolutePath),
                    string.Empty,
                    string.Empty,
                    Path.GetDirectoryName(Application.dataPath)));
            }

            return DashboardCatalogBuilder.Build(sources, installedPackages);
        }

        private static string RequestedPackageId(
            PackageManagerPackageInfo package,
            IReadOnlyDictionary<string, string> requestedPackageIds)
        {
            if (package != null && requestedPackageIds.TryGetValue(package.name, out string packageId))
                return packageId;
            return package?.packageId ?? string.Empty;
        }

        private static IReadOnlyDictionary<string, string> ReadRequestedPackageIds()
        {
            try
            {
                DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
                if (projectDirectory == null)
                    return new Dictionary<string, string>(StringComparer.Ordinal);

                string manifestPath = Path.Combine(projectDirectory.FullName, "Packages", "manifest.json");
                string manifest = File.ReadAllText(manifestPath, Encoding.UTF8);
                return ParseRequestedPackageIds(manifest);
            }
            catch (Exception)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        internal static IReadOnlyDictionary<string, string> ParseRequestedPackageIds(string manifest)
        {
            var packageIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in ManifestDependencyPattern.Matches(manifest ?? string.Empty))
            {
                string name = match.Groups["name"].Value;
                string packageId = match.Groups["value"].Value;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(packageId))
                    packageIds[name] = packageId;
            }
            return packageIds;
        }

        private static DashboardDescriptorSource ReadSource(
            DashboardSourceKind kind,
            string path,
            string rootPath,
            string packageName,
            string packageVersion,
            string projectRootPath = null)
        {
            try
            {
                return new DashboardDescriptorSource(
                    kind,
                    path,
                    rootPath,
                    packageName,
                    packageVersion,
                    File.ReadAllText(path, Encoding.UTF8),
                    projectRootPath: projectRootPath);
            }
            catch (Exception exception)
            {
                return new DashboardDescriptorSource(
                    kind,
                    path,
                    rootPath,
                    packageName,
                    packageVersion,
                    null,
                    exception.Message,
                    projectRootPath);
            }
        }

        private static IReadOnlyList<string> FindProjectDescriptorPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(DescriptorFileName)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0 ||
                    !string.Equals(Path.GetFileName(path), DescriptorFileName, StringComparison.Ordinal))
                {
                    continue;
                }
                paths.Add(path);
            }

            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
    }
}
