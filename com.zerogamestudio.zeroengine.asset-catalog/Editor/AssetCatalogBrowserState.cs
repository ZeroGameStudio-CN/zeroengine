using System;

namespace ZeroEngine.AssetCatalog
{
    public enum AssetCatalogBrowserLayoutMode
    {
        SideBySide,
        Stacked
    }

    public readonly struct AssetCatalogBrowserLayout
    {
        public AssetCatalogBrowserLayout(AssetCatalogBrowserLayoutMode mode, float resultWidth, float detailWidth)
        {
            Mode = mode;
            ResultWidth = resultWidth;
            DetailWidth = detailWidth;
        }

        public AssetCatalogBrowserLayoutMode Mode { get; }
        public float ResultWidth { get; }
        public float DetailWidth { get; }
    }

    public static class AssetCatalogBrowserLayoutPolicy
    {
        public const float MinimumResultWidth = 360f;
        public const float MinimumDetailWidth = 420f;
        public const float PaneGap = 12f;
        public const float StackedThreshold = MinimumResultWidth + MinimumDetailWidth + PaneGap;

        public static AssetCatalogBrowserLayout Calculate(float availableWidth)
        {
            if (availableWidth < StackedThreshold)
                return new AssetCatalogBrowserLayout(AssetCatalogBrowserLayoutMode.Stacked, Math.Max(0f, availableWidth), Math.Max(0f, availableWidth));
            float resultWidth = Math.Max(MinimumResultWidth, Math.Min(520f, availableWidth * 0.42f));
            float detailWidth = availableWidth - PaneGap - resultWidth;
            return new AssetCatalogBrowserLayout(AssetCatalogBrowserLayoutMode.SideBySide, resultWidth, detailWidth);
        }
    }

    public sealed class AssetCatalogSelectionState
    {
        private long _previewGeneration;

        public string SelectedIdentityKey { get; private set; }
        public long PreviewGeneration => _previewGeneration;

        public bool Select(AssetCatalogIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            string key = identity.StableKey;
            if (string.Equals(SelectedIdentityKey, key, StringComparison.Ordinal)) return false;
            SelectedIdentityKey = key;
            _previewGeneration++;
            return true;
        }

        public bool IsCurrentPreview(string identityKey, long previewGeneration)
        {
            return string.Equals(SelectedIdentityKey, identityKey, StringComparison.Ordinal) && previewGeneration == _previewGeneration;
        }
    }
}
