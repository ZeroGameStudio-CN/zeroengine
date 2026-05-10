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
        public void IsCurrentCommandComplete_WalkTargetBelowPlayer_CompletesWhenXArrived()
        {
            var host = new GameObject("WalkCompletionBelowPlayerTest");

            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                var commands = new System.Collections.Generic.List<MoveCommand>
                {
                    MoveCommand.Walk(new Vector3(2.55f, 3f, 0f), 0.5f, 1)
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
            var graph = host.AddComponent<PlatformGraphGenerator>();
            graph.Config.ScanCenter = new Vector2(0f, 1.5f);
            graph.Config.ScanSize = new Vector2(16f, 8f);
            graph.Config.GroundLayer = oneWay ? 0 : 1 << platform.gameObject.layer;
            graph.Config.OneWayPlatformLayer = oneWay ? 1 << platform.gameObject.layer : 0;
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
