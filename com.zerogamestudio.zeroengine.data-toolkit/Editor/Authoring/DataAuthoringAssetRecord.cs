using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringAssetRecord
    {
        public DataAuthoringAssetRecord(
            Object asset,
            string assetPath,
            string stableId,
            string displayName,
            string subtitle,
            Texture icon,
            string searchText = null)
        {
            Asset = asset;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? StableId : displayName;
            Subtitle = subtitle ?? string.Empty;
            Icon = icon;
            SearchText = string.IsNullOrWhiteSpace(searchText)
                ? $"{StableId} {DisplayName} {Subtitle} {AssetPath}"
                : searchText;
        }

        public Object Asset { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string DisplayName { get; }
        public string Subtitle { get; }
        public Texture Icon { get; }
        public string SearchText { get; }

        public bool MatchesSearch(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return SearchText.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
