using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Map
{
    public sealed class WorldMapState
    {
        public WorldMapState(string worldGraphId = null)
        {
            WorldGraphId = worldGraphId ?? string.Empty;
            Discovery = new WorldMapDiscoveryState();
            MarkerRegistry = new WorldMapMarkerRegistry();
        }

        public string WorldGraphId { get; private set; }
        public string ActiveCellId { get; private set; }
        public string ActiveAnchorId { get; private set; }
        public WorldMapDiscoveryState Discovery { get; }
        public WorldMapMarkerRegistry MarkerRegistry { get; }

        public bool ApplyRuntimeLocation(WorldGraphRuntimeLocation location)
        {
            if (!location.IsValid)
            {
                return false;
            }

            WorldGraphId = location.WorldGraphId;
            ActiveCellId = location.CellId;
            ActiveAnchorId = location.AnchorId;
            Discovery.DiscoverCell(location.CellId);
            if (!string.IsNullOrWhiteSpace(location.AnchorId))
            {
                Discovery.VisitAnchor(location.AnchorId);
            }

            return true;
        }

        public void ClearRuntimeLocation()
        {
            ActiveCellId = string.Empty;
            ActiveAnchorId = string.Empty;
        }
    }
}
