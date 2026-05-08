using NUnit.Framework;
using System.Linq;
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

        [Test]
        public void TryRequestPath_ToCenteredHighPlatform_WalksToEdgeBeforeJumping()
        {
            var host = new GameObject("TPlatformPathTest");
            var lower = CreatePlatform("LowerPlatform", new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var upper = CreatePlatform("UpperPlatform", new Vector2(0f, 3f), new Vector2(2f, 0.2f));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(0f, 1.5f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.Config.GroundLayer = 1 << lower.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                host.AddComponent<JumpLinkCalculator>().GenerateJumpLinks();

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(0f, 0.35f, 0f),
                        new Vector3(0f, 3.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, result.FailureReason.ToString());
                Assert.IsNotNull(result.Path);
                Assert.That(result.Path.Commands, Has.Count.GreaterThanOrEqualTo(2));

                var first = result.Path.Commands[0];
                Assert.AreEqual(MoveCommandType.Walk, first.CommandType);
                Assert.Greater(Mathf.Abs(first.Target.x), 0.5f, "The first command should walk to a platform edge, not stay under the target center.");
                Assert.IsTrue(result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        private static GameObject CreatePlatform(string name, Vector2 position, Vector2 size)
        {
            var platform = new GameObject(name);
            platform.transform.position = position;
            var collider = platform.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return platform;
        }
    }
}
