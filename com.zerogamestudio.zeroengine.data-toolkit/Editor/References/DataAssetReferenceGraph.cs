using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetReferenceGraph
    {
        public static readonly DataAssetReferenceGraph Empty = new(Array.Empty<DataAssetReferenceEdge>());

        public DataAssetReferenceGraph(IEnumerable<DataAssetReferenceEdge> edges)
        {
            Edges = (edges ?? Array.Empty<DataAssetReferenceEdge>())
                .Where(edge => edge != null)
                .OrderBy(edge => edge.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.FieldPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.TargetName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<DataAssetReferenceEdge> Edges { get; }

        public IReadOnlyList<DataAssetReferenceEdge> GetOutgoing(string assetPath)
        {
            assetPath = NormalizePath(assetPath);
            return Edges.Where(edge => string.Equals(edge.SourcePath, assetPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public IReadOnlyList<DataAssetReferenceEdge> GetIncoming(string assetPath)
        {
            assetPath = NormalizePath(assetPath);
            return Edges.Where(edge => string.Equals(edge.TargetPath, assetPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
