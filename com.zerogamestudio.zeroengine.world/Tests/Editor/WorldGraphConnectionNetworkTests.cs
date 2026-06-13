using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph.Tests
{
    public sealed class WorldGraphConnectionNetworkTests
    {
        [Test]
        public void TryFindWalkableConnection_ByBoundary_ReturnsCrossGraphTarget()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[]
            {
                new WorldGraphConnectionDefinition(
                    "connection.map_a.map_b.walk",
                    "world.map_a",
                    "cell.map_a.exit",
                    "boundary.map_a.to_map_b",
                    "anchor.map_a.exit",
                    "world.map_b",
                    "WorldGraph/MapB",
                    "cell.map_b.entrance",
                    "anchor.map_b.entrance",
                    WorldTravelMode.SeamlessWalk)
            });

            Assert.True(network.TryFindByBoundary(
                "world.map_a",
                "cell.map_a.exit",
                "boundary.map_a.to_map_b",
                out var connection));

            Assert.NotNull(connection);
            Assert.AreEqual("world.map_b", connection.TargetWorldGraphId);
            Assert.AreEqual("WorldGraph/MapB", connection.TargetWorldGraphAddress);
            Assert.AreEqual(WorldTravelMode.SeamlessWalk, connection.TravelMode);
            Assert.True(connection.IsWalkable);
        }

        [Test]
        public void TryFindWalkableConnection_IgnoresFastTravelSemantics()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[]
            {
                new WorldGraphConnectionDefinition(
                    "connection.fast",
                    "world.map_a",
                    "cell.map_a.exit",
                    "boundary.map_a.fast",
                    "anchor.map_a.fast",
                    "world.map_b",
                    "WorldGraph/MapB",
                    "cell.map_b.entrance",
                    "anchor.map_b.entrance",
                    WorldTravelMode.FastTravel)
            });

            Assert.True(network.TryFindByBoundary(
                "world.map_a",
                "cell.map_a.exit",
                "boundary.map_a.fast",
                out var connection));
            Assert.False(connection.IsWalkable);
        }
    }
}
