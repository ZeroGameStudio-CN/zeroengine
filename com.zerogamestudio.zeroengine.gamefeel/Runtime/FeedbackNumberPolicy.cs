using System;

namespace ZeroEngine.GameFeel
{
    public enum FeedbackNumberMode { Individual, Action, AdaptiveStream }

    /// <summary>Captured producer intent. IDs identify lifetimes, not asset names or physics callbacks.</summary>
    public readonly struct FeedbackNumberContext : IEquatable<FeedbackNumberContext>
    {
        public readonly FeedbackNumberMode Mode;
        public readonly ulong OwnerId, SourceId;
        public readonly long GroupId, BeatId;
        public readonly bool Important;
        public bool CanMerge => !Important && OwnerId != 0 && SourceId != 0 && GroupId > 0 &&
            (Mode == FeedbackNumberMode.Action || Mode == FeedbackNumberMode.AdaptiveStream);

        public FeedbackNumberContext(FeedbackNumberMode mode, ulong ownerId, ulong sourceId, long groupId,
            bool important = false, long beatId = 0)
        { Mode = mode; OwnerId = ownerId; SourceId = sourceId; GroupId = groupId; Important = important; BeatId = beatId; }

        public bool Equals(FeedbackNumberContext other) => Mode == other.Mode && OwnerId == other.OwnerId &&
            SourceId == other.SourceId && GroupId == other.GroupId && Important == other.Important && BeatId == other.BeatId;
        public override bool Equals(object obj) => obj is FeedbackNumberContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Mode, OwnerId, SourceId, GroupId, Important, BeatId);
    }

    /// <summary>Stream hysteresis and a bounded first-hit window; all times use the caller's clock.</summary>
    public readonly struct FeedbackNumberPolicy
    {
        public readonly double EnterInterval, ExitInterval, IdleSeconds, MaximumWindow;
        public static FeedbackNumberPolicy Default => new(.1, .16, .6, .6);

        public FeedbackNumberPolicy(double enterInterval, double exitInterval, double idleSeconds, double maximumWindow)
        {
            if (!Finite(enterInterval) || !Finite(exitInterval) || !Finite(idleSeconds) || !Finite(maximumWindow) ||
                enterInterval <= 0 || exitInterval < enterInterval || idleSeconds < exitInterval || maximumWindow <= 0)
                throw new ArgumentOutOfRangeException(nameof(enterInterval));
            EnterInterval = enterInterval; ExitInterval = exitInterval; IdleSeconds = idleSeconds; MaximumWindow = maximumWindow;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
