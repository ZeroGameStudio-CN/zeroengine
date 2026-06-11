using System;

namespace ZeroEngine.ModSystem
{
    public sealed class ModContentImportResult
    {
        private static readonly ModLoadIssue[] EmptyIssues = Array.Empty<ModLoadIssue>();

        private ModContentImportResult(bool succeeded, ModLoadIssue[] issues)
        {
            Succeeded = succeeded;
            Issues = issues ?? EmptyIssues;
        }

        public bool Succeeded { get; }
        public ModLoadIssue[] Issues { get; }

        public static ModContentImportResult Success()
        {
            return new ModContentImportResult(true, EmptyIssues);
        }

        public static ModContentImportResult Failed(ModLoadIssue issue)
        {
            return new ModContentImportResult(false, issue == null ? EmptyIssues : new[] { issue });
        }

        public static ModContentImportResult Failed(ModLoadIssue[] issues)
        {
            return new ModContentImportResult(false, issues);
        }
    }
}
