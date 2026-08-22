using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class XlsxWorkbookTests
    {
        private const string SchemaJson =
            "{" +
            "\"$id\":\"zgs.sample.xlsx\"," +
            "\"x-zgs-schema-version\":1," +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"items\"]," +
            "\"properties\":{" +
            "\"items\":{" +
            "\"type\":\"array\",\"x-zgs-sheet\":\"Items\",\"uniqueItems\":true," +
            "\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"kind\",\"weight\"]," +
            "\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"title\":\"ID\",\"x-zgs-primary-key\":true}," +
            "\"kind\":{\"type\":\"string\",\"title\":\"类型\",\"enum\":[\"common\",\"rare\"]}," +
            "\"weight\":{\"type\":\"number\",\"title\":\"权重\",\"x-zgs-number-type\":\"float32\",\"minimum\":0}," +
            "\"enabled\":{\"type\":\"boolean\",\"title\":\"启用\",\"default\":true}" +
            "}}}}}";

        [Test]
        public void TemplateAndReader_RoundTripTypedDocument()
        {
            ConfigSchema schema = Schema();
            ConfigDocument source = Document();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample.xlsx",
                    source,
                    ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(source.Root)));
                stream.Position = 0;

                XlsxReadResult read = new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                    stream,
                    new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion),
                    "sample.xlsx");

                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Document.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
                Assert.That(read.SourceMap, Has.Count.EqualTo(4));
            }
        }

        [Test]
        public void Template_ProtectsMetadataAndDefaultsToNavigationSheet()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample.xlsx",
                    Document());
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    string[] validationErrors = new OpenXmlValidator()
                        .Validate(workbook)
                        .Select(value => value.Id + ": " + value.Description + " at " + value.Path.XPath)
                        .ToArray();
                    Assert.That(validationErrors, Is.Empty);
                    Assert.That(
                        workbook.WorkbookPart.WorkbookStylesPart.Stylesheet
                            .GetFirstChild<CellStyles>()
                            .Elements<CellStyle>()
                            .Single()
                            .Name.Value,
                        Is.EqualTo("Normal"));
                    Sheet[] sheets = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().ToArray();
                    Assert.That(sheets[0].Name.Value, Is.EqualTo("_zgs_schema"));
                    Assert.That(sheets[0].State.Value, Is.EqualTo(SheetStateValues.VeryHidden));
                    Assert.That(
                        sheets.Single(sheet => sheet.Name.Value == "_zgs_meta").State.Value,
                        Is.EqualTo(SheetStateValues.VeryHidden));
                    WorkbookView workbookView = workbook.WorkbookPart.Workbook
                        .GetFirstChild<BookViews>()
                        .Elements<WorkbookView>()
                        .Single();
                    Sheet navigationSheet = sheets.Single(
                        sheet => sheet.Name.Value == XlsxConfigWorkbookWriter.NavigationSheetName);
                    uint navigationSheetIndex = (uint)Array.IndexOf(sheets, navigationSheet);
                    Assert.That(workbookView.ActiveTab.Value, Is.EqualTo(navigationSheetIndex));
                    Assert.That(workbookView.FirstSheet.Value, Is.EqualTo(navigationSheetIndex));
                    WorksheetPart navigationPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(
                        navigationSheet.Id.Value);
                    SheetView navigationView = navigationPart.Worksheet.GetFirstChild<SheetViews>()
                        .Elements<SheetView>()
                        .Single();
                    Assert.That(navigationView.TabSelected.Value, Is.True);
                    Pane navigationPane = navigationView.GetFirstChild<Pane>();
                    Assert.That(navigationView.ShowGridLines.Value, Is.False);
                    Assert.That(navigationPane.HorizontalSplit, Is.Null);
                    Assert.That(navigationPane.VerticalSplit.Value, Is.EqualTo(4D));
                    Assert.That(navigationPane.TopLeftCell.Value, Is.EqualTo("A5"));
                    Assert.That(
                        navigationPart.Worksheet.GetFirstChild<AutoFilter>().Reference.Value,
                        Is.EqualTo("A4:D5"));
                    Hyperlink itemLink = navigationPart.Worksheet.GetFirstChild<Hyperlinks>()
                        .Elements<Hyperlink>()
                        .Single();
                    Assert.That(itemLink.Reference.Value, Is.EqualTo("B5"));
                    Assert.That(itemLink.Location.Value, Is.EqualTo("'Items'!A2"));
                    Sheet itemsSheet = sheets.Single(sheet => sheet.Name.Value == "Items");
                    WorksheetPart itemsPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(itemsSheet.Id.Value);
                    SheetView itemsView = itemsPart.Worksheet.GetFirstChild<SheetViews>()
                        .Elements<SheetView>()
                        .Single();
                    Assert.That(itemsView.TabSelected.Value, Is.False);
                    Assert.That(itemsView.ShowGridLines.Value, Is.False);
                    Assert.That(itemsPart.Worksheet.Elements<SheetProtection>(), Is.Empty);
                    Assert.That(
                        itemsPart.Worksheet.GetFirstChild<SheetData>().Elements<Row>().First().Hidden.Value,
                        Is.True);
                    Hyperlink navigationLink = itemsPart.Worksheet.GetFirstChild<Hyperlinks>()
                        .Elements<Hyperlink>()
                        .Single();
                    Assert.That(navigationLink.Reference.Value, Is.EqualTo("A2"));
                    Assert.That(
                        navigationLink.Location.Value,
                        Is.EqualTo("'" + XlsxConfigWorkbookWriter.NavigationSheetName + "'!A1"));
                    Row businessHeader = itemsPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .ElementAt(1);
                    Assert.That(businessHeader.Elements<Cell>().First().InnerText, Does.StartWith("← 配置目录 ｜ ＊ ID"));
                    Assert.That(businessHeader.Elements<Cell>().ElementAt(1).InnerText, Is.EqualTo("＊ 类型"));

                    TableDefinitionPart tablePart = itemsPart.TableDefinitionParts.Single();
                    Table table = tablePart.Table;
                    Assert.That(table.Reference.Value, Is.EqualTo("A2:D3"));
                    Assert.That(table.GetFirstChild<AutoFilter>().Reference.Value, Is.EqualTo("A2:D3"));
                    Assert.That(table.GetFirstChild<TableColumns>().Count.Value, Is.EqualTo(4U));
                    Assert.That(table.GetFirstChild<TableStyleInfo>().ShowRowStripes.Value, Is.True);
                    Assert.That(itemsPart.Worksheet.GetFirstChild<TableParts>().Count.Value, Is.EqualTo(1U));
                    Column[] columns = itemsPart.Worksheet.GetFirstChild<Columns>()
                        .Elements<Column>()
                        .ToArray();
                    Assert.That(columns.Select(column => column.Style.Value),
                        Is.EqualTo(new uint[] { 9U, 9U, 1U, 1U }));
                    CellFormat textFormat = workbook.WorkbookPart.WorkbookStylesPart.Stylesheet
                        .CellFormats
                        .Elements<CellFormat>()
                        .ElementAt(9);
                    Assert.That(textFormat.NumberFormatId.Value, Is.EqualTo(49U));
                    Assert.That(textFormat.ApplyNumberFormat.Value, Is.True);

                    DataValidation[] validations = itemsPart.Worksheet.Elements<DataValidations>()
                        .Single()
                        .Elements<DataValidation>()
                        .ToArray();
                    Assert.That(validations, Has.Length.EqualTo(3));
                    DataValidation enumValidation = validations.Single(
                        value => value.SequenceOfReferences.InnerText.StartsWith("B3:", StringComparison.Ordinal));
                    Assert.That(enumValidation.Type.Value, Is.EqualTo(DataValidationValues.List));
                    Assert.That(enumValidation.Formula1.InnerText, Is.EqualTo("=ZGS_ENUM_Items_kind"));
                    DataValidation numberValidation = validations.Single(
                        value => value.SequenceOfReferences.InnerText.StartsWith("C3:", StringComparison.Ordinal));
                    Assert.That(numberValidation.Type.Value, Is.EqualTo(DataValidationValues.Decimal));
                    Assert.That(
                        numberValidation.Operator.Value,
                        Is.EqualTo(DataValidationOperatorValues.GreaterThanOrEqual));
                    Assert.That(numberValidation.Formula1.InnerText, Is.EqualTo("0"));
                    DataValidation booleanValidation = validations.Single(
                        value => value.SequenceOfReferences.InnerText.StartsWith("D3:", StringComparison.Ordinal));
                    Assert.That(booleanValidation.Type.Value, Is.EqualTo(DataValidationValues.List));
                    Assert.That(booleanValidation.Formula1.InnerText, Is.EqualTo("\"TRUE,FALSE\""));
                    Assert.That(itemsPart.Worksheet.Descendants<CellFormula>(), Is.Empty);
                }
            }
        }

        [Test]
        public void EmptyTemplate_ProvidesEditableTableRowAndReadsAsEmptyArray()
        {
            ConfigSchema schema = Schema();
            ConfigDocument source = EmptyDocument();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", source);
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    Sheet items = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                        .Single(sheet => sheet.Name.Value == "Items");
                    WorksheetPart part = (WorksheetPart)workbook.WorkbookPart.GetPartById(items.Id.Value);
                    Assert.That(part.TableDefinitionParts.Single().Table.Reference.Value, Is.EqualTo("A2:D3"));
                    Row inputRow = part.Worksheet.GetFirstChild<SheetData>().Elements<Row>().ElementAt(2);
                    Assert.That(inputRow.Elements<Cell>().All(cell => string.IsNullOrEmpty(cell.InnerText)), Is.True);
                }

                stream.Position = 0;
                ConfigDocument read = new XlsxConfigSourceReader(schema).Read(
                    stream,
                    new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion));
                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
            }
        }

        [Test]
        public void Reader_AcceptsLegacyWorkbookWithoutNavigationSheet()
        {
            ConfigSchema schema = Schema();
            ConfigDocument source = Document();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", source);
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    Sheet navigationSheet = workbook.WorkbookPart.Workbook.Sheets
                        .Elements<Sheet>()
                        .Single(sheet => sheet.Name.Value == XlsxConfigWorkbookWriter.NavigationSheetName);
                    OpenXmlPart navigationPart = workbook.WorkbookPart.GetPartById(navigationSheet.Id.Value);
                    navigationSheet.Remove();
                    workbook.WorkbookPart.DeletePart(navigationPart);
                    workbook.WorkbookPart.Workbook.Save();
                }

                stream.Position = 0;
                ConfigDocument read = new XlsxConfigSourceReader(schema).Read(
                    stream,
                    new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion));
                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
            }
        }

        [Test]
        public void Reader_RejectsFormulaInjection()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample.xlsx",
                    Document());
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    Sheet items = workbook.WorkbookPart.Workbook.Sheets
                        .Elements<Sheet>()
                        .Single(sheet => sheet.Name.Value == "Items");
                    WorksheetPart part =
                        (WorksheetPart)workbook.WorkbookPart.GetPartById(items.Id.Value);
                    Cell cell = part.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .ElementAt(2)
                        .Elements<Cell>()
                        .ElementAt(2);
                    cell.CellFormula = new CellFormula("1+1");
                    part.Worksheet.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(
                    () => new XlsxConfigSourceReader(schema).Read(
                        stream,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion)));
                Assert.That(exception.Code, Is.EqualTo("XLSX_FORMULA_FORBIDDEN"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Reader_RejectsTableFormulaInjection(bool totalsFormula)
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample.xlsx",
                    Document());
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    Sheet items = workbook.WorkbookPart.Workbook.Sheets
                        .Elements<Sheet>()
                        .Single(sheet => sheet.Name.Value == "Items");
                    WorksheetPart part =
                        (WorksheetPart)workbook.WorkbookPart.GetPartById(items.Id.Value);
                    TableColumn column = part.TableDefinitionParts.Single().Table
                        .GetFirstChild<TableColumns>()
                        .Elements<TableColumn>()
                        .First();
                    if (totalsFormula)
                    {
                        column.Append(new TotalsRowFormula("SUM([ID])"));
                    }
                    else
                    {
                        column.Append(new CalculatedColumnFormula("[ID]"));
                    }

                    part.TableDefinitionParts.Single().Table.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(
                    () => new XlsxConfigSourceReader(schema).Read(
                        stream,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion)));
                Assert.That(exception.Code, Is.EqualTo("XLSX_FORMULA_FORBIDDEN"));
            }
        }

        [Test]
        public void Template_TruncatesTableHeadersToOpenXmlLimit()
        {
            string longTitle = new string('长', 300);
            string schemaJson =
                "{\"$id\":\"zgs.sample.long-header\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"]," +
                "\"properties\":{\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"code\"],\"properties\":{\"id\":{\"type\":\"string\"," +
                "\"title\":\"" + longTitle + "\",\"x-zgs-primary-key\":true}," +
                "\"code\":{\"type\":\"string\",\"title\":\"" + longTitle + "\"}}}}}}";
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(schemaJson));
            var source = new ConfigDocument(
                "long-header.xlsx",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("items", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("00123")),
                            new ConfigProperty("code", new ConfigStringNode("abc"))
                        })
                    }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "long-header.xlsx", source);
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                    TableColumn[] columns = workbook.WorkbookPart.WorksheetParts
                        .SelectMany(part => part.TableDefinitionParts)
                        .SelectMany(part => part.Table.GetFirstChild<TableColumns>().Elements<TableColumn>())
                        .ToArray();
                    Assert.That(columns, Is.Not.Empty);
                    Assert.That(columns.All(column => column.Name.Value.Length <= 255), Is.True);
                    Assert.That(
                        columns.Select(column => column.Name.Value)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        Is.EqualTo(columns.Length));
                }

                stream.Position = 0;
                ConfigDocument read = new XlsxConfigSourceReader(schema).Read(
                    stream,
                    new ConfigReadContext("long-header.xlsx", schema.SchemaId, schema.SchemaVersion));
                Assert.That(CanonicalJsonWriter.WriteText(read.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
            }
        }

        [Test]
        public void Template_LabelsNonRuntimeHeadersWithoutChangingMachineHeaders()
        {
            const string schemaJson =
                "{\"$id\":\"zgs.sample.authoring-headers\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"parents\"]," +
                "\"properties\":{\"parents\":{\"type\":\"array\",\"x-zgs-sheet\":\"Parents\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"children\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"title\":\"ID\",\"x-zgs-primary-key\":true}," +
                "\"authoringName\":{\"type\":\"string\",\"title\":\"策划名称\"," +
                "\"x-zgs-authoring-only\":true}," +
                "\"children\":{\"type\":\"array\",\"x-zgs-sheet\":\"Children\"," +
                "\"x-zgs-parent-key\":\"parentId\",\"x-zgs-order-field\":\"order\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"order\",\"value\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"title\":\"ID\",\"x-zgs-primary-key\":true}," +
                "\"order\":{\"type\":\"integer\",\"title\":\"顺序\"," +
                "\"x-zgs-number-type\":\"int32\"}," +
                "\"value\":{\"type\":\"string\",\"title\":\"值\"}}}}}}}}}";
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(schemaJson));
            var source = new ConfigDocument(
                "authoring-headers",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("parents", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty("authoringName", new ConfigStringNode("策划可读名称")),
                            new ConfigProperty("children", new ConfigArrayNode(new ConfigNode[]
                            {
                                new ConfigObjectNode(new[]
                                {
                                    new ConfigProperty("id", new ConfigStringNode("child-a")),
                                    new ConfigProperty("order", new ConfigIntegerNode(1)),
                                    new ConfigProperty("value", new ConfigStringNode("value-a"))
                                })
                            }))
                        })
                    }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "authoring-headers",
                    source);
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                    Sheet[] sheets = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().ToArray();
                    WorksheetPart navigationPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(
                        sheets.Single(sheet => sheet.Name.Value == XlsxConfigWorkbookWriter.NavigationSheetName)
                            .Id.Value);
                    Row legendRow = navigationPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(row => row.RowIndex.Value == 3U);
                    Assert.That(legendRow.Elements<Cell>().Single().InnerText, Does.Contain("不会进入运行时 JSON / DTO"));

                    WorksheetPart parentsPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(
                        sheets.Single(sheet => sheet.Name.Value == "Parents").Id.Value);
                    Row[] parentRows = parentsPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Take(3)
                        .ToArray();
                    Assert.That(parentRows[0].Elements<Cell>().Select(cell => cell.InnerText),
                        Is.EqualTo(new[] { "id", "authoringName" }));
                    Assert.That(parentRows[1].Elements<Cell>().ElementAt(1).InnerText,
                        Is.EqualTo("策划名称（仅策划，不导出）"));
                    Assert.That(parentRows[2].Elements<Cell>().ElementAt(1).InnerText,
                        Is.EqualTo("策划可读名称"));

                    WorksheetPart childrenPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(
                        sheets.Single(sheet => sheet.Name.Value == "Children").Id.Value);
                    Row[] childRows = childrenPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Take(2)
                        .ToArray();
                    Assert.That(childRows[0].Elements<Cell>().Select(cell => cell.InnerText),
                        Is.EqualTo(new[] { "parentId", "id", "order", "value" }));
                    Assert.That(childRows[1].Elements<Cell>().First().InnerText,
                        Does.Contain("parentId（关联键，不导出）"));
                    Assert.That(childRows[1].Elements<Cell>().ElementAt(2).InnerText,
                        Is.EqualTo("＊ 顺序"));
                }
            }
        }

        [Test]
        public void ChildTable_RoundTripsParentKeyAndExplicitOrder()
        {
            const string nestedSchemaJson =
                "{\"$id\":\"zgs.sample.nested\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"parents\"]," +
                "\"properties\":{\"parents\":{\"type\":\"array\",\"x-zgs-sheet\":\"Parents\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"children\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"children\":{\"type\":\"array\",\"x-zgs-sheet\":\"Children\"," +
                "\"x-zgs-parent-key\":\"parentId\",\"x-zgs-order-field\":\"order\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"order\",\"value\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"order\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
                "\"value\":{\"type\":\"string\"}}}}}}}}}";
            ConfigSchema nestedSchema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(nestedSchemaJson));
            var child = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("child-a")),
                new ConfigProperty("order", new ConfigIntegerNode(1)),
                new ConfigProperty("value", new ConfigStringNode("value-a"))
            });
            var source = new ConfigDocument(
                "nested",
                nestedSchema.SchemaId,
                nestedSchema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("parents", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty("children", new ConfigArrayNode(new ConfigNode[] { child }))
                        })
                    }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, nestedSchema, "nested", source);
                stream.Position = 0;
                XlsxReadResult read = new XlsxConfigSourceReader(nestedSchema).ReadWithSourceMap(
                    stream,
                    new ConfigReadContext("nested", nestedSchema.SchemaId, nestedSchema.SchemaVersion),
                    "nested.xlsx");

                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Document.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
                Assert.That(read.SourceMap.Any(value => value.Sheet == "Children" && value.JsonPath.Contains("children")), Is.True);
            }
        }

        [Test]
        public void AuthoringSheets_GroupRootAndChildTablesOnOneVisibleWorksheet()
        {
            const string nestedSchemaJson =
                "{\"$id\":\"zgs.sample.grouped\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"parents\"]," +
                "\"properties\":{\"parents\":{\"type\":\"array\",\"x-zgs-sheet\":\"Parents\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"children\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"children\":{\"type\":\"array\",\"x-zgs-sheet\":\"Children\"," +
                "\"x-zgs-parent-key\":\"parentId\",\"x-zgs-order-field\":\"order\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"order\",\"value\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"order\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
                "\"value\":{\"type\":\"string\"}}}}}}}}}";
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(nestedSchemaJson));
            var source = new ConfigDocument(
                "grouped",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("parents", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty("children", new ConfigArrayNode(new ConfigNode[]
                            {
                                new ConfigObjectNode(new[]
                                {
                                    new ConfigProperty("id", new ConfigStringNode("child-a")),
                                    new ConfigProperty("order", new ConfigIntegerNode(1)),
                                    new ConfigProperty("value", new ConfigStringNode("value-a"))
                                })
                            }))
                        })
                    }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "grouped",
                    source,
                    null,
                    new[] { "parents" },
                    new[]
                    {
                        new ConfigAuthoringSheetProfile("Authoring", new[] { "parents" })
                    });
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                    Sheet[] visible = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                        .Where(sheet => sheet.State == null || sheet.State.Value == SheetStateValues.Visible)
                        .ToArray();
                    Assert.That(visible.Select(sheet => sheet.Name.Value), Is.EqualTo(new[] { "Authoring" }));
                    WorksheetPart part = (WorksheetPart)workbook.WorkbookPart.GetPartById(visible[0].Id.Value);
                    Assert.That(
                        part.TableDefinitionParts.Select(value => value.Table.Reference.Value),
                        Is.EqualTo(new[] { "A2:A3", "C2:F3" }));
                    Assert.That(part.Worksheet.GetFirstChild<TableParts>().Count.Value, Is.EqualTo(2U));
                }

                stream.Position = 0;
                XlsxReadResult read = new XlsxConfigSourceReader(
                    schema,
                    null,
                    new[] { "parents" }).ReadWithSourceMap(
                    stream,
                    new ConfigReadContext("grouped", schema.SchemaId, schema.SchemaVersion),
                    "grouped.xlsx");
                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Document.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
                Assert.That(read.SourceMap.All(value => value.Sheet == "Authoring"), Is.True);
                Assert.That(read.SourceMap.Any(value => value.Column >= 3), Is.True);
            }
        }

        [Test]
        public void OneToOneObject_UsesFlattenedColumnsAndRestoresObject()
        {
            const string schemaJson =
                "{\"$id\":\"zgs.sample.object\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"]," +
                "\"properties\":{\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\",\"stats\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"stats\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"power\"],\"properties\":{" +
                "\"power\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}}}}}}}}";
            ConfigSchema objectSchema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(schemaJson));
            var source = new ConfigDocument(
                "object",
                objectSchema.SchemaId,
                objectSchema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("items", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("item-a")),
                            new ConfigProperty("stats", new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("power", new ConfigIntegerNode(5))
                            }))
                        })
                    }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, objectSchema, "object", source);
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    Sheet items = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                        .Single(value => value.Name.Value == "Items");
                    WorksheetPart part = (WorksheetPart)workbook.WorkbookPart.GetPartById(items.Id.Value);
                    string header = part.Worksheet.GetFirstChild<SheetData>().Elements<Row>().First()
                        .Elements<Cell>().Last().InlineString.Text.Text;
                    Assert.That(header, Is.EqualTo("stats.power"));
                }

                stream.Position = 0;
                ConfigDocument read = new XlsxConfigSourceReader(objectSchema).Read(
                    stream,
                    new ConfigReadContext("object", objectSchema.SchemaId, objectSchema.SchemaVersion));
                Assert.That(CanonicalJsonWriter.WriteText(read.Root), Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
            }
        }

        [Test]
        public void Reader_EnforcesCompressedRowAndColumnLimitsAtBoundary()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", Document());
                long length = stream.Length;
                stream.Position = 0;
                Assert.DoesNotThrow(() => new XlsxConfigSourceReader(
                    schema,
                    new XlsxWorkbookLimits(length, XlsxWorkbookLimits.DefaultExpandedBytes, 128, 1, 4))
                    .Read(stream, new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));

                stream.Position = 0;
                XlsxConfigException compressed = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema, new XlsxWorkbookLimits(length - 1))
                        .Read(stream, new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(compressed.Code, Is.EqualTo("XLSX_COMPRESSED_LIMIT"));

                stream.Position = 0;
                XlsxConfigException row = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(
                            schema,
                            new XlsxWorkbookLimits(length, XlsxWorkbookLimits.DefaultExpandedBytes, 128, 0, 4))
                        .Read(stream, new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(row.Code, Is.EqualTo("XLSX_ROW_LIMIT"));

                stream.Position = 0;
                XlsxConfigException column = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(
                            schema,
                            new XlsxWorkbookLimits(length, XlsxWorkbookLimits.DefaultExpandedBytes, 128, 1, 3))
                        .Read(stream, new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(column.Code, Is.EqualTo("XLSX_COLUMN_LIMIT"));
            }
        }

        [Test]
        public void Reader_DoesNotChargeOptionalNavigationAgainstWorksheetLimit()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", Document());
                long length = stream.Length;
                stream.Position = 0;
                Assert.DoesNotThrow(() => new XlsxConfigSourceReader(
                        schema,
                        new XlsxWorkbookLimits(
                            length,
                            XlsxWorkbookLimits.DefaultExpandedBytes,
                            4,
                            1,
                            4))
                    .Read(
                        stream,
                        new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
            }
        }

        [Test]
        public void Reader_RejectsMacroPart()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", Document());
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    VbaProjectPart macro = workbook.WorkbookPart.AddNewPart<VbaProjectPart>();
                    using (Stream content = macro.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        content.WriteByte(0);
                    }
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).Read(
                        stream,
                        new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(exception.Code, Is.EqualTo("XLSX_MACRO_FORBIDDEN"));
            }
        }

        [Test]
        public void Reader_FailsClosedForEncryptedOrInvalidPackage()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("not-an-xlsx-package")))
            {
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).Read(
                        stream,
                        new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(exception.Code, Is.EqualTo("XLSX_OPEN_FAILED"));
            }
        }

        [Test]
        public void Reader_RejectsExternalWorkbookPart()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "sample.xlsx", Document());
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    ExternalWorkbookPart external = workbook.WorkbookPart.AddNewPart<ExternalWorkbookPart>();
                    external.ExternalLink = new ExternalLink(new ExternalBook());
                    external.ExternalLink.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).Read(
                        stream,
                        new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
                Assert.That(exception.Code, Is.EqualTo("XLSX_EXTERNAL_LINK_FORBIDDEN"));
            }
        }

        private static ConfigSchema Schema()
        {
            return ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
        }

        private static ConfigDocument Document()
        {
            return new ConfigDocument(
                "sample.xlsx",
                "zgs.sample.xlsx",
                1,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("item.a")),
                                new ConfigProperty("kind", new ConfigStringNode("rare")),
                                new ConfigProperty("weight", new ConfigNumberNode(0.25f)),
                                new ConfigProperty("enabled", new ConfigBooleanNode(true))
                            })
                        }))
                }));
        }

        private static ConfigDocument EmptyDocument()
        {
            return new ConfigDocument(
                "sample.xlsx",
                "zgs.sample.xlsx",
                1,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("items", new ConfigArrayNode(Array.Empty<ConfigNode>()))
                }));
        }
    }
}
