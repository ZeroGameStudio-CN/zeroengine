using System;

namespace ZeroEngine.Interaction.Core
{
    public interface IInteractionClock { double Now { get; } }

    /// <summary>Caller-owned spatial admission. Return false when the target cannot be reached.</summary>
    public interface IInteractionRange
    {
        bool TryGetSquaredDistance(IInteractionTarget target, out double distance);
    }

    public enum InteractionMode { Press, Hold, TapOrHold }
    public enum InteractionGesture { Tap, Hold, Repeat }
    public enum InteractionStatus { Succeeded, Denied, Cancelled, Faulted }

    public readonly struct InteractionPolicy
    {
        public InteractionMode Mode { get; }
        public double HoldSeconds { get; }
        public double RepeatSeconds { get; }

        public InteractionPolicy(InteractionMode mode, double holdSeconds = 0.5, double repeatSeconds = 0)
        {
            if (!Enum.IsDefined(typeof(InteractionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (!Numbers.Finite(holdSeconds) || holdSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(holdSeconds));
            if (!Numbers.Finite(repeatSeconds) || repeatSeconds < 0) throw new ArgumentOutOfRangeException(nameof(repeatSeconds));
            Mode = mode; HoldSeconds = holdSeconds; RepeatSeconds = repeatSeconds;
        }

        public static InteractionPolicy Press => new InteractionPolicy(InteractionMode.Press);
    }

    public readonly struct InteractionCheck
    {
        public bool Allowed { get; }
        public string Reason { get; }
        private InteractionCheck(bool allowed, string reason) { Allowed = allowed; Reason = reason; }
        public static InteractionCheck Permit => new InteractionCheck(true, null);
        public static InteractionCheck Deny(string reason) => new InteractionCheck(false,
            string.IsNullOrWhiteSpace(reason) ? "Interaction denied." : reason);
    }

    public readonly struct InteractionContext
    {
        public string ActorId { get; }
        public object Actor { get; }
        public string TargetId { get; }
        public long Activation { get; }
        public InteractionGesture Gesture { get; }
        public double Time { get; }
        public double SquaredDistance { get; }
        internal InteractionContext(string actorId, object actor, InteractionRegistration target,
            InteractionGesture gesture, double time, double distance)
        {
            ActorId = actorId; Actor = actor; TargetId = target.Id; Activation = target.Activation;
            Gesture = gesture; Time = time; SquaredDistance = distance;
        }
    }

    public readonly struct InteractionResult
    {
        public InteractionContext Context { get; }
        public long RequestId { get; }
        public InteractionStatus Status { get; }
        public string Reason { get; }
        public bool Success => Status == InteractionStatus.Succeeded;
        internal InteractionResult(InteractionContext context, long requestId, InteractionStatus status, string reason)
        { Context = context; RequestId = requestId; Status = status; Reason = reason; }
    }

    /// <summary>Implementation owns its business action. Check must be side-effect free and fail closed.</summary>
    public interface IInteractionTarget
    {
        string Id { get; }
        int Priority { get; }
        bool IsAvailable { get; }
        InteractionPolicy Policy { get; }
        InteractionCheck Check(InteractionContext context);
        void Execute(InteractionRequest request);
    }

    internal static class Numbers
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
