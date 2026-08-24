using System;
using System.Diagnostics;
using System.Threading;
using Unity.Profiling;

namespace ZeroEngine.Pathfinding2D
{
    public enum PlatformPathfindingMetricKind
    {
        PathRequest,
        RouteEvaluation,
        FindPath,
        GraphBuild,
        JumpLinkBuild,
        Count
    }

    public enum PlatformPathfindingCounterKind
    {
        RequestSubmitted,
        RequestStarted,
        RequestSucceeded,
        RequestPartial,
        RequestFailed,
        CandidateTargetsEvaluated,
        FindPathCalls,
        ExpandedNodes,
        OpenSetPeak,
        PartialFallbackAttempts,
        ReachableFallbackAttempts,
        GraphNodes,
        GraphLinks,
        JumpLinksCreated,
        FallLinksCreated,
        DropLinksCreated,
        Count
    }

    public readonly struct PlatformPathfindingMetricSnapshot
    {
        public readonly long SampleCount;
        public readonly long ElapsedTicks;

        public double ElapsedMilliseconds => ElapsedTicks * 1000d / Stopwatch.Frequency;

        public PlatformPathfindingMetricSnapshot(long sampleCount, long elapsedTicks)
        {
            SampleCount = sampleCount;
            ElapsedTicks = elapsedTicks;
        }
    }

    public sealed class PlatformPathfindingDiagnosticsSnapshot
    {
        private readonly long[] metricSampleCounts;
        private readonly long[] metricElapsedTicks;
        private readonly long[] counters;
        private readonly long[] failureCounts;

        internal PlatformPathfindingDiagnosticsSnapshot(
            long[] metricSampleCounts,
            long[] metricElapsedTicks,
            long[] counters,
            long[] failureCounts)
        {
            this.metricSampleCounts = metricSampleCounts;
            this.metricElapsedTicks = metricElapsedTicks;
            this.counters = counters;
            this.failureCounts = failureCounts;
        }

        public PlatformPathfindingMetricSnapshot GetMetric(PlatformPathfindingMetricKind kind)
        {
            int index = PlatformPathfindingDiagnostics.GetMetricIndex(kind);
            return new PlatformPathfindingMetricSnapshot(
                metricSampleCounts[index],
                metricElapsedTicks[index]);
        }

        public long GetCounter(PlatformPathfindingCounterKind kind)
        {
            return counters[PlatformPathfindingDiagnostics.GetCounterIndex(kind)];
        }

        public long GetFailureCount(PlatformPathFailureReason reason)
        {
            return failureCounts[PlatformPathfindingDiagnostics.GetFailureIndex(reason)];
        }
    }

    /// <summary>
    /// Backend-neutral process counters for platform pathfinding calibration and regression gates.
    /// Recording is allocation-free; Capture and Reset are intended for test/report boundaries.
    /// </summary>
    public static class PlatformPathfindingDiagnostics
    {
        private const int MetricCount = (int)PlatformPathfindingMetricKind.Count;
        private const int CounterCount = (int)PlatformPathfindingCounterKind.Count;

        private static long[] MetricSampleCounts;
        private static long[] MetricElapsedTicks;
        private static long[] Counters;
        private static long[] FailureCounts;
        private static ProfilerMarker[] MetricMarkers;

        private static int captureEnabled;

        public static bool IsCapturing => Volatile.Read(ref captureEnabled) != 0;

        /// <summary>
        /// Starts an explicit diagnostics window. Call at a boundary where no pathfinding work is running.
        /// </summary>
        public static void BeginCapture(bool reset = true)
        {
            Volatile.Write(ref captureEnabled, 0);
            EnsureInitialized();
            if (reset)
                ResetInitializedValues();

            Volatile.Write(ref captureEnabled, 1);
        }

        /// <summary>
        /// Stops diagnostics and captures the completed window.
        /// </summary>
        /// <remarks>
        /// The caller must ensure no pathfinding work is concurrently recording when this boundary is used.
        /// </remarks>
        public static PlatformPathfindingDiagnosticsSnapshot EndCapture()
        {
            Volatile.Write(ref captureEnabled, 0);
            return Capture();
        }

        internal static MeasurementScope Measure(PlatformPathfindingMetricKind kind)
        {
            if (!IsCapturing)
                return default;

            return new MeasurementScope(GetMetricIndex(kind));
        }

        public static void Increment(PlatformPathfindingCounterKind kind)
        {
            if (!IsCapturing)
                return;

            Interlocked.Increment(ref Counters[GetCounterIndex(kind)]);
        }

        public static void Add(PlatformPathfindingCounterKind kind, long value)
        {
            if (!IsCapturing || value == 0)
                return;

            Interlocked.Add(ref Counters[GetCounterIndex(kind)], value);
        }

        public static void RecordMaximum(PlatformPathfindingCounterKind kind, long value)
        {
            if (!IsCapturing)
                return;

            int index = GetCounterIndex(kind);
            long current = Interlocked.Read(ref Counters[index]);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(ref Counters[index], value, current);
                if (observed == current)
                    return;

                current = observed;
            }
        }

        public static void RecordPathResult(PlatformPathResult result)
        {
            if (!IsCapturing)
                return;

            if (result.RequestStarted)
                Interlocked.Increment(ref Counters[(int)PlatformPathfindingCounterKind.RequestStarted]);

            if (result.Success)
            {
                Interlocked.Increment(ref Counters[(int)PlatformPathfindingCounterKind.RequestSucceeded]);
            }
            else if (result.CompletionKind == PlatformPathCompletionKind.Partial)
            {
                Interlocked.Increment(ref Counters[(int)PlatformPathfindingCounterKind.RequestPartial]);
            }
            else
            {
                Interlocked.Increment(ref Counters[(int)PlatformPathfindingCounterKind.RequestFailed]);
            }

            if (result.FailureReason != PlatformPathFailureReason.None)
                Interlocked.Increment(ref FailureCounts[GetFailureIndex(result.FailureReason)]);
        }

        public static PlatformPathfindingDiagnosticsSnapshot Capture()
        {
            EnsureInitialized();
            var metricSampleCounts = CaptureValues(MetricSampleCounts);
            var metricElapsedTicks = CaptureValues(MetricElapsedTicks);
            var counters = CaptureValues(Counters);
            var failureCounts = CaptureValues(FailureCounts);
            return new PlatformPathfindingDiagnosticsSnapshot(
                metricSampleCounts,
                metricElapsedTicks,
                counters,
                failureCounts);
        }

        public static void Reset()
        {
            EnsureInitialized();
            ResetInitializedValues();
        }

        private static void EnsureInitialized()
        {
            if (MetricSampleCounts != null)
                return;

            MetricSampleCounts = new long[MetricCount];
            MetricElapsedTicks = new long[MetricCount];
            Counters = new long[CounterCount];
            FailureCounts = new long[Enum.GetValues(typeof(PlatformPathFailureReason)).Length];
            MetricMarkers = new[]
            {
                new ProfilerMarker("ZeroEngine.Pathfinding2D.PathRequest"),
                new ProfilerMarker("ZeroEngine.Pathfinding2D.RouteEvaluation"),
                new ProfilerMarker("ZeroEngine.Pathfinding2D.FindPath"),
                new ProfilerMarker("ZeroEngine.Pathfinding2D.GraphBuild"),
                new ProfilerMarker("ZeroEngine.Pathfinding2D.JumpLinkBuild")
            };
        }

        private static void ResetInitializedValues()
        {
            ResetValues(MetricSampleCounts);
            ResetValues(MetricElapsedTicks);
            ResetValues(Counters);
            ResetValues(FailureCounts);
        }

        internal static int GetMetricIndex(PlatformPathfindingMetricKind kind)
        {
            int index = (int)kind;
            if ((uint)index >= MetricCount)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

            return index;
        }

        internal static int GetCounterIndex(PlatformPathfindingCounterKind kind)
        {
            int index = (int)kind;
            if ((uint)index >= CounterCount)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

            return index;
        }

        internal static int GetFailureIndex(PlatformPathFailureReason reason)
        {
            int index = (int)reason;
            if ((uint)index >= FailureCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);

            return index;
        }

        private static void RecordMeasurement(int metricIndex, long elapsedTicks)
        {
            Interlocked.Increment(ref MetricSampleCounts[metricIndex]);
            Interlocked.Add(ref MetricElapsedTicks[metricIndex], elapsedTicks);
        }

        private static long[] CaptureValues(long[] source)
        {
            var result = new long[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = Interlocked.Read(ref source[i]);

            return result;
        }

        private static void ResetValues(long[] values)
        {
            for (int i = 0; i < values.Length; i++)
                Interlocked.Exchange(ref values[i], 0L);
        }

        internal struct MeasurementScope : IDisposable
        {
            private readonly long startedAt;
            private int metricSlot;

            internal MeasurementScope(int metricIndex)
            {
                metricSlot = metricIndex + 1;
                MetricMarkers[metricIndex].Begin();
                startedAt = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (metricSlot == 0)
                    return;

                int slot = Interlocked.Exchange(ref metricSlot, 0);
                if (slot == 0)
                    return;

                int metricIndex = slot - 1;
                long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                MetricMarkers[metricIndex].End();
                RecordMeasurement(metricIndex, elapsedTicks);
            }
        }
    }
}
