using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetBatchEditService
    {
        public static DataAssetChangePreview PreviewSetValue(
            IEnumerable<Object> assets,
            string fieldPath,
            string newValue)
        {
            var assetsByPath = new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
            var updates = new List<DataAssetFieldUpdate>();
            foreach (var asset in assets ?? Array.Empty<Object>())
            {
                if (asset == null)
                {
                    continue;
                }

                var assetPath = ResolveAssetPath(asset);
                assetsByPath[assetPath] = asset;
                updates.Add(new DataAssetFieldUpdate(assetPath, fieldPath, newValue));
            }

            return DataAssetFieldChangeService.PreviewChanges(updates, assetsByPath);
        }

        private static string ResolveAssetPath(Object asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? asset.name : assetPath.Replace('\\', '/');
        }
    }
}
