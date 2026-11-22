namespace ZeroEngine.GameFeel
{
    public enum FeedbackDecision
    {
        New,
        Stronger,
        Duplicate,
        CapacityExceeded,
        InvalidKey,
        InvalidStrength,
        InvalidTime,
        ClockMovedBackwards
    }

    public enum FeedbackCommitResult
    {
        Committed,
        NotAccepted,
        WrongCoordinator,
        StaleEvaluation,
        InvalidRetention
    }

    /// <summary>
    /// An immediate, single-use proposal, not a reservation. Commit after the consumer
    /// actually accepts the output. Do not retain across callbacks or clock advances.
    /// </summary>
    public readonly struct FeedbackEvaluation
    {
        public FeedbackDecision Decision { get; }
        public bool CanPlay => OwnerId != 0 &&
            (Decision == FeedbackDecision.New || Decision == FeedbackDecision.Stronger);
        public FeedbackKey Key { get; }
        public double Strength { get; }
        public double FirstAcceptedAt { get; }
        public double PreviousStrength { get; }

        internal long OwnerId { get; }
        internal long Generation { get; }
        internal long Revision { get; }
        internal int Slot { get; }
        internal double EvaluatedAt { get; }

        internal FeedbackEvaluation(FeedbackDecision decision, FeedbackKey key, double strength,
            double firstAcceptedAt, double previousStrength, long ownerId, long generation,
            long revision, int slot, double evaluatedAt)
        {
            Decision = decision;
            Key = key;
            Strength = strength;
            FirstAcceptedAt = firstAcceptedAt;
            PreviousStrength = previousStrength;
            OwnerId = ownerId;
            Generation = generation;
            Revision = revision;
            Slot = slot;
            EvaluatedAt = evaluatedAt;
        }
    }

    /// <summary>
    /// Ownership of one committed version. A previous version cannot cancel its replacement.
    /// This handle controls only retained coordination state, never a device or time source.
    /// </summary>
    public readonly struct FeedbackHandle
    {
        internal long OwnerId { get; }
        internal long Generation { get; }
        internal long EntryVersion { get; }
        internal int Slot { get; }

        internal FeedbackHandle(long ownerId, long generation, long entryVersion, int slot)
        {
            OwnerId = ownerId;
            Generation = generation;
            EntryVersion = entryVersion;
            Slot = slot;
        }
    }
}
