using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// Package-owned deterministic Dijkstra backend. It is the no-plugin default and reference oracle.
    /// </summary>
    public sealed class ManagedPlatformPathSearchBackend : IPlatformPathSearchBackend
    {
        private const float CostEpsilon = 0.00001f;

        private PlatformSearchGraphSnapshot preparedGraph;
        private float[] costs = Array.Empty<float>();
        private int[] previousLinkIndices = Array.Empty<int>();
        private bool[] closed = Array.Empty<bool>();
        private int[] targetIndices = Array.Empty<int>();
        private readonly MinHeap heap = new MinHeap(4);

        public string BackendId => "zeroengine.managed";
        public int BackendRevision => 1;
        public PlatformPathSearchBackendCapabilities Capabilities =>
            PlatformPathSearchBackendCapabilities.MultipleTargets |
            PlatformPathSearchBackendCapabilities.SingleThreaded;

        public PlatformSearchGraphPreparation PrepareGraph(
            PlatformSearchGraphSnapshot graph,
            Action<PlatformSearchGraphPreparation> completed = null)
        {
            if (graph == null)
                return PlatformSearchGraphPreparation.Failed(null, PlatformSearchFailureReason.GraphUnavailable);

            preparedGraph = graph;
            return PlatformSearchGraphPreparation.Ready(graph);
        }

        public bool IsGraphReady(long graphIdentity, long graphRevision)
        {
            return preparedGraph != null &&
                   preparedGraph.GraphIdentity == graphIdentity &&
                   preparedGraph.GraphRevision == graphRevision;
        }

        public PlatformSearchSubmission Submit(
            PlatformSearchRequest request,
            Action<PlatformSearchResult> completed = null)
        {
            bool captureDiagnostics = PlatformPathfindingDiagnostics.IsCapturing;
            if (captureDiagnostics)
            {
                PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.FindPathCalls);
                PlatformPathfindingDiagnostics.Add(
                    PlatformPathfindingCounterKind.CandidateTargetsEvaluated,
                    request?.TargetCount ?? 0);
            }

            PlatformSearchResult result;
            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.FindPath))
            {
                result = Search(request);
            }

            if (captureDiagnostics)
            {
                PlatformPathfindingDiagnostics.Add(
                    PlatformPathfindingCounterKind.ExpandedNodes,
                    result.ExpandedNodes);
                PlatformPathfindingDiagnostics.RecordMaximum(
                    PlatformPathfindingCounterKind.OpenSetPeak,
                    result.OpenSetPeak);
            }

            return result.FailureReason == PlatformSearchFailureReason.InvalidRequest ||
                   result.FailureReason == PlatformSearchFailureReason.GraphUnavailable ||
                   result.FailureReason == PlatformSearchFailureReason.StaleGraph
                ? PlatformSearchSubmission.Rejected(result)
                : PlatformSearchSubmission.Immediate(result);
        }

        private PlatformSearchResult Search(PlatformSearchRequest request)
        {
            if (request == null || request.Graph == null || request.TargetCount == 0)
                return Failure(request, PlatformSearchFailureReason.InvalidRequest);

            PlatformSearchGraphSnapshot graph = request.Graph;
            if (!request.CostContext.IsValid ||
                request.CostContext.PolicyRevision != graph.SearchCostPolicyRevision)
                return Failure(request, PlatformSearchFailureReason.InvalidRequest);

            if (preparedGraph == null)
                return Failure(request, PlatformSearchFailureReason.GraphUnavailable);

            if (!IsGraphReady(graph.GraphIdentity, graph.GraphRevision))
                return Failure(request, PlatformSearchFailureReason.StaleGraph);

            if (!graph.TryGetNodeIndex(request.SourceNodeId, out int sourceIndex))
                return Failure(request, PlatformSearchFailureReason.StartNodeNotFound);

            EnsureCapacity(graph.NodeCount, request.TargetCount);
            for (int i = 0; i < request.TargetCount; i++)
            {
                if (!graph.TryGetNodeIndex(request.GetTargetNodeId(i), out targetIndices[i]))
                    return Failure(request, PlatformSearchFailureReason.TargetNodeNotFound);
            }

            if (IsExpired(request.DeadlineTimestamp))
                return Failure(request, PlatformSearchFailureReason.Timeout);

            int nodeCount = graph.NodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                costs[i] = float.PositiveInfinity;
                previousLinkIndices[i] = -1;
                closed[i] = false;
            }

            heap.Clear(Math.Max(4, Math.Min(graph.LinkCount + 1, 256)));
            costs[sourceIndex] = 0f;
            heap.Push(sourceIndex, 0f);

            int remainingTargets = CountUniqueTargets(targetIndices, request.TargetCount);
            long expandedNodes = 0;
            long openSetPeak = 1;
            while (heap.Count > 0)
            {
                if (IsExpired(request.DeadlineTimestamp))
                    return Failure(request, PlatformSearchFailureReason.Timeout, expandedNodes, openSetPeak);

                HeapEntry entry = heap.Pop();
                int currentIndex = entry.NodeIndex;
                if (closed[currentIndex] || entry.Cost > costs[currentIndex] + CostEpsilon)
                    continue;

                closed[currentIndex] = true;
                expandedNodes++;
                if (Contains(targetIndices, request.TargetCount, currentIndex))
                {
                    remainingTargets--;
                    if (remainingTargets == 0)
                        break;
                }

                int outgoingCount = graph.GetOutgoingLinkCount(currentIndex);
                for (int i = 0; i < outgoingCount; i++)
                {
                    PlatformSearchLink link = graph.GetOutgoingLink(currentIndex, i);
                    int destinationIndex = link.ToNodeIndex;
                    if (closed[destinationIndex])
                        continue;

                    float stepCost = request.CostContext.GetTraversalCost(
                        link,
                        graph.GetNode(destinationIndex));
                    float candidateCost = costs[currentIndex] + stepCost;
                    if (candidateCost + CostEpsilon >= costs[destinationIndex])
                        continue;

                    costs[destinationIndex] = candidateCost;
                    previousLinkIndices[destinationIndex] = FindLinkIndex(graph, link.LinkId);
                    heap.Push(destinationIndex, candidateCost);
                    if (heap.Count > openSetPeak)
                        openSetPeak = heap.Count;
                }
            }

            var targetResults = new PlatformSearchTargetResult[request.TargetCount];
            bool anyFound = false;
            for (int i = 0; i < request.TargetCount; i++)
            {
                int targetIndex = targetIndices[i];
                int resolvedIndex = targetIndex;
                bool partial = false;
                if (float.IsPositiveInfinity(costs[targetIndex]))
                {
                    if (!request.AllowPartialPath ||
                        !TryFindClosestReachableNode(graph, costs, sourceIndex, targetIndex, out resolvedIndex))
                    {
                        targetResults[i] = new PlatformSearchTargetResult(
                            request.GetTargetNodeId(i),
                            request.GetTargetNodeId(i),
                            false,
                            false,
                            float.PositiveInfinity,
                            Array.Empty<int>());
                        continue;
                    }

                    partial = true;
                }

                int[] linkIds = ReconstructLinkIds(graph, previousLinkIndices, sourceIndex, resolvedIndex);
                targetResults[i] = new PlatformSearchTargetResult(
                    request.GetTargetNodeId(i),
                    graph.GetNode(resolvedIndex).NodeId,
                    true,
                    partial,
                    costs[resolvedIndex],
                    linkIds);
                anyFound = true;
            }

            return new PlatformSearchResult(
                request.RequestRevision,
                graph.GraphIdentity,
                graph.GraphRevision,
                BackendId,
                BackendRevision,
                anyFound ? PlatformSearchFailureReason.None : PlatformSearchFailureReason.NoPath,
                targetResults,
                expandedNodes,
                openSetPeak);
        }

        private PlatformSearchResult Failure(
            PlatformSearchRequest request,
            PlatformSearchFailureReason reason,
            long expandedNodes = 0,
            long openSetPeak = 0)
        {
            PlatformSearchGraphSnapshot graph = request?.Graph;
            return new PlatformSearchResult(
                request?.RequestRevision ?? 0,
                graph?.GraphIdentity ?? 0,
                graph?.GraphRevision ?? 0,
                BackendId,
                BackendRevision,
                reason,
                Array.Empty<PlatformSearchTargetResult>(),
                expandedNodes,
                openSetPeak);
        }

        private static bool IsExpired(long deadlineTimestamp)
        {
            return deadlineTimestamp > 0 && Stopwatch.GetTimestamp() >= deadlineTimestamp;
        }

        private void EnsureCapacity(int nodeCount, int targetCount)
        {
            if (costs.Length < nodeCount)
            {
                int capacity = Math.Max(nodeCount, Math.Max(4, costs.Length * 2));
                costs = new float[capacity];
                previousLinkIndices = new int[capacity];
                closed = new bool[capacity];
            }

            if (targetIndices.Length < targetCount)
                targetIndices = new int[Math.Max(targetCount, Math.Max(4, targetIndices.Length * 2))];
        }

        private static int CountUniqueTargets(int[] targetIndices, int count)
        {
            int unique = 0;
            for (int i = 0; i < count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++)
                {
                    if (targetIndices[j] == targetIndices[i])
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                    unique++;
            }

            return unique;
        }

        private static bool Contains(int[] values, int count, int value)
        {
            for (int i = 0; i < count; i++)
            {
                if (values[i] == value)
                    return true;
            }

            return false;
        }

        private static int FindLinkIndex(PlatformSearchGraphSnapshot graph, int linkId)
        {
            // LinkIds are assigned from the committed source-link index and are contiguous.
            if ((uint)linkId >= (uint)graph.LinkCount || graph.GetLink(linkId).LinkId != linkId)
                throw new InvalidOperationException($"Snapshot link id {linkId} is not contiguous.");
            return linkId;
        }

        private static bool TryFindClosestReachableNode(
            PlatformSearchGraphSnapshot graph,
            float[] costs,
            int sourceIndex,
            int targetIndex,
            out int closestIndex)
        {
            Vector3 targetPosition = graph.GetNode(targetIndex).Position;
            float closestDistance = float.PositiveInfinity;
            closestIndex = -1;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (i == sourceIndex || float.IsPositiveInfinity(costs[i]))
                    continue;

                float distance = Vector2.Distance(graph.GetNode(i).Position, targetPosition);
                if (distance + CostEpsilon < closestDistance ||
                    (Mathf.Abs(distance - closestDistance) <= CostEpsilon && i < closestIndex))
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex >= 0;
        }

        private static int[] ReconstructLinkIds(
            PlatformSearchGraphSnapshot graph,
            int[] previousLinkIndices,
            int sourceIndex,
            int destinationIndex)
        {
            if (sourceIndex == destinationIndex)
                return Array.Empty<int>();

            var reversed = new List<int>();
            int currentIndex = destinationIndex;
            while (currentIndex != sourceIndex)
            {
                int linkIndex = previousLinkIndices[currentIndex];
                if (linkIndex < 0)
                    return Array.Empty<int>();

                PlatformSearchLink link = graph.GetLink(linkIndex);
                reversed.Add(link.LinkId);
                currentIndex = link.FromNodeIndex;
            }

            reversed.Reverse();
            return reversed.ToArray();
        }

        private readonly struct HeapEntry
        {
            public readonly int NodeIndex;
            public readonly float Cost;
            public readonly long Sequence;

            public HeapEntry(int nodeIndex, float cost, long sequence)
            {
                NodeIndex = nodeIndex;
                Cost = cost;
                Sequence = sequence;
            }
        }

        private sealed class MinHeap
        {
            private readonly List<HeapEntry> entries;
            private long nextSequence;

            public int Count => entries.Count;

            public MinHeap(int capacity)
            {
                entries = new List<HeapEntry>(capacity);
            }

            public void Clear(int capacity)
            {
                entries.Clear();
                if (entries.Capacity < capacity)
                    entries.Capacity = capacity;
                nextSequence = 0;
            }

            public void Push(int nodeIndex, float cost)
            {
                var entry = new HeapEntry(nodeIndex, cost, nextSequence++);
                entries.Add(entry);
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!ComesBefore(entries[index], entries[parent]))
                        break;
                    Swap(index, parent);
                    index = parent;
                }
            }

            public HeapEntry Pop()
            {
                HeapEntry root = entries[0];
                int last = entries.Count - 1;
                entries[0] = entries[last];
                entries.RemoveAt(last);
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= entries.Count)
                        break;

                    int right = left + 1;
                    int best = right < entries.Count && ComesBefore(entries[right], entries[left])
                        ? right
                        : left;
                    if (!ComesBefore(entries[best], entries[index]))
                        break;
                    Swap(index, best);
                    index = best;
                }

                return root;
            }

            private static bool ComesBefore(HeapEntry a, HeapEntry b)
            {
                if (a.Cost < b.Cost - CostEpsilon)
                    return true;
                if (a.Cost > b.Cost + CostEpsilon)
                    return false;
                if (a.NodeIndex != b.NodeIndex)
                    return a.NodeIndex < b.NodeIndex;
                return a.Sequence < b.Sequence;
            }

            private void Swap(int a, int b)
            {
                HeapEntry temp = entries[a];
                entries[a] = entries[b];
                entries[b] = temp;
            }
        }
    }
}
