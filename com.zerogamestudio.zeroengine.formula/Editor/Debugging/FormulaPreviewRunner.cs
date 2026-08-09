using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaPreviewCaseResult
    {
        public FormulaPreviewCaseResult(
            FormulaPreviewCase previewCase,
            float value,
            bool succeeded,
            FormulaEvaluationReport report)
        {
            Case = previewCase;
            Value = value;
            Succeeded = succeeded;
            Report = report;
        }

        public FormulaPreviewCase Case { get; }
        public float Value { get; }
        public bool Succeeded { get; }
        public FormulaEvaluationReport Report { get; }
    }

    public sealed class FormulaPreviewBatchReport
    {
        public FormulaPreviewBatchReport(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaPreviewCaseResult> results)
        {
            Formula = formula;
            Profile = profile;
            Results = results ?? Array.Empty<FormulaPreviewCaseResult>();
        }

        public FormulaAsset Formula { get; }
        public FormulaEditorProfile Profile { get; }
        public IReadOnlyList<FormulaPreviewCaseResult> Results { get; }
    }

    public static class FormulaPreviewRunner
    {
        public static FormulaPreviewBatchReport EvaluateCases(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            IEnumerable<FormulaPreviewCase> previewCases)
        {
            var results = new List<FormulaPreviewCaseResult>();
            if (previewCases != null)
            {
                foreach (var previewCase in previewCases)
                {
                    if (previewCase == null)
                        continue;

                    var context = FormulaEditorPreview.CreateContext(profile, previewCase.Values.ToDictionary());
                    var succeeded = FormulaEditorPreview.TryEvaluate(
                        formula,
                        profile,
                        context,
                        out var value,
                        out var report);
                    results.Add(new FormulaPreviewCaseResult(previewCase, value, succeeded, report));
                }
            }

            return new FormulaPreviewBatchReport(formula, profile, results.AsReadOnly());
        }

        public static FormulaPreviewCase CreateCaseFromSnapshot(
            string id,
            string displayName,
            FormulaRuntimeSnapshot snapshot)
        {
            var description = snapshot == null
                ? string.Empty
                : $"{snapshot.SourceLabel} {snapshot.CapturedAtUtc}".Trim();
            return new FormulaPreviewCase(
                id,
                displayName,
                snapshot?.Values,
                description);
        }
    }
}
