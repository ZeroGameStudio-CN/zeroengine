using System;
using System.Collections.Generic;
using UnityEngine;
namespace ZeroEngine.FogOfWar.Unity
{
    // Caller-owned index of immutable segment bounds. Queries retain original segment order.
    public sealed class SegmentQueryIndex2D
    {
        private const int LeafSize = 8;
        private struct Box { internal Vector2 Min, Max; }
        private struct Node { internal Box Bounds; internal int Left, Right, First, Count; }
        private readonly IComparer<int> compareX, compareY;
        private Box[] boxes = Array.Empty<Box>();
        private Node[] nodes = Array.Empty<Node>();
        private int[] order = Array.Empty<int>();
        private readonly int[] stack = new int[64];
        private int count, used, root = -1;
        private float coordinateMagnitude;
        private bool supported;
        public SegmentQueryIndex2D()
        {
            compareX = Comparer<int>.Create((a,b) => Center(a, true).CompareTo(Center(b, true)));
            compareY = Comparer<int>.Create((a,b) => Center(a, false).CompareTo(Center(b, false)));
        }
        private float Center(int i, bool x) => x ? boxes[i].Min.x * .5f + boxes[i].Max.x * .5f
            : boxes[i].Min.y * .5f + boxes[i].Max.y * .5f;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        private static float Magnitude(Vector2 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y));
        public void Rebuild(IReadOnlyList<SightPolygon2D.Segment> segments)
        {
            count = segments.Count; used = 0; root = -1; coordinateMagnitude = 0; supported = true;
            if (boxes.Length < count)
            {
                int capacity = Math.Max(count, Math.Max(8, boxes.Length * 2));
                Array.Resize(ref boxes, capacity); Array.Resize(ref order, capacity); Array.Resize(ref nodes, checked(capacity * 2));
            }
            for (int i = 0; i < count; i++)
            {
                var edge = segments[i]; order[i] = i;
                if (!Finite(edge.A) || !Finite(edge.B)) { supported = false; continue; }
                boxes[i] = new Box { Min = Vector2.Min(edge.A, edge.B), Max = Vector2.Max(edge.A, edge.B) };
                coordinateMagnitude = Mathf.Max(coordinateMagnitude, Mathf.Max(Magnitude(edge.A), Magnitude(edge.B)));
            }
            if (supported && count > 0) root = Build(0, count);
        }
        private int Build(int first, int length)
        {
            var bounds = boxes[order[first]];
            for (int i = first + 1; i < first + length; i++)
            { bounds.Min = Vector2.Min(bounds.Min, boxes[order[i]].Min); bounds.Max = Vector2.Max(bounds.Max, boxes[order[i]].Max); }
            int id = used++;
            if (length <= LeafSize) { nodes[id] = new Node { Bounds = bounds, First = first, Count = length }; return id; }
            Array.Sort(order, first, length, bounds.Max.x - bounds.Min.x >= bounds.Max.y - bounds.Min.y ? compareX : compareY);
            int leftLength = length / 2, left = Build(first, leftLength), right = Build(first + leftLength, length - leftLength);
            nodes[id] = new Node { Bounds = bounds, Left = left, Right = right }; return id;
        }
        public bool TryQuery(Vector2 min, Vector2 max, List<int> matches)
        {
            matches.Clear();
            if (!supported || !Finite(min) || !Finite(max) || min.x > max.x || min.y > max.y) return false;
            if (root < 0) return true;
            // Retain conservative coverage of the original clamped projection's float rounding.
            float padding = Mathf.Max(.001f, (coordinateMagnitude + Mathf.Max(Magnitude(min), Magnitude(max))) * .000002f);
            min -= Vector2.one * padding; max += Vector2.one * padding;
            int top = 0; stack[top++] = root;
            while (top > 0)
            {
                var node = nodes[stack[--top]];
                if (node.Bounds.Max.x < min.x || node.Bounds.Min.x > max.x || node.Bounds.Max.y < min.y || node.Bounds.Min.y > max.y) continue;
                if (node.Count > 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int index = order[i]; var box = boxes[index];
                        if (box.Max.x >= min.x && box.Min.x <= max.x && box.Max.y >= min.y && box.Min.y <= max.y) matches.Add(index);
                    }
                }
                else { stack[top++] = node.Right; stack[top++] = node.Left; }
            }
            matches.Sort(); return true;
        }
    }
}
