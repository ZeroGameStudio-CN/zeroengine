using System.Collections.Generic;
using UnityEditor;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldGraphAssetAuthoringUtility
    {
        public static WorldGraphSO EnsureWorldGraphAsset(
            string assetPath,
            string worldGraphId,
            IEnumerable<WorldRegionDefinition> regions,
            IEnumerable<WorldTravelLinkDefinition> travelLinks,
            IEnumerable<WorldFastTravelNodeDefinition> fastTravelNodes)
        {
            var graph = WorldAuthoringAssetUtility.EnsureScriptableObjectAsset<WorldGraphSO>(assetPath);
            if (graph == null)
            {
                return null;
            }

            graph.ConfigureForTests(worldGraphId, regions, travelLinks, fastTravelNodes);
            EditorUtility.SetDirty(graph);
            return graph;
        }
    }
}
