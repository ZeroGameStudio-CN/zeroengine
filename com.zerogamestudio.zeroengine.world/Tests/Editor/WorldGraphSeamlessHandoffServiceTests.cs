using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph.Tests
{
    public sealed class WorldGraphSeamlessHandoffServiceTests
    {
        [Test]
        public async Task HandoffAsync_WalkableBoundary_LoadsTargetBeforeUnloadingSource()
        {
            var network = CreateNetwork(WorldTravelMode.SeamlessWalk);
            var host = new FakeWorldGraphRuntimeHost("world.map_a");
            var service = new WorldGraphSeamlessHandoffService(network, host);

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.map_a.to_map_b"),
                CancellationToken.None);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffCompleted, result.Status);
            CollectionAssert.AreEqual(
                new[] { "load:world.map_b", "switch:world.map_b", "unload:world.map_a" },
                host.Calls);
        }

        [Test]
        public async Task HandoffAsync_MissingConnection_ReturnsMissing()
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            var service = new WorldGraphSeamlessHandoffService(
                network,
                new FakeWorldGraphRuntimeHost("world.map_a"));

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.missing"),
                CancellationToken.None);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffConnectionMissing, result.Status);
        }

        [Test]
        public async Task HandoffAsync_FastTravelConnection_ReturnsNotWalkable()
        {
            var network = CreateNetwork(WorldTravelMode.FastTravel);
            var service = new WorldGraphSeamlessHandoffService(
                network,
                new FakeWorldGraphRuntimeHost("world.map_a"));

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.map_a.to_map_b"),
                CancellationToken.None);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffConnectionNotWalkable, result.Status);
        }

        [Test]
        public async Task HandoffAsync_TargetLoadFails_ReturnsTargetLoadFailed()
        {
            var network = CreateNetwork(WorldTravelMode.SeamlessWalk);
            var host = new FakeWorldGraphRuntimeHost("world.map_a")
            {
                LoadResult = WorldGraphRuntimeSessionStatus.GraphMissing
            };
            var service = new WorldGraphSeamlessHandoffService(network, host);

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.map_a.to_map_b"),
                CancellationToken.None);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffTargetLoadFailed, result.Status);
            CollectionAssert.AreEqual(new[] { "load:world.map_b" }, host.Calls);
        }

        [Test]
        public async Task HandoffAsync_SourceUnloadFails_ReturnsUnloadFailed()
        {
            var network = CreateNetwork(WorldTravelMode.SeamlessWalk);
            var host = new FakeWorldGraphRuntimeHost("world.map_a")
            {
                UnloadResult = WorldGraphRuntimeSessionStatus.UnloadFailed
            };
            var service = new WorldGraphSeamlessHandoffService(network, host);

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.map_a.to_map_b"),
                CancellationToken.None);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.UnloadFailed, result.Status);
            CollectionAssert.AreEqual(
                new[] { "load:world.map_b", "switch:world.map_b", "unload:world.map_a" },
                host.Calls);
        }

        [Test]
        public async Task HandoffAsync_CancellationAfterSwitch_DoesNotCancelSourceCleanup()
        {
            var network = CreateNetwork(WorldTravelMode.SeamlessWalk);
            using var cts = new CancellationTokenSource();
            var host = new FakeWorldGraphRuntimeHost("world.map_a")
            {
                AfterSwitch = cts.Cancel
            };
            var service = new WorldGraphSeamlessHandoffService(network, host);

            var result = await service.HandoffAsync(
                new WorldGraphHandoffRequest("world.map_a", "cell.map_a.exit", "boundary.map_a.to_map_b"),
                cts.Token);

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffCompleted, result.Status);
            Assert.False(host.UnloadTokenCanBeCanceled, "Source cleanup after active graph switch must not be externally cancellable.");
            CollectionAssert.AreEqual(
                new[] { "load:world.map_b", "switch:world.map_b", "unload:world.map_a" },
                host.Calls);
        }

        [Test]
        public async Task HandoffAsync_ConcurrentRequest_ReturnsBusy()
        {
            var network = CreateNetwork(WorldTravelMode.SeamlessWalk);
            var loadGate = new TaskCompletionSource<bool>();
            var host = new FakeWorldGraphRuntimeHost("world.map_a") { LoadGate = loadGate };
            var service = new WorldGraphSeamlessHandoffService(network, host);
            var request = new WorldGraphHandoffRequest(
                "world.map_a",
                "cell.map_a.exit",
                "boundary.map_a.to_map_b");

            var first = service.HandoffAsync(request, CancellationToken.None);
            var busy = await service.HandoffAsync(request, CancellationToken.None);
            loadGate.SetResult(true);
            var completed = await first;

            Assert.AreEqual(WorldGraphRuntimeSessionStatus.Busy, busy.Status);
            Assert.AreEqual(WorldGraphRuntimeSessionStatus.HandoffCompleted, completed.Status);
        }

        private static WorldGraphConnectionNetworkSO CreateNetwork(WorldTravelMode mode)
        {
            var network = ScriptableObject.CreateInstance<WorldGraphConnectionNetworkSO>();
            network.ConfigureForTests(new[] { CreateConnection(mode) });
            return network;
        }

        private static WorldGraphConnectionDefinition CreateConnection(WorldTravelMode mode)
        {
            return new WorldGraphConnectionDefinition(
                "connection.map_a.map_b.walk",
                "world.map_a",
                "cell.map_a.exit",
                "boundary.map_a.to_map_b",
                "anchor.map_a.exit",
                "world.map_b",
                "WorldGraph/MapB",
                "cell.map_b.entrance",
                "anchor.map_b.entrance",
                mode);
        }

        private sealed class FakeWorldGraphRuntimeHost : IWorldGraphRuntimeHost
        {
            public FakeWorldGraphRuntimeHost(string activeWorldGraphId)
            {
                ActiveWorldGraphId = activeWorldGraphId;
            }

            public string ActiveWorldGraphId { get; private set; }
            public List<string> Calls { get; } = new();
            public WorldGraphRuntimeSessionStatus LoadResult { get; set; } = WorldGraphRuntimeSessionStatus.Loaded;
            public WorldGraphRuntimeSessionStatus SwitchResult { get; set; } = WorldGraphRuntimeSessionStatus.Loaded;
            public WorldGraphRuntimeSessionStatus UnloadResult { get; set; } = WorldGraphRuntimeSessionStatus.Unloaded;
            public TaskCompletionSource<bool> LoadGate { get; set; }
            public Action AfterSwitch { get; set; }
            public bool UnloadTokenCanBeCanceled { get; private set; }

            public async Task<WorldGraphRuntimeSessionResult> LoadTargetAsync(
                WorldGraphConnectionDefinition connection,
                CancellationToken cancellationToken)
            {
                Calls.Add($"load:{connection.TargetWorldGraphId}");
                if (LoadGate != null)
                {
                    await LoadGate.Task;
                }

                return new WorldGraphRuntimeSessionResult(LoadResult);
            }

            public Task<WorldGraphRuntimeSessionResult> SwitchActiveGraphAsync(
                WorldGraphConnectionDefinition connection,
                CancellationToken cancellationToken)
            {
                Calls.Add($"switch:{connection.TargetWorldGraphId}");
                ActiveWorldGraphId = connection.TargetWorldGraphId;
                AfterSwitch?.Invoke();
                return Task.FromResult(new WorldGraphRuntimeSessionResult(SwitchResult));
            }

            public Task<WorldGraphRuntimeSessionResult> UnloadSourceAsync(
                WorldGraphConnectionDefinition connection,
                CancellationToken cancellationToken)
            {
                Calls.Add($"unload:{connection.SourceWorldGraphId}");
                UnloadTokenCanBeCanceled = cancellationToken.CanBeCanceled;
                return Task.FromResult(new WorldGraphRuntimeSessionResult(UnloadResult));
            }
        }
    }
}
