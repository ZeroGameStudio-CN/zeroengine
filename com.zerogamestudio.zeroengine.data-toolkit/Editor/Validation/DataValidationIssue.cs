using System;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataValidationIssue
    {
        public DataValidationIssue(
            DataValidationSeverity severity,
            string typeName,
            string assetName,
            string assetPath,
            string fieldPath,
            string message)
        {
            Severity = severity;
            TypeName = typeName ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            AssetPath = NormalizePath(assetPath);
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DataValidationSeverity Severity { get; }
        public string TypeName { get; }
        public string AssetName { get; }
        public string AssetPath { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public bool BelongsToPath(string assetPath)
        {
            return string.Equals(AssetPath, NormalizePath(assetPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
