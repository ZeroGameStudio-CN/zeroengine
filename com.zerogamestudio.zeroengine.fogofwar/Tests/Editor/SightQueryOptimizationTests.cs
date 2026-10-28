using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.FogOfWar.Unity;

namespace ZeroEngine.FogOfWar.Tests
{
    // Core contract: work reduction retains exact sight results and a conservative ordered query.
    public sealed class SightQueryOptimizationTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PreparedRebuildRetainsEveryPointBit(bool fine, bool deduplicate)
        {
            var random = new System.Random(1791100000);
            for (int scenario = 0; scenario < 24; scenario++)
            {
                var edges = Edges(random, 48, 0);
                var baseline = new SightPolygon2D { FineSectorsEnabled = fine, EndpointDeduplicationEnabled = deduplicate };
                var candidate = new SightPolygon2D { FineSectorsEnabled = fine, EndpointDeduplicationEnabled = deduplicate,
                    PreparedIntersectionsEnabled = true };
                Vector2 origin = new((float)random.NextDouble() * 8 - 4, (float)random.NextDouble() * 6 - 3);
                baseline.Rebuild(origin, new Vector2(12, 8), -.6f, 4, edges);
                candidate.Rebuild(origin, new Vector2(12, 8), -.6f, 4, edges);
                Assert.That(candidate.InputAngleCount, Is.EqualTo(baseline.InputAngleCount));
                Assert.That(candidate.Points.Count, Is.EqualTo(baseline.Points.Count));
                Assert.That(candidate.UniformDirectionReuses, Is.GreaterThan(0));
                for (int i = 0; i < baseline.Points.Count; i++)
                {
                    Assert.That(BitConverter.SingleToInt32Bits(candidate.Points[i].x), Is.EqualTo(BitConverter.SingleToInt32Bits(baseline.Points[i].x)));
                    Assert.That(BitConverter.SingleToInt32Bits(candidate.Points[i].y), Is.EqualTo(BitConverter.SingleToInt32Bits(baseline.Points[i].y)));
                }
                for (int i = 0; i < 30; i++)
                {
                    Vector2 point = new((float)random.NextDouble() * 30 - 15, (float)random.NextDouble() * 20 - 10);
                    Assert.That(candidate.Sample(point), Is.EqualTo(baseline.Sample(point)));
                }
            }
        }

        [Test]
        public void TogglingPreparedWorkRetainsInputCountAndResetsReuseObservation()
        {
            var edges = Edges(new System.Random(19), 32, 0);
            var sight = new SightPolygon2D();
            sight.Rebuild(Vector2.zero, new Vector2(10, 8), 1, 3, edges);
            int inputs = sight.InputAngleCount;
            sight.PreparedIntersectionsEnabled = true;
            sight.Rebuild(Vector2.zero, new Vector2(10, 8), 1, 3, edges);
            Assert.That(sight.InputAngleCount, Is.EqualTo(inputs));
            Assert.That(sight.UniformDirectionReuses, Is.GreaterThan(0));
            sight.PreparedIntersectionsEnabled = false;
            sight.Rebuild(Vector2.zero, new Vector2(10, 8), 1, 3, edges);
            Assert.That(sight.InputAngleCount, Is.EqualTo(inputs));
            Assert.That(sight.UniformDirectionReuses, Is.Zero);
        }

        [TestCase(0f)]
        [TestCase(100000f)]
        [TestCase(-100000f)]
        public void SpatialQueryRetainsAllOriginalCircleHitsInOriginalOrder(float shift)
        {
            var random = new System.Random(31);
            var edges = Edges(random, 500, shift);
            var index = new SegmentQueryIndex2D(); index.Rebuild(edges);
            var matches = new List<int>();
            for (int query = 0; query < 32; query++)
            {
                Vector2 origin = new(shift + (float)random.NextDouble() * 20 - 10, (float)random.NextDouble() * 20 - 10);
                float radius = (float)random.NextDouble() * 10 + .001f;
                Assert.That(index.TryQuery(origin - Vector2.one * radius, origin + Vector2.one * radius, matches), Is.True);
                for (int i = 1; i < matches.Count; i++) Assert.That(matches[i], Is.GreaterThan(matches[i - 1]));
                for (int i = 0; i < edges.Count; i++)
                {
                    var edge = edges[i]; var ab = edge.B - edge.A;
                    float t = Mathf.Clamp01(Vector2.Dot(origin - edge.A, ab) / Mathf.Max(.0000001f, ab.sqrMagnitude));
                    if ((origin - (edge.A + ab * t)).sqrMagnitude <= radius * radius)
                        Assert.That(matches.Contains(i), Is.True, "An original accepted edge must not be rejected by the broad phase.");
                }
            }
        }

        [Test]
        public void RebuildAndEmptyQueriesDoNotRetainOldIndices()
        {
            var index = new SegmentQueryIndex2D(); var matches = new List<int> { 999 };
            index.Rebuild(new[] { new SightPolygon2D.Segment(Vector2.zero, Vector2.one) });
            Assert.That(index.TryQuery(-Vector2.one, Vector2.one * 2, matches), Is.True);
            Assert.That(matches, Is.EqualTo(new[] { 0 }));
            index.Rebuild(Array.Empty<SightPolygon2D.Segment>());
            Assert.That(index.TryQuery(-Vector2.one, Vector2.one, matches), Is.True);
            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void UnsupportedCoordinatesRequestFallbackWithoutStaleOutput()
        {
            var index = new SegmentQueryIndex2D(); var matches = new List<int> { 999 };
            index.Rebuild(new[] { new SightPolygon2D.Segment(new Vector2(float.NaN, 0), Vector2.one) });
            Assert.That(index.TryQuery(-Vector2.one, Vector2.one, matches), Is.False);
            Assert.That(matches, Is.Empty);
            index.Rebuild(new[] { new SightPolygon2D.Segment(Vector2.zero, Vector2.one) });
            Assert.That(index.TryQuery(new Vector2(float.NegativeInfinity, 0), Vector2.one, matches), Is.False);
            Assert.That(matches, Is.Empty);
        }

        private static List<SightPolygon2D.Segment> Edges(System.Random random, int count, float shift)
        {
            var result = new List<SightPolygon2D.Segment>(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 a = new(shift + (float)random.NextDouble() * 80 - 40, (float)random.NextDouble() * 60 - 30);
                Vector2 b = a + new Vector2((float)random.NextDouble() * 10 - 5, (float)random.NextDouble() * 10 - 5);
                result.Add(new SightPolygon2D.Segment(a, b));
            }
            return result;
        }
    }
}
