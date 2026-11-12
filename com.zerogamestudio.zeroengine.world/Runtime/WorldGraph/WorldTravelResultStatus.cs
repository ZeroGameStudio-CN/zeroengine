namespace ZeroEngine.World.WorldGraph
{
    public enum WorldTravelResultStatus
    {
        Succeeded,
        GraphMissing,
        LinkNotFound,
        AnchorNotFound,
        OriginMismatch,
        StreamingFailed,
        Cancelled,
        Busy
    }
}
