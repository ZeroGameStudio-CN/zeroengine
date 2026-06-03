using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaPreviewReportExporterTests
    {
        [Test]
        public void ToJson_ExportsCaseResultSummary()
        {
            var report = CreateReport();

            var json = FormulaPreviewReportExporter.ToJson(report);

            StringAssert.Contains("\"caseId\":\"low\"", json);
            StringAssert.Contains("\"succeeded\":true", json);
            StringAssert.Contains("\"result\":10", json);
            StringAssert.Contains("\"diagnosticCount\":0", json);
            StringAssert.Contains("\"stepCount\":1", json);
        }

        [Test]
        public void ToMarkdown_ExportsCaseResultSummary()
        {
            var report = CreateReport();

            var markdown = FormulaPreviewReportExporter.ToMarkdown(report);

            StringAssert.Contains("# Formula Preview Report", markdown);
            StringAssert.Contains("low", markdown);
            StringAssert.Contains("10", markdown);
            StringAssert.Contains("Steps: 1", markdown);
        }

        private static FormulaPreviewBatchReport CreateReport()
        {
            var evaluationReport = new FormulaEvaluationReport(null, "测试公式");
            evaluationReport.SetResult(10f, true);
            evaluationReport.AddStep(new FormulaStepEvaluation(
                0,
                FormulaOperationType.Add,
                FormulaValueSourceType.Provider,
                "金币",
                0f,
                10f,
                10f));

            return new FormulaPreviewBatchReport(
                null,
                FormulaEditorProfile.CreateEmpty("test", "测试公式"),
                new[]
                {
                    new FormulaPreviewCaseResult(
                        new FormulaPreviewCase(
                            "low",
                            "低金币",
                            new FormulaPreviewValueSet(null),
                            string.Empty),
                        10f,
                        true,
                        evaluationReport),
                });
        }
    }
}
