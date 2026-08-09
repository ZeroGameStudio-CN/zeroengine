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
        public void Template_ContainsProtectedMetadataAndEnumValidation()
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
                    Assert.That(
                        sheets.Single(sheet => sheet.Name.Value == "_zgs_meta").State.Value,
                        Is.EqualTo(SheetStateValues.VeryHidden));
                    WorksheetPart itemsPart = (WorksheetPart)workbook.WorkbookPart.GetPartById(
                        sheets.Single(sheet => sheet.Name.Value == "Items").Id.Value);
                    Assert.That(
                        itemsPart.Worksheet.GetFirstChild<SheetData>().Elements<Row>().First().Hidden.Value,
                        Is.True);
                    Assert.That(itemsPart.Worksheet.Elements<DataValidations>().Single().Count.Value, Is.EqualTo(1U));
                }
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
    }
}
