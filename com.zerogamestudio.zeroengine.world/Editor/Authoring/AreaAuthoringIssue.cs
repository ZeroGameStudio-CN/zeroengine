namespace ZeroEngine.World.Authoring
{
    public readonly struct AreaAuthoringIssue
    {
        public AreaAuthoringIssue(
            AreaAuthoringIssueSeverity severity,
            string code,
            string message,
            string assetPath = null,
            string contextId = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            AssetPath = assetPath;
            ContextId = contextId;
        }

        public AreaAuthoringIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string AssetPath { get; }
        public string ContextId { get; }
        public bool IsError => Severity == AreaAuthoringIssueSeverity.Error;

        public override string ToString()
        {
            var text = $"{Code}: {Message}";
            if (!string.IsNullOrWhiteSpace(AssetPath))
            {
                text += $" [{AssetPath}]";
            }

            if (!string.IsNullOrWhiteSpace(ContextId))
            {
                text += $" ({ContextId})";
            }

            return text;
        }
    }
}
