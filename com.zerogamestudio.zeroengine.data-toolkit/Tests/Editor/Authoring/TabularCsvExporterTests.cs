using System.IO;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class TabularCsvExporterTests
    {
        [Test]
        public void WriteCsvFolder_WritesEscapedUtf8Sheet()
        {
            var workbook = new TabularWorkbook();
            var sheet = workbook.GetOrCreateSheet("Characters");
            sheet.SetColumns("id", "name", "description");
            sheet.AddRow("char_001", "李剑心", "line one, \"quoted\"\nline two");

            var folder = Path.Combine("Temp", "DataToolkitCsvExportTests");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            TabularCsvExporter.WriteCsvFolder(workbook, folder);

            var csv = File.ReadAllText(Path.Combine(folder, "Characters.csv"));
            StringAssert.Contains("id,name,description", csv);
            StringAssert.Contains("char_001", csv);
            StringAssert.Contains("李剑心", csv);
            StringAssert.Contains("\"line one, \"\"quoted\"\"", csv);
        }

        [Test]
        public void WriteTsvFolder_WritesTabSeparatedSheet()
        {
            var workbook = new TabularWorkbook();
            var sheet = workbook.GetOrCreateSheet("AIProfiles");
            sheet.SetColumns("id", "name");
            sheet.AddRow("ai_bandit", "山贼 AI");

            var folder = Path.Combine("Temp", "DataToolkitTsvExportTests");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            TabularCsvExporter.WriteTsvFolder(workbook, folder);

            var tsv = File.ReadAllText(Path.Combine(folder, "AIProfiles.tsv"));
            StringAssert.Contains("id\tname", tsv);
            StringAssert.Contains("ai_bandit\t山贼 AI", tsv);
        }
    }
}
