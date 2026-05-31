using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringImportServiceTests
    {
        [Test]
        public void BuildPreview_ReportsMissingRequiredColumns()
        {
            var workbook = new TabularImportWorkbook();
            workbook.AddSheet(new TabularImportSheet("Characters", new[] { "id" }, Array.Empty<TabularImportRow>()));
            var adapter = new TestImportAdapter("Test", "Characters", new[] { "id", "name" });

            var preview = DataAuthoringImportService.BuildPreview(workbook, new[] { adapter }, createMissingAssets: false);

            Assert.True(preview.HasBlockingErrors);
            Assert.That(preview.Issues.Any(issue =>
                issue.Severity == DataAuthoringIssueSeverity.Error &&
                issue.SheetName == "Characters" &&
                issue.ColumnName == "name"));
        }

        [Test]
        public void Apply_RefusesPreviewWithBlockingErrors()
        {
            var adapter = new TestImportAdapter("Test", "Characters", new[] { "id" });
            var preview = new TabularImportPreview();
            preview.AddIssue(TabularImportIssue.Error("Characters", 2, "id", "Assets/Test.asset", "test", "id", "broken"));

            Assert.Throws<InvalidOperationException>(() => DataAuthoringImportService.Apply(preview, new[] { adapter }));
            Assert.False(adapter.ApplyCalled);
        }

        [Test]
        public void Apply_CallsAdaptersThatContributedChanges()
        {
            var adapter = new TestImportAdapter("Test", "Characters", new[] { "id" });
            var preview = new TabularImportPreview();
            preview.AddChange(new TabularImportChange(
                "Test",
                TabularImportChangeKind.UpdateScalar,
                "Characters",
                2,
                "name",
                "Assets/Test.asset",
                "test",
                "characterName",
                "Old",
                "New"));

            DataAuthoringImportService.Apply(preview, new[] { adapter });

            Assert.True(adapter.ApplyCalled);
        }

        private sealed class TestImportAdapter : IDataAuthoringImportAdapter
        {
            private readonly string _sheetName;
            private readonly IReadOnlyList<string> _requiredColumns;

            public TestImportAdapter(string adapterId, string sheetName, IReadOnlyList<string> requiredColumns)
            {
                AdapterId = adapterId;
                _sheetName = sheetName;
                _requiredColumns = requiredColumns;
            }

            public string AdapterId { get; }
            public bool ApplyCalled { get; private set; }
            public IReadOnlyList<string> RequiredSheets => new[] { _sheetName };
            public IReadOnlyList<string> OptionalSheets => Array.Empty<string>();

            public IReadOnlyList<string> GetRequiredColumns(string sheetName)
            {
                return sheetName == _sheetName ? _requiredColumns : Array.Empty<string>();
            }

            public IReadOnlyList<string> GetKnownColumns(string sheetName)
            {
                return GetRequiredColumns(sheetName);
            }

            public TabularImportPreview Preview(TabularImportWorkbook workbook, bool createMissingAssets)
            {
                return new TabularImportPreview();
            }

            public void Apply(TabularImportPreview preview)
            {
                ApplyCalled = true;
            }
        }
    }
}
