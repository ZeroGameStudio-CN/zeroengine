using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitDiagnosticsReport
    {
        public DataToolkitDiagnosticsReport(string projectId, IEnumerable<DataToolkitTypeCoverageInfo> types)
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? "ZGS" : projectId.Trim();
            Types = (types ?? Array.Empty<DataToolkitTypeCoverageInfo>())
                .Where(type => type != null)
                .OrderBy(type => type.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ProjectId { get; }
        public IReadOnlyList<DataToolkitTypeCoverageInfo> Types { get; }
        public int TypeCount => Types.Count;
        public int AssetCount => Types.Sum(type => type.AssetCount);
        public int FirstClassCount => Count(DataToolkitInspectorCoverageLevel.FirstClass);
        public int SafePreviewCount => Count(DataToolkitInspectorCoverageLevel.SafePreview);
        public int NativeInspectorFallbackCount => Count(DataToolkitInspectorCoverageLevel.NativeInspectorFallback);
        public int RawOdinFallbackCount => Count(DataToolkitInspectorCoverageLevel.RawOdinFallback);
        public int NoAssetsCount => Count(DataToolkitInspectorCoverageLevel.NoAssets);
        public int UnsupportedCount => Count(DataToolkitInspectorCoverageLevel.Unsupported);

        private int Count(DataToolkitInspectorCoverageLevel level)
        {
            return Types.Count(type => type.CoverageLevel == level);
        }
    }
}
