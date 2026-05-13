using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public static class AssetDiscoveryService
    {
        private static readonly Dictionary<string, string[]> AssetPathCache = new();

        public static string[] GetAssetPathsForType(Type type, DataToolkitProjectSettings settings)
        {
            if (type == null || settings == null)
            {
                return Array.Empty<string>();
            }

            var cacheKey = $"{settings.ProjectId}|{type.AssemblyQualifiedName}";
            if (AssetPathCache.TryGetValue(cacheKey, out var cachedPaths))
            {
                return cachedPaths;
            }

            var searchRoots = settings.SearchRoots.Count == 0
                ? new[] { "Assets" }
                : settings.SearchRoots.ToArray();

            var paths = AssetDatabase.FindAssets($"t:{type.Name}", searchRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(NormalizePath)
                .Where(path => !IsExcluded(path, settings))
                .Where(path => ContainsAssetOfType(path, type))
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AssetPathCache[cacheKey] = paths;
            return paths;
        }

        public static void ClearCaches()
        {
            AssetPathCache.Clear();
        }

        private static bool ContainsAssetOfType(string path, Type type)
        {
            if (type == typeof(GameObject))
            {
                return AssetDatabase.LoadAssetAtPath(path, type) != null;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset != null && type.IsInstanceOfType(asset))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcluded(string path, DataToolkitProjectSettings settings)
        {
            return settings.ExcludedPaths.Any(excluded =>
                path.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
