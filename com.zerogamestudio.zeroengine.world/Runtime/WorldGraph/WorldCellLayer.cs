using System;

namespace ZeroEngine.World.WorldGraph
{
    [Flags]
    public enum WorldCellLayer
    {
        None = 0,
        Geometry = 1 << 0,
        Collision = 1 << 1,
        Navigation = 1 << 2,
        GameplayMarkers = 1 << 3,
        LightingAndVolumes = 1 << 4,
        Audio = 1 << 5,
        All = Geometry | Collision | Navigation | GameplayMarkers | LightingAndVolumes | Audio
    }

    public enum WorldAnchorKind
    {
        Spawn,
        Door,
        Portal,
        RoadExit,
        FastTravel,
        Cutscene,
        BattleReturn,
        InteriorEntry,
        InteriorExit
    }
}
