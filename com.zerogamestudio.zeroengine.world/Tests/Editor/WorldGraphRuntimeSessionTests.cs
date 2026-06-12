using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphRuntimeSessionTests
    {
        [Test]
        public async Task LoadStart_ActivatesStartCellPlacesActorRecordsLocationAndPublishesSnapshot()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            var result = await session.LoadStartAsync(CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Loaded));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.LoadedCellIds);
            Assert.That(actor.PlaceAtAnchorCount, Is.EqualTo(1));
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.LastLocation.CellId, Is.EqualTo("cell.street"));
            Assert.That(store.LastLocation.AnchorId, Is.EqualTo("anchor.street.spawn"));
            Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
            Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.street"));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, session.Snapshot.LoadedCellIds);
        }

        [Test]
        public async Task LoadStart_WhenGraphIdMismatches_FailsBeforeStreaming()
        {
            var graph = TestGraphFactory.CreateGraph("world.other");
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            var result = await session.LoadStartAsync(CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.GraphMismatch));
            Assert.That(loader.LoadedCellIds, Is.Empty);
            Assert.That(loader.BulkUnloadCount, Is.EqualTo(0));
            Assert.That(actor.PlaceAtAnchorCount, Is.EqualTo(0));
            Assert.That(store.SaveCount, Is.EqualTo(0));
            Assert.That(session.Snapshot.LastFailure, Is.EqualTo(WorldGraphRuntimeSessionStatus.GraphMismatch.ToString()));
        }

        [Test]
        public async Task Travel_WhenActorPlacementFails_UnloadsAndDoesNotRecordLocation()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            await session.LoadStartAsync(CancellationToken.None);
            actor.FailPlaceAtPosition = true;
            var result = await session.TravelAsync(
                "link.street.apothecary.enter",
                "anchor.street.apothecary.outside",
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.ActorMissing));
            Assert.That(loader.BulkUnloadCount, Is.EqualTo(1));
            Assert.That(store.SaveCount, Is.EqualTo(1), "Failed travel must not overwrite the start save location.");
            Assert.That(store.ClearCount, Is.EqualTo(1), "Cleanup after failed placement must clear the runtime location store.");
            Assert.That(session.Snapshot.LastFailure, Is.EqualTo(WorldGraphRuntimeSessionStatus.ActorMissing.ToString()));
        }

        [Test]
        public async Task ActivateCell_WhenBoundaryIsMissing_FailsBeforeStreaming()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            await session.LoadStartAsync(CancellationToken.None);
            var loadedBefore = loader.LoadedCellIds.Count;
            var result = await session.ActivateCellAsync(
                "cell.street",
                "cell.wild",
                "boundary.missing",
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingBoundaryMissing));
            Assert.That(loader.LoadedCellIds.Count, Is.EqualTo(loadedBefore));
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(session.Snapshot.LastFailure, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingBoundaryMissing.ToString()));
        }

        [Test]
        public async Task Restore_ActivatesSavedCellPlacesActorAndRecordsResolvedLocation()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);
            var location = new WorldGraphRuntimeLocation(
                "world.test",
                "region.test",
                "cell.wild",
                "anchor.wild.spawn",
                "Wild",
                new Vector3(12f, 0f, 0f),
                new Vector3(1f, 0f, 2f),
                Quaternion.Euler(0f, 90f, 0f),
                new Vector3(13f, 0f, 2f),
                Quaternion.Euler(0f, 90f, 0f));

            var result = await session.RestoreAsync(location, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Loaded));
            Assert.That(actor.PlaceAtLocationCount, Is.EqualTo(1));
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.LastLocation.CellId, Is.EqualTo("cell.wild"));
            Assert.That(store.LastLocation.AnchorId, Is.EqualTo("anchor.wild.spawn"));
            Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
            Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.wild"));
        }

        [Test]
        public async Task ConcurrentOperations_ReturnBusyWithoutMutatingStreamingState()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader(blockLoads: true);
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            var firstLoad = session.LoadStartAsync(CancellationToken.None);
            await loader.WaitForLoadAttemptAsync();

            var busyResult = await session.LoadStartAsync(CancellationToken.None);
            loader.ReleaseBlockedLoads();
            var firstResult = await firstLoad;

            Assert.That(busyResult.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Busy));
            Assert.That(firstResult.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Loaded));
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Unload_ClearsStreamingServicesAndLocationStore()
        {
            var graph = TestGraphFactory.CreateGraph();
            var loader = new RecordingCellLoader();
            var actor = new RecordingRuntimeActor();
            var store = new RecordingLocationStore();
            var session = CreateSession(graph, loader, actor, store);

            await session.LoadStartAsync(CancellationToken.None);
            var result = await session.UnloadAsync(CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Unloaded));
            Assert.That(loader.BulkUnloadCount, Is.EqualTo(1));
            Assert.That(loader.LoadedCellIds, Is.Empty);
            Assert.That(store.ClearCount, Is.EqualTo(1));
            Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Unloaded"));
            Assert.That(session.Snapshot.ActiveCellId, Is.Empty);
        }

        private static WorldGraphRuntimeSession CreateSession(
            WorldGraphSO graph,
            RecordingCellLoader loader,
            RecordingRuntimeActor actor,
            RecordingLocationStore store)
        {
            return new WorldGraphRuntimeSession(
                graph,
                loader,
                actor,
                store,
                new WorldGraphRuntimeSessionOptions(
                    "world.test",
                    "cell.street",
                    "anchor.street.spawn",
                    maxLoadedBudgetWeight: 12,
                    minimumCellResidency: TimeSpan.Zero));
        }

        private static class TestGraphFactory
        {
            public static WorldGraphSO CreateGraph(string worldGraphId = "world.test")
            {
                var street = new WorldCellDefinition(
                    "cell.street",
                    "Street",
                    WorldCellKind.Outdoor,
                    "StreetScene",
                    Vector3.zero,
                    WorldCellLayer.All,
                    1,
                    new[]
                    {
                        new WorldAnchorDefinition(
                            "anchor.street.spawn",
                            "Street Spawn",
                            WorldAnchorKind.Spawn,
                            Vector3.zero,
                            Vector3.forward),
                        new WorldAnchorDefinition(
                            "anchor.street.apothecary.outside",
                            "Apothecary Outside",
                            WorldAnchorKind.InteriorEntry,
                            new Vector3(2f, 0f, 0f),
                            Vector3.forward)
                    },
                    new[]
                    {
                        new WorldStreamingBoundaryDefinition(
                            "boundary.street.wild",
                            new[] { "cell.wild" })
                    });
                var wild = new WorldCellDefinition(
                    "cell.wild",
                    "Wild",
                    WorldCellKind.Outdoor,
                    "WildScene",
                    new Vector3(12f, 0f, 0f),
                    WorldCellLayer.All,
                    1,
                    new[]
                    {
                        new WorldAnchorDefinition(
                            "anchor.wild.spawn",
                            "Wild Spawn",
                            WorldAnchorKind.RoadExit,
                            new Vector3(1f, 0f, 2f),
                            Vector3.back)
                    },
                    Array.Empty<WorldStreamingBoundaryDefinition>());
                var apothecary = new WorldCellDefinition(
                    "cell.apothecary",
                    "Apothecary",
                    WorldCellKind.Interior,
                    "ApothecaryScene",
                    new Vector3(4f, 0f, 0f),
                    WorldCellLayer.All,
                    1,
                    new[]
                    {
                        new WorldAnchorDefinition(
                            "anchor.apothecary.inside",
                            "Apothecary Inside",
                            WorldAnchorKind.InteriorEntry,
                            new Vector3(1f, 0f, 2f),
                            Vector3.back)
                    },
                    Array.Empty<WorldStreamingBoundaryDefinition>());

                var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
                graph.ConfigureForTests(
                    worldGraphId,
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.test",
                            "Test Region",
                            new[] { street, wild, apothecary })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.apothecary.enter",
                            "anchor.street.apothecary.outside",
                            "anchor.apothecary.inside",
                            WorldTravelMode.SeamlessInterior,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());
                return graph;
            }
        }

        private sealed class RecordingCellLoader : IWorldCellLoader, IWorldCellBulkUnloader
        {
            private readonly bool _blockLoads;
            private readonly List<string> _loadedCellIds = new List<string>();
            private readonly TaskCompletionSource<bool> _loadAttempt =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _loadRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public RecordingCellLoader(bool blockLoads = false)
            {
                _blockLoads = blockLoads;
                if (!blockLoads)
                {
                    _loadRelease.TrySetResult(true);
                }
            }

            public IReadOnlyList<string> LoadedCellIds => _loadedCellIds;
            public int BulkUnloadCount { get; private set; }

            public async Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                _loadAttempt.TrySetResult(true);
                if (_blockLoads)
                {
                    await _loadRelease.Task;
                }

                _loadedCellIds.Add(cell.CellId);
                return WorldCellOperationResult.SucceededResult(cell.CellId);
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                _loadedCellIds.Remove(cell.CellId);
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public Task<WorldCellOperationResult> UnloadAllAsync(CancellationToken cancellationToken)
            {
                BulkUnloadCount++;
                _loadedCellIds.Clear();
                return Task.FromResult(WorldCellOperationResult.SucceededResult(null));
            }

            public Task WaitForLoadAttemptAsync()
            {
                return _loadAttempt.Task;
            }

            public void ReleaseBlockedLoads()
            {
                _loadRelease.TrySetResult(true);
            }
        }

        private sealed class RecordingRuntimeActor : IWorldGraphRuntimeActor
        {
            public bool HasActor { get; set; } = true;
            public bool FailPlaceAtPosition { get; set; }
            public int PlaceAtAnchorCount { get; private set; }
            public int PlaceAtPositionCount { get; private set; }
            public int PlaceAtLocationCount { get; private set; }

            public bool TryPlaceAtAnchor(
                WorldCellDefinition cell,
                WorldAnchorDefinition anchor,
                WorldPosition resolvedPosition)
            {
                PlaceAtAnchorCount++;
                return HasActor;
            }

            public bool TryPlaceAtPosition(WorldPosition position)
            {
                PlaceAtPositionCount++;
                return HasActor && !FailPlaceAtPosition;
            }

            public bool TryPlaceAtLocation(
                WorldCellDefinition cell,
                WorldGraphRuntimeLocation location)
            {
                PlaceAtLocationCount++;
                return HasActor;
            }

            public WorldGraphRuntimeLocation CaptureLocation(
                WorldGraphSO graph,
                WorldCellDefinition cell,
                WorldAnchorDefinition anchor,
                WorldGraphRuntimeLocation fallback)
            {
                return fallback;
            }
        }

        private sealed class RecordingLocationStore : IWorldGraphRuntimeLocationStore
        {
            public int SaveCount { get; private set; }
            public int ClearCount { get; private set; }
            public WorldGraphRuntimeLocation LastLocation { get; private set; }

            public void Save(WorldGraphRuntimeLocation location)
            {
                SaveCount++;
                LastLocation = location;
            }

            public void Clear()
            {
                ClearCount++;
                LastLocation = default;
            }
        }
    }
}
