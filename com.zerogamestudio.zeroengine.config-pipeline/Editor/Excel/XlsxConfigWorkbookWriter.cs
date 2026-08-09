using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class XlsxConfigWorkbookWriter
    {
        public const int WorkbookFormatVersion = 1;

        public void WriteTemplate(
            Stream destination,
            ConfigSchema schema,
            string configSetId,
            ConfigDocument document = null,
            string workbookBaseHash = null,
            IEnumerable<string> ownedRootProperties = null)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (string.IsNullOrWhiteSpace(configSetId))
            {
                throw new ArgumentException("Config set ID is required.", nameof(configSetId));
            }

            List<TableDefinition> tables = DiscoverTables(schema, ownedRootProperties);
            if (tables.Count == 0)
            {
                throw new InvalidOperationException("Schema does not declare any x-zgs-sheet arrays.");
            }

            using (SpreadsheetDocument workbook =
                   SpreadsheetDocument.Create(
                       destination,
                       SpreadsheetDocumentType.Workbook,
                       true))
            {
                WorkbookPart workbookPart = workbook.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                AddStyles(workbookPart);
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                AddSchemaSheet(workbookPart, sheets, schema, tables, ref sheetId);
                AddMetaSheet(
                    workbookPart,
                    sheets,
                    schema,
                    configSetId,
                    workbookBaseHash ?? CreateEmptySourceHash(tables),
                    ref sheetId);
                AddEnumSheet(workbookPart, sheets, tables, ref sheetId);
                foreach (TableDefinition table in tables)
                {
                    IReadOnlyList<TableRow> rows = FindRows(document, table);
                    AddDataSheet(workbookPart, sheets, table, rows, ref sheetId);
                }

                workbookPart.Workbook.Save();
            }
        }

        private static List<TableDefinition> DiscoverTables(
            ConfigSchema schema,
            IEnumerable<string> ownedRootProperties)
        {
            var tables = new List<TableDefinition>();
            var sheetNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> owned = ownedRootProperties == null
                ? null
                : new HashSet<string>(ownedRootProperties, StringComparer.Ordinal);
            foreach (ConfigSchemaProperty property in schema.Root.Properties)
            {
                if (owned != null && !owned.Contains(property.Name))
                {
                    continue;
                }

                ConfigSchemaNode arraySchema = property.Schema;
                if (arraySchema.Type != ConfigSchemaType.Array ||
                    string.IsNullOrEmpty(arraySchema.Sheet) ||
                    arraySchema.Items?.Type != ConfigSchemaType.Object)
                {
                    continue;
                }

                AddTable(tables, sheetNames, property.Name, property.Name, arraySchema, null);
            }

            if (owned != null && !owned.SetEquals(tables.Select(table => table.RootPropertyName)))
            {
                throw new InvalidOperationException(
                    "Workbook ownership names must match declared top-level x-zgs-sheet tables.");
            }

            return tables;
        }

        private static void AddTable(
            List<TableDefinition> tables,
            HashSet<string> sheetNames,
            string rootPropertyName,
            string propertyName,
            ConfigSchemaNode arraySchema,
            TableDefinition parent)
        {
            if (arraySchema.Items?.Type != ConfigSchemaType.Object ||
                string.IsNullOrEmpty(arraySchema.Sheet) ||
                !sheetNames.Add(arraySchema.Sheet))
            {
                throw new InvalidOperationException("Every table requires a unique sheet and object items schema.");
            }

            var fields = new List<FieldDefinition>();
            AddScalarFields(arraySchema.Items, string.Empty, true, fields);
            FieldDefinition primaryKey = fields.SingleOrDefault(field => field.Schema.PrimaryKey);
            if (primaryKey == null || fields.Count(field => field.Schema.PrimaryKey) != 1)
            {
                throw new InvalidOperationException("Every table requires exactly one primary key.");
            }

            if (parent != null &&
                (string.IsNullOrEmpty(arraySchema.ParentKey) ||
                 string.IsNullOrEmpty(arraySchema.OrderField) ||
                 fields.Any(field => field.Name == arraySchema.ParentKey) ||
                 !fields.Any(field => field.Name == arraySchema.OrderField)))
            {
                throw new InvalidOperationException(
                    "Child tables require a synthetic parent key and explicit order field.");
            }

            var table = new TableDefinition(
                rootPropertyName,
                propertyName,
                arraySchema.Sheet,
                arraySchema,
                fields,
                primaryKey,
                parent);
            tables.Add(table);
            foreach (ConfigSchemaProperty child in arraySchema.Items.Properties
                         .Where(field => field.Schema.Type == ConfigSchemaType.Array))
            {
                AddTable(tables, sheetNames, rootPropertyName, child.Name, child.Schema, table);
            }
        }

        private static void AddScalarFields(
            ConfigSchemaNode objectSchema,
            string prefix,
            bool parentRequired,
            List<FieldDefinition> fields)
        {
            foreach (ConfigSchemaProperty property in objectSchema.Properties)
            {
                string path = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : prefix + "." + property.Name;
                bool required = parentRequired && objectSchema.IsRequired(property.Name);
                if (property.Schema.Type == ConfigSchemaType.Object)
                {
                    AddScalarFields(property.Schema, path, required, fields);
                }
                else if (property.Schema.Type != ConfigSchemaType.Array)
                {
                    fields.Add(new FieldDefinition(path, property.Schema, required));
                }
            }
        }

        private static void AddStyles(WorkbookPart workbookPart)
        {
            WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = new Stylesheet(
                new Fonts(new Font()) { Count = 1 },
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
                {
                    Count = 2
                },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(
                    new CellFormat(),
                    new CellFormat
                    {
                        ApplyProtection = true,
                        Protection = new Protection { Locked = false }
                    })
                {
                    Count = 2
                },
                new CellStyles(
                    new CellStyle
                    {
                        Name = "Normal",
                        FormatId = 0,
                        BuiltinId = 0
                    })
                {
                    Count = 1
                },
                new DifferentialFormats { Count = 0 },
                new TableStyles
                {
                    Count = 0,
                    DefaultTableStyle = "TableStyleMedium2",
                    DefaultPivotStyle = "PivotStyleLight16"
                });
            stylesPart.Stylesheet.Save();
        }

        private static void AddSchemaSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            ConfigSchema schema,
            IReadOnlyList<TableDefinition> tables,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            sheetData.Append(RowOf(
                "Sheet",
                "Field",
                "Title",
                "Type",
                "Required",
                "Default",
                "Range/Enum",
                "Reference",
                "Unit",
                "Description"));
            foreach (TableDefinition table in tables)
            {
                foreach (FieldDefinition field in table.Fields)
                {
                    ConfigSchemaNode fieldSchema = field.Schema;
                    sheetData.Append(RowOf(
                        table.SheetName,
                        field.Name,
                        fieldSchema.Title ?? field.Name,
                        DescribeType(fieldSchema),
                        field.Required ? "yes" : "no",
                        fieldSchema.DefaultValue == null
                            ? string.Empty
                            : CanonicalJsonWriter.WriteText(fieldSchema.DefaultValue).Trim(),
                        DescribeConstraint(fieldSchema),
                        fieldSchema.ReferencePath ?? string.Empty,
                        fieldSchema.Unit ?? string.Empty,
                        fieldSchema.Description ?? string.Empty));
                }
            }

            worksheetPart.Worksheet.Append(new SheetProtection
            {
                Sheet = true,
                Objects = true,
                Scenarios = true
            });
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "_zgs_schema",
                State = SheetStateValues.Visible
            });
        }

        private static void AddMetaSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            ConfigSchema schema,
            string configSetId,
            string workbookBaseHash,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(
                new SheetData(
                    RowOf("toolFormatVersion", WorkbookFormatVersion.ToString(CultureInfo.InvariantCulture)),
                    RowOf("schemaId", schema.SchemaId),
                    RowOf("schemaVersion", schema.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                    RowOf("schemaHash", schema.SchemaHash),
                    RowOf("configSetId", configSetId),
                    RowOf("workbookBaseHash", workbookBaseHash)),
                new SheetProtection
                {
                    Sheet = true,
                    Objects = true,
                    Scenarios = true
                });
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "_zgs_meta",
                State = SheetStateValues.VeryHidden
            });
        }

        private static void AddEnumSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            IReadOnlyList<TableDefinition> tables,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            int rowIndex = 1;
            var definedNames = new DefinedNames();
            foreach (TableDefinition table in tables)
            {
                foreach (FieldDefinition field in table.Fields)
                {
                    if (field.Schema.EnumValues.Count == 0)
                    {
                        continue;
                    }

                    int firstRow = rowIndex;
                    foreach (ConfigNode enumValue in field.Schema.EnumValues)
                    {
                        sheetData.Append(RowOf(ScalarText(enumValue)));
                        rowIndex++;
                    }

                    string rangeName = EnumRangeName(table.SheetName, field.Name);
                    definedNames.Append(new DefinedName
                    {
                        Name = rangeName,
                        Text = "'_zgs_lists'!$A$" + firstRow + ":$A$" + (rowIndex - 1)
                    });
                }
            }

            if (definedNames.HasChildren)
            {
                workbookPart.Workbook.Append(definedNames);
            }

            worksheetPart.Worksheet.Append(new SheetProtection
            {
                Sheet = true,
                Objects = true,
                Scenarios = true
            });
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "_zgs_lists",
                State = SheetStateValues.VeryHidden
            });
        }

        private static void AddDataSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            TableDefinition table,
            IReadOnlyList<TableRow> rows,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetViews = new SheetViews(
                new SheetView(
                    new Pane
                    {
                        VerticalSplit = 2D,
                        TopLeftCell = "A3",
                        ActivePane = PaneValues.BottomLeft,
                        State = PaneStateValues.Frozen
                    })
                {
                    WorkbookViewId = 0U
                });
            var columns = new Columns(
                new Column
                {
                    Min = 1,
                    Max = (uint)table.ColumnCount,
                    Style = 1,
                    Width = 18,
                    CustomWidth = true
                });
            var sheetData = new SheetData();
            var machineHeader = new Row { RowIndex = 1U, Hidden = true };
            var titleHeader = new Row { RowIndex = 2U };
            for (int columnIndex = 0; columnIndex < table.ColumnCount; columnIndex++)
            {
                bool parentColumn = table.Parent != null && columnIndex == 0;
                FieldDefinition field = parentColumn
                    ? null
                    : table.Fields[columnIndex - (table.Parent == null ? 0 : 1)];
                string fieldName = parentColumn ? table.ArraySchema.ParentKey : field.Name;
                string fieldTitle = parentColumn ? table.ArraySchema.ParentKey : field.Schema.Title ?? field.Name;
                Cell machineCell = TextCell(fieldName, 0);
                machineCell.CellReference = ColumnName(columnIndex + 1) + "1";
                machineHeader.Append(machineCell);
                Cell titleCell = TextCell(fieldTitle, 0);
                titleCell.CellReference = ColumnName(columnIndex + 1) + "2";
                titleHeader.Append(titleCell);
            }

            sheetData.Append(machineHeader);
            sheetData.Append(titleHeader);
            if (rows != null)
            {
                uint rowIndex = 3;
                foreach (TableRow tableRow in rows)
                {
                    var row = new Row { RowIndex = rowIndex++ };
                    if (table.Parent != null)
                    {
                        Cell parentCell = TextCell(tableRow.ParentKey, 1);
                        parentCell.CellReference = "A" + (rowIndex - 1).ToString(CultureInfo.InvariantCulture);
                        row.Append(parentCell);
                    }

                    for (int fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
                    {
                        FieldDefinition field = table.Fields[fieldIndex];
                        Cell cell = TryGetPath(tableRow.Value, field.Name, out ConfigNode value)
                                 ? ValueCell(value, 1)
                                 : new Cell { StyleIndex = 1U };
                        cell.CellReference =
                            ColumnName(fieldIndex + table.FieldColumnOffset) +
                            (rowIndex - 1).ToString(CultureInfo.InvariantCulture);
                        row.Append(cell);
                    }

                    sheetData.Append(row);
                }
            }

            string lastColumn = ColumnName(table.ColumnCount);
            var worksheet = new Worksheet(sheetViews, columns, sheetData);
            worksheet.Append(new SheetProtection
            {
                Sheet = true,
                Objects = true,
                Scenarios = true,
                InsertRows = false,
                DeleteRows = false,
                Sort = false,
                AutoFilter = false
            });
            worksheet.Append(new AutoFilter { Reference = "A2:" + lastColumn + "2" });
            var validations = new DataValidations();
            for (int index = 0; index < table.Fields.Count; index++)
            {
                FieldDefinition field = table.Fields[index];
                if (field.Schema.EnumValues.Count == 0)
                {
                    continue;
                }

                string columnName = ColumnName(index + table.FieldColumnOffset);
                validations.Append(new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = !field.Required,
                    ShowErrorMessage = true,
                    ErrorTitle = "Invalid config value",
                    Error = "Select a value declared by the Schema.",
                    SequenceOfReferences = new ListValue<StringValue>
                    {
                        InnerText = columnName + "3:" + columnName + "1048576"
                    },
                    Formula1 = new Formula1("=" + EnumRangeName(table.SheetName, field.Name))
                });
            }

            if (validations.HasChildren)
            {
                validations.Count = (uint)validations.ChildElements.Count;
                worksheet.Append(validations);
            }

            worksheetPart.Worksheet = worksheet;
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = table.SheetName,
                State = SheetStateValues.Visible
            });
        }

        private static Row RowOf(params string[] values)
        {
            var row = new Row();
            foreach (string value in values)
            {
                row.Append(TextCell(value, 0));
            }

            return row;
        }

        private static Cell TextCell(string value, uint styleIndex)
        {
            return new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(value ?? string.Empty)),
                StyleIndex = styleIndex
            };
        }

        private static Cell ValueCell(ConfigNode value, uint styleIndex)
        {
            switch (value.Kind)
            {
                case ConfigNodeKind.String:
                    return TextCell(((ConfigStringNode)value).Value, styleIndex);
                case ConfigNodeKind.Boolean:
                    return new Cell
                    {
                        DataType = CellValues.Boolean,
                        CellValue = new CellValue(((ConfigBooleanNode)value).Value ? "1" : "0"),
                        StyleIndex = styleIndex
                    };
                case ConfigNodeKind.Integer:
                    return new Cell
                    {
                        DataType = CellValues.Number,
                        CellValue = new CellValue(
                            CanonicalNumberWriter.Write(((ConfigIntegerNode)value).Value)),
                        StyleIndex = styleIndex
                    };
                case ConfigNodeKind.Number:
                    var number = (ConfigNumberNode)value;
                    return new Cell
                    {
                        DataType = CellValues.Number,
                        CellValue = new CellValue(
                            number.NumberType == ConfigNumberType.Float32
                                ? CanonicalNumberWriter.Write(number.Float32Value)
                                : CanonicalNumberWriter.Write(number.Value)),
                        StyleIndex = styleIndex
                    };
                case ConfigNodeKind.Null:
                    return new Cell { StyleIndex = styleIndex };
                default:
                    throw new NotSupportedException("Workbook cells only support scalar values.");
            }
        }

        private static IReadOnlyList<TableRow> FindRows(
            ConfigDocument document,
            TableDefinition table)
        {
            if (document == null)
            {
                return null;
            }

            if (table.Parent == null)
            {
                if (!document.Root.TryGetValue(table.RootPropertyName, out ConfigNode value))
                {
                    return Array.Empty<TableRow>();
                }

                ConfigArrayNode array = value as ConfigArrayNode ??
                                        throw new InvalidOperationException("Table property must be an array.");
                return array.Items.Select(node => new TableRow(
                    node as ConfigObjectNode ?? throw new InvalidOperationException("Table rows must be objects."),
                    null)).ToList();
            }

            var result = new List<TableRow>();
            foreach (TableRow parentRow in FindRows(document, table.Parent))
            {
                if (!parentRow.Value.TryGetValue(
                        table.Parent.PrimaryKey.Name,
                        out ConfigNode parentKeyNode) ||
                    !(parentKeyNode is ConfigStringNode parentKey))
                {
                    throw new InvalidOperationException("Parent table row requires a string primary key.");
                }

                if (!parentRow.Value.TryGetValue(table.PropertyName, out ConfigNode childrenNode))
                {
                    continue;
                }

                ConfigArrayNode children = childrenNode as ConfigArrayNode ??
                                           throw new InvalidOperationException("Child table property must be an array.");
                foreach (ConfigNode child in children.Items)
                {
                    result.Add(new TableRow(
                        child as ConfigObjectNode ??
                        throw new InvalidOperationException("Child table rows must be objects."),
                        parentKey.Value));
                }
            }

            return result;
        }

        private static bool TryGetPath(
            ConfigObjectNode root,
            string path,
            out ConfigNode value)
        {
            ConfigNode current = root;
            foreach (string segment in path.Split('.'))
            {
                if (!(current is ConfigObjectNode objectNode) ||
                    !objectNode.TryGetValue(segment, out current))
                {
                    value = null;
                    return false;
                }
            }

            value = current;
            return true;
        }

        private static string CreateEmptySourceHash(IReadOnlyList<TableDefinition> tables)
        {
            var properties = new List<ConfigProperty>();
            foreach (TableDefinition table in tables.Where(value => value.Parent == null))
            {
                properties.Add(
                    new ConfigProperty(
                        table.RootPropertyName,
                        new ConfigArrayNode(Array.Empty<ConfigNode>())));
            }

            return ConfigHash.Sha256(
                CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(properties)));
        }

        private static string DescribeType(ConfigSchemaNode schema)
        {
            if (schema.IntegerType.HasValue)
            {
                return schema.IntegerType.Value == ConfigIntegerType.Int32 ? "int32" : "int64";
            }

            if (schema.NumberType.HasValue)
            {
                return schema.NumberType.Value == ConfigNumberType.Float32 ? "float32" : "float64";
            }

            return schema.Type.ToString().ToLowerInvariant();
        }

        private static string DescribeConstraint(ConfigSchemaNode schema)
        {
            if (schema.EnumValues.Count != 0)
            {
                return string.Join(", ", schema.EnumValues.Select(ScalarText));
            }

            return (schema.Minimum ?? schema.ExclusiveMinimum)?.ToString(CultureInfo.InvariantCulture) +
                   ".." +
                   (schema.Maximum ?? schema.ExclusiveMaximum)?.ToString(CultureInfo.InvariantCulture);
        }

        private static string ScalarText(ConfigNode node)
        {
            if (node is ConfigStringNode text)
            {
                return text.Value;
            }

            return CanonicalJsonWriter.WriteText(node).Trim();
        }

        private static string EnumRangeName(string sheet, string field)
        {
            string raw = "ZGS_ENUM_" + sheet + "_" + field;
            return new string(
                raw.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        }

        private static string ColumnName(int column)
        {
            if (column <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            string result = string.Empty;
            int current = column;
            while (current > 0)
            {
                current--;
                result = (char)('A' + current % 26) + result;
                current /= 26;
            }

            return result;
        }

        private sealed class TableDefinition
        {
            public TableDefinition(
                string rootPropertyName,
                string propertyName,
                string sheetName,
                ConfigSchemaNode arraySchema,
                IReadOnlyList<FieldDefinition> fields,
                FieldDefinition primaryKey,
                TableDefinition parent)
            {
                RootPropertyName = rootPropertyName;
                PropertyName = propertyName;
                SheetName = sheetName;
                ArraySchema = arraySchema;
                Fields = fields;
                PrimaryKey = primaryKey;
                Parent = parent;
            }

            public string RootPropertyName { get; }

            public string PropertyName { get; }

            public string SheetName { get; }

            public IReadOnlyList<FieldDefinition> Fields { get; }

            public ConfigSchemaNode ItemSchema => ArraySchema.Items;

            public ConfigSchemaNode ArraySchema { get; }

            public FieldDefinition PrimaryKey { get; }

            public TableDefinition Parent { get; }

            public int FieldColumnOffset => Parent == null ? 1 : 2;

            public int ColumnCount => Fields.Count + (Parent == null ? 0 : 1);
        }

        private sealed class FieldDefinition
        {
            public FieldDefinition(string name, ConfigSchemaNode schema, bool required)
            {
                Name = name;
                Schema = schema;
                Required = required;
            }

            public string Name { get; }

            public ConfigSchemaNode Schema { get; }

            public bool Required { get; }
        }

        private sealed class TableRow
        {
            public TableRow(ConfigObjectNode value, string parentKey)
            {
                Value = value;
                ParentKey = parentKey;
            }

            public ConfigObjectNode Value { get; }

            public string ParentKey { get; }
        }
    }
}
