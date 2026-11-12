using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    [Category("Unit")]
    public sealed class WorldStreamingServicePreparationTests
    {
        [Test]
        [Category("Boundary")]
        public void ExistingResultStatuses_PreservePublishedNumericValues()
        {
            Assert.That((int)WorldCellOperationStatus.Succeeded, Is.Zero);
            Assert.That((int)WorldCellOperationStatus.Failed, Is.EqualTo(1));
            Assert.That((int)WorldCellOperationStatus.Cancelled, Is.EqualTo(2));
            Assert.That((int)WorldCellReadinessStatus.Succeeded, Is.Zero);
            Assert.That((int)WorldCellReadinessStatus.Failed, Is.EqualTo(1));
            Assert.That((int)WorldCellReadinessStatus.Cancelled, Is.EqualTo(2));
            Assert.That((int)WorldStreamingResultStatus.Succeeded, Is.Zero);
            Assert.That((int)WorldStreamingResultStatus.GraphMissing, Is.EqualTo(1));
            Assert.That((int)WorldStreamingResultStatus.CellNotFound, Is.EqualTo(2));
            Assert.That((int)WorldStreamingResultStatus.LoaderFailed, Is.EqualTo(3));
            Assert.That((int)WorldStreamingResultStatus.ReadinessFailed, Is.EqualTo(4));
            Assert.That((int)WorldStreamingResultStatus.BudgetExceeded, Is.EqualTo(5));
            Assert.That((int)WorldStreamingResultStatus.Cancelled, Is.EqualTo(6));
            Assert.That((int)WorldStreamingResultStatus.Busy, Is.EqualTo(7));
        }

        [Test]
        public void DefaultOperationReadinessAndStreamingResults_FailClosed()
        {
            Assert.That(default(WorldCellOperationResult).Status, Is.EqualTo(WorldCellOperationStatus.Unknown));
            Assert.That(default(WorldCellOperationResult).IsSuccess, Is.False);
            Assert.That(default(WorldCellReadinessResult).Status, Is.EqualTo(WorldCellReadinessStatus.Unknown));
            Assert.That(default(WorldCellReadinessResult).IsSuccess, Is.False);
            Assert.That(default(WorldStreamingResult).Status, Is.EqualTo(WorldStreamingResultStatus.Unknown));
            Assert.That(default(WorldStreamingResult).Succeeded, Is.False);
        }

        [Test]
        public async Task EnsureCellLoadedAsync_DifferentLayerSetForLoadedCell_FailsClosedWithoutReloading()
        {
            var graph = CreateGraph();
            try
            {
                var loader = new CountingLoader();
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 2,
                    minimumCellResidency: TimeSpan.Zero);

                var first = await streaming.EnsureCellLoadedAsync(
                    "cell.layered",
                    WorldCellLayer.Geometry,
                    CancellationToken.None);
                var mismatch = await streaming.EnsureCellLoadedAsync(
                    "cell.layered",
                    WorldCellLayer.Audio,
                    CancellationToken.None);

                Assert.That(first.Succeeded, Is.True);
                Assert.That(mismatch.Status, Is.EqualTo(WorldStreamingResultStatus.LayerMismatch));
                Assert.That(mismatch.Message, Does.Contain("explicit reload"));
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                Assert.That(streaming.ActiveCellId, Is.Null);
                Assert.That(streaming.LoadedCellIds, Is.EquivalentTo(new[] { "cell.layered" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_TargetHasNeighbor_PreparesCompleteWindowWithoutActivatingOrUnloadingSource()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new CountingLoader();
                var readiness = new CountingReadinessService();
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 3,
                    minimumCellResidency: TimeSpan.Zero,
                    readinessService: readiness);
                Assert.That((await streaming.ActivateCellAsync(
                    "cell.start",
                    WorldCellLayer.Geometry,
                    loadBoundaryCells: false,
                    CancellationToken.None)).Succeeded, Is.True);

                var result = await streaming.EnsureCellLoadedAsync(
                    "cell.target",
                    WorldCellLayer.Geometry | WorldCellLayer.Audio,
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    streaming.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(loader.GetLoadCount("cell.start"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(loader.GetUnloadCount("cell.start"), Is.Zero);
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_CurrentLoadedUnionWithTargetWindowExceedsBudget_FailsBeforeLoadingWindow()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new CountingLoader();
                var readiness = new CountingReadinessService();
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 2,
                    minimumCellResidency: TimeSpan.Zero,
                    readinessService: readiness);
                Assert.That((await streaming.ActivateCellAsync(
                    "cell.start",
                    WorldCellLayer.Geometry,
                    loadBoundaryCells: false,
                    CancellationToken.None)).Succeeded, Is.True);

                var result = await streaming.EnsureCellLoadedAsync(
                    "cell.target",
                    WorldCellLayer.Geometry | WorldCellLayer.Audio,
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
                Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(streaming.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));
                Assert.That(loader.GetLoadCount("cell.target"), Is.Zero);
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.Zero);
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.Zero);
                Assert.That(readiness.GetPrepareCount("cell.neighbor"), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_TargetNeighborReadinessFails_RollsBackOnlyNewWindowAndKeepsPinnedSource()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new CountingLoader();
                var readiness = new CountingReadinessService
                {
                    FailedCellId = "cell.neighbor"
                };
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 3,
                    minimumCellResidency: TimeSpan.Zero,
                    readinessService: readiness);
                Assert.That((await streaming.ActivateCellAsync(
                    "cell.start",
                    WorldCellLayer.Geometry,
                    loadBoundaryCells: false,
                    CancellationToken.None)).Succeeded, Is.True);
                var sourceResidency = streaming.AcquireCellPin("cell.start", "active-source");

                var result = await streaming.EnsureCellLoadedAsync(
                    "cell.target",
                    WorldCellLayer.Geometry | WorldCellLayer.Audio,
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.ReadinessFailed));
                Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(streaming.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));
                Assert.That(streaming.GetCellPinReasons("cell.start"), Does.Contain("active-source"));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(loader.GetUnloadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(loader.GetUnloadCount("cell.start"), Is.Zero);

                sourceResidency.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_AfterTargetLeaseRelease_UnloadsOnlyOutsideActiveWindow()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new CountingLoader();
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 3,
                    minimumCellResidency: TimeSpan.Zero);
                Assert.That((await streaming.ActivateCellAsync(
                    "cell.start",
                    WorldCellLayer.Geometry,
                    loadBoundaryCells: false,
                    CancellationToken.None)).Succeeded, Is.True);

                var targetWindowLease = streaming.AcquireCellPin("cell.target", "door-preload");
                Assert.That((await streaming.EnsureCellLoadedAsync(
                    "cell.target",
                    WorldCellLayer.Geometry | WorldCellLayer.Audio,
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None)).Succeeded, Is.True);
                Assert.That(
                    streaming.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));

                targetWindowLease.Dispose();
                var result = await streaming.ReconcileActiveWindowAsync(
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    streaming.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(loader.GetUnloadCount("cell.start"), Is.Zero);
                Assert.That(loader.GetUnloadCount("cell.target"), Is.Zero);
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_CameraPinnedOutsideCell_PreservesItUntilPinRelease()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new CountingLoader();
                var streaming = new WorldStreamingService(
                    graph,
                    loader,
                    maxLoadedBudgetWeight: 3,
                    minimumCellResidency: TimeSpan.Zero);
                Assert.That((await streaming.ActivateCellAsync(
                    "cell.start",
                    WorldCellLayer.Geometry,
                    loadBoundaryCells: false,
                    CancellationToken.None)).Succeeded, Is.True);
                Assert.That((await streaming.EnsureCellLoadedAsync(
                    "cell.target",
                    WorldCellLayer.Geometry | WorldCellLayer.Audio,
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None)).Succeeded, Is.True);
                var cameraLease = streaming.AcquireCellPin("cell.neighbor", "camera-visible");

                var pinnedResult = await streaming.ReconcileActiveWindowAsync(
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(pinnedResult.Succeeded, Is.True);
                Assert.That(
                    streaming.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.Zero);

                cameraLease.Dispose();
                var releasedResult = await streaming.ReconcileActiveWindowAsync(
                    loadBoundaryCells: true,
                    cancellationToken: CancellationToken.None);

                Assert.That(releasedResult.Succeeded, Is.True);
                Assert.That(
                    streaming.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static WorldGraphSO CreateGraph()
        {
            var cell = new WorldCellDefinition(
                "cell.layered",
                "Layered Cell",
                WorldCellKind.Interior,
                "scene.layered",
                WorldCellLayer.Geometry | WorldCellLayer.Audio,
                1,
                Array.Empty<WorldAnchorDefinition>(),
                Array.Empty<WorldStreamingBoundaryDefinition>());
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[] { new WorldRegionDefinition("region.test", "Test", new[] { cell }) },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private static WorldGraphSO CreateThreeCellGraph()
        {
            var startCell = new WorldCellDefinition(
                "cell.start",
                "Start Cell",
                WorldCellKind.Outdoor,
                "scene.start",
                WorldCellLayer.Geometry,
                1,
                Array.Empty<WorldAnchorDefinition>(),
                new[]
                {
                    new WorldStreamingBoundaryDefinition(
                        "boundary.target",
                        new[] { "cell.target" })
                });
            var targetCell = new WorldCellDefinition(
                "cell.target",
                "Target Cell",
                WorldCellKind.Interior,
                "scene.target",
                WorldCellLayer.Geometry | WorldCellLayer.Audio,
                1,
                Array.Empty<WorldAnchorDefinition>(),
                new[]
                {
                    new WorldStreamingBoundaryDefinition(
                        "boundary.neighbor",
                        new[] { "cell.neighbor" })
                });
            var neighborCell = new WorldCellDefinition(
                "cell.neighbor",
                "Target Neighbor",
                WorldCellKind.Interior,
                "scene.neighbor",
                WorldCellLayer.Geometry,
                1,
                Array.Empty<WorldAnchorDefinition>(),
                Array.Empty<WorldStreamingBoundaryDefinition>());
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[]
                {
                    new WorldRegionDefinition(
                        "region.test",
                        "Test",
                        new[] { startCell, targetCell, neighborCell })
                },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private sealed class CountingLoader : IWorldCellLoader
        {
            private readonly Dictionary<string, int> _loadCounts = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _unloadCounts = new Dictionary<string, int>();

            public int LoadCount { get; private set; }

            public Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                LoadCount++;
                Increment(_loadCounts, cell.CellId);
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                Increment(_unloadCounts, cell.CellId);
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public int GetLoadCount(string cellId)
            {
                return GetCount(_loadCounts, cellId);
            }

            public int GetUnloadCount(string cellId)
            {
                return GetCount(_unloadCounts, cellId);
            }
        }

        private sealed class CountingReadinessService : IWorldCellReadinessService
        {
            private readonly Dictionary<string, int> _prepareCounts = new Dictionary<string, int>();

            public string FailedCellId { get; set; }

            public Task<WorldCellReadinessResult> PrepareCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                Increment(_prepareCounts, cell.CellId);
                return Task.FromResult(cell.CellId == FailedCellId
                    ? WorldCellReadinessResult.Failed(cell.CellId, "Synthetic readiness failure.")
                    : WorldCellReadinessResult.SucceededResult(cell.CellId));
            }

            public int GetPrepareCount(string cellId)
            {
                return GetCount(_prepareCounts, cellId);
            }
        }

        private static void Increment(IDictionary<string, int> counts, string cellId)
        {
            counts[cellId] = counts.TryGetValue(cellId, out var count) ? count + 1 : 1;
        }

        private static int GetCount(IReadOnlyDictionary<string, int> counts, string cellId)
        {
            return counts.TryGetValue(cellId, out var count) ? count : 0;
        }
    }
}
