namespace ZeroEngine.TCE
{
    public readonly struct TceValidationIssue
    {
        public TceValidationIssue(TceValidationSeverity severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public TceValidationSeverity Severity { get; }
        public string Code { get; }
        public string Path { get; }
        public string Message { get; }
    }
}
