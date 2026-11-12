using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.UI
{
    /// <summary>
    /// Visual styles supported by the reusable screen transition presenter.
    /// Values are serialized and must remain stable.
    /// </summary>
    public enum ScreenTransitionStyle
    {
        Fade = 0,
        CircleWipe = 1,
        DiamondWipe = 2,
        Dissolve = 3
    }

    public enum ScreenTransitionStatus
    {
        Completed = 0,
        Busy = 1
    }

    public enum ScreenTransitionLogLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public enum ScreenTransitionEasing
    {
        Linear = 0,
        InOutSine = 1
    }

    /// <summary>
    /// An immutable request for a single blackout/action/reveal sequence.
    /// </summary>
    public readonly struct ScreenTransitionRequest
    {
        public ScreenTransitionRequest(
            ScreenTransitionStyle style,
            float? nullableDurationSeconds = null,
            bool blockRaycasts = true,
            bool lockInput = true)
        {
            Style = style;
            DurationSeconds = nullableDurationSeconds;
            BlockRaycasts = blockRaycasts;
            LockInput = lockInput;
        }

        public ScreenTransitionStyle Style { get; }
        public float? DurationSeconds { get; }
        public float? NullableDurationSeconds => DurationSeconds;
        public float? Duration => DurationSeconds;
        public bool BlockRaycasts { get; }
        public bool LockInput { get; }
    }

    public readonly struct ScreenTransitionResult
    {
        public ScreenTransitionResult(ScreenTransitionStatus status)
        {
            Status = status;
        }

        public ScreenTransitionStatus Status { get; }
        public bool IsCompleted => Status == ScreenTransitionStatus.Completed;
        public bool IsBusy => Status == ScreenTransitionStatus.Busy;
    }

    /// <summary>
    /// Structured logging payload. The exception is populated only when a
    /// transition failed after it had become active.
    /// </summary>
    public readonly struct ScreenTransitionLogEntry
    {
        public ScreenTransitionLogEntry(
            ScreenTransitionLogLevel level,
            ScreenTransitionStyle style,
            string message,
            Exception exception = null)
        {
            Level = level;
            Style = style;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public ScreenTransitionLogLevel Level { get; }
        public ScreenTransitionStyle Style { get; }
        public string Message { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Structured telemetry payload emitted once for every accepted request,
    /// including Busy and failed/cancelled terminal paths.
    /// </summary>
    public readonly struct ScreenTransitionTelemetry
    {
        public ScreenTransitionTelemetry(
            ScreenTransitionStyle style,
            float durationSeconds,
            ScreenTransitionStatus status,
            bool cancelled,
            Exception exception = null)
        {
            Style = style;
            DurationSeconds = durationSeconds;
            Status = status;
            Cancelled = cancelled;
            Exception = exception;
        }

        public ScreenTransitionStyle Style { get; }
        public float DurationSeconds { get; }
        public ScreenTransitionStatus Status { get; }
        public bool Cancelled { get; }
        public Exception Exception { get; }
        public bool Succeeded => Status == ScreenTransitionStatus.Completed
            && !Cancelled
            && Exception == null;
        public bool IsBusy => Status == ScreenTransitionStatus.Busy;
    }

    /// <summary>
    /// Project-neutral hooks. A presenter copies the lease returned by the
    /// current hooks into the active request, so replacing hooks while a
    /// transition is running cannot strand or double-release that lease.
    /// </summary>
    public sealed class ScreenTransitionHooks
    {
        public ScreenTransitionHooks(
            Func<IDisposable> acquireInputLockLease = null,
            Action<ScreenTransitionLogEntry> log = null,
            Action<ScreenTransitionTelemetry> telemetry = null)
        {
            AcquireInputLockLease = acquireInputLockLease;
            Log = log;
            Telemetry = telemetry;
        }

        public Func<IDisposable> AcquireInputLockLease { get; }
        public Action<ScreenTransitionLogEntry> Log { get; }
        public Action<ScreenTransitionTelemetry> Telemetry { get; }
    }

    public interface IScreenTransitionService
    {
        bool IsTransitioning { get; }

        Task<ScreenTransitionResult> RunAsync(
            ScreenTransitionRequest request,
            Func<CancellationToken, Task> blackoutAction,
            CancellationToken cancellationToken = default);

        void SetCovered();
        void SetClear();
    }
}
