using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class PlatformPathfindingDiagnosticsTests
    {
        [SetUp]
        public void SetUp()
        {
            PlatformPathfindingDiagnostics.BeginCapture(reset: true);
        }

        [TearDown]
        public void TearDown()
        {
            PlatformPathfindingDiagnostics.EndCapture();
            PlatformPathfindingDiagnostics.Reset();
        }

        [Test]
        public void DisabledRecording_IsNoOp()
        {
            PlatformPathfindingDiagnostics.EndCapture();
            PlatformPathfindingDiagnostics.Reset();

            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.RequestSubmitted);
            PlatformPathfindingDiagnostics.RecordPathResult(PlatformPathResult.Failed(
                PlatformPathFailureReason.Throttled,
                Vector3.zero,
                Vector3.right,
                Vector3.right,
                requestStarted: false));
            RequestWithoutGraph();

            var snapshot = PlatformPathfindingDiagnostics.Capture();

            Assert.IsFalse(PlatformPathfindingDiagnostics.IsCapturing);
            Assert.AreEqual(0, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSubmitted));
            Assert.AreEqual(0, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestFailed));
            Assert.AreEqual(0, snapshot.GetFailureCount(PlatformPathFailureReason.Throttled));
            Assert.AreEqual(0, snapshot.GetMetric(PlatformPathfindingMetricKind.PathRequest).SampleCount);
        }

        [Test]
        public void BeginAndEndCapture_DefineExplicitMeasurementWindow()
        {
            PlatformPathfindingDiagnostics.EndCapture();
            PlatformPathfindingDiagnostics.BeginCapture(reset: true);

            Assert.IsTrue(PlatformPathfindingDiagnostics.IsCapturing);
            RequestWithoutGraph();

            var snapshot = PlatformPathfindingDiagnostics.EndCapture();

            Assert.IsFalse(PlatformPathfindingDiagnostics.IsCapturing);
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSubmitted));
            Assert.AreEqual(1, snapshot.GetMetric(PlatformPathfindingMetricKind.PathRequest).SampleCount);
        }

        [Test]
        public void Reset_ClearsMetricsCountersAndFailures()
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.RequestSubmitted);
            PlatformPathfindingDiagnostics.RecordPathResult(PlatformPathResult.Failed(
                PlatformPathFailureReason.Throttled,
                Vector3.zero,
                Vector3.right,
                Vector3.right,
                requestStarted: false));
            RequestWithoutGraph();

            PlatformPathfindingDiagnostics.Reset();
            var snapshot = PlatformPathfindingDiagnostics.Capture();

            Assert.AreEqual(0, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSubmitted));
            Assert.AreEqual(0, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestFailed));
            Assert.AreEqual(0, snapshot.GetFailureCount(PlatformPathFailureReason.Throttled));
            Assert.AreEqual(0, snapshot.GetMetric(PlatformPathfindingMetricKind.PathRequest).SampleCount);
            Assert.AreEqual(0, snapshot.GetMetric(PlatformPathfindingMetricKind.PathRequest).ElapsedTicks);
        }

        [Test]
        public void InstrumentedRequest_RecordsOneNonNegativeMetricSample()
        {
            RequestWithoutGraph();

            var metric = PlatformPathfindingDiagnostics.Capture()
                .GetMetric(PlatformPathfindingMetricKind.PathRequest);

            Assert.AreEqual(1, metric.SampleCount);
            Assert.GreaterOrEqual(metric.ElapsedTicks, 0);
            Assert.GreaterOrEqual(metric.ElapsedMilliseconds, 0d);
        }

        [Test]
        public void RecordPathResult_SeparatesSuccessPartialFailureAndStartedCounts()
        {
            PlatformPathfindingDiagnostics.RecordPathResult(PlatformPathResult.Failed(
                PlatformPathFailureReason.Throttled,
                Vector3.zero,
                Vector3.right,
                Vector3.right,
                requestStarted: false));
            PlatformPathfindingDiagnostics.RecordPathResult(PlatformPathResult.Partial(
                PlatformPathFailureReason.PartialPath,
                Vector3.zero,
                Vector3.right,
                Vector3.one,
                null,
                null));
            PlatformPathfindingDiagnostics.RecordPathResult(PlatformPathResult.Succeeded(
                Vector3.zero,
                Vector3.right,
                Vector3.right,
                null,
                null));

            var snapshot = PlatformPathfindingDiagnostics.Capture();

            Assert.AreEqual(2, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestStarted));
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSucceeded));
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestPartial));
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestFailed));
            Assert.AreEqual(1, snapshot.GetFailureCount(PlatformPathFailureReason.Throttled));
            Assert.AreEqual(1, snapshot.GetFailureCount(PlatformPathFailureReason.PartialPath));
        }

        [Test]
        public void AddAndRecordMaximum_UseSumAndHighWaterMarkSemantics()
        {
            PlatformPathfindingDiagnostics.Add(PlatformPathfindingCounterKind.ExpandedNodes, 4);
            PlatformPathfindingDiagnostics.Add(PlatformPathfindingCounterKind.ExpandedNodes, 7);
            PlatformPathfindingDiagnostics.RecordMaximum(PlatformPathfindingCounterKind.OpenSetPeak, 6);
            PlatformPathfindingDiagnostics.RecordMaximum(PlatformPathfindingCounterKind.OpenSetPeak, 3);
            PlatformPathfindingDiagnostics.RecordMaximum(PlatformPathfindingCounterKind.OpenSetPeak, 9);

            var snapshot = PlatformPathfindingDiagnostics.Capture();

            Assert.AreEqual(11, snapshot.GetCounter(PlatformPathfindingCounterKind.ExpandedNodes));
            Assert.AreEqual(9, snapshot.GetCounter(PlatformPathfindingCounterKind.OpenSetPeak));
        }

        [Test]
        public void TryRequestPath_RecordsPublicRequestBoundary()
        {
            RequestWithoutGraph();

            var snapshot = PlatformPathfindingDiagnostics.Capture();
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSubmitted));
            Assert.AreEqual(0, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestStarted));
            Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestFailed));
            Assert.AreEqual(1, snapshot.GetFailureCount(PlatformPathFailureReason.MissingGraphGenerator));
            Assert.AreEqual(1, snapshot.GetMetric(PlatformPathfindingMetricKind.PathRequest).SampleCount);
        }

        [Test]
        public void TryEvaluateRoute_RecordsPublicRouteBoundary()
        {
            var host = new GameObject("PathfindingDiagnosticsRouteTest");
            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(null);

                bool success = pathfinder.TryEvaluateRoute(
                    new PlatformRouteQuery(Vector3.zero, Vector3.right),
                    out var result);

                var snapshot = PlatformPathfindingDiagnostics.Capture();
                Assert.IsFalse(success);
                Assert.AreEqual(PlatformPathFailureReason.MissingGraphGenerator, result.FailureReason);
                Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestSubmitted));
                Assert.AreEqual(1, snapshot.GetCounter(PlatformPathfindingCounterKind.RequestFailed));
                Assert.AreEqual(1, snapshot.GetFailureCount(PlatformPathFailureReason.MissingGraphGenerator));
                Assert.AreEqual(1, snapshot.GetMetric(PlatformPathfindingMetricKind.RouteEvaluation).SampleCount);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GraphJumpAndFindPath_RecordCoreMetrics()
        {
            var host = new GameObject("PathfindingDiagnosticsCoreMetricsTest");
            var lower = CreatePlatform(
                "PathfindingDiagnosticsLowerPlatform",
                new Vector2(0f, 0f),
                new Vector2(10f, 0.2f));
            var upper = CreatePlatform(
                "PathfindingDiagnosticsUpperPlatform",
                new Vector2(0f, 3f),
                new Vector2(2f, 0.2f));

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

                var snapshot = PlatformPathfindingDiagnostics.Capture();
                Assert.IsTrue(success, result.FailureReason.ToString());
                Assert.AreEqual(1, snapshot.GetMetric(PlatformPathfindingMetricKind.GraphBuild).SampleCount);
                Assert.AreEqual(1, snapshot.GetMetric(PlatformPathfindingMetricKind.JumpLinkBuild).SampleCount);
                Assert.Greater(snapshot.GetMetric(PlatformPathfindingMetricKind.FindPath).SampleCount, 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.GraphNodes), 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.GraphLinks), 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.CandidateTargetsEvaluated), 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.FindPathCalls), 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.ExpandedNodes), 0);
                Assert.Greater(snapshot.GetCounter(PlatformPathfindingCounterKind.OpenSetPeak), 0);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lower);
                Object.DestroyImmediate(upper);
            }
        }

        private static void RequestWithoutGraph()
        {
            var host = new GameObject("PathfindingDiagnosticsRequestTest");
            try
            {
                var pathfinder = host.AddComponent<Platform2DPathfinder>();
                pathfinder.SetGraphGenerator(null);
                pathfinder.TryRequestPath(
                    new PlatformPathRequest(Vector3.zero, Vector3.right),
                    out _);
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
    }
}
