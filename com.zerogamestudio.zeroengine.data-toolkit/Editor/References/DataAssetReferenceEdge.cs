namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetReferenceEdge
    {
        public DataAssetReferenceEdge(
            string sourcePath,
            string sourceName,
            string sourceType,
            string fieldPath,
            string targetPath,
            string targetName,
            string targetType)
        {
            SourcePath = sourcePath ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            SourceType = sourceType ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
            TargetName = targetName ?? string.Empty;
            TargetType = targetType ?? string.Empty;
        }

        public string SourcePath { get; }
        public string SourceName { get; }
        public string SourceType { get; }
        public string FieldPath { get; }
        public string TargetPath { get; }
        public string TargetName { get; }
        public string TargetType { get; }
    }
}
