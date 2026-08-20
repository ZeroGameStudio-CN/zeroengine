using System;
using System.Collections.Generic;
using System.Linq;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public sealed class WorldGraphGraduationProfile
    {
        public WorldGraphGraduationProfile(
            WorldGraphSO graph,
            string graphAssetPath,
            string expectedWorldGraphId,
            string startCellId,
            string startAnchorId,
            IEnumerable<WorldTravelMode> requiredTravelModes,
            IEnumerable<WorldAddressablesGroupContract> addressablesGroups,
            IEnumerable<WorldAddressableAssetContract> addressableAssets,
            Func<WorldCellDefinition, string> getCellScenePath,
            Func<WorldCellDefinition, string> getNavigationAssetPath,
            Func<string, string> getWorldCellRootName,
            Func<WorldCellLayer, string> getLayerRootName,
            Func<WorldCellLayer, string> getLayerReadinessMarkerName,
            Func<string, string, string> getTravelPortalName,
            Func<string, string, string> getStreamingBoundaryName,
            string geometryContentObjectName,
            string navigationReadinessSourceScriptGuid,
            string navigationSourceId,
            bool requireStrictNavigationSceneBinding,
            WorldGraphConnectionNetworkSO connectionNetwork = null,
            IReadOnlyDictionary<string, WorldGraphSO> connectedGraphsById = null)
        {
            Graph = graph;
            GraphAssetPath = graphAssetPath;
            ExpectedWorldGraphId = expectedWorldGraphId;
            StartCellId = startCellId;
            StartAnchorId = startAnchorId;
            RequiredTravelModes = requiredTravelModes?.Distinct().ToArray() ?? Array.Empty<WorldTravelMode>();
            AddressablesGroups = addressablesGroups?.ToArray() ?? Array.Empty<WorldAddressablesGroupContract>();
            AddressableAssets = addressableAssets?.ToArray() ?? Array.Empty<WorldAddressableAssetContract>();
            GetCellScenePath = getCellScenePath;
            GetNavigationAssetPath = getNavigationAssetPath;
            GetWorldCellRootName = getWorldCellRootName;
            GetLayerRootName = getLayerRootName;
            GetLayerReadinessMarkerName = getLayerReadinessMarkerName;
            GetTravelPortalName = getTravelPortalName;
            GetStreamingBoundaryName = getStreamingBoundaryName;
            GeometryContentObjectName = geometryContentObjectName;
            NavigationReadinessSourceScriptGuid = navigationReadinessSourceScriptGuid;
            NavigationSourceId = navigationSourceId;
            RequireStrictNavigationSceneBinding = requireStrictNavigationSceneBinding;
            ConnectionNetwork = connectionNetwork;
            ConnectedGraphsById = connectedGraphsById ?? new Dictionary<string, WorldGraphSO>();
        }

        public WorldGraphSO Graph { get; }
        public string GraphAssetPath { get; }
        public string ExpectedWorldGraphId { get; }
        public string StartCellId { get; }
        public string StartAnchorId { get; }
        public IReadOnlyList<WorldTravelMode> RequiredTravelModes { get; }
        public IReadOnlyList<WorldAddressablesGroupContract> AddressablesGroups { get; }
        public IReadOnlyList<WorldAddressableAssetContract> AddressableAssets { get; }
        public Func<WorldCellDefinition, string> GetCellScenePath { get; }
        public Func<WorldCellDefinition, string> GetNavigationAssetPath { get; }
        public Func<string, string> GetWorldCellRootName { get; }
        public Func<WorldCellLayer, string> GetLayerRootName { get; }
        public Func<WorldCellLayer, string> GetLayerReadinessMarkerName { get; }
        public Func<string, string, string> GetTravelPortalName { get; }
        public Func<string, string, string> GetStreamingBoundaryName { get; }
        public string GeometryContentObjectName { get; }
        public string NavigationReadinessSourceScriptGuid { get; }
        public string NavigationSourceId { get; }
        public bool RequireStrictNavigationSceneBinding { get; }
        public WorldGraphConnectionNetworkSO ConnectionNetwork { get; }
        public IReadOnlyDictionary<string, WorldGraphSO> ConnectedGraphsById { get; }
    }

    public readonly struct WorldAddressablesGroupContract
    {
        public WorldAddressablesGroupContract(
            string groupName,
            string groupAssetPath,
            IEnumerable<string> requiredSchemaAssetPaths)
        {
            GroupName = groupName;
            GroupAssetPath = groupAssetPath;
            RequiredSchemaAssetPaths = requiredSchemaAssetPaths?.ToArray() ?? Array.Empty<string>();
        }

        public string GroupName { get; }
        public string GroupAssetPath { get; }
        public IReadOnlyList<string> RequiredSchemaAssetPaths { get; }
    }

    public readonly struct WorldAddressableAssetContract
    {
        public WorldAddressableAssetContract(
            string groupName,
            string groupAssetPath,
            string assetPath,
            string address,
            Type expectedAssetType)
        {
            GroupName = groupName;
            GroupAssetPath = groupAssetPath;
            AssetPath = assetPath;
            Address = address;
            ExpectedAssetType = expectedAssetType;
        }

        public string GroupName { get; }
        public string GroupAssetPath { get; }
        public string AssetPath { get; }
        public string Address { get; }
        public Type ExpectedAssetType { get; }
    }
}
