using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphAuthoringUtilityTests
    {
        private const string TempFolderPath = "Assets/__ZEWorldGraphAuthoringUtilityTests";
        private const string GraphAssetPath = TempFolderPath + "/WorldGraph.asset";
        private const string NavigationFolderPath = TempFolderPath + "/Navigation";

        [Test]
        public void EnsureWorldGraphAsset_CreatesAndConfiguresAsset()
        {
            try
            {
                var graph = WorldGraphAssetAuthoringUtility.EnsureWorldGraphAsset(
                    GraphAssetPath,
                    "world.test",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.test",
                            "Test",
                            new[]
                            {
                                CreateCell("cell.test", Vector3.zero)
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                Assert.NotNull(graph);
                Assert.AreEqual("world.test", graph.WorldGraphId);
                Assert.AreEqual(GraphAssetPath, AssetDatabase.GetAssetPath(graph));
                Assert.NotNull(AssetDatabase.LoadAssetAtPath<WorldGraphSO>(GraphAssetPath));
            }
            finally
            {
                EditorTestAssetCleanup.DeleteAssetFolder(TempFolderPath);
            }
        }

        [Test]
        public void CreateBoundsFromAnchors_UsesAnchorExtentsAndMinimumSize()
        {
            var cell = new WorldCellDefinition(
                "cell.test",
                "Test",
                WorldCellKind.Outdoor,
                "Test_Cell",
                WorldCellLayer.Navigation,
                1,
                new[]
                {
                    new WorldAnchorDefinition("anchor.a", "A", WorldAnchorKind.Spawn, new Vector3(-1f, 0f, -3f), Vector3.forward),
                    new WorldAnchorDefinition("anchor.b", "B", WorldAnchorKind.RoadExit, new Vector3(5f, 0f, 1f), Vector3.forward)
                },
                Array.Empty<WorldStreamingBoundaryDefinition>());

            var bounds = WorldNavigationBakeAuthoringUtility.CreateBoundsFromAnchors(cell);

            Assert.AreEqual(new Vector3(2f, 0f, -1f), bounds.center);
            Assert.AreEqual(new Vector3(10f, 4f, 8f), bounds.size);
        }

        [Test]
        public void EnsurePlanarNavigationDataAsset_CreatesSanitizedAsset()
        {
            try
            {
                var cell = CreateCell("cell.test/one", Vector3.zero);
                var bounds = WorldNavigationBakeAuthoringUtility.CreateBoundsFromAnchors(cell);
                var navMeshData = WorldNavigationBakeAuthoringUtility.EnsurePlanarNavigationDataAsset(
                    cell,
                    NavigationFolderPath,
                    bounds,
                    forceRebuild: true);

                var expectedPath = NavigationFolderPath + "/NavMesh_cell_test_one.asset";
                Assert.NotNull(navMeshData);
                Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(navMeshData));
                Assert.AreEqual(expectedPath, WorldNavigationBakeAuthoringUtility.GetNavigationDataAssetPath(NavigationFolderPath, cell.CellId));
            }
            finally
            {
                EditorTestAssetCleanup.DeleteAssetFolder(TempFolderPath);
            }
        }

        private static WorldCellDefinition CreateCell(string cellId, Vector3 anchorPosition)
        {
            return new WorldCellDefinition(
                cellId,
                "Test Cell",
                WorldCellKind.Outdoor,
                "Test_Cell",
                WorldCellLayer.Navigation,
                1,
                new[]
                {
                    new WorldAnchorDefinition("anchor.test", "Spawn", WorldAnchorKind.Spawn, anchorPosition, Vector3.forward)
                },
                Array.Empty<WorldStreamingBoundaryDefinition>());
        }
    }
}
