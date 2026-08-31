using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class JumpLinkCalculatorTests
    {
        private const int GroundLayer = 8;
        private const int OneWayLayer = 9;
        private const int ObstacleLayer = 10;

        [Test]
        public void GenerateJumpLinks_GroundBetweenEndpoints_BlocksDirectJump()
        {
            var host = new GameObject("GroundBlocksJumpHost");
            var lower = CreatePlatform("JumpLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("JumpUpper", GroundLayer, new Vector2(5f, 0f), new Vector2(2f, 0.2f));
            var blocker = CreatePlatform("JumpSolidBlocker", GroundLayer, new Vector2(2.5f, 1.5f), new Vector2(0.6f, 3f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(2.5f, 0.5f);
                graph.Config.ScanSize = new Vector2(8f, 4f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        graph.GetNode(link.FromNodeId).TryGetCollider(out var fromCollider) &&
                        graph.GetNode(link.ToNodeId).TryGetCollider(out var toCollider) &&
                        fromCollider == lower.GetComponent<Collider2D>() &&
                        toCollider == upper.GetComponent<Collider2D>()),
                    "GroundLayer must block jump trajectories even when ObstacleLayer is empty.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(blocker);
            }
        }

        [Test]
        public void GenerateJumpLinks_CompositeEdgeRoundingWithinTolerance_KeepsReachableJump()
        {
            var host = new GameObject("CompositeEdgeRoundingJumpHost");
            var upper = CreatePlatform("RoundedUpper", GroundLayer, new Vector2(15.4995f, 11f), new Vector2(30.999f, 0.2f));
            var middle = CreatePlatform("RoundedMiddle", GroundLayer, new Vector2(51.0005f, 5f), new Vector2(27.999f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(32.5f, 6f);
                graph.Config.ScanSize = new Vector2(80f, 18f);
                graph.Config.NodeSpacing = 1.5f;
                graph.Config.EdgeInset = 0.3f;
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 6.75f;
                calculator.Config.AirJumpCount = 1;
                calculator.Config.AirJumpVelocity = 20f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasJumpLink(graph, middle.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "Sub-centimeter CompositeCollider edge rounding must not remove a boundary jump within the configured body-safe node distance limit.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(middle);
            }
        }

        [Test]
        public void GenerateJumpLinks_OneWayBetweenEndpoints_DoesNotBlockDirectJump()
        {
            var host = new GameObject("OneWayDoesNotBlockJumpHost");
            var lower = CreatePlatform("OneWayJumpLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("OneWayJumpUpper", GroundLayer, new Vector2(5f, 0f), new Vector2(2f, 0.2f));
            var oneWay = CreatePlatform("OneWayJumpMiddle", OneWayLayer, new Vector2(2.5f, 1.5f), new Vector2(0.6f, 3f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 1 << OneWayLayer);
                graph.Config.ScanCenter = new Vector2(2.5f, 0.5f);
                graph.Config.ScanSize = new Vector2(8f, 4f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        graph.GetNode(link.FromNodeId).TryGetCollider(out var fromCollider) &&
                        graph.GetNode(link.ToNodeId).TryGetCollider(out var toCollider) &&
                        fromCollider == lower.GetComponent<Collider2D>() &&
                        toCollider == upper.GetComponent<Collider2D>()),
                    "One-way platforms should not be treated as ordinary jump trajectory blockers.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(oneWay);
            }
        }

        [Test]
        public void GenerateJumpLinks_MaxHorizontalDistance_RejectsNodeGapBeyondConfiguredLimit()
        {
            var host = new GameObject("MaxHorizontalDistanceRejectHost");
            var lower = CreatePlatform("MaxHorizontalDistanceRejectLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("MaxHorizontalDistanceRejectUpper", GroundLayer, new Vector2(9.9f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(5f, 1.5f);
                graph.Config.ScanSize = new Vector2(16f, 8f);
                graph.Config.EdgeInset = 0.3f;
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                PlatformNodeData fromNode = graph.Nodes.Single(node =>
                    node.PlatformCollider == lowerCollider &&
                    node.NodeType == PlatformNodeType.RightEdge &&
                    !node.IsTransitionAnchor);
                PlatformNodeData toNode = graph.Nodes
                    .Where(node => node.PlatformCollider == upperCollider && node.NodeType == PlatformNodeType.Surface)
                    .OrderBy(node => node.Position.x)
                    .First();
                float horizontalNodeDistance = Mathf.Abs(toNode.Position.x - fromNode.Position.x);

                Assert.That(horizontalNodeDistance, Is.EqualTo(6.65f).Within(0.02f));

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 6f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        link.FromNodeId == fromNode.NodeId &&
                        link.ToNodeId == toNode.NodeId),
                    "A jump whose actual graph-node distance is about 6.65 must not cross a 6.0 maximum plus 0.05 tolerance.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_MaxHorizontalDistance_KeepsNodeGapWithinConfiguredTolerance()
        {
            var host = new GameObject("MaxHorizontalDistanceKeepHost");
            var lower = CreatePlatform("MaxHorizontalDistanceKeepLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("MaxHorizontalDistanceKeepUpper", GroundLayer, new Vector2(9.25f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(4.5f, 1.5f);
                graph.Config.ScanSize = new Vector2(15f, 8f);
                graph.Config.EdgeInset = 0.3f;
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                PlatformNodeData fromNode = graph.Nodes.Single(node =>
                    node.PlatformCollider == lowerCollider &&
                    node.NodeType == PlatformNodeType.RightEdge &&
                    !node.IsTransitionAnchor);
                PlatformNodeData toNode = graph.Nodes
                    .Where(node => node.PlatformCollider == upperCollider && node.NodeType == PlatformNodeType.Surface)
                    .OrderBy(node => node.Position.x)
                    .First();
                float horizontalNodeDistance = Mathf.Abs(toNode.Position.x - fromNode.Position.x);

                Assert.That(horizontalNodeDistance, Is.EqualTo(6f).Within(0.02f));
                Assert.That(horizontalNodeDistance, Is.LessThanOrEqualTo(6.05f));

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 6f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        link.FromNodeId == fromNode.NodeId &&
                        link.ToNodeId == toNode.NodeId),
                    "A jump whose actual graph-node distance is 6.0 must remain available under the 6.0 maximum plus 0.05 tolerance.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_HigherPlatformOnRight_UsesRightSourceEdgeOnly()
        {
            var host = new GameObject("RightwardJumpDirectionHost");
            var lower = CreatePlatform("RightwardJumpLower", GroundLayer, new Vector2(-3f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("RightwardJumpUpper", GroundLayer, new Vector2(2f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(-0.5f, 1.5f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 5f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.1f;
                calculator.GenerateJumpLinks();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                Assert.IsTrue(
                    HasJumpLinkFromEdgeToPlatform(graph, lowerCollider, PlatformNodeType.RightEdge, upperCollider),
                    "A higher platform on the right must retain the outward jump from the source right edge.");
                Assert.IsFalse(
                    HasJumpLinkFromEdgeToPlatform(graph, lowerCollider, PlatformNodeType.LeftEdge, upperCollider),
                    "The source left edge must not offer an inward jump across its own platform to a right-side target.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_UsesBodySafeLandingsAndSkipsPhysicalTransitionAnchors()
        {
            var host = new GameObject("BodySafeJumpEdgesHost");
            var lower = CreatePlatform("BodySafeJumpLower", GroundLayer, new Vector2(-3f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("BodySafeJumpUpper", GroundLayer, new Vector2(2f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(-0.5f, 1.5f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.Config.EdgeInset = 0.3f;
                graph.Config.CharacterRadius = 0.45f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 5f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.1f;
                calculator.GenerateJumpLinks();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                var links = graph.Links.Where(link =>
                    link.LinkType == PlatformLinkType.Jump &&
                    graph.GetNode(link.FromNodeId) is { } fromNode &&
                    graph.GetNode(link.ToNodeId) is { } toNode &&
                    fromNode.PlatformCollider == lowerCollider &&
                    toNode.PlatformCollider == upperCollider).ToArray();

                Assert.That(links, Is.Not.Empty, "The reachable rightward jump must remain available.");
                float safeInset = graph.Config.CharacterRadius + 0.05f;
                foreach (var link in links)
                {
                    var fromNode = graph.GetNode(link.FromNodeId).Value;
                    var toNode = graph.GetNode(link.ToNodeId).Value;
                    Assert.That(fromNode.IsTransitionAnchor, Is.False, "Jump must not start at the physical boundary anchor.");
                    Assert.That(toNode.IsTransitionAnchor, Is.False, "Jump must not land at the physical boundary anchor.");
                    Assert.That(toNode.Position.x, Is.GreaterThanOrEqualTo(upperCollider.bounds.min.x + safeInset - 0.001f));
                    Assert.That(toNode.Position.x, Is.LessThanOrEqualTo(upperCollider.bounds.max.x - safeInset + 0.001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_NarrowButStandablePlatform_KeepsSafeSurfaceLanding()
        {
            var host = new GameObject("NarrowSafeLandingHost");
            var lower = CreatePlatform("NarrowSafeLandingLower", GroundLayer, new Vector2(-1.5f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("NarrowSafeLandingUpper", GroundLayer, new Vector2(0.5f, 2.5f), new Vector2(0.9f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(-0.5f, 1.25f);
                graph.Config.ScanSize = new Vector2(6f, 6f);
                graph.Config.MinPlatformWidth = 1f;
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 4f;
                calculator.Config.MaxHorizontalDistance = 4f;
                calculator.Config.TrajectoryCheckRadius = 0.1f;
                calculator.GenerateJumpLinks();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                Assert.That(graph.Links.Any(link =>
                    link.LinkType == PlatformLinkType.Jump &&
                    graph.GetNode(link.FromNodeId) is { } fromNode &&
                    graph.GetNode(link.ToNodeId) is { } toNode &&
                    fromNode.PlatformCollider == lowerCollider &&
                    toNode.PlatformCollider == upperCollider &&
                    toNode.NodeType == PlatformNodeType.Surface &&
                    Mathf.Abs(toNode.Position.x - upperCollider.bounds.center.x) <= 0.01f), Is.True,
                    "A platform that exactly fits the actor must retain one body-safe center landing.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_MultipleSafeSurfaceCandidates_KeepsBothApproachLandings()
        {
            var host = new GameObject("MultipleSafeLandingCandidatesHost");
            var leftSource = CreatePlatform("MultipleSafeLandingLeftSource", GroundLayer, new Vector2(-5f, 0f), new Vector2(4f, 0.2f));
            var rightSource = CreatePlatform("MultipleSafeLandingRightSource", GroundLayer, new Vector2(5f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("MultipleSafeLandingUpper", GroundLayer, new Vector2(0f, 3f), new Vector2(6f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0f, 1.5f);
                graph.Config.ScanSize = new Vector2(18f, 8f);
                graph.Config.EdgeInset = 0.3f;
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 5f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.1f;
                calculator.GenerateJumpLinks();

                Collider2D leftSourceCollider = leftSource.GetComponent<Collider2D>();
                Collider2D rightSourceCollider = rightSource.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                float safeInset = graph.Config.CharacterRadius + 0.05f;
                float leftSafeX = upperCollider.bounds.min.x + safeInset;
                float rightSafeX = upperCollider.bounds.max.x - safeInset;

                Assert.IsTrue(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        graph.GetNode(link.FromNodeId) is { } fromNode &&
                        graph.GetNode(link.ToNodeId) is { } toNode &&
                        fromNode.PlatformCollider == leftSourceCollider &&
                        toNode.PlatformCollider == upperCollider &&
                        toNode.NodeType == PlatformNodeType.Surface &&
                        Mathf.Abs(toNode.Position.x - leftSafeX) <= 0.05f),
                    "A left-side source must retain the upper platform's left safe landing candidate.");
                Assert.IsTrue(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.Jump &&
                        graph.GetNode(link.FromNodeId) is { } fromNode &&
                        graph.GetNode(link.ToNodeId) is { } toNode &&
                        fromNode.PlatformCollider == rightSourceCollider &&
                        toNode.PlatformCollider == upperCollider &&
                        toNode.NodeType == PlatformNodeType.Surface &&
                        Mathf.Abs(toNode.Position.x - rightSafeX) <= 0.05f),
                    "A right-side source must retain the upper platform's right safe landing candidate.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(leftSource);
                Object.DestroyImmediate(rightSource);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_HigherPlatformOnLeft_UsesLeftSourceEdgeOnly()
        {
            var host = new GameObject("LeftwardJumpDirectionHost");
            var lower = CreatePlatform("LeftwardJumpLower", GroundLayer, new Vector2(3f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("LeftwardJumpUpper", GroundLayer, new Vector2(-2f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0.5f, 1.5f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxJumpVelocity = 20f;
                calculator.Config.MaxJumpHeight = 5f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.1f;
                calculator.GenerateJumpLinks();

                Collider2D lowerCollider = lower.GetComponent<Collider2D>();
                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                Assert.IsTrue(
                    HasJumpLinkFromEdgeToPlatform(graph, lowerCollider, PlatformNodeType.LeftEdge, upperCollider),
                    "A higher platform on the left must retain the outward jump from the source left edge.");
                Assert.IsFalse(
                    HasJumpLinkFromEdgeToPlatform(graph, lowerCollider, PlatformNodeType.RightEdge, upperCollider),
                    "The source right edge must not offer an inward jump across its own platform to a left-side target.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_LegacySingleJumpProfile_DoesNotUseAirJumpCapacity()
        {
            var host = new GameObject("LegacySingleJumpProfileHost");
            var lower = CreatePlatform("LegacyProfileLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("LegacyProfileUpper", GroundLayer, new Vector2(3f, 5f), new Vector2(2f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                GenerateTwoPlatformGraph(graph);

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 1f;
                calculator.Config.MaxJumpVelocity = 8f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "Legacy profile should remain constrained by its single jump velocity.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_PlayerAirJumpProfile_UsesTotalVerticalCapability()
        {
            var host = new GameObject("PlayerAirJumpProfileHost");
            var lower = CreatePlatform("PlayerProfileLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("PlayerProfileUpper", GroundLayer, new Vector2(3f, 5f), new Vector2(2f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                GenerateTwoPlatformGraph(graph);

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 1f;
                calculator.Config.MaxJumpVelocity = 8f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.AirJumpCount = 1;
                calculator.Config.AirJumpVelocity = 8f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "Player profile should use configured air jumps when estimating graph reachability.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_SmartJumpProfile_UsesTargetHeightInsteadOfAirJumpCount()
        {
            var host = new GameObject("SmartJumpProfileHost");
            var lower = CreatePlatform("SmartProfileLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("SmartProfileUpper", GroundLayer, new Vector2(3f, 5f), new Vector2(2f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                GenerateTwoPlatformGraph(graph);

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 1f;
                calculator.Config.MaxJumpVelocity = 8f;
                calculator.Config.MaxJumpHeight = 6f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.UseSingleSmartJump = true;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "Smart jump profile should allow a single height-based jump within MaxJumpHeight.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_SmartJumpProfile_DoesNotInheritAirJumpCapacity()
        {
            var host = new GameObject("SmartJumpNoAirJumpProfileHost");
            var lower = CreatePlatform("SmartNoAirLower", GroundLayer, new Vector2(0f, 0f), new Vector2(2f, 0.2f));
            var upper = CreatePlatform("SmartNoAirUpper", GroundLayer, new Vector2(3f, 5f), new Vector2(2f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                GenerateTwoPlatformGraph(graph);

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 1f;
                calculator.Config.MaxJumpVelocity = 8f;
                calculator.Config.MaxJumpHeight = 3f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.UseSingleSmartJump = true;
                calculator.Config.AirJumpCount = 3;
                calculator.Config.AirJumpVelocity = 20f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "Smart/entity profile must ignore AirJumpCount and stay bounded by MaxJumpHeight.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_DropThrough_DoesNotPassIntermediateGround()
        {
            var host = new GameObject("DropThroughGroundBlockerHost");
            var oneWay = CreatePlatform("DropOneWay", OneWayLayer, new Vector2(0f, 5f), new Vector2(4f, 0.2f));
            var middle = CreatePlatform("DropMiddleGround", GroundLayer, new Vector2(0f, 2.5f), new Vector2(4f, 0.2f));
            var lower = CreatePlatform("DropLowerGround", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 1 << OneWayLayer);
                graph.Config.ScanCenter = new Vector2(0f, 2.5f);
                graph.Config.ScanSize = new Vector2(8f, 8f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.2f;
                calculator.GenerateJumpLinks();

                var oneWayCollider = oneWay.GetComponent<Collider2D>();
                var lowerCollider = lower.GetComponent<Collider2D>();
                var middleCollider = middle.GetComponent<Collider2D>();

                Assert.IsTrue(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.DropThrough &&
                        graph.GetNode(link.FromNodeId).TryGetCollider(out var fromCollider) &&
                        graph.GetNode(link.ToNodeId).TryGetCollider(out var toCollider) &&
                        fromCollider == oneWayCollider &&
                        toCollider == middleCollider),
                    "The first solid platform below a one-way platform should remain reachable.");

                Assert.IsFalse(
                    graph.Links.Any(link =>
                        link.LinkType == PlatformLinkType.DropThrough &&
                        graph.GetNode(link.FromNodeId).TryGetCollider(out var fromCollider) &&
                        graph.GetNode(link.ToNodeId).TryGetCollider(out var toCollider) &&
                        fromCollider == oneWayCollider &&
                        toCollider == lowerCollider),
                    "DropThrough must stop at an intermediate solid platform instead of tunneling to lower floors.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(oneWay);
                Object.DestroyImmediate(middle);
                Object.DestroyImmediate(lower);
            }
        }

        [Test]
        public void GenerateJumpLinks_SeparateColliderPhysicalEdge_CreatesFallToLowerSurface()
        {
            var host = new GameObject("SeparateColliderEdgeFallHost");
            var lower = CreatePlatform("EdgeFallLower", GroundLayer, new Vector2(0f, 2.5f), new Vector2(42f, 5f));
            var upper = CreatePlatform("EdgeFallUpper", GroundLayer, new Vector2(-6f, 7.5f), new Vector2(30f, 5f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0f, 5f);
                graph.Config.ScanSize = new Vector2(48f, 12f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasFallLinkNear(
                        graph,
                        upper.GetComponent<Collider2D>(),
                        lower.GetComponent<Collider2D>(),
                        new Vector2(9f, 10f),
                        new Vector2(9.1f, 5f)),
                    "A physical ledge should be able to fall to the first lower surface even when the two surfaces use separate colliders.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_RejectsSameColliderStructuralLip()
        {
            var host = new GameObject("SameColliderStructuralLipStepUpFallbackHost");
            var platform = CreateMultiPathPolygon(
                "SameColliderStepPlatforms",
                GroundLayer,
                CreateRectPath(new Vector2(11f, -0.1f), new Vector2(26f, 0.2f)),
                new[]
                {
                    new Vector2(24f, -7f),
                    new Vector2(24f, 8.8f),
                    new Vector2(24.05f, 8.8f),
                    new Vector2(24.05f, 7f),
                    new Vector2(77f, 7f),
                    new Vector2(77f, -7f)
                });
            var wall = CreatePlatform("SameColliderStepWall", ObstacleLayer, new Vector2(24f, 3.5f), new Vector2(0.2f, 7f));

            try
            {
                var graph = GenerateRoom016StyleStepUpGraph(host);
                float minSafeLandingX = 24f + graph.Config.CharacterRadius + 0.05f;

                Assert.IsFalse(
                    HasJumpLinkNear(
                        graph,
                        platform,
                        platform,
                        new Vector2(24f, 0f),
                        new Vector2(24f, 7f)),
                    "Same-collider edge step-up must not target the unsafe ledge edge where the character center cannot fit.");

                Assert.IsFalse(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(24f, 0f),
                        minLandingX: minSafeLandingX,
                        maxLandingX: minSafeLandingX + 1.0f,
                        landingY: 7f),
                    "Same-collider edge step-up must reject a trajectory whose radius clips the solid lip/underside before the landing point.");

                Assert.IsFalse(
                    HasJumpLinkToSurfacePastX(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(24f, 0f),
                        minX: minSafeLandingX + 1.5f,
                        landingY: 7f),
                    "Same-collider edge step-up must not also create distant interior surface jump links.");

            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_AllowsClearSameColliderSeparatedLanding()
        {
            var host = new GameObject("ClearSameColliderEdgeStepUpFallbackHost");
            var platform = CreateMultiPathPlatform(
                "ClearSameColliderStepPlatforms",
                GroundLayer,
                (new Vector2(0f, 0f), new Vector2(4f, 0.2f)),
                (new Vector2(3f, 3f), new Vector2(2f, 0.2f)));

            try
            {
                var graph = GenerateEdgeStepUpGraph(host);
                float lowerRightEdgeX = 2f;
                float landingY = 3.1f;
                float minSafeLandingX = lowerRightEdgeX + graph.Config.CharacterRadius + 0.05f;

                Assert.IsTrue(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(lowerRightEdgeX, 0.1f),
                        minLandingX: minSafeLandingX,
                        maxLandingX: minSafeLandingX + 0.8f,
                        landingY: landingY),
                    "A same-collider step-up with separated thin platforms and no structural lip should keep the safe landing link.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_AllowsShortAdjacentStairLandings()
        {
            var host = new GameObject("ShortAdjacentStairStepUpFallbackHost");
            var platform = CreateMultiPathPlatform(
                "ShortAdjacentStairStep",
                GroundLayer,
                (new Vector2(11f, -0.1f), new Vector2(22f, 0.2f)),
                (new Vector2(27f, 1.9f), new Vector2(10f, 0.2f)),
                (new Vector2(35f, 4.9f), new Vector2(6f, 0.2f)),
                (new Vector2(53f, 9.9f), new Vector2(30f, 0.2f)));

            try
            {
                var graph = GenerateRoom015StyleStairGraph(host);

                Assert.IsTrue(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(22f, 0f),
                        minLandingX: 22.45f,
                        maxLandingX: 22.75f,
                        landingY: 2f),
                    "Adjacent short stairs should keep the safe y0 -> y2 landing link.");

                Assert.IsTrue(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(32f, 2f),
                        minLandingX: 32.45f,
                        maxLandingX: 32.75f,
                        landingY: 5f),
                    "Adjacent short stairs should keep the safe y2 -> y5 landing link.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_AllowsSolidShortStairInteriorLanding()
        {
            var host = new GameObject("SolidShortStairStepUpFallbackHost");
            var platform = CreateMultiPathPolygon(
                "SolidShortStairStep",
                GroundLayer,
                new[]
                {
                    new Vector2(0f, -0.2f),
                    new Vector2(0f, 0f),
                    new Vector2(22f, 0f),
                    new Vector2(22f, 2f),
                    new Vector2(32f, 2f),
                    new Vector2(32f, 5f),
                    new Vector2(38f, 5f),
                    new Vector2(38f, 10f),
                    new Vector2(68f, 10f),
                    new Vector2(68f, 5f),
                    new Vector2(74f, 5f),
                    new Vector2(74f, 0f),
                    new Vector2(103f, 0f),
                    new Vector2(103f, -0.2f),
                });

            try
            {
                var graph = GenerateRoom015StyleStairGraph(host);

                Assert.IsTrue(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(22f, 0f),
                        minLandingX: 23.4f,
                        maxLandingX: 24.0f,
                        landingY: 2f),
                    "A solid short stair should keep a y0 -> y2 jump link to the first interior surface node.\n" +
                    BuildLocalGraphDebug(graph, minX: 20f, maxX: 26f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_UsesSafeLowerPitLandingCenter()
        {
            var host = new GameObject("SameColliderLowerPitStepUpFallbackHost");
            var platform = CreateMultiPathPlatform(
                "SameColliderLowerPitStep",
                GroundLayer,
                (new Vector2(11f, -0.1f), new Vector2(26f, 0.2f)),
                (new Vector2(42.5f, -7.1f), new Vector2(37f, 0.2f)));
            var wall = CreatePlatform("SameColliderLowerPitWall", ObstacleLayer, new Vector2(24f, -3.5f), new Vector2(0.2f, 7f));

            try
            {
                var graph = GenerateRoom016StyleStepUpGraph(host);

                Assert.IsFalse(
                    HasJumpLinkNear(
                        graph,
                        platform,
                        platform,
                        new Vector2(24f, -7f),
                        new Vector2(24f, 0f)),
                    "Same-collider edge step-up must reject unsafe upper edge targets even when the raw trajectory is clear.");

                Assert.IsTrue(
                    HasJumpLinkToSafeLandingNearLedge(
                        graph,
                        platform,
                        platform,
                        expectedFrom: new Vector2(24f, -7f),
                        minLandingX: 23.2f,
                        maxLandingX: 23.6f,
                        landingY: 0f),
                    "Same-collider edge step-up from the lower pit should use the nearest safe surface center on the upper ledge.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_RejectsSameColliderOverheadSurface()
        {
            var host = new GameObject("SameColliderOverheadStepUpFallbackHost");
            var platform = CreateMultiPathPlatform(
                "SameColliderStepWithOverhead",
                GroundLayer,
                (new Vector2(11f, -0.1f), new Vector2(26f, 0.2f)),
                (new Vector2(50.5f, 0f), new Vector2(53f, 14f)),
                (new Vector2(24.2f, 7.9f), new Vector2(1.4f, 0.2f)));
            var wall = CreatePlatform("SameColliderOverheadStepWall", ObstacleLayer, new Vector2(24f, 3.5f), new Vector2(0.2f, 7f));

            try
            {
                var graph = GenerateRoom016StyleStepUpGraph(host);

                Assert.IsFalse(
                    HasJumpLinkNear(
                        graph,
                        platform,
                        platform,
                        new Vector2(24f, 0f),
                        new Vector2(24f, 7f)),
                    "A same-collider surface inside the landing head-clearance box must still block the edge step-up fallback.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(platform.gameObject);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_AllowsCleanLandingClearance()
        {
            var host = new GameObject("CleanEdgeStepUpFallbackHost");
            var lower = CreatePlatform("CleanStepLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("CleanStepUpper", GroundLayer, new Vector2(3f, 3f), new Vector2(2f, 0.2f));
            var wall = CreatePlatform("CleanStepWall", ObstacleLayer, new Vector2(2.05f, 1.55f), new Vector2(0.2f, 2.9f));

            try
            {
                var graph = GenerateEdgeStepUpGraph(host);

                Assert.IsTrue(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "A clean edge-to-edge step-up with enough head clearance should keep the fallback jump link.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void GenerateJumpLinks_SingleImpulseProfile_RejectsTargetSideCollisionBeforeFootClearance()
        {
            var host = new GameObject("SingleImpulseBlockedLandingHost");
            var lower = CreatePlatform("SingleImpulseBlockedLower", GroundLayer, new Vector2(-3f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("SingleImpulseBlockedUpper", GroundLayer, new Vector2(1.5f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(-0.5f, 1.5f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpHeight = 4f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.Config.UseSingleSmartJump = true;
                calculator.Config.SupportsMidairHorizontalSteering = false;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "A single-impulse actor must not receive a link that collides with the target side before its feet clear the surface.\n" +
                    BuildLocalGraphDebug(graph, minX: -6f, maxX: 5f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_SingleImpulseProfile_KeepsArcThatClearsTargetSideBeforeContact()
        {
            var host = new GameObject("SingleImpulseClearLandingHost");
            var lower = CreatePlatform("SingleImpulseClearLower", GroundLayer, new Vector2(-3f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("SingleImpulseClearUpper", GroundLayer, new Vector2(3.5f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0.25f, 1.5f);
                graph.Config.ScanSize = new Vector2(14f, 8f);
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpHeight = 4f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.Config.UseSingleSmartJump = true;
                calculator.Config.SupportsMidairHorizontalSteering = false;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "A single-impulse actor must retain a ballistic arc whose feet clear the target side before horizontal contact.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_SingleImpulseProfile_VerticalOneWayTarget_KeepsJump()
        {
            var host = new GameObject("SingleImpulseVerticalOneWayHost");
            var lower = CreatePlatform("SingleImpulseVerticalLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("SingleImpulseVerticalOneWayUpper", OneWayLayer, new Vector2(0f, 3f), new Vector2(4f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 1 << OneWayLayer);
                graph.Config.ScanCenter = new Vector2(0f, 1.5f);
                graph.Config.ScanSize = new Vector2(8f, 8f);
                graph.Config.CharacterRadius = 0.4f;
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.GravityScale = 5f;
                calculator.Config.MaxJumpHeight = 4f;
                calculator.Config.MaxHorizontalDistance = 8f;
                calculator.Config.TrajectoryCheckRadius = 0.4f;
                calculator.Config.UseSingleSmartJump = true;
                calculator.Config.SupportsMidairHorizontalSteering = false;
                calculator.GenerateJumpLinks();

                Collider2D upperCollider = upper.GetComponent<Collider2D>();
                Assert.IsTrue(
                    graph.SurfaceSegments.Any(segment => segment.Collider == upperCollider && segment.IsOneWay),
                    "The vertical target must be represented as a one-way surface segment.");
                Assert.IsTrue(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upperCollider),
                    "A no-steering actor must retain a vertical jump through a one-way target platform.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_RejectsSkippedIntermediateSurface()
        {
            var host = new GameObject("IntermediateEdgeStepUpFallbackHost");
            var lower = CreatePlatform("IntermediateStepLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var middle = CreatePlatform("IntermediateStepMiddle", GroundLayer, new Vector2(2.6f, 1.5f), new Vector2(1.2f, 0.2f));
            var upper = CreatePlatform("IntermediateStepUpper", GroundLayer, new Vector2(3f, 3f), new Vector2(2f, 0.2f));
            var wall = CreatePlatform("IntermediateStepWall", ObstacleLayer, new Vector2(2.05f, 1.55f), new Vector2(0.2f, 2.9f));

            try
            {
                var graph = GenerateEdgeStepUpGraph(host);

                Assert.IsFalse(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "The fallback must not skip over the first surface directly above the edge.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(middle);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void GenerateJumpLinks_EdgeStepUpFallback_RejectsBlockedLandingHeadClearance()
        {
            var host = new GameObject("CeilingEdgeStepUpFallbackHost");
            var lower = CreatePlatform("CeilingStepLower", GroundLayer, new Vector2(0f, 0f), new Vector2(4f, 0.2f));
            var upper = CreatePlatform("CeilingStepUpper", GroundLayer, new Vector2(3f, 3f), new Vector2(2f, 0.2f));
            var wall = CreatePlatform("CeilingStepWall", ObstacleLayer, new Vector2(2.05f, 1.55f), new Vector2(0.2f, 2.9f));
            var ceiling = CreatePlatform("CeilingStepBlocker", ObstacleLayer, new Vector2(2.1f, 4f), new Vector2(1.2f, 0.2f));

            try
            {
                var graph = GenerateEdgeStepUpGraph(host);

                Assert.IsFalse(
                    HasJumpLink(graph, lower.GetComponent<Collider2D>(), upper.GetComponent<Collider2D>()),
                    "A low ceiling above the landing surface should block the edge step-up fallback.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(ceiling);
            }
        }

        [Test]
        public void GenerateJumpLinks_RightEdgeAdjacentHigherSolidWall_DoesNotCreateFallChain()
        {
            var host = new GameObject("Room008RightEdgeWallFallHost");
            var lower = CreatePlatform("Room008RightLower", GroundLayer, new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var source = CreatePlatform("Room008RightSource", GroundLayer, new Vector2(0f, 4f), new Vector2(6f, 0.2f));
            var higherWall = CreatePlatform("Room008RightHigherWall", GroundLayer, new Vector2(4f, 5.6f), new Vector2(2f, 3f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0f, 3.5f);
                graph.Config.ScanSize = new Vector2(12f, 10f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 8f;
                calculator.Config.MaxFallHorizontalDistance = 4f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    HasFallLinkFromEdgeToPlatform(
                        graph,
                        source.GetComponent<Collider2D>(),
                        PlatformNodeType.RightEdge,
                        lower.GetComponent<Collider2D>()),
                    "A solid wall flush with the source right edge must not create a Fall chain that walks into the higher wall.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(higherWall);
            }
        }

        [Test]
        public void GenerateJumpLinks_LeftEdgeAdjacentHigherSolidWall_DoesNotCreateFallChain()
        {
            var host = new GameObject("Room008LeftEdgeWallFallHost");
            var lower = CreatePlatform("Room008LeftLower", GroundLayer, new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var source = CreatePlatform("Room008LeftSource", GroundLayer, new Vector2(0f, 4f), new Vector2(6f, 0.2f));
            var higherWall = CreatePlatform("Room008LeftHigherWall", GroundLayer, new Vector2(-4f, 5.6f), new Vector2(2f, 3f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0f, 3.5f);
                graph.Config.ScanSize = new Vector2(12f, 10f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 8f;
                calculator.Config.MaxFallHorizontalDistance = 4f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsFalse(
                    HasFallLinkFromEdgeToPlatform(
                        graph,
                        source.GetComponent<Collider2D>(),
                        PlatformNodeType.LeftEdge,
                        lower.GetComponent<Collider2D>()),
                    "A solid wall flush with the source left edge must not create a mirrored Fall chain that walks into the higher wall.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(higherWall);
            }
        }

        [Test]
        public void GenerateJumpLinks_ExposedRightEdge_CreatesFallChain()
        {
            var host = new GameObject("Room008ExposedRightEdgeFallHost");
            var lower = CreatePlatform("Room008ExposedLower", GroundLayer, new Vector2(0f, 0f), new Vector2(10f, 0.2f));
            var source = CreatePlatform("Room008ExposedSource", GroundLayer, new Vector2(0f, 4f), new Vector2(6f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(0f, 2f);
                graph.Config.ScanSize = new Vector2(12f, 8f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 8f;
                calculator.Config.MaxFallHorizontalDistance = 4f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(
                    HasFallLinkFromEdgeToPlatform(
                        graph,
                        source.GetComponent<Collider2D>(),
                        PlatformNodeType.RightEdge,
                        lower.GetComponent<Collider2D>()),
                    "An exposed source edge must retain its valid Fall chain to the lower platform.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void GenerateJumpLinks_HighExposedEdgeAboveWideLowerPlatform_CreatesFallTransitionAnchor()
        {
            var host = new GameObject("HighWideLowerPlatformFallHost");
            var lower = CreatePlatform("HighWideLowerPlatform", GroundLayer, new Vector2(33f, 0f), new Vector2(64f, 0.2f));
            var source = CreatePlatform("HighNarrowUpperPlatform", GroundLayer, new Vector2(33f, 13f), new Vector2(16f, 0.2f));

            try
            {
                var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
                graph.Config.ScanCenter = new Vector2(33f, 6.5f);
                graph.Config.ScanSize = new Vector2(70f, 30f);
                graph.GeneratePlatformGraph();

                var calculator = host.AddComponent<JumpLinkCalculator>();
                calculator.Config.MaxFallHeight = 20f;
                calculator.Config.MaxFallHorizontalDistance = 4f;
                calculator.Config.TrajectoryCheckRadius = 0.25f;
                calculator.GenerateJumpLinks();

                Assert.IsTrue(graph.Links.Any(link =>
                {
                    if (link.LinkType != PlatformLinkType.Fall)
                        return false;

                    var fromNode = graph.GetNode(link.FromNodeId);
                    var toNode = graph.GetNode(link.ToNodeId);
                    return fromNode.HasValue &&
                           toNode.HasValue &&
                           fromNode.Value.PlatformCollider == source.GetComponent<Collider2D>() &&
                           fromNode.Value.NodeType == PlatformNodeType.RightEdge &&
                           toNode.Value.PlatformCollider == lower.GetComponent<Collider2D>() &&
                           toNode.Value.NodeType == PlatformNodeType.RightEdge &&
                           toNode.Value.IsTransitionAnchor;
                }), "A high exposed edge must retain a Fall link to the matching lower transition anchor.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(source);
            }
        }

        private static PlatformGraphGenerator CreateGraph(GameObject host, LayerMask groundMask, LayerMask oneWayMask)
        {
            var graph = host.AddComponent<PlatformGraphGenerator>();
            graph.Config.GroundLayer = groundMask;
            graph.Config.OneWayPlatformLayer = oneWayMask;
            graph.Config.ObstacleLayer = 0;
            graph.Config.NodeSpacing = 1f;
            graph.Config.EdgeInset = 0.2f;
            return graph;
        }

        private static void GenerateTwoPlatformGraph(PlatformGraphGenerator graph)
        {
            graph.Config.ScanCenter = new Vector2(1.5f, 2.5f);
            graph.Config.ScanSize = new Vector2(8f, 12f);
            graph.GeneratePlatformGraph();
        }

        private static PlatformGraphGenerator GenerateEdgeStepUpGraph(GameObject host)
        {
            var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
            graph.Config.ObstacleLayer = 1 << ObstacleLayer;
            graph.Config.ScanCenter = new Vector2(1.5f, 2f);
            graph.Config.ScanSize = new Vector2(8f, 8f);
            graph.Config.CharacterRadius = 0.4f;
            graph.Config.CharacterHeight = 1.8f;
            graph.GeneratePlatformGraph();

            var calculator = host.AddComponent<JumpLinkCalculator>();
            calculator.Config.MaxJumpVelocity = 18f;
            calculator.Config.MaxJumpHeight = 6f;
            calculator.Config.MaxHorizontalDistance = 4f;
            calculator.Config.TrajectoryCheckRadius = 0.4f;
            calculator.GenerateJumpLinks();

            return graph;
        }

        private static PlatformGraphGenerator GenerateRoom016StyleStepUpGraph(GameObject host)
        {
            var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
            graph.Config.ObstacleLayer = 1 << ObstacleLayer;
            graph.Config.ScanCenter = new Vector2(38f, 0.5f);
            graph.Config.ScanSize = new Vector2(84f, 24f);
            graph.Config.CharacterRadius = 0.4f;
            graph.Config.CharacterHeight = 1.8f;
            graph.GeneratePlatformGraph();

            var calculator = host.AddComponent<JumpLinkCalculator>();
            calculator.Config.GravityScale = 5f;
            calculator.Config.MaxJumpVelocity = 20f;
            calculator.Config.MaxJumpHeight = 4f;
            calculator.Config.MaxHorizontalDistance = 6f;
            calculator.Config.MaxFallHeight = 18f;
            calculator.Config.AirJumpCount = 1;
            calculator.Config.AirJumpVelocity = 20f;
            calculator.Config.TrajectoryCheckRadius = 0.4f;
            calculator.GenerateJumpLinks();

            return graph;
        }

        private static PlatformGraphGenerator GenerateRoom015StyleStairGraph(GameObject host)
        {
            var graph = CreateGraph(host, groundMask: 1 << GroundLayer, oneWayMask: 0);
            graph.Config.ObstacleLayer = 0;
            graph.Config.ScanCenter = new Vector2(51.5f, 4.5f);
            graph.Config.ScanSize = new Vector2(107f, 15f);
            graph.Config.CharacterRadius = 0.4f;
            graph.Config.CharacterHeight = 1.8f;
            graph.Config.NodeSpacing = 1.5f;
            graph.Config.EdgeInset = 0.3f;
            graph.GeneratePlatformGraph();

            var calculator = host.AddComponent<JumpLinkCalculator>();
            calculator.Config.GravityScale = 5f;
            calculator.Config.MaxJumpVelocity = 20f;
            calculator.Config.MaxJumpHeight = 4f;
            calculator.Config.MaxHorizontalDistance = 6f;
            calculator.Config.MaxFallHeight = 15f;
            calculator.Config.AirJumpCount = 1;
            calculator.Config.AirJumpVelocity = 20f;
            calculator.Config.TrajectoryCheckRadius = 0.4f;
            calculator.GenerateJumpLinks();

            return graph;
        }

        private static bool HasJumpLink(PlatformGraphGenerator graph, Collider2D from, Collider2D to)
        {
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Jump &&
                graph.GetNode(link.FromNodeId).TryGetCollider(out var fromCollider) &&
                graph.GetNode(link.ToNodeId).TryGetCollider(out var toCollider) &&
                fromCollider == from &&
                toCollider == to);
        }

        private static bool HasFallLinkNear(
            PlatformGraphGenerator graph,
            Collider2D from,
            Collider2D to,
            Vector2 expectedFrom,
            Vector2 expectedTo)
        {
            const float tolerance = 0.35f;
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Fall &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                toNode.PlatformCollider == to &&
                Vector2.Distance(fromNode.Position, expectedFrom) <= tolerance &&
                Vector2.Distance(toNode.Position, expectedTo) <= tolerance);
        }

        private static bool HasFallLinkFromEdgeToPlatform(
            PlatformGraphGenerator graph,
            Collider2D from,
            PlatformNodeType fromType,
            Collider2D to)
        {
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Fall &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                fromNode.NodeType == fromType &&
                toNode.PlatformCollider == to);
        }

        private static bool HasJumpLinkFromEdgeToPlatform(
            PlatformGraphGenerator graph,
            Collider2D from,
            PlatformNodeType fromType,
            Collider2D to)
        {
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Jump &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                fromNode.NodeType == fromType &&
                toNode.PlatformCollider == to);
        }

        private static bool HasJumpLinkNear(
            PlatformGraphGenerator graph,
            Collider2D from,
            Collider2D to,
            Vector2 expectedFrom,
            Vector2 expectedTo)
        {
            const float tolerance = 0.45f;
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Jump &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                toNode.PlatformCollider == to &&
                Vector2.Distance(fromNode.Position, expectedFrom) <= tolerance &&
                Vector2.Distance(toNode.Position, expectedTo) <= tolerance);
        }

        private static bool HasJumpLinkToSafeLandingNearLedge(
            PlatformGraphGenerator graph,
            Collider2D from,
            Collider2D to,
            Vector2 expectedFrom,
            float minLandingX,
            float maxLandingX,
            float landingY)
        {
            const float fromTolerance = 0.45f;
            const float yTolerance = 0.05f;
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Jump &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                toNode.PlatformCollider == to &&
                Vector2.Distance(fromNode.Position, expectedFrom) <= fromTolerance &&
                toNode.NodeType == PlatformNodeType.Surface &&
                toNode.Position.x >= minLandingX &&
                toNode.Position.x <= maxLandingX &&
                Mathf.Abs(toNode.Position.y - landingY) <= yTolerance);
        }

        private static bool HasJumpLinkToSurfacePastX(
            PlatformGraphGenerator graph,
            Collider2D from,
            Collider2D to,
            Vector2 expectedFrom,
            float minX,
            float landingY)
        {
            const float fromTolerance = 0.45f;
            const float yTolerance = 0.05f;
            return graph.Links.Any(link =>
                link.LinkType == PlatformLinkType.Jump &&
                graph.GetNode(link.FromNodeId) is { } fromNode &&
                graph.GetNode(link.ToNodeId) is { } toNode &&
                fromNode.PlatformCollider == from &&
                toNode.PlatformCollider == to &&
                Vector2.Distance(fromNode.Position, expectedFrom) <= fromTolerance &&
                toNode.NodeType == PlatformNodeType.Surface &&
                toNode.Position.x > minX &&
                Mathf.Abs(toNode.Position.y - landingY) <= yTolerance);
        }

        private static string BuildLocalGraphDebug(PlatformGraphGenerator graph, float minX, float maxX)
        {
            var nodes = graph.Nodes
                .Where(node => node.Position.x >= minX && node.Position.x <= maxX)
                .Select(node => $"{node.NodeId}:{node.NodeType}@({node.Position.x:F2},{node.Position.y:F2})/g{node.SurfaceGroupId}/a{node.IsTransitionAnchor}");
            var links = graph.Links
                .Where(link => link.LinkType != PlatformLinkType.Walk)
                .Select(link => (link, from: graph.GetNode(link.FromNodeId), to: graph.GetNode(link.ToNodeId)))
                .Where(item =>
                    item.from.HasValue &&
                    item.to.HasValue &&
                    (item.from.Value.Position.x >= minX && item.from.Value.Position.x <= maxX ||
                     item.to.Value.Position.x >= minX && item.to.Value.Position.x <= maxX))
                .Select(item => $"{item.link.LinkType} {item.link.FromNodeId}@({item.from.Value.Position.x:F2},{item.from.Value.Position.y:F2}) -> {item.link.ToNodeId}@({item.to.Value.Position.x:F2},{item.to.Value.Position.y:F2})");

            return "nodes: " + string.Join(", ", nodes) + "\nlinks: " + string.Join(", ", links);
        }

        private static GameObject CreatePlatform(string name, int layer, Vector2 position, Vector2 size)
        {
            var platform = new GameObject(name) { layer = layer };
            platform.transform.position = position;
            var collider = platform.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return platform;
        }

        private static PolygonCollider2D CreateMultiPathPlatform(
            string name,
            int layer,
            params (Vector2 center, Vector2 size)[] platforms)
        {
            var platform = new GameObject(name) { layer = layer };
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

        private static PolygonCollider2D CreateMultiPathPolygon(
            string name,
            int layer,
            params Vector2[][] paths)
        {
            var platform = new GameObject(name) { layer = layer };
            var collider = platform.AddComponent<PolygonCollider2D>();
            collider.pathCount = paths.Length;

            for (int i = 0; i < paths.Length; i++)
                collider.SetPath(i, paths[i]);

            Physics2D.SyncTransforms();
            return collider;
        }

        private static Vector2[] CreateRectPath(Vector2 center, Vector2 size)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            return new[]
            {
                new Vector2(center.x - halfWidth, center.y - halfHeight),
                new Vector2(center.x - halfWidth, center.y + halfHeight),
                new Vector2(center.x + halfWidth, center.y + halfHeight),
                new Vector2(center.x + halfWidth, center.y - halfHeight)
            };
        }
    }

    internal static class PlatformNodeDataTestExtensions
    {
        public static bool TryGetCollider(this PlatformNodeData? node, out Collider2D collider)
        {
            collider = node?.PlatformCollider;
            return collider != null;
        }
    }
}
