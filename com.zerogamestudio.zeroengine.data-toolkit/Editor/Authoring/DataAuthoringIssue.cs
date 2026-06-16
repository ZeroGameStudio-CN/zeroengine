namespace ZGS.DataToolkit.Editor
{
    public enum DataAuthoringIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct DataAuthoringIssue
    {
        public DataAuthoringIssue(
            DataAuthoringIssueSeverity severity,
            string assetPath,
            string assetType,
            string stableId,
            string fieldPath,
            string message)
        {
            Severity = severity;
            AssetPath = assetPath ?? string.Empty;
            AssetType = assetType ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            Message = string.IsNullOrWhiteSpace(message) ? severity.ToString() : message;
        }

        public DataAuthoringIssueSeverity Severity { get; }
        public string AssetPath { get; }
        public string AssetType { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public static DataAuthoringIssue Info(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Info, assetPath, assetType, stableId, fieldPath, message);
        }

        public static DataAuthoringIssue Warning(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Warning, assetPath, assetType, stableId, fieldPath, message);
        }

        public static DataAuthoringIssue Error(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Error, assetPath, assetType, stableId, fieldPath, message);
        }
    }
}
