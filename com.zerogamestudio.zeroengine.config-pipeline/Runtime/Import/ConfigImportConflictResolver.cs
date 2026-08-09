using System;

namespace ZeroGameStudio.ConfigPipeline
{
    public enum ConfigImportDecision
    {
        CandidateCurrentEqual,
        CandidateJsonOnly,
        CandidateUnbased,
        RejectStaleJson,
        RejectConflict
    }

    public sealed class ConfigImportConflictResult
    {
        internal ConfigImportConflictResult(ConfigImportDecision decision, string diagnosticCode)
        {
            Decision = decision;
            DiagnosticCode = diagnosticCode;
        }

        public ConfigImportDecision Decision { get; }

        public string DiagnosticCode { get; }

        public bool CanCreateCandidate =>
            Decision == ConfigImportDecision.CandidateCurrentEqual ||
            Decision == ConfigImportDecision.CandidateJsonOnly ||
            Decision == ConfigImportDecision.CandidateUnbased;
    }

    public static class ConfigImportConflictResolver
    {
        public static ConfigImportConflictResult Resolve(
            string baseSourceHash,
            string jsonSourceHash,
            string workbookCurrentHash)
        {
            RequireHash(jsonSourceHash, nameof(jsonSourceHash));
            RequireHash(workbookCurrentHash, nameof(workbookCurrentHash));
            if (string.IsNullOrEmpty(baseSourceHash))
            {
                return new ConfigImportConflictResult(
                    ConfigImportDecision.CandidateUnbased,
                    "CONFIG_IMPORT_UNBASED");
            }

            RequireHash(baseSourceHash, nameof(baseSourceHash));
            if (string.Equals(jsonSourceHash, workbookCurrentHash, StringComparison.Ordinal))
            {
                return new ConfigImportConflictResult(
                    ConfigImportDecision.CandidateCurrentEqual,
                    "CONFIG_IMPORT_CURRENT_EQUAL");
            }

            if (string.Equals(workbookCurrentHash, baseSourceHash, StringComparison.Ordinal))
            {
                return new ConfigImportConflictResult(
                    ConfigImportDecision.CandidateJsonOnly,
                    "CONFIG_IMPORT_JSON_ONLY");
            }

            if (string.Equals(jsonSourceHash, baseSourceHash, StringComparison.Ordinal))
            {
                return new ConfigImportConflictResult(
                    ConfigImportDecision.RejectStaleJson,
                    "CONFIG_IMPORT_STALE_JSON");
            }

            return new ConfigImportConflictResult(
                ConfigImportDecision.RejectConflict,
                "CONFIG_IMPORT_DIVERGED");
        }

        private static void RequireHash(string value, string parameterName)
        {
            if (value == null || value.Length != 64)
            {
                throw new ArgumentException("Expected a lowercase SHA-256 hash.", parameterName);
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException("Expected a lowercase SHA-256 hash.", parameterName);
                }
            }
        }
    }
}
