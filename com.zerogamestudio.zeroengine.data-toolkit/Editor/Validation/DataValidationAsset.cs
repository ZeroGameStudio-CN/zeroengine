using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataValidationAsset
    {
        public DataValidationAsset(Type type, string assetPath, Object asset)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            AssetPath = NormalizePath(assetPath);
            Asset = asset;
        }

        public Type Type { get; }
        public string AssetPath { get; }
        public Object Asset { get; }
        public string AssetName => Asset == null ? string.Empty : Asset.name;

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
