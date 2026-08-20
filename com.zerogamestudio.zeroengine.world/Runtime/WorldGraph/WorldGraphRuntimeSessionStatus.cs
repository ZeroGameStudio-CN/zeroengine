namespace ZeroEngine.World.WorldGraph
{
    public enum WorldGraphRuntimeSessionStatus
    {
        None = 0,
        Loaded = 1,
        Unloaded = 2,
        GraphMissing = 3,
        GraphMismatch = 4,
        StartCellMissing = 5,
        StartAnchorMissing = 6,
        TargetCellMissing = 7,
        AnchorNotFound = 8,
        ActorMissing = 9,
        StreamingBoundaryMissing = 10,
        StreamingFailed = 11,
        TravelFailed = 12,
        UnloadFailed = 13,
        Cancelled = 14,
        Failed = 15,
        Busy = 16,
        NotLoaded = 17,
        ActiveCellMismatch = 18,
        Traveled = 19,
        LinkNotFound = 20,
        OriginMismatch = 21,
        HandoffCompleted = 22,
        HandoffConnectionMissing = 23,
        HandoffConnectionNotWalkable = 24,
        HandoffTargetLoadFailed = 25,
        HandoffSwitchFailed = 26
    }
}
