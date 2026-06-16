using System.Collections.Generic;

namespace ZeroEngine.AbilitySystem.Editor
{
    public enum AbilityEditorIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct AbilityEditorValidationIssue
    {
        public AbilityEditorValidationIssue(string code, AbilityEditorIssueSeverity severity, string message)
        {
            Code = code;
            Severity = severity;
            Message = message;
        }

        public string Code { get; }
        public AbilityEditorIssueSeverity Severity { get; }
        public string Message { get; }
    }

    public static class AbilityEditorValidationUtility
    {
        public static IEnumerable<AbilityEditorValidationIssue> Validate(AbilityDefinition ability)
        {
            foreach (var issue in AbilityDefinitionValidator.Validate(ability))
            {
                yield return new AbilityEditorValidationIssue(
                    issue.Code,
                    ToEditorSeverity(issue.Severity),
                    issue.Message);
            }
        }

        private static AbilityEditorIssueSeverity ToEditorSeverity(AbilityValidationSeverity severity)
        {
            switch (severity)
            {
                case AbilityValidationSeverity.Info:
                    return AbilityEditorIssueSeverity.Info;
                case AbilityValidationSeverity.Warning:
                    return AbilityEditorIssueSeverity.Warning;
                default:
                    return AbilityEditorIssueSeverity.Error;
            }
        }
    }
}
