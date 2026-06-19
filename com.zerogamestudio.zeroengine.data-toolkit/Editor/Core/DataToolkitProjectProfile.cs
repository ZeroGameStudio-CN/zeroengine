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
            IEnumerable<IDataToolkitAssetInspectorProvider> assetInspectorProviders = null,
            IEnumerable<IDataToolkitFooterProvider> footerProviders = null,
            IEnumerable<IDataToolkitValidationProvider> validationProviders = null)
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
            FooterProviders = (footerProviders ?? Array.Empty<IDataToolkitFooterProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
            ValidationProviders = (validationProviders ?? Array.Empty<IDataToolkitValidationProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
        }

        public DataToolkitProjectSettings Settings { get; }
        public IReadOnlyList<IDataToolkitToolbarProvider> ToolbarProviders { get; }
        public IReadOnlyList<IDataToolkitAssetInspectorProvider> AssetInspectorProviders { get; }
        public IReadOnlyList<IDataToolkitFooterProvider> FooterProviders { get; }
        public IReadOnlyList<IDataToolkitValidationProvider> ValidationProviders { get; }
    }
}
