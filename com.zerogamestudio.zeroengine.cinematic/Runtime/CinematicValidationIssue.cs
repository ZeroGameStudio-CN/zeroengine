namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicValidationIssue
    {
        public CinematicValidationIssue(
            string code,
            string contextId,
            string message,
            CinematicValidationSeverity severity = CinematicValidationSeverity.Error)
        {
            Code = code ?? string.Empty;
            ContextId = contextId ?? string.Empty;
            Message = message ?? string.Empty;
            Severity = severity;
        }

        public string Code { get; }

        public string ContextId { get; }

        public string Message { get; }

        public CinematicValidationSeverity Severity { get; }
    }
}
