namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldGraphHandoffRequest
    {
        public WorldGraphHandoffRequest(
            string sourceWorldGraphId,
            string sourceCellId,
            string sourceBoundaryId)
        {
            SourceWorldGraphId = sourceWorldGraphId ?? string.Empty;
            SourceCellId = sourceCellId ?? string.Empty;
            SourceBoundaryId = sourceBoundaryId ?? string.Empty;
        }

        public string SourceWorldGraphId { get; }
        public string SourceCellId { get; }
        public string SourceBoundaryId { get; }
    }
}
