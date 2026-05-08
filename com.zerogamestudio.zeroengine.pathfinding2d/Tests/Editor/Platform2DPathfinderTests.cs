using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class Platform2DPathfinderTests
    {
        [Test]
        public void TryRequestPathWithoutGraph_ReturnsMissingGraphReason()
        {
            var host = new GameObject("PathfinderMissingGraphTest");
            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(Vector3.zero, Vector3.right),
                    out var result);

                Assert.IsFalse(success);
                Assert.IsFalse(result.Success);
                Assert.AreEqual(PlatformPathFailureReason.MissingGraphGenerator, result.FailureReason);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TryRequestPathWithUngeneratedGraph_ReturnsGraphNotGenerated()
        {
            var host = new GameObject("UngeneratedGraphTest");
            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(Vector3.zero, Vector3.right),
                    out var result);

                Assert.IsFalse(success);
                Assert.AreEqual(PlatformPathFailureReason.GraphNotGenerated, result.FailureReason);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ProjectTargetToGround_ReturnsPlatformHitPoint()
        {
            var platform = new GameObject("ProjectionPlatform");
            try
            {
                platform.transform.position = new Vector3(0f, 2f, 0f);
                var collider = platform.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(6f, 0.2f);
                Physics2D.SyncTransforms();

                var projected = PlatformTargetProjection.ProjectTargetToGround(new Vector2(0f, 2.8f), ~0, 1.5f);

                Assert.AreEqual(2.1f, projected.y, 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(platform);
            }
        }

        [Test]
        public void GetSnapshot_ReportsMissingGraph()
        {
            var host = new GameObject("SnapshotMissingGraphTest");
            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();

                var snapshot = pathfinder.GetSnapshot();

                Assert.IsFalse(snapshot.HasGraphGenerator);
                Assert.AreEqual(0, snapshot.NodeCount);
                Assert.AreEqual(0, snapshot.LinkCount);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
