using System.Globalization;
using System.IO;
using System.Text;

namespace ZGS.DataToolkit.Editor
{
    public static class ConfigValidationReportWriter
    {
        public static void WriteMarkdown(ConfigValidationReport report, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder(4096);
            builder.Append("# ").AppendLine(EscapeMarkdownText(report.Title));
            builder.AppendLine();
            builder.AppendLine("| Severity | Code | Asset | Object | Message |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var issue in report.GetDeterministicIssues())
            {
                builder
                    .Append("| ")
                    .Append(issue.Severity)
                    .Append(" | ")
                    .Append(EscapeTableCell(issue.Code))
                    .Append(" | ")
                    .Append(EscapeTableCell(issue.AssetPath))
                    .Append(" | ")
                    .Append(EscapeTableCell(issue.ObjectName))
                    .Append(" | ")
                    .Append(EscapeTableCell(issue.Message))
                    .AppendLine(" |");
            }

            builder.AppendLine();
            builder.Append("Errors: ").AppendLine(report.ErrorCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Warnings: ").AppendLine(report.WarningCount.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        private static string EscapeMarkdownText(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }

        private static string EscapeTableCell(string value)
        {
            return EscapeMarkdownText(value).Replace("|", "\\|");
        }
    }
}
