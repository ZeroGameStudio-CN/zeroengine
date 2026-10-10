namespace ZGS.DataToolkit.Editor
{
    public readonly struct ConfigValidationIssue
    {
        public ConfigValidationIssue(
            ConfigValidationSeverity severity,
            string code,
            string message,
            string assetPath = "",
            string objectName = "")
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            ObjectName = objectName ?? string.Empty;
        }

        public ConfigValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string AssetPath { get; }
        public string ObjectName { get; }
    }
}
