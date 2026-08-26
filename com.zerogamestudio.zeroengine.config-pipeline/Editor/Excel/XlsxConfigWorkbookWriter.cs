using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class XlsxConfigWorkbookWriter
    {
        public const int WorkbookFormatVersion = 1;
        public const string NavigationSheetName = "配置目录";

        private const uint EditableCellStyle = 1U;
        private const uint EditableTextCellStyle = 9U;
        private const uint NavigationTitleStyle = 2U;
        private const uint NavigationSubtitleStyle = 3U;
        private const uint NavigationHeaderStyle = 4U;
        private const uint NavigationLinkStyle = 5U;
        private const uint NavigationBodyStyle = 6U;
        private const uint BusinessHeaderStyle = 7U;
        private const uint BusinessHeaderLinkStyle = 8U;
        private const int MaximumTableColumnNameLength = 255;
        private static readonly DateTimeOffset DeterministicPackageTimestamp =
            new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

            byte[] packageBytes;
            using (var packageStream = new MemoryStream())
            {
                using (SpreadsheetDocument workbook =
                       SpreadsheetDocument.Create(
                           packageStream,
                           SpreadsheetDocumentType.Workbook,
                           true))
                {
                    WorkbookPart workbookPart = workbook.AddWorkbookPart();
                    workbook.ChangeIdOfPart(workbookPart, "rIdWorkbook");
                    workbookPart.Workbook = new Workbook();
                    AddStyles(workbookPart);
                    var workbookView = new WorkbookView();
                    workbookPart.Workbook.AppendChild(new BookViews(workbookView));
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
                    uint navigationSheetIndex = sheetId - 1U;
                    workbookView.ActiveTab = navigationSheetIndex;
                    workbookView.FirstSheet = navigationSheetIndex;
                    AddNavigationSheet(workbookPart, sheets, tables, ref sheetId);
                    uint tableId = 1U;
                    for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
                    {
                        TableDefinition table = tables[tableIndex];
                        AddDataSheet(
                            workbookPart,
                            sheets,
                            table,
                            FindRows(document, table),
                            false,
                            ref tableId,
                            ref sheetId);
                    }

                    workbookPart.Workbook.Save();
                }

                packageBytes = packageStream.ToArray();
            }

            WriteDeterministicPackage(packageBytes, destination);
        }

        private static void WriteDeterministicPackage(byte[] packageBytes, Stream destination)
        {
            using (var sourceStream = new MemoryStream(packageBytes, false))
            using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, false))
            using (var deterministicStream = new MemoryStream())
            {
                using (var destinationArchive =
                       new ZipArchive(deterministicStream, ZipArchiveMode.Create, true))
                {
                    foreach (ZipArchiveEntry sourceEntry in
                             sourceArchive.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
                    {
                        ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(
                            sourceEntry.FullName,
                            CompressionLevel.Optimal);
                        destinationEntry.LastWriteTime = DeterministicPackageTimestamp;
                        using (Stream sourceEntryStream = sourceEntry.Open())
                        using (Stream destinationEntryStream = destinationEntry.Open())
                        {
                            sourceEntryStream.CopyTo(destinationEntryStream);
                        }
                    }
                }

                deterministicStream.Position = 0;
                deterministicStream.CopyTo(destination);
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
                !sheetNames.Add(arraySchema.Sheet) ||
                IsReservedSheetName(arraySchema.Sheet))
            {
                throw new InvalidOperationException("Every table requires a unique sheet and object items schema.");
            }

            var fields = new List<FieldDefinition>();
            AddScalarFields(arraySchema.Items, string.Empty, true, fields);
            List<FieldDefinition> primaryKeys = fields
                .Where(field => field.Schema.PrimaryKey)
                .ToList();
            if (primaryKeys.Count == 0 ||
                primaryKeys.Any(primaryKey =>
                    primaryKey.Schema.Type != ConfigSchemaType.String || primaryKey.Name.Contains(".")))
            {
                throw new InvalidOperationException(
                    "Every table requires one or more top-level string primary keys.");
            }

            if (parent != null && parent.PrimaryKeys.Count != 1)
            {
                throw new InvalidOperationException(
                    "Child tables currently require a parent table with exactly one primary key.");
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
                primaryKeys,
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
            WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>("rIdStyles");
            stylesPart.Stylesheet = new Stylesheet(
                new Fonts(
                    new Font(),
                    new Font(
                        new Bold(),
                        new FontSize { Val = 16D },
                        new Color { Rgb = "FFFFFFFF" }),
                    new Font(
                        new Bold(),
                        new Color { Rgb = "FFFFFFFF" }),
                    new Font(
                        new Underline(),
                        new Color { Rgb = "FF0563C1" }),
                    new Font(new Color { Rgb = "FF666666" }),
                    new Font(
                        new Underline(),
                        new Color { Rgb = "FFFFFFFF" }))
                {
                    Count = 6
                },
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    SolidFill("FF1F4E78"),
                    SolidFill("FF5B9BD5"))
                {
                    Count = 4
                },
                new Borders(
                    new Border(),
                    new Border(
                        new LeftBorder
                        {
                            Style = BorderStyleValues.Thin,
                            Color = new Color { Rgb = "FFD9E2F3" }
                        },
                        new RightBorder
                        {
                            Style = BorderStyleValues.Thin,
                            Color = new Color { Rgb = "FFD9E2F3" }
                        },
                        new TopBorder
                        {
                            Style = BorderStyleValues.Thin,
                            Color = new Color { Rgb = "FFD9E2F3" }
                        },
                        new BottomBorder
                        {
                            Style = BorderStyleValues.Thin,
                            Color = new Color { Rgb = "FFD9E2F3" }
                        },
                        new DiagonalBorder()))
                {
                    Count = 2
                },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(
                    new CellFormat(),
                    new CellFormat
                    {
                        ApplyProtection = true,
                        Protection = new Protection { Locked = false }
                    },
                    new CellFormat
                    {
                        FontId = 1,
                        FillId = 2,
                        ApplyFont = true,
                        ApplyFill = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Vertical = VerticalAlignmentValues.Center
                        }
                    },
                    new CellFormat
                    {
                        FontId = 4,
                        ApplyFont = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Vertical = VerticalAlignmentValues.Center
                        }
                    },
                    new CellFormat
                    {
                        FontId = 2,
                        FillId = 3,
                        BorderId = 1,
                        ApplyFont = true,
                        ApplyFill = true,
                        ApplyBorder = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Horizontal = HorizontalAlignmentValues.Center,
                            Vertical = VerticalAlignmentValues.Center
                        }
                    },
                    new CellFormat
                    {
                        FontId = 3,
                        BorderId = 1,
                        ApplyFont = true,
                        ApplyBorder = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Vertical = VerticalAlignmentValues.Center
                        }
                    },
                    new CellFormat
                    {
                        BorderId = 1,
                        ApplyBorder = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Vertical = VerticalAlignmentValues.Center,
                            WrapText = true
                        }
                    },
                    new CellFormat
                    {
                        FontId = 2,
                        FillId = 2,
                        BorderId = 1,
                        ApplyFont = true,
                        ApplyFill = true,
                        ApplyBorder = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Horizontal = HorizontalAlignmentValues.Center,
                            Vertical = VerticalAlignmentValues.Center,
                            WrapText = true
                        }
                    },
                    new CellFormat
                    {
                        FontId = 5,
                        FillId = 2,
                        BorderId = 1,
                        ApplyFont = true,
                        ApplyFill = true,
                        ApplyBorder = true,
                        ApplyAlignment = true,
                        Alignment = new Alignment
                        {
                            Horizontal = HorizontalAlignmentValues.Left,
                            Vertical = VerticalAlignmentValues.Center,
                            WrapText = true
                        }
                    },
                    new CellFormat
                    {
                        NumberFormatId = 49U,
                        ApplyNumberFormat = true,
                        ApplyProtection = true,
                        Protection = new Protection { Locked = false }
                    })
                {
                    Count = 10
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

        private static Fill SolidFill(string color)
        {
            return new Fill(
                new PatternFill(
                    new ForegroundColor { Rgb = color },
                    new BackgroundColor { Indexed = 64U })
                {
                    PatternType = PatternValues.Solid
                });
        }

        private static void AddSchemaSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            ConfigSchema schema,
            IReadOnlyList<TableDefinition> tables,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = AddWorksheetPart(workbookPart, sheetId);
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
                State = SheetStateValues.VeryHidden
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
            WorksheetPart worksheetPart = AddWorksheetPart(workbookPart, sheetId);
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
            WorksheetPart worksheetPart = AddWorksheetPart(workbookPart, sheetId);
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

        private static void AddNavigationSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            IReadOnlyList<TableDefinition> tables,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = AddWorksheetPart(workbookPart, sheetId);
            var sheetViews = new SheetViews(
                new SheetView(
                    new Pane
                    {
                        VerticalSplit = 4D,
                        TopLeftCell = "A5",
                        ActivePane = PaneValues.BottomLeft,
                        State = PaneStateValues.Frozen
                    })
                {
                    WorkbookViewId = 0U,
                    ShowGridLines = false,
                    TabSelected = true,
                    ZoomScale = 95U,
                    ZoomScaleNormal = 100U
                });
            var columns = new Columns(
                new Column { Min = 1, Max = 1, Width = 24, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 28, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 56, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 12, CustomWidth = true });
            var sheetData = new SheetData();

            var titleRow = new Row
            {
                RowIndex = 1U,
                Height = 30D,
                CustomHeight = true
            };
            Cell titleCell = TextCell("配置目录", NavigationTitleStyle);
            titleCell.CellReference = "A1";
            titleRow.Append(titleCell);
            sheetData.Append(titleRow);

            var subtitleRow = new Row
            {
                RowIndex = 2U,
                Height = 24D,
                CustomHeight = true
            };
            Cell subtitleCell = TextCell(
                "点击 Sheet 名称进入配置；保存后回 Unity 执行 Check / Plan / Apply。",
                NavigationSubtitleStyle);
            subtitleCell.CellReference = "A2";
            subtitleRow.Append(subtitleCell);
            sheetData.Append(subtitleRow);

            var headerRow = new Row
            {
                RowIndex = 4U,
                Height = 22D,
                CustomHeight = true
            };
            string[] headers = { "配置模块", "Sheet", "说明", "字段数" };
            for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
            {
                Cell cell = TextCell(headers[columnIndex], NavigationHeaderStyle);
                cell.CellReference = ColumnName(columnIndex + 1) + "4";
                headerRow.Append(cell);
            }

            sheetData.Append(headerRow);
            var hyperlinks = new Hyperlinks();
            for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
            {
                TableDefinition table = tables[tableIndex];
                uint rowIndex = (uint)tableIndex + 5U;
                var row = new Row
                {
                    RowIndex = rowIndex,
                    Height = 21D,
                    CustomHeight = true
                };
                Cell moduleCell = TextCell(
                    table.ArraySchema.Title ?? table.PropertyName,
                    NavigationBodyStyle);
                moduleCell.CellReference = "A" + rowIndex.ToString(CultureInfo.InvariantCulture);
                row.Append(moduleCell);
                Cell sheetCell = TextCell(table.SheetName, NavigationLinkStyle);
                sheetCell.CellReference = "B" + rowIndex.ToString(CultureInfo.InvariantCulture);
                row.Append(sheetCell);
                Cell descriptionCell = TextCell(
                    table.ArraySchema.Description ?? string.Empty,
                    NavigationBodyStyle);
                descriptionCell.CellReference = "C" + rowIndex.ToString(CultureInfo.InvariantCulture);
                row.Append(descriptionCell);
                Cell fieldCountCell = NumberCell(table.ColumnCount, NavigationBodyStyle);
                fieldCountCell.CellReference = "D" + rowIndex.ToString(CultureInfo.InvariantCulture);
                row.Append(fieldCountCell);
                sheetData.Append(row);
                hyperlinks.Append(new Hyperlink
                {
                    Reference = sheetCell.CellReference,
                    Location = "'" + table.SheetName.Replace("'", "''") + "'!A2",
                    Display = table.SheetName
                });
            }

            var worksheet = new Worksheet(sheetViews, columns, sheetData);
            worksheet.Append(new SheetProtection
            {
                Sheet = true,
                Objects = true,
                Scenarios = true,
                Sort = false,
                AutoFilter = false
            });
            uint lastDirectoryRow = (uint)Math.Max(4, tables.Count + 4);
            worksheet.Append(new AutoFilter
            {
                Reference = "A4:D" + lastDirectoryRow.ToString(CultureInfo.InvariantCulture)
            });
            worksheet.Append(
                new MergeCells(
                    new MergeCell { Reference = "A1:D1" },
                    new MergeCell { Reference = "A2:D2" })
                {
                    Count = 2U
                });
            worksheet.Append(hyperlinks);
            worksheetPart.Worksheet = worksheet;
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = NavigationSheetName,
                State = SheetStateValues.Visible
            });
        }

        private static bool IsReservedSheetName(string sheetName)
        {
            return string.Equals(sheetName, "_zgs_schema", StringComparison.Ordinal) ||
                   string.Equals(sheetName, "_zgs_meta", StringComparison.Ordinal) ||
                   string.Equals(sheetName, "_zgs_lists", StringComparison.Ordinal) ||
                   string.Equals(sheetName, NavigationSheetName, StringComparison.Ordinal);
        }

        private static void AddDataSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            TableDefinition table,
            IReadOnlyList<TableRow> rows,
            bool selected,
            ref uint tableId,
            ref uint sheetId)
        {
            WorksheetPart worksheetPart = AddWorksheetPart(workbookPart, sheetId);
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
                    WorkbookViewId = 0U,
                    TabSelected = selected,
                    ShowGridLines = false,
                    ZoomScale = 90U,
                    ZoomScaleNormal = 100U
                });
            var columns = new Columns();
            for (int columnIndex = 0; columnIndex < table.ColumnCount; columnIndex++)
            {
                columns.Append(new Column
                {
                    Min = (uint)columnIndex + 1U,
                    Max = (uint)columnIndex + 1U,
                    Style = EditableStyleForColumn(table, columnIndex),
                    Width = SuggestedColumnWidth(table, columnIndex),
                    CustomWidth = true
                });
            }

            var sheetData = new SheetData();
            var machineHeader = new Row { RowIndex = 1U, Hidden = true };
            var titleHeader = new Row
            {
                RowIndex = 2U,
                Height = 28D,
                CustomHeight = true
            };
            var displayHeaders = new List<string>();
            var usedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int columnIndex = 0; columnIndex < table.ColumnCount; columnIndex++)
            {
                bool parentColumn = table.Parent != null && columnIndex == 0;
                FieldDefinition field = parentColumn
                    ? null
                    : table.Fields[columnIndex - (table.Parent == null ? 0 : 1)];
                string fieldName = parentColumn ? table.ArraySchema.ParentKey : field.Name;
                string fieldTitle = parentColumn ? table.ArraySchema.ParentKey : field.Schema.Title ?? field.Name;
                if (parentColumn || field.Required)
                {
                    fieldTitle = "＊ " + fieldTitle;
                }

                Cell machineCell = TextCell(fieldName, 0);
                machineCell.CellReference = ColumnName(columnIndex + 1) + "1";
                machineHeader.Append(machineCell);
                if (columnIndex == 0)
                {
                    fieldTitle = "← 配置目录 ｜ " + fieldTitle;
                }

                fieldTitle = UniqueDisplayHeader(fieldTitle, fieldName, usedHeaders);
                displayHeaders.Add(fieldTitle);
                Cell titleCell = TextCell(
                    fieldTitle,
                    columnIndex == 0 ? BusinessHeaderLinkStyle : BusinessHeaderStyle);
                titleCell.CellReference = ColumnName(columnIndex + 1) + "2";
                titleHeader.Append(titleCell);
            }

            sheetData.Append(machineHeader);
            sheetData.Append(titleHeader);
            uint rowIndex = 3U;
            if (rows != null)
            {
                foreach (TableRow tableRow in rows)
                {
                    var row = new Row { RowIndex = rowIndex++ };
                    if (table.Parent != null)
                    {
                        Cell parentCell = TextCell(tableRow.ParentKey, EditableTextCellStyle);
                        parentCell.CellReference = "A" + (rowIndex - 1).ToString(CultureInfo.InvariantCulture);
                        row.Append(parentCell);
                    }

                    for (int fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
                    {
                        FieldDefinition field = table.Fields[fieldIndex];
                        uint styleIndex = EditableStyleForSchema(field.Schema);
                        Cell cell = TryGetPath(tableRow.Value, field.Name, out ConfigNode value)
                                 ? ValueCell(value, styleIndex)
                                 : new Cell { StyleIndex = styleIndex };
                        cell.CellReference =
                            ColumnName(fieldIndex + table.FieldColumnOffset) +
                            (rowIndex - 1).ToString(CultureInfo.InvariantCulture);
                        row.Append(cell);
                    }

                    sheetData.Append(row);
                }
            }

            if (rowIndex == 3U)
            {
                var blankAuthoringRow = new Row { RowIndex = rowIndex++ };
                for (int columnIndex = 0; columnIndex < table.ColumnCount; columnIndex++)
                {
                    blankAuthoringRow.Append(new Cell
                    {
                        CellReference = ColumnName(columnIndex + 1) + "3",
                        StyleIndex = EditableStyleForColumn(table, columnIndex)
                    });
                }

                sheetData.Append(blankAuthoringRow);
            }

            string lastColumn = ColumnName(table.ColumnCount);
            uint lastDataRow = rowIndex - 1U;
            var worksheet = new Worksheet(sheetViews, columns, sheetData);
            var validations = new DataValidations();
            for (int index = 0; index < table.Fields.Count; index++)
            {
                FieldDefinition field = table.Fields[index];
                DataValidation validation = CreateFieldValidation(table, field, index);
                if (validation == null)
                {
                    continue;
                }

                validations.Append(validation);
            }

            if (validations.HasChildren)
            {
                validations.Count = (uint)validations.ChildElements.Count;
                worksheet.Append(validations);
            }

            worksheet.Append(
                new Hyperlinks(
                    new Hyperlink
                    {
                        Reference = "A2",
                        Location = "'" + NavigationSheetName + "'!A1",
                        Display = "← 配置目录"
                    }));
            AddBusinessTable(
                worksheetPart,
                worksheet,
                table,
                displayHeaders,
                lastColumn,
                lastDataRow,
                ref tableId);

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

        private static DataValidation CreateFieldValidation(
            TableDefinition table,
            FieldDefinition field,
            int fieldIndex)
        {
            string columnName = ColumnName(fieldIndex + table.FieldColumnOffset);
            var validation = new DataValidation
            {
                AllowBlank = !field.Required,
                ShowErrorMessage = true,
                ShowInputMessage = true,
                ErrorStyle = DataValidationErrorStyleValues.Stop,
                ErrorTitle = "配置值无效",
                Error = "请按字段约束输入；保存后仍需执行 Check。",
                PromptTitle = ValidationTitle(field),
                SequenceOfReferences = new ListValue<StringValue>
                {
                    InnerText = columnName + "3:" + columnName + "1048576"
                }
            };

            if (field.Schema.EnumValues.Count != 0)
            {
                validation.Type = DataValidationValues.List;
                validation.Prompt = field.Required ? "必填；请从下拉选项选择。" : "请从下拉选项选择。";
                validation.Formula1 = new Formula1("=" + EnumRangeName(table.SheetName, field.Name));
                return validation;
            }

            if (field.Schema.Type == ConfigSchemaType.Boolean)
            {
                validation.Type = DataValidationValues.List;
                validation.Prompt = field.Required ? "必填；请选择 TRUE 或 FALSE。" : "请选择 TRUE 或 FALSE。";
                validation.Formula1 = new Formula1("\"TRUE,FALSE\"");
                return validation;
            }

            bool integer = field.Schema.Type == ConfigSchemaType.Integer;
            if (!integer && field.Schema.Type != ConfigSchemaType.Number)
            {
                return null;
            }

            double? minimum = field.Schema.ExclusiveMinimum ?? field.Schema.Minimum;
            double? maximum = field.Schema.ExclusiveMaximum ?? field.Schema.Maximum;
            bool minimumExclusive = field.Schema.ExclusiveMinimum.HasValue;
            bool maximumExclusive = field.Schema.ExclusiveMaximum.HasValue;
            validation.Prompt = field.Required ? "必填；请输入符合范围的数值。" : "请输入符合范围的数值。";
            if (!minimum.HasValue && !maximum.HasValue)
            {
                if (!integer)
                {
                    return null;
                }

                return CreateCustomNumericValidation(
                    validation,
                    columnName,
                    true,
                    null,
                    false,
                    null,
                    false,
                    field.Required);
            }

            if (minimum.HasValue && maximum.HasValue &&
                (minimumExclusive || maximumExclusive))
            {
                return CreateCustomNumericValidation(
                    validation,
                    columnName,
                    integer,
                    minimum,
                    minimumExclusive,
                    maximum,
                    maximumExclusive,
                    field.Required);
            }

            validation.Type = integer ? DataValidationValues.Whole : DataValidationValues.Decimal;
            if (minimum.HasValue && maximum.HasValue)
            {
                validation.Operator = DataValidationOperatorValues.Between;
                validation.Formula1 = new Formula1(CanonicalNumberWriter.Write(minimum.Value));
                validation.Formula2 = new Formula2(CanonicalNumberWriter.Write(maximum.Value));
            }
            else if (minimum.HasValue)
            {
                validation.Operator = minimumExclusive
                    ? DataValidationOperatorValues.GreaterThan
                    : DataValidationOperatorValues.GreaterThanOrEqual;
                validation.Formula1 = new Formula1(CanonicalNumberWriter.Write(minimum.Value));
            }
            else
            {
                validation.Operator = maximumExclusive
                    ? DataValidationOperatorValues.LessThan
                    : DataValidationOperatorValues.LessThanOrEqual;
                validation.Formula1 = new Formula1(CanonicalNumberWriter.Write(maximum.Value));
            }

            return validation;
        }

        private static DataValidation CreateCustomNumericValidation(
            DataValidation validation,
            string columnName,
            bool integer,
            double? minimum,
            bool minimumExclusive,
            double? maximum,
            bool maximumExclusive,
            bool required)
        {
            string cell = columnName + "3";
            var conditions = new List<string> { "ISNUMBER(" + cell + ")" };
            if (integer)
            {
                conditions.Add("MOD(" + cell + ",1)=0");
            }

            if (minimum.HasValue)
            {
                conditions.Add(
                    cell + (minimumExclusive ? ">" : ">=") + CanonicalNumberWriter.Write(minimum.Value));
            }

            if (maximum.HasValue)
            {
                conditions.Add(
                    cell + (maximumExclusive ? "<" : "<=") + CanonicalNumberWriter.Write(maximum.Value));
            }

            string predicate = "AND(" + string.Join(",", conditions) + ")";
            validation.Type = DataValidationValues.Custom;
            validation.Formula1 = new Formula1(
                required ? "=" + predicate : "=OR(" + cell + "=\"\"," + predicate + ")");
            return validation;
        }

        private static string ValidationTitle(FieldDefinition field)
        {
            string title = field.Schema.Title ?? field.Name;
            return title.Length <= 30 ? title : title.Substring(0, 30);
        }

        private static void AddBusinessTable(
            WorksheetPart worksheetPart,
            Worksheet worksheet,
            TableDefinition definition,
            IReadOnlyList<string> displayHeaders,
            string lastColumn,
            uint lastDataRow,
            ref uint tableId)
        {
            string reference = "A2:" + lastColumn + lastDataRow.ToString(CultureInfo.InvariantCulture);
            uint currentTableId = tableId++;
            string tableName = BusinessTableName(definition.SheetName, currentTableId);
            var tableColumns = new TableColumns { Count = (uint)displayHeaders.Count };
            for (int columnIndex = 0; columnIndex < displayHeaders.Count; columnIndex++)
            {
                tableColumns.Append(new TableColumn
                {
                    Id = (uint)columnIndex + 1U,
                    Name = displayHeaders[columnIndex]
                });
            }

            TableDefinitionPart tablePart = worksheetPart.AddNewPart<TableDefinitionPart>(
                "rIdTable" + currentTableId.ToString(CultureInfo.InvariantCulture));
            tablePart.Table = new Table(
                new AutoFilter { Reference = reference },
                tableColumns,
                new TableStyleInfo
                {
                    Name = "TableStyleMedium2",
                    ShowFirstColumn = false,
                    ShowLastColumn = false,
                    ShowRowStripes = true,
                    ShowColumnStripes = false
                })
            {
                Id = currentTableId,
                Name = tableName,
                DisplayName = tableName,
                Reference = reference,
                TotalsRowShown = false
            };
            tablePart.Table.Save();
            worksheet.Append(
                new TableParts(
                    new TablePart { Id = worksheetPart.GetIdOfPart(tablePart) })
                {
                    Count = 1U
                });
        }

        private static string BusinessTableName(string sheetName, uint tableId)
        {
            var builder = new StringBuilder("ZGS_");
            foreach (char value in sheetName)
            {
                builder.Append(char.IsLetterOrDigit(value) || value == '_' ? value : '_');
            }

            builder.Append('_').Append(tableId.ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static WorksheetPart AddWorksheetPart(WorkbookPart workbookPart, uint sheetId)
        {
            return workbookPart.AddNewPart<WorksheetPart>(
                "rIdSheet" + sheetId.ToString(CultureInfo.InvariantCulture));
        }

        private static string UniqueDisplayHeader(
            string title,
            string fieldName,
            ISet<string> usedHeaders)
        {
            string candidate = FitDisplayHeader(title, string.Empty);
            if (usedHeaders.Add(candidate))
            {
                return candidate;
            }

            candidate = FitDisplayHeader(title, " (" + fieldName + ")");
            if (usedHeaders.Add(candidate))
            {
                return candidate;
            }

            int suffix = 2;
            while (!usedHeaders.Add(candidate))
            {
                candidate = FitDisplayHeader(
                    title,
                    " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")");
                suffix++;
            }

            return candidate;
        }

        private static string FitDisplayHeader(string title, string suffix)
        {
            string safeTitle = title ?? string.Empty;
            string safeSuffix = suffix ?? string.Empty;
            if (safeSuffix.Length >= MaximumTableColumnNameLength)
            {
                return TruncateText(safeSuffix, MaximumTableColumnNameLength);
            }

            return TruncateText(
                       safeTitle,
                       MaximumTableColumnNameLength - safeSuffix.Length) +
                   safeSuffix;
        }

        private static string TruncateText(string value, int maximumLength)
        {
            if (value.Length <= maximumLength)
            {
                return value;
            }

            int length = maximumLength;
            if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            {
                length--;
            }

            return value.Substring(0, length);
        }

        private static uint EditableStyleForColumn(TableDefinition table, int columnIndex)
        {
            if (table.Parent != null && columnIndex == 0)
            {
                return EditableTextCellStyle;
            }

            int fieldIndex = columnIndex - (table.Parent == null ? 0 : 1);
            return EditableStyleForSchema(table.Fields[fieldIndex].Schema);
        }

        private static uint EditableStyleForSchema(ConfigSchemaNode schema)
        {
            return schema.Type == ConfigSchemaType.String
                ? EditableTextCellStyle
                : EditableCellStyle;
        }

        private static double SuggestedColumnWidth(TableDefinition table, int columnIndex)
        {
            bool parentColumn = table.Parent != null && columnIndex == 0;
            if (parentColumn)
            {
                return 28D;
            }

            FieldDefinition field = table.Fields[columnIndex - (table.Parent == null ? 0 : 1)];
            if (field.Schema.PrimaryKey)
            {
                return 28D;
            }

            if (field.Schema.Type == ConfigSchemaType.Boolean)
            {
                return 12D;
            }

            if (field.Schema.Type == ConfigSchemaType.Integer ||
                field.Schema.Type == ConfigSchemaType.Number)
            {
                return 15D;
            }

            if (field.Schema.EnumValues.Count != 0)
            {
                return 20D;
            }

            return string.IsNullOrEmpty(field.Schema.Description) ? 22D : 28D;
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

        private static Cell NumberCell(int value, uint styleIndex)
        {
            return new Cell
            {
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
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
                        table.Parent.PrimaryKeys[0].Name,
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
                IReadOnlyList<FieldDefinition> primaryKeys,
                TableDefinition parent)
            {
                RootPropertyName = rootPropertyName;
                PropertyName = propertyName;
                SheetName = sheetName;
                ArraySchema = arraySchema;
                Fields = fields;
                PrimaryKeys = primaryKeys;
                Parent = parent;
            }

            public string RootPropertyName { get; }

            public string PropertyName { get; }

            public string SheetName { get; }

            public IReadOnlyList<FieldDefinition> Fields { get; }

            public ConfigSchemaNode ItemSchema => ArraySchema.Items;

            public ConfigSchemaNode ArraySchema { get; }

            public IReadOnlyList<FieldDefinition> PrimaryKeys { get; }

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
