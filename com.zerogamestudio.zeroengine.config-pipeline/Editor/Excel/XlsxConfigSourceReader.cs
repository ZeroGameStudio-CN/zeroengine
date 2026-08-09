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
    public sealed class XlsxConfigSourceReader : IConfigSourceReader
    {
        private static readonly HashSet<string> InternalSheets =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "_zgs_schema",
                "_zgs_meta",
                "_zgs_lists"
            };

        private readonly ConfigSchema schema;
        private readonly XlsxWorkbookLimits limits;
        private readonly HashSet<string> ownedRootProperties;

        public XlsxConfigSourceReader(
            ConfigSchema schema,
            XlsxWorkbookLimits limits = null,
            IEnumerable<string> ownedRootProperties = null)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
            this.limits = limits ?? new XlsxWorkbookLimits();
            this.ownedRootProperties = ownedRootProperties == null
                ? null
                : new HashSet<string>(ownedRootProperties, StringComparer.Ordinal);
        }

        public ConfigDocument Read(Stream source, ConfigReadContext context)
        {
            return ReadWithSourceMap(source, context, string.Empty).Document;
        }

        public XlsxReadResult ReadWithSourceMap(
            Stream source,
            ConfigReadContext context,
            string workbookName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (source.CanSeek && source.Length > limits.MaximumCompressedBytes)
            {
                throw new XlsxConfigException(
                    "XLSX_COMPRESSED_LIMIT",
                    "Workbook exceeds the compressed-size limit.");
            }

            SpreadsheetDocument opened;
            try
            {
                opened = SpreadsheetDocument.Open(
                    source,
                    false,
                    new OpenSettings
                    {
                        AutoSave = false,
                        MaxCharactersInPart = limits.MaximumExpandedBytes
                    });
            }
            catch (Exception exception) when (
                exception is OpenXmlPackageException ||
                exception is InvalidDataException ||
                exception is FileFormatException)
            {
                throw new XlsxConfigException(
                    "XLSX_OPEN_FAILED",
                    "Workbook is encrypted, corrupt, or not a supported XLSX package.");
            }

            using (SpreadsheetDocument workbook = opened)
            {
                WorkbookPart workbookPart = workbook.WorkbookPart ??
                    throw new XlsxConfigException("XLSX_WORKBOOK_MISSING", "Workbook part is missing.");
                RejectUnsafeParts(workbookPart);
                EnforceExpandedLimit(workbookPart);
                List<TableDefinition> tables = DiscoverTables();
                ValidateSheets(workbookPart, tables);
                Dictionary<string, string> metadata = ReadMetadata(workbookPart);
                ValidateMetadata(metadata, context);

                var sourceMap = new List<XlsxSourceMapEntry>();
                var rowsByTable = new Dictionary<TableDefinition, List<ReadRow>>();
                foreach (TableDefinition table in tables)
                {
                    Sheet sheet = FindSheet(workbookPart, table.SheetName);
                    WorksheetPart worksheetPart =
                        (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                    List<ReadRow> rows = ReadTable(
                        workbookPart,
                        worksheetPart,
                        table,
                        context.ConfigSetId,
                        workbookName,
                        sourceMap);
                    rowsByTable.Add(table, rows);
                }

                foreach (TableDefinition table in tables
                             .Where(value => value.Parent != null)
                             .OrderByDescending(value => value.Depth))
                {
                    AttachChildRows(table, rowsByTable);
                }

                var rootProperties = new List<ConfigProperty>();
                foreach (TableDefinition table in tables.Where(value => value.Parent == null))
                {
                    List<ReadRow> rows = rowsByTable[table];
                    AddSourceMap(table, rows, "$/" + EscapePointer(table.RootPropertyName), workbookName, sourceMap);
                    rootProperties.Add(new ConfigProperty(
                        table.RootPropertyName,
                        new ConfigArrayNode(rows.Select(row => row.Value))));
                }

                return new XlsxReadResult(
                    new ConfigDocument(
                        context.ConfigSetId,
                        context.SchemaId,
                        context.SchemaVersion,
                        new ConfigObjectNode(rootProperties)),
                    metadata["workbookBaseHash"],
                    sourceMap);
            }
        }

        private List<TableDefinition> DiscoverTables()
        {
            var tables = new List<TableDefinition>();
            var sheetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigSchemaProperty property in schema.Root.Properties)
            {
                if (ownedRootProperties != null && !ownedRootProperties.Contains(property.Name))
                {
                    continue;
                }

                ConfigSchemaNode array = property.Schema;
                if (array.Type != ConfigSchemaType.Array || string.IsNullOrEmpty(array.Sheet))
                {
                    continue;
                }

                AddTable(tables, sheetNames, property.Name, property.Name, array, null);
            }

            if (ownedRootProperties != null &&
                !ownedRootProperties.SetEquals(tables.Select(table => table.RootPropertyName)))
            {
                throw new XlsxConfigException(
                    "XLSX_TABLE_OWNER_INVALID",
                    "Workbook ownership names must match declared top-level x-zgs-sheet tables.");
            }

            return tables;
        }

        private void AddTable(
            List<TableDefinition> tables,
            HashSet<string> sheetNames,
            string rootPropertyName,
            string propertyName,
            ConfigSchemaNode array,
            TableDefinition parent)
        {
            if (array.Items?.Type != ConfigSchemaType.Object ||
                string.IsNullOrEmpty(array.Sheet) ||
                !sheetNames.Add(array.Sheet) ||
                InternalSheets.Contains(array.Sheet))
            {
                throw new XlsxConfigException(
                    "XLSX_TABLE_SCHEMA_INVALID",
                    "Every table requires a unique x-zgs-sheet and object items schema.");
            }

            var fields = new List<FieldDefinition>();
            AddScalarFields(array.Items, string.Empty, fields);
            if (fields.Count + (parent == null ? 0 : 1) > limits.MaximumColumnsPerSheet)
            {
                throw new XlsxConfigException("XLSX_COLUMN_LIMIT", "Sheet exceeds the declared-column limit.");
            }

            FieldDefinition primaryKey = fields.SingleOrDefault(field => field.Schema.PrimaryKey);
            if (primaryKey == null || fields.Count(field => field.Schema.PrimaryKey) != 1 ||
                primaryKey.Schema.Type != ConfigSchemaType.String || primaryKey.Name.Contains("."))
            {
                throw new XlsxConfigException(
                    "XLSX_PRIMARY_KEY_REQUIRED",
                    "Each table requires exactly one string primary key.");
            }

            if (parent != null &&
                (string.IsNullOrEmpty(array.ParentKey) ||
                 string.IsNullOrEmpty(array.OrderField) ||
                 fields.Any(field => field.Name == array.ParentKey) ||
                 !fields.Any(field => field.Name == array.OrderField)))
            {
                throw new XlsxConfigException(
                    "XLSX_CHILD_TABLE_SCHEMA_INVALID",
                    "Child tables require a synthetic parent-key column and explicit order field.");
            }

            var table = new TableDefinition(
                rootPropertyName,
                propertyName,
                array.Sheet,
                array,
                fields,
                primaryKey,
                parent);
            tables.Add(table);
            foreach (ConfigSchemaProperty child in array.Items.Properties
                         .Where(field => field.Schema.Type == ConfigSchemaType.Array))
            {
                AddTable(tables, sheetNames, rootPropertyName, child.Name, child.Schema, table);
            }
        }

        private static void AddScalarFields(
            ConfigSchemaNode objectSchema,
            string prefix,
            List<FieldDefinition> fields)
        {
            foreach (ConfigSchemaProperty property in objectSchema.Properties)
            {
                string path = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : prefix + "." + property.Name;
                if (property.Schema.Type == ConfigSchemaType.Object)
                {
                    AddScalarFields(property.Schema, path, fields);
                }
                else if (property.Schema.Type != ConfigSchemaType.Array)
                {
                    fields.Add(new FieldDefinition(path, property.Schema));
                }
            }
        }

        private void ValidateSheets(WorkbookPart workbookPart, IReadOnlyList<TableDefinition> tables)
        {
            List<Sheet> sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ??
                                 new List<Sheet>();
            if (sheets.Count > limits.MaximumWorksheetCount)
            {
                throw new XlsxConfigException(
                    "XLSX_SHEET_LIMIT",
                    "Workbook exceeds the worksheet-count limit.");
            }

            var allowed = new HashSet<string>(InternalSheets, StringComparer.Ordinal);
            foreach (TableDefinition table in tables)
            {
                allowed.Add(table.SheetName);
            }

            foreach (Sheet sheet in sheets)
            {
                if (!allowed.Remove(sheet.Name.Value))
                {
                    throw new XlsxConfigException(
                        "XLSX_UNKNOWN_SHEET",
                        "Workbook contains unknown or duplicate sheet '" + sheet.Name + "'.");
                }

                if (!InternalSheets.Contains(sheet.Name.Value) &&
                    sheet.State != null &&
                    sheet.State.Value != SheetStateValues.Visible)
                {
                    throw new XlsxConfigException(
                        "XLSX_DATA_SHEET_HIDDEN",
                        "Declared data sheets cannot be hidden.");
                }
            }

            if (allowed.Count != 0)
            {
                throw new XlsxConfigException(
                    "XLSX_SHEET_MISSING",
                    "Workbook is missing one or more declared/internal sheets.");
            }

            Sheet firstSheet = sheets.FirstOrDefault();
            if (firstSheet == null ||
                !string.Equals(firstSheet.Name.Value, "_zgs_schema", StringComparison.Ordinal))
            {
                throw new XlsxConfigException(
                    "XLSX_SCHEMA_SHEET_ORDER",
                    "_zgs_schema must be the first worksheet.");
            }
        }

        private static Dictionary<string, string> ReadMetadata(WorkbookPart workbookPart)
        {
            Sheet sheet = FindSheet(workbookPart, "_zgs_meta");
            WorksheetPart worksheetPart =
                (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Row row in worksheetPart.Worksheet.GetFirstChild<SheetData>().Elements<Row>())
            {
                List<Cell> cells = row.Elements<Cell>().ToList();
                if (cells.Count < 2)
                {
                    continue;
                }

                string key = ReadCellText(workbookPart, cells[0]);
                string value = ReadCellText(workbookPart, cells[1]);
                if (metadata.ContainsKey(key))
                {
                    throw new XlsxConfigException(
                        "XLSX_META_DUPLICATE",
                        "Duplicate metadata key '" + key + "'.");
                }

                metadata.Add(key, value);
            }

            return metadata;
        }

        private void ValidateMetadata(
            IReadOnlyDictionary<string, string> metadata,
            ConfigReadContext context)
        {
            RequireMetadata(
                metadata,
                "toolFormatVersion",
                XlsxConfigWorkbookWriter.WorkbookFormatVersion.ToString(CultureInfo.InvariantCulture));
            RequireMetadata(metadata, "schemaId", schema.SchemaId);
            RequireMetadata(
                metadata,
                "schemaVersion",
                schema.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            RequireMetadata(metadata, "schemaHash", schema.SchemaHash);
            RequireMetadata(metadata, "configSetId", context.ConfigSetId);
            if (!metadata.TryGetValue("workbookBaseHash", out string baseHash) ||
                baseHash.Length != 64)
            {
                throw new XlsxConfigException(
                    "XLSX_META_BASE_HASH",
                    "Workbook base hash is missing or invalid.");
            }

            if (!string.Equals(context.SchemaId, schema.SchemaId, StringComparison.Ordinal) ||
                context.SchemaVersion != schema.SchemaVersion)
            {
                throw new XlsxConfigException(
                    "XLSX_CONTEXT_SCHEMA_MISMATCH",
                    "Read context does not match the selected schema.");
            }
        }

        private List<ReadRow> ReadTable(
            WorkbookPart workbookPart,
            WorksheetPart worksheetPart,
            TableDefinition table,
            string configSetId,
            string workbookName,
            List<XlsxSourceMapEntry> sourceMap)
        {
            SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>() ??
                                  throw new XlsxConfigException(
                                      "XLSX_SHEET_DATA_MISSING",
                                      "Sheet '" + table.SheetName + "' has no data.");
            List<Row> rows = sheetData.Elements<Row>().ToList();
            if (rows.Count < 2)
            {
                throw new XlsxConfigException(
                    "XLSX_HEADER_MISSING",
                    "Sheet '" + table.SheetName + "' is missing machine/title rows.");
            }

            ValidateHeader(workbookPart, rows[0], table);
            var readRows = new List<ReadRow>();
            foreach (Row row in rows.Skip(2))
            {
                if (row.RowIndex.HasValue && row.RowIndex.Value - 2 > limits.MaximumRowsPerSheet)
                {
                    throw new XlsxConfigException(
                        "XLSX_ROW_LIMIT",
                        "Sheet '" + table.SheetName + "' exceeds the data-row limit.");
                }

                Dictionary<int, Cell> cells = IndexCells(row);
                if (cells.Values.Any(cell => cell.CellFormula != null))
                {
                    throw new XlsxConfigException(
                        "XLSX_FORMULA_FORBIDDEN",
                        "Formulas are forbidden in data sheets.");
                }

                if (cells.Keys.Any(column => column > table.ColumnCount))
                {
                    throw new XlsxConfigException(
                        "XLSX_UNKNOWN_COLUMN",
                        "Sheet '" + table.SheetName + "' contains undeclared columns.");
                }

                var values = new Dictionary<FieldDefinition, ConfigNode>();
                var presentColumns = new List<int>();
                bool hasValue = false;
                string parentKey = null;
                if (table.Parent != null)
                {
                    cells.TryGetValue(1, out Cell parentCell);
                    string parentText = parentCell == null
                        ? string.Empty
                        : ReadCellText(workbookPart, parentCell);
                    if (!string.IsNullOrEmpty(parentText))
                    {
                        ConfigNode parsedParent = ParseCell(
                            table.Parent.PrimaryKey.Schema,
                            parentCell,
                            parentText);
                        parentKey = ((ConfigStringNode)parsedParent).Value;
                        hasValue = true;
                    }
                }

                for (int columnIndex = 0; columnIndex < table.Fields.Count; columnIndex++)
                {
                    int physicalColumn = columnIndex + table.FieldColumnOffset;
                    cells.TryGetValue(physicalColumn, out Cell cell);
                    string text = cell == null ? string.Empty : ReadCellText(workbookPart, cell);
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    hasValue = true;
                    presentColumns.Add(columnIndex);
                    FieldDefinition field = table.Fields[columnIndex];
                    ConfigNode value = ParseCell(field.Schema, cell, text);
                    values.Add(field, value);
                }

                if (hasValue)
                {
                    ConfigObjectNode objectNode = BuildObject(
                        table.ArraySchema.Items,
                        string.Empty,
                        table.Fields,
                        values);
                    if (!objectNode.TryGetValue(
                            table.PrimaryKey.Name,
                            out ConfigNode keyNode) ||
                        !(keyNode is ConfigStringNode key))
                    {
                        throw new XlsxConfigException(
                            "XLSX_PRIMARY_KEY_MISSING",
                            "Every non-empty row requires primary key '" +
                            table.PrimaryKey.Name + "'.");
                    }

                    readRows.Add(
                        new ReadRow(
                            objectNode,
                            key.Value,
                            ReadOrderValue(objectNode, table.ArraySchema.OrderField),
                            checked((int)(row.RowIndex?.Value ?? (uint)(readRows.Count + 3))),
                            presentColumns,
                            parentKey));
                }
            }

            readRows.Sort((left, right) =>
            {
                int order = Nullable.Compare(left.Order, right.Order);
                return order != 0
                    ? order
                    : string.CompareOrdinal(left.PrimaryKey, right.PrimaryKey);
            });
            if (table.Parent != null && readRows.Any(row => string.IsNullOrEmpty(row.ParentKey)))
            {
                throw new XlsxConfigException(
                    "XLSX_PARENT_KEY_MISSING",
                    "Every child-table row requires parent key '" + table.ArraySchema.ParentKey + "'.");
            }

            return readRows;
        }

        private static ConfigObjectNode BuildObject(
            ConfigSchemaNode schema,
            string prefix,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyDictionary<FieldDefinition, ConfigNode> values)
        {
            var properties = new List<ConfigProperty>();
            foreach (ConfigSchemaProperty property in schema.Properties)
            {
                string path = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : prefix + "." + property.Name;
                if (property.Schema.Type == ConfigSchemaType.Object)
                {
                    ConfigObjectNode child = BuildObject(property.Schema, path, fields, values);
                    if (child.Properties.Count != 0)
                    {
                        properties.Add(new ConfigProperty(property.Name, child));
                    }
                }
                else if (property.Schema.Type != ConfigSchemaType.Array)
                {
                    FieldDefinition field = fields.Single(value => value.Name == path);
                    if (values.TryGetValue(field, out ConfigNode node))
                    {
                        properties.Add(new ConfigProperty(property.Name, node));
                    }
                }
            }

            return new ConfigObjectNode(properties);
        }

        private static void AttachChildRows(
            TableDefinition child,
            IReadOnlyDictionary<TableDefinition, List<ReadRow>> rowsByTable)
        {
            List<ReadRow> parents = rowsByTable[child.Parent];
            var parentsById = new Dictionary<string, ReadRow>(StringComparer.Ordinal);
            foreach (ReadRow parent in parents)
            {
                if (parentsById.ContainsKey(parent.PrimaryKey))
                {
                    throw new XlsxConfigException(
                        "XLSX_PRIMARY_KEY_DUPLICATE",
                        "Duplicate primary key '" + parent.PrimaryKey + "'.");
                }

                parentsById.Add(parent.PrimaryKey, parent);
            }

            foreach (IGrouping<string, ReadRow> group in rowsByTable[child]
                         .GroupBy(row => row.ParentKey, StringComparer.Ordinal))
            {
                if (!parentsById.TryGetValue(group.Key, out ReadRow parent))
                {
                    throw new XlsxConfigException(
                        "XLSX_PARENT_KEY_DANGLING",
                        "Child table references missing parent '" + group.Key + "'.");
                }

                List<ReadRow> children = group.ToList();
                parent.ChildRows.Add(child, children);
                parent.Value = AddChildProperty(parent.Value, child, children);
            }

            foreach (ReadRow parent in parents.Where(value => !value.ChildRows.ContainsKey(child)))
            {
                parent.ChildRows.Add(child, new List<ReadRow>());
                parent.Value = AddChildProperty(parent.Value, child, Array.Empty<ReadRow>());
            }
        }

        private static ConfigObjectNode AddChildProperty(
            ConfigObjectNode parent,
            TableDefinition child,
            IEnumerable<ReadRow> rows)
        {
            var properties = new List<ConfigProperty>();
            foreach (ConfigSchemaProperty schemaProperty in child.Parent.ArraySchema.Items.Properties)
            {
                if (schemaProperty.Name == child.PropertyName)
                {
                    properties.Add(new ConfigProperty(
                        child.PropertyName,
                        new ConfigArrayNode(rows.Select(row => row.Value))));
                }
                else if (parent.TryGetValue(schemaProperty.Name, out ConfigNode value))
                {
                    properties.Add(new ConfigProperty(schemaProperty.Name, value));
                }
            }

            return new ConfigObjectNode(properties);
        }

        private static void AddSourceMap(
            TableDefinition table,
            IReadOnlyList<ReadRow> rows,
            string arrayPath,
            string workbookName,
            List<XlsxSourceMapEntry> sourceMap)
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                ReadRow row = rows[rowIndex];
                string rowPath = arrayPath + "/" + rowIndex;
                foreach (int columnIndex in row.PresentColumns)
                {
                    sourceMap.Add(new XlsxSourceMapEntry(
                        rowPath + "/" + table.Fields[columnIndex].PointerPath,
                        workbookName,
                        table.SheetName,
                        row.SourceRow,
                        columnIndex + table.FieldColumnOffset));
                }

                foreach (KeyValuePair<TableDefinition, List<ReadRow>> child in row.ChildRows)
                {
                    AddSourceMap(
                        child.Key,
                        child.Value,
                        rowPath + "/" + EscapePointer(child.Key.PropertyName),
                        workbookName,
                        sourceMap);
                }
            }
        }

        private static void ValidateHeader(
            WorkbookPart workbookPart,
            Row machineHeader,
            TableDefinition table)
        {
            Dictionary<int, Cell> cells = IndexCells(machineHeader);
            if (cells.Count != table.ColumnCount)
            {
                throw new XlsxConfigException(
                    "XLSX_HEADER_COUNT",
                    "Machine header column count does not match the Schema.");
            }

            for (int index = 0; index < table.ColumnCount; index++)
            {
                if (!cells.TryGetValue(index + 1, out Cell cell))
                {
                    throw new XlsxConfigException(
                        "XLSX_HEADER_COUNT",
                        "Machine header contains a missing column.");
                }

                string actual = ReadCellText(workbookPart, cell);
                string expected = table.Parent != null && index == 0
                    ? table.ArraySchema.ParentKey
                    : table.Fields[index - (table.Parent == null ? 0 : 1)].Name;
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new XlsxConfigException(
                        "XLSX_HEADER_TAMPERED",
                        "Machine header does not match the Schema at column " + (index + 1) + ".");
                }
            }
        }

        private static ConfigNode ParseCell(
            ConfigSchemaNode schema,
            Cell cell,
            string text)
        {
            switch (schema.Type)
            {
                case ConfigSchemaType.String:
                    if (cell.DataType == null ||
                        (cell.DataType.Value != CellValues.SharedString &&
                         cell.DataType.Value != CellValues.InlineString &&
                         cell.DataType.Value != CellValues.String))
                    {
                        throw new XlsxConfigException(
                            "XLSX_STRING_CELL_REQUIRED",
                            "String fields require an explicit string cell.");
                    }

                    return new ConfigStringNode(text);
                case ConfigSchemaType.Boolean:
                    if (string.Equals(text, "1", StringComparison.Ordinal) ||
                        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConfigBooleanNode(true);
                    }

                    if (string.Equals(text, "0", StringComparison.Ordinal) ||
                        string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConfigBooleanNode(false);
                    }

                    throw new XlsxConfigException(
                        "XLSX_BOOLEAN_INVALID",
                        "Boolean cells must be true/false or 1/0.");
                case ConfigSchemaType.Integer:
                    if (!long.TryParse(
                            text,
                            NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture,
                            out long integer))
                    {
                        throw new XlsxConfigException(
                            "XLSX_INTEGER_INVALID",
                            "Integer cell is invalid.");
                    }

                    return new ConfigIntegerNode(integer);
                case ConfigSchemaType.Number:
                    if (!double.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double number) ||
                        double.IsNaN(number) ||
                        double.IsInfinity(number))
                    {
                        throw new XlsxConfigException(
                            "XLSX_NUMBER_INVALID",
                            "Number cell is invalid or non-finite.");
                    }

                    return schema.NumberType == ConfigNumberType.Float32
                        ? new ConfigNumberNode((float)number)
                        : new ConfigNumberNode(number);
                default:
                    throw new XlsxConfigException(
                        "XLSX_CELL_TYPE_UNSUPPORTED",
                        "Workbook cells only support scalar fields.");
            }
        }

        private static string ReadCellText(WorkbookPart workbookPart, Cell cell)
        {
            if (cell.DataType != null &&
                cell.DataType.Value == CellValues.SharedString)
            {
                if (!int.TryParse(cell.CellValue?.Text, out int index) ||
                    workbookPart.SharedStringTablePart?.SharedStringTable == null)
                {
                    throw new XlsxConfigException(
                        "XLSX_SHARED_STRING_INVALID",
                        "Shared string reference is invalid.");
                }

                return workbookPart.SharedStringTablePart.SharedStringTable
                    .Elements<SharedStringItem>()
                    .ElementAt(index)
                    .InnerText;
            }

            if (cell.DataType != null &&
                cell.DataType.Value == CellValues.InlineString)
            {
                return cell.InlineString?.InnerText ?? string.Empty;
            }

            return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
        }

        private static Dictionary<int, Cell> IndexCells(Row row)
        {
            var cells = new Dictionary<int, Cell>();
            int nextColumn = 1;
            foreach (Cell cell in row.Elements<Cell>())
            {
                int column = cell.CellReference == null
                    ? nextColumn
                    : ParseColumn(cell.CellReference.Value);
                if (cells.ContainsKey(column))
                {
                    throw new XlsxConfigException(
                        "XLSX_CELL_REFERENCE_DUPLICATE",
                        "Row contains duplicate cell references.");
                }

                cells.Add(column, cell);
                nextColumn = column + 1;
            }

            return cells;
        }

        private static int ParseColumn(string reference)
        {
            int column = 0;
            int index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                char value = char.ToUpperInvariant(reference[index]);
                if (value < 'A' || value > 'Z')
                {
                    break;
                }

                column = checked(column * 26 + value - 'A' + 1);
                index++;
            }

            if (column == 0)
            {
                throw new XlsxConfigException(
                    "XLSX_CELL_REFERENCE_INVALID",
                    "Cell reference is invalid.");
            }

            return column;
        }

        private static void RejectUnsafeParts(WorkbookPart workbookPart)
        {
            if (workbookPart.VbaProjectPart != null)
            {
                throw new XlsxConfigException("XLSX_MACRO_FORBIDDEN", "Macro-enabled workbooks are forbidden.");
            }

            if (workbookPart.ExternalWorkbookParts.Any() ||
                workbookPart.ExternalRelationships.Any() ||
                workbookPart.WorksheetParts.Any(part => part.ExternalRelationships.Any()))
            {
                throw new XlsxConfigException(
                    "XLSX_EXTERNAL_LINK_FORBIDDEN",
                    "External workbook relationships are forbidden.");
            }

            foreach (WorksheetPart worksheetPart in workbookPart.WorksheetParts)
            {
                if (worksheetPart.Worksheet.Descendants<CellFormula>().Any())
                {
                    throw new XlsxConfigException(
                        "XLSX_FORMULA_FORBIDDEN",
                        "Workbook formulas are forbidden.");
                }
            }
        }

        private void EnforceExpandedLimit(WorkbookPart workbookPart)
        {
            long total = 0;
            var visited = new HashSet<OpenXmlPart>();
            var pending = new Stack<OpenXmlPart>();
            pending.Push(workbookPart);
            while (pending.Count != 0)
            {
                OpenXmlPart part = pending.Pop();
                if (!visited.Add(part))
                {
                    continue;
                }

                using (Stream stream = part.GetStream(FileMode.Open, FileAccess.Read))
                {
                    total += stream.Length;
                }

                if (total > limits.MaximumExpandedBytes)
                {
                    throw new XlsxConfigException(
                        "XLSX_EXPANDED_LIMIT",
                        "Workbook exceeds the expanded-size limit.");
                }

                foreach (IdPartPair child in part.Parts)
                {
                    pending.Push(child.OpenXmlPart);
                }
            }
        }

        private static Sheet FindSheet(WorkbookPart workbookPart, string name)
        {
            return workbookPart.Workbook.Sheets.Elements<Sheet>()
                .Single(sheet => string.Equals(sheet.Name.Value, name, StringComparison.Ordinal));
        }

        private static void RequireMetadata(
            IReadOnlyDictionary<string, string> metadata,
            string key,
            string expected)
        {
            if (!metadata.TryGetValue(key, out string actual) ||
                !string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new XlsxConfigException(
                    "XLSX_META_MISMATCH",
                    "Workbook metadata '" + key + "' does not match the selected configuration.");
            }
        }

        private static long? ReadOrderValue(ConfigObjectNode row, string orderField)
        {
            if (string.IsNullOrEmpty(orderField))
            {
                return null;
            }

            if (!row.TryGetValue(orderField, out ConfigNode value) ||
                !(value is ConfigIntegerNode integer))
            {
                throw new XlsxConfigException(
                    "XLSX_ORDER_FIELD_INVALID",
                    "Declared order field '" + orderField + "' must contain an integer.");
            }

            return integer.Value;
        }

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
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

            public ConfigSchemaNode ArraySchema { get; }

            public IReadOnlyList<FieldDefinition> Fields { get; }

            public FieldDefinition PrimaryKey { get; }

            public TableDefinition Parent { get; }

            public int Depth => Parent == null ? 0 : Parent.Depth + 1;

            public int FieldColumnOffset => Parent == null ? 1 : 2;

            public int ColumnCount => Fields.Count + (Parent == null ? 0 : 1);
        }

        private sealed class FieldDefinition
        {
            public FieldDefinition(string name, ConfigSchemaNode schema)
            {
                Name = name;
                Schema = schema;
            }

            public string Name { get; }

            public ConfigSchemaNode Schema { get; }

            public string PointerPath => string.Join(
                "/",
                Name.Split('.').Select(EscapePointer));
        }

        private sealed class ReadRow
        {
            public ReadRow(
                ConfigObjectNode value,
                string primaryKey,
                long? order,
                int sourceRow,
                IReadOnlyList<int> presentColumns,
                string parentKey)
            {
                Value = value;
                PrimaryKey = primaryKey;
                Order = order;
                SourceRow = sourceRow;
                PresentColumns = presentColumns;
                ParentKey = parentKey;
                ChildRows = new Dictionary<TableDefinition, List<ReadRow>>();
            }

            public ConfigObjectNode Value { get; set; }

            public string PrimaryKey { get; }

            public long? Order { get; }

            public int SourceRow { get; }

            public IReadOnlyList<int> PresentColumns { get; }

            public string ParentKey { get; }

            public Dictionary<TableDefinition, List<ReadRow>> ChildRows { get; }
        }
    }

    public sealed class XlsxConfigException : Exception
    {
        public XlsxConfigException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
