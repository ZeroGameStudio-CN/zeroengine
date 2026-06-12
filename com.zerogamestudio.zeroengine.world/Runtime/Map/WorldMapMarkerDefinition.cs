using System;
using UnityEngine;

namespace ZeroEngine.World.Map
{
    public enum WorldMapMarkerCategory
    {
        Player,
        PartyMember,
        Enemy,
        Npc,
        Quest,
        Waypoint,
        Building,
        Portal,
        Item,
        Treasure,
        Shop,
        Custom
    }

    public enum WorldMapMarkerVisibility
    {
        Always,
        DiscoveredOnly,
        Hidden
    }

    public readonly struct WorldMapMarkerDefinition
    {
        public WorldMapMarkerDefinition(
            string markerId,
            WorldMapMarkerCategory category,
            string worldGraphId,
            string cellId,
            string anchorId,
            string label,
            Vector3 worldPosition,
            Quaternion worldRotation,
            int priority = 0,
            WorldMapMarkerVisibility visibility = WorldMapMarkerVisibility.Always)
        {
            MarkerId = markerId ?? string.Empty;
            Category = category;
            WorldGraphId = worldGraphId ?? string.Empty;
            CellId = cellId ?? string.Empty;
            AnchorId = anchorId ?? string.Empty;
            Label = label ?? string.Empty;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
            Priority = priority;
            Visibility = visibility;
        }

        public string MarkerId { get; }
        public WorldMapMarkerCategory Category { get; }
        public string WorldGraphId { get; }
        public string CellId { get; }
        public string AnchorId { get; }
        public string Label { get; }
        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
        public int Priority { get; }
        public WorldMapMarkerVisibility Visibility { get; }
        public bool IsValid => WorldMapStableId.IsStableId(MarkerId);
    }

    public readonly struct WorldMapMarkerFilter
    {
        private readonly WorldMapMarkerCategory[] _includedCategories;

        public WorldMapMarkerFilter(
            WorldMapMarkerCategory[] includedCategories,
            bool includeHidden = false,
            bool includeUndiscovered = false)
        {
            _includedCategories = includedCategories == null || includedCategories.Length == 0
                ? null
                : (WorldMapMarkerCategory[])includedCategories.Clone();
            IncludeHidden = includeHidden;
            IncludeUndiscovered = includeUndiscovered;
        }

        public bool IncludeHidden { get; }
        public bool IncludeUndiscovered { get; }
        public static WorldMapMarkerFilter All => new WorldMapMarkerFilter(null, includeHidden: true, includeUndiscovered: true);

        public bool Allows(WorldMapMarkerDefinition marker, WorldMapDiscoveryState discoveryState)
        {
            if (!AllowsCategory(marker.Category))
            {
                return false;
            }

            if (marker.Visibility == WorldMapMarkerVisibility.Hidden && !IncludeHidden)
            {
                return false;
            }

            if (marker.Visibility == WorldMapMarkerVisibility.DiscoveredOnly
                && !IncludeUndiscovered
                && !IsDiscovered(marker, discoveryState))
            {
                return false;
            }

            return true;
        }

        private bool AllowsCategory(WorldMapMarkerCategory category)
        {
            if (_includedCategories == null || _includedCategories.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < _includedCategories.Length; i++)
            {
                if (_includedCategories[i] == category)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDiscovered(WorldMapMarkerDefinition marker, WorldMapDiscoveryState discoveryState)
        {
            if (discoveryState == null)
            {
                return false;
            }

            return (!string.IsNullOrWhiteSpace(marker.CellId) && discoveryState.IsCellDiscovered(marker.CellId))
                   || (!string.IsNullOrWhiteSpace(marker.AnchorId) && discoveryState.IsAnchorVisited(marker.AnchorId));
        }
    }

    internal static class WorldMapStableId
    {
        public static bool IsStableId(string id)
        {
            return WorldMapStableIdUtility.IsStableId(id);
        }
    }
}
