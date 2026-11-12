using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldNavigationBakeAuthoringUtility
    {
        private const string DefaultAssetNamePrefix = "NavMesh_";

        public static NavMeshData EnsurePlanarNavigationDataAsset(
            WorldCellDefinition cell,
            string assetFolder,
            Bounds cellLocalBounds,
            bool forceRebuild = false,
            string assetNamePrefix = DefaultAssetNamePrefix)
        {
            if (cell == null || string.IsNullOrWhiteSpace(cell.CellId) || string.IsNullOrWhiteSpace(assetFolder))
            {
                return null;
            }

            WorldAuthoringAssetUtility.EnsureFolder(assetFolder);

            var assetPath = GetNavigationDataAssetPath(assetFolder, cell.CellId, assetNamePrefix);
            var navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (navMeshData != null && !forceRebuild)
            {
                return navMeshData;
            }

            var rebuiltNavMeshData = BuildPlanarNavigationData(cellLocalBounds);
            if (rebuiltNavMeshData == null)
            {
                Debug.LogWarning($"[WorldNavigationBakeAuthoringUtility] Failed to bake navigation data for {cell.CellId}.");
                return forceRebuild ? null : navMeshData;
            }

            rebuiltNavMeshData.name = $"{assetNamePrefix}{WorldAuthoringAssetUtility.SanitizeId(cell.CellId)}";
            if (navMeshData != null)
            {
                EditorUtility.CopySerialized(rebuiltNavMeshData, navMeshData);
                navMeshData.name = rebuiltNavMeshData.name;
                EditorUtility.SetDirty(navMeshData);
                Object.DestroyImmediate(rebuiltNavMeshData);
                return navMeshData;
            }

            AssetDatabase.CreateAsset(rebuiltNavMeshData, assetPath);
            return rebuiltNavMeshData;
        }

        public static Bounds CreateBoundsFromAnchors(
            WorldCellDefinition cell,
            float padding = 2f,
            float minimumSize = 4f)
        {
            if (cell == null)
            {
                return new Bounds(Vector3.zero, new Vector3(minimumSize, minimumSize, minimumSize));
            }

            var hasAnchor = false;
            var min = Vector3.zero;
            var max = Vector3.zero;
            foreach (var anchor in cell.Anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                var position = anchor.CellLocalPosition;
                if (!hasAnchor)
                {
                    min = position;
                    max = position;
                    hasAnchor = true;
                    continue;
                }

                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            if (!hasAnchor)
            {
                return new Bounds(Vector3.zero, new Vector3(minimumSize, minimumSize, minimumSize));
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            size = new Vector3(
                Mathf.Max(size.x + padding * 2f, minimumSize),
                minimumSize,
                Mathf.Max(size.z + padding * 2f, minimumSize));
            return new Bounds(center, size);
        }

        public static string GetNavigationDataAssetPath(
            string assetFolder,
            string cellId,
            string assetNamePrefix = DefaultAssetNamePrefix)
        {
            return $"{assetFolder}/{assetNamePrefix}{WorldAuthoringAssetUtility.SanitizeId(cellId)}.asset";
        }

        public static NavMeshData BuildPlanarNavigationData(Bounds cellLocalBounds)
        {
            var buildSettings = NavMesh.GetSettingsByID(0);
            if (buildSettings.agentTypeID == -1)
            {
                buildSettings.agentTypeID = 0;
                buildSettings.agentRadius = 0.5f;
                buildSettings.agentHeight = 2f;
                buildSettings.agentSlope = 45f;
                buildSettings.agentClimb = 0.4f;
            }

            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                area = 0,
                transform = Matrix4x4.TRS(
                    new Vector3(cellLocalBounds.center.x, cellLocalBounds.center.y - 0.05f, cellLocalBounds.center.z),
                    Quaternion.identity,
                    Vector3.one),
                size = new Vector3(cellLocalBounds.size.x, 0.1f, cellLocalBounds.size.z)
            };

            var sources = new List<NavMeshBuildSource> { source };
            return NavMeshBuilder.BuildNavMeshData(
                buildSettings,
                sources,
                cellLocalBounds,
                Vector3.zero,
                Quaternion.identity);
        }
    }
}
