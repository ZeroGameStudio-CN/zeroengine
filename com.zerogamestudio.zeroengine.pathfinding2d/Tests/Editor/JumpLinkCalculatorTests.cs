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
