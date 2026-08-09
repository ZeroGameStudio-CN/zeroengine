using System.IO;
using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaScannerCliTests
    {
        [Test]
        public void EvaluateExitCode_ReturnsOneForErrors()
        {
            var report = new FormulaAssetScanReport();
            report.AddIssue(FormulaAssetScanSeverity.Error, "Assets/Test.asset", "bad");

            var result = FormulaScannerCli.EvaluateExitCode(
                report,
                new FormulaScannerCliOptions(string.Empty, string.Empty, string.Empty, false, false));

            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.ErrorCount, Is.EqualTo(1));
        }

        [Test]
        public void EvaluateExitCode_WarningsPassUnlessConfigured()
        {
            var report = new FormulaAssetScanReport();
            report.AddIssue(FormulaAssetScanSeverity.Warning, "Assets/Test.asset", "warn");

            var defaultResult = FormulaScannerCli.EvaluateExitCode(
                report,
                new FormulaScannerCliOptions(string.Empty, string.Empty, string.Empty, false, false));
            var strictResult = FormulaScannerCli.EvaluateExitCode(
                report,
                new FormulaScannerCliOptions(string.Empty, string.Empty, string.Empty, true, false));

            Assert.That(defaultResult.ExitCode, Is.EqualTo(0));
            Assert.That(strictResult.ExitCode, Is.EqualTo(2));
        }

        [Test]
        public void ParseArgs_ReadsFormulaFlags()
        {
            var options = FormulaScannerCli.ParseArgs(new[]
            {
                "-formulaProfile", "pob",
                "-formulaReportJson", "Temp/formula.json",
                "-formulaReportMarkdown", "Temp/formula.md",
                "-formulaFailOnWarning",
            });

            Assert.That(options.ProfileId, Is.EqualTo("pob"));
            Assert.That(options.JsonReportPath, Is.EqualTo("Temp/formula.json"));
            Assert.That(options.MarkdownReportPath, Is.EqualTo("Temp/formula.md"));
            Assert.That(options.FailOnWarning, Is.True);
        }

        [Test]
        public void WriteReports_CreatesJsonAndMarkdownFiles()
        {
            var directory = Path.Combine(Path.GetTempPath(), "formula-cli-tests");
            var jsonPath = Path.Combine(directory, "formula.json");
            var markdownPath = Path.Combine(directory, "formula.md");
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            var report = new FormulaAssetScanReport { AssetCount = 1 };

            FormulaScannerCli.WriteReports(
                report,
                new FormulaScannerCliOptions(string.Empty, jsonPath, markdownPath, false, false));

            Assert.That(File.Exists(jsonPath), Is.True);
            Assert.That(File.Exists(markdownPath), Is.True);
            StringAssert.Contains("\"assetCount\":1", File.ReadAllText(jsonPath));
            StringAssert.Contains("Assets: 1", File.ReadAllText(markdownPath));

            Directory.Delete(directory, true);
        }
    }
}
