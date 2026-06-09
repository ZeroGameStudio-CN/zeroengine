using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public static class DataToolkitDiagnosticsService
    {
        public static DataToolkitDiagnosticsReport BuildReport(
            DataToolkitContext context,
            IEnumerable<IDataToolkitAssetInspectorProvider> inspectorProviders)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var providers = (inspectorProviders ?? Array.Empty<IDataToolkitAssetInspectorProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
            var rows = new List<DataToolkitTypeCoverageInfo>();

            foreach (var type in ManageableDataTypeDiscovery.GetManageableScriptableObjectTypes())
            {
                var assetPaths = AssetDiscoveryService.GetAssetPathsForType(type, context.Settings);
                var samplePath = assetPaths.FirstOrDefault();
                var sampleAsset = AssetDiscoveryService.LoadFirstAssetOfType(samplePath, type);
                var customProvider = sampleAsset == null ? null : FindCustomProvider(context, providers, sampleAsset);
                var coverage = Classify(context, assetPaths.Length, sampleAsset, customProvider);
                var reason = BuildReason(context, assetPaths.Length, sampleAsset, customProvider, coverage);

                rows.Add(new DataToolkitTypeCoverageInfo(
                    type,
                    assetPaths.Length,
                    samplePath,
                    coverage,
                    reason));
            }

            return new DataToolkitDiagnosticsReport(context.Settings.ProjectId, rows);
        }

        private static DataToolkitInspectorCoverageLevel Classify(
            DataToolkitContext context,
            int assetCount,
            UnityEngine.Object sampleAsset,
            IDataToolkitAssetInspectorProvider customProvider)
        {
            if (assetCount == 0)
            {
                return DataToolkitInspectorCoverageLevel.NoAssets;
            }

            if (sampleAsset == null)
            {
                return DataToolkitInspectorCoverageLevel.Unsupported;
            }

            if (customProvider != null)
            {
                return DataToolkitInspectorCoverageLevel.FirstClass;
            }

            if (context.Settings.SafeInspectorRules.Any(rule => rule.Matches(sampleAsset.GetType())) ||
                context.Settings.DefaultInspectorMode == DataToolkitDefaultInspectorMode.SafeSummary ||
                context.Settings.DefaultInspectorMode == DataToolkitDefaultInspectorMode.LazyPreview)
            {
                return DataToolkitInspectorCoverageLevel.SafePreview;
            }

            if (context.Settings.DefaultInspectorMode == DataToolkitDefaultInspectorMode.FullInspector)
            {
                return DataToolkitInspectorCoverageLevel.NativeInspectorFallback;
            }

            return DataToolkitInspectorCoverageLevel.RawOdinFallback;
        }

        private static string BuildReason(
            DataToolkitContext context,
            int assetCount,
            UnityEngine.Object sampleAsset,
            IDataToolkitAssetInspectorProvider customProvider,
            DataToolkitInspectorCoverageLevel coverageLevel)
        {
            if (assetCount == 0)
            {
                return "No asset was found in the active search roots.";
            }

            if (sampleAsset == null)
            {
                return "Asset paths were found, but no sample asset could be loaded for this type.";
            }

            if (customProvider != null)
            {
                return $"Custom provider: {customProvider.GetType().Name}";
            }

            if (context.Settings.SafeInspectorRules.Any(rule => rule.Matches(sampleAsset.GetType())))
            {
                return "Safe inspector rule matches this type.";
            }

            if (coverageLevel == DataToolkitInspectorCoverageLevel.SafePreview)
            {
                return $"Default inspector mode: {context.Settings.DefaultInspectorMode}";
            }

            if (coverageLevel == DataToolkitInspectorCoverageLevel.NativeInspectorFallback)
            {
                return "Default inspector mode: FullInspector; Unity native inspector will be used when no custom provider matched.";
            }

            return "No custom provider or safe preview rule matched; raw Odin fallback will be used.";
        }

        private static IDataToolkitAssetInspectorProvider FindCustomProvider(
            DataToolkitContext context,
            IReadOnlyList<IDataToolkitAssetInspectorProvider> providers,
            UnityEngine.Object sampleAsset)
        {
            foreach (var provider in providers)
            {
                try
                {
                    if (provider.CanInspect(context, sampleAsset))
                    {
                        return provider;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return null;
        }
    }
}
