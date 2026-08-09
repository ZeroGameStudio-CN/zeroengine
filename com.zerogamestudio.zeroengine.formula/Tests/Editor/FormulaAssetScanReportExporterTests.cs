using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaAssetScanReportExporterTests
    {
        [Test]
        public void ToJson_ExportsIssueCountsAndMessages()
        {
            var report = new FormulaAssetScanReport { AssetCount = 2 };
            report.AddIssue(FormulaAssetScanSeverity.Warning, "Assets/Test.asset", "缺少目录信息");

            var json = FormulaAssetScanReportExporter.ToJson(report);

            StringAssert.Contains("\"assetCount\":2", json);
            StringAssert.Contains("\"warningCount\":1", json);
            StringAssert.Contains("\"severity\":\"Warning\"", json);
            StringAssert.Contains("缺少目录信息", json);
        }

        [Test]
        public void ToMarkdown_ExportsReadableSummary()
        {
            var report = new FormulaAssetScanReport { AssetCount = 2 };
            report.AddIssue(FormulaAssetScanSeverity.Error, "Assets/Test.asset", "provider 不存在");

            var markdown = FormulaAssetScanReportExporter.ToMarkdown(report);

            StringAssert.Contains("# Formula Scan Report", markdown);
            StringAssert.Contains("Assets: 2", markdown);
            StringAssert.Contains("Errors: 1", markdown);
            StringAssert.Contains("provider 不存在", markdown);
        }
    }
}
