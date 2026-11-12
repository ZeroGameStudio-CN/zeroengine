namespace ZeroEngine.World.Presentation
{
    public readonly struct WorldVisibilityRequestSnapshot
    {
        private readonly string _targetId;
        private readonly string _channelId;
        private readonly string _sourceId;
        private readonly string _reason;

        internal WorldVisibilityRequestSnapshot(long tokenId, WorldVisibilityRequest request)
        {
            TokenId = tokenId;
            _targetId = request.TargetId;
            _channelId = request.ChannelId;
            _sourceId = request.SourceId;
            _reason = request.Reason;
            Visibility = request.Visibility;
        }

        public long TokenId { get; }
        public string TargetId => _targetId ?? string.Empty;
        public string ChannelId => _channelId ?? string.Empty;
        public string SourceId => _sourceId ?? string.Empty;
        public string Reason => _reason ?? string.Empty;
        public float Visibility { get; }
    }
}
