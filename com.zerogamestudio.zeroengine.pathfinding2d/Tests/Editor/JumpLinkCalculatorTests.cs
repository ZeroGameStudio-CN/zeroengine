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

        private static GameObject CreatePlatform(string name, int layer, Vector2 position, Vector2 size)
        {
            var platform = new GameObject(name) { layer = layer };
            platform.transform.position = position;
            var collider = platform.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return platform;
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
