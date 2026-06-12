using System.Collections.Generic;

namespace ZeroEngine.World.Map
{
    public interface IWorldMapMarkerProvider
    {
        void CollectMarkers(List<WorldMapMarkerDefinition> results);
    }
}
