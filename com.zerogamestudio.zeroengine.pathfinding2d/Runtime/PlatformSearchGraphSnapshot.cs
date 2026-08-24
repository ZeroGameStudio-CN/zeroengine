using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformSearchNode
    {
        public readonly int NodeId;
        public readonly Vector3 Position;
        public readonly PlatformNodeType NodeType;
        public readonly bool IsOneWay;
        public readonly float SurfaceY;
        public readonly int SurfaceGroupId;
        public readonly bool IsTransitionAnchor;

        internal PlatformSearchNode(PlatformNodeData source)
        {
            NodeId = source.NodeId;
            Position = source.Position;
            NodeType = source.NodeType;
            IsOneWay = source.IsOneWay;
            SurfaceY = source.SurfaceY;
            SurfaceGroupId = source.SurfaceGroupId;
            IsTransitionAnchor = source.IsTransitionAnchor;
        }
    }

    public readonly struct PlatformSearchLink
    {
        public readonly int LinkId;
        public readonly int FromNodeId;
        public readonly int ToNodeId;
        public readonly int FromNodeIndex;
        public readonly int ToNodeIndex;
        public readonly PlatformLinkType LinkType;
        public readonly float BaseCost;

        internal PlatformSearchLink(
            int linkId,
            PlatformLinkData source,
            int fromNodeIndex,
            int toNodeIndex)
        {
            LinkId = linkId;
            FromNodeId = source.FromNodeId;
            ToNodeId = source.ToNodeId;
            FromNodeIndex = fromNodeIndex;
            ToNodeIndex = toNodeIndex;
            LinkType = source.LinkType;
            BaseCost = source.Cost;
        }
    }

    /// <summary>
    /// Immutable, Unity-object-free graph used by replaceable search backends.
    /// </summary>
    public sealed class PlatformSearchGraphSnapshot
    {
        private readonly PlatformSearchNode[] nodes;
        private readonly PlatformSearchLink[] links;
        private readonly Dictionary<int, int> nodeIndexById;
        private readonly int[] outgoingOffsets;
        private readonly int[] outgoingLinkIndices;
        private readonly int[] incomingOffsets;
        private readonly int[] incomingLinkIndices;

        public long GraphIdentity { get; }
        public long GraphRevision { get; }
        public int SearchCostPolicyRevision { get; }
        public int NodeCount => nodes.Length;
        public int LinkCount => links.Length;

        private PlatformSearchGraphSnapshot(
            long graphIdentity,
            long graphRevision,
            int searchCostPolicyRevision,
            PlatformSearchNode[] nodes,
            PlatformSearchLink[] links,
            Dictionary<int, int> nodeIndexById,
            int[] outgoingOffsets,
            int[] outgoingLinkIndices,
            int[] incomingOffsets,
            int[] incomingLinkIndices)
        {
            GraphIdentity = graphIdentity;
            GraphRevision = graphRevision;
            SearchCostPolicyRevision = searchCostPolicyRevision;
            this.nodes = nodes;
            this.links = links;
            this.nodeIndexById = nodeIndexById;
            this.outgoingOffsets = outgoingOffsets;
            this.outgoingLinkIndices = outgoingLinkIndices;
            this.incomingOffsets = incomingOffsets;
            this.incomingLinkIndices = incomingLinkIndices;
        }

        public PlatformSearchNode GetNode(int nodeIndex)
        {
            if ((uint)nodeIndex >= (uint)nodes.Length)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));

            return nodes[nodeIndex];
        }

        public PlatformSearchLink GetLink(int linkIndex)
        {
            if ((uint)linkIndex >= (uint)links.Length)
                throw new ArgumentOutOfRangeException(nameof(linkIndex));

            return links[linkIndex];
        }

        public bool TryGetNodeIndex(int nodeId, out int nodeIndex)
        {
            return nodeIndexById.TryGetValue(nodeId, out nodeIndex);
        }

        public int GetOutgoingLinkCount(int nodeIndex)
        {
            ValidateNodeIndex(nodeIndex);
            return outgoingOffsets[nodeIndex + 1] - outgoingOffsets[nodeIndex];
        }

        public PlatformSearchLink GetOutgoingLink(int nodeIndex, int offset)
        {
            ValidateNodeIndex(nodeIndex);
            int start = outgoingOffsets[nodeIndex];
            int count = outgoingOffsets[nodeIndex + 1] - start;
            if ((uint)offset >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            return links[outgoingLinkIndices[start + offset]];
        }

        public int GetIncomingLinkCount(int nodeIndex)
        {
            ValidateNodeIndex(nodeIndex);
            return incomingOffsets[nodeIndex + 1] - incomingOffsets[nodeIndex];
        }

        public PlatformSearchLink GetIncomingLink(int nodeIndex, int offset)
        {
            ValidateNodeIndex(nodeIndex);
            int start = incomingOffsets[nodeIndex];
            int count = incomingOffsets[nodeIndex + 1] - start;
            if ((uint)offset >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            return links[incomingLinkIndices[start + offset]];
        }

        internal static bool TryCreate(
            long graphIdentity,
            long graphRevision,
            int searchCostPolicyRevision,
            IReadOnlyList<PlatformNodeData> sourceNodes,
            IReadOnlyList<PlatformLinkData> sourceLinks,
            out PlatformSearchGraphSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            if (graphIdentity <= 0 || graphRevision <= 0)
            {
                error = "Graph identity and revision must be positive.";
                return false;
            }

            if (sourceNodes == null || sourceLinks == null)
            {
                error = "Graph collections cannot be null.";
                return false;
            }

            var copiedNodes = new PlatformSearchNode[sourceNodes.Count];
            var indexById = new Dictionary<int, int>(sourceNodes.Count);
            for (int i = 0; i < sourceNodes.Count; i++)
            {
                PlatformNodeData node = sourceNodes[i];
                if (!IsFinite(node.Position.x) ||
                    !IsFinite(node.Position.y) ||
                    !IsFinite(node.Position.z) ||
                    !IsFinite(node.SurfaceY))
                {
                    error = $"Platform node {node.NodeId} has non-finite geometry.";
                    return false;
                }

                if (!indexById.TryAdd(node.NodeId, i))
                {
                    error = $"Duplicate platform node id {node.NodeId}.";
                    return false;
                }

                copiedNodes[i] = new PlatformSearchNode(node);
            }

            var copiedLinks = new PlatformSearchLink[sourceLinks.Count];
            var outgoingCounts = new int[copiedNodes.Length];
            var incomingCounts = new int[copiedNodes.Length];
            for (int i = 0; i < sourceLinks.Count; i++)
            {
                PlatformLinkData link = sourceLinks[i];
                if (!indexById.TryGetValue(link.FromNodeId, out int fromIndex) ||
                    !indexById.TryGetValue(link.ToNodeId, out int toIndex))
                {
                    error = $"Link {i} references missing nodes {link.FromNodeId}->{link.ToNodeId}.";
                    return false;
                }

                if (float.IsNaN(link.Cost) || float.IsInfinity(link.Cost) || link.Cost < 0f)
                {
                    error = $"Link {i} has invalid cost {link.Cost}.";
                    return false;
                }

                copiedLinks[i] = new PlatformSearchLink(i, link, fromIndex, toIndex);
                outgoingCounts[fromIndex]++;
                incomingCounts[toIndex]++;
            }

            int[] outgoingStarts = BuildOffsets(outgoingCounts);
            int[] incomingStarts = BuildOffsets(incomingCounts);
            int[] outgoingIndices = new int[copiedLinks.Length];
            int[] incomingIndices = new int[copiedLinks.Length];
            int[] outgoingCursors = (int[])outgoingStarts.Clone();
            int[] incomingCursors = (int[])incomingStarts.Clone();
            for (int i = 0; i < copiedLinks.Length; i++)
            {
                PlatformSearchLink link = copiedLinks[i];
                outgoingIndices[outgoingCursors[link.FromNodeIndex]++] = i;
                incomingIndices[incomingCursors[link.ToNodeIndex]++] = i;
            }

            snapshot = new PlatformSearchGraphSnapshot(
                graphIdentity,
                graphRevision,
                searchCostPolicyRevision,
                copiedNodes,
                copiedLinks,
                indexById,
                outgoingStarts,
                outgoingIndices,
                incomingStarts,
                incomingIndices);
            return true;
        }

        private static int[] BuildOffsets(int[] counts)
        {
            var offsets = new int[counts.Length + 1];
            for (int i = 0; i < counts.Length; i++)
                offsets[i + 1] = offsets[i] + counts[i];

            return offsets;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ValidateNodeIndex(int nodeIndex)
        {
            if ((uint)nodeIndex >= (uint)nodes.Length)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));
        }
    }
}
