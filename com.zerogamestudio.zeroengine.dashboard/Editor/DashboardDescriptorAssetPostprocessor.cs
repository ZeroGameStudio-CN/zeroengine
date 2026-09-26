using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace ZeroEngine.Editor.Dashboard
{
    internal sealed class DashboardDescriptorAssetPostprocessor : AssetPostprocessor
    {
        internal static event Action DescriptorsChanged;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsDescriptor(importedAssets) &&
                !ContainsDescriptor(deletedAssets) &&
                !ContainsDescriptor(movedAssets) &&
                !ContainsDescriptor(movedFromAssetPaths))
            {
                return;
            }

            DashboardCatalogSession.Invalidate();
            DescriptorsChanged?.Invoke();
        }

        internal static bool ContainsDescriptor(string[] paths)
        {
            return paths != null && paths.Any(path => string.Equals(
                Path.GetFileName(path),
                DashboardCatalogDiscovery.DescriptorFileName,
                StringComparison.Ordinal));
        }
    }
}
