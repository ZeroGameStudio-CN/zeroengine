using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphRuntimeSessionResidencyTests
    {
        [Test]
        public void AcquireCellResidency_BeforeLoad_ThrowsInvalidOperationException()
        {
            var graph = CreateGraph();
            try
            {
                var session = CreateSession(graph);

                Assert.Throws<InvalidOperationException>(() =>
                    session.AcquireCellResidency("cell.start", "camera-visible"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task AcquireCellResidency_LoadedSession_TracksAndReleasesReason()
        {
            var graph = CreateGraph();
            try
            {
                var session = CreateSession(graph);
                var loadResult = await session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadResult.Succeeded, Is.True);

                var lease = session.AcquireCellResidency("  cell.start  ", "  camera-visible  ");
                Assert.That(
                    session.Snapshot.PinnedCellSummaries,
                    Does.Contain("cell.start: camera-visible"));

                lease.Dispose();
                lease.Dispose();

                Assert.That(session.Snapshot.PinnedCellSummaries, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task AcquireCellResidency_WhileStartCellLoadIsAwaiting_ThrowsInvalidOperationException()
        {
            var graph = CreateGraph();
            try
            {
                var loader = new ControlledLoader(blockLoads: true);
                var session = CreateSession(graph, loader);

                var loadTask = session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadTask.IsCompleted, Is.False);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Loading"));

                Assert.Throws<InvalidOperationException>(() =>
                    session.AcquireCellResidency("cell.start", "camera-visible"));

                loader.CompleteLoad("cell.start");
                var loadResult = await loadTask;
                Assert.That(loadResult.Succeeded, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task AcquireCellResidency_WhileUnloadIsAwaiting_ThrowsInvalidOperationException()
        {
            var graph = CreateGraph();
            try
            {
                var loader = new ControlledLoader(blockLoads: false);
                var session = CreateSession(graph, loader);
                var loadResult = await session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadResult.Succeeded, Is.True);

                var existingLease = session.AcquireCellResidency("cell.start", "camera-visible");
                loader.BlockUnloads();
                var unloadTask = session.UnloadAsync(CancellationToken.None);
                Assert.That(unloadTask.IsCompleted, Is.False);

                Assert.Throws<InvalidOperationException>(() =>
                    session.AcquireCellResidency("cell.start", "camera-visible"));

                loader.CompleteUnload("cell.start");
                var unloadResult = await unloadTask;
                Assert.That(unloadResult.Succeeded, Is.True);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Unloaded"));
                existingLease.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task AcquireCellResidency_PinnedLoadedCellConsumesStreamingBudgetUntilReleased()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new SuccessfulLoader();
                var session = CreateSession(graph, loader, maxLoadedBudgetWeight: 1);
                var loadResult = await session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadResult.Succeeded, Is.True);

                var lease = session.AcquireCellResidency("cell.start", "camera-visible");
                var protectedActivation = await session.ActivateCellAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(protectedActivation.Succeeded, Is.False);
                Assert.That(
                    protectedActivation.StreamingResult.Status,
                    Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));

                lease.Dispose();
                var activationAfterRelease = await session.ActivateCellAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(activationAfterRelease.Succeeded, Is.True);
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.target"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.target" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task AcquireCellResidency_UnknownCell_ThrowsArgumentException()
        {
            var graph = CreateGraph();
            try
            {
                var session = CreateSession(graph);
                var loadResult = await session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadResult.Succeeded, Is.True);

                Assert.Throws<ArgumentException>(() =>
                    session.AcquireCellResidency("cell.missing", "camera-visible"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static WorldGraphSO CreateGraph()
        {
            var anchor = new WorldAnchorDefinition(
                "anchor.start",
                "Start",
                WorldAnchorKind.Spawn,
                Vector3.zero,
                Vector3.forward);
            var cell = new WorldCellDefinition(
                "cell.start",
                "Start Cell",
                WorldCellKind.Outdoor,
                "scene.start",
                WorldCellLayer.Geometry,
                1,
                new[] { anchor },
                Array.Empty<WorldStreamingBoundaryDefinition>());
            var region = new WorldRegionDefinition("region.start", "Start Region", new[] { cell });
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[] { region },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private static WorldGraphSO CreateTwoCellGraph()
        {
            var startAnchor = new WorldAnchorDefinition(
                "anchor.start",
                "Start",
                WorldAnchorKind.Spawn,
                Vector3.zero,
                Vector3.forward);
            var startCell = new WorldCellDefinition(
                "cell.start",
                "Start Cell",
                WorldCellKind.Outdoor,
                "scene.start",
                WorldCellLayer.Geometry,
                1,
                new[] { startAnchor },
                new[]
                {
                    new WorldStreamingBoundaryDefinition(
                        "boundary.target",
                        new[] { "cell.target" })
                });
            var targetCell = new WorldCellDefinition(
                "cell.target",
                "Target Cell",
                WorldCellKind.Outdoor,
                "scene.target",
                WorldCellLayer.Geometry,
                1,
                Array.Empty<WorldAnchorDefinition>(),
                Array.Empty<WorldStreamingBoundaryDefinition>());
            var region = new WorldRegionDefinition(
                "region.start",
                "Start Region",
                new[] { startCell, targetCell });
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[] { region },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private static WorldGraphRuntimeSession CreateSession(
            WorldGraphSO graph,
            IWorldCellLoader loader = null,
            int maxLoadedBudgetWeight = 4)
        {
            return new WorldGraphRuntimeSession(
                graph,
                loader ?? new SuccessfulLoader(),
                new SuccessfulActor(),
                new RecordingLocationStore(),
                new WorldGraphRuntimeSessionOptions(
                    "world.test",
                    "cell.start",
                    "anchor.start",
                    maxLoadedBudgetWeight,
                    TimeSpan.Zero,
                    loadBoundaryCells: false));
        }

        private sealed class SuccessfulLoader : IWorldCellLoader
        {
            public Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }
        }

        private sealed class ControlledLoader : IWorldCellLoader
        {
            private TaskCompletionSource<WorldCellOperationResult> _loadCompletion;
            private TaskCompletionSource<WorldCellOperationResult> _unloadCompletion;

            public ControlledLoader(bool blockLoads)
            {
                if (blockLoads)
                {
                    _loadCompletion = CreateCompletionSource();
                }
            }

            public Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                return _loadCompletion?.Task
                       ?? Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                return _unloadCompletion?.Task
                       ?? Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public void BlockUnloads()
            {
                _unloadCompletion = CreateCompletionSource();
            }

            public void CompleteLoad(string cellId)
            {
                _loadCompletion.SetResult(WorldCellOperationResult.SucceededResult(cellId));
            }

            public void CompleteUnload(string cellId)
            {
                _unloadCompletion.SetResult(WorldCellOperationResult.SucceededResult(cellId));
            }

            private static TaskCompletionSource<WorldCellOperationResult> CreateCompletionSource()
            {
                return new TaskCompletionSource<WorldCellOperationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private sealed class SuccessfulActor : IWorldGraphRuntimeActor
        {
            public bool HasActor => true;

            public bool TryPlaceAtAnchor(
                WorldCellDefinition cell,
                WorldAnchorDefinition anchor,
                WorldPosition resolvedPosition)
            {
                return true;
            }

            public bool TryPlaceAtPosition(WorldPosition position)
            {
                return true;
            }

            public bool TryPlaceAtLocation(
                WorldCellDefinition cell,
                WorldGraphRuntimeLocation location)
            {
                return true;
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
            public void Save(WorldGraphRuntimeLocation location)
            {
            }

            public void Clear()
            {
            }
        }
    }
}
