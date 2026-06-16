namespace ZeroEngine.Dialog
{
    public enum DialogGraphValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct DialogGraphValidationIssue
    {
        public DialogGraphValidationIssue(
            DialogGraphValidationSeverity severity,
            string code,
            string graphId = null,
            string nodeId = null,
            string targetNodeId = null,
            string commandId = null,
            string message = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            GraphId = graphId ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            TargetNodeId = targetNodeId ?? string.Empty;
            CommandId = commandId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DialogGraphValidationSeverity Severity { get; }
        public string Code { get; }
        public string GraphId { get; }
        public string NodeId { get; }
        public string TargetNodeId { get; }
        public string CommandId { get; }
        public string Message { get; }
    }

    public static class DialogGraphValidationCodes
    {
        public const string MissingStartNode = "dialog.missing_start";
        public const string MissingEndNode = "dialog.missing_end";
        public const string DuplicateNodeId = "dialog.duplicate_node_id";
        public const string BrokenOutputConnection = "dialog.broken_output";
        public const string UnknownCommandId = "dialog.unknown_command";
        public const string UnknownLocalizationKey = "dialog.unknown_localization_key";
    }
}
