using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigPresetResetPreview
    {
        internal ConfigPresetResetPreview(
            ConfigPipelinePreparedPlan preparedPlan,
            string sourcePlanId,
            ConfigEffectiveValue current,
            ConfigEffectiveValue inherited,
            string sourceWorkbookHash,
            string candidateWorkbookHash)
        {
            PreparedPlan = preparedPlan;
            SourcePlanId = sourcePlanId;
            ResetPlanId = preparedPlan.Plan.PlanId;
            TargetArtifactPath = current.ArtifactPath;
            JsonPath = current.JsonPath;
            CurrentCanonicalValue = current.CanonicalValue;
            InheritedCanonicalValue = inherited.CanonicalValue;
            Workbook = current.Workbook;
            Sheet = current.Sheet;
            Row = current.Row;
            Column = current.Column;
            SourceWorkbookHash = sourceWorkbookHash;
            CandidateWorkbookHash = candidateWorkbookHash;
        }

        internal ConfigPipelinePreparedPlan PreparedPlan { get; }
        public string SourcePlanId { get; }
        public string ResetPlanId { get; }
        public string TargetArtifactPath { get; }
        public string JsonPath { get; }
        public string CurrentCanonicalValue { get; }
        public string InheritedCanonicalValue { get; }
        public string Workbook { get; }
        public string Sheet { get; }
        public int Row { get; }
        public int Column { get; }
        public string SourceWorkbookHash { get; }
        public string CandidateWorkbookHash { get; }
    }

    public sealed partial class ConfigPipelineService
    {
        public ConfigPresetResetPreview PlanPresetReset(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            string targetArtifactPath,
            string jsonPath)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            string targetPath = ConfigPathGuard.NormalizeRelativePath(targetArtifactPath);
            ConfigPipelinePreparedPlan current = Plan(
                root,
                profileRelativePath,
                configSetId,
                packageIdentity);
            ConfigEffectiveValue selected = FindEffectiveValue(current, targetPath, jsonPath);
            if (!selected.HasEditableInstanceCell)
            {
                throw new InvalidOperationException(
                    "CONFIG_RESET_REQUIRES_INSTANCE_WORKBOOK_CELL");
            }

            string workbookPath = ConfigPathGuard.NormalizeRelativePath(selected.Workbook);
            string workbookAbsolute = ConfigPathGuard.ResolveInside(root, workbookPath);
            byte[] sourceWorkbook = File.ReadAllBytes(workbookAbsolute);
            byte[] candidateWorkbook = ConfigWorkbookCellEditor.Clear(
                sourceWorkbook,
                selected.Sheet,
                selected.Row,
                selected.Column);
            var overrides = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { workbookPath, candidateWorkbook }
            };
            ConfigPipelinePreparedPlan candidate = PlanInternal(
                root,
                profileRelativePath,
                configSetId,
                packageIdentity,
                overrides);
            ConfigEffectiveValue inherited = FindEffectiveValue(candidate, targetPath, jsonPath);
            if (inherited.SourceKind != ConfigValueSourceKind.Preset)
            {
                throw new InvalidOperationException(
                    "CONFIG_RESET_TARGET_DOES_NOT_INHERIT_PRESET");
            }

            return new ConfigPresetResetPreview(
                candidate,
                current.Plan.PlanId,
                selected,
                inherited,
                ConfigHash.Sha256(sourceWorkbook),
                ConfigHash.Sha256(candidateWorkbook));
        }

        public ConfigApplyResult ApplyExpectedPresetReset(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            string targetArtifactPath,
            string jsonPath,
            string expectedSourcePlanId,
            string expectedResetPlanId)
        {
            if (string.IsNullOrWhiteSpace(expectedSourcePlanId) ||
                string.IsNullOrWhiteSpace(expectedResetPlanId))
            {
                throw new ArgumentException("Expected source and reset Plan IDs are required.");
            }

            ConfigPresetResetPreview preview = PlanPresetReset(
                projectRoot,
                profileRelativePath,
                configSetId,
                packageIdentity,
                targetArtifactPath,
                jsonPath);
            if (!string.Equals(preview.SourcePlanId, expectedSourcePlanId, StringComparison.Ordinal) ||
                !string.Equals(preview.ResetPlanId, expectedResetPlanId, StringComparison.Ordinal))
            {
                throw new ConfigPlanStaleException("CONFIG_PLAN_CHANGED_REPLAN_REQUIRED");
            }

            return new ConfigTransactionalApplier().Apply(
                projectRoot,
                preview.PreparedPlan.Plan,
                packageIdentity,
                preview.PreparedPlan.Artifacts,
                () => Check(projectRoot, profileRelativePath, configSetId, packageIdentity));
        }

        private static ConfigEffectiveValue FindEffectiveValue(
            ConfigPipelinePreparedPlan plan,
            string artifactPath,
            string jsonPath)
        {
            ConfigEffectiveValue[] matches = plan.EffectiveValues
                .Where(value =>
                    string.Equals(value.ArtifactPath, artifactPath, StringComparison.Ordinal) &&
                    string.Equals(value.JsonPath, jsonPath, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "CONFIG_EFFECTIVE_VALUE_NOT_UNIQUE: " + artifactPath + " " + jsonPath);
            }

            return matches[0];
        }
    }

    internal static class ConfigWorkbookCellEditor
    {
        public static byte[] Clear(byte[] source, string sheetName, int row, int column)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(sheetName) || row <= 0 || column <= 0)
            {
                throw new ArgumentException("Workbook cell coordinates are invalid.");
            }

            byte[] editedPackage;
            using (var editable = new MemoryStream())
            {
                editable.Write(source, 0, source.Length);
                editable.Position = 0;
                using (SpreadsheetDocument document = SpreadsheetDocument.Open(editable, true))
                {
                    WorkbookPart workbookPart = document.WorkbookPart ??
                        throw new InvalidDataException("Workbook part is missing.");
                    Sheet sheet = workbookPart.Workbook.Sheets?
                        .Elements<Sheet>()
                        .SingleOrDefault(value =>
                            string.Equals(value.Name?.Value, sheetName, StringComparison.Ordinal));
                    if (sheet == null)
                    {
                        throw new InvalidDataException("Workbook sheet is missing: " + sheetName);
                    }

                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                    string reference = ColumnName(column) + row;
                    Cell cell = worksheetPart.Worksheet
                        .GetFirstChild<SheetData>()?
                        .Elements<Row>()
                        .Where(value => value.RowIndex?.Value == (uint)row)
                        .SelectMany(value => value.Elements<Cell>())
                        .SingleOrDefault(value =>
                            string.Equals(value.CellReference?.Value, reference, StringComparison.Ordinal));
                    if (cell == null)
                    {
                        throw new ConfigPlanStaleException(
                            "CONFIG_RESET_SOURCE_CELL_MISSING: " + sheetName + "!" + reference);
                    }

                    cell.RemoveAllChildren<CellValue>();
                    cell.RemoveAllChildren<InlineString>();
                    cell.RemoveAllChildren<CellFormula>();
                    cell.DataType = null;
                    worksheetPart.Worksheet.Save();
                }

                editedPackage = editable.ToArray();
            }

            using (var deterministic = new MemoryStream())
            {
                XlsxConfigWorkbookWriter.WriteDeterministicPackage(editedPackage, deterministic);
                return deterministic.ToArray();
            }
        }

        private static string ColumnName(int column)
        {
            string name = string.Empty;
            while (column > 0)
            {
                column--;
                name = (char)('A' + column % 26) + name;
                column /= 26;
            }

            return name;
        }
    }
}
