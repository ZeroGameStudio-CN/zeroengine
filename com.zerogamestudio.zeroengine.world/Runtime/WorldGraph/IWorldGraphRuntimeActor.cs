namespace ZeroEngine.World.WorldGraph
{
    public interface IWorldGraphRuntimeActor
    {
        bool HasActor { get; }

        bool TryPlaceAtAnchor(
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor,
            WorldPosition resolvedPosition);

        bool TryPlaceAtPosition(WorldPosition position);

        bool TryPlaceAtLocation(
            WorldCellDefinition cell,
            WorldGraphRuntimeLocation location);

        WorldGraphRuntimeLocation CaptureLocation(
            WorldGraphSO graph,
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor,
            WorldGraphRuntimeLocation fallback);
    }
}
