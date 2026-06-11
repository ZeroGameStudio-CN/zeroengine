namespace ZeroEngine.ModSystem
{
    public sealed class ModLoadIssue
    {
        public ModLoadIssue(ModIssueSeverity severity, string modId, string path, string message)
        {
            Severity = severity;
            ModId = modId ?? string.Empty;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ModIssueSeverity Severity { get; }
        public string ModId { get; }
        public string Path { get; }
        public string Message { get; }
    }
}
