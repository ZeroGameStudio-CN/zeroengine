using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests.Editor
{
    [TestFixture]
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class ProjectPipelineTests
    {
        private string root;
        private ConfigSchema schema;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "zgs-config-project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            byte[] schemaBytes = Utf8(
                "{\"$id\":\"urn:zgs:test:project\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"items\",\"groups\"],\"properties\":{" +
                "\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"value\"]," +
                "\"properties\":{\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
                "\"groupId\":{\"type\":\"string\",\"x-zgs-ref\":\"#/properties/groups/items/properties/id\"}," +
                "\"clientValue\":{\"type\":\"string\",\"x-zgs-scope\":\"client\"}," +
                "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}}}}," +
                "\"groups\":{\"type\":\"array\",\"x-zgs-sheet\":\"Groups\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\"]," +
                "\"properties\":{\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}}}");
            File.WriteAllBytes(Path.Combine(root, "Config", "schema.json"), schemaBytes);
            schema = ConfigSchemaParser.Parse(schemaBytes);
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 7);
            WriteWorkbook("Config/groups.xlsx", "groups", "group-a", null);
            File.WriteAllBytes(Path.Combine(root, "Config", "config-project.json"), Utf8(ProfileJson()));
        }

        [TearDown]
        public void TearDown()
        {
            ConfigMaintenanceRegistry.ClearForTests();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Profile_RejectsDuplicateTableOwners()
        {
            string json = ProfileJson().Replace(
                "[\"groups\"]",
                "[\"items\"]");

            Assert.Throws<InvalidDataException>(() => ConfigProjectProfileParser.Parse(Utf8(json)));
        }

        [Test]
        public void Profile_ParsesAuthoringSheetGroups()
        {
            string json = ProfileJson().Replace(
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]}",
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]," +
                "\"authoringSheets\":[{\"name\":\"Item\",\"tables\":[\"items\"]}]}");

            ConfigProjectProfile profile = ConfigProjectProfileParser.Parse(Utf8(json));
            ConfigSetProfile set = profile.GetConfigSet("sample");
            ConfigWorkbookProfile workbook = set.Workbooks[0];
            Assert.That(set.AuthoringWorkbookFormat, Is.EqualTo("xlsx"));
            Assert.That(set.UsesMacroEnabledWorkbooks, Is.False);
            Assert.That(workbook.AuthoringSheets, Has.Count.EqualTo(1));
            Assert.That(workbook.AuthoringSheets[0].Name, Is.EqualTo("Item"));
            Assert.That(workbook.AuthoringSheets[0].Tables, Is.EqualTo(new[] { "items" }));
        }

        [Test]
        public void Profile_ParsesXlsmAuthoringFormatAndRequiresMatchingPaths()
        {
            string json = ProfileJson()
                .Replace(
                    "\"authoringSource\":\"excel\",",
                    "\"authoringSource\":\"excel\",\"authoringWorkbookFormat\":\"xlsm\"," +
                    "\"authoringOperationsVersion\":1,")
                .Replace(".xlsx", ".xlsm")
                .Replace(
                    "{\"path\":\"Config/items.xlsm\",\"tables\":[\"items\"]}",
                    "{\"path\":\"Config/items.xlsm\",\"tables\":[\"items\"]," +
                    "\"protectedRecordIds\":{\"items\":[\"starter-item\"]}}");

            ConfigSetProfile set = ConfigProjectProfileParser.Parse(Utf8(json))
                .GetConfigSet("sample");
            Assert.That(set.AuthoringWorkbookFormat, Is.EqualTo("xlsm"));
            Assert.That(set.UsesMacroEnabledWorkbooks, Is.True);
            Assert.That(set.AuthoringOperationsVersion, Is.EqualTo(1));
            Assert.That(set.UsesAuthoringOperations, Is.True);
            Assert.That(set.Workbooks.All(value => value.Path.EndsWith(".xlsm")), Is.True);

            Assert.Throws<InvalidDataException>(() =>
                ConfigProjectProfileParser.Parse(Utf8(json.Replace(
                    "Config/items.xlsm",
                    "Config/items.xlsx"))));

            File.WriteAllBytes(
                Path.Combine(root, "Config", "config-project-xlsm.json"),
                Utf8(json));
            string templates = Path.Combine(root, "XlsmTemplates");
            new ConfigPipelineService().WriteTemplates(
                root,
                "Config/config-project-xlsm.json",
                "sample",
                templates);
            string itemTemplate = Path.Combine(templates, "items.xlsm");
            Assert.That(File.Exists(itemTemplate), Is.True);
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(itemTemplate, false))
            {
                Assert.That(
                    workbook.DocumentType,
                    Is.EqualTo(SpreadsheetDocumentType.MacroEnabledWorkbook));
                Assert.That(workbook.WorkbookPart.VbaProjectPart, Is.Null);
                WorksheetPart items = GetWorksheetPart(workbook, "Items");
                Row[] rows = items.Worksheet.GetFirstChild<SheetData>().Elements<Row>().ToArray();
                Assert.That(rows[0].RowIndex.Value, Is.EqualTo(1U));
                Cell[] actionCells = rows[0].Elements<Cell>().Take(6).ToArray();
                Assert.That(
                    actionCells.Select(value => value.InnerText),
                    Is.EqualTo(new[] { "新增", "复制", "安全删除", "编辑关系", "技术区", "帮助" }));
                Assert.That(actionCells.All(value => value.CellFormula == null), Is.True);
                Assert.That(
                    actionCells.All(value => value.DataType?.Value == CellValues.InlineString),
                    Is.True);
                Assert.That(rows[1].Hidden.Value, Is.True);
                Assert.That(rows[2].RowIndex.Value, Is.EqualTo(3U));
                Pane pane = items.Worksheet.GetFirstChild<SheetViews>()
                    .Elements<SheetView>().Single().GetFirstChild<Pane>();
                Assert.That(pane.VerticalSplit.Value, Is.EqualTo(3D));
                Assert.That(pane.TopLeftCell.Value, Is.EqualTo("A4"));
                Assert.That(items.Worksheet.GetFirstChild<SheetProtection>(), Is.Not.Null);
                DefinedName[] names = workbook.WorkbookPart.Workbook
                    .GetFirstChild<DefinedNames>().Elements<DefinedName>().ToArray();
                Assert.That(names.Count(value => value.Name.Value.StartsWith("ZGS_ACTION_Items_")),
                    Is.EqualTo(6));
                Assert.That(
                    names.Single(value => value.Name.Value == "ZGS_META_VERSION").Text,
                    Is.EqualTo("\"1\""));
                Assert.That(names.Any(value => value.Name.Value == "ZGS_META_TABLE_1"), Is.True);
                Assert.That(names.Any(value => value.Name.Value == "ZGS_META_PROTECTED_1_1"),
                    Is.True);
            }
        }

        [Test]
        public void Profile_ParsesProtectedRecordIdsAndRejectsForeignTables()
        {
            string json = ProfileJson().Replace(
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]}",
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]," +
                "\"protectedRecordIds\":{\"items\":[\"starter-item\"]}}");

            ConfigWorkbookProfile workbook = ConfigProjectProfileParser.Parse(Utf8(json))
                .GetConfigSet("sample").Workbooks[0];
            Assert.That(workbook.ProtectedRecordIds["items"], Is.EqualTo(new[] { "starter-item" }));
            Assert.Throws<InvalidDataException>(() =>
                ConfigProjectProfileParser.Parse(Utf8(json.Replace(
                    "\"items\":[\"starter-item\"]",
                    "\"groups\":[\"starter-item\"]"))));
        }

        [Test]
        public void Profile_RejectsAuthoringSheetThatDoesNotCoverWorkbookTables()
        {
            string json = ProfileJson().Replace(
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]}",
                "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]," +
                "\"authoringSheets\":[{\"name\":\"Item\",\"tables\":[\"groups\"]}]}");

            Assert.Throws<InvalidDataException>(() => ConfigProjectProfileParser.Parse(Utf8(json)));
        }

        [Test]
        public void Plan_RejectsSchemaRootThatIsNotRegisteredToAWorkbook()
        {
            string json = ProfileJson().Replace(
                ",{\"path\":\"Config/groups.xlsx\",\"tables\":[\"groups\"]}",
                string.Empty);
            File.WriteAllBytes(
                Path.Combine(root, "Config", "config-project.json"),
                Utf8(json));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));
            Assert.That(exception.Message, Does.Contain("CONFIG_WORKBOOK_OWNERSHIP_INCOMPLETE"));
            Assert.That(exception.Message, Does.Contain("groups"));
        }

        [Test]
        public void Plan_MissingRequiredFieldUsesRecordRowWithoutSiblingColumn()
        {
            WriteWorkbook("Config/items.xlsx", "items", "item-a", null);

            ConfigPipelineValidationException exception =
                Assert.Throws<ConfigPipelineValidationException>(() =>
                    new ConfigPipelineService().Plan(
                        root,
                        "Config/config-project.json",
                        "sample",
                        "package@1"));
            ConfigDiagnostic diagnostic = exception.Diagnostics.Single(value =>
                value.Code == "CONFIG_REQUIRED_MISSING" &&
                value.FieldPath == "$/items/0/value");

            Assert.That(diagnostic.SourceLocation, Is.Not.Null);
            Assert.That(diagnostic.SourceLocation.Source, Is.EqualTo("Config/items.xlsx"));
            Assert.That(diagnostic.SourceLocation.Sheet, Is.EqualTo("Items"));
            Assert.That(diagnostic.SourceLocation.Row, Is.EqualTo(3));
            Assert.That(diagnostic.SourceLocation.Column, Is.Null);
        }

        [Test]
        public void Plan_DanglingReferenceUsesExactWorkbookCell()
        {
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 7, "group-missing");

            ConfigPipelineValidationException exception =
                Assert.Throws<ConfigPipelineValidationException>(() =>
                    new ConfigPipelineService().Plan(
                        root,
                        "Config/config-project.json",
                        "sample",
                        "package@1"));
            ConfigDiagnostic diagnostic = exception.Diagnostics.Single(value =>
                value.Code == "CONFIG_REFERENCE_DANGLING" &&
                value.FieldPath == "$/items/0/groupId");

            Assert.That(diagnostic.SourceLocation, Is.Not.Null);
            Assert.That(diagnostic.SourceLocation.Source, Is.EqualTo("Config/items.xlsx"));
            Assert.That(diagnostic.SourceLocation.Sheet, Is.EqualTo("Items"));
            Assert.That(diagnostic.SourceLocation.Row, Is.EqualTo(3));
            Assert.That(diagnostic.SourceLocation.Column, Is.EqualTo(3));
        }

        [Test]
        public void Plan_FormulaCellUsesExactWorkbookCell()
        {
            string workbookPath = Path.Combine(root, "Config", "items.xlsx");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                Sheet sheet = workbook.WorkbookPart.Workbook.Sheets
                    .Elements<Sheet>()
                    .Single(value => value.Name.Value == "Items");
                WorksheetPart worksheetPart =
                    (WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id.Value);
                Cell valueCell = worksheetPart.Worksheet
                    .Descendants<Cell>()
                    .Single(value => value.CellReference?.Value == "B3");
                valueCell.CellFormula = new CellFormula("1+1");
                valueCell.CellValue = new CellValue("8");
                worksheetPart.Worksheet.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));

            Assert.That(exception.Code, Is.EqualTo("XLSX_FORMULA_FORBIDDEN"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
            Assert.That(exception.Row, Is.EqualTo(3));
            Assert.That(exception.Column, Is.EqualTo(2));
        }

        [Test]
        public void Plan_FormulaOutsideTableUsesExactWorkbookCell()
        {
            string workbookPath = Path.Combine(root, "Config", "items.xlsx");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                Sheet sheet = workbook.WorkbookPart.Workbook.Sheets
                    .Elements<Sheet>()
                    .Single(value => value.Name.Value == "Items");
                WorksheetPart worksheetPart =
                    (WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id.Value);
                SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                var row = new Row { RowIndex = 10U };
                row.Append(new Cell
                {
                    CellReference = "Z10",
                    CellFormula = new CellFormula("1+1"),
                    CellValue = new CellValue("2")
                });
                sheetData.Append(row);
                worksheetPart.Worksheet.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));

            Assert.That(exception.Code, Is.EqualTo("XLSX_FORMULA_FORBIDDEN"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
            Assert.That(exception.Row, Is.EqualTo(10));
            Assert.That(exception.Column, Is.EqualTo(26));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Plan_CellReferenceRowMustMatchParentRow(bool formula)
        {
            string workbookPath = Path.Combine(root, "Config", "items.xlsx");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                WorksheetPart worksheetPart = workbook.WorkbookPart.WorksheetParts.Single(value =>
                    workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(sheet =>
                        sheet.Id.Value == workbook.WorkbookPart.GetIdOfPart(value)).Name.Value ==
                    "Items");
                var spoofedRow = new Row { RowIndex = 100U };
                var valueCell = new Cell
                {
                    CellReference = "B3",
                    CellValue = new CellValue("8")
                };
                if (formula)
                {
                    valueCell.CellFormula = new CellFormula("1+1");
                }

                spoofedRow.Append(valueCell);
                worksheetPart.Worksheet.GetFirstChild<SheetData>().Append(spoofedRow);
                worksheetPart.Worksheet.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));
            Assert.That(exception.Code, Is.EqualTo("XLSX_CELL_ROW_MISMATCH"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
            Assert.That(exception.Row, Is.EqualTo(100));
            Assert.That(exception.Column, Is.EqualTo(2));
        }

        [Test]
        public void Plan_RowIndexIsRequired()
        {
            string workbookPath = Path.Combine(root, "Config", "items.xlsx");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                WorksheetPart worksheetPart = workbook.WorkbookPart.WorksheetParts.Single(value =>
                    workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(sheet =>
                        sheet.Id.Value == workbook.WorkbookPart.GetIdOfPart(value)).Name.Value ==
                    "Items");
                worksheetPart.Worksheet.Descendants<Row>()
                    .Single(value => value.RowIndex?.Value == 3U)
                    .RowIndex = null;
                worksheetPart.Worksheet.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));
            Assert.That(exception.Code, Is.EqualTo("XLSX_ROW_INDEX_MISSING"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
        }

        [Test]
        public void Plan_ZeroTableIdFailsClosed()
        {
            string workbookPath = Path.Combine(root, "Config", "items.xlsx");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                Table table = workbook.WorkbookPart.WorksheetParts
                    .SelectMany(sheet => sheet.TableDefinitionParts)
                    .Single()
                    .Table;
                table.Id = 0U;
                table.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));
            Assert.That(exception.Code, Is.EqualTo("XLSX_TABLE_ID_INVALID"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
        }

        [Test]
        public void Plan_XlsmConnectionsPartIsForbidden()
        {
            UseMacroEnabledAuthoringWorkbooks();
            string workbookPath = Path.Combine(root, "Config", "items.xlsm");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                ConnectionsPart connectionsPart =
                    workbook.WorkbookPart.AddNewPart<ConnectionsPart>();
                connectionsPart.Connections = new Connections();
                connectionsPart.Connections.Save();
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));

            Assert.That(exception.Code, Is.EqualTo("XLSX_UNSAFE_PART_FORBIDDEN"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsm"));
        }

        [Test]
        public void Plan_XlsmEmbeddedOlePartIsForbidden()
        {
            UseMacroEnabledAuthoringWorkbooks();
            string workbookPath = Path.Combine(root, "Config", "items.xlsm");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            {
                Sheet sheet = workbook.WorkbookPart.Workbook.Sheets
                    .Elements<Sheet>()
                    .Single(value => value.Name.Value == "Items");
                WorksheetPart worksheetPart =
                    (WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id.Value);
                EmbeddedObjectPart embeddedObject = worksheetPart.AddEmbeddedObjectPart(
                    EmbeddedObjectPartType.Binary,
                    "rIdUnsafeOle");
                using (Stream stream = embeddedObject.GetStream(FileMode.Create, FileAccess.Write))
                {
                    stream.WriteByte(0);
                }
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));

            Assert.That(exception.Code, Is.EqualTo("XLSX_UNSAFE_PART_FORBIDDEN"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/items.xlsm"));
        }

        [Test]
        public void Plan_SecondWorkbookInvalidVbaReportsItsWorkbookName()
        {
            UseMacroEnabledAuthoringWorkbooks();
            string workbookPath = Path.Combine(root, "Config", "groups.xlsm");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookPath, true))
            using (Stream content = workbook.WorkbookPart
                       .AddNewPart<VbaProjectPart>()
                       .GetStream(FileMode.Create, FileAccess.Write))
            {
                content.WriteByte(0);
            }

            XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                new ConfigPipelineService().Plan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"));
            Assert.That(exception.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));
            Assert.That(exception.Workbook, Is.EqualTo("Config/groups.xlsm"));
        }

        [TestCase("/items/0/value")]
        [TestCase("$.items[0].value")]
        [TestCase("$['items'][0]['value']")]
        public void Plan_ExactFieldPathSyntaxesUseExactWorkbookCell(string fieldPath)
        {
            ConfigMaintenanceRegistry.RegisterValidator(
                "sample",
                new FixedDiagnosticValidator(fieldPath));

            ConfigPipelineValidationException exception =
                Assert.Throws<ConfigPipelineValidationException>(() =>
                    new ConfigPipelineService().Plan(
                        root,
                        "Config/config-project.json",
                        "sample",
                        "package@1"));
            ConfigDiagnostic diagnostic = exception.Diagnostics.Single(value =>
                value.Code == "CONFIG_TEST_LOCATION");

            Assert.That(diagnostic.SourceLocation, Is.Not.Null);
            Assert.That(diagnostic.SourceLocation.Source, Is.EqualTo("Config/items.xlsx"));
            Assert.That(diagnostic.SourceLocation.Sheet, Is.EqualTo("Items"));
            Assert.That(diagnostic.SourceLocation.Row, Is.EqualTo(3));
            Assert.That(diagnostic.SourceLocation.Column, Is.EqualTo(2));
        }

        [Test]
        public void Plan_ArrayIndexMismatchDoesNotBorrowAnotherRecordLocation()
        {
            ConfigMaintenanceRegistry.RegisterValidator(
                "sample",
                new FixedDiagnosticValidator("$/items/99/value"));

            ConfigPipelineValidationException exception =
                Assert.Throws<ConfigPipelineValidationException>(() =>
                    new ConfigPipelineService().Plan(
                        root,
                        "Config/config-project.json",
                        "sample",
                        "package@1"));
            ConfigDiagnostic diagnostic = exception.Diagnostics.Single(value =>
                value.Code == "CONFIG_TEST_LOCATION");

            Assert.That(diagnostic.SourceLocation, Is.Null);
        }

        [Test]
        public void Plan_PreservesExistingDiagnosticSourceLocation()
        {
            var original = new ConfigSourceLocation("original.json", "SheetA", 9, 4);
            ConfigMaintenanceRegistry.RegisterValidator(
                "sample",
                new FixedDiagnosticValidator("$/items/0/value", original));

            ConfigPipelineValidationException exception =
                Assert.Throws<ConfigPipelineValidationException>(() =>
                    new ConfigPipelineService().Plan(
                        root,
                        "Config/config-project.json",
                        "sample",
                        "package@1"));
            ConfigDiagnostic diagnostic = exception.Diagnostics.Single(value =>
                value.Code == "CONFIG_TEST_LOCATION");

            Assert.That(diagnostic.SourceLocation, Is.SameAs(original));
        }

        [Test]
        public void PlanApplyCheck_MergesOwnedWorkbooksAndIsDeterministic()
        {
            var service = new ConfigPipelineService();

            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");
            Assert.That(plan.Plan.IsCurrent, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Generated", "sample.json")), Is.False);

            service.Apply(root, "Config/config-project.json", "sample", "package@1");

            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@1"), Is.True);
            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@2"), Is.False);
            string json = File.ReadAllText(Path.Combine(root, "Generated", "sample.json"));
            Assert.That(json, Does.Contain("\"item-a\""));
            Assert.That(json, Does.Contain("\"group-a\""));
        }

        [Test]
        public void ExpectedPlanApply_AppliesExactBatchPreview()
        {
            ConfigPipelineCommandResult preview = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                ConfigPipelineMode.Plan);

            Assert.That(preview.PlanId, Is.Not.Empty);
            Assert.That(
                Encoding.UTF8.GetString(preview.MachineJson),
                Does.Contain("\"planId\": \"" + preview.PlanId + "\""));

            ConfigPipelineCommandResult applied = ConfigPipelineBatch.ApplyExpectedPlan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                preview.PlanId);

            Assert.That(applied.Current, Is.True);
            Assert.That(applied.PlanId, Is.EqualTo(preview.PlanId));
            Assert.That(
                new ConfigPipelineService().Check(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1"),
                Is.True);
        }

        [Test]
        public void ExpectedPlanApply_RejectsChangedWorkbookWithoutWriting()
        {
            var service = new ConfigPipelineService();
            ConfigPipelinePreparedPlan preview = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 9);

            ConfigPlanStaleException exception = Assert.Throws<ConfigPlanStaleException>(() =>
                service.ApplyExpectedPlan(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "package@1",
                    preview.Plan.PlanId));

            Assert.That(exception.Message, Is.EqualTo("CONFIG_PLAN_CHANGED_REPLAN_REQUIRED"));
            Assert.That(File.Exists(Path.Combine(root, "Generated", "sample.json")), Is.False);
        }

        [Test]
        public void ExportJsonCandidate_NeverOverwritesOfficialWorkbooks()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string officialHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx"));
            string candidates = Path.Combine(root, "Candidates");

            ConfigImportConflictResult result = service.ExportJsonCandidate(
                root,
                "Config/config-project.json",
                "sample",
                "client",
                candidates);

            Assert.That(result.Decision, Is.EqualTo(ConfigImportDecision.CandidateCurrentEqual));
            Assert.That(File.Exists(Path.Combine(candidates, "items.candidate.xlsx")), Is.True);
            Assert.That(File.Exists(Path.Combine(candidates, "groups.candidate.xlsx")), Is.True);
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(officialHash));
        }

        [Test]
        public void ExportJsonCandidate_RejectsExistingOutputWithoutChangingIt()
        {
            WriteUnbasedJson(DefaultRuntimeRoot(1));
            string output = Path.Combine(root, "ExistingJsonCandidates");
            Directory.CreateDirectory(output);
            string marker = Path.Combine(output, "keep.txt");
            File.WriteAllText(marker, "keep", new UTF8Encoding(false));

            Assert.Throws<InvalidOperationException>(() =>
                new ConfigPipelineService().ExportJsonCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "client",
                    output));

            Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));
            Assert.That(Directory.GetFiles(output), Is.EqualTo(new[] { marker }));
        }

        [Test]
        public void ExportJsonCandidate_SecondWorkbookFailureLeavesNoPublishedSet()
        {
            WriteUnbasedJson(DefaultRuntimeRoot(2));
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(
                       Path.Combine(root, "Config", "groups.xlsx"),
                       true))
            {
                WorksheetPart sheet = workbook.WorkbookPart.WorksheetParts.Single(value =>
                    value.TableDefinitionParts.Any());
                var mergeCells = new MergeCells(
                    new MergeCell { Reference = "A4:B4" });
                sheet.Worksheet.InsertAfter(
                    mergeCells,
                    sheet.Worksheet.GetFirstChild<SheetData>());
                sheet.Worksheet.Save();
            }

            string output = Path.Combine(root, "AtomicJsonCandidates");
            InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                new ConfigPipelineService().ExportJsonCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "client",
                    output));

            Assert.That(
                failure.Message,
                Does.StartWith("CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT:"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".AtomicJsonCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void ExportJsonCandidate_DuplicateCandidateNamesLeaveNoPublishedSet()
        {
            WriteUnbasedJson(DefaultRuntimeRoot(1));
            string duplicateDirectory = Path.Combine(root, "Config", "duplicate");
            Directory.CreateDirectory(duplicateDirectory);
            File.Copy(
                Path.Combine(root, "Config", "groups.xlsx"),
                Path.Combine(duplicateDirectory, "items.xlsx"));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                ProfileJson().Replace(
                    "Config/groups.xlsx",
                    "Config/duplicate/items.xlsx"),
                new UTF8Encoding(false));
            string output = Path.Combine(root, "DuplicateJsonCandidates");

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                new ConfigPipelineService().ExportJsonCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "client",
                    output));

            Assert.That(failure.Message, Does.Contain("names must be unique"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".DuplicateJsonCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void ExportJsonCandidate_PreservesAuthoringOnlyFieldsAndIndexedChildIdentity()
        {
            UseAuthoringOnlyCandidateFixture(2);
            string output = Path.Combine(root, "AuthoringOnlyCandidates");

            new ConfigPipelineService().ExportJsonCandidate(
                root,
                "Config/config-project.json",
                "sample",
                "client",
                output);

            using (FileStream stream = File.OpenRead(Path.Combine(
                       output,
                       "items.candidate.xlsx")))
            {
                ConfigDocument candidate = new XlsxConfigSourceReader(
                        schema,
                        null,
                        new[] { "items" })
                    .Read(
                        stream,
                        new ConfigReadContext(
                            "sample",
                            schema.SchemaId,
                            schema.SchemaVersion));
                string json = CanonicalJsonWriter.WriteText(candidate.Root);
                Assert.That(json, Does.Contain("\"authoringName\": \"策划物品\""));
                Assert.That(json, Does.Contain("\"id\": \"child-a\""));
                Assert.That(json, Does.Contain("\"order\": 1"));
                Assert.That(json, Does.Contain("\"value\": 100"));
                Assert.That(json, Does.Contain("\"id\": \"child-b\""));
                Assert.That(json, Does.Contain("\"order\": 2"));
                Assert.That(json, Does.Contain("\"value\": 101"));
            }
        }

        [TestCase(1)]
        [TestCase(3)]
        public void ExportJsonCandidate_AuthoringOnlyChildLengthChangeFailsClosed(
            int projectedChildCount)
        {
            UseAuthoringOnlyCandidateFixture(projectedChildCount);
            string output = Path.Combine(root, "AmbiguousJsonCandidates");

            InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                new ConfigPipelineService().ExportJsonCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    "client",
                    output));

            Assert.That(
                failure.Message,
                Does.StartWith("CONFIG_JSON_CANDIDATE_AUTHORING_IDENTITY_AMBIGUOUS:"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".AmbiguousJsonCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void RefreshCandidate_PreservesAllWorkbookDataAndOfficialFiles()
        {
            var service = new ConfigPipelineService();
            string itemsHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx"));
            string groupsHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "groups.xlsx"));
            string candidates = Path.Combine(root, "RefreshCandidates");

            ConfigWorkbookRefreshCandidateResult result = service.ExportWorkbookRefreshCandidate(
                root,
                "Config/config-project.json",
                "sample",
                candidates);

            Assert.That(result.CandidateFileCount, Is.EqualTo(2));
            Assert.That(result.SourceHash, Is.Not.Empty);
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(itemsHash));
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "groups.xlsx")),
                Is.EqualTo(groupsHash));
            foreach (string name in new[] { "items.candidate.xlsx", "groups.candidate.xlsx" })
            {
                string path = Path.Combine(candidates, name);
                Assert.That(File.Exists(path), Is.True);
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(path, false))
                {
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                    Assert.That(
                        workbook.WorkbookPart.Workbook.Sheets
                            .Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                            .Any(value => value.Name.Value == XlsxConfigWorkbookWriter.NavigationSheetName),
                        Is.True);
                }
            }

            ConfigPipelineCommandResult batch = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                null,
                ConfigPipelineMode.RefreshCandidate,
                Path.Combine(root, "BatchRefreshCandidates"));
            Assert.That(batch.Success, Is.True);
            Assert.That(batch.Summary, Does.Contain(result.SourceHash));
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.RefreshCandidate),
                Is.False);
        }

        [Test]
        public void RefreshCandidate_RejectsNonEmptyOutputWithoutChangingIt()
        {
            string candidates = Path.Combine(root, "ExistingCandidates");
            Directory.CreateDirectory(candidates);
            string marker = Path.Combine(candidates, "keep.txt");
            File.WriteAllText(marker, "keep", new UTF8Encoding(false));

            Assert.Throws<InvalidOperationException>(() =>
                new ConfigPipelineService().ExportWorkbookRefreshCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    candidates));

            Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));
            Assert.That(Directory.GetFiles(candidates), Is.EqualTo(new[] { marker }));
        }

        [Test]
        public void RefreshCandidate_FailureAfterStagingLeavesNoPublishedSet()
        {
            string duplicateDirectory = Path.Combine(root, "Config", "duplicate");
            Directory.CreateDirectory(duplicateDirectory);
            File.Copy(
                Path.Combine(root, "Config", "groups.xlsx"),
                Path.Combine(duplicateDirectory, "items.xlsx"));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                ProfileJson().Replace("Config/groups.xlsx", "Config/duplicate/items.xlsx"),
                new UTF8Encoding(false));
            string candidates = Path.Combine(root, "AtomicCandidates");

            Assert.Throws<InvalidOperationException>(() =>
                new ConfigPipelineService().ExportWorkbookRefreshCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    candidates));

            Assert.That(Directory.Exists(candidates), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".AtomicCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void BatchUpgradeCandidate_PreservesCurrentWorkbookData()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string officialHash = ConfigPipelinePlanBuilder.HashFile(
                Path.Combine(root, "Config", "items.xlsx"));
            string nextSchemaJson = File.ReadAllText(Path.Combine(root, "Config", "schema.json"))
                .Replace("\"x-zgs-schema-version\":1", "\"x-zgs-schema-version\":2")
                .Replace(
                    "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}",
                    "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}," +
                    "\"descriptionKey\":{\"type\":\"string\"}");
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                nextSchemaJson,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                ProfileJson().Replace("Config/schema.json", "Config/schema-v2.json"),
                new UTF8Encoding(false));
            string candidates = Path.Combine(root, "UpgradeCandidates");

            ConfigPipelineCommandResult result = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                ConfigPipelineMode.UpgradeCandidate,
                candidates,
                null,
                "Config/config-project-v2.json");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Summary, Does.Contain("1->2:2"));
            ConfigSchema nextSchema = ConfigSchemaParser.Parse(Utf8(nextSchemaJson));
            ConfigObjectNode wholeSet = ReadCandidateSet(
                nextSchema,
                candidates,
                new[]
                {
                    System.Tuple.Create("items.candidate.xlsx", new[] { "items" }),
                    System.Tuple.Create("groups.candidate.xlsx", new[] { "groups" })
                },
                false);
            var expected = new ConfigObjectNode(new[]
            {
                new ConfigProperty(
                    "items",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("item-a")),
                            new ConfigProperty("value", new ConfigIntegerNode(7))
                        })
                    })),
                new ConfigProperty(
                    "groups",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("group-a"))
                        })
                    }))
            });
            Assert.That(
                CanonicalJsonWriter.WriteText(wholeSet),
                Is.EqualTo(CanonicalJsonWriter.WriteText(expected)));

            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(officialHash));
        }

        [Test]
        public void UpgradeCandidate_UnsafeSecondDirectWorkbookLeavesNoPublishedSet()
        {
            UseMacroEnabledAuthoringWorkbooks();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string nextSchemaJson = VersionTwoSchemaJson();
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                nextSchemaJson,
                new UTF8Encoding(false));
            WriteWorkbook(
                "Config/groups-next.xlsm",
                "groups",
                "group-a",
                null,
                null,
                true);
            string unsafePath = Path.Combine(root, "Config", "groups-next.xlsm");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(unsafePath, true))
            {
                ConnectionsPart connections =
                    workbook.WorkbookPart.AddNewPart<ConnectionsPart>();
                connections.Connections = new Connections();
                connections.Connections.Save();
            }

            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                UpgradeProfileJson(
                    "Config/schema-v2.json",
                    "{\"path\":\"Config/items.xlsm\",\"tables\":[\"items\"]}," +
                    "{\"path\":\"Config/groups-next.xlsm\",\"tables\":[\"groups\"]}"),
                new UTF8Encoding(false));
            string output = Path.Combine(root, "UnsafeUpgradeCandidates");

            XlsxConfigException failure = Assert.Throws<XlsxConfigException>(() =>
                service.ExportSchemaUpgradeCandidate(
                    root,
                    "Config/config-project.json",
                    "Config/config-project-v2.json",
                    "sample",
                    "package@1",
                    output));
            Assert.That(failure.Code, Is.EqualTo("XLSX_UNSAFE_PART_FORBIDDEN"));
            Assert.That(failure.Workbook, Is.EqualTo(unsafePath));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".UnsafeUpgradeCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void UpgradeCandidate_RenamedMergeRequiresExplicitMigration()
        {
            UseMacroEnabledAuthoringWorkbooks();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                VersionTwoSchemaJson(),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                UpgradeProfileJson(
                    "Config/schema-v2.json",
                    "{\"path\":\"Config/combined-next.xlsm\"," +
                    "\"tables\":[\"items\",\"groups\"]}"),
                new UTF8Encoding(false));
            string output = Path.Combine(root, "MergeUpgradeCandidates");

            InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                service.ExportSchemaUpgradeCandidate(
                    root,
                    "Config/config-project.json",
                    "Config/config-project-v2.json",
                    "sample",
                    "package@1",
                    output));
            Assert.That(
                failure.Message,
                Does.StartWith("CONFIG_WORKBOOK_SOURCE_MIGRATION_REQUIRED:"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".MergeUpgradeCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void UpgradeCandidate_RenamedSplitRequiresExplicitMigration()
        {
            UseMacroEnabledAuthoringWorkbooks();
            WriteCombinedMacroWorkbook("Config/combined.xlsm");
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                UpgradeProfileJson(
                    "Config/schema.json",
                    "{\"path\":\"Config/combined.xlsm\"," +
                    "\"tables\":[\"items\",\"groups\"]}"),
                new UTF8Encoding(false));
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                VersionTwoSchemaJson(),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                UpgradeProfileJson(
                    "Config/schema-v2.json",
                    "{\"path\":\"Config/items-next.xlsm\",\"tables\":[\"items\"]}," +
                    "{\"path\":\"Config/groups-next.xlsm\",\"tables\":[\"groups\"]}"),
                new UTF8Encoding(false));
            string output = Path.Combine(root, "SplitUpgradeCandidates");

            InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                service.ExportSchemaUpgradeCandidate(
                    root,
                    "Config/config-project.json",
                    "Config/config-project-v2.json",
                    "sample",
                    "package@1",
                    output));
            Assert.That(
                failure.Message,
                Does.StartWith("CONFIG_WORKBOOK_SOURCE_MIGRATION_REQUIRED:"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".SplitUpgradeCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void UpgradeCandidate_FreshTableCreatesTemplateAndRoundTripsWholeSet()
        {
            UseMacroEnabledAuthoringWorkbooks();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                FreshTableSchemaJson(),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                UpgradeProfileJson(
                    "Config/schema-v2.json",
                    "{\"path\":\"Config/items.xlsm\",\"tables\":[\"items\"]}," +
                    "{\"path\":\"Config/groups.xlsm\",\"tables\":[\"groups\"]}," +
                    "{\"path\":\"Config/bonuses-next.xlsm\",\"tables\":[\"bonuses\"]}"),
                new UTF8Encoding(false));
            ConfigMaintenanceRegistry.RegisterMigration(
                "sample",
                new AddBonusesMigration());
            string output = Path.Combine(root, "FreshUpgradeCandidates");

            ConfigSchemaUpgradeCandidateResult result =
                service.ExportSchemaUpgradeCandidate(
                    root,
                    "Config/config-project.json",
                    "Config/config-project-v2.json",
                    "sample",
                    "package@1",
                    output);
            Assert.That(result.CandidateFileCount, Is.EqualTo(3));
            Assert.That(
                Directory.GetFiles(output, "*.xlsm"),
                Has.Length.EqualTo(3));
            Assert.That(
                Directory.GetDirectories(root, ".FreshUpgradeCandidates.staging.*"),
                Is.Empty);

            ConfigSchema nextSchema = ConfigSchemaParser.Parse(Utf8(FreshTableSchemaJson()));
            ConfigObjectNode wholeSet = ReadCandidateSet(
                nextSchema,
                output,
                new[]
                {
                    System.Tuple.Create("items.candidate.xlsm", new[] { "items" }),
                    System.Tuple.Create("groups.candidate.xlsm", new[] { "groups" }),
                    System.Tuple.Create("bonuses-next.candidate.xlsm", new[] { "bonuses" })
                });
            ConfigDocument expected = new AddBonusesMigration().Migrate(
                ReadCurrentMacroSet());
            Assert.That(
                CanonicalJsonWriter.WriteText(wholeSet),
                Is.EqualTo(CanonicalJsonWriter.WriteText(expected.Root)));
        }

        [Test]
        public void UpgradeCandidate_SecondWorkbookLayoutConflictIsAtomic()
        {
            UseMacroEnabledAuthoringWorkbooks();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string nextSchemaJson = ExpandedGroupSchemaJson();
            Assert.That(nextSchemaJson, Does.Contain("\"label\""));
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                nextSchemaJson,
                new UTF8Encoding(false));
            WriteWorkbook(
                "Config/groups-next.xlsm",
                "groups",
                "group-a",
                null,
                null,
                true);
            string groupsNext = Path.Combine(root, "Config", "groups-next.xlsm");
            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(groupsNext, true))
            {
                WorksheetPart sheet = workbook.WorkbookPart.WorksheetParts.Single(value =>
                    workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(sheetRef =>
                        sheetRef.Id.Value == workbook.WorkbookPart.GetIdOfPart(value)).Name.Value ==
                    "Groups");
                sheet.Worksheet.Append(new MergeCells(
                    new MergeCell { Reference = "B1:B3" }));
                sheet.Worksheet.Save();
            }

            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                UpgradeProfileJson(
                    "Config/schema-v2.json",
                    "{\"path\":\"Config/items.xlsm\",\"tables\":[\"items\"]}," +
                    "{\"path\":\"Config/groups-next.xlsm\",\"tables\":[\"groups\"]}"),
                new UTF8Encoding(false));
            string output = Path.Combine(root, "AtomicUpgradeCandidates");

            InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                service.ExportSchemaUpgradeCandidate(
                    root,
                    "Config/config-project.json",
                    "Config/config-project-v2.json",
                    "sample",
                    "package@1",
                    output));
            Assert.That(
                failure.Message,
                Does.StartWith("CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT:"));
            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(
                Directory.GetDirectories(root, ".AtomicUpgradeCandidates.staging.*"),
                Is.Empty);
        }

        [Test]
        public void BatchApi_CheckIsReadOnlyAndReturnsMachineResult()
        {
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.ExportCandidate),
                Is.False);
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.UpgradeCandidate),
                Is.True);
            ConfigPipelineCommandResult stale = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                ConfigPipelineMode.Check);

            Assert.That(stale.Success, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Generated", "sample.json")), Is.False);
            Assert.That(Encoding.UTF8.GetString(stale.MachineJson), Does.Contain("\"current\": false"));
            string resultPath = Path.Combine(root, "BatchResults", "result.json");
            ConfigPipelineBatch.WriteMachineResult(resultPath, stale.MachineJson);
            Assert.That(File.ReadAllBytes(resultPath), Is.EqualTo(stale.MachineJson));
            byte[] failure = ConfigPipelineBatch.CreateFailureMachineJson(
                new InvalidOperationException("synthetic failure"));
            ConfigPipelineBatch.WriteMachineResult(resultPath, failure);
            string failureJson = File.ReadAllText(resultPath);
            Assert.That(failureJson, Does.Contain("\"success\": false"));
            Assert.That(failureJson, Does.Contain("synthetic failure"));
        }

        [Test]
        public void Apply_CreatesDeterministicMetaForNewUnityArtifacts()
        {
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                ProfileJson().Replace("Generated/", "Assets/Generated/"),
                new UTF8Encoding(false));
            var service = new ConfigPipelineService();
            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");

            Assert.That(plan.Plan.Entries.Any(value => value.RelativePath.EndsWith(".meta", StringComparison.Ordinal)), Is.True);
            Assert.That(
                plan.Plan.Entries.Any(value => value.RelativePath == "Assets/Generated.meta"),
                Is.True);
            var otherArtifacts = new System.Collections.Generic.List<ConfigArtifact>
            {
                new ConfigArtifact("Assets/Generated/other.json", Utf8("{}"))
            };
            ConfigPipelineService.AddRequiredUnityMetas(root, "other", otherArtifacts);
            byte[] plannedDirectoryMeta = plan.Artifacts.Single(
                value => value.RelativePath == "Assets/Generated.meta").Content;
            byte[] otherDirectoryMeta = otherArtifacts.Single(
                value => value.RelativePath == "Assets/Generated.meta").Content;
            Assert.That(otherDirectoryMeta, Is.EqualTo(plannedDirectoryMeta));
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string directoryMeta = File.ReadAllText(Path.Combine(root, "Assets", "Generated.meta"));
            string meta = File.ReadAllText(Path.Combine(root, "Assets", "Generated", "sample.json.meta"));
            Assert.That(directoryMeta, Does.Contain("folderAsset: yes"));
            Assert.That(directoryMeta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(meta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@1"), Is.True);
        }

        [Test]
        public void Plan_ReportsStableIdFieldDiff()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 8);

            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");

            Assert.That(
                plan.ValueDiffs.Any(value => value.FieldPath.Contains("[id=item-a]/value")),
                Is.True);
        }

        [Test]
        public void CatalogEditor_UsesSamePlanAndTransactionalApplyRoute()
        {
            string input = Path.Combine(root, "Catalog", "input.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(input));
            File.WriteAllText(input, "catalog-source", new UTF8Encoding(false));
            ConfigMaintenanceRegistry.RegisterCatalogEditor("sample", new FakeCatalogEditor());
            var service = new ConfigCatalogMaintenanceService();
            var bindings = new[]
            {
                new ConfigAssetBinding("icon.coin", "0123456789abcdef0123456789abcdef", "Sprite")
            };

            ConfigCatalogPreparedPlan plan = service.Plan(root, "sample", "package@1", bindings);
            Assert.That(plan.Plan.IsCurrent, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Catalog", "catalog.json")), Is.False);

            service.Apply(root, "sample", "package@1", bindings);
            Assert.That(service.Plan(root, "sample", "package@1", bindings).Plan.IsCurrent, Is.True);
        }

        [Test]
        public void CatalogEditor_CreatesDeterministicMetaForNewUnityArtifact()
        {
            string input = Path.Combine(root, "Catalog", "input.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(input));
            File.WriteAllText(input, "catalog-source", new UTF8Encoding(false));
            ConfigMaintenanceRegistry.RegisterCatalogEditor(
                "sample",
                new FakeCatalogEditor("Assets/Generated/catalog.json"));
            var service = new ConfigCatalogMaintenanceService();
            var bindings = new[]
            {
                new ConfigAssetBinding("icon.coin", "0123456789abcdef0123456789abcdef", "Sprite")
            };

            ConfigCatalogPreparedPlan plan = service.Plan(root, "sample", "package@1", bindings);
            Assert.That(
                plan.Plan.Entries.Any(value => value.RelativePath == "Assets/Generated/catalog.json.meta"),
                Is.True);
            service.Apply(root, "sample", "package@1", bindings);
            string meta = File.ReadAllText(Path.Combine(
                root,
                "Assets",
                "Generated",
                "catalog.json.meta"));
            Assert.That(meta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(service.Plan(root, "sample", "package@1", bindings).Plan.IsCurrent, Is.True);
        }

        [Test]
        public void ExportClientCandidate_PreservesServerOnlyWorkbookFields()
        {
            WriteScopedItemsWorkbook();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string candidates = Path.Combine(root, "ScopedCandidates");

            service.ExportJsonCandidate(
                root,
                "Config/config-project.json",
                "sample",
                "client",
                candidates);

            using (FileStream stream = File.OpenRead(Path.Combine(candidates, "items.candidate.xlsx")))
            {
                ConfigDocument candidate = new XlsxConfigSourceReader(
                    schema,
                    null,
                    new[] { "items" }).Read(
                        stream,
                        new ConfigReadContext("sample", schema.SchemaId, schema.SchemaVersion));
                string json = CanonicalJsonWriter.WriteText(candidate.Root);
                Assert.That(json, Does.Contain("\"serverValue\": \"server-kept\""));
            }
        }

        private string VersionTwoSchemaJson()
        {
            return File.ReadAllText(Path.Combine(root, "Config", "schema.json"))
                .Replace(
                    "\"x-zgs-schema-version\":1",
                    "\"x-zgs-schema-version\":2");
        }

        private string ExpandedGroupSchemaJson()
        {
            const string currentGroup =
                "\"properties\":{\"id\":{\"type\":\"string\"," +
                "\"x-zgs-primary-key\":true}}}}}}";
            const string expandedGroup =
                "\"properties\":{\"id\":{\"type\":\"string\"," +
                "\"x-zgs-primary-key\":true}," +
                "\"label\":{\"type\":\"string\"}}}}}}";
            string current = VersionTwoSchemaJson();
            if (!current.Contains(currentGroup))
            {
                throw new InvalidOperationException("Test Schema group shape changed.");
            }

            return current.Replace(currentGroup, expandedGroup);
        }

        private static string FreshTableSchemaJson()
        {
            return "{\"$id\":\"urn:zgs:test:project\"," +
                   "\"x-zgs-schema-version\":2,\"type\":\"object\"," +
                   "\"additionalProperties\":false," +
                   "\"required\":[\"items\",\"groups\",\"bonuses\"]," +
                   "\"properties\":{" +
                   "\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
                   "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                   "\"required\":[\"id\",\"value\"],\"properties\":{" +
                   "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                   "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}}}}," +
                   "\"groups\":{\"type\":\"array\",\"x-zgs-sheet\":\"Groups\"," +
                   "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                   "\"required\":[\"id\"],\"properties\":{" +
                   "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}," +
                   "\"bonuses\":{\"type\":\"array\",\"x-zgs-sheet\":\"Bonuses\"," +
                   "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                   "\"required\":[\"id\"],\"properties\":{" +
                   "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}}}";
        }

        private static string UpgradeProfileJson(
            string schemaPath,
            string workbooksJson)
        {
            return "{\"formatVersion\":1,\"configSets\":[{" +
                   "\"configSetId\":\"sample\",\"authoringSource\":\"excel\"," +
                   "\"authoringWorkbookFormat\":\"xlsm\"," +
                   "\"schema\":\"" + schemaPath + "\",\"workbooks\":[" +
                   workbooksJson + "]," +
                   "\"generatedNamespace\":\"Sample.Generated\"," +
                   "\"rootClassName\":\"SampleConfig\"," +
                   "\"codePath\":\"Generated/SampleConfig.g.cs\",\"targets\":[{" +
                   "\"scope\":\"client\",\"json\":\"Generated/sample.json\"," +
                   "\"manifest\":\"Generated/sample.manifest.json\"," +
                   "\"sourceMap\":\"Generated/sample.sourcemap.json\"}]}]}";
        }

        private void WriteCombinedMacroWorkbook(string relativePath)
        {
            var document = new ConfigDocument(
                "sample",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("item-a")),
                                new ConfigProperty("value", new ConfigIntegerNode(7))
                            })
                        })),
                    new ConfigProperty(
                        "groups",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("group-a"))
                            })
                        }))
                }));
            using (FileStream stream = File.Create(Path.Combine(
                       root,
                       relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    document,
                    null,
                    new[] { "items", "groups" },
                    null,
                    true);
            }
        }

        private ConfigDocument ReadCurrentMacroSet()
        {
            ConfigObjectNode rootNode = ReadCandidateSet(
                schema,
                Path.Combine(root, "Config"),
                new[]
                {
                    System.Tuple.Create("items.xlsm", new[] { "items" }),
                    System.Tuple.Create("groups.xlsm", new[] { "groups" })
                });
            return new ConfigDocument(
                "sample",
                schema.SchemaId,
                schema.SchemaVersion,
                rootNode);
        }

        private static ConfigObjectNode ReadCandidateSet(
            ConfigSchema candidateSchema,
            string directory,
            System.Collections.Generic.IEnumerable<System.Tuple<string, string[]>> workbooks,
            bool macroEnabled = true)
        {
            var values = new System.Collections.Generic.Dictionary<string, ConfigNode>(
                StringComparer.Ordinal);
            foreach (System.Tuple<string, string[]> workbook in workbooks)
            {
                using (FileStream stream = File.OpenRead(Path.Combine(directory, workbook.Item1)))
                {
                    ConfigDocument document = new XlsxConfigSourceReader(
                            candidateSchema,
                            null,
                            workbook.Item2,
                            macroEnabled)
                        .Read(
                            stream,
                            new ConfigReadContext(
                                "sample",
                                candidateSchema.SchemaId,
                                candidateSchema.SchemaVersion));
                    foreach (ConfigProperty property in document.Root.Properties)
                    {
                        values.Add(property.Name, property.Value);
                    }
                }
            }

            return new ConfigObjectNode(candidateSchema.Root.Properties.Select(property =>
                new ConfigProperty(property.Name, values[property.Name])));
        }

        private void WriteWorkbook(
            string relativePath,
            string property,
            string id,
            int? value,
            string groupId = null,
            bool macroEnabled = false)
        {
            var fields = new System.Collections.Generic.List<ConfigProperty>
            {
                new ConfigProperty("id", new ConfigStringNode(id))
            };
            if (value.HasValue)
            {
                fields.Add(new ConfigProperty("value", new ConfigIntegerNode(value.Value)));
            }

            if (groupId != null)
            {
                fields.Add(new ConfigProperty("groupId", new ConfigStringNode(groupId)));
            }

            var rootNode = new ConfigObjectNode(new[]
            {
                new ConfigProperty(property, new ConfigArrayNode(new ConfigNode[]
                {
                    new ConfigObjectNode(fields)
                }))
            });
            string absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            using (FileStream stream = File.Create(absolute))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    new ConfigDocument("sample", schema.SchemaId, schema.SchemaVersion, rootNode),
                    null,
                    new[] { property },
                    null,
                    macroEnabled);
            }
        }

        private void UseMacroEnabledAuthoringWorkbooks()
        {
            WriteWorkbook("Config/items.xlsm", "items", "item-a", 7, null, true);
            WriteWorkbook("Config/groups.xlsm", "groups", "group-a", null, null, true);
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                ProfileJson()
                    .Replace(
                        "\"authoringSource\":\"excel\",",
                        "\"authoringSource\":\"excel\",\"authoringWorkbookFormat\":\"xlsm\",")
                    .Replace(".xlsx", ".xlsm"),
                new UTF8Encoding(false));
        }

        private void WriteScopedItemsWorkbook()
        {
            var item = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("item-a")),
                new ConfigProperty("value", new ConfigIntegerNode(7)),
                new ConfigProperty("clientValue", new ConfigStringNode("client-value")),
                new ConfigProperty("serverValue", new ConfigStringNode("server-kept"))
            });
            string absolute = Path.Combine(root, "Config", "items.xlsx");
            using (FileStream stream = File.Create(absolute))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    new ConfigDocument(
                        "sample",
                        schema.SchemaId,
                        schema.SchemaVersion,
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("items", new ConfigArrayNode(new ConfigNode[] { item }))
                        })),
                    null,
                    new[] { "items" });
            }
        }

        private void UseAuthoringOnlyCandidateFixture(int projectedChildCount)
        {
            const string schemaJson =
                "{\"$id\":\"urn:zgs:test:project\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"items\",\"groups\"],\"properties\":{" +
                "\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"authoringName\",\"value\",\"children\"]," +
                "\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"authoringName\":{\"type\":\"string\",\"x-zgs-authoring-only\":true}," +
                "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
                "\"children\":{\"type\":\"array\",\"x-zgs-sheet\":\"ItemChildren\"," +
                "\"x-zgs-parent-key\":\"parentId\",\"x-zgs-order-field\":\"order\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"order\",\"value\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true," +
                "\"x-zgs-authoring-only\":true}," +
                "\"order\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"," +
                "\"x-zgs-authoring-only\":true}," +
                "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}}}}}}}," +
                "\"groups\":{\"type\":\"array\",\"x-zgs-sheet\":\"Groups\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\"]," +
                "\"properties\":{\"id\":{\"type\":\"string\"," +
                "\"x-zgs-primary-key\":true}}}}}}";
            byte[] schemaBytes = Utf8(schemaJson);
            File.WriteAllBytes(Path.Combine(root, "Config", "schema.json"), schemaBytes);
            schema = ConfigSchemaParser.Parse(schemaBytes);

            var children = new ConfigArrayNode(new ConfigNode[]
            {
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("id", new ConfigStringNode("child-a")),
                    new ConfigProperty("order", new ConfigIntegerNode(1)),
                    new ConfigProperty("value", new ConfigIntegerNode(10))
                }),
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("id", new ConfigStringNode("child-b")),
                    new ConfigProperty("order", new ConfigIntegerNode(2)),
                    new ConfigProperty("value", new ConfigIntegerNode(20))
                })
            });
            var item = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("item-a")),
                new ConfigProperty("authoringName", new ConfigStringNode("策划物品")),
                new ConfigProperty("value", new ConfigIntegerNode(7)),
                new ConfigProperty("children", children)
            });
            using (FileStream stream = File.Create(Path.Combine(root, "Config", "items.xlsx")))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    new ConfigDocument(
                        "sample",
                        schema.SchemaId,
                        schema.SchemaVersion,
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty(
                                "items",
                                new ConfigArrayNode(new ConfigNode[] { item }))
                        })),
                    null,
                    new[] { "items" });
            }

            WriteWorkbook("Config/groups.xlsx", "groups", "group-a", null);
            WriteUnbasedJson(AuthoringOnlyRuntimeRoot(projectedChildCount));
        }

        private static ConfigObjectNode AuthoringOnlyRuntimeRoot(int childCount)
        {
            var children = new ConfigNode[childCount];
            for (int index = 0; index < children.Length; index++)
            {
                children[index] = new ConfigObjectNode(new[]
                {
                    new ConfigProperty("value", new ConfigIntegerNode(100 + index))
                });
            }

            return new ConfigObjectNode(new[]
            {
                new ConfigProperty(
                    "items",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("item-a")),
                            new ConfigProperty("value", new ConfigIntegerNode(8)),
                            new ConfigProperty("children", new ConfigArrayNode(children))
                        })
                    })),
                new ConfigProperty(
                    "groups",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("group-a"))
                        })
                    }))
            });
        }

        private static ConfigObjectNode DefaultRuntimeRoot(int groupCount)
        {
            var groups = new ConfigNode[groupCount];
            for (int index = 0; index < groups.Length; index++)
            {
                groups[index] = new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "id",
                        new ConfigStringNode(index == 0 ? "group-a" : "group-" + index))
                });
            }

            return new ConfigObjectNode(new[]
            {
                new ConfigProperty(
                    "items",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("item-a")),
                            new ConfigProperty("value", new ConfigIntegerNode(7))
                        })
                    })),
                new ConfigProperty("groups", new ConfigArrayNode(groups))
            });
        }

        private void WriteUnbasedJson(ConfigObjectNode rootNode)
        {
            string generated = Path.Combine(root, "Generated");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(
                Path.Combine(generated, "sample.json"),
                CanonicalJsonWriter.WriteUtf8(rootNode));
            string manifest = Path.Combine(generated, "sample.manifest.json");
            if (File.Exists(manifest))
            {
                File.Delete(manifest);
            }
        }

        private static WorksheetPart GetWorksheetPart(
            SpreadsheetDocument workbook,
            string sheetName)
        {
            Sheet sheet = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                .Single(value => value.Name.Value == sheetName);
            return (WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id.Value);
        }

        private static string ProfileJson()
        {
            return "{\"formatVersion\":1,\"configSets\":[{" +
                   "\"configSetId\":\"sample\",\"authoringSource\":\"excel\"," +
                   "\"schema\":\"Config/schema.json\",\"workbooks\":[" +
                   "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]}," +
                   "{\"path\":\"Config/groups.xlsx\",\"tables\":[\"groups\"]}]," +
                   "\"generatedNamespace\":\"Sample.Generated\",\"rootClassName\":\"SampleConfig\"," +
                   "\"codePath\":\"Generated/SampleConfig.g.cs\",\"targets\":[{" +
                   "\"scope\":\"client\",\"json\":\"Generated/sample.json\"," +
                   "\"manifest\":\"Generated/sample.manifest.json\"," +
                   "\"sourceMap\":\"Generated/sample.sourcemap.json\"}]}]}";
        }

        private static byte[] Utf8(string value)
        {
            return new UTF8Encoding(false).GetBytes(value);
        }

        private sealed class AddBonusesMigration : IConfigMigration
        {
            public string SchemaId => "urn:zgs:test:project";
            public int SourceVersion => 1;
            public int TargetVersion => 2;

            public ConfigDocument Migrate(ConfigDocument source)
            {
                return new ConfigDocument(
                    source.ConfigSetId,
                    source.SchemaId,
                    TargetVersion,
                    new ConfigObjectNode(source.Root.Properties.Concat(new[]
                    {
                        new ConfigProperty(
                            "bonuses",
                            new ConfigArrayNode(Array.Empty<ConfigNode>()))
                    })));
            }
        }

        private sealed class FixedDiagnosticValidator : IConfigValidator
        {
            private readonly string fieldPath;
            private readonly ConfigSourceLocation sourceLocation;

            public FixedDiagnosticValidator(
                string fieldPath,
                ConfigSourceLocation sourceLocation = null)
            {
                this.fieldPath = fieldPath;
                this.sourceLocation = sourceLocation;
            }

            public System.Collections.Generic.IReadOnlyList<ConfigDiagnostic> Validate(
                ConfigDocument document,
                ConfigValidationContext context)
            {
                return new[]
                {
                    new ConfigDiagnostic(
                        "CONFIG_TEST_LOCATION",
                        ConfigDiagnosticSeverity.Error,
                        "Synthetic location diagnostic.",
                        document.ConfigSetId,
                        fieldPath,
                        sourceLocation)
                };
            }
        }

        private sealed class FakeCatalogEditor : IConfigAssetCatalogEditor
        {
            private readonly string artifactPath;

            public FakeCatalogEditor(string artifactPath = "Catalog/catalog.json")
            {
                this.artifactPath = artifactPath;
            }

            public System.Collections.Generic.IReadOnlyList<string> InputRelativePaths =>
                new[] { "Catalog/input.txt" };

            public ConfigAssetCatalogPlan Plan(
                string projectRoot,
                string configSetId,
                System.Collections.Generic.IReadOnlyList<ConfigAssetBinding> bindings)
            {
                ConfigAssetBinding binding = bindings.Single();
                byte[] content = CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
                {
                    new ConfigProperty("contentId", new ConfigStringNode(binding.ContentId)),
                    new ConfigProperty("assetGuid", new ConfigStringNode(binding.AssetGuid)),
                    new ConfigProperty("expectedType", new ConfigStringNode(binding.ExpectedType))
                }));
                return new ConfigAssetCatalogPlan(
                    new[] { new ConfigArtifact(artifactPath, content) },
                    new[]
                    {
                        new ConfigAssetBindingChange(
                            binding.ContentId,
                            ConfigAssetBindingChangeKind.Added)
                    },
                    Array.Empty<ConfigDiagnostic>());
            }
        }
    }
}
