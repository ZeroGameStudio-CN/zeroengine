using System;

namespace ZeroGameStudio.ConfigPipeline
{
    public enum ConfigDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ConfigSourceLocation
    {
        public ConfigSourceLocation(
            string source,
            string sheet = null,
            int? row = null,
            int? column = null)
        {
            Source = source ?? string.Empty;
            Sheet = sheet;
            Row = row;
            Column = column;
        }

        public string Source { get; }

        public string Sheet { get; }

        public int? Row { get; }

        public int? Column { get; }
    }

    public sealed class ConfigDiagnostic
    {
        public ConfigDiagnostic(
            string code,
            ConfigDiagnosticSeverity severity,
            string message,
            string configSetId,
            string fieldPath,
            ConfigSourceLocation sourceLocation = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Diagnostic code is required.", nameof(code));
            }

            Code = code;
            Severity = severity;
            Message = message ?? string.Empty;
            ConfigSetId = configSetId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            SourceLocation = sourceLocation;
        }

        public string Code { get; }

        public ConfigDiagnosticSeverity Severity { get; }

        public string Message { get; }

        public string ConfigSetId { get; }

        public string FieldPath { get; }

        public ConfigSourceLocation SourceLocation { get; }
    }
}
