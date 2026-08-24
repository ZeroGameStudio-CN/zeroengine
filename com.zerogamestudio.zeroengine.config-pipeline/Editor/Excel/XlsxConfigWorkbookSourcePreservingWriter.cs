using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    /// <summary>
    /// Updates the pipeline-owned table ranges in a copy of an authoring workbook.
    /// The source package remains the base package so VBA, code names, defined names,
    /// drawings, comments and other designer-owned parts are retained.
    /// </summary>
    internal static class XlsxConfigWorkbookSourcePreservingWriter
    {
        private static readonly HashSet<string> PipelineSheets =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "_zgs_schema",
                "_zgs_meta",
                "_zgs_lists",
                XlsxConfigWorkbookWriter.NavigationSheetName
            };
        private const string PipelineEnumDefinedNamePrefix = "ZGS_ENUM_";

        public static void WriteCandidate(
            string sourcePath,
            string destinationPath,
            ConfigSchema schema,
            string configSetId,
            ConfigDocument document,
            string workbookBaseHash,
            IEnumerable<string> ownedRootProperties,
            IEnumerable<ConfigAuthoringSheetProfile> authoringSheets,
            bool macroEnabled)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Source workbook path is required.", nameof(sourcePath));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException(
                    "Destination workbook path is required.",
                    nameof(destinationPath));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Source workbook was not found.", sourcePath);
            }

            string destination = Path.GetFullPath(destinationPath);
            string source = Path.GetFullPath(sourcePath);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A source-preserving candidate cannot overwrite its source workbook.");
            }

            using (SpreadsheetDocument sourceWorkbook =
                   SpreadsheetDocument.Open(source, false))
            {
                XlsxConfigSourceReader.ValidateSafeAuthoringPackage(
                    sourceWorkbook,
                    macroEnabled,
                    sourcePath);
            }

            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    "Destination workbook must have a parent directory.");
            }

            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(
                parent,
                "." + Path.GetFileName(destination) + ".writing." +
                Guid.NewGuid().ToString("N"));
            File.Copy(source, temporary, false);
            try
            {
                using (var template = new MemoryStream())
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        template,
                        schema,
                        configSetId,
                        document,
                        workbookBaseHash,
                        ownedRootProperties,
                        authoringSheets,
                        macroEnabled);
                    template.Position = 0;

                    using (SpreadsheetDocument sourceWorkbook =
                           SpreadsheetDocument.Open(temporary, true))
                    using (SpreadsheetDocument generatedWorkbook =
                           SpreadsheetDocument.Open(template, false))
                    {
                        XlsxConfigSourceReader.ValidateSafeAuthoringPackage(
                            sourceWorkbook,
                            macroEnabled,
                            sourcePath);
                        MergeWorkbook(sourceWorkbook, generatedWorkbook);
                    }
                }

                if (File.Exists(destination))
                {
                    PromoteOverExisting(temporary, destination);
                }
                else
                {
                    File.Move(temporary, destination);
                }
            }
            catch
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                throw;
            }
        }

        private static void PromoteOverExisting(
            string temporary,
            string destination)
        {
            try
            {
                File.Replace(temporary, destination, null);
                return;
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException ||
                exception is PlatformNotSupportedException)
            {
                // Some supported filesystems deny Replace even when same-directory
                // renames are available. Keep the reviewed destination recoverable.
            }

            string backup = destination + ".replacing." + Guid.NewGuid().ToString("N");
            File.Move(destination, backup);
            try
            {
                File.Move(temporary, destination);
                File.Delete(backup);
            }
            catch
            {
                if (File.Exists(destination) && !File.Exists(temporary))
                {
                    File.Move(destination, temporary);
                }

                if (!File.Exists(destination) && File.Exists(backup))
                {
                    File.Move(backup, destination);
                }

                throw;
            }
        }

        private static void MergeWorkbook(
            SpreadsheetDocument sourceWorkbook,
            SpreadsheetDocument generatedWorkbook)
        {
            WorkbookPart sourcePart = sourceWorkbook.WorkbookPart ??
                                       throw new InvalidDataException(
                                           "Source workbook part is missing.");
            WorkbookPart generatedPart = generatedWorkbook.WorkbookPart ??
                                         throw new InvalidDataException(
                                             "Generated workbook part is missing.");

            XlsxConfigSourceReader.ValidateWorkbookTableIds(
                sourceWorkbook,
                null);
            XlsxConfigSourceReader.ValidateWorkbookTableIds(
                generatedWorkbook,
                null);
            AuthoringFidelitySnapshot fidelity = AuthoringFidelitySnapshot.Capture(
                sourcePart,
                generatedPart);
            ValidateGeneratedStyleIndexes(sourcePart, generatedPart);

            Dictionary<string, WorksheetPart> sourceSheets = WorksheetMap(sourcePart);
            Dictionary<string, WorksheetPart> generatedSheets = WorksheetMap(generatedPart);
            string removedBusinessSheet = sourceSheets.Keys.FirstOrDefault(name =>
                !PipelineSheets.Contains(name) && !generatedSheets.ContainsKey(name));
            if (!string.IsNullOrEmpty(removedBusinessSheet))
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_REMOVED_SHEET_REQUIRES_MIGRATION: " +
                    removedBusinessSheet);
            }

            foreach (KeyValuePair<string, WorksheetPart> generatedSheet in generatedSheets)
            {
                if (!sourceSheets.TryGetValue(generatedSheet.Key, out WorksheetPart sourceSheet))
                {
                    if (PipelineSheets.Contains(generatedSheet.Key))
                    {
                        throw new InvalidDataException(
                            "Source workbook is missing pipeline worksheet '" +
                            generatedSheet.Key + "'.");
                    }

                    AddGeneratedBusinessSheet(
                        sourcePart,
                        generatedPart,
                        generatedSheet.Key,
                        generatedSheet.Value);
                    continue;
                }

                if (PipelineSheets.Contains(generatedSheet.Key))
                {
                    // These sheets are pipeline-owned and have no worksheet relationships.
                    // Replacing only their XML keeps all source workbook parts intact.
                    string codeName = CodeName(sourceSheet);
                    Worksheet replacement =
                        (Worksheet)generatedSheet.Value.Worksheet.CloneNode(true);
                    RestoreCodeName(replacement, codeName);
                    sourceSheet.Worksheet = replacement;
                    sourceSheet.Worksheet.Save();
                    continue;
                }

                MergeManagedTables(sourcePart, sourceSheet, generatedSheet.Value);
            }

            MergePipelineDefinedNames(sourcePart, generatedPart);
            fidelity.Verify(sourcePart);
            sourcePart.Workbook.Save();
        }

        private static void AddGeneratedBusinessSheet(
            WorkbookPart sourcePart,
            WorkbookPart generatedPart,
            string sheetName,
            WorksheetPart generatedSheet)
        {
            if (generatedSheet.Parts.Any(pair =>
                    !(pair.OpenXmlPart is TableDefinitionPart)) ||
                generatedSheet.ExternalRelationships.Any() ||
                generatedSheet.HyperlinkRelationships.Any())
            {
                throw new InvalidDataException(
                    "Generated worksheet contains unsupported authoring relationships: " +
                    sheetName);
            }

            WorksheetPart addedSheet = sourcePart.AddNewPart<WorksheetPart>();
            Worksheet worksheet = (Worksheet)generatedSheet.Worksheet.CloneNode(true);
            var relationshipIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TableDefinitionPart generatedTable in
                     generatedSheet.TableDefinitionParts)
            {
                TableDefinitionPart addedTable =
                    addedSheet.AddNewPart<TableDefinitionPart>();
                addedTable.Table = (Table)generatedTable.Table.CloneNode(true);
                addedTable.Table.Id = NextAvailableTableId(sourcePart);
                addedTable.Table.Save();
                relationshipIds.Add(
                    generatedSheet.GetIdOfPart(generatedTable),
                    addedSheet.GetIdOfPart(addedTable));
            }

            TableParts tableParts = worksheet.GetFirstChild<TableParts>();
            if (tableParts != null)
            {
                foreach (TablePart tablePart in tableParts.Elements<TablePart>())
                {
                    if (string.IsNullOrEmpty(tablePart.Id?.Value) ||
                        !relationshipIds.TryGetValue(
                            tablePart.Id.Value,
                            out string relationshipId))
                    {
                        throw new InvalidDataException(
                            "Generated worksheet contains an invalid table relationship: " +
                            sheetName);
                    }

                    tablePart.Id = relationshipId;
                }
            }

            addedSheet.Worksheet = worksheet;
            addedSheet.Worksheet.Save();
            Sheet generatedReference = generatedPart.Workbook.Sheets.Elements<Sheet>()
                .Single(sheet => string.Equals(
                    sheet.Id?.Value,
                    generatedPart.GetIdOfPart(generatedSheet),
                    StringComparison.Ordinal));
            uint nextSheetId = sourcePart.Workbook.Sheets.Elements<Sheet>()
                .Select(sheet => sheet.SheetId?.Value ?? 0U)
                .DefaultIfEmpty(0U)
                .Max() + 1U;
            sourcePart.Workbook.Sheets.Append(new Sheet
            {
                Id = sourcePart.GetIdOfPart(addedSheet),
                SheetId = nextSheetId,
                Name = sheetName,
                State = generatedReference.State
            });
        }

        private static Dictionary<string, WorksheetPart> WorksheetMap(WorkbookPart workbookPart)
        {
            var result = new Dictionary<string, WorksheetPart>(StringComparer.Ordinal);
            Sheets sheets = workbookPart.Workbook.Sheets ??
                            throw new InvalidDataException("Workbook sheets are missing.");
            foreach (Sheet sheet in sheets.Elements<Sheet>())
            {
                if (string.IsNullOrEmpty(sheet.Name?.Value) ||
                    string.IsNullOrEmpty(sheet.Id?.Value))
                {
                    throw new InvalidDataException("Workbook contains an invalid sheet reference.");
                }

                WorksheetPart worksheet = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
                if (worksheet == null || result.ContainsKey(sheet.Name.Value))
                {
                    throw new InvalidDataException(
                        "Workbook contains an invalid or duplicate worksheet.");
                }

                result.Add(sheet.Name.Value, worksheet);
            }

            return result;
        }

        private static void MergeManagedTables(
            WorkbookPart sourcePart,
            WorksheetPart sourceSheet,
            WorksheetPart generatedSheet)
        {
            List<ManagedTableState> sourceTables = ManagedTableStates(sourceSheet);
            List<ManagedTableState> generatedTables = ManagedTableStates(generatedSheet);
            var matches = new Dictionary<ManagedTableState, ManagedTableState>();
            var usedSourceTables = new HashSet<ManagedTableState>();

            foreach (ManagedTableState generatedTable in generatedTables)
            {
                ManagedTableState exact = sourceTables.SingleOrDefault(sourceTable =>
                    !usedSourceTables.Contains(sourceTable) &&
                    string.Equals(
                        sourceTable.Name,
                        generatedTable.Name,
                        StringComparison.Ordinal));
                if (exact != null)
                {
                    matches.Add(generatedTable, exact);
                    usedSourceTables.Add(exact);
                }
            }

            foreach (ManagedTableState generatedTable in generatedTables.Where(
                         table => !matches.ContainsKey(table)))
            {
                List<ManagedTableState> logicalMatches = sourceTables.Where(sourceTable =>
                        !usedSourceTables.Contains(sourceTable) &&
                        string.Equals(
                            LogicalTableName(sourceTable.Name),
                            LogicalTableName(generatedTable.Name),
                            StringComparison.Ordinal))
                    .ToList();
                if (logicalMatches.Count == 1)
                {
                    matches.Add(generatedTable, logicalMatches[0]);
                    usedSourceTables.Add(logicalMatches[0]);
                }
            }

            foreach (ManagedTableState generatedTable in generatedTables.Where(
                         table => !matches.ContainsKey(table)))
            {
                List<ManagedTableState> placementMatches = sourceTables.Where(sourceTable =>
                        !usedSourceTables.Contains(sourceTable) &&
                        sourceTable.FirstColumn == generatedTable.FirstColumn)
                    .ToList();
                if (placementMatches.Count == 1)
                {
                    matches.Add(generatedTable, placementMatches[0]);
                    usedSourceTables.Add(placementMatches[0]);
                }
            }

            var targets = new List<ManagedTableTarget>();
            foreach (KeyValuePair<ManagedTableState, ManagedTableState> match in matches)
            {
                targets.Add(new ManagedTableTarget(match.Key, match.Value));
            }

            int[] existingRowOffsets = targets.Select(target => target.RowOffset)
                .Distinct()
                .ToArray();
            if (existingRowOffsets.Length > 1 &&
                generatedTables.Any(table => !matches.ContainsKey(table)))
            {
                throw new InvalidDataException(
                    "New pipeline tables cannot be placed because existing tables use " +
                    "different authoring row offsets.");
            }

            int newTableRowOffset = existingRowOffsets.Length == 1
                ? existingRowOffsets[0]
                : 0;
            foreach (ManagedTableState generatedTable in generatedTables.Where(
                         table => !matches.ContainsKey(table)))
            {
                targets.Add(new ManagedTableTarget(
                    generatedTable,
                    null,
                    newTableRowOffset));
            }

            if (targets.Any(target => target.ColumnOffset != 0))
            {
                throw new InvalidDataException(
                    "Pipeline table column relocation requires an explicit workbook migration.");
            }

            EnsureManagedTargetsAreSafe(sourcePart, sourceSheet, targets);
            var sourcePlacements = targets.Where(target => target.Source != null)
                .Select(target =>
                    new ManagedTablePlacement(
                        target.Source.FirstColumn,
                        target.Source.FirstRow,
                        target.Source.LastColumn,
                        target.Source.FirstColumn,
                        target.Source.FirstRow,
                        target.Source.LastColumn))
                .ToList();

            foreach (ManagedTableState removedTable in sourceTables.Where(
                         table => !usedSourceTables.Contains(table)))
            {
                DetachManagedTablePreservingCells(sourceSheet, removedTable);
            }

            foreach (ManagedTableTarget target in targets)
            {
                ReplaceManagedCells(
                    sourceSheet,
                    generatedSheet,
                    target.FirstColumn,
                    target.ManagedFirstRow,
                    Math.Max(
                        target.Source?.LastColumn ?? target.LastColumn,
                        target.LastColumn),
                    target.Source?.LastRow ?? target.LastRow,
                    target.Generated.ManagedFirstRow,
                    target.Generated.LastRow,
                    target.RowOffset);
                UpsertManagedTable(sourcePart, sourceSheet, target);
            }

            MergeManagedDataValidations(
                sourceSheet,
                generatedSheet,
                sourcePlacements,
                targets.Select(target => target.Placement).ToList());
            sourceSheet.Worksheet.Save();
        }

        private sealed class AuthoringFidelitySnapshot
        {
            private readonly byte[] vbaProject;
            private readonly Dictionary<string, string> codeNames;
            private readonly Dictionary<string, string> designerDefinedNames;
            private readonly Dictionary<string, string> designerCells;

            private AuthoringFidelitySnapshot(
                byte[] vbaProject,
                Dictionary<string, string> codeNames,
                Dictionary<string, string> designerDefinedNames,
                Dictionary<string, string> designerCells)
            {
                this.vbaProject = vbaProject;
                this.codeNames = codeNames;
                this.designerDefinedNames = designerDefinedNames;
                this.designerCells = designerCells;
            }

            public static AuthoringFidelitySnapshot Capture(
                WorkbookPart sourcePart,
                WorkbookPart generatedPart)
            {
                var generatedDefinedNames = new HashSet<string>(StringComparer.Ordinal);
                var generatedEnumNames = new HashSet<string>(StringComparer.Ordinal);
                DefinedNames generatedNames =
                    generatedPart.Workbook.GetFirstChild<DefinedNames>();
                if (generatedNames != null)
                {
                    foreach (DefinedName name in generatedNames.Elements<DefinedName>())
                    {
                        generatedDefinedNames.Add(DefinedNameKey(name));
                        if (name.Name?.Value?.StartsWith(
                                PipelineEnumDefinedNamePrefix,
                                StringComparison.Ordinal) == true)
                        {
                            generatedEnumNames.Add(name.Name.Value);
                        }
                    }
                }

                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                DefinedNames sourceNames = sourcePart.Workbook.GetFirstChild<DefinedNames>();
                if (sourceNames != null)
                {
                    foreach (DefinedName name in sourceNames.Elements<DefinedName>())
                    {
                        string key = DefinedNameKey(name);
                        bool stalePipelineEnum = name.Name?.Value?.StartsWith(
                                                     PipelineEnumDefinedNamePrefix,
                                                     StringComparison.Ordinal) == true &&
                                                 !generatedEnumNames.Contains(
                                                     name.Name.Value);
                        if (!generatedDefinedNames.Contains(key) && !stalePipelineEnum)
                        {
                            names.Add(key, name.OuterXml);
                        }
                    }
                }

                var sheetCodeNames = new Dictionary<string, string>(StringComparer.Ordinal);
                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, WorksheetPart> entry in WorksheetMap(sourcePart))
                {
                    sheetCodeNames.Add(entry.Key, CodeName(entry.Value));
                    if (PipelineSheets.Contains(entry.Key))
                    {
                        continue;
                    }

                    List<ManagedRange> managedRanges = entry.Value.TableDefinitionParts
                        .Select(part => ManagedRange.FromTable(part.Table))
                        .ToList();
                    foreach (Cell cell in entry.Value.Worksheet.Descendants<Cell>())
                    {
                        string reference = cell.CellReference?.Value;
                        int column = ColumnOf(reference);
                        uint row = RowOf(reference);
                        if (managedRanges.Any(range => range.Contains(column, row)))
                        {
                            continue;
                        }

                        cells.Add(entry.Key + "!" + reference, cell.OuterXml);
                    }
                }

                return new AuthoringFidelitySnapshot(
                    ReadVbaProject(sourcePart),
                    sheetCodeNames,
                    names,
                    cells);
            }

            public void Verify(WorkbookPart sourcePart)
            {
                byte[] candidateVba = ReadVbaProject(sourcePart);
                if ((vbaProject == null) != (candidateVba == null) ||
                    (vbaProject != null && !vbaProject.SequenceEqual(candidateVba)))
                {
                    throw new InvalidDataException(
                        "CONFIG_WORKBOOK_VBA_FIDELITY_FAILED");
                }

                Dictionary<string, WorksheetPart> sheets = WorksheetMap(sourcePart);
                foreach (KeyValuePair<string, string> expected in codeNames)
                {
                    if (!sheets.TryGetValue(expected.Key, out WorksheetPart sheet) ||
                        !string.Equals(CodeName(sheet), expected.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "CONFIG_WORKBOOK_SHEET_CODENAME_FIDELITY_FAILED: " + expected.Key);
                    }
                }

                var actualNames = new Dictionary<string, string>(StringComparer.Ordinal);
                DefinedNames names = sourcePart.Workbook.GetFirstChild<DefinedNames>();
                if (names != null)
                {
                    foreach (DefinedName name in names.Elements<DefinedName>())
                    {
                        actualNames[DefinedNameKey(name)] = name.OuterXml;
                    }
                }

                foreach (KeyValuePair<string, string> expected in designerDefinedNames)
                {
                    if (!actualNames.TryGetValue(expected.Key, out string actual) ||
                        !string.Equals(actual, expected.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "CONFIG_WORKBOOK_DEFINED_NAME_FIDELITY_FAILED: " + expected.Key);
                    }
                }

                var actualCells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, WorksheetPart> entry in sheets)
                {
                    if (PipelineSheets.Contains(entry.Key))
                    {
                        continue;
                    }

                    foreach (Cell cell in entry.Value.Worksheet.Descendants<Cell>())
                    {
                        string reference = cell.CellReference?.Value;
                        actualCells[entry.Key + "!" + reference] = cell.OuterXml;
                    }
                }

                foreach (KeyValuePair<string, string> expected in designerCells)
                {
                    if (!actualCells.TryGetValue(expected.Key, out string actual) ||
                        !string.Equals(actual, expected.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "CONFIG_WORKBOOK_DESIGNER_CELL_FIDELITY_FAILED: " + expected.Key);
                    }
                }
            }
        }

        private sealed class ManagedTableState
        {
            public ManagedTableState(
                string name,
                TableDefinitionPart part,
                int firstColumn,
                uint firstRow,
                int lastColumn,
                uint lastRow)
            {
                Name = name;
                Part = part;
                FirstColumn = firstColumn;
                FirstRow = firstRow;
                LastColumn = lastColumn;
                LastRow = lastRow;
            }

            public string Name { get; }
            public TableDefinitionPart Part { get; }
            public int FirstColumn { get; }
            public uint FirstRow { get; }
            public int LastColumn { get; }
            public uint LastRow { get; }
            public uint ManagedFirstRow => FirstRow > 1U ? FirstRow - 1U : FirstRow;
            public int ColumnCount => LastColumn - FirstColumn + 1;

            public bool ContainsManagedCell(int column, uint row)
            {
                return column >= FirstColumn &&
                       column <= LastColumn &&
                       row >= ManagedFirstRow &&
                       row <= LastRow;
            }
        }

        private sealed class ManagedTableTarget
        {
            public ManagedTableTarget(
                ManagedTableState generated,
                ManagedTableState source,
                int newTableRowOffset = 0)
            {
                Generated = generated ?? throw new ArgumentNullException(nameof(generated));
                Source = source;
                RowOffset = source == null
                    ? newTableRowOffset
                    : checked((int)((long)source.FirstRow - generated.FirstRow));
                if (RowOffset < 0)
                {
                    throw new InvalidDataException(
                        "Pipeline table '" + generated.Name +
                        "' moved above its generated header; refresh is unsafe.");
                }

                FirstColumn = source?.FirstColumn ?? generated.FirstColumn;
                LastColumn = checked(FirstColumn + generated.ColumnCount - 1);
                if (LastColumn > 16384)
                {
                    throw new InvalidDataException(
                        "Pipeline table '" + generated.Name +
                        "' exceeds the worksheet column limit.");
                }

                long firstRow = (long)generated.FirstRow + RowOffset;
                long lastRow = (long)generated.LastRow + RowOffset;
                if (firstRow > 1048576L || lastRow > 1048576L)
                {
                    throw new InvalidDataException(
                        "Pipeline table '" + generated.Name +
                        "' exceeds the worksheet row limit.");
                }

                FirstRow = (uint)firstRow;
                LastRow = (uint)lastRow;
                Placement = new ManagedTablePlacement(
                    FirstColumn,
                    FirstRow,
                    LastColumn,
                    generated.FirstColumn,
                    generated.FirstRow,
                    generated.LastColumn);
            }

            public ManagedTableState Generated { get; }
            public ManagedTableState Source { get; }
            public int RowOffset { get; }
            public int ColumnOffset => FirstColumn - Generated.FirstColumn;
            public int FirstColumn { get; }
            public uint FirstRow { get; }
            public int LastColumn { get; }
            public uint LastRow { get; }
            public uint ManagedFirstRow => FirstRow > 1U ? FirstRow - 1U : FirstRow;
            public ManagedTablePlacement Placement { get; }

            public bool ContainsManagedCell(int column, uint row)
            {
                return column >= FirstColumn &&
                       column <= LastColumn &&
                       row >= ManagedFirstRow &&
                       row <= LastRow;
            }

            public bool Intersects(ManagedTableTarget other)
            {
                return FirstColumn <= other.LastColumn &&
                       LastColumn >= other.FirstColumn &&
                       ManagedFirstRow <= other.LastRow &&
                       LastRow >= other.ManagedFirstRow;
            }
        }

        private static List<ManagedTableState> ManagedTableStates(
            WorksheetPart worksheetPart)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ManagedTableState>();
            foreach (TableDefinitionPart tablePart in worksheetPart.TableDefinitionParts)
            {
                Table table = tablePart.Table;
                string name = table?.Name?.Value ?? table?.DisplayName?.Value;
                if (string.IsNullOrEmpty(name) || !names.Add(name))
                {
                    throw new InvalidDataException(
                        "Worksheet contains an invalid or duplicate pipeline table.");
                }

                ParseRange(
                    table.Reference?.Value,
                    out int firstColumn,
                    out uint firstRow,
                    out int lastColumn,
                    out uint lastRow);
                if (firstColumn < 1 || lastColumn > 16384 ||
                    firstRow < 1U || lastRow > 1048576U ||
                    lastRow < firstRow ||
                    (ulong)lastRow - firstRow >
                    (ulong)XlsxWorkbookLimits.DefaultRowsPerSheet)
                {
                    throw new InvalidDataException(
                        "CONFIG_WORKBOOK_TABLE_RANGE_LIMIT: " + name);
                }

                result.Add(new ManagedTableState(
                    name,
                    tablePart,
                    firstColumn,
                    firstRow,
                    lastColumn,
                    lastRow));
            }

            return result;
        }

        private static string LogicalTableName(string name)
        {
            int separator = name?.LastIndexOf('_') ?? -1;
            if (separator <= 0 || separator == name.Length - 1 ||
                !uint.TryParse(
                    name.Substring(separator + 1),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
            {
                return name ?? string.Empty;
            }

            return name.Substring(0, separator);
        }

        private static void EnsureManagedTargetsAreSafe(
            WorkbookPart sourcePart,
            WorksheetPart sourceSheet,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            for (int index = 0; index < targets.Count; index++)
            {
                for (int otherIndex = index + 1;
                     otherIndex < targets.Count;
                     otherIndex++)
                {
                    if (targets[index].Intersects(targets[otherIndex]))
                    {
                        throw new InvalidDataException(
                            "Pipeline table layout overlaps after Schema evolution: " +
                            targets[index].Generated.Name + ", " +
                            targets[otherIndex].Generated.Name);
                    }
                }
            }

            if (!targets.Any(HasNewManagedArea))
            {
                return;
            }

            EnsureDesignerDefinedNamesAreSafe(
                sourcePart,
                sourceSheet,
                targets);

            foreach (Cell cell in sourceSheet.Worksheet.Descendants<Cell>())
            {
                string reference = cell.CellReference?.Value;
                EnsureDesignerRangeIsSafe(reference, targets);
            }

            foreach (MergeCell merge in sourceSheet.Worksheet.Descendants<MergeCell>())
            {
                EnsureDesignerRangeIsSafe(merge.Reference?.Value, targets);
            }

            var existingPlacements = targets.Where(target => target.Source != null)
                .Select(target => new ManagedTablePlacement(
                    target.Source.FirstColumn,
                    target.Source.FirstRow,
                    target.Source.LastColumn,
                    target.Source.FirstColumn,
                    target.Source.FirstRow,
                    target.Source.LastColumn))
                .ToList();
            foreach (DataValidation validation in
                     sourceSheet.Worksheet.Descendants<DataValidation>())
            {
                if (IsPipelineValidation(validation, existingPlacements))
                {
                    continue;
                }

                EnsureDesignerRangesAreSafe(
                    validation.SequenceOfReferences?.InnerText,
                    targets);
            }

            foreach (Hyperlink hyperlink in sourceSheet.Worksheet.Descendants<Hyperlink>())
            {
                EnsureDesignerRangeIsSafe(hyperlink.Reference?.Value, targets);
            }

            foreach (ConditionalFormatting formatting in
                     sourceSheet.Worksheet.Elements<ConditionalFormatting>())
            {
                EnsureDesignerRangesAreSafe(
                    formatting.SequenceOfReferences?.InnerText,
                    targets);
            }

            foreach (AutoFilter autoFilter in sourceSheet.Worksheet.Descendants<AutoFilter>())
            {
                EnsureDesignerRangeIsSafe(autoFilter.Reference?.Value, targets);
            }

            foreach (PivotTablePart pivotPart in sourceSheet.PivotTableParts)
            {
                EnsureDesignerRangeIsSafe(
                    pivotPart.PivotTableDefinition?.Location?.Reference?.Value,
                    targets);
            }

            if ((sourceSheet.VmlDrawingParts.Any() ||
                 sourceSheet.Worksheet.Descendants<LegacyDrawing>().Any()) &&
                targets.Any(HasNewManagedArea))
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: legacy VML drawing");
            }

            DrawingsPart drawingsPart = sourceSheet.DrawingsPart;
            if (drawingsPart?.WorksheetDrawing == null)
            {
                return;
            }

            foreach (Xdr.TwoCellAnchor anchor in
                     drawingsPart.WorksheetDrawing.Descendants<Xdr.TwoCellAnchor>())
            {
                ParseDrawingMarker(
                    anchor.FromMarker,
                    out int fromColumn,
                    out uint fromRow);
                ParseDrawingMarker(
                    anchor.ToMarker,
                    out int toColumn,
                    out uint toRow);
                EnsureDesignerCoordinatesAreSafe(
                    Math.Min(fromColumn, toColumn),
                    Math.Min(fromRow, toRow),
                    Math.Max(fromColumn, toColumn),
                    Math.Max(fromRow, toRow),
                    "drawing",
                    targets);
            }

            if ((drawingsPart.WorksheetDrawing.Descendants<Xdr.OneCellAnchor>().Any() ||
                 drawingsPart.WorksheetDrawing.Descendants<Xdr.AbsoluteAnchor>().Any()) &&
                targets.Any(HasNewManagedArea))
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: drawing anchor");
            }
        }

        private static void EnsureDesignerDefinedNamesAreSafe(
            WorkbookPart workbookPart,
            WorksheetPart worksheetPart,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            DefinedNames definedNames =
                workbookPart.Workbook.GetFirstChild<DefinedNames>();
            if (definedNames == null)
            {
                return;
            }

            Sheet[] sheets = workbookPart.Workbook.Sheets.Elements<Sheet>().ToArray();
            string relationshipId = workbookPart.GetIdOfPart(worksheetPart);
            int sheetIndex = Array.FindIndex(
                sheets,
                sheet => string.Equals(
                    sheet.Id?.Value,
                    relationshipId,
                    StringComparison.Ordinal));
            if (sheetIndex < 0 || string.IsNullOrEmpty(sheets[sheetIndex].Name?.Value))
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: worksheet identity");
            }

            string sheetName = sheets[sheetIndex].Name.Value;
            string[] sheetNames = sheets
                .Select(sheet => sheet.Name?.Value ?? string.Empty)
                .ToArray();
            foreach (DefinedName definedName in definedNames.Elements<DefinedName>())
            {
                string name = definedName.Name?.Value ?? string.Empty;
                if (name.StartsWith(
                        PipelineEnumDefinedNamePrefix,
                        StringComparison.Ordinal) ||
                    name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EnsureDesignerDefinedNameIsSafe(
                    definedName,
                    name,
                    sheetName,
                    (uint)sheetIndex,
                    sheetNames,
                    targets);
            }
        }

        private static void EnsureDesignerDefinedNameIsSafe(
            DefinedName definedName,
            string name,
            string targetSheetName,
            uint targetSheetIndex,
            IReadOnlyList<string> sheetNames,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            string formula = definedName.Text?.Trim();
            bool localToTarget = definedName.LocalSheetId?.Value == targetSheetIndex;
            if (string.IsNullOrEmpty(formula))
            {
                if (localToTarget)
                {
                    ThrowDefinedNameLayoutConflict(name);
                }

                return;
            }

            if (formula[0] == '=')
            {
                formula = formula.Substring(1).TrimStart();
            }

            if (!TrySplitDefinedNameUnion(formula, out List<string> areas))
            {
                if (localToTarget || ReferencesTargetSheet(formula, targetSheetName))
                {
                    ThrowDefinedNameLayoutConflict(name);
                }

                return;
            }

            var unqualifiedAreas = new List<string>();
            bool explicitlyTargetsSheet = false;
            foreach (string area in areas)
            {
                if (!TryParseDefinedNameArea(
                        area,
                        out bool hasSheetQualifier,
                        out bool externalQualifier,
                        out string qualifiedSheet,
                        out string reference))
                {
                    if (localToTarget || ReferencesTargetSheet(area, targetSheetName))
                    {
                        ThrowDefinedNameLayoutConflict(name);
                    }

                    continue;
                }

                if (externalQualifier)
                {
                    continue;
                }

                if (!hasSheetQualifier)
                {
                    unqualifiedAreas.Add(reference);
                    continue;
                }

                if (!string.Equals(
                        qualifiedSheet,
                        targetSheetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (QualifierIncludesTargetSheet(
                            qualifiedSheet,
                            targetSheetIndex,
                            sheetNames) ||
                        ReferencesTargetSheet(area, targetSheetName))
                    {
                        ThrowDefinedNameLayoutConflict(name);
                    }

                    continue;
                }

                explicitlyTargetsSheet = true;
                EnsureDesignerDefinedNameRangeIsSafe(
                    reference,
                    name,
                    targets);
            }

            foreach (string reference in unqualifiedAreas)
            {
                if (localToTarget)
                {
                    if (IsConstantDefinedNameFormula(reference))
                    {
                        continue;
                    }

                    EnsureDesignerDefinedNameRangeIsSafe(
                        reference,
                        name,
                        targets);
                }
                else if (explicitlyTargetsSheet)
                {
                    // A global union that mixes an explicit target sheet with an
                    // unqualified area is ambiguous. Preserve the source and fail closed.
                    ThrowDefinedNameLayoutConflict(name);
                }
            }
        }

        private static void EnsureDesignerDefinedNameRangeIsSafe(
            string reference,
            string name,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            try
            {
                ParseDesignerRange(
                    reference,
                    out int firstColumn,
                    out uint firstRow,
                    out int lastColumn,
                    out uint lastRow);
                EnsureDesignerCoordinatesAreSafe(
                    firstColumn,
                    firstRow,
                    lastColumn,
                    lastRow,
                    "defined name " + name,
                    targets);
            }
            catch (Exception exception) when (
                (exception is InvalidDataException || exception is OverflowException) &&
                !exception.Message.StartsWith(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: defined name ",
                    StringComparison.Ordinal))
            {
                ThrowDefinedNameLayoutConflict(name);
            }
        }

        private static bool TrySplitDefinedNameUnion(
            string formula,
            out List<string> areas)
        {
            areas = new List<string>();
            bool quoted = false;
            bool stringQuoted = false;
            int start = 0;
            for (int index = 0; index < formula.Length; index++)
            {
                if (!stringQuoted && formula[index] == '\'')
                {
                    if (quoted && index + 1 < formula.Length &&
                        formula[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }

                    quoted = !quoted;
                    continue;
                }

                if (!quoted && formula[index] == '"')
                {
                    if (stringQuoted && index + 1 < formula.Length &&
                        formula[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    stringQuoted = !stringQuoted;
                    continue;
                }

                if (!quoted && !stringQuoted && formula[index] == ',')
                {
                    string area = formula.Substring(start, index - start).Trim();
                    if (area.Length == 0)
                    {
                        return false;
                    }

                    areas.Add(area);
                    start = index + 1;
                }
            }

            if (quoted || stringQuoted)
            {
                return false;
            }

            string finalArea = formula.Substring(start).Trim();
            if (finalArea.Length == 0)
            {
                return false;
            }

            areas.Add(finalArea);
            return true;
        }

        private static bool TryParseDefinedNameArea(
            string area,
            out bool hasSheetQualifier,
            out bool externalQualifier,
            out string sheetName,
            out string reference)
        {
            hasSheetQualifier = false;
            externalQualifier = false;
            sheetName = null;
            reference = null;
            bool quoted = false;
            bool stringQuoted = false;
            int bang = -1;
            for (int index = 0; index < area.Length; index++)
            {
                if (!stringQuoted && area[index] == '\'')
                {
                    if (quoted && index + 1 < area.Length && area[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }

                    quoted = !quoted;
                }
                else if (!quoted && area[index] == '"')
                {
                    if (stringQuoted && index + 1 < area.Length &&
                        area[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    stringQuoted = !stringQuoted;
                }
                else if (!quoted && !stringQuoted && area[index] == '!')
                {
                    if (bang >= 0)
                    {
                        return false;
                    }

                    bang = index;
                }
            }

            if (quoted || stringQuoted)
            {
                return false;
            }

            if (bang < 0)
            {
                reference = area.Trim();
                return reference.Length != 0;
            }

            hasSheetQualifier = true;
            string qualifier = area.Substring(0, bang).Trim();
            reference = area.Substring(bang + 1).Trim();
            if (qualifier.Length == 0 || reference.Length == 0)
            {
                return false;
            }

            if (qualifier.IndexOf('[') >= 0 || qualifier.IndexOf(']') >= 0)
            {
                externalQualifier = true;
                return true;
            }

            if (qualifier[0] == '\'')
            {
                if (qualifier.Length < 2 || qualifier[qualifier.Length - 1] != '\'')
                {
                    return false;
                }

                string encodedSheetName = qualifier.Substring(
                    1,
                    qualifier.Length - 2);
                for (int index = 0; index < encodedSheetName.Length; index++)
                {
                    if (encodedSheetName[index] != '\'')
                    {
                        continue;
                    }

                    if (index + 1 >= encodedSheetName.Length ||
                        encodedSheetName[index + 1] != '\'')
                    {
                        return false;
                    }

                    index++;
                }

                sheetName = encodedSheetName.Replace("''", "'");
            }
            else
            {
                if (qualifier.IndexOf('\'') >= 0)
                {
                    return false;
                }

                sheetName = qualifier;
            }

            return sheetName.Length != 0;
        }

        private static bool ReferencesTargetSheet(string text, string sheetName)
        {
            string quoted = "'" + sheetName.Replace("'", "''") + "'!";
            if (text.IndexOf(quoted, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string unquoted = sheetName + "!";
            int index = text.IndexOf(unquoted, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                if (index == 0 ||
                    (!char.IsLetterOrDigit(text[index - 1]) &&
                     text[index - 1] != '_' &&
                     text[index - 1] != '.' &&
                     text[index - 1] != ']'))
                {
                    return true;
                }

                index = text.IndexOf(
                    unquoted,
                    index + unquoted.Length,
                    StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool QualifierIncludesTargetSheet(
            string qualifier,
            uint targetSheetIndex,
            IReadOnlyList<string> sheetNames)
        {
            string[] endpoints = qualifier.Split(':');
            if (endpoints.Length != 2)
            {
                return false;
            }

            int firstIndex = IndexOfSheet(sheetNames, endpoints[0].Trim());
            int lastIndex = IndexOfSheet(sheetNames, endpoints[1].Trim());
            if (firstIndex < 0 || lastIndex < 0)
            {
                return false;
            }

            int minimum = Math.Min(firstIndex, lastIndex);
            int maximum = Math.Max(firstIndex, lastIndex);
            return targetSheetIndex >= minimum && targetSheetIndex <= maximum;
        }

        private static int IndexOfSheet(
            IReadOnlyList<string> sheetNames,
            string name)
        {
            for (int index = 0; index < sheetNames.Count; index++)
            {
                if (string.Equals(
                        sheetNames[index],
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsConstantDefinedNameFormula(string formula)
        {
            string value = formula.Trim();
            return (value.Length >= 2 && value[0] == '"' &&
                    value[value.Length - 1] == '"') ||
                   bool.TryParse(value, out _) ||
                   double.TryParse(
                       value,
                       System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out _);
        }

        private static void ThrowDefinedNameLayoutConflict(string name)
        {
            throw new InvalidDataException(
                "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: defined name " + name);
        }

        private static void EnsureDesignerRangesAreSafe(
            string references,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            if (string.IsNullOrWhiteSpace(references))
            {
                return;
            }

            foreach (string reference in references.Split(
                         new[] { ' ' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                EnsureDesignerRangeIsSafe(reference, targets);
            }
        }

        private static void EnsureDesignerRangeIsSafe(
            string reference,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            ParseDesignerRange(
                reference,
                out int firstColumn,
                out uint firstRow,
                out int lastColumn,
                out uint lastRow);
            EnsureDesignerCoordinatesAreSafe(
                firstColumn,
                firstRow,
                lastColumn,
                lastRow,
                reference,
                targets);
        }

        private static void EnsureDesignerCoordinatesAreSafe(
            int firstColumn,
            uint firstRow,
            int lastColumn,
            uint lastRow,
            string description,
            IReadOnlyList<ManagedTableTarget> targets)
        {
            if (!targets.Any(target => IntersectsNewManagedArea(
                    target,
                    firstColumn,
                    firstRow,
                    lastColumn,
                    lastRow)))
            {
                return;
            }

            throw new InvalidDataException(
                "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: " + description);
        }

        private static bool HasNewManagedArea(ManagedTableTarget target)
        {
            ManagedTableState source = target.Source;
            return source == null ||
                   target.FirstColumn < source.FirstColumn ||
                   target.LastColumn > source.LastColumn ||
                   target.ManagedFirstRow < source.ManagedFirstRow ||
                   target.LastRow > source.LastRow;
        }

        private static void ParseDrawingMarker(
            Xdr.MarkerType marker,
            out int column,
            out uint row)
        {
            if (marker?.ColumnId == null || marker.RowId == null ||
                !int.TryParse(
                    marker.ColumnId.Text,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int zeroBasedColumn) ||
                !uint.TryParse(
                    marker.RowId.Text,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out uint zeroBasedRow) ||
                zeroBasedColumn < 0 || zeroBasedColumn >= 16384 ||
                zeroBasedRow >= 1048576U)
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: drawing anchor");
            }

            column = zeroBasedColumn + 1;
            row = zeroBasedRow + 1U;
        }

        private static bool IntersectsNewManagedArea(
            ManagedTableTarget target,
            int firstColumn,
            uint firstRow,
            int lastColumn,
            uint lastRow)
        {
            int overlapFirstColumn = Math.Max(firstColumn, target.FirstColumn);
            uint overlapFirstRow = Math.Max(firstRow, target.ManagedFirstRow);
            int overlapLastColumn = Math.Min(lastColumn, target.LastColumn);
            uint overlapLastRow = Math.Min(lastRow, target.LastRow);
            if (overlapFirstColumn > overlapLastColumn ||
                overlapFirstRow > overlapLastRow)
            {
                return false;
            }

            ManagedTableState source = target.Source;
            return source == null ||
                   overlapFirstColumn < source.FirstColumn ||
                   overlapLastColumn > source.LastColumn ||
                   overlapFirstRow < source.ManagedFirstRow ||
                   overlapLastRow > source.LastRow;
        }

        private static void ParseDesignerRange(
            string reference,
            out int firstColumn,
            out uint firstRow,
            out int lastColumn,
            out uint lastRow)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: range is missing");
            }

            string[] cells = reference.Replace("$", string.Empty).Split(':');
            if (cells.Length == 1)
            {
                firstColumn = lastColumn = ColumnOf(cells[0]);
                firstRow = lastRow = RowOf(cells[0]);
            }
            else if (cells.Length == 2)
            {
                firstColumn = ColumnOf(cells[0]);
                firstRow = RowOf(cells[0]);
                lastColumn = ColumnOf(cells[1]);
                lastRow = RowOf(cells[1]);
            }
            else
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: " + reference);
            }

            if (firstColumn > lastColumn || firstRow > lastRow)
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_MANAGED_LAYOUT_CONFLICT: " + reference);
            }
        }

        private sealed class ManagedRange
        {
            private ManagedRange(
                int firstColumn,
                uint firstRow,
                int lastColumn,
                uint lastRow)
            {
                FirstColumn = firstColumn;
                FirstRow = firstRow;
                LastColumn = lastColumn;
                LastRow = lastRow;
            }

            private int FirstColumn { get; }
            private uint FirstRow { get; }
            private int LastColumn { get; }
            private uint LastRow { get; }

            public static ManagedRange FromTable(Table table)
            {
                if (table == null)
                {
                    throw new InvalidDataException("Pipeline table is invalid.");
                }

                ParseRange(
                    table.Reference?.Value,
                    out int firstColumn,
                    out uint firstRow,
                    out int lastColumn,
                    out uint lastRow);
                return new ManagedRange(
                    firstColumn,
                    firstRow > 1U ? firstRow - 1U : firstRow,
                    lastColumn,
                    lastRow);
            }

            public bool Contains(int column, uint row)
            {
                return column >= FirstColumn &&
                       column <= LastColumn &&
                       row >= FirstRow &&
                       row <= LastRow;
            }
        }

        private sealed class ManagedTablePlacement
        {
            public ManagedTablePlacement(
                int sourceFirstColumn,
                uint sourceFirstRow,
                int sourceLastColumn,
                int generatedFirstColumn,
                uint generatedFirstRow,
                int generatedLastColumn)
            {
                SourceFirstColumn = sourceFirstColumn;
                SourceFirstRow = sourceFirstRow;
                SourceLastColumn = sourceLastColumn;
                GeneratedFirstColumn = generatedFirstColumn;
                GeneratedFirstRow = generatedFirstRow;
                GeneratedLastColumn = generatedLastColumn;
            }

            private int SourceFirstColumn { get; }
            private uint SourceFirstRow { get; }
            private int SourceLastColumn { get; }
            private int GeneratedFirstColumn { get; }
            private uint GeneratedFirstRow { get; }
            private int GeneratedLastColumn { get; }
            private uint SourceDataFirstRow => SourceFirstRow + 1U;
            private uint GeneratedDataFirstRow => GeneratedFirstRow + 1U;

            public bool ContainsGeneratedValidation(
                int firstColumn,
                uint firstRow,
                int lastColumn,
                uint lastRow)
            {
                return firstColumn >= GeneratedFirstColumn &&
                       lastColumn <= GeneratedLastColumn &&
                       firstRow == GeneratedDataFirstRow &&
                       lastRow == 1048576U;
            }

            public bool ContainsSourceValidation(
                int firstColumn,
                uint firstRow,
                int lastColumn,
                uint lastRow)
            {
                return firstColumn >= SourceFirstColumn &&
                       lastColumn <= SourceLastColumn &&
                       firstRow == SourceDataFirstRow &&
                       lastRow == 1048576U;
            }

            public string MapGeneratedRange(
                int firstColumn,
                int lastColumn)
            {
                int columnOffset = SourceFirstColumn - GeneratedFirstColumn;
                return RangeReference(
                    firstColumn + columnOffset,
                    SourceDataFirstRow,
                    lastColumn + columnOffset,
                    1048576U);
            }

            public string MapGeneratedFormula(string formula)
            {
                if (string.IsNullOrEmpty(formula))
                {
                    return formula;
                }

                int columnOffset = SourceFirstColumn - GeneratedFirstColumn;
                string mapped = formula;
                for (int column = GeneratedLastColumn;
                     column >= GeneratedFirstColumn;
                     column--)
                {
                    mapped = mapped.Replace(
                        ColumnName(column) + GeneratedDataFirstRow.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ColumnName(column + columnOffset) + SourceDataFirstRow.ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                }

                return mapped;
            }
        }

        private static void MergeManagedDataValidations(
            WorksheetPart sourceSheet,
            WorksheetPart generatedSheet,
            IReadOnlyList<ManagedTablePlacement> sourcePlacements,
            IReadOnlyList<ManagedTablePlacement> targetPlacements)
        {
            DataValidations sourceContainer =
                sourceSheet.Worksheet.Elements<DataValidations>().SingleOrDefault();
            DataValidations generatedContainer =
                generatedSheet.Worksheet.Elements<DataValidations>().SingleOrDefault();
            var retained = new List<DataValidation>();
            if (sourceContainer != null)
            {
                retained.AddRange(sourceContainer.Elements<DataValidation>()
                    .Where(validation => !IsPipelineValidation(
                        validation,
                        sourcePlacements))
                    .Select(validation => (DataValidation)validation.CloneNode(true)));
            }

            var mapped = new List<DataValidation>();
            if (generatedContainer != null)
            {
                foreach (DataValidation validation in
                         generatedContainer.Elements<DataValidation>())
                {
                    mapped.Add(MapGeneratedValidation(validation, targetPlacements));
                }
            }

            if (sourceContainer == null && retained.Count == 0 && mapped.Count == 0)
            {
                return;
            }

            if (sourceContainer == null)
            {
                sourceContainer = new DataValidations();
                OpenXmlElement next =
                    sourceSheet.Worksheet.GetFirstChild<Hyperlinks>();
                if (next == null)
                {
                    next = sourceSheet.Worksheet.GetFirstChild<TableParts>();
                }
                if (next == null)
                {
                    sourceSheet.Worksheet.Append(sourceContainer);
                }
                else
                {
                    sourceSheet.Worksheet.InsertBefore(sourceContainer, next);
                }
            }
            else
            {
                sourceContainer.RemoveAllChildren<DataValidation>();
            }

            foreach (DataValidation validation in retained.Concat(mapped))
            {
                sourceContainer.Append(validation);
            }

            sourceContainer.Count = (uint)(retained.Count + mapped.Count);
            if (!sourceContainer.ChildElements.Any())
            {
                sourceSheet.Worksheet.RemoveChild(sourceContainer);
            }
        }

        private static bool IsPipelineValidation(
            DataValidation validation,
            IReadOnlyList<ManagedTablePlacement> placements)
        {
            string references = validation.SequenceOfReferences?.InnerText;
            if (string.IsNullOrWhiteSpace(references))
            {
                return false;
            }

            string[] ranges = references.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            return ranges.Length != 0 && ranges.All(reference =>
            {
                try
                {
                    ParseRange(
                        reference,
                        out int firstColumn,
                        out uint firstRow,
                        out int lastColumn,
                        out uint lastRow);
                    return placements.Any(placement => placement.ContainsSourceValidation(
                        firstColumn,
                        firstRow,
                        lastColumn,
                        lastRow));
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            });
        }

        private static DataValidation MapGeneratedValidation(
            DataValidation validation,
            IReadOnlyList<ManagedTablePlacement> placements)
        {
            DataValidation result = (DataValidation)validation.CloneNode(true);
            string references = validation.SequenceOfReferences?.InnerText;
            if (string.IsNullOrWhiteSpace(references))
            {
                throw new InvalidDataException(
                    "Pipeline data validation is missing its cell range.");
            }

            string[] ranges = references.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            ManagedTablePlacement formulaPlacement = null;
            var mapped = new List<string>();
            foreach (string reference in ranges)
            {
                ParseRange(
                    reference,
                    out int firstColumn,
                    out uint firstRow,
                    out int lastColumn,
                    out uint lastRow);
                ManagedTablePlacement placement = placements.SingleOrDefault(value =>
                    value.ContainsGeneratedValidation(
                        firstColumn,
                        firstRow,
                        lastColumn,
                        lastRow));
                if (placement == null)
                {
                    throw new InvalidDataException(
                        "Pipeline data validation is outside a managed table range.");
                }

                formulaPlacement = formulaPlacement ?? placement;
                if (!ReferenceEquals(formulaPlacement, placement))
                {
                    throw new InvalidDataException(
                        "Pipeline data validation cannot span managed tables.");
                }

                mapped.Add(placement.MapGeneratedRange(firstColumn, lastColumn));
            }

            result.SequenceOfReferences.InnerText = string.Join(" ", mapped);
            if (result.Formula1 != null)
            {
                result.Formula1.Text = formulaPlacement.MapGeneratedFormula(
                    result.Formula1.Text);
            }

            if (result.Formula2 != null)
            {
                result.Formula2.Text = formulaPlacement.MapGeneratedFormula(
                    result.Formula2.Text);
            }

            return result;
        }

        private static byte[] ReadVbaProject(WorkbookPart workbookPart)
        {
            VbaProjectPart vbaPart = workbookPart.VbaProjectPart;
            if (vbaPart == null)
            {
                return null;
            }

            using (Stream stream = vbaPart.GetStream(FileMode.Open, FileAccess.Read))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static void ValidateGeneratedStyleIndexes(
            WorkbookPart sourcePart,
            WorkbookPart generatedPart)
        {
            CellFormats sourceFormats = sourcePart.WorkbookStylesPart?
                .Stylesheet?.CellFormats;
            CellFormats generatedFormats = generatedPart.WorkbookStylesPart?
                .Stylesheet?.CellFormats;
            if (sourceFormats == null || generatedFormats == null)
            {
                throw new InvalidDataException(
                    "CONFIG_WORKBOOK_STYLE_INDEX_FIDELITY_FAILED");
            }

            List<CellFormat> source = sourceFormats.Elements<CellFormat>().ToList();
            List<CellFormat> generated = generatedFormats.Elements<CellFormat>().ToList();
            var usedStyleIndexes = new HashSet<uint>(
                generatedPart.WorksheetParts
                    .SelectMany(sheet => sheet.Worksheet.Descendants<Cell>())
                    .Select(cell => cell.StyleIndex?.Value ?? 0U));
            foreach (uint index in usedStyleIndexes)
            {
                if (index >= source.Count ||
                    index >= generated.Count ||
                    !string.Equals(
                        StyleIdentity(source[(int)index]),
                        StyleIdentity(generated[(int)index]),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "CONFIG_WORKBOOK_STYLE_INDEX_FIDELITY_FAILED: " + index);
                }
            }
        }

        private static string StyleIdentity(CellFormat format)
        {
            return (format.NumberFormatId?.Value ?? 0U) + ":" +
                   (format.FontId?.Value ?? 0U) + ":" +
                   (format.FillId?.Value ?? 0U) + ":" +
                   (format.BorderId?.Value ?? 0U) + ":" +
                   (format.FormatId?.Value ?? 0U);
        }

        private static string DefinedNameKey(DefinedName name)
        {
            string value = name.Name?.Value ?? string.Empty;
            string localSheet = name.LocalSheetId?.Value.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture) ??
                                string.Empty;
            return value + "\u0000" + localSheet;
        }

        private static string CodeName(WorksheetPart worksheetPart)
        {
            SheetProperties properties =
                worksheetPart.Worksheet.GetFirstChild<SheetProperties>();
            return properties?.CodeName?.Value;
        }

        private static void RestoreCodeName(Worksheet worksheet, string codeName)
        {
            if (string.IsNullOrEmpty(codeName))
            {
                return;
            }

            SheetProperties properties = worksheet.GetFirstChild<SheetProperties>();
            if (properties == null)
            {
                properties = new SheetProperties();
                worksheet.PrependChild(properties);
            }

            properties.CodeName = codeName;
        }

        private static Dictionary<string, TableDefinitionPart> TableMap(
            WorksheetPart worksheetPart)
        {
            var result = new Dictionary<string, TableDefinitionPart>(StringComparer.Ordinal);
            foreach (TableDefinitionPart tablePart in worksheetPart.TableDefinitionParts)
            {
                string name = tablePart.Table?.Name?.Value ??
                              tablePart.Table?.DisplayName?.Value;
                if (string.IsNullOrEmpty(name) || result.ContainsKey(name))
                {
                    throw new InvalidDataException(
                        "Worksheet contains an invalid or duplicate pipeline table.");
                }

                result.Add(name, tablePart);
            }

            return result;
        }

        private static void DetachManagedTablePreservingCells(
            WorksheetPart worksheetPart,
            ManagedTableState table)
        {
            string relationshipId = worksheetPart.GetIdOfPart(table.Part);
            TableParts tableParts = worksheetPart.Worksheet.GetFirstChild<TableParts>();
            TablePart relationship = tableParts?.Elements<TablePart>()
                .SingleOrDefault(value => string.Equals(
                    value.Id?.Value,
                    relationshipId,
                    StringComparison.Ordinal));
            relationship?.Remove();
            if (tableParts != null)
            {
                tableParts.Count = (uint)tableParts.ChildElements.Count;
                if (!tableParts.ChildElements.Any())
                {
                    tableParts.Remove();
                }
            }

            worksheetPart.DeletePart(table.Part);
        }

        private static void UpsertManagedTable(
            WorkbookPart workbookPart,
            WorksheetPart worksheetPart,
            ManagedTableTarget target)
        {
            Table table = (Table)target.Generated.Part.Table.CloneNode(true);
            string reference = RangeReference(
                target.FirstColumn,
                target.FirstRow,
                target.LastColumn,
                target.LastRow);
            table.Reference = reference;
            if (table.AutoFilter != null)
            {
                table.AutoFilter.Reference = reference;
            }

            if (target.Source != null)
            {
                uint sourceId = target.Source.Part.Table.Id?.Value ?? 0U;
                table.Id = sourceId == 0U
                    ? NextAvailableTableId(workbookPart)
                    : sourceId;
                target.Source.Part.Table = table;
                target.Source.Part.Table.Save();
                return;
            }

            table.Id = NextAvailableTableId(workbookPart);
            TableDefinitionPart added =
                worksheetPart.AddNewPart<TableDefinitionPart>();
            added.Table = table;
            added.Table.Save();
            TableParts tableParts = worksheetPart.Worksheet.GetFirstChild<TableParts>();
            if (tableParts == null)
            {
                tableParts = new TableParts();
                WorksheetExtensionList extensions =
                    worksheetPart.Worksheet.GetFirstChild<WorksheetExtensionList>();
                if (extensions == null)
                {
                    worksheetPart.Worksheet.Append(tableParts);
                }
                else
                {
                    worksheetPart.Worksheet.InsertBefore(tableParts, extensions);
                }
            }

            tableParts.Append(new TablePart
            {
                Id = worksheetPart.GetIdOfPart(added)
            });
            tableParts.Count = (uint)tableParts.ChildElements.Count;
        }

        private static uint NextAvailableTableId(WorkbookPart workbookPart)
        {
            var used = new HashSet<uint>(workbookPart.WorksheetParts
                .SelectMany(sheet => sheet.TableDefinitionParts)
                .Select(part => part.Table?.Id?.Value ?? 0U)
                .Where(value => value != 0U));
            uint candidate = 1U;
            while (used.Contains(candidate))
            {
                candidate++;
                if (candidate == 0U)
                {
                    throw new InvalidDataException(
                        "Workbook has no available Excel table ID.");
                }
            }

            return candidate;
        }

        private static void ReplaceManagedCells(
            WorksheetPart sourceSheet,
            WorksheetPart generatedSheet,
            int firstColumn,
            uint firstRow,
            int lastColumn,
            uint sourceLastRow,
            uint generatedFirstRow,
            uint generatedLastRow,
            int rowOffset)
        {
            SheetData sourceData = sourceSheet.Worksheet.GetFirstChild<SheetData>() ??
                                    throw new InvalidDataException(
                                        "Source worksheet data is missing.");
            SheetData generatedData = generatedSheet.Worksheet.GetFirstChild<SheetData>() ??
                                      throw new InvalidDataException(
                                          "Generated worksheet data is missing.");

            Dictionary<uint, Row> sourceRows = sourceData.Elements<Row>()
                .Where(row => row.RowIndex.HasValue)
                .ToDictionary(row => row.RowIndex.Value);
            Dictionary<uint, Row> generatedRows = generatedData.Elements<Row>()
                .Where(row => row.RowIndex.HasValue)
                .ToDictionary(row => row.RowIndex.Value);
            long mappedGeneratedLastRow = (long)generatedLastRow + rowOffset;
            uint endRow = Math.Max(
                sourceLastRow,
                mappedGeneratedLastRow > uint.MaxValue
                    ? uint.MaxValue
                    : (uint)mappedGeneratedLastRow);
            var affectedRows = new SortedSet<uint>(sourceRows.Keys.Where(row =>
                row >= firstRow && row <= endRow));
            foreach (uint generatedRowIndex in generatedRows.Keys.Where(row =>
                         row >= generatedFirstRow && row <= generatedLastRow))
            {
                long mappedRow = (long)generatedRowIndex + rowOffset;
                if (mappedRow >= firstRow && mappedRow <= endRow)
                {
                    affectedRows.Add((uint)mappedRow);
                }
            }

            foreach (uint rowIndex in affectedRows)
            {
                sourceRows.TryGetValue(rowIndex, out Row sourceRow);
                long generatedRowNumber = (long)rowIndex - rowOffset;
                Row generatedRow = generatedRowNumber >= 0L &&
                                   generatedRowNumber <= uint.MaxValue &&
                                   generatedRows.TryGetValue(
                                       (uint)generatedRowNumber,
                                       out Row mappedGeneratedRow)
                    ? mappedGeneratedRow
                    : null;
                if (sourceRow == null && generatedRow == null)
                {
                    continue;
                }

                bool createdSourceRow = sourceRow == null;
                if (sourceRow == null)
                {
                    sourceRow = new Row { RowIndex = rowIndex };
                    sourceData.Append(sourceRow);
                    sourceRows.Add(rowIndex, sourceRow);
                }

                foreach (Cell cell in sourceRow.Elements<Cell>().ToList())
                {
                    int column = ColumnOf(cell.CellReference?.Value);
                    if (column >= firstColumn && column <= lastColumn)
                    {
                        sourceRow.RemoveChild(cell);
                    }
                }

                if (generatedRow != null)
                {
                    foreach (Cell generatedCell in generatedRow.Elements<Cell>())
                    {
                        int column = ColumnOf(generatedCell.CellReference?.Value);
                        if (column >= firstColumn && column <= lastColumn)
                        {
                            Cell mappedCell = (Cell)generatedCell.CloneNode(true);
                            mappedCell.CellReference = ColumnName(column) +
                                                       rowIndex.ToString(
                                                           System.Globalization.CultureInfo.InvariantCulture);
                            sourceRow.Append(mappedCell);
                        }
                    }
                }

                List<Cell> ordered = sourceRow.Elements<Cell>()
                    .OrderBy(cell => ColumnOf(cell.CellReference?.Value))
                    .ToList();
                foreach (Cell cell in ordered)
                {
                    sourceRow.RemoveChild(cell);
                }

                foreach (Cell cell in ordered)
                {
                    sourceRow.Append(cell);
                }

                if (createdSourceRow && !sourceRow.ChildElements.Any())
                {
                    sourceData.RemoveChild(sourceRow);
                }
            }

            List<Row> orderedRows = sourceData.Elements<Row>()
                .OrderBy(row => row.RowIndex?.Value ?? 0U)
                .ToList();
            foreach (Row row in orderedRows)
            {
                sourceData.RemoveChild(row);
            }

            foreach (Row row in orderedRows)
            {
                sourceData.Append(row);
            }
        }

        private static void MergePipelineDefinedNames(
            WorkbookPart sourcePart,
            WorkbookPart generatedPart)
        {
            DefinedNames generatedNames = generatedPart.Workbook.GetFirstChild<DefinedNames>();
            DefinedNames sourceNames = sourcePart.Workbook.GetFirstChild<DefinedNames>();
            var generatedEnumNames = new HashSet<string>(
                (generatedNames?.Elements<DefinedName>() ??
                 Enumerable.Empty<DefinedName>())
                .Select(name => name.Name?.Value)
                .Where(name => name?.StartsWith(
                    PipelineEnumDefinedNamePrefix,
                    StringComparison.Ordinal) == true),
                StringComparer.Ordinal);
            if (sourceNames != null)
            {
                foreach (DefinedName stale in sourceNames.Elements<DefinedName>()
                             .Where(name => name.Name?.Value?.StartsWith(
                                                PipelineEnumDefinedNamePrefix,
                                                StringComparison.Ordinal) == true &&
                                            !generatedEnumNames.Contains(
                                                name.Name.Value))
                             .ToList())
                {
                    stale.Remove();
                }
            }

            if (generatedNames == null)
            {
                return;
            }

            if (sourceNames == null)
            {
                sourceNames = new DefinedNames();
                sourcePart.Workbook.Append(sourceNames);
            }

            foreach (DefinedName generatedName in generatedNames.Elements<DefinedName>())
            {
                if (string.IsNullOrEmpty(generatedName.Name?.Value))
                {
                    continue;
                }

                DefinedName sourceName = sourceNames.Elements<DefinedName>()
                    .FirstOrDefault(value =>
                        string.Equals(
                            DefinedNameKey(value),
                            DefinedNameKey(generatedName),
                            StringComparison.Ordinal));
                if (sourceName == null)
                {
                    sourceNames.Append((DefinedName)generatedName.CloneNode(true));
                }
                else
                {
                    sourceName.Text = generatedName.Text;
                    sourceName.LocalSheetId = generatedName.LocalSheetId;
                }
            }
        }

        private static string RangeReference(
            int firstColumn,
            uint firstRow,
            int lastColumn,
            uint lastRow)
        {
            return ColumnName(firstColumn) + firstRow.ToString(
                       System.Globalization.CultureInfo.InvariantCulture) +
                   ":" +
                   ColumnName(lastColumn) + lastRow.ToString(
                       System.Globalization.CultureInfo.InvariantCulture);
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

        private static int ColumnOf(string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                throw new InvalidDataException("Cell reference is missing.");
            }

            int column = 0;
            int index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                char value = char.ToUpperInvariant(reference[index]);
                if (value < 'A' || value > 'Z')
                {
                    throw new InvalidDataException("Cell reference is invalid.");
                }

                column = checked(column * 26 + value - 'A' + 1);
                index++;
            }

            return column == 0
                ? throw new InvalidDataException("Cell reference is invalid.")
                : column;
        }

        private static void ParseRange(
            string reference,
            out int firstColumn,
            out uint firstRow,
            out int lastColumn,
            out uint lastRow)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new InvalidDataException("Table range is missing.");
            }

            string[] cells = reference.Split(':');
            if (cells.Length != 2)
            {
                throw new InvalidDataException("Table range is invalid.");
            }

            firstColumn = ColumnOf(cells[0]);
            lastColumn = ColumnOf(cells[1]);
            firstRow = RowOf(cells[0]);
            lastRow = RowOf(cells[1]);
            if (firstColumn > lastColumn || firstRow > lastRow)
            {
                throw new InvalidDataException("Table range is invalid.");
            }
        }

        private static uint RowOf(string reference)
        {
            int index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                index++;
            }

            if (index == reference.Length ||
                !uint.TryParse(reference.Substring(index), out uint row) ||
                row == 0U)
            {
                throw new InvalidDataException("Cell reference is invalid.");
            }

            return row;
        }
    }
}
