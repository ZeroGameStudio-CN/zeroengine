using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetCsvExporter
    {
        public static void ExportValidationSummary(
            string outputPath,
            IEnumerable<Type> types,
            DataToolkitProjectSettings settings,
            DataValidationReport validationReport)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            validationReport ??= DataValidationReport.Empty;
            var builder = new StringBuilder();
            AppendRow(builder, "Type", "Asset", "Path", "Guid", "Errors", "Warnings");

            foreach (var type in types ?? Array.Empty<Type>())
            {
                if (!ManageableDataTypeDiscovery.IsManageableScriptableObjectType(type))
                {
                    continue;
                }

                foreach (var assetPath in AssetDiscoveryService.GetAssetPathsForType(type, settings))
                {
                    validationReport.GetIssueCountsForAssetPath(assetPath, out var errors, out var warnings);
                    AppendRow(
                        builder,
                        type.Name,
                        Path.GetFileNameWithoutExtension(assetPath),
                        assetPath,
                        AssetDatabase.AssetPathToGUID(assetPath),
                        errors.ToString(),
                        warnings.ToString());
                }
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void AppendRow(StringBuilder builder, params string[] cells)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(DataAssetCsvUtility.Escape(cells[i]));
            }

            builder.AppendLine();
        }
    }
}
