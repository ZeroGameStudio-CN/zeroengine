using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

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
        public void WriterOutput_StrictReaderRoundTripsGeneratedTechnicalSheets()
        {
            ConfigSchema schema = Schema();
            ConfigDocument source = Document();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "strict-roundtrip.xlsx",
                    source,
                    ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(source.Root)));
                stream.Position = 0;

                XlsxReadResult read = new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                    stream,
                    new ConfigReadContext(
                        "strict-roundtrip.xlsx",
                        schema.SchemaId,
                        schema.SchemaVersion),
                    "strict-roundtrip.xlsx");

                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Document.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(source.Root)));
            }
        }

        [Test]
        public void WriterOutput_TechnicalRowsAndCellsHaveConsistentReferences()
        {
            ConfigSchema schema = Schema();
            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "technical-references.xlsx",
                    Document());
                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, false))
                {
                    foreach (string sheetName in new[]
                             {
                                 "_zgs_schema",
                                 "_zgs_meta",
                                 "_zgs_lists"
                             })
                    {
                        WorksheetPart worksheetPart = GetWorksheetPart(workbook, sheetName);
                        Row[] rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()
                            .Elements<Row>()
                            .ToArray();
                        Assert.That(rows, Is.Not.Empty, sheetName);

                        for (int rowOffset = 0; rowOffset < rows.Length; rowOffset++)
                        {
                            Row row = rows[rowOffset];
                            Assert.That(row.RowIndex, Is.Not.Null, sheetName);
                            uint rowIndex = row.RowIndex.Value;
                            Assert.That(rowIndex, Is.EqualTo((uint)rowOffset + 1U), sheetName);

                            Cell[] cells = row.Elements<Cell>().ToArray();
                            Assert.That(cells, Is.Not.Empty, sheetName + " row " + rowIndex);
                            for (int cellOffset = 0; cellOffset < cells.Length; cellOffset++)
                            {
                                Cell cell = cells[cellOffset];
                                Assert.That(
                                    cell.CellReference,
                                    Is.Not.Null,
                                    sheetName + " row " + rowIndex);
                                string reference = cell.CellReference.Value;
                                Assert.That(
                                    TestRowOf(reference),
                                    Is.EqualTo(rowIndex),
                                    sheetName + " " + reference);
                                Assert.That(
                                    TestColumnOf(reference),
                                    Is.EqualTo(cellOffset + 1),
                                    sheetName + " " + reference);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void PublicApi_PreservesLegacyAndExplicitMacroSignatures()
        {
            Type[] legacyReaderParameters =
            {
                typeof(ConfigSchema),
                typeof(XlsxWorkbookLimits),
                typeof(System.Collections.Generic.IEnumerable<string>)
            };
            Type[] macroReaderParameters = legacyReaderParameters
                .Concat(new[] { typeof(bool) })
                .ToArray();
            var legacyReader = typeof(XlsxConfigSourceReader).GetConstructor(
                legacyReaderParameters);
            var macroReader = typeof(XlsxConfigSourceReader).GetConstructor(
                macroReaderParameters);

            Assert.That(legacyReader, Is.Not.Null);
            Assert.That(macroReader, Is.Not.Null);
            Assert.That(legacyReader.GetParameters()[1].IsOptional, Is.True);
            Assert.That(legacyReader.GetParameters()[2].IsOptional, Is.True);
            Assert.That(macroReader.GetParameters()[3].IsOptional, Is.False);

            Type[] legacyWriterParameters =
            {
                typeof(Stream),
                typeof(ConfigSchema),
                typeof(string),
                typeof(ConfigDocument),
                typeof(string),
                typeof(System.Collections.Generic.IEnumerable<string>),
                typeof(System.Collections.Generic.IEnumerable<ConfigAuthoringSheetProfile>)
            };
            Type[] macroWriterParameters = legacyWriterParameters
                .Concat(new[] { typeof(bool) })
                .ToArray();
            var writerMethods = typeof(XlsxConfigWorkbookWriter).GetMethods()
                .Where(method => method.Name == "WriteTemplate")
                .ToArray();
            var legacyWriter = writerMethods.SingleOrDefault(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(legacyWriterParameters));
            var macroWriter = writerMethods.SingleOrDefault(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(macroWriterParameters));

            Assert.That(legacyWriter, Is.Not.Null);
            Assert.That(macroWriter, Is.Not.Null);
            Assert.That(legacyWriter.GetParameters()[3].IsOptional, Is.True);
            Assert.That(legacyWriter.GetParameters()[6].IsOptional, Is.True);
            Assert.That(macroWriter.GetParameters()[7].IsOptional, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void ParseColumn_RejectsMissingReferenceAsStructuredError(string reference)
        {
            var parseColumn = typeof(XlsxConfigSourceReader).GetMethod(
                "ParseColumn",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            Assert.That(parseColumn, Is.Not.Null);

            System.Reflection.TargetInvocationException invocation =
                Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                    parseColumn.Invoke(
                        null,
                        new object[] { reference, "authoring.xlsx", "Items", 7 }));
            XlsxConfigException exception = invocation.InnerException as XlsxConfigException;

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Code, Is.EqualTo("XLSX_CELL_REFERENCE_INVALID"));
            Assert.That(exception.Workbook, Is.EqualTo("authoring.xlsx"));
            Assert.That(exception.Sheet, Is.EqualTo("Items"));
            Assert.That(exception.Row, Is.EqualTo(7));
            Assert.That(exception.Column, Is.Null);
        }

        [Test]
        public void Reader_ReportsTamperedHeaderWorkbookCell()
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
                    WorksheetPart itemsPart = GetWorksheetPart(workbook, "Items");
                    Cell header = itemsPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(row => row.RowIndex.Value == 1U)
                        .Elements<Cell>()
                        .Single(cell => cell.CellReference.Value == "B1");
                    header.InlineString = new InlineString(new Text("tampered-kind"));
                    itemsPart.Worksheet.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                        stream,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion),
                        "authoring-items.xlsx"));

                Assert.That(exception.Code, Is.EqualTo("XLSX_HEADER_TAMPERED"));
                Assert.That(exception.Workbook, Is.EqualTo("authoring-items.xlsx"));
                Assert.That(exception.Sheet, Is.EqualTo("Items"));
                Assert.That(exception.Row, Is.EqualTo(1));
                Assert.That(exception.Column, Is.EqualTo(2));
            }
        }

        [Test]
        public void Reader_ReportsInvalidNumberWorkbookCell()
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
                    WorksheetPart itemsPart = GetWorksheetPart(workbook, "Items");
                    Cell number = itemsPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(row => row.RowIndex.Value == 3U)
                        .Elements<Cell>()
                        .Single(cell => cell.CellReference.Value == "C3");
                    number.DataType = CellValues.Number;
                    number.InlineString = null;
                    number.CellValue = new CellValue("not-a-number");
                    itemsPart.Worksheet.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                        stream,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion),
                        "authoring-items.xlsx"));

                Assert.That(exception.Code, Is.EqualTo("XLSX_NUMBER_INVALID"));
                Assert.That(exception.Workbook, Is.EqualTo("authoring-items.xlsx"));
                Assert.That(exception.Sheet, Is.EqualTo("Items"));
                Assert.That(exception.Row, Is.EqualTo(3));
                Assert.That(exception.Column, Is.EqualTo(3));
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
        public void Reader_ReportsDanglingChildParentIdAfterParentRowDeletion()
        {
            ConfigSchema schema = LocationNestedSchema();
            ConfigDocument source = NestedDocument(schema);

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "nested-location",
                    source);
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    WorksheetPart parentsPart = GetWorksheetPart(workbook, "Parents");
                    Row parent = parentsPart.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(row => row.RowIndex.Value == 3U);
                    parent.Remove();
                    parentsPart.Worksheet.Save();
                }

                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                        stream,
                        new ConfigReadContext(
                            "nested-location",
                            schema.SchemaId,
                            schema.SchemaVersion),
                        "nested-location.xlsx"));

                Assert.That(exception.Code, Is.EqualTo("XLSX_PARENT_KEY_DANGLING"));
                Assert.That(exception.Workbook, Is.EqualTo("nested-location.xlsx"));
                Assert.That(exception.Sheet, Is.EqualTo("Children"));
                Assert.That(exception.Row, Is.EqualTo(3));
                Assert.That(exception.Column, Is.EqualTo(1));
            }
        }

        [Test]
        public void Reader_ReportsDuplicateParentPrimaryKeyWorkbookCell()
        {
            ConfigSchema schema = LocationNestedSchema();
            ConfigDocument source = NestedDocumentWithDuplicateParent(schema);

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "nested-location",
                    source);
                stream.Position = 0;
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema).ReadWithSourceMap(
                        stream,
                        new ConfigReadContext(
                            "nested-location",
                            schema.SchemaId,
                            schema.SchemaVersion),
                        "nested-location.xlsx"));

                Assert.That(exception.Code, Is.EqualTo("XLSX_PRIMARY_KEY_DUPLICATE"));
                Assert.That(exception.Workbook, Is.EqualTo("nested-location.xlsx"));
                Assert.That(exception.Sheet, Is.EqualTo("Parents"));
                Assert.That(exception.Row == 3 || exception.Row == 4, Is.True);
                Assert.That(exception.Column, Is.EqualTo(1));
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
        public void Reader_AllowsMacroPartWhenConfigured()
        {
            ConfigSchema schema = Schema();
            using (var ordinary = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    ordinary,
                    schema,
                    "sample.xlsx",
                    Document());
                ordinary.Position = 0;
                XlsxConfigException mismatch = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema, null, null, true).Read(
                        ordinary,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion)));
                Assert.That(mismatch.Code, Is.EqualTo("XLSX_AUTHORING_FORMAT_MISMATCH"));
            }

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample.xlsx",
                    Document(),
                    null,
                    null,
                    null,
                    true);
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                {
                    VbaProjectPart macro = workbook.WorkbookPart.AddNewPart<VbaProjectPart>();
                    using (Stream content = macro.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        content.WriteByte(1);
                    }
                }

                stream.Position = 0;
                XlsxConfigException invalid = Assert.Throws<XlsxConfigException>(() =>
                    new XlsxConfigSourceReader(schema, null, null, true).Read(
                        stream,
                        new ConfigReadContext(
                            "sample.xlsx",
                            schema.SchemaId,
                            schema.SchemaVersion)));
                Assert.That(invalid.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));

                stream.Position = 0;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, true))
                using (Stream content = workbook.WorkbookPart.VbaProjectPart.GetStream(
                           FileMode.Create,
                           FileAccess.Write))
                {
                    byte[] macro = VbaProjectFixture();
                    content.Write(macro, 0, macro.Length);
                }

                stream.Position = 0;
                Assert.DoesNotThrow(() => new XlsxConfigSourceReader(
                        schema,
                        null,
                        null,
                        true)
                    .Read(
                        stream,
                        new ConfigReadContext("sample.xlsx", schema.SchemaId, schema.SchemaVersion)));
            }
        }

        [Test]
        public void SourcePreservingWriterRetainsMacroAndDesignerCells()
        {
            ConfigSchema schema = Schema();
            string sourcePath = Path.Combine(
                Path.GetTempPath(),
                "zgs-xlsm-source-" + Guid.NewGuid().ToString("N") + ".xlsm");
            string candidatePath = Path.Combine(
                Path.GetTempPath(),
                "zgs-xlsm-candidate-" + Guid.NewGuid().ToString("N") + ".xlsm");
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        schema,
                        "sample.xlsx",
                        Document(),
                        null,
                        null,
                        null,
                        true);
                }

                byte[] macroBytes = VbaProjectFixture();
                string sourceTableReference;
                uint designerStyleIndex;
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(sourcePath, true))
                {
                    VbaProjectPart macro = workbook.WorkbookPart.AddNewPart<VbaProjectPart>();
                    using (Stream content = macro.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        content.Write(macroBytes, 0, macroBytes.Length);
                    }

                    WorksheetPart sheet = workbook.WorkbookPart.WorksheetParts.Single(value =>
                        workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(sheetRef =>
                            sheetRef.Id.Value == workbook.WorkbookPart.GetIdOfPart(value)).Name.Value ==
                        "Items");
                    CellFormats cellFormats = workbook.WorkbookPart.WorkbookStylesPart
                        .Stylesheet.CellFormats;
                    designerStyleIndex = (uint)cellFormats.ChildElements.Count;
                    cellFormats.Append(new CellFormat(
                        new Alignment { Horizontal = HorizontalAlignmentValues.Center })
                    {
                        NumberFormatId = 0U,
                        FontId = 0U,
                        FillId = 0U,
                        BorderId = 0U,
                        FormatId = 0U,
                        ApplyAlignment = true
                    });
                    cellFormats.Count = (uint)cellFormats.ChildElements.Count;
                    workbook.WorkbookPart.WorkbookStylesPart.Stylesheet.Save();

                    SheetData sheetData = sheet.Worksheet.GetFirstChild<SheetData>();
                    foreach (Row row in sheetData.Elements<Row>()
                                 .OrderByDescending(value => value.RowIndex.Value)
                                 .ToList())
                    {
                        uint shiftedRow = row.RowIndex.Value + 1U;
                        row.RowIndex = shiftedRow;
                        foreach (Cell cell in row.Elements<Cell>())
                        {
                            cell.CellReference = ShiftCellReference(cell.CellReference.Value, 1U);
                        }
                    }

                    sheetData.PrependChild(new Row(
                        new Cell
                        {
                            CellReference = "A1",
                            StyleIndex = designerStyleIndex,
                            DataType = CellValues.InlineString,
                            InlineString = new InlineString(new Text("designer-action"))
                        })
                    {
                        RowIndex = 1U
                    });
                    Row designerRow = sheetData.Elements<Row>()
                        .Single(value => value.RowIndex.Value == 4U);
                    designerRow.Append(new Cell
                    {
                        CellReference = "Z4",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text("designer-area"))
                    });

                    Table table = sheet.TableDefinitionParts.Single().Table;
                    table.Reference = ShiftRangeReference(table.Reference.Value, 1U);
                    table.AutoFilter.Reference = table.Reference.Value;
                    sourceTableReference = table.Reference.Value;
                    table.Save();

                    DataValidations validations =
                        sheet.Worksheet.GetFirstChild<DataValidations>();
                    foreach (DataValidation validation in
                             validations.Elements<DataValidation>())
                    {
                        validation.SequenceOfReferences.InnerText =
                            ShiftValidationReferences(
                                validation.SequenceOfReferences.InnerText,
                                1U);
                    }

                    validations.Append(new DataValidation(
                        new Formula1("\"designer-a,designer-b\""))
                    {
                        Type = DataValidationValues.List,
                        AllowBlank = true,
                        SequenceOfReferences = new ListValue<StringValue>
                        {
                            InnerText = "Z1:Z10"
                        }
                    });
                    validations.Count = (uint)validations.ChildElements.Count;

                    SheetView sheetView = sheet.Worksheet.GetFirstChild<SheetViews>()
                        .Elements<SheetView>()
                        .Single();
                    Pane pane = sheetView.GetFirstChild<Pane>();
                    pane.VerticalSplit = 3D;
                    pane.TopLeftCell = "A4";

                    var conditionalFormatting = new ConditionalFormatting(
                        new ConditionalFormattingRule(new Formula("TRUE"))
                        {
                            Type = ConditionalFormatValues.Expression,
                            Priority = 1
                        })
                    {
                        SequenceOfReferences = new ListValue<StringValue>
                        {
                            InnerText = "Z1:Z10"
                        }
                    };
                    sheet.Worksheet.InsertBefore(conditionalFormatting, validations);

                    SheetProperties properties =
                        sheet.Worksheet.GetFirstChild<SheetProperties>();
                    if (properties == null)
                    {
                        properties = new SheetProperties();
                        sheet.Worksheet.PrependChild(properties);
                    }

                    properties.CodeName = "ItemAuthoring";

                    DefinedNames names = workbook.WorkbookPart.Workbook.GetFirstChild<DefinedNames>();
                    if (names == null)
                    {
                        names = new DefinedNames();
                        workbook.WorkbookPart.Workbook.Append(names);
                    }

                    names.Append(new DefinedName
                    {
                        Name = "Designer_Action_Area",
                        Text = "'Items'!$A$1"
                    });
                    names.PrependChild(new DefinedName
                    {
                        Name = "ZGS_ENUM_Items_kind",
                        LocalSheetId = 0U,
                        Text = "'Items'!$A$1"
                    });
                    sheet.Worksheet.Save();
                    workbook.WorkbookPart.Workbook.Save();
                }

                XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                    sourcePath,
                    candidatePath,
                    schema,
                    "sample.xlsx",
                    Document(),
                    "source-hash",
                    new[] { "items" },
                    null,
                    true);

                string firstCandidateHash = Sha256(File.ReadAllBytes(candidatePath));
                Assert.Throws<XlsxConfigException>(() =>
                    XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                        sourcePath,
                        candidatePath,
                        schema,
                        "sample.xlsx",
                        Document(),
                        "source-hash",
                        new[] { "items" },
                        null,
                        false));
                Assert.That(
                    Sha256(File.ReadAllBytes(candidatePath)),
                    Is.EqualTo(firstCandidateHash));
                Assert.DoesNotThrow(() =>
                    XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                        sourcePath,
                        candidatePath,
                        schema,
                        "sample.xlsx",
                        Document(),
                        "source-hash",
                        new[] { "items" },
                        null,
                        true));

                using (SpreadsheetDocument candidate = SpreadsheetDocument.Open(candidatePath, false))
                {
                    Assert.That(
                        candidate.DocumentType,
                        Is.EqualTo(SpreadsheetDocumentType.MacroEnabledWorkbook));
                    Assert.That(candidate.WorkbookPart.VbaProjectPart, Is.Not.Null);
                    using (Stream content = candidate.WorkbookPart.VbaProjectPart.GetStream(
                               FileMode.Open,
                               FileAccess.Read))
                    using (var copy = new MemoryStream())
                    {
                        content.CopyTo(copy);
                        Assert.That(Sha256(copy.ToArray()), Is.EqualTo(Sha256(macroBytes)));
                    }

                    WorksheetPart sheet = candidate.WorkbookPart.WorksheetParts.Single(value =>
                        candidate.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(sheetRef =>
                            sheetRef.Id.Value == candidate.WorkbookPart.GetIdOfPart(value)).Name.Value ==
                        "Items");
                    Assert.That(
                        sheet.Worksheet.GetFirstChild<SheetProperties>().CodeName.Value,
                        Is.EqualTo("ItemAuthoring"));
                    Cell actionCell = sheet.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(value => value.RowIndex.Value == 1U)
                        .Elements<Cell>()
                        .Single(value => value.CellReference.Value == "A1");
                    Assert.That(actionCell.InlineString.Text.Text, Is.EqualTo("designer-action"));
                    Assert.That(actionCell.StyleIndex.Value, Is.EqualTo(designerStyleIndex));
                    Cell designerCell = sheet.Worksheet.GetFirstChild<SheetData>()
                        .Elements<Row>()
                        .Single(value => value.RowIndex.Value == 4U)
                        .Elements<Cell>()
                        .Single(value => value.CellReference.Value == "Z4");
                    Assert.That(designerCell.InlineString.Text.Text, Is.EqualTo("designer-area"));
                    Assert.That(
                        sheet.Worksheet.GetFirstChild<SheetData>()
                            .Elements<Row>()
                            .Single(value => value.RowIndex.Value == 2U)
                            .Elements<Cell>()
                            .First(value => value.CellReference.Value == "A2")
                            .InlineString.Text.Text,
                        Is.EqualTo("id"));
                    Assert.That(
                        sheet.Worksheet.GetFirstChild<SheetData>()
                            .Elements<Row>()
                            .Single(value => value.RowIndex.Value == 3U)
                            .Elements<Cell>()
                            .First(value => value.CellReference.Value == "A3")
                            .InlineString.Text.Text,
                        Does.Contain("ID"));
                    Assert.That(
                        sheet.Worksheet.GetFirstChild<SheetData>()
                            .Elements<Row>()
                            .Single(value => value.RowIndex.Value == 4U)
                            .Elements<Cell>()
                            .First(value => value.CellReference.Value == "A4")
                            .InlineString.Text.Text,
                        Is.EqualTo("item.a"));
                    Table table = sheet.TableDefinitionParts.Single().Table;
                    Assert.That(table.Reference.Value, Is.EqualTo(sourceTableReference));
                    Assert.That(table.AutoFilter.Reference.Value, Is.EqualTo(sourceTableReference));
                    DataValidation[] validations = sheet.Worksheet
                        .GetFirstChild<DataValidations>()
                        .Elements<DataValidation>()
                        .ToArray();
                    Assert.That(validations, Has.Length.EqualTo(4));
                    Assert.That(
                        validations.Where(value =>
                                value.SequenceOfReferences.InnerText != "Z1:Z10")
                            .All(value => value.SequenceOfReferences.InnerText.Contains("4:")),
                        Is.True);
                    Assert.That(
                        validations.Single(value =>
                                value.SequenceOfReferences.InnerText == "Z1:Z10")
                            .Formula1.InnerText,
                        Is.EqualTo("\"designer-a,designer-b\""));
                    Pane candidatePane = sheet.Worksheet.GetFirstChild<SheetViews>()
                        .Elements<SheetView>()
                        .Single()
                        .GetFirstChild<Pane>();
                    Assert.That(candidatePane.VerticalSplit.Value, Is.EqualTo(3D));
                    Assert.That(candidatePane.TopLeftCell.Value, Is.EqualTo("A4"));
                    ConditionalFormatting candidateFormatting = sheet.Worksheet
                        .Elements<ConditionalFormatting>()
                        .Single();
                    Assert.That(
                        candidateFormatting.SequenceOfReferences.InnerText,
                        Is.EqualTo("Z1:Z10"));
                    Assert.That(
                        candidateFormatting.Descendants<Formula>().Single().Text,
                        Is.EqualTo("TRUE"));
                    Assert.That(
                        candidate.WorkbookPart.Workbook.GetFirstChild<DefinedNames>()
                            .Elements<DefinedName>()
                            .Single(value => value.Name.Value == "Designer_Action_Area")
                            .Text,
                        Is.EqualTo("'Items'!$A$1"));
                    Assert.That(
                        candidate.WorkbookPart.Workbook.GetFirstChild<DefinedNames>()
                            .Elements<DefinedName>()
                            .Single(value =>
                                value.Name.Value == "ZGS_ENUM_Items_kind" &&
                                value.LocalSheetId?.Value == 0U)
                            .Text,
                        Is.EqualTo("'Items'!$A$1"));
                    Assert.That(new OpenXmlValidator().Validate(candidate), Is.Empty);
                }
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }

                if (File.Exists(candidatePath))
                {
                    File.Delete(candidatePath);
                }
            }
        }

        [Test]
        public void SourcePreservingWriterAddsBusinessSheetAndFailsClosedOnWholeSheetRemoval()
        {
            ConfigSchema baseSchema = EvolutionSchema(false, "Bonuses");
            ConfigSchema expandedSchema = EvolutionSchema(true, "Bonuses");
            string sourcePath = TemporaryWorkbookPath("sheet-source");
            string expandedPath = TemporaryWorkbookPath("sheet-expanded");
            string removedPath = TemporaryWorkbookPath("sheet-removed");
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        baseSchema,
                        "evolution.xlsm",
                        EvolutionDocument(baseSchema, false),
                        null,
                        null,
                        null,
                        true);
                }

                byte[] macroBytes = VbaProjectFixture();
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(sourcePath, true))
                using (Stream content = workbook.WorkbookPart
                           .AddNewPart<VbaProjectPart>()
                           .GetStream(FileMode.Create, FileAccess.Write))
                {
                    content.Write(macroBytes, 0, macroBytes.Length);
                }

                XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                    sourcePath,
                    expandedPath,
                    expandedSchema,
                    "evolution.xlsm",
                    EvolutionDocument(expandedSchema, true),
                    null,
                    null,
                    null,
                    true);

                using (FileStream stream = File.OpenRead(expandedPath))
                {
                    ConfigDocument read = new XlsxConfigSourceReader(
                            expandedSchema,
                            null,
                            null,
                            true)
                        .Read(
                            stream,
                            new ConfigReadContext(
                                "evolution.xlsm",
                                expandedSchema.SchemaId,
                                expandedSchema.SchemaVersion));
                    Assert.That(
                        CanonicalJsonWriter.WriteText(read.Root),
                        Is.EqualTo(CanonicalJsonWriter.WriteText(
                            EvolutionDocument(expandedSchema, true).Root)));
                }

                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Open(expandedPath, false))
                {
                    Assert.That(
                        workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                            .Any(sheet => sheet.Name.Value == "Bonuses"),
                        Is.True);
                    Assert.That(workbook.WorkbookPart.VbaProjectPart, Is.Not.Null);
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                }

                InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                    XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                        expandedPath,
                        removedPath,
                        baseSchema,
                        "evolution.xlsm",
                        EvolutionDocument(baseSchema, false),
                        null,
                        null,
                        null,
                        true));
                Assert.That(
                    failure.Message,
                    Does.StartWith("CONFIG_WORKBOOK_REMOVED_SHEET_REQUIRES_MIGRATION:"));
                Assert.That(File.Exists(removedPath), Is.False);
            }
            finally
            {
                foreach (string path in new[] { sourcePath, expandedPath, removedPath })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        [Test]
        public void SourcePreservingWriterEvolvesManagedTablesWithoutStaleTableDefinitions()
        {
            ConfigSchema baseSchema = EvolutionSchema(false, "Bonuses");
            ConfigSchema expandedSchema = EvolutionSchema(true, "Bonuses");
            ConfigSchema renamedSchema = EvolutionSchema(true, "Rewards");
            string sourcePath = TemporaryWorkbookPath("table-source");
            string expandedPath = TemporaryWorkbookPath("table-expanded");
            string renamedPath = TemporaryWorkbookPath("table-renamed");
            string contractedPath = TemporaryWorkbookPath("table-contracted");
            var baseSheets = new[]
            {
                new ConfigAuthoringSheetProfile("Authoring", new[] { "items" })
            };
            var expandedSheets = new[]
            {
                new ConfigAuthoringSheetProfile(
                    "Authoring",
                    new[] { "items", "bonuses" })
            };
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        baseSchema,
                        "evolution.xlsm",
                        EvolutionDocument(baseSchema, false),
                        null,
                        null,
                        baseSheets,
                        true);
                }

                byte[] macroBytes = VbaProjectFixture();
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(sourcePath, true))
                {
                    VbaProjectPart macro = workbook.WorkbookPart.AddNewPart<VbaProjectPart>();
                    using (Stream content = macro.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        content.Write(macroBytes, 0, macroBytes.Length);
                    }

                    WorksheetPart sheet = GetWorksheetPart(workbook, "Authoring");
                    SheetProperties properties =
                        sheet.Worksheet.GetFirstChild<SheetProperties>();
                    if (properties == null)
                    {
                        properties = new SheetProperties();
                        sheet.Worksheet.PrependChild(properties);
                    }

                    properties.CodeName = "EvolutionAuthoring";
                    SheetData data = sheet.Worksheet.GetFirstChild<SheetData>();
                    Row designerRow = new Row { RowIndex = 20U };
                    designerRow.Append(new Cell
                    {
                        CellReference = "Z20",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text("designer-anchor"))
                    });
                    data.Append(designerRow);
                    sheet.Worksheet.Save();
                }

                XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                    sourcePath,
                    expandedPath,
                    expandedSchema,
                    "evolution.xlsm",
                    EvolutionDocument(expandedSchema, true),
                    null,
                    null,
                    expandedSheets,
                    true);
                AssertEvolutionWorkbookReads(
                    expandedPath,
                    expandedSchema,
                    EvolutionDocument(expandedSchema, true));

                XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                    expandedPath,
                    renamedPath,
                    renamedSchema,
                    "evolution.xlsm",
                    EvolutionDocument(renamedSchema, true),
                    null,
                    null,
                    expandedSheets,
                    true);
                AssertEvolutionWorkbookReads(
                    renamedPath,
                    renamedSchema,
                    EvolutionDocument(renamedSchema, true));

                XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                    renamedPath,
                    contractedPath,
                    baseSchema,
                    "evolution.xlsm",
                    EvolutionDocument(baseSchema, false),
                    null,
                    null,
                    baseSheets,
                    true);
                AssertEvolutionWorkbookReads(
                    contractedPath,
                    baseSchema,
                    EvolutionDocument(baseSchema, false));

                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Open(contractedPath, false))
                {
                    WorksheetPart sheet = GetWorksheetPart(workbook, "Authoring");
                    Assert.That(sheet.TableDefinitionParts.Count(), Is.EqualTo(1));
                    Assert.That(
                        sheet.TableDefinitionParts.Single().Table.Name.Value,
                        Is.EqualTo(XlsxConfigWorkbookWriter.BusinessTableName("Items", 1U)));
                    Assert.That(
                        sheet.Worksheet.Descendants<Cell>()
                            .Single(cell => cell.CellReference.Value == "C3")
                            .InlineString.Text.Text,
                        Is.EqualTo("bonus.a"));
                    Assert.That(
                        sheet.Worksheet.Descendants<Cell>()
                            .Single(cell => cell.CellReference.Value == "Z20")
                            .InlineString.Text.Text,
                        Is.EqualTo("designer-anchor"));
                    Assert.That(
                        sheet.Worksheet.GetFirstChild<SheetProperties>().CodeName.Value,
                        Is.EqualTo("EvolutionAuthoring"));
                    using (Stream content = workbook.WorkbookPart.VbaProjectPart.GetStream(
                               FileMode.Open,
                               FileAccess.Read))
                    using (var copy = new MemoryStream())
                    {
                        content.CopyTo(copy);
                        Assert.That(Sha256(copy.ToArray()), Is.EqualTo(Sha256(macroBytes)));
                    }

                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                }
            }
            finally
            {
                foreach (string path in new[]
                         {
                             sourcePath,
                             expandedPath,
                             renamedPath,
                             contractedPath
                         })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        [Test]
        public void SourcePreservingWriterRejectsNonCellObjectsInNewManagedArea()
        {
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                var merges = new MergeCells(new MergeCell { Reference = range });
                sheet.Worksheet.Append(merges);
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                DataValidations validations =
                    sheet.Worksheet.GetFirstChild<DataValidations>();
                if (validations == null)
                {
                    validations = new DataValidations();
                    sheet.Worksheet.Append(validations);
                }

                validations.Append(new DataValidation(new Formula1("\"designer\""))
                {
                    Type = DataValidationValues.List,
                    SequenceOfReferences = new ListValue<StringValue>
                    {
                        InnerText = range
                    }
                });
                validations.Count = (uint)validations.ChildElements.Count;
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                var hyperlinks = new Hyperlinks(new Hyperlink
                {
                    Reference = range,
                    Location = "'Authoring'!A1"
                });
                sheet.Worksheet.Append(hyperlinks);
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                sheet.Worksheet.Append(new ConditionalFormatting(
                    new ConditionalFormattingRule(new Formula("TRUE"))
                    {
                        Type = ConditionalFormatValues.Expression,
                        Priority = 1
                    })
                {
                    SequenceOfReferences = new ListValue<StringValue>
                    {
                        InnerText = range
                    }
                });
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                sheet.Worksheet.Append(new AutoFilter { Reference = range });
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                PivotTablePart pivot = sheet.AddNewPart<PivotTablePart>();
                pivot.PivotTableDefinition = new PivotTableDefinition(
                    new Location
                    {
                        Reference = range,
                        FirstHeaderRow = 1U,
                        FirstDataRow = 1U,
                        FirstDataColumn = 1U
                    });
                pivot.PivotTableDefinition.Save();
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SourcePreservingWriterRejectsBlankNamedCellInNewManagedArea(
            bool localName)
        {
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                string targetCell = range.Split(':')[0];
                Assert.That(
                    sheet.Worksheet.Descendants<Cell>().Any(cell =>
                        string.Equals(
                            cell.CellReference?.Value,
                            targetCell,
                            StringComparison.Ordinal)),
                    Is.False,
                    "The regression target must remain blank and have no <c> node.");
                AppendDesignerDefinedName(
                    workbook,
                    sheet,
                    localName ? "Local_Blank_Target" : "Global_Blank_Target",
                    localName ? targetCell : "'Authoring'!" + targetCell,
                    localName);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SourcePreservingWriterAllowsNamedRangesFarFromNewManagedArea(
            bool localName)
        {
            AssertEvolutionLayout((workbook, sheet, _) =>
            {
                AppendDesignerDefinedName(
                    workbook,
                    sheet,
                    localName ? "Local_Far_Area" : "Global_Far_Area",
                    localName
                        ? "$Z$100,$AA$100:$AA$101"
                        : "'Authoring'!$Z$100,'Authoring'!$AA$100:$AA$101",
                    localName);
            }, false);
        }

        [Test]
        public void SourcePreservingWriterRejectsUnparseableNameThatTargetsManagedSheet()
        {
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                string targetCell = range.Split(':')[0];
                AppendDesignerDefinedName(
                    workbook,
                    sheet,
                    "Dynamic_Target",
                    "OFFSET('Authoring'!" + targetCell + ",0,0)",
                    false);
            });
        }

        [Test]
        public void SourcePreservingWriterIgnoresPipelineAndBuiltInDefinedNames()
        {
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                string targetCell = range.Split(':')[0];
                AppendDesignerDefinedName(
                    workbook,
                    sheet,
                    "ZGS_ENUM_Managed_Probe",
                    "'Authoring'!" + targetCell,
                    false);
                AppendDesignerDefinedName(
                    workbook,
                    sheet,
                    "_xlnm.Print_Area",
                    targetCell,
                    true);
            }, false);
        }

        [Test]
        public void SourcePreservingWriterRejectsUnmappableDrawingsInNewManagedArea()
        {
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                DrawingsPart drawings = sheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(
                    new Xdr.AbsoluteAnchor(
                        new Xdr.Position { X = 0L, Y = 0L },
                        new Xdr.Extent { Cx = 1L, Cy = 1L },
                        new Xdr.ClientData()));
                drawings.WorksheetDrawing.Save();
                sheet.Worksheet.Append(new Drawing
                {
                    Id = sheet.GetIdOfPart(drawings)
                });
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                DrawingsPart drawings = sheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(
                    new Xdr.OneCellAnchor(
                        new Xdr.FromMarker(
                            new Xdr.ColumnId("0"),
                            new Xdr.ColumnOffset("0"),
                            new Xdr.RowId("0"),
                            new Xdr.RowOffset("0")),
                        new Xdr.Extent { Cx = 1L, Cy = 1L },
                        new Xdr.ClientData()));
                drawings.WorksheetDrawing.Save();
                sheet.Worksheet.Append(new Drawing
                {
                    Id = sheet.GetIdOfPart(drawings)
                });
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                string[] cells = range.Split(':');
                int firstColumn = TestColumnOf(cells[0]);
                uint firstRow = TestRowOf(cells[0]);
                int lastColumn = TestColumnOf(cells[1]);
                uint lastRow = TestRowOf(cells[1]);
                DrawingsPart drawings = sheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(
                    new Xdr.TwoCellAnchor(
                        DrawingFromMarker(firstColumn - 1, firstRow - 1U),
                        DrawingToMarker(lastColumn - 1, lastRow - 1U),
                        new Xdr.ClientData()));
                drawings.WorksheetDrawing.Save();
                sheet.Worksheet.Append(new Drawing
                {
                    Id = sheet.GetIdOfPart(drawings)
                });
            });
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                DrawingsPart drawings = sheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(
                    new Xdr.TwoCellAnchor(
                        DrawingFromMarker(25, 99U),
                        DrawingToMarker(25, 100U),
                        new Xdr.ClientData()));
                drawings.WorksheetDrawing.Save();
                sheet.Worksheet.Append(new Drawing
                {
                    Id = sheet.GetIdOfPart(drawings)
                });
            }, false);
            AssertEvolutionLayout((workbook, sheet, range) =>
            {
                VmlDrawingPart vml = sheet.AddNewPart<VmlDrawingPart>();
                byte[] xml = Encoding.UTF8.GetBytes(
                    "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" />");
                using (Stream content = vml.GetStream(FileMode.Create, FileAccess.Write))
                {
                    content.Write(xml, 0, xml.Length);
                }

                sheet.Worksheet.Append(new LegacyDrawing
                {
                    Id = sheet.GetIdOfPart(vml)
                });
            });
        }

        [Test]
        public void SourcePreservingWriterRejectsSparseOversizedTableBeforeCandidate()
        {
            ConfigSchema schema = EvolutionSchema(false, "Bonuses");
            string sourcePath = TemporaryWorkbookPath("sparse-source");
            string candidatePath = TemporaryWorkbookPath("sparse-candidate");
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        schema,
                        "evolution.xlsm",
                        EvolutionDocument(schema, false),
                        null,
                        null,
                        null,
                        true);
                }

                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Open(sourcePath, true))
                {
                    Table table = workbook.WorkbookPart.WorksheetParts
                        .SelectMany(sheet => sheet.TableDefinitionParts)
                        .Single()
                        .Table;
                    table.Reference = "A2:A1048576";
                    table.AutoFilter.Reference = table.Reference.Value;
                    table.Save();
                }

                using (FileStream stream = File.OpenRead(sourcePath))
                {
                    XlsxConfigException readFailure = Assert.Throws<XlsxConfigException>(() =>
                        new XlsxConfigSourceReader(schema, null, null, true).Read(
                            stream,
                            new ConfigReadContext(
                                "evolution.xlsm",
                                schema.SchemaId,
                                schema.SchemaVersion)));
                    Assert.That(readFailure.Code, Is.EqualTo("XLSX_ROW_LIMIT"));
                }

                InvalidDataException writeFailure = Assert.Throws<InvalidDataException>(() =>
                    XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                        sourcePath,
                        candidatePath,
                        schema,
                        "evolution.xlsm",
                        EvolutionDocument(schema, false),
                        null,
                        null,
                        null,
                        true));
                Assert.That(
                    writeFailure.Message,
                    Does.StartWith("CONFIG_WORKBOOK_TABLE_RANGE_LIMIT:"));
                Assert.That(File.Exists(candidatePath), Is.False);
            }
            finally
            {
                foreach (string path in new[] { sourcePath, candidatePath })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        [Test]
        public void SourcePreservingWriterRejectsDuplicateTableIdsAcrossSheets()
        {
            ConfigSchema schema = EvolutionSchema(true, "Bonuses");
            string sourcePath = TemporaryWorkbookPath("duplicate-table-id-source");
            string candidatePath = TemporaryWorkbookPath("duplicate-table-id-candidate");
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        schema,
                        "evolution.xlsm",
                        EvolutionDocument(schema, true),
                        null,
                        null,
                        null,
                        true);
                }

                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Open(sourcePath, true))
                {
                    Table[] tables = workbook.WorkbookPart.WorksheetParts
                        .SelectMany(sheet => sheet.TableDefinitionParts)
                        .Select(part => part.Table)
                        .ToArray();
                    Assert.That(tables, Has.Length.EqualTo(2));
                    tables[1].Id = tables[0].Id.Value;
                    tables[1].Save();
                }

                using (FileStream stream = File.OpenRead(sourcePath))
                {
                    XlsxConfigException readFailure = Assert.Throws<XlsxConfigException>(() =>
                        new XlsxConfigSourceReader(schema, null, null, true).Read(
                            stream,
                            new ConfigReadContext(
                                "duplicate-table-id.xlsm",
                                schema.SchemaId,
                                schema.SchemaVersion)));
                    Assert.That(readFailure.Code, Is.EqualTo("XLSX_TABLE_ID_INVALID"));
                }

                XlsxConfigException failure = Assert.Throws<XlsxConfigException>(() =>
                    XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                        sourcePath,
                        candidatePath,
                        schema,
                        "evolution.xlsm",
                        EvolutionDocument(schema, true),
                        null,
                        null,
                        null,
                        true));
                Assert.That(failure.Code, Is.EqualTo("XLSX_TABLE_ID_INVALID"));
                Assert.That(File.Exists(candidatePath), Is.False);
            }
            finally
            {
                foreach (string path in new[] { sourcePath, candidatePath })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
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

        private static WorksheetPart GetWorksheetPart(
            SpreadsheetDocument workbook,
            string sheetName)
        {
            Sheet sheet = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
                .Single(value => value.Name.Value == sheetName);
            return (WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id.Value);
        }

        private static ConfigSchema LocationNestedSchema()
        {
            const string nestedSchemaJson =
                "{\"$id\":\"zgs.sample.nested-location\",\"x-zgs-schema-version\":1," +
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
            return ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(nestedSchemaJson));
        }

        private static ConfigDocument NestedDocument(ConfigSchema schema)
        {
            var child = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("child-a")),
                new ConfigProperty("order", new ConfigIntegerNode(1)),
                new ConfigProperty("value", new ConfigStringNode("value-a"))
            });
            return new ConfigDocument(
                "nested-location",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("parents", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty(
                                "children",
                                new ConfigArrayNode(new ConfigNode[] { child }))
                        })
                    }))
                }));
        }

        private static ConfigDocument NestedDocumentWithDuplicateParent(ConfigSchema schema)
        {
            var child = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("child-a")),
                new ConfigProperty("order", new ConfigIntegerNode(1)),
                new ConfigProperty("value", new ConfigStringNode("value-a"))
            });
            return new ConfigDocument(
                "nested-location",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("parents", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty(
                                "children",
                                new ConfigArrayNode(new ConfigNode[] { child }))
                        }),
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("parent-a")),
                            new ConfigProperty(
                                "children",
                                new ConfigArrayNode(Array.Empty<ConfigNode>()))
                        })
                    }))
                }));
        }

        private static ConfigSchema EvolutionSchema(
            bool includeBonuses,
            string bonusSheet)
        {
            string bonusProperty = includeBonuses
                ? ",\"bonuses\":{\"type\":\"array\",\"x-zgs-sheet\":\"" +
                  bonusSheet +
                  "\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                  "\"required\":[\"id\"],\"properties\":{" +
                  "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}"
                : string.Empty;
            string required = includeBonuses
                ? "[\"items\",\"bonuses\"]"
                : "[\"items\"]";
            string json =
                "{\"$id\":\"zgs.sample.evolution\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":" + required + ",\"properties\":{" +
                "\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
                "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"id\"],\"properties\":{" +
                "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}" +
                bonusProperty + "}}";
            return ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(json));
        }

        private static ConfigDocument EvolutionDocument(
            ConfigSchema schema,
            bool includeBonuses)
        {
            var properties = new System.Collections.Generic.List<ConfigProperty>
            {
                new ConfigProperty(
                    "items",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("item.a"))
                        })
                    }))
            };
            if (includeBonuses)
            {
                properties.Add(new ConfigProperty(
                    "bonuses",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("bonus.a"))
                        })
                    })));
            }

            return new ConfigDocument(
                "evolution.xlsm",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(properties));
        }

        private static Xdr.FromMarker DrawingFromMarker(int column, uint row)
        {
            return new Xdr.FromMarker(
                new Xdr.ColumnId(column.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(row.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.RowOffset("0"));
        }

        private static Xdr.ToMarker DrawingToMarker(int column, uint row)
        {
            return new Xdr.ToMarker(
                new Xdr.ColumnId(column.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(row.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.RowOffset("0"));
        }

        private static int TestColumnOf(string reference)
        {
            int column = 0;
            foreach (char value in reference.TakeWhile(char.IsLetter))
            {
                column = column * 26 + char.ToUpperInvariant(value) - 'A' + 1;
            }

            return column;
        }

        private static uint TestRowOf(string reference)
        {
            string row = new string(reference.SkipWhile(char.IsLetter).ToArray());
            return uint.Parse(
                row,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AppendDesignerDefinedName(
            SpreadsheetDocument workbook,
            WorksheetPart worksheetPart,
            string name,
            string reference,
            bool localName)
        {
            WorkbookPart workbookPart = workbook.WorkbookPart;
            DefinedNames names = workbookPart.Workbook.GetFirstChild<DefinedNames>();
            if (names == null)
            {
                names = new DefinedNames();
                workbookPart.Workbook.Append(names);
            }

            var definedName = new DefinedName
            {
                Name = name,
                Text = reference
            };
            if (localName)
            {
                string relationshipId = workbookPart.GetIdOfPart(worksheetPart);
                Sheet[] sheets = workbookPart.Workbook.Sheets.Elements<Sheet>().ToArray();
                int sheetIndex = Array.FindIndex(
                    sheets,
                    sheet => string.Equals(
                        sheet.Id?.Value,
                        relationshipId,
                        StringComparison.Ordinal));
                Assert.That(sheetIndex, Is.GreaterThanOrEqualTo(0));
                definedName.LocalSheetId = (uint)sheetIndex;
            }

            names.Append(definedName);
            workbookPart.Workbook.Save();
        }

        private static void AssertEvolutionLayout(
            Action<SpreadsheetDocument, WorksheetPart, string> addDesignerObject,
            bool expectConflict = true)
        {
            ConfigSchema baseSchema = EvolutionSchema(false, "Bonuses");
            ConfigSchema expandedSchema = EvolutionSchema(true, "Bonuses");
            var baseSheets = new[]
            {
                new ConfigAuthoringSheetProfile("Authoring", new[] { "items" })
            };
            var expandedSheets = new[]
            {
                new ConfigAuthoringSheetProfile(
                    "Authoring",
                    new[] { "items", "bonuses" })
            };
            string sourcePath = TemporaryWorkbookPath("layout-source");
            string candidatePath = TemporaryWorkbookPath("layout-candidate");
            try
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        baseSchema,
                        "evolution.xlsm",
                        EvolutionDocument(baseSchema, false),
                        null,
                        null,
                        baseSheets,
                        true);
                }

                string targetRange;
                using (var generated = new MemoryStream())
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        generated,
                        expandedSchema,
                        "evolution.xlsm",
                        EvolutionDocument(expandedSchema, true),
                        null,
                        null,
                        expandedSheets,
                        true);
                    generated.Position = 0;
                    using (SpreadsheetDocument workbook =
                           SpreadsheetDocument.Open(generated, false))
                    {
                        WorksheetPart sheet = GetWorksheetPart(workbook, "Authoring");
                        targetRange = sheet.TableDefinitionParts.Single(part =>
                                part.Table.Name.Value ==
                                XlsxConfigWorkbookWriter.BusinessTableName("Bonuses", 2U))
                            .Table.Reference.Value;
                    }
                }

                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Open(sourcePath, true))
                {
                    WorksheetPart sheet = GetWorksheetPart(workbook, "Authoring");
                    addDesignerObject(workbook, sheet, targetRange);
                    sheet.Worksheet.Save();
                }

                if (expectConflict)
                {
                    InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
                        XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                            sourcePath,
                            candidatePath,
                            expandedSchema,
                            "evolution.xlsm",
                            EvolutionDocument(expandedSchema, true),
                            null,
                            null,
                            expandedSheets,
                            true));
                    Assert.That(
                        failure.Message,
                        Does.StartWith("CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT:"));
                    Assert.That(File.Exists(candidatePath), Is.False);
                }
                else
                {
                    Assert.DoesNotThrow(() =>
                        XlsxConfigWorkbookSourcePreservingWriter.WriteCandidate(
                            sourcePath,
                            candidatePath,
                            expandedSchema,
                            "evolution.xlsm",
                            EvolutionDocument(expandedSchema, true),
                            null,
                            null,
                            expandedSheets,
                            true));
                    Assert.That(File.Exists(candidatePath), Is.True);
                }
            }
            finally
            {
                foreach (string path in new[] { sourcePath, candidatePath })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        private static string TemporaryWorkbookPath(string purpose)
        {
            return Path.Combine(
                Path.GetTempPath(),
                "zgs-xlsm-" + purpose + "-" + Guid.NewGuid().ToString("N") + ".xlsm");
        }

        private static void AssertEvolutionWorkbookReads(
            string path,
            ConfigSchema schema,
            ConfigDocument expected)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                ConfigDocument read = new XlsxConfigSourceReader(
                        schema,
                        null,
                        null,
                        true)
                    .Read(
                        stream,
                        new ConfigReadContext(
                            "evolution.xlsm",
                            schema.SchemaId,
                            schema.SchemaVersion));
                Assert.That(
                    CanonicalJsonWriter.WriteText(read.Root),
                    Is.EqualTo(CanonicalJsonWriter.WriteText(expected.Root)));
            }
        }

        private static byte[] VbaProjectFixture()
        {
            return VbaCompoundFileValidatorTests.CreateValidVbaProjectFixture();
        }

        private static string ShiftRangeReference(string reference, uint rowOffset)
        {
            string[] cells = reference.Split(':');
            return ShiftCellReference(cells[0], rowOffset) + ":" +
                   ShiftCellReference(cells[1], rowOffset);
        }

        private static string ShiftValidationReferences(
            string references,
            uint rowOffset)
        {
            return string.Join(
                " ",
                references.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(reference =>
                    {
                        string[] cells = reference.Split(':');
                        return ShiftCellReference(cells[0], rowOffset) + ":" + cells[1];
                    }));
        }

        private static string ShiftCellReference(string reference, uint rowOffset)
        {
            string column = new string(reference.TakeWhile(char.IsLetter).ToArray());
            uint row = uint.Parse(reference.Substring(column.Length));
            return column + (row + rowOffset).ToString();
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
            }
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
