using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphRuntimeSessionPreparationTests
    {
        [Test]
        public async Task EnsureCellLoadedAsync_BeforeSessionIsExploring_ReturnsNotLoaded()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(graph, loader, new ControlledReadinessService());

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.NotLoaded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Unloaded"));
                Assert.That(loader.GetLoadCount("cell.target"), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_BeforeSessionIsExploring_ReturnsNotLoaded()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(
                    graph,
                    loader,
                    new ControlledReadinessService(),
                    loadBoundaryCells: true);

                var result = await session.ReconcileActiveWindowAsync(CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.NotLoaded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Unloaded"));
                Assert.That(session.Snapshot.ActiveCellId, Is.Empty);
                Assert.That(session.Snapshot.LoadedCellIds, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_PreparesPinnedTargetWithoutChangingActiveCell_ThenActivationReusesLoad()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService();
                var actor = new RecordingActor();
                var locationStore = new RecordingLocationStore();
                var session = CreateSession(graph, loader, readiness, actor, locationStore);

                var loadResult = await session.LoadStartAsync(CancellationToken.None);
                Assert.That(loadResult.Succeeded, Is.True);
                var placementCountBeforePrepare = actor.PlacementCount;
                var saveCountBeforePrepare = locationStore.SaveCount;

                var residency = session.AcquireCellResidency("cell.target", "door-preload");
                var prepareResult = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(prepareResult.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Loaded));
                Assert.That(prepareResult.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(session.Snapshot.PinnedCellSummaries, Does.Contain("cell.target: door-preload"));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.EqualTo(1));
                Assert.That(actor.PlacementCount, Is.EqualTo(placementCountBeforePrepare));
                Assert.That(locationStore.SaveCount, Is.EqualTo(saveCountBeforePrepare));

                var activationResult = await session.ActivateCellAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(activationResult.Succeeded, Is.True);
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.target"));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.EqualTo(1));

                residency.Dispose();
                residency.Dispose();
                Assert.That(session.Snapshot.PinnedCellSummaries, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_TargetHasNeighbor_PreparesActivationWindowAndActivationReusesEveryLoad()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService();
                var actor = new RecordingActor();
                var locationStore = new RecordingLocationStore();
                var session = CreateSession(
                    graph,
                    loader,
                    readiness,
                    actor,
                    locationStore,
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.Zero);
                var placementCountBeforePrepare = actor.PlacementCount;
                var saveCountBeforePrepare = locationStore.SaveCount;

                var prepareResult = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(prepareResult.Succeeded, Is.True);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(actor.PlacementCount, Is.EqualTo(placementCountBeforePrepare));
                Assert.That(locationStore.SaveCount, Is.EqualTo(saveCountBeforePrepare));

                var activationResult = await session.ActivateCellAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(activationResult.Succeeded, Is.True);
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.target"));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.EqualTo(1));
                Assert.That(readiness.GetPrepareCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_AfterTargetWindowLeaseRelease_UnloadsOnlyOutsideActiveWindow()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(
                    graph,
                    loader,
                    new ControlledReadinessService(),
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var targetWindowLease = session.AcquireCellResidency("cell.target", "door-preload");
                Assert.That((await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None)).Succeeded, Is.True);
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(
                    session.Snapshot.PinnedCellSummaries,
                    Does.Contain("cell.target: door-preload"));

                targetWindowLease.Dispose();
                var result = await session.ReconcileActiveWindowAsync(CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Loaded));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(session.Snapshot.PinnedCellSummaries, Is.Empty);
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
                var loader = new TrackingLoader();
                var session = CreateSession(
                    graph,
                    loader,
                    new ControlledReadinessService(),
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);
                Assert.That((await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None)).Succeeded, Is.True);
                var cameraLease = session.AcquireCellResidency("cell.neighbor", "camera-visible");

                var pinnedResult = await session.ReconcileActiveWindowAsync(CancellationToken.None);

                Assert.That(pinnedResult.Succeeded, Is.True);
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(
                    session.Snapshot.PinnedCellSummaries,
                    Does.Contain("cell.neighbor: camera-visible"));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.Zero);

                cameraLease.Dispose();
                var releasedResult = await session.ReconcileActiveWindowAsync(CancellationToken.None);

                Assert.That(releasedResult.Succeeded, Is.True);
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_PreCancelled_PreservesLoadedStateAndRestoresExploring()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(
                    graph,
                    loader,
                    new ControlledReadinessService(),
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);
                Assert.That((await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None)).Succeeded, Is.True);
                var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                var result = await session.ReconcileActiveWindowAsync(cancellation.Token);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Cancelled));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.Cancelled));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ReconcileActiveWindowAsync_UnloadFailure_ReportsFailureWithoutChangingActiveCell()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(
                    graph,
                    loader,
                    new ControlledReadinessService(),
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);
                Assert.That((await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None)).Succeeded, Is.True);
                loader.FailedUnloadCellId = "cell.neighbor";

                var result = await session.ReconcileActiveWindowAsync(CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.LoaderFailed));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target", "cell.neighbor" }));
                Assert.That(session.Snapshot.LastFailure, Does.Contain("LoaderFailed"));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_TargetWindowUnionExceedsBudget_PreservesPreviouslyLoadedSourceAndTarget()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService();
                var session = CreateSession(
                    graph,
                    loader,
                    readiness,
                    maxLoadedBudgetWeight: 2,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(loader.GetLoadCount("cell.target"), Is.EqualTo(1));
                Assert.That(loader.GetLoadCount("cell.neighbor"), Is.Zero);
                Assert.That(readiness.GetPrepareCount("cell.neighbor"), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_TargetNeighborReadinessFails_RollsBackNeighborOnlyAndKeepsPriorWindow()
        {
            var graph = CreateThreeCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService
                {
                    FailedCellId = "cell.neighbor"
                };
                var session = CreateSession(
                    graph,
                    loader,
                    readiness,
                    maxLoadedBudgetWeight: 3,
                    loadBoundaryCells: true);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);
                var targetResidency = session.AcquireCellResidency("cell.target", "door-preload");

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.ReadinessFailed));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(
                    session.Snapshot.LoadedCellIds,
                    Is.EquivalentTo(new[] { "cell.start", "cell.target" }));
                Assert.That(session.Snapshot.PinnedCellSummaries, Does.Contain("cell.target: door-preload"));
                Assert.That(loader.GetUnloadCount("cell.neighbor"), Is.EqualTo(1));
                Assert.That(loader.GetUnloadCount("cell.target"), Is.Zero);
                Assert.That(loader.GetUnloadCount("cell.start"), Is.Zero);

                targetResidency.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_LoadedPlusTargetExceedsBudget_FailsBeforeLoadingAndRestoresExploring()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService();
                var session = CreateSession(
                    graph,
                    loader,
                    readiness,
                    new RecordingActor(),
                    new RecordingLocationStore(),
                    maxLoadedBudgetWeight: 1);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var residency = session.AcquireCellResidency("cell.target", "door-preload");
                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));
                Assert.That(loader.GetLoadCount("cell.target"), Is.Zero);
                Assert.That(readiness.GetPrepareCount("cell.target"), Is.Zero);

                residency.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_ReadinessFailure_RollsBackTargetAndKeepsCurrentWorld()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var readiness = new ControlledReadinessService
                {
                    FailedCellId = "cell.target"
                };
                var session = CreateSession(graph, loader, readiness);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.ReadinessFailed));
                Assert.That(loader.GetUnloadCount("cell.target"), Is.EqualTo(1));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_CancelledAfterReadinessSuccess_RollsBackAndRestoresExploring()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var cancellation = new CancellationTokenSource();
                var readiness = new ControlledReadinessService
                {
                    CancelOnSuccessfulCellId = "cell.target",
                    CancellationToSignal = cancellation
                };
                var session = CreateSession(graph, loader, readiness);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    cancellation.Token);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Cancelled));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.Cancelled));
                Assert.That(loader.GetUnloadCount("cell.target"), Is.EqualTo(1));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
                Assert.That(session.Snapshot.LoadedCellIds, Is.EquivalentTo(new[] { "cell.start" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_ReadinessRollbackFails_ReportsRollbackFailure()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader
                {
                    FailedUnloadCellId = "cell.target"
                };
                var readiness = new ControlledReadinessService
                {
                    FailedCellId = "cell.target"
                };
                var session = CreateSession(graph, loader, readiness);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var result = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(result.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingFailed));
                Assert.That(result.StreamingResult.Status, Is.EqualTo(WorldStreamingResultStatus.RollbackFailed));
                Assert.That(result.StreamingResult.Message, Does.Contain("Rollback failed"));
                Assert.That(session.Snapshot.LastFailure, Does.Contain("RollbackFailed"));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_WhileReadinessIsPending_RejectsCompetingOperations()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var readiness = new ControlledReadinessService
                {
                    BlockedCellId = "cell.target"
                };
                var session = CreateSession(graph, new TrackingLoader(), readiness);
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var residency = session.AcquireCellResidency("cell.target", "door-preload");
                var prepareTask = session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);

                Assert.That(prepareTask.IsCompleted, Is.False);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("StreamingPreparing"));

                var competingPrepare = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);
                var competingActivation = await session.ActivateCellAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);
                var competingReconciliation = await session.ReconcileActiveWindowAsync(
                    CancellationToken.None);

                Assert.That(competingPrepare.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Busy));
                Assert.That(competingActivation.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Busy));
                Assert.That(competingReconciliation.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.Busy));
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("StreamingPreparing"));
                Assert.Throws<InvalidOperationException>(() =>
                    session.AcquireCellResidency("cell.target", "overlapping-owner"));

                readiness.CompleteBlockedCell();
                Assert.That((await prepareTask).Succeeded, Is.True);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                residency.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task EnsureCellLoadedAsync_InvalidBoundaryOrMissingTarget_DoesNotLoadTarget()
        {
            var graph = CreateTwoCellGraph();
            try
            {
                var loader = new TrackingLoader();
                var session = CreateSession(graph, loader, new ControlledReadinessService());
                Assert.That((await session.LoadStartAsync(CancellationToken.None)).Succeeded, Is.True);

                var activeCellMismatch = await session.EnsureCellLoadedAsync(
                    "cell.other",
                    "cell.target",
                    "boundary.target",
                    CancellationToken.None);
                var invalidBoundary = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.target",
                    "boundary.missing",
                    CancellationToken.None);
                var missingTarget = await session.EnsureCellLoadedAsync(
                    "cell.start",
                    "cell.missing",
                    "boundary.missing-target",
                    CancellationToken.None);

                Assert.That(activeCellMismatch.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.ActiveCellMismatch));
                Assert.That(invalidBoundary.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.StreamingBoundaryMissing));
                Assert.That(missingTarget.Status, Is.EqualTo(WorldGraphRuntimeSessionStatus.TargetCellMissing));
                Assert.That(loader.GetLoadCount("cell.target"), Is.Zero);
                Assert.That(session.Snapshot.RuntimeState, Is.EqualTo("Exploring"));
                Assert.That(session.Snapshot.ActiveCellId, Is.EqualTo("cell.start"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static WorldGraphRuntimeSession CreateSession(
            WorldGraphSO graph,
            IWorldCellLoader loader,
            IWorldCellReadinessService readiness,
            RecordingActor actor = null,
            RecordingLocationStore locationStore = null,
            int maxLoadedBudgetWeight = 4,
            bool loadBoundaryCells = false)
        {
            return new WorldGraphRuntimeSession(
                graph,
                loader,
                actor ?? new RecordingActor(),
                locationStore ?? new RecordingLocationStore(),
                new WorldGraphRuntimeSessionOptions(
                    "world.test",
                    "cell.start",
                    "anchor.start",
                    maxLoadedBudgetWeight,
                    TimeSpan.Zero,
                    loadBoundaryCells),
                readiness);
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
                        new[] { "cell.target" }),
                    new WorldStreamingBoundaryDefinition(
                        "boundary.missing-target",
                        new[] { "cell.missing" })
                });
            var targetCell = new WorldCellDefinition(
                "cell.target",
                "Target Cell",
                WorldCellKind.Interior,
                "scene.target",
                WorldCellLayer.Geometry | WorldCellLayer.Audio,
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

        private static WorldGraphSO CreateThreeCellGraph()
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
            var region = new WorldRegionDefinition(
                "region.start",
                "Start Region",
                new[] { startCell, targetCell, neighborCell });
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[] { region },
                Array.Empty<WorldTravelLinkDefinition>(),
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private sealed class TrackingLoader : IWorldCellLoader
        {
            private readonly Dictionary<string, int> _loadCounts = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _unloadCounts = new Dictionary<string, int>();

            public string FailedUnloadCellId { get; set; }

            public Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                Increment(_loadCounts, cell.CellId);
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                Increment(_unloadCounts, cell.CellId);
                return Task.FromResult(cell.CellId == FailedUnloadCellId
                    ? WorldCellOperationResult.Failed(cell.CellId, "Synthetic unload failure.")
                    : WorldCellOperationResult.SucceededResult(cell.CellId));
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

        private sealed class ControlledReadinessService : IWorldCellReadinessService
        {
            private readonly Dictionary<string, int> _prepareCounts = new Dictionary<string, int>();
            private TaskCompletionSource<WorldCellReadinessResult> _blockedCompletion;

            public string FailedCellId { get; set; }
            public string BlockedCellId { get; set; }
            public string CancelOnSuccessfulCellId { get; set; }
            public CancellationTokenSource CancellationToSignal { get; set; }

            public Task<WorldCellReadinessResult> PrepareCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                Increment(_prepareCounts, cell.CellId);
                if (cell.CellId == FailedCellId)
                {
                    return Task.FromResult(WorldCellReadinessResult.Failed(
                        cell.CellId,
                        "Synthetic readiness failure."));
                }

                if (cell.CellId == BlockedCellId)
                {
                    _blockedCompletion ??= new TaskCompletionSource<WorldCellReadinessResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return _blockedCompletion.Task;
                }

                if (cell.CellId == CancelOnSuccessfulCellId)
                {
                    CancellationToSignal?.Cancel();
                }

                return Task.FromResult(WorldCellReadinessResult.SucceededResult(cell.CellId));
            }

            public int GetPrepareCount(string cellId)
            {
                return GetCount(_prepareCounts, cellId);
            }

            public void CompleteBlockedCell()
            {
                _blockedCompletion.SetResult(
                    WorldCellReadinessResult.SucceededResult(BlockedCellId));
            }
        }

        private sealed class RecordingActor : IWorldGraphRuntimeActor
        {
            public int PlacementCount { get; private set; }
            public bool HasActor => true;

            public bool TryPlaceAtAnchor(
                WorldCellDefinition cell,
                WorldAnchorDefinition anchor,
                WorldPosition resolvedPosition)
            {
                PlacementCount++;
                return true;
            }

            public bool TryPlaceAtPosition(WorldPosition position)
            {
                PlacementCount++;
                return true;
            }

            public bool TryPlaceAtLocation(
                WorldCellDefinition cell,
                WorldGraphRuntimeLocation location)
            {
                PlacementCount++;
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
            public int SaveCount { get; private set; }

            public void Save(WorldGraphRuntimeLocation location)
            {
                SaveCount++;
            }

            public void Clear()
            {
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
