namespace ZeroEngine.ModSystem
{
    public sealed class ModLoadIssue
    {
        public ModLoadIssue(ModIssueSeverity severity, string modId, string path, string message)
            : this(severity, string.Empty, modId, path, message)
        {
        }

        public ModLoadIssue(
            ModIssueSeverity severity,
            string reasonCode,
            string modId,
            string path,
            string message)
        {
            Severity = severity;
            ReasonCode = reasonCode ?? string.Empty;
            ModId = modId ?? string.Empty;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ModIssueSeverity Severity { get; }
        public string ReasonCode { get; }
        public string ModId { get; }
        public string Path { get; }
        public string Message { get; }
    }
}
