using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class PlatformSearchBackendTests
    {
        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
            createdObjects.Clear();
        }

        [Test]
        public void BuildTransaction_PublishesOneImmutableRevisionAndPreservesOldSnapshotUntilCommit()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            PlatformSearchGraphSnapshot first = graph.CommitBuild();

            Assert.AreEqual(1, first.GraphRevision);
            Assert.AreEqual(1, first.LinkCount);

            graph.BeginBuild();
            graph.AddLink(Link(nodes[1], nodes[0], PlatformLinkType.Walk, 1f));

            Assert.AreSame(first, graph.SearchSnapshot);
            Assert.AreEqual(1, graph.SearchSnapshot.LinkCount);
            Assert.AreEqual(1, graph.GraphRevision);

            PlatformSearchGraphSnapshot second = graph.CommitBuild();
            Assert.AreEqual(2, second.GraphRevision);
            Assert.AreEqual(2, second.LinkCount);
            Assert.AreEqual(1, first.LinkCount);

            graph.Links.Add(Link(nodes[0], nodes[1], PlatformLinkType.Jump, 0.25f));
            Assert.AreEqual(2, graph.SearchSnapshot.LinkCount,
                "Direct legacy collection edits must not mutate the committed snapshot.");
        }

        [Test]
        public void BaseGraphReuse_CopiesWalkTopologyButExcludesProfileTraversalLinks()
        {
            PlatformGraphGenerator source = CreateGraph(2, out PlatformNodeData[] nodes);
            source.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            source.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Jump, 2f));

            var targetHost = new GameObject("PlatformSearchBackendBaseReuseTarget");
            createdObjects.Add(targetHost);
            var target = targetHost.AddComponent<PlatformGraphGenerator>();
            target.Config.ScanCenter = source.Config.ScanCenter;
            target.Config.ScanSize = source.Config.ScanSize;
            target.Config.GroundLayer = source.Config.GroundLayer;
            target.Config.OneWayPlatformLayer = source.Config.OneWayPlatformLayer;
            target.Config.ObstacleLayer = source.Config.ObstacleLayer;
            target.Config.NodeSpacing = source.Config.NodeSpacing;
            target.Config.EdgeInset = source.Config.EdgeInset;
            target.Config.MinPlatformWidth = source.Config.MinPlatformWidth;
            target.BeginBuild();

            target.GeneratePlatformGraphFromBase(source);

            Assert.AreEqual(source.Nodes.Count, target.Nodes.Count);
            Assert.AreEqual(source.SurfaceSegments.Count, target.SurfaceSegments.Count);
            Assert.AreEqual(1, target.Links.Count);
            Assert.AreEqual(PlatformLinkType.Walk, target.Links[0].LinkType);
            if (source.SurfaceSegments.Count > 0)
                Assert.AreNotSame(source.SurfaceSegments[0], target.SurfaceSegments[0]);
            PlatformSearchGraphSnapshot snapshot = target.CommitBuild();
            Assert.AreEqual(target.Nodes.Count, snapshot.NodeCount);
            Assert.AreEqual(1, snapshot.LinkCount);
        }

        [Test]
        public void FailedCommit_PreservesPreviouslyCommittedSnapshotAndRevision()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            PlatformSearchGraphSnapshot committed = graph.CommitBuild();

            graph.BeginBuild();
            graph.Links.Add(new PlatformLinkData
            {
                FromNodeId = nodes[0].NodeId,
                ToNodeId = 999999,
                LinkType = PlatformLinkType.Walk,
                Cost = 1f
            });

            Assert.IsFalse(graph.TryCommitBuild(out _, out string error));
            StringAssert.Contains("missing nodes", error);
            Assert.AreSame(committed, graph.SearchSnapshot);
            Assert.AreEqual(committed.GraphRevision, graph.GraphRevision);
            graph.CancelBuild();
        }

        [Test]
        public void Snapshot_PreservesDirectedParallelLinksAndOriginalBindings()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            var jump = Link(nodes[0], nodes[1], PlatformLinkType.Jump, 5f);
            jump.JumpVelocityX = 3f;
            jump.JumpVelocityY = 8f;
            var fall = Link(nodes[0], nodes[1], PlatformLinkType.Fall, 2f);
            graph.AddLink(jump);
            graph.AddLink(fall);
            PlatformSearchGraphSnapshot snapshot = graph.CommitBuild();

            Assert.AreEqual(2, snapshot.LinkCount);
            Assert.AreEqual(0, snapshot.GetLink(0).LinkId);
            Assert.AreEqual(1, snapshot.GetLink(1).LinkId);
            Assert.AreEqual(PlatformLinkType.Jump, snapshot.GetLink(0).LinkType);
            Assert.AreEqual(PlatformLinkType.Fall, snapshot.GetLink(1).LinkType);
            Assert.IsTrue(snapshot.TryGetNodeIndex(nodes[0].NodeId, out int fromNodeIndex));
            Assert.IsTrue(snapshot.TryGetNodeIndex(nodes[1].NodeId, out int toNodeIndex));
            Assert.AreEqual(2, snapshot.GetOutgoingLinkCount(fromNodeIndex));
            Assert.AreEqual(0, snapshot.GetOutgoingLinkCount(toNodeIndex));
            Assert.AreEqual(2, snapshot.GetIncomingLinkCount(toNodeIndex));
            Assert.IsTrue(graph.TryGetCommittedLink(snapshot.GraphRevision, 0, out PlatformLinkData binding));
            Assert.AreEqual(3f, binding.JumpVelocityX);
            Assert.AreEqual(8f, binding.JumpVelocityY);
        }

        [Test]
        public void ManagedBackend_UsesLowestCostParallelEdgeAndDoesNotInventReverseReachability()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Jump, 5f));
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Fall, 2f));
            PlatformSearchGraphSnapshot snapshot = graph.CommitBuild();
            var backend = new ManagedPlatformPathSearchBackend();
            backend.PrepareGraph(snapshot);

            PlatformSearchResult forward = SubmitImmediate(
                backend,
                Request(snapshot, 1, nodes[0].NodeId, nodes[1].NodeId));
            Assert.IsTrue(forward.Success);
            Assert.AreEqual(2f, forward.GetTarget(0).SearchCost, 0.0001f);
            Assert.AreEqual(1, forward.GetTarget(0).LinkCount);
            Assert.AreEqual(1, forward.GetTarget(0).GetLinkId(0));

            PlatformSearchResult reverse = SubmitImmediate(
                backend,
                Request(snapshot, 2, nodes[1].NodeId, nodes[0].NodeId));
            Assert.AreEqual(PlatformSearchFailureReason.NoPath, reverse.FailureReason);
        }

        [Test]
        public void ManagedBackend_MultiTargetMatchesIndependentSingleTargetCostsAndLinks()
        {
            PlatformGraphGenerator graph = CreateGraph(3, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.AddLink(Link(nodes[1], nodes[2], PlatformLinkType.Jump, 2f));
            graph.AddLink(Link(nodes[0], nodes[2], PlatformLinkType.Fall, 10f));
            PlatformSearchGraphSnapshot snapshot = graph.CommitBuild();
            var backend = new ManagedPlatformPathSearchBackend();
            backend.PrepareGraph(snapshot);

            var multiRequest = new PlatformSearchRequest(
                10,
                snapshot,
                nodes[0].NodeId,
                new[] { nodes[1].NodeId, nodes[2].NodeId },
                NeutralCostContext());
            PlatformSearchResult multi = SubmitImmediate(backend, multiRequest);
            PlatformSearchResult single1 = SubmitImmediate(
                backend,
                Request(snapshot, 11, nodes[0].NodeId, nodes[1].NodeId));
            PlatformSearchResult single2 = SubmitImmediate(
                backend,
                Request(snapshot, 12, nodes[0].NodeId, nodes[2].NodeId));

            AssertTargetEquivalent(single1.GetTarget(0), multi.GetTarget(0));
            AssertTargetEquivalent(single2.GetTarget(0), multi.GetTarget(1));
            Assert.Less(multi.ExpandedNodes, single1.ExpandedNodes + single2.ExpandedNodes);
        }

        [Test]
        public void ManagedBackend_PartialPathReturnsClosestReachableNode()
        {
            PlatformGraphGenerator graph = CreateGraph(3, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            PlatformSearchGraphSnapshot snapshot = graph.CommitBuild();
            var backend = new ManagedPlatformPathSearchBackend();
            backend.PrepareGraph(snapshot);

            var request = new PlatformSearchRequest(
                20,
                snapshot,
                nodes[0].NodeId,
                new[] { nodes[2].NodeId },
                NeutralCostContext(),
                allowPartialPath: true);
            PlatformSearchTargetResult result = SubmitImmediate(backend, request).GetTarget(0);

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.IsPartial);
            Assert.AreEqual(nodes[1].NodeId, result.ResolvedNodeId);
            Assert.AreEqual(1, result.LinkCount);
        }

        [Test]
        public void ManagedBackend_ElevatedLowNodePenaltyUsesSharedCostContext()
        {
            PlatformGraphGenerator graph = CreateGraph(3, out PlatformNodeData[] nodes);
            PlatformNodeData low = nodes[1];
            low.Position = new Vector3(low.Position.x, nodes[0].Position.y - 2f, 0f);
            graph.Nodes[graph.NodeIdToIndex[low.NodeId]] = low;
            nodes[1] = low;
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Fall, 1f));
            graph.AddLink(Link(nodes[1], nodes[2], PlatformLinkType.Jump, 1f));
            graph.AddLink(Link(nodes[0], nodes[2], PlatformLinkType.Jump, 10f));
            PlatformSearchGraphSnapshot snapshot = graph.CommitBuild();
            var backend = new ManagedPlatformPathSearchBackend();
            backend.PrepareGraph(snapshot);

            PlatformSearchResult neutral = SubmitImmediate(
                backend,
                Request(snapshot, 30, nodes[0].NodeId, nodes[2].NodeId));
            var elevatedContext = new PlatformPathSearchCostContext(
                true,
                nodes[0].Position.y,
                0.5f);
            var elevatedRequest = new PlatformSearchRequest(
                31,
                snapshot,
                nodes[0].NodeId,
                new[] { nodes[2].NodeId },
                elevatedContext);
            PlatformSearchResult elevated = SubmitImmediate(backend, elevatedRequest);

            Assert.AreEqual(2f, neutral.GetTarget(0).SearchCost, 0.0001f);
            Assert.AreEqual(10f, elevated.GetTarget(0).SearchCost, 0.0001f);
            Assert.AreEqual(2, neutral.GetTarget(0).LinkCount);
            Assert.AreEqual(1, elevated.GetTarget(0).LinkCount);
        }

        [Test]
        public void Pathfinder_QueryDoesNotMutateCurrentPathUntilExplicitCurrentRevisionCommit()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);

            PlatformPathQuerySubmission submission = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(nodes[0].Position, nodes[1].Position, forceRequest: true));

            Assert.AreEqual(PlatformSearchSubmissionKind.Immediate, submission.Kind);
            Assert.IsTrue(submission.ImmediateResult.PathResult.Success);
            Assert.IsNull(pathfinder.CurrentPath);
            Assert.IsTrue(pathfinder.TryCommitPathQueryResult(submission.ImmediateResult));
            Assert.IsNotNull(pathfinder.CurrentPath);
        }

        [Test]
        public void Pathfinder_ResultCannotCommitWhileGraphBuildTransactionIsOpen()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            PlatformPathQuerySubmission submission = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(nodes[0].Position, nodes[1].Position, forceRequest: true));

            graph.BeginBuild();
            try
            {
                Assert.IsFalse(pathfinder.TryCommitPathQueryResult(submission.ImmediateResult));
                Assert.IsNull(pathfinder.CurrentPath);
            }
            finally
            {
                graph.CancelBuild();
            }
        }

        [Test]
        public void Pathfinder_CurrentRevisionNoPathCommitClearsPreviouslyCommittedPath()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);

            PlatformPathQuerySubmission success = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(nodes[0].Position, nodes[1].Position, forceRequest: true));
            Assert.IsTrue(pathfinder.TryCommitPathQueryResult(success.ImmediateResult));
            Assert.IsNotNull(pathfinder.CurrentPath);

            PlatformPathQuerySubmission noPath = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(
                    nodes[1].Position,
                    nodes[0].Position,
                    forceRequest: true,
                    allowPartialPathOverride: false));

            Assert.AreEqual(PlatformSearchSubmissionKind.Immediate, noPath.Kind);
            Assert.AreEqual(PlatformPathFailureReason.PathNotFound, noPath.ImmediateResult.PathResult.FailureReason);
            Assert.IsTrue(pathfinder.TryCommitPathQueryResult(noPath.ImmediateResult));
            Assert.IsNull(pathfinder.CurrentPath);
            Assert.AreEqual(PlatformPathFailureReason.PathNotFound, pathfinder.LastFailureReason);
        }

        [Test]
        public void Pathfinder_PendingOldRevisionCallbackCannotCommitAfterGraphReplacement()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            var backend = new DeferredManagedBackend();
            pathfinder.SetSearchBackend(backend);

            PlatformPathQueryResult callbackResult = null;
            PlatformPathQuerySubmission submission = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(nodes[0].Position, nodes[1].Position, forceRequest: true),
                result => callbackResult = result);
            Assert.AreEqual(PlatformSearchSubmissionKind.Pending, submission.Kind);

            graph.BeginBuild();
            graph.AddLink(Link(nodes[1], nodes[0], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            backend.Complete();

            Assert.IsNotNull(callbackResult);
            Assert.AreEqual(PlatformPathFailureReason.StaleResult, callbackResult.PathResult.FailureReason);
            Assert.IsFalse(pathfinder.TryCommitPathQueryResult(callbackResult));
            Assert.IsNull(pathfinder.CurrentPath);
        }

        [Test]
        public void Pathfinder_ClearPathCancelsPendingBackendHandle()
        {
            PlatformGraphGenerator graph = CreateGraph(2, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            var backend = new DeferredManagedBackend();
            pathfinder.SetSearchBackend(backend);

            PlatformPathQuerySubmission submission = pathfinder.SubmitPathQuery(
                new PlatformPathRequest(nodes[0].Position, nodes[1].Position, forceRequest: true));
            Assert.AreEqual(PlatformSearchSubmissionKind.Pending, submission.Kind);

            pathfinder.ClearPath();

            Assert.IsTrue(submission.Handle.IsCancelled);
            Assert.IsTrue(backend.CancelObserved);
            Assert.IsFalse(backend.Complete());
        }

        [Test]
        public void Pathfinder_RouteBatchUsesOneCompatibleMultiTargetSearchAndNeverMutatesCurrentPath()
        {
            PlatformGraphGenerator graph = CreateGraph(3, out PlatformNodeData[] nodes);
            graph.AddLink(Link(nodes[0], nodes[1], PlatformLinkType.Walk, 1f));
            graph.AddLink(Link(nodes[1], nodes[2], PlatformLinkType.Jump, 2f));
            graph.AddLink(Link(nodes[0], nodes[2], PlatformLinkType.Jump, 10f));
            graph.CommitBuild();
            Platform2DPathfinder pathfinder = graph.gameObject.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            var backend = new CountingManagedBackend();
            pathfinder.SetSearchBackend(backend);

            PlatformRouteBatchSubmission submission = pathfinder.SubmitRouteBatch(
                new PlatformRouteBatchQuery(new[]
                {
                    new PlatformRouteQuery(nodes[0].Position, nodes[1].Position, projectTargetToGround: false),
                    new PlatformRouteQuery(nodes[0].Position, nodes[2].Position, projectTargetToGround: false)
                }));

            Assert.AreEqual(PlatformSearchSubmissionKind.Immediate, submission.Kind);
            Assert.AreEqual(1, backend.SubmitCount);
            Assert.IsTrue(submission.ImmediateResult.GetResult(0).Success);
            Assert.IsTrue(submission.ImmediateResult.GetResult(1).Success);
            Assert.IsNull(pathfinder.CurrentPath);
        }

        private PlatformGraphGenerator CreateGraph(int platformCount, out PlatformNodeData[] selectedNodes)
        {
            var host = new GameObject("PlatformSearchBackendTestsHost");
            createdObjects.Add(host);
            for (int i = 0; i < platformCount; i++)
            {
                var platform = new GameObject($"PlatformSearchBackendTestsPlatform{i}");
                platform.transform.position = new Vector3((i - (platformCount - 1) * 0.5f) * 4f, 0f, 0f);
                var collider = platform.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(1f, 0.2f);
                createdObjects.Add(platform);
            }
            Physics2D.SyncTransforms();

            var graph = host.AddComponent<PlatformGraphGenerator>();
            graph.Config.ScanCenter = Vector2.zero;
            graph.Config.ScanSize = new Vector2(32f, 8f);
            graph.Config.GroundLayer = 1 << 0;
            graph.Config.OneWayPlatformLayer = 0;
            graph.Config.ObstacleLayer = 0;
            graph.Config.NodeSpacing = 2f;
            graph.Config.EdgeInset = 0.1f;
            graph.Config.MinPlatformWidth = 0.1f;
            graph.BeginBuild();
            graph.GeneratePlatformGraph();
            selectedNodes = graph.Nodes
                .Where(node => node.PlatformCollider != null)
                .GroupBy(node => node.PlatformCollider)
                .OrderBy(group => group.Key.bounds.center.x)
                .Select(group => group
                    .OrderBy(node => Mathf.Abs(node.Position.x - group.Key.bounds.center.x))
                    .First())
                .Take(platformCount)
                .ToArray();
            Assert.AreEqual(platformCount, selectedNodes.Length);
            graph.Links.Clear();
            graph.AdjacencyList.Clear();
            return graph;
        }

        private static PlatformLinkData Link(
            PlatformNodeData from,
            PlatformNodeData to,
            PlatformLinkType type,
            float cost)
        {
            return new PlatformLinkData
            {
                FromNodeId = from.NodeId,
                ToNodeId = to.NodeId,
                LinkType = type,
                Cost = cost,
                Duration = cost,
                IsOneWay = true
            };
        }

        private static PlatformPathSearchCostContext NeutralCostContext()
        {
            return new PlatformPathSearchCostContext(false, 0f, 0.5f);
        }

        private static PlatformSearchRequest Request(
            PlatformSearchGraphSnapshot snapshot,
            long requestRevision,
            int sourceNodeId,
            int targetNodeId)
        {
            return new PlatformSearchRequest(
                requestRevision,
                snapshot,
                sourceNodeId,
                new[] { targetNodeId },
                NeutralCostContext());
        }

        private static PlatformSearchResult SubmitImmediate(
            IPlatformPathSearchBackend backend,
            PlatformSearchRequest request)
        {
            PlatformSearchSubmission submission = backend.Submit(request);
            Assert.AreEqual(PlatformSearchSubmissionKind.Immediate, submission.Kind);
            Assert.IsNotNull(submission.ImmediateResult);
            return submission.ImmediateResult;
        }

        private static void AssertTargetEquivalent(
            PlatformSearchTargetResult expected,
            PlatformSearchTargetResult actual)
        {
            Assert.AreEqual(expected.Found, actual.Found);
            Assert.AreEqual(expected.IsPartial, actual.IsPartial);
            Assert.AreEqual(expected.ResolvedNodeId, actual.ResolvedNodeId);
            Assert.AreEqual(expected.SearchCost, actual.SearchCost, 0.0001f);
            Assert.AreEqual(expected.LinkCount, actual.LinkCount);
            for (int i = 0; i < expected.LinkCount; i++)
                Assert.AreEqual(expected.GetLinkId(i), actual.GetLinkId(i));
        }

        private sealed class DeferredManagedBackend : IPlatformPathSearchBackend
        {
            private readonly ManagedPlatformPathSearchBackend oracle = new ManagedPlatformPathSearchBackend();
            private PlatformSearchRequest pendingRequest;
            private Action<PlatformSearchResult> pendingCallback;
            private PlatformPathSearchHandle pendingHandle;

            public string BackendId => "tests.deferred-managed";
            public int BackendRevision => 1;
            public PlatformPathSearchBackendCapabilities Capabilities =>
                PlatformPathSearchBackendCapabilities.MultipleTargets |
                PlatformPathSearchBackendCapabilities.Asynchronous |
                PlatformPathSearchBackendCapabilities.SingleThreaded;
            public bool CancelObserved { get; private set; }

            public PlatformSearchGraphPreparation PrepareGraph(
                PlatformSearchGraphSnapshot graph,
                Action<PlatformSearchGraphPreparation> completed = null)
            {
                oracle.PrepareGraph(graph);
                return PlatformSearchGraphPreparation.Ready(graph);
            }

            public bool IsGraphReady(long graphIdentity, long graphRevision)
            {
                return oracle.IsGraphReady(graphIdentity, graphRevision);
            }

            public PlatformSearchSubmission Submit(
                PlatformSearchRequest request,
                Action<PlatformSearchResult> completed = null)
            {
                pendingRequest = request;
                pendingCallback = completed;
                pendingHandle = new PlatformPathSearchHandle(request.RequestRevision);
                pendingHandle.SetCancelAction(() => CancelObserved = true);
                return PlatformSearchSubmission.Pending(pendingHandle);
            }

            public bool Complete()
            {
                if (pendingHandle == null || !pendingHandle.TryComplete())
                    return false;

                PlatformSearchResult oracleResult = oracle.Submit(pendingRequest).ImmediateResult;
                var translated = new PlatformSearchResult(
                    oracleResult.RequestRevision,
                    oracleResult.GraphIdentity,
                    oracleResult.GraphRevision,
                    BackendId,
                    BackendRevision,
                    oracleResult.FailureReason,
                    Enumerable.Range(0, oracleResult.TargetCount)
                        .Select(oracleResult.GetTarget)
                        .ToArray(),
                    oracleResult.ExpandedNodes,
                    oracleResult.OpenSetPeak);
                pendingCallback?.Invoke(translated);
                return true;
            }
        }

        private sealed class CountingManagedBackend : IPlatformPathSearchBackend
        {
            private readonly ManagedPlatformPathSearchBackend inner = new ManagedPlatformPathSearchBackend();

            public string BackendId => "tests.counting-managed";
            public int BackendRevision => 1;
            public PlatformPathSearchBackendCapabilities Capabilities => inner.Capabilities;
            public int SubmitCount { get; private set; }

            public PlatformSearchGraphPreparation PrepareGraph(
                PlatformSearchGraphSnapshot graph,
                Action<PlatformSearchGraphPreparation> completed = null)
            {
                return inner.PrepareGraph(graph, completed);
            }

            public bool IsGraphReady(long graphIdentity, long graphRevision)
            {
                return inner.IsGraphReady(graphIdentity, graphRevision);
            }

            public PlatformSearchSubmission Submit(
                PlatformSearchRequest request,
                Action<PlatformSearchResult> completed = null)
            {
                SubmitCount++;
                return inner.Submit(request, completed);
            }
        }
    }
}
