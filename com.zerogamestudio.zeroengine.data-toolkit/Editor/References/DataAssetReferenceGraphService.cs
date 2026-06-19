using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetReferenceGraphService
    {
        public static DataAssetReferenceGraph Build(IEnumerable<Object> assets)
        {
            var edges = new List<DataAssetReferenceEdge>();
            foreach (var asset in assets ?? Array.Empty<Object>())
            {
                if (asset == null)
                {
                    continue;
                }

                AddAssetEdges(asset, edges);
            }

            return new DataAssetReferenceGraph(edges);
        }

        private static void AddAssetEdges(Object asset, ICollection<DataAssetReferenceEdge> edges)
        {
            var sourcePath = ResolveAssetPath(asset);
            var serializedObject = new SerializedObject(asset);
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyPath == "m_Script" ||
                    iterator.propertyType != SerializedPropertyType.ObjectReference ||
                    iterator.objectReferenceValue == null)
                {
                    continue;
                }

                var target = iterator.objectReferenceValue;
                edges.Add(new DataAssetReferenceEdge(
                    sourcePath,
                    asset.name,
                    asset.GetType().Name,
                    iterator.propertyPath,
                    ResolveAssetPath(target),
                    target.name,
                    target.GetType().Name));
            }
        }

        private static string ResolveAssetPath(Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            var assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? asset.name : assetPath.Replace('\\', '/');
        }
    }
}
