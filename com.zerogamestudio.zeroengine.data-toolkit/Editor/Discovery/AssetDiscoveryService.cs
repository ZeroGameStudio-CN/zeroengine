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
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AssetPathCache[cacheKey] = paths;
            return paths;
        }

        public static UnityEngine.Object LoadFirstAssetOfType(string path, Type type)
        {
            if (string.IsNullOrEmpty(path) || type == null)
            {
                return null;
            }

            var mainAsset = AssetDatabase.LoadAssetAtPath(path, type);
            if (mainAsset != null)
            {
                return mainAsset;
            }

            if (type == typeof(GameObject))
            {
                return null;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset != null && type.IsInstanceOfType(asset))
                {
                    return asset;
                }
            }

            return null;
        }

        public static void ClearCaches()
        {
            AssetPathCache.Clear();
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
