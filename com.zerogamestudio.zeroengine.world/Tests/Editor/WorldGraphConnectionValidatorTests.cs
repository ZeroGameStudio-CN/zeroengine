using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.Editor.WorldGraph;

namespace ZeroEngine.World.WorldGraph.Tests
{
    public sealed class WorldGraphConnectionValidatorTests
    {
        [Test]
        public void Validate_WalkableConnectionMissingTargetAddress_ReturnsError()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[]
            {
                CreateConnection(WorldTravelMode.SeamlessWalk, targetWorldGraphAddress: string.Empty)
            });

            var issues = WorldGraphConnectionValidator.Validate(network, CreateGraphsById());

            AssertHasIssue(
                issues,
                "WORLD_GRAPH_CONNECTION_TARGET_ADDRESS_MISSING",
                "connection.map_a.map_b.walk");
        }

        [Test]
        public void Validate_WalkableConnectionMissingTargetCell_ReturnsError()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[]
            {
                CreateConnection(WorldTravelMode.SeamlessWalk, targetCellId: "cell.map_b.missing")
            });

            var issues = WorldGraphConnectionValidator.Validate(network, CreateGraphsById());

            AssertHasIssue(
                issues,
                "WORLD_GRAPH_CONNECTION_TARGET_CELL_MISSING",
                "cell.map_b.missing");
        }

        [Test]
        public void Validate_FastTravelConnection_DoesNotRunWalkableNoBlackScreenChecks()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[] { CreateConnection(WorldTravelMode.FastTravel) });

            var issues = WorldGraphConnectionValidator.Validate(network, CreateGraphsById());

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void Validate_SourceAndTargetIds_AreIncludedInIssueMessages()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[]
            {
                CreateConnection(WorldTravelMode.SeamlessWalk, targetWorldGraphId: "world.map_missing")
            });

            var issues = WorldGraphConnectionValidator.Validate(network, CreateGraphsById());

            Assert.That(issues.Any(issue =>
                issue.Code == "WORLD_GRAPH_CONNECTION_TARGET_GRAPH_MISSING"
                && issue.Message.Contains("world.map_a")
                && issue.Message.Contains("world.map_missing")), Is.True);
        }

        private static WorldGraphConnectionDefinition CreateConnection(
            WorldTravelMode mode,
            string targetWorldGraphId = "world.map_b",
            string targetWorldGraphAddress = "WorldGraph/MapB",
            string targetCellId = "cell.map_b.entrance")
        {
            return new WorldGraphConnectionDefinition(
                "connection.map_a.map_b.walk",
                "world.map_a",
                "cell.map_a.exit",
                "boundary.map_a.to_map_b",
                "anchor.map_a.exit",
                targetWorldGraphId,
                targetWorldGraphAddress,
                targetCellId,
                "anchor.map_b.entrance",
                mode);
        }

        private static IReadOnlyDictionary<string, WorldGraphSO> CreateGraphsById()
        {
            return new Dictionary<string, WorldGraphSO>
            {
                ["world.map_a"] = CreateGraph(
                    "world.map_a",
                    "cell.map_a.exit",
                    "anchor.map_a.exit",
                    "boundary.map_a.to_map_b"),
                ["world.map_b"] = CreateGraph(
                    "world.map_b",
                    "cell.map_b.entrance",
                    "anchor.map_b.entrance",
                    null)
            };
        }

        private static WorldGraphSO CreateGraph(
            string graphId,
            string cellId,
            string anchorId,
            string boundaryId)
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            var boundaries = string.IsNullOrWhiteSpace(boundaryId)
                ? Array.Empty<WorldStreamingBoundaryDefinition>()
                : new[] { new WorldStreamingBoundaryDefinition(boundaryId, Array.Empty<string>()) };
            var cell = new WorldCellDefinition(
                cellId,
                cellId,
                WorldCellKind.Outdoor,
                $"Scenes/{cellId}.unity",
                WorldCellLayer.All,
                1,
                new[]
                {
                    new WorldAnchorDefinition(
                        anchorId,
                        anchorId,
                        WorldAnchorKind.RoadExit,
                        Vector3.zero,
                        Vector3.forward)
                },
                boundaries);
            graph.ConfigureForTests(
                graphId,
                new[] { new WorldRegionDefinition($"region.{graphId}", graphId, new[] { cell }) },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private static void AssertHasIssue(
            IReadOnlyList<AreaAuthoringIssue> issues,
            string code,
            string contextId)
        {
            Assert.That(issues.Any(issue =>
                issue.Code == code
                && issue.ContextId == contextId
                && issue.IsError), Is.True);
        }
    }
}
