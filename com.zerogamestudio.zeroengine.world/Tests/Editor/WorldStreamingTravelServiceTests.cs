using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldStreamingTravelServiceTests
    {
        [Test]
        public async Task ActivateCell_LoadsCurrentCellAndStreamingBoundaryTargets()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.LoadedCellIds);
            Assert.That(service.ActiveCellId, Is.EqualTo("cell.street"));
        }

        [Test]
        public async Task ActivateCell_WhenBoundaryCellsRequested_LoadsBidirectionalSeamlessWalkNeighbors()
        {
            var graph = TestWorldGraphFactory.CreateBidirectionalSeamlessWalkGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.kiln", "cell.wild" }, loader.LoadedCellIds);
            Assert.That(service.ActiveCellId, Is.EqualTo("cell.kiln"));
        }

        [Test]
        public async Task ActivateCell_LoadsEachCellWithItsAuthoredLayerMask()
        {
            var graph = TestWorldGraphFactory.CreateLayeredBoundaryGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            Assert.That(
                loader.LoadedLayersByCell["cell.street"],
                Is.EqualTo(WorldCellLayer.Geometry | WorldCellLayer.Collision));
            Assert.That(
                loader.LoadedLayersByCell["cell.wild"],
                Is.EqualTo(WorldCellLayer.Navigation | WorldCellLayer.GameplayMarkers));
        }

        [Test]
        public async Task ActivateCell_WhenRequestedLayersAreSpecific_LoadsBoundaryCellsWithTheirAuthoredLayerMask()
        {
            var graph = TestWorldGraphFactory.CreateOverlappingLayeredBoundaryGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.Geometry | WorldCellLayer.Collision,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            Assert.That(
                loader.LoadedLayersByCell["cell.street"],
                Is.EqualTo(WorldCellLayer.Geometry | WorldCellLayer.Collision));
            Assert.That(
                loader.LoadedLayersByCell["cell.wild"],
                Is.EqualTo(WorldCellLayer.Geometry | WorldCellLayer.GameplayMarkers));
        }

        [Test]
        public async Task ActivateCell_PreparesReadinessBeforeMarkingCellsLoaded()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader();
            var readiness = new RecordingWorldCellReadinessService();
            var service = new WorldStreamingService(
                graph,
                loader,
                int.MaxValue,
                TimeSpan.Zero,
                readinessService: readiness);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEqual(new[] { "cell.street", "cell.wild" }, readiness.PreparedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenReadinessFails_RollsBackLoadedScenesAndKeepsActiveCell()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader();
            var readiness = new RecordingWorldCellReadinessService(failCellId: "cell.wild");
            var service = new WorldStreamingService(
                graph,
                loader,
                int.MaxValue,
                TimeSpan.Zero,
                readinessService: readiness);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.ReadinessFailed));
            Assert.That(service.ActiveCellId, Is.Null);
            Assert.That(service.LoadedCellIds, Is.Empty);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.UnloadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenLoaderFails_ReturnsStructuredFailure()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader(failCellId: "cell.street");
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.LoaderFailed));
            Assert.That(service.IsCellLoaded("cell.street"), Is.False);
        }

        [Test]
        public async Task ActivateCell_WhenBoundaryLoaderFails_RollsBackNewCellsWithoutChangingActiveCell()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader(failCellId: "cell.wild");
            var service = new WorldStreamingService(graph, loader);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.LoaderFailed));
            Assert.That(service.ActiveCellId, Is.Null);
            Assert.That(service.LoadedCellIds, Is.Empty);
            CollectionAssert.AreEquivalent(new[] { "cell.street" }, loader.UnloadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenRequiredWindowExceedsBudget_ReturnsBudgetFailureWithoutPartialLoads()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 3);

            var result = await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
            Assert.That(loader.LoadedCellIds, Is.Empty);
            Assert.That(service.LoadedCellIds, Is.Empty);
        }

        [Test]
        public async Task ActivateCell_UnloadsCellsOutsideTheNewStreamingWindow()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 4);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.UnloadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenUnloadFailsAfterActiveCellWasRemoved_ClearsStaleActiveCell()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader(failUnloadCellId: "cell.wild");
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 6);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.LoaderFailed));
            Assert.That(service.IsCellLoaded("cell.street"), Is.False);
            Assert.That(service.ActiveCellId, Is.Null);
            CollectionAssert.AreEquivalent(new[] { "cell.wild", "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenUnloadThrows_ReturnsStructuredFailureAndClearsStaleActiveCell()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader(throwUnloadCellId: "cell.wild");
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 6);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.LoaderFailed));
            Assert.That(result.Message, Does.Contain("Injected unload exception."));
            Assert.That(service.IsCellLoaded("cell.street"), Is.False);
            Assert.That(service.ActiveCellId, Is.Null);
            CollectionAssert.AreEquivalent(new[] { "cell.wild", "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenUnloadIsCancelled_RollsBackNewCellsWithoutChangingActiveCell()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader(cancelUnloadCellId: "cell.street");
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 6);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Cancelled));
            Assert.That(service.ActiveCellId, Is.EqualTo("cell.street"));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, service.LoadedCellIds);
            CollectionAssert.Contains(loader.UnloadedCellIds, "cell.kiln");
        }

        [Test]
        public async Task ActivateCell_KeepsPinnedCellsLoadedOutsideTheNewStreamingWindow()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 4);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            service.SetCellPinned("cell.street", true);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.wild" }, loader.UnloadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task AcquireCellPin_KeepsCellResidentUntilHandleIsDisposed()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 4);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var handle = service.AcquireCellPin("cell.street", "battle_return");

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            Assert.That(service.IsCellPinned("cell.street"), Is.True);
            CollectionAssert.Contains(service.GetCellPinReasons("cell.street"), "battle_return");
            CollectionAssert.AreEquivalent(new[] { "cell.wild" }, loader.UnloadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.kiln" }, service.LoadedCellIds);

            handle.Dispose();
            Assert.That(service.IsCellPinned("cell.street"), Is.False);

            result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_KeepsCellsInsideMinimumResidencyWindowLoaded()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var clock = new ManualClock(DateTimeOffset.UnixEpoch);
            var service = new WorldStreamingService(
                graph,
                loader,
                maxLoadedBudgetWeight: 6,
                minimumCellResidency: TimeSpan.FromSeconds(10),
                utcNowProvider: clock.Now);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            Assert.That(loader.UnloadedCellIds, Is.Empty);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild", "cell.kiln" }, service.LoadedCellIds);

            clock.Advance(TimeSpan.FromSeconds(11));
            result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.UnloadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task ActivateCell_WhenMinimumResidencyPreventsSafeUnload_ReturnsBudgetFailureWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var clock = new ManualClock(DateTimeOffset.UnixEpoch);
            var service = new WorldStreamingService(
                graph,
                loader,
                maxLoadedBudgetWeight: 4,
                minimumCellResidency: TimeSpan.FromSeconds(10),
                utcNowProvider: clock.Now);

            await service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.BudgetExceeded));
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, loader.LoadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.street", "cell.wild" }, service.LoadedCellIds);
            Assert.That(service.ActiveCellId, Is.EqualTo("cell.street"));
        }

        [Test]
        public async Task ActivateCell_DoesNotImplicitlyLoadPinnedCellsThatAreNotResident()
        {
            var graph = TestWorldGraphFactory.CreateBudgetWindowGraph();
            var loader = new RecordingWorldCellLoader();
            var service = new WorldStreamingService(graph, loader, maxLoadedBudgetWeight: 4);
            service.SetCellPinned("cell.street", true);

            var result = await service.ActivateCellAsync(
                "cell.kiln",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.AreEquivalent(new[] { "cell.kiln" }, loader.LoadedCellIds);
            CollectionAssert.AreEquivalent(new[] { "cell.kiln" }, service.LoadedCellIds);
        }

        [Test]
        public async Task EnsureCellLoaded_WhenStreamingOperationIsInProgress_ReturnsBusyWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateStreetToWildGraph();
            var loader = new RecordingWorldCellLoader(blockLoads: true, blockCellId: "cell.street");
            var service = new WorldStreamingService(graph, loader);

            var activation = service.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: false,
                CancellationToken.None);
            var result = await service.EnsureCellLoadedAsync(
                "cell.wild",
                WorldCellLayer.All,
                CancellationToken.None);
            loader.ReleaseBlockedLoads();
            var activationResult = await activation;

            Assert.That(result.Status, Is.EqualTo(WorldStreamingResultStatus.Busy));
            Assert.That(activationResult.Status, Is.EqualTo(WorldStreamingResultStatus.Succeeded));
            CollectionAssert.DoesNotContain(loader.LoadedCellIds, "cell.wild");
            CollectionAssert.AreEquivalent(new[] { "cell.street" }, service.LoadedCellIds);
        }

        [Test]
        public async Task TravelAsync_SeamlessInteriorLink_ActivatesTargetCellAndReturnsAnchorPosition()
        {
            var graph = TestWorldGraphFactory.CreateStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.Succeeded));
            Assert.That(result.Destination.CellId, Is.EqualTo("cell.apothecary"));
            Assert.That(result.Destination.AnchorId, Is.EqualTo("anchor.apothecary.inside"));
            Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.apothecary"));
        }

        [Test]
        public async Task TravelAsync_TargetCellWithWorldOrigin_ReturnsWorldSpaceAnchorPosition()
        {
            var graph = TestWorldGraphFactory.CreateOffsetStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.Succeeded));
            Assert.That(result.Destination.CellLocalPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(result.Destination.WorldSpacePosition, Is.EqualTo(new Vector3(5f, 0f, 2f)));
        }

        [Test]
        public async Task TravelAsync_MissingLink_ReturnsStructuredFailureWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.missing"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.LinkNotFound));
            Assert.That(loader.LoadedCellIds, Is.Empty);
        }

        [Test]
        public async Task TravelAsync_NullTravelLink_ReturnsStructuredFailureWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateGraphWithNullTravelLink();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.missing"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.LinkNotFound));
            Assert.That(loader.LoadedCellIds, Is.Empty);
        }

        [Test]
        public async Task TravelAsync_DefaultRequest_DoesNotMatchMalformedBlankLink()
        {
            var graph = TestWorldGraphFactory.CreateGraphWithMalformedBlankTravelLink();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var result = await travel.TravelAsync(default, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.LinkNotFound));
            Assert.That(result.Message, Does.Contain("link id"));
            Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.street"));
            Assert.That(loader.LoadedCellIds, Does.Not.Contain("cell.apothecary"));
        }

        [Test]
        public async Task TravelAsync_WhenActiveCellIsMissing_ReturnsOriginMismatchWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.OriginMismatch));
            Assert.That(result.Message, Does.Contain("active source cell"));
            Assert.That(loader.LoadedCellIds, Is.Empty);
        }

        [Test]
        public async Task TravelAsync_BidirectionalLink_WhenStartedFromTargetAnchor_ReturnsOriginAnchor()
        {
            var graph = TestWorldGraphFactory.CreateBidirectionalStreetToWildTravelGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.wild",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.wild", "anchor.wild.entry"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.Succeeded));
            Assert.That(result.Destination.CellId, Is.EqualTo("cell.street"));
            Assert.That(result.Destination.AnchorId, Is.EqualTo("anchor.street.gate"));
        }

        [Test]
        public async Task TravelAsync_WhenRequestedAnchorIsNotInActiveCell_ReturnsOriginMismatchWithoutActivatingTarget()
        {
            var graph = TestWorldGraphFactory.CreateBidirectionalStreetToWildTravelGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.wild", "anchor.wild.entry"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.OriginMismatch));
            Assert.That(result.Message, Does.Contain("active cell"));
            Assert.That(streaming.ActiveCellId, Is.EqualTo("cell.street"));
            CollectionAssert.AreEquivalent(new[] { "cell.street" }, loader.LoadedCellIds);
        }

        [Test]
        public async Task TravelAsync_OneWayLink_WhenStartedFromTargetAnchor_ReturnsOriginMismatchWithoutLoading()
        {
            var graph = TestWorldGraphFactory.CreateStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader();
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            var result = await travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter", "anchor.apothecary.inside"),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(WorldTravelResultStatus.OriginMismatch));
            Assert.That(loader.LoadedCellIds, Is.Empty);
        }

        [Test]
        public async Task TravelAsync_ConcurrentRequest_IsRejectedDeterministically()
        {
            var graph = TestWorldGraphFactory.CreateStreetToInteriorGraph();
            var loader = new RecordingWorldCellLoader(blockLoads: true, blockCellId: "cell.apothecary");
            var streaming = new WorldStreamingService(graph, loader);
            var travel = new WorldTravelService(graph, streaming);

            await streaming.ActivateCellAsync(
                "cell.street",
                WorldCellLayer.All,
                loadBoundaryCells: true,
                CancellationToken.None);
            var first = travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter"),
                CancellationToken.None);
            var second = await travel.TravelAsync(
                new WorldTravelRequest("link.street.apothecary.enter"),
                CancellationToken.None);
            loader.ReleaseBlockedLoads();
            await first;

            Assert.That(second.Status, Is.EqualTo(WorldTravelResultStatus.Busy));
        }

        private static class TestWorldGraphFactory
        {
            public static WorldGraphSO CreateStreetToWildGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.spawn",
                                    "Street Spawn",
                                    WorldAnchorKind.Spawn,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            new[]
                            {
                                new WorldStreamingBoundaryDefinition("boundary.street.to_wild", new[] { "cell.wild" })
                            }),
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.All,
                            1,
                            Array.Empty<WorldAnchorDefinition>(),
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    Array.Empty<WorldTravelLinkDefinition>());
            }

            public static WorldGraphSO CreateBudgetWindowGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            2,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.spawn",
                                    "Street Spawn",
                                    WorldAnchorKind.Spawn,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            new[]
                            {
                                new WorldStreamingBoundaryDefinition("boundary.street.to_wild", new[] { "cell.wild" })
                            }),
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.All,
                            2,
                            Array.Empty<WorldAnchorDefinition>(),
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.kiln",
                            "Kiln",
                            WorldCellKind.Outdoor,
                            "KilnScene",
                            WorldCellLayer.All,
                            2,
                            Array.Empty<WorldAnchorDefinition>(),
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    Array.Empty<WorldTravelLinkDefinition>());
            }

            public static WorldGraphSO CreateLayeredBoundaryGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.Geometry | WorldCellLayer.Collision,
                            1,
                            Array.Empty<WorldAnchorDefinition>(),
                            new[]
                            {
                                new WorldStreamingBoundaryDefinition("boundary.street.to_wild", new[] { "cell.wild" })
                            }),
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.Navigation | WorldCellLayer.GameplayMarkers,
                            1,
                            Array.Empty<WorldAnchorDefinition>(),
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    Array.Empty<WorldTravelLinkDefinition>());
            }

            public static WorldGraphSO CreateOverlappingLayeredBoundaryGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.Geometry | WorldCellLayer.Collision,
                            1,
                            Array.Empty<WorldAnchorDefinition>(),
                            new[]
                            {
                                new WorldStreamingBoundaryDefinition("boundary.street.to_wild", new[] { "cell.wild" })
                            }),
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.Geometry | WorldCellLayer.GameplayMarkers,
                            1,
                            Array.Empty<WorldAnchorDefinition>(),
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    Array.Empty<WorldTravelLinkDefinition>());
            }

            public static WorldGraphSO CreateBidirectionalStreetToWildTravelGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.gate",
                                    "Street Gate",
                                    WorldAnchorKind.RoadExit,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.wild.entry",
                                    "Wild Entry",
                                    WorldAnchorKind.RoadExit,
                                    new Vector3(8f, 0f, 0f),
                                    Vector3.back)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.wild",
                            "anchor.street.gate",
                            "anchor.wild.entry",
                            WorldTravelMode.PortalTransition,
                            true)
                    });
            }

            public static WorldGraphSO CreateBidirectionalSeamlessWalkGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.wild",
                            "Wild",
                            WorldCellKind.Outdoor,
                            "WildScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.wild.kiln_gate",
                                    "Wild Kiln Gate",
                                    WorldAnchorKind.RoadExit,
                                    new Vector3(16f, 0f, 0f),
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.kiln",
                            "Kiln",
                            WorldCellKind.Outdoor,
                            "KilnScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.kiln.entry",
                                    "Kiln Entry",
                                    WorldAnchorKind.RoadExit,
                                    Vector3.zero,
                                    Vector3.back)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.wild.kiln",
                            "anchor.wild.kiln_gate",
                            "anchor.kiln.entry",
                            WorldTravelMode.SeamlessWalk,
                            true)
                    });
            }

            public static WorldGraphSO CreateStreetToInteriorGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.apothecary.outside",
                                    "Apothecary Outside",
                                    WorldAnchorKind.InteriorEntry,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.apothecary",
                            "Apothecary",
                            WorldCellKind.Interior,
                            "ApothecaryInteriorScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.apothecary.inside",
                                    "Apothecary Inside",
                                    WorldAnchorKind.InteriorEntry,
                                    new Vector3(2f, 0f, 3f),
                                    Vector3.back)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.apothecary.enter",
                            "anchor.street.apothecary.outside",
                            "anchor.apothecary.inside",
                            WorldTravelMode.SeamlessInterior,
                            false)
                    });
            }

            public static WorldGraphSO CreateGraphWithNullTravelLink()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.spawn",
                                    "Street Spawn",
                                    WorldAnchorKind.Spawn,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new WorldTravelLinkDefinition[] { null });
            }

            public static WorldGraphSO CreateGraphWithMalformedBlankTravelLink()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
                            "cell.street",
                            "Street",
                            WorldCellKind.Outdoor,
                            "StreetScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.street.apothecary.outside",
                                    "Apothecary Outside",
                                    WorldAnchorKind.InteriorEntry,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.apothecary",
                            "Apothecary",
                            WorldCellKind.Interior,
                            "ApothecaryInteriorScene",
                            WorldCellLayer.All,
                            1,
                            new[]
                            {
                                new WorldAnchorDefinition(
                                    "anchor.apothecary.inside",
                                    "Apothecary Inside",
                                    WorldAnchorKind.InteriorEntry,
                                    new Vector3(2f, 0f, 3f),
                                    Vector3.back)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            null,
                            "anchor.street.apothecary.outside",
                            "anchor.apothecary.inside",
                            WorldTravelMode.SeamlessInterior,
                            false)
                    });
            }

            public static WorldGraphSO CreateOffsetStreetToInteriorGraph()
            {
                return CreateGraph(
                    new[]
                    {
                        new WorldCellDefinition(
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
                                    "anchor.street.apothecary.outside",
                                    "Apothecary Outside",
                                    WorldAnchorKind.InteriorEntry,
                                    Vector3.zero,
                                    Vector3.forward)
                            },
                            Array.Empty<WorldStreamingBoundaryDefinition>()),
                        new WorldCellDefinition(
                            "cell.apothecary",
                            "Apothecary",
                            WorldCellKind.Interior,
                            "ApothecaryInteriorScene",
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
                            Array.Empty<WorldStreamingBoundaryDefinition>())
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.apothecary.enter",
                            "anchor.street.apothecary.outside",
                            "anchor.apothecary.inside",
                            WorldTravelMode.SeamlessInterior,
                            false)
                    });
            }

            private static WorldGraphSO CreateGraph(
                IEnumerable<WorldCellDefinition> cells,
                IEnumerable<WorldTravelLinkDefinition> travelLinks)
            {
                var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
                graph.ConfigureForTests(
                    "world.test",
                    new[]
                    {
                        new WorldRegionDefinition("region.test", "Test Region", cells)
                    },
                    travelLinks,
                    Array.Empty<WorldFastTravelNodeDefinition>());
                return graph;
            }
        }

        private sealed class RecordingWorldCellLoader : IWorldCellLoader
        {
            private readonly string _failCellId;
            private readonly string _failUnloadCellId;
            private readonly string _cancelUnloadCellId;
            private readonly string _throwUnloadCellId;
            private readonly bool _blockLoads;
            private readonly string _blockCellId;
            private readonly List<string> _loadedCellIds = new List<string>();
            private readonly List<string> _unloadedCellIds = new List<string>();
            private readonly Dictionary<string, WorldCellLayer> _loadedLayersByCell =
                new Dictionary<string, WorldCellLayer>();
            private readonly TaskCompletionSource<bool> _loadRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public RecordingWorldCellLoader(
                string failCellId = null,
                bool blockLoads = false,
                string failUnloadCellId = null,
                string cancelUnloadCellId = null,
                string throwUnloadCellId = null,
                string blockCellId = null)
            {
                _failCellId = failCellId;
                _blockLoads = blockLoads;
                _failUnloadCellId = failUnloadCellId;
                _cancelUnloadCellId = cancelUnloadCellId;
                _throwUnloadCellId = throwUnloadCellId;
                _blockCellId = blockCellId;
            }

            public IReadOnlyList<string> LoadedCellIds => _loadedCellIds;
            public IReadOnlyList<string> UnloadedCellIds => _unloadedCellIds;
            public IReadOnlyDictionary<string, WorldCellLayer> LoadedLayersByCell => _loadedLayersByCell;

            public void ReleaseBlockedLoads()
            {
                _loadRelease.TrySetResult(true);
            }

            public async Task<WorldCellOperationResult> LoadCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                if (_blockLoads && (string.IsNullOrWhiteSpace(_blockCellId) || cell.CellId == _blockCellId))
                {
                    await _loadRelease.Task;
                }

                if (cell.CellId == _failCellId)
                {
                    return WorldCellOperationResult.Failed(cell.CellId, "Injected failure.");
                }

                _loadedCellIds.Add(cell.CellId);
                _loadedLayersByCell[cell.CellId] = layers;
                return WorldCellOperationResult.SucceededResult(cell.CellId);
            }

            public Task<WorldCellOperationResult> UnloadCellAsync(
                WorldCellDefinition cell,
                CancellationToken cancellationToken)
            {
                if (cell.CellId == _failUnloadCellId)
                {
                    return Task.FromResult(WorldCellOperationResult.Failed(cell.CellId, "Injected unload failure."));
                }

                if (cell.CellId == _cancelUnloadCellId)
                {
                    return Task.FromResult(WorldCellOperationResult.Cancelled(cell.CellId, "Injected unload cancellation."));
                }

                if (cell.CellId == _throwUnloadCellId)
                {
                    throw new InvalidOperationException("Injected unload exception.");
                }

                _loadedCellIds.Remove(cell.CellId);
                _unloadedCellIds.Add(cell.CellId);
                return Task.FromResult(WorldCellOperationResult.SucceededResult(cell.CellId));
            }
        }

        private sealed class RecordingWorldCellReadinessService : IWorldCellReadinessService
        {
            private readonly string _failCellId;
            private readonly List<string> _preparedCellIds = new List<string>();

            public RecordingWorldCellReadinessService(string failCellId = null)
            {
                _failCellId = failCellId;
            }

            public IReadOnlyList<string> PreparedCellIds => _preparedCellIds;

            public Task<WorldCellReadinessResult> PrepareCellAsync(
                WorldCellDefinition cell,
                WorldCellLayer layers,
                CancellationToken cancellationToken)
            {
                _preparedCellIds.Add(cell.CellId);
                return Task.FromResult(cell.CellId == _failCellId
                    ? WorldCellReadinessResult.Failed(cell.CellId, "Injected readiness failure.")
                    : WorldCellReadinessResult.SucceededResult(cell.CellId));
            }
        }

        private sealed class ManualClock
        {
            private DateTimeOffset _now;

            public ManualClock(DateTimeOffset now)
            {
                _now = now;
            }

            public DateTimeOffset Now()
            {
                return _now;
            }

            public void Advance(TimeSpan delta)
            {
                _now += delta;
            }
        }
    }
}
