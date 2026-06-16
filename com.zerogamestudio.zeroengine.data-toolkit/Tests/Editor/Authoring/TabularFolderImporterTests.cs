using System.IO;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class TabularFolderImporterTests
    {
        [Test]
        public void ReadFolder_ParsesCsvQuotesCommasNewlinesAndUtf8()
        {
            var folder = Path.Combine("Temp", "TabularFolderImporterTestsCsv");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "Characters.csv"),
                "id,name,notes\nchar_1,\"张三, 少侠\",\"line1\nline2\"\n",
                System.Text.Encoding.UTF8);

            var workbook = TabularFolderImporter.ReadFolder(folder);
            var sheet = workbook.GetSheet("Characters");

            Assert.NotNull(sheet);
            Assert.AreEqual("张三, 少侠", sheet.Rows[0].GetCell("name"));
            Assert.AreEqual("line1\nline2", sheet.Rows[0].GetCell("notes"));
        }

        [Test]
        public void ReadFolder_BlocksMixedCsvAndTsv()
        {
            var folder = Path.Combine("Temp", "TabularFolderImporterTestsMixed");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "A.csv"), "id\n1\n");
            File.WriteAllText(Path.Combine(folder, "B.tsv"), "id\tname\n1\tA\n");

            var exception = Assert.Throws<System.InvalidOperationException>(() => TabularFolderImporter.ReadFolder(folder));
            StringAssert.Contains("mixed", exception.Message.ToLowerInvariant());
        }
    }
}
