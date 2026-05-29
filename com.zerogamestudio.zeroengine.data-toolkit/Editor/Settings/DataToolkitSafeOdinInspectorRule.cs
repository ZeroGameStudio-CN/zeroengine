using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitSafeOdinInspectorRule
    {
        public DataToolkitSafeOdinInspectorRule(
            Type assetType,
            IEnumerable<string> excludedPropertyPaths,
            string summary = null)
        {
            AssetType = assetType ?? throw new ArgumentNullException(nameof(assetType));
            ExcludedPropertyPaths = (excludedPropertyPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        }

        public Type AssetType { get; }
        public IReadOnlyList<string> ExcludedPropertyPaths { get; }
        public string Summary { get; }

        public bool Matches(Type assetType)
        {
            return assetType != null && AssetType.IsAssignableFrom(assetType);
        }
    }
}
