using System;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitTypeCoverageInfo
    {
        public DataToolkitTypeCoverageInfo(
            Type type,
            int assetCount,
            string sampleAssetPath,
            DataToolkitInspectorCoverageLevel coverageLevel,
            string reason)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            AssetCount = Math.Max(0, assetCount);
            SampleAssetPath = string.IsNullOrWhiteSpace(sampleAssetPath) ? string.Empty : sampleAssetPath.Replace('\\', '/');
            CoverageLevel = coverageLevel;
            Reason = string.IsNullOrWhiteSpace(reason) ? coverageLevel.ToString() : reason.Trim();
        }

        public Type Type { get; }
        public string TypeName => Type.Name;
        public int AssetCount { get; }
        public string SampleAssetPath { get; }
        public DataToolkitInspectorCoverageLevel CoverageLevel { get; }
        public string Reason { get; }
    }
}
