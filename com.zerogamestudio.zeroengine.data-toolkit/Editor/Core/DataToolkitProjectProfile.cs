using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitProjectProfile
    {
        public DataToolkitProjectProfile(
            DataToolkitProjectSettings settings,
            IEnumerable<IDataToolkitToolbarProvider> toolbarProviders = null,
            IEnumerable<IDataToolkitAssetInspectorProvider> assetInspectorProviders = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ToolbarProviders = (toolbarProviders ?? Array.Empty<IDataToolkitToolbarProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
            AssetInspectorProviders = (assetInspectorProviders ?? Array.Empty<IDataToolkitAssetInspectorProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
        }

        public DataToolkitProjectSettings Settings { get; }
        public IReadOnlyList<IDataToolkitToolbarProvider> ToolbarProviders { get; }
        public IReadOnlyList<IDataToolkitAssetInspectorProvider> AssetInspectorProviders { get; }
    }
}
