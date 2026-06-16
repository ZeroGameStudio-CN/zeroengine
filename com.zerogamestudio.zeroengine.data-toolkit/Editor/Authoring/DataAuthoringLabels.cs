using System;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringSeverityLabels
    {
        public static DataAuthoringSeverityLabels Default => new();

        public string Error { get; set; } = "Error";
        public string Warning { get; set; } = "Warning";
        public string Info { get; set; } = "Info";

        public string Format(DataAuthoringIssueSeverity severity)
        {
            return severity switch
            {
                DataAuthoringIssueSeverity.Error => Error,
                DataAuthoringIssueSeverity.Warning => Warning,
                _ => Info
            };
        }
    }

    public sealed class DataAuthoringIssueTableLabels
    {
        public static DataAuthoringIssueTableLabels Default => new();

        public DataAuthoringSeverityLabels SeverityLabels { get; set; } = DataAuthoringSeverityLabels.Default;
        public string Severity { get; set; } = "Severity";
        public string Group { get; set; } = "Group";
        public string StableId { get; set; } = "Stable ID";
        public string Asset { get; set; } = "Asset";
        public string Field { get; set; } = "Field";
        public string Message { get; set; } = "Message";
        public string Action { get; set; } = string.Empty;
        public string Ping { get; set; } = "Ping";
        public string PingTooltip { get; set; } = "Locate the affected asset.";
        public string NoIssues { get; set; } = "No issues.";
        public string OverflowFormat { get; set; } = "Showing first {0} of {1} problem rows.";
        public string Error
        {
            get => SeverityLabels.Error;
            set => SeverityLabels = new DataAuthoringSeverityLabels
            {
                Error = value,
                Warning = SeverityLabels.Warning,
                Info = SeverityLabels.Info
            };
        }

        public string Warning
        {
            get => SeverityLabels.Warning;
            set => SeverityLabels = new DataAuthoringSeverityLabels
            {
                Error = SeverityLabels.Error,
                Warning = value,
                Info = SeverityLabels.Info
            };
        }

        public string Info
        {
            get => SeverityLabels.Info;
            set => SeverityLabels = new DataAuthoringSeverityLabels
            {
                Error = SeverityLabels.Error,
                Warning = SeverityLabels.Warning,
                Info = value
            };
        }
    }

    public sealed class DataAuthoringChangeTableLabels
    {
        public static DataAuthoringChangeTableLabels Default => new();

        public string Kind { get; set; } = "Kind";
        public string Sheet { get; set; } = "Sheet";
        public string Row { get; set; } = "Row";
        public string StableId { get; set; } = "Stable ID";
        public string Asset { get; set; } = "Asset";
        public string Field { get; set; } = "Field";
        public string Old { get; set; } = "Old";
        public string New { get; set; } = "New";
        public string Ping { get; set; } = "Ping";
        public string PingTooltip { get; set; } = "Locate the affected asset.";
        public string NoChanges { get; set; } = "No changes.";
        public string OverflowFormat { get; set; } = "Showing first {0} of {1} diff rows.";
    }

    public sealed class DataAuthoringWindowLabels
    {
        public static DataAuthoringWindowLabels Default => new();

        public DataAuthoringSeverityLabels SeverityLabels { get; set; } = DataAuthoringSeverityLabels.Default;
        public DataAuthoringIssueTableLabels IssueTableLabels { get; set; } = DataAuthoringIssueTableLabels.Default;
        public DataAuthoringChangeTableLabels ChangeTableLabels { get; set; } = DataAuthoringChangeTableLabels.Default;
        public string Groups { get; set; } = "Groups";
        public string Assets { get; set; } = "Assets";
        public string Tools { get; set; } = "Tools";
        public string ValidateSelected { get; set; } = "Validate Selected";
        public string ValidateGroup { get; set; } = "Validate Group";
        public string ValidateAll { get; set; } = "Validate All";
        public string ExportCsv { get; set; } = "Export CSV";
        public string ImportPreview { get; set; } = "Import Preview";
        public string Refresh { get; set; } = "Refresh";
        public string Create { get; set; } = "Create";
        public string Duplicate { get; set; } = "Duplicate";
        public string Ping { get; set; } = "Ping";
        public string SelectAsset { get; set; } = "Select an asset.";
        public string Problems { get; set; } = "Problems";
        public string Expand { get; set; } = "Expand";
        public string Collapse { get; set; } = "Collapse";
        public string IssueScopeSelected { get; set; } = "selected asset";
        public string IssueScopeGroup { get; set; } = "current group";
        public string IssueScopeAll { get; set; } = "all";
        public string IssueScopeImportPreview { get; set; } = "import preview";
        public string IssueScopeApplyImport { get; set; } = "apply import";
        public string IssueSummaryFormat { get; set; } = "{0} ({1})  {2} errors / {3} warnings / {4} info";
        public string SearchProblems { get; set; } = "Search problems";
        public string ApplyImport { get; set; } = "Apply Import";
        public string Apply { get; set; } = "Apply";
        public string Cancel { get; set; } = "Cancel";
        public string Clear { get; set; } = "Clear";
        public string ChangesSummaryFormat { get; set; } = "Changes: {0}    Errors: {1}    Warnings: {2}";
        public string ImportDiff { get; set; } = "Import Diff";
        public string ImportIssues { get; set; } = "Import Issues";
        public string SearchDiffRows { get; set; } = "Search diff rows";
        public string SearchImportIssues { get; set; } = "Search import issues";
        public string ExportCsvDefaultFolder { get; set; } = "RpgCharacterExport";
        public string ImportFolderDialogTitle { get; set; } = "Import CSV/TSV";
        public string ApplyImportDialogTitle { get; set; } = "Apply Import";
        public string ApplyImportDialogFormat { get; set; } = "Apply {0} data changes from {1}?";
    }

    public sealed class DataAuthoringWindowActions
    {
        public static DataAuthoringWindowActions Empty { get; } = new(string.Empty, null);

        public DataAuthoringWindowActions(string openIssueDashboardLabel = "", Action openIssueDashboard = null)
        {
            OpenIssueDashboardLabel = openIssueDashboardLabel ?? string.Empty;
            OpenIssueDashboard = openIssueDashboard;
        }

        public string OpenIssueDashboardLabel { get; }
        public Action OpenIssueDashboard { get; }
    }
}
