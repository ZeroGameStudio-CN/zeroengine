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
                Assert.AreEqual(PlatformPathCompletionKind.Failed, result.CompletionKind);
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
                Assert.AreEqual(PlatformPathCompletionKind.Failed, result.CompletionKind);
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
                Assert.AreEqual(PlatformPathCompletionKind.Failed, snapshot.CompletionKind);
                Assert.AreEqual("none", snapshot.CommandDebug);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TryRequestPath_WhenAlreadyAtResolvedTarget_ReturnsArrivedKind()
        {
            var host = new GameObject("ArrivedKindTest");
            var platform = CreatePlatform("ArrivedKindPlatform", new Vector2(0f, 0f), new Vector2(6f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, platform, platform, maxJumpVelocity: 16f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(0f, 0.35f, 0f),
                        new Vector3(0.1f, 0.35f, 0f),
                        forceRequest: true,
                        projectTargetToGround: false),
                    out var result);

                Assert.IsTrue(success);
                Assert.AreEqual(PlatformPathCompletionKind.Arrived, result.CompletionKind);
                Assert.AreEqual(PlatformPathCompletionKind.Arrived, result.Path.CompletionKind);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform);
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
                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxJumpVelocity = 16f;
                jumpLinkCalculator.GenerateJumpLinks();

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

        [Test]
        public void TryRequestPath_TPlatformHighTarget_DoesNotGenerateVerticalWalk()
        {
            var host = new GameObject("TPlatformVerticalWalkRegression");
            var lower = CreatePlatform("TPlatformLower", new Vector2(58f, 3f), new Vector2(12f, 0.2f));
            var upper = CreatePlatform("TPlatformUpper", new Vector2(62f, 7f), new Vector2(16f, 0.2f));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(62f, 5f);
                graph.Config.ScanSize = new Vector2(30f, 14f);
                graph.Config.GroundLayer = 1 << lower.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxJumpVelocity = 18f;
                jumpLinkCalculator.GenerateJumpLinks();

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(53.49f, 3.12f, 0f),
                        new Vector3(69.98f, 13.03f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.AreEqual(PlatformPathCompletionKind.FullPath, result.CompletionKind, BuildPathDebug(result));
                AssertNoVerticalWalkCommands(result);
                Assert.IsTrue(
                    result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump),
                    $"Expected a jump command for high T-platform traversal. {BuildPathDebug(result)}");
                Assert.IsFalse(
                    result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Fall || command.CommandType == MoveCommandType.DropDown),
                    $"Elevated T-platform traversal should not start by going down. {BuildPathDebug(result)}");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void TryRequestPath_WhenUpperNodeIsCloserByDistance_StartsFromGroundSurface()
        {
            var host = new GameObject("StartSurfaceGroupRegression");
            var lower = CreatePlatform("StartSurfaceLower", new Vector2(0f, 0f), new Vector2(8f, 0.2f));
            var upper = CreatePlatform("StartSurfaceUpper", new Vector2(0.4f, 3.5f), new Vector2(3f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, lower, upper, maxJumpVelocity: 16f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(0f, 0.12f, 0f),
                        new Vector3(0.4f, 4.0f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                AssertNoVerticalWalkCommands(result);
                Assert.AreEqual(
                    result.Path.StartPosition.y,
                    result.Path.Commands.First().Target.y,
                    0.5f,
                    $"First command should stay on the player's current ground surface. {BuildPathDebug(result)}");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void IsCurrentCommandComplete_WalkToNearEdge_DoesNotUseGlobalArriveDistance()
        {
            var host = new GameObject("NearEdgeWalkCompletionTest");
            var lower = CreatePlatform("NearEdgeLowerPlatform", new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var upper = CreatePlatform("NearEdgeUpperPlatform", new Vector2(0f, 3f), new Vector2(2f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, lower, upper, maxJumpVelocity: 16f);
                var start = new Vector3(-0.3f, 0.35f, 0f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        start,
                        new Vector3(0f, 3.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.That(result.Path.Commands, Has.Count.GreaterThanOrEqualTo(2));
                Assert.AreEqual(MoveCommandType.Walk, result.Path.Commands[0].CommandType);
                Assert.Less(Mathf.Abs(start.x - result.Path.Commands[0].Target.x), pathfinder.Config.ArriveDistance);
                Assert.IsFalse(pathfinder.IsCurrentCommandComplete(start, isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void TryRequestPath_ToOffsetHighPlatform_WalksTowardSameSideEdgeBeforeJumping()
        {
            var host = new GameObject("OffsetHighPlatformPathTest");
            var lower = CreatePlatform("OffsetLowerPlatform", new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var upper = CreatePlatform("OffsetUpperPlatform", new Vector2(4f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, lower, upper, maxJumpVelocity: 16f);
                var start = new Vector3(1.4f, 0.35f, 0f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        start,
                        new Vector3(4f, 3.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.That(result.Path.Commands, Has.Count.GreaterThanOrEqualTo(2));

                var first = result.Path.Commands[0];
                Assert.AreEqual(MoveCommandType.Walk, first.CommandType);
                Assert.Greater(first.Target.x, start.x, "The first command should walk toward the reachable same-side edge, not away from the target platform.");
                Assert.IsTrue(result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump));
                Assert.IsFalse(pathfinder.IsCurrentCommandComplete(start, isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void TryRequestPath_ToOffsetHighPlatform_DoesNotPreferOppositeEdgeWhenSameSideEdgeIsReachable()
        {
            var host = new GameObject("OffsetHighPlatformSameSideTest");
            var lower = CreatePlatform("SameSideLowerPlatform", new Vector2(0f, 0f), new Vector2(12f, 0.2f));
            var upper = CreatePlatform("SameSideUpperPlatform", new Vector2(4f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, lower, upper, maxJumpVelocity: 16f);
                var start = new Vector3(1.6f, 0.35f, 0f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        start,
                        new Vector3(4f, 3.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.That(result.Path.Commands, Has.Count.GreaterThanOrEqualTo(2));

                var first = result.Path.Commands[0];
                Assert.AreEqual(MoveCommandType.Walk, first.CommandType);
                Assert.Greater(first.Target.x, 0f);
                Assert.IsTrue(result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void TryRequestPath_ElevatedFarTarget_DoesNotReturnFallOnlyPartialPath()
        {
            var host = new GameObject("ElevatedFarTargetNoFallOnlyPartialTest");
            var startPlatform = CreatePlatform("StartPlatform", new Vector2(20f, 0f), new Vector2(3f, 0.2f));
            var lowerRoute = CreatePlatform("LowerRoutePlatform", new Vector2(40f, -8f), new Vector2(40f, 0.2f));
            var targetPlatform = CreatePlatform("UnreachableHighTargetPlatform", new Vector2(53f, 8f), new Vector2(3f, 0.2f));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(37f, 0f);
                graph.Config.ScanSize = new Vector2(50f, 22f);
                graph.Config.GroundLayer = 1 << startPlatform.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxJumpVelocity = 8f;
                jumpLinkCalculator.Config.MaxJumpHeight = 4f;
                jumpLinkCalculator.Config.MaxHorizontalDistance = 4f;
                jumpLinkCalculator.GenerateJumpLinks();

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(20.5f, 0.35f, 0f),
                        new Vector3(53f, 8.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                bool hasFallOnlyRoute = result.Path?.Commands != null &&
                                        result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Fall) &&
                                        !result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump);

                Assert.IsFalse(success && hasFallOnlyRoute, BuildPathDebug(result));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(startPlatform);
                Object.DestroyImmediate(lowerRoute);
                Object.DestroyImmediate(targetPlatform);
            }
        }

        [Test]
        public void TryRequestPath_ElevatedFarTarget_DoesNotReturnWalkOnlyLowPartialPath()
        {
            var host = new GameObject("ElevatedFarTargetNoWalkOnlyPartialTest");
            var lowerRoute = CreatePlatform("LowerWalkOnlyRoutePlatform", new Vector2(35f, 0f), new Vector2(40f, 0.2f));
            var targetPlatform = CreatePlatform("UnreachableWalkOnlyHighTargetPlatform", new Vector2(53f, 8f), new Vector2(3f, 0.2f));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(35f, 4f);
                graph.Config.ScanSize = new Vector2(45f, 18f);
                graph.Config.GroundLayer = 1 << lowerRoute.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxJumpVelocity = 8f;
                jumpLinkCalculator.Config.MaxJumpHeight = 4f;
                jumpLinkCalculator.Config.MaxHorizontalDistance = 4f;
                jumpLinkCalculator.GenerateJumpLinks();

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(20.5f, 0.35f, 0f),
                        new Vector3(53f, 8.6f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                bool hasWalkOnlyLowRoute = result.Path?.Commands != null &&
                                           result.Path.Commands.All(command => command.CommandType == MoveCommandType.Walk) &&
                                           result.Path.Commands.Count > 0 &&
                                           result.Path.Commands[result.Path.Commands.Count - 1].Target.y < 7.5f;

                Assert.IsFalse(success && hasWalkOnlyLowRoute, BuildPathDebug(result));
                Assert.AreNotEqual(PlatformPathCompletionKind.FullPath, result.CompletionKind, BuildPathDebug(result));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lowerRoute);
                Object.DestroyImmediate(targetPlatform);
            }
        }

        [Test]
        public void TryRequestPath_SameColliderStartBelowUpperPlatform_UsesLowerSurfaceRoute()
        {
            var host = new GameObject("SameColliderStartBelowUpperRouteTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderLayeredRoute",
                (new Vector2(35f, -0.1f), new Vector2(40f, 0.2f)),
                (new Vector2(65f, 3.9f), new Vector2(20f, 0.2f)),
                (new Vector2(35.5f, 9.9f), new Vector2(27f, 0.2f)));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(40f, 5f);
                graph.Config.ScanSize = new Vector2(80f, 24f);
                graph.Config.GroundLayer = 1 << platform.gameObject.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1.5f;
                graph.Config.EdgeInset = 0.3f;

                graph.GeneratePlatformGraph();
                var startGroup = graph.FindSurfaceGroupAt(new Vector2(29.27f, 5.92f), platform, 8f);
                Assert.IsTrue(graph.TryGetSurfaceSegment(startGroup, out var startSegment), "Expected a start surface segment.");
                Assert.AreEqual(0f, startSegment.Y, 0.05f, "Start segment must be the lower floor, not the overhead target platform.");

                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxJumpVelocity = 99f;
                jumpLinkCalculator.Config.MaxHorizontalDistance = 6f;
                jumpLinkCalculator.Config.MaxJumpHeight = 6f;
                jumpLinkCalculator.GenerateJumpLinks();

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(29.27f, 5.92f, 0f),
                        new Vector3(28.81f, 11.06f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.AreEqual(PlatformPathCompletionKind.FullPath, result.CompletionKind, BuildPathDebug(result));
                Assert.That(result.Path.Commands.Count(command => command.CommandType == MoveCommandType.Jump), Is.GreaterThanOrEqualTo(2), BuildPathDebug(result));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void TryRequestPath_SameColliderTargetCandidates_StayOnTargetSurfaceGroup()
        {
            var host = new GameObject("SameColliderTargetSurfaceGroupTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderMultiPlatform",
                (new Vector2(0f, 0f), new Vector2(14f, 0.2f)),
                (new Vector2(-4f, 3f), new Vector2(2f, 0.2f)),
                (new Vector2(4f, 3f), new Vector2(2f, 0.2f)));

            try
            {
                var pathfinder = CreatePathfinderForSingleCollider(host, platform, maxJumpVelocity: 16f);
                var graph = pathfinder.GraphGenerator;
                var target = new Vector3(4f, 3.6f, 0f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(0f, 0.35f, 0f),
                        target,
                        forceRequest: true,
                        projectTargetToGround: true),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.IsTrue(result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Jump), BuildPathDebug(result));

                var targetNode = graph.FindNearestNodeOnPlatform(result.ResolvedTarget, platform, pathfinder.Config.MaxNodeSearchRadius);
                Assert.IsTrue(targetNode.HasValue, BuildPathDebug(result));

                var lastCommandNode = graph.FindNearestNode(
                    result.Path.Commands[result.Path.Commands.Count - 1].Target,
                    pathfinder.Config.MaxNodeSearchRadius);
                Assert.IsTrue(lastCommandNode.HasValue, BuildPathDebug(result));
                Assert.AreEqual(targetNode.Value.SurfaceGroupId, lastCommandNode.Value.SurfaceGroupId, BuildPathDebug(result));
                Assert.That(pathfinder.GetSnapshot().CommandDebug, Does.Contain("/group="));

                var oneWayHost = new GameObject("SameColliderTargetSurfaceGroupOneWayHost");
                try
                {
                    var oneWayPathfinder = CreatePathfinderForSingleCollider(
                        oneWayHost,
                        platform,
                        maxJumpVelocity: 16f,
                        oneWay: true);
                    bool oneWaySuccess = oneWayPathfinder.TryRequestPath(
                        new PlatformPathRequest(
                            new Vector3(0f, 0.35f, 0f),
                            target,
                            forceRequest: true,
                            projectTargetToGround: true),
                        out var oneWayResult);

                    Assert.IsTrue(oneWaySuccess, BuildPathDebug(oneWayResult));
                    var oneWayTargetNode = oneWayPathfinder.GraphGenerator.FindNearestNodeOnPlatform(
                        oneWayResult.ResolvedTarget,
                        platform,
                        oneWayPathfinder.Config.MaxNodeSearchRadius);
                    var oneWayFinalNode = oneWayPathfinder.GraphGenerator.FindNearestNode(
                        oneWayResult.Path.Commands[oneWayResult.Path.Commands.Count - 1].Target,
                        oneWayPathfinder.Config.MaxNodeSearchRadius);
                    Assert.IsTrue(oneWayTargetNode.HasValue, BuildPathDebug(oneWayResult));
                    Assert.IsTrue(oneWayFinalNode.HasValue, BuildPathDebug(oneWayResult));
                    Assert.AreEqual(oneWayTargetNode.Value.SurfaceGroupId, oneWayFinalNode.Value.SurfaceGroupId, BuildPathDebug(oneWayResult));
                }
                finally
                {
                    Object.DestroyImmediate(oneWayHost);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateWalkLinks_SameColliderGap_DoesNotConnectSurfaceGroups()
        {
            var host = new GameObject("SameColliderGapWalkLinkTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderGapPlatform",
                (new Vector2(-3f, 0f), new Vector2(2f, 0.2f)),
                (new Vector2(3f, 0f), new Vector2(2f, 0.2f)));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(0f, 0f);
                graph.Config.ScanSize = new Vector2(10f, 4f);
                graph.Config.GroundLayer = 1 << platform.gameObject.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 0.75f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();

                Assert.That(graph.Nodes.Select(node => node.SurfaceGroupId).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
                Assert.IsFalse(graph.Links.Any(link =>
                {
                    var from = graph.GetNode(link.FromNodeId);
                    var to = graph.GetNode(link.ToNodeId);
                    return link.LinkType == PlatformLinkType.Walk &&
                           from.HasValue &&
                           to.HasValue &&
                           from.Value.SurfaceGroupId != to.Value.SurfaceGroupId;
                }));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GeneratePlatformGraph_MultiPathPolygon_ExposesSurfaceSegments()
        {
            var host = new GameObject("SurfaceSegmentMetadataTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SurfaceSegmentMetadataPlatform",
                (new Vector2(-3f, 0f), new Vector2(2f, 0.2f)),
                (new Vector2(3f, 0f), new Vector2(2f, 0.2f)),
                (new Vector2(0f, 3f), new Vector2(2f, 0.2f)));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(0f, 1.5f);
                graph.Config.ScanSize = new Vector2(10f, 8f);
                graph.Config.GroundLayer = 1 << platform.gameObject.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 0.75f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();

                Assert.That(graph.SurfaceSegments, Has.Count.EqualTo(3));
                Assert.IsTrue(graph.TryGetSurfaceSegment(0, out var firstSegment));
                Assert.AreEqual(-4f, firstSegment.Left, 0.05f);
                Assert.AreEqual(-2f, firstSegment.Right, 0.05f);
                Assert.AreEqual(0.1f, firstSegment.Y, 0.05f);
                Assert.AreSame(platform, firstSegment.Collider);

                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(graph);
                var snapshot = pathfinder.GetSnapshot();
                Assert.AreEqual(3, snapshot.SurfaceSegmentCount);
                Assert.That(snapshot.SurfaceSegmentDebug, Does.Contain("x=[-4.00,-2.00]"));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_SurfaceNodeAboveLowerPlatform_DoesNotCreateFallFromInterior()
        {
            var host = new GameObject("InteriorFallLinkRegression");
            var upper = CreatePlatform("InteriorFallUpper", new Vector2(0f, 4f), new Vector2(6f, 0.2f));
            var lower = CreatePlatform("InteriorFallLower", new Vector2(0f, 0f), new Vector2(6f, 0.2f));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(0f, 2f);
                graph.Config.ScanSize = new Vector2(10f, 8f);
                graph.Config.GroundLayer = 1 << upper.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.GenerateJumpLinks();

                Assert.IsFalse(graph.Links.Any(link =>
                {
                    if (link.LinkType != PlatformLinkType.Fall)
                        return false;

                    var from = graph.GetNode(link.FromNodeId);
                    return from.HasValue && from.Value.NodeType == PlatformNodeType.Surface;
                }), "Fall links must start from real ledges, not surface/interior nodes.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(lower);
            }
        }

        [Test]
        public void TryRequestPath_SameColliderStepDownAcrossSharedEdge_ReachesLowerSurface()
        {
            var host = new GameObject("SameColliderStepDownRouteTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderStepDownPlatform",
                (new Vector2(15f, -0.1f), new Vector2(80f, 0.2f)),
                (new Vector2(59.5f, 3.9f), new Vector2(9f, 0.2f)),
                (new Vector2(94.5f, -0.1f), new Vector2(61f, 0.2f)));

            try
            {
                var pathfinder = CreatePathfinderForSingleCollider(
                    host,
                    platform,
                    scanCenter: new Vector2(50f, 1.5f),
                    scanSize: new Vector2(160f, 14f),
                    maxJumpVelocity: 18f,
                    maxJumpHeight: 8f,
                    maxHorizontalDistance: 8f,
                    maxFallHeight: 20f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(50.21f, 0.12f, 0f),
                        new Vector3(85f, 2f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true,
                        projectionDistance: 4f),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.AreEqual(PlatformPathCompletionKind.FullPath, result.CompletionKind, BuildPathDebug(result));
                Assert.IsTrue(
                    result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Fall),
                    BuildPathDebug(result));
                Assert.Greater(result.Path.EndPosition.x, 80f, BuildPathDebug(result));
                Assert.AreEqual(0f, result.Path.EndPosition.y, 0.5f, BuildPathDebug(result));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_PhysicalEdgeTransitionAnchor_CanStartFall()
        {
            var host = new GameObject("PhysicalEdgeTransitionFallTest");
            var platform = CreateMultiPathPolygonPlatform(
                "PhysicalEdgeTransitionFallPlatform",
                (new Vector2(2f, -0.1f), new Vector2(4f, 0.2f)),
                (new Vector2(7f, -4.1f), new Vector2(6f, 0.2f)));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(5f, -2f);
                graph.Config.ScanSize = new Vector2(14f, 10f);
                graph.Config.GroundLayer = 1 << platform.gameObject.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.4f;

                graph.GeneratePlatformGraph();
                int upperGroup = graph.FindSurfaceGroupAt(new Vector2(4f, 0f), platform, 1f);
                int lowerGroup = graph.FindSurfaceGroupAt(new Vector2(4f, -4f), platform, 1f);
                Assert.GreaterOrEqual(upperGroup, 0, "Expected upper surface group.");
                Assert.GreaterOrEqual(lowerGroup, 0, "Expected lower surface group.");

                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxFallHeight = 10f;
                jumpLinkCalculator.Config.MaxFallHorizontalDistance = 0.25f;
                jumpLinkCalculator.GenerateJumpLinks();

                Assert.IsTrue(graph.Links.Any(link =>
                {
                    if (link.LinkType != PlatformLinkType.Fall)
                        return false;

                    var from = graph.GetNode(link.FromNodeId);
                    var to = graph.GetNode(link.ToNodeId);
                    return from.HasValue &&
                           to.HasValue &&
                           from.Value.IsTransitionAnchor &&
                           from.Value.SurfaceGroupId == upperGroup &&
                           to.Value.SurfaceGroupId == lowerGroup;
                }), "Physical edge transition anchors should be valid Fall starts; inset edge nodes can be too far away for narrow step-downs.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_SameColliderEdgeFall_DoesNotSkipIntermediateLandingSurface()
        {
            var host = new GameObject("SameColliderLayeredFallTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderLayeredFallPlatform",
                (new Vector2(0f, 5.9f), new Vector2(4f, 0.2f)),
                (new Vector2(3.85f, 3.9f), new Vector2(4.3f, 0.2f)),
                (new Vector2(3.85f, 1.9f), new Vector2(4.3f, 0.2f)));

            try
            {
                var graph = host.AddComponent<PlatformGraphGenerator>();
                graph.Config.ScanCenter = new Vector2(2f, 4f);
                graph.Config.ScanSize = new Vector2(12f, 10f);
                graph.Config.GroundLayer = 1 << platform.gameObject.layer;
                graph.Config.OneWayPlatformLayer = 0;
                graph.Config.ObstacleLayer = 0;
                graph.Config.NodeSpacing = 1f;
                graph.Config.EdgeInset = 0.2f;

                graph.GeneratePlatformGraph();
                int upperGroup = graph.FindSurfaceGroupAt(new Vector2(2f, 6f), platform, 1f);
                int middleGroup = graph.FindSurfaceGroupAt(new Vector2(2.1f, 4f), platform, 1f);
                int lowerGroup = graph.FindSurfaceGroupAt(new Vector2(2.1f, 2f), platform, 1f);
                Assert.GreaterOrEqual(upperGroup, 0, "Expected upper surface group.");
                Assert.GreaterOrEqual(middleGroup, 0, "Expected middle surface group.");
                Assert.GreaterOrEqual(lowerGroup, 0, "Expected lower surface group.");

                var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
                jumpLinkCalculator.Config.MaxFallHeight = 10f;
                jumpLinkCalculator.Config.MaxFallHorizontalDistance = 2f;
                jumpLinkCalculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasFallLink(graph, upperGroup, middleGroup),
                    "The first landing surface below a same-collider edge should remain reachable.");
                Assert.IsFalse(
                    HasFallLink(graph, upperGroup, lowerGroup),
                    "Same-collider edge fall links must not tunnel past an intermediate landing surface.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void TryRequestPath_SameColliderTallStepDownAtEdge_ReachesLowerSurface()
        {
            var host = new GameObject("SameColliderTallStepDownRouteTest");
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderTallStepDownPlatform",
                (new Vector2(18.5f, -0.1f), new Vector2(35f, 0.2f)),
                (new Vector2(54f, -12.1f), new Vector2(36f, 0.2f)));

            try
            {
                var pathfinder = CreatePathfinderForSingleCollider(
                    host,
                    platform,
                    scanCenter: new Vector2(36f, -6f),
                    scanSize: new Vector2(80f, 30f),
                    maxJumpVelocity: 18f,
                    maxJumpHeight: 8f,
                    maxHorizontalDistance: 8f,
                    maxFallHeight: 40f);

                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(35.8f, 0.12f, 0f),
                        new Vector3(60f, -10.1f, 0f),
                        forceRequest: true,
                        projectTargetToGround: true,
                        projectionDistance: 4f),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                Assert.AreEqual(PlatformPathCompletionKind.FullPath, result.CompletionKind, BuildPathDebug(result));
                Assert.IsTrue(
                    result.Path.Commands.Any(command => command.CommandType == MoveCommandType.Fall),
                    BuildPathDebug(result));
                Assert.Greater(result.Path.EndPosition.x, 55f, BuildPathDebug(result));
                Assert.AreEqual(-12f, result.Path.EndPosition.y, 0.5f, BuildPathDebug(result));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void IsCurrentCommandComplete_WalkTargetAbovePlayer_DoesNotComplete()
        {
            var host = new GameObject("WalkCompletionHeightTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Walk(new Vector3(1f, 4f, 0f), 0.5f, 1)
                };
                var path = new Platform2DPath(
                    new Vector3(0.9f, 0f, 0f),
                    new Vector3(1f, 4f, 0f),
                    commands);
                SetCurrentPath(pathfinder, path);

                Assert.IsFalse(pathfinder.IsCurrentCommandComplete(new Vector3(1f, 0f, 0f), isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void IsCurrentCommandComplete_NonTerminalWalkTargetBelowPlayer_CanAdvance()
        {
            var host = new GameObject("WalkCompletionBelowPlayerTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Walk(new Vector3(2.55f, 3f, 0f), 0.5f, 1),
                    MoveCommand.Jump(new Vector3(3.5f, 5f, 0f), 8f, 1f, 0.3f, facingDirection: 1)
                };
                var path = new Platform2DPath(
                    new Vector3(2.63f, 4.32f, 0f),
                    new Vector3(2.55f, 3f, 0f),
                    commands);
                SetCurrentPath(pathfinder, path);

                Assert.IsTrue(pathfinder.IsCurrentCommandComplete(new Vector3(2.63f, 4.32f, 0f), isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void IsCurrentCommandComplete_TerminalWalkTargetFarBelowGroundedPlayer_DoesNotComplete()
        {
            var host = new GameObject("TerminalWalkFarBelowPlayerTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Walk(new Vector3(15.30f, 0f, 0f), 0.2f, -1)
                };
                var path = new Platform2DPath(
                    new Vector3(15.20f, 5.32f, 0f),
                    new Vector3(15.30f, 0f, 0f),
                    commands);
                SetCurrentPath(pathfinder, path);

                Assert.IsFalse(pathfinder.IsCurrentCommandComplete(new Vector3(15.20f, 5.32f, 0f), isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void IsCurrentCommandComplete_TerminalWalkSamePlane_Completes()
        {
            var host = new GameObject("TerminalWalkSamePlaneTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Walk(new Vector3(15.30f, 5.25f, 0f), 0.2f, -1)
                };
                var path = new Platform2DPath(
                    new Vector3(15.20f, 5.32f, 0f),
                    new Vector3(15.30f, 5.25f, 0f),
                    commands);
                SetCurrentPath(pathfinder, path);

                Assert.IsTrue(pathfinder.IsCurrentCommandComplete(new Vector3(15.20f, 5.32f, 0f), isGrounded: true));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ValidatePath_JumpCommandAlignedXWithVerticalGap_RemainsValid()
        {
            var host = new GameObject("JumpValidationVerticalGapTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Jump(new Vector3(3f, 6f, 0f), 10f, 0f, 0.5f)
                };
                SetCurrentPath(pathfinder, new Platform2DPath(Vector3.zero, new Vector3(3f, 6f, 0f), commands));

                var result = pathfinder.ValidatePath(new Vector3(3f, 1f, 0f), new Vector3(3f, 6f, 0f));

                Assert.AreEqual(PathValidationResult.Valid, result);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ValidatePath_FallCommandHorizontallyDeviated_ReturnsDeviated()
        {
            var host = new GameObject("FallValidationDeviationTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Fall(new Vector3(3f, 0f, 0f), 0.5f, 1)
                };
                SetCurrentPath(pathfinder, new Platform2DPath(new Vector3(3f, 6f, 0f), new Vector3(3f, 0f, 0f), commands));

                var result = pathfinder.ValidatePath(new Vector3(8f, 4f, 0f), new Vector3(3f, 0f, 0f));

                Assert.AreEqual(PathValidationResult.Deviated, result);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TryEvaluateRoute_ReturnsComparableCostWithoutMutatingCurrentPath()
        {
            var host = new GameObject("RouteCostQueryTest");
            var ground = CreatePlatform("RouteCostGround", new Vector2(0f, 0f), new Vector2(12f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, ground, ground, maxJumpVelocity: 16f);
                Assert.IsTrue(pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(-4f, 0.35f, 0f),
                        new Vector3(-2f, 0.35f, 0f),
                        forceRequest: true,
                        projectTargetToGround: false),
                    out var activePathResult),
                    BuildPathDebug(activePathResult));
                var originalSnapshot = pathfinder.GetSnapshot();

                bool success = pathfinder.TryEvaluateRoute(
                    new PlatformRouteQuery(
                        new Vector3(-4f, 0.35f, 0f),
                        new Vector3(4f, 0.35f, 0f),
                        projectTargetToGround: false),
                    out var result);

                Assert.IsTrue(success, result.DebugSummary);
                Assert.IsTrue(result.Cost.IsReachable);
                Assert.Greater(result.Cost.Total, 0f);
                Assert.AreEqual(1, result.Cost.CommandCount);
                Assert.That(result.DebugSummary, Does.Contain("0:Walk"));
                Assert.AreEqual(originalSnapshot.CurrentCommandIndex, pathfinder.GetSnapshot().CurrentCommandIndex);
                Assert.AreEqual(originalSnapshot.CommandDebug, pathfinder.GetSnapshot().CommandDebug);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void TryEvaluateRoute_FallOnlyStartsFromReachableEdge()
        {
            var host = new GameObject("FallEdgeOnlyRoute");
            var upper = CreatePlatform("FallUpper", new Vector2(0f, 5f), new Vector2(12f, 0.2f));
            var lower = CreatePlatform("FallLower", new Vector2(0f, 0f), new Vector2(12f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, upper, lower, maxJumpVelocity: 12f);

                bool success = pathfinder.TryEvaluateRoute(
                    new PlatformRouteQuery(
                        new Vector3(0f, 5.12f, 0f),
                        new Vector3(0f, 0.12f, 0f),
                        projectTargetToGround: true),
                    out var result);

                if (!success)
                {
                    Assert.AreEqual(PlatformPathFailureReason.InvalidCommand, result.FailureReason, result.DebugSummary);
                    return;
                }

                Vector3 previous = result.Path.StartPosition;
                foreach (var command in result.Path.Commands)
                {
                    if (command.CommandType == MoveCommandType.Fall)
                    {
                        float distanceToLeftEdge = Mathf.Abs(previous.x + 6f);
                        float distanceToRightEdge = Mathf.Abs(previous.x - 6f);
                        Assert.LessOrEqual(
                            Mathf.Min(distanceToLeftEdge, distanceToRightEdge),
                            0.4f,
                            $"Fall source must be an upper-platform edge, not the landing target. Source={previous:F2}, Command={command}, {result.DebugSummary}");
                    }

                    previous = command.Target;
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(lower);
            }
        }

        [Test]
        public void Snapshot_IncludesRouteCostAndSurfaceSegments()
        {
            var host = new GameObject("RouteSnapshotDetails");
            var ground = CreatePlatform("SnapshotGround", new Vector2(0f, 0f), new Vector2(12f, 0.2f));

            try
            {
                var pathfinder = CreatePathfinder(host, ground, ground, maxJumpVelocity: 16f);
                bool success = pathfinder.TryRequestPath(
                    new PlatformPathRequest(
                        new Vector3(-4f, 0.35f, 0f),
                        new Vector3(4f, 0.35f, 0f),
                        forceRequest: true,
                        projectTargetToGround: false),
                    out var result);

                Assert.IsTrue(success, BuildPathDebug(result));
                var snapshot = pathfinder.GetSnapshot();
                Assert.Greater(snapshot.RouteCost, 0f);
                Assert.GreaterOrEqual(snapshot.CurrentSurfaceSegmentId, 0);
                Assert.GreaterOrEqual(snapshot.TargetSurfaceSegmentId, 0);
                Assert.IsTrue(snapshot.CommandsValidated);
                Assert.That(snapshot.SurfaceSegmentDebug, Does.Contain("x=["));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(ground);
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

        private static PolygonCollider2D CreateMultiPathPolygonPlatform(
            string name,
            params (Vector2 center, Vector2 size)[] platforms)
        {
            var platform = new GameObject(name);
            var collider = platform.AddComponent<PolygonCollider2D>();
            collider.pathCount = platforms.Length;

            for (int i = 0; i < platforms.Length; i++)
            {
                var rect = platforms[i];
                float halfWidth = rect.size.x * 0.5f;
                float halfHeight = rect.size.y * 0.5f;
                collider.SetPath(i, new[]
                {
                    new Vector2(rect.center.x - halfWidth, rect.center.y - halfHeight),
                    new Vector2(rect.center.x - halfWidth, rect.center.y + halfHeight),
                    new Vector2(rect.center.x + halfWidth, rect.center.y + halfHeight),
                    new Vector2(rect.center.x + halfWidth, rect.center.y - halfHeight)
                });
            }

            Physics2D.SyncTransforms();
            return collider;
        }

        private static Platform2DPathfinder CreatePathfinderForSingleCollider(
            GameObject host,
            Collider2D platform,
            float maxJumpVelocity,
            bool oneWay = false)
        {
            return CreatePathfinderForSingleCollider(
                host,
                platform,
                scanCenter: new Vector2(0f, 1.5f),
                scanSize: new Vector2(16f, 8f),
                maxJumpVelocity: maxJumpVelocity,
                maxJumpHeight: 6f,
                maxHorizontalDistance: 6f,
                maxFallHeight: 10f,
                oneWay: oneWay);
        }

        private static Platform2DPathfinder CreatePathfinderForSingleCollider(
            GameObject host,
            Collider2D platform,
            Vector2 scanCenter,
            Vector2 scanSize,
            float maxJumpVelocity,
            float maxJumpHeight,
            float maxHorizontalDistance,
            float maxFallHeight,
            bool oneWay = false)
        {
            var graph = host.AddComponent<PlatformGraphGenerator>();
            graph.Config.ScanCenter = scanCenter;
            graph.Config.ScanSize = scanSize;
            graph.Config.GroundLayer = oneWay ? 0 : 1 << platform.gameObject.layer;
            graph.Config.OneWayPlatformLayer = oneWay ? 1 << platform.gameObject.layer : 0;
            graph.Config.ObstacleLayer = 0;
            graph.Config.NodeSpacing = 1f;
            graph.Config.EdgeInset = 0.2f;

            graph.GeneratePlatformGraph();
            var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
            jumpLinkCalculator.Config.MaxJumpVelocity = maxJumpVelocity;
            jumpLinkCalculator.Config.MaxJumpHeight = maxJumpHeight;
            jumpLinkCalculator.Config.MaxHorizontalDistance = maxHorizontalDistance;
            jumpLinkCalculator.Config.MaxFallHeight = maxFallHeight;
            jumpLinkCalculator.GenerateJumpLinks();

            var pathfinder = host.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            return pathfinder;
        }

        private static Platform2DPathfinder CreatePathfinder(
            GameObject host,
            GameObject lower,
            GameObject upper,
            float maxJumpVelocity)
        {
            var graph = host.AddComponent<PlatformGraphGenerator>();
            graph.Config.ScanCenter = new Vector2(0f, 1.5f);
            graph.Config.ScanSize = new Vector2(14f, 8f);
            graph.Config.GroundLayer = 1 << lower.layer;
            graph.Config.OneWayPlatformLayer = 0;
            graph.Config.ObstacleLayer = 0;
            graph.Config.NodeSpacing = 1f;
            graph.Config.EdgeInset = 0.2f;

            graph.GeneratePlatformGraph();
            var jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();
            jumpLinkCalculator.Config.MaxJumpVelocity = maxJumpVelocity;
            jumpLinkCalculator.GenerateJumpLinks();

            var pathfinder = host.AddComponent<Platform2DPathfinder>();
            pathfinder.SetGraphGenerator(graph);
            return pathfinder;
        }

        private static bool HasFallLink(PlatformGraphGenerator graph, int fromGroup, int toGroup)
        {
            return graph.Links.Any(link =>
            {
                if (link.LinkType != PlatformLinkType.Fall)
                    return false;

                var from = graph.GetNode(link.FromNodeId);
                var to = graph.GetNode(link.ToNodeId);
                return from.HasValue &&
                       to.HasValue &&
                       from.Value.SurfaceGroupId == fromGroup &&
                       to.Value.SurfaceGroupId == toGroup;
            });
        }

        private static string BuildPathDebug(PlatformPathResult result)
        {
            if (result.Path?.Commands == null)
                return $"{result.FailureReason}; commands=0";

            string commands = string.Join(" -> ", result.Path.Commands.Select((command, index) =>
                $"{index}:{command.CommandType}@{command.Target:F2}/face={command.FacingDirection}"));
            return $"{result.FailureReason}; kind={result.CompletionKind}; commands={result.Path.Commands.Count}; {commands}";
        }

        private static void AssertNoVerticalWalkCommands(PlatformPathResult result, float maxVerticalDelta = 0.5f)
        {
            Assert.IsNotNull(result.Path, BuildPathDebug(result));
            Assert.IsNotNull(result.Path.Commands, BuildPathDebug(result));

            for (int i = 0; i < result.Path.Commands.Count; i++)
            {
                var command = result.Path.Commands[i];
                if (command.CommandType != MoveCommandType.Walk)
                    continue;

                Vector3 previousTarget = i == 0
                    ? result.Path.StartPosition
                    : result.Path.Commands[i - 1].Target;
                float verticalDelta = Mathf.Abs(command.Target.y - previousTarget.y);
                Assert.LessOrEqual(
                    verticalDelta,
                    maxVerticalDelta,
                    $"Walk command {i} moves vertically from {previousTarget:F2} to {command.Target:F2}. {BuildPathDebug(result)}");
            }
        }

        private static void SetCurrentPath(Platform2DPathfinder pathfinder, Platform2DPath path)
        {
            var property = typeof(Platform2DPathfinder).GetProperty(nameof(Platform2DPathfinder.CurrentPath));
            property.SetValue(pathfinder, path);
        }
    }
}
