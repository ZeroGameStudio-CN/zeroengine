namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldTravelRequest
    {
        public WorldTravelRequest(string linkId)
            : this(linkId, null)
        {
        }

        public WorldTravelRequest(string linkId, string fromAnchorId)
        {
            LinkId = linkId;
            FromAnchorId = fromAnchorId;
        }

        public string LinkId { get; }
        public string FromAnchorId { get; }
    }
}
