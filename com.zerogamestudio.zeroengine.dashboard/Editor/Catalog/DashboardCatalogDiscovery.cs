using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        internal static DashboardCatalog Discover()
        {
            PackageManagerPackageInfo[] registeredPackages =
                PackageManagerPackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageManagerPackageInfo>();
            var installedPackages = registeredPackages
                .Where(package => package != null && !string.IsNullOrEmpty(package.name))
                .Select(package => new DashboardInstalledPackage(
                    package.name,
                    package.version,
                    package.resolvedPath,
                    package.displayName,
                    package.packageId,
                    package.isDirectDependency,
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
