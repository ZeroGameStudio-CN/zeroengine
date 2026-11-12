using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldAuthoringAssetUtility
    {
        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        public static T EnsureScriptableObjectAsset<T>(string assetPath)
            where T : ScriptableObject
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            var folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            EnsureFolder(folder);

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        public static string SanitizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "missing"
                : value.Replace('.', '_').Replace('/', '_').Replace(' ', '_');
        }
    }
}
