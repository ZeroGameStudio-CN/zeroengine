using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DataAuthoringProviderAttribute : Attribute
    {
    }

    public sealed class DataAuthoringProfile
    {
        public DataAuthoringProfile(
            string profileId,
            string title,
            IEnumerable<IDataAuthoringAssetAdapter> adapters,
            string description = null,
            IEnumerable<IDataAuthoringImportAdapter> importAdapters = null,
            IEnumerable<IDataAuthoringPreviewProvider> previewProviders = null,
            IEnumerable<IDataAuthoringDetailSection> detailSections = null,
            DataAuthoringWindowLabels labels = null,
            DataAuthoringWindowActions actions = null)
        {
            ProfileId = profileId ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Adapters = Order(adapters, adapter => adapter.Order);
            ImportAdapters = (importAdapters ?? Array.Empty<IDataAuthoringImportAdapter>())
                .Where(adapter => adapter != null)
                .ToArray();
            PreviewProviders = Order(previewProviders, provider => provider.Order);
            DetailSections = Order(detailSections, section => section.Order);
            Labels = labels ?? DataAuthoringWindowLabels.CreateDefault();
            Actions = actions ?? DataAuthoringWindowActions.Empty;
        }

        public string ProfileId { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<IDataAuthoringAssetAdapter> Adapters { get; }
        public IReadOnlyList<IDataAuthoringImportAdapter> ImportAdapters { get; }
        public IReadOnlyList<IDataAuthoringPreviewProvider> PreviewProviders { get; }
        public IReadOnlyList<IDataAuthoringDetailSection> DetailSections { get; }
        public DataAuthoringWindowLabels Labels { get; }
        public DataAuthoringWindowActions Actions { get; }

        private static IReadOnlyList<T> Order<T>(IEnumerable<T> values, Func<T, int> orderSelector)
        {
            return (values ?? Array.Empty<T>())
                .Where(value => value != null)
                .OrderBy(orderSelector)
                .ToArray();
        }
    }

    public interface IDataAuthoringAssetAdapter
    {
        string GroupId { get; }
        string DisplayName { get; }
        int Order { get; }
        IReadOnlyList<DataAuthoringAssetRecord> GetAssets();
        Object CreateAsset();
        Object DuplicateAsset(Object source);
        void DrawInspector(Object asset);
        IReadOnlyList<DataAuthoringIssue> Validate(Object asset);
        void AddExportSheets(TabularWorkbook workbook);
    }

    public interface IDataAuthoringImportAdapter
    {
        string AdapterId { get; }
        IReadOnlyList<string> RequiredSheets { get; }
        IReadOnlyList<string> OptionalSheets { get; }
        IReadOnlyList<string> GetRequiredColumns(string sheetName);
        IReadOnlyList<string> GetKnownColumns(string sheetName);
        TabularImportPreview Preview(TabularImportWorkbook workbook, bool createMissingAssets);
        void Apply(TabularImportPreview preview);
    }

    public interface IDataAuthoringPreviewProvider
    {
        string ProviderId { get; }
        int Order { get; }
        bool CanPreview(Object asset);
        void DrawPreview(DataAuthoringPreviewContext context);
    }

    public interface IDataAuthoringDetailSection
    {
        string SectionId { get; }
        string Title { get; }
        int Order { get; }
        bool CanDraw(Object asset);
        void DrawSection(DataAuthoringPreviewContext context);
    }

    public sealed class DataAuthoringAssetRecord
    {
        public DataAuthoringAssetRecord(
            Object asset,
            string assetPath,
            string stableId,
            string displayName,
            string summary,
            Texture icon = null)
        {
            Asset = asset;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Summary = summary ?? string.Empty;
            Icon = icon;
        }

        public Object Asset { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public Texture Icon { get; }
    }

    public enum DataAuthoringIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class DataAuthoringIssue
    {
        public DataAuthoringIssue(
            DataAuthoringIssueSeverity severity,
            string assetPath,
            string assetType,
            string stableId,
            string fieldPath,
            string message)
        {
            Severity = severity;
            AssetPath = assetPath ?? string.Empty;
            AssetType = assetType ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DataAuthoringIssueSeverity Severity { get; }
        public string AssetPath { get; }
        public string AssetType { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public static DataAuthoringIssue Error(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Error, assetPath, assetType, stableId, fieldPath, message);
        }

        public static DataAuthoringIssue Warning(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Warning, assetPath, assetType, stableId, fieldPath, message);
        }

        public static DataAuthoringIssue Info(string assetPath, string assetType, string stableId, string fieldPath, string message)
        {
            return new DataAuthoringIssue(DataAuthoringIssueSeverity.Info, assetPath, assetType, stableId, fieldPath, message);
        }
    }

    public sealed class DataAuthoringPreviewContext
    {
        public DataAuthoringPreviewContext(DataAuthoringProfile profile, IDataAuthoringAssetAdapter adapter, Object asset)
        {
            Profile = profile;
            Adapter = adapter;
            Asset = asset;
        }

        public DataAuthoringProfile Profile { get; }
        public IDataAuthoringAssetAdapter Adapter { get; }
        public Object Asset { get; }
    }

    public sealed class DataAuthoringWindowActions
    {
        public static readonly DataAuthoringWindowActions Empty = new DataAuthoringWindowActions(string.Empty, null);

        public DataAuthoringWindowActions(string openIssueDashboardLabel, Action openIssueDashboard)
        {
            OpenIssueDashboardLabel = openIssueDashboardLabel ?? string.Empty;
            OpenIssueDashboard = openIssueDashboard;
        }

        public string OpenIssueDashboardLabel { get; }
        public Action OpenIssueDashboard { get; }
    }

    public sealed class DataAuthoringSeverityLabels
    {
        public string Error { get; set; } = "Error";
        public string Warning { get; set; } = "Warning";
        public string Info { get; set; } = "Info";
    }

    public sealed class DataAuthoringIssueTableLabels
    {
        public DataAuthoringSeverityLabels SeverityLabels { get; set; } = new DataAuthoringSeverityLabels();
        public string Severity { get; set; } = "Severity";
        public string Group { get; set; } = "Group";
        public string StableId { get; set; } = "Stable ID";
        public string Asset { get; set; } = "Asset";
        public string Field { get; set; } = "Field";
        public string Message { get; set; } = "Message";
        public string Action { get; set; } = "Action";
        public string Ping { get; set; } = "Ping";
        public string PingTooltip { get; set; } = "Ping asset";
        public string NoIssues { get; set; } = "No issues.";
        public string OverflowFormat { get; set; } = "Showing {0} / {1}.";
    }

    public sealed class DataAuthoringChangeTableLabels
    {
        public string Kind { get; set; } = "Kind";
        public string Sheet { get; set; } = "Sheet";
        public string Row { get; set; } = "Row";
        public string StableId { get; set; } = "Stable ID";
        public string Asset { get; set; } = "Asset";
        public string Field { get; set; } = "Field";
        public string Old { get; set; } = "Old";
        public string New { get; set; } = "New";
        public string Ping { get; set; } = "Ping";
        public string PingTooltip { get; set; } = "Ping asset";
        public string NoChanges { get; set; } = "No changes.";
        public string OverflowFormat { get; set; } = "Showing {0} / {1}.";
    }

    public sealed class DataAuthoringWindowLabels
    {
        public DataAuthoringSeverityLabels SeverityLabels { get; set; } = new DataAuthoringSeverityLabels();
        public DataAuthoringIssueTableLabels IssueTableLabels { get; set; } = new DataAuthoringIssueTableLabels();
        public DataAuthoringChangeTableLabels ChangeTableLabels { get; set; } = new DataAuthoringChangeTableLabels();
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
        public string IssueScopeSelected { get; set; } = "Selected";
        public string IssueScopeGroup { get; set; } = "Group";
        public string IssueScopeAll { get; set; } = "All";
        public string IssueScopeImportPreview { get; set; } = "Import Preview";
        public string IssueScopeApplyImport { get; set; } = "Apply Import";
        public string IssueSummaryFormat { get; set; } = "{0} ({1}) {2} errors / {3} warnings / {4} info";
        public string SearchProblems { get; set; } = "Search Problems";
        public string ApplyImport { get; set; } = "Apply Import";
        public string Apply { get; set; } = "Apply";
        public string Cancel { get; set; } = "Cancel";
        public string Clear { get; set; } = "Clear";
        public string ChangesSummaryFormat { get; set; } = "Changes: {0} Errors: {1} Warnings: {2}";
        public string ImportDiff { get; set; } = "Import Diff";
        public string ImportIssues { get; set; } = "Import Issues";
        public string SearchDiffRows { get; set; } = "Search Diff";
        public string SearchImportIssues { get; set; } = "Search Import Issues";
        public string ExportCsvDefaultFolder { get; set; } = "DataAuthoringExport";
        public string ImportFolderDialogTitle { get; set; } = "Import CSV/TSV";
        public string ApplyImportDialogTitle { get; set; } = "Apply Import";
        public string ApplyImportDialogFormat { get; set; } = "Apply {0} changes from {1}?";

        public static DataAuthoringWindowLabels CreateDefault()
        {
            return new DataAuthoringWindowLabels();
        }
    }

    public class DataAuthoringLockedField
    {
        public DataAuthoringLockedField(string fieldPath, string displayName, string reason)
        {
            FieldPath = fieldPath ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string FieldPath { get; }
        public string DisplayName { get; }
        public string Reason { get; }
    }

    public interface IDataAuthoringFieldLockProvider
    {
        string ProviderId { get; }
        IReadOnlyList<DataAuthoringLockedField> GetLockedFields(Type assetType);
    }

    public static class DataAuthoringFieldLockRegistry
    {
        private static readonly List<IDataAuthoringFieldLockProvider> Providers = new List<IDataAuthoringFieldLockProvider>();

        public static void Register(IDataAuthoringFieldLockProvider provider)
        {
            if (provider == null || Providers.Any(existing => existing.GetType() == provider.GetType()))
            {
                return;
            }

            Providers.Add(provider);
        }

        public static bool TryGetLockedField(Type assetType, string fieldPath, out DataAuthoringLockedField lockedField)
        {
            lockedField = null;
            if (assetType == null || string.IsNullOrWhiteSpace(fieldPath))
            {
                return false;
            }

            foreach (var provider in Providers)
            {
                var fields = provider.GetLockedFields(assetType) ?? Array.Empty<DataAuthoringLockedField>();
                foreach (var field in fields)
                {
                    if (field != null && string.Equals(field.FieldPath, fieldPath, StringComparison.Ordinal))
                    {
                        lockedField = field;
                        return true;
                    }
                }
            }

            return false;
        }

        public static void ClearForTests()
        {
            Providers.Clear();
        }
    }

    public static class DataAuthoringFieldLockUtility
    {
        public static string BuildAssignedValueDisableExpression(string fieldPath, Type fieldType, bool isLocked)
        {
            if (!isLocked || string.IsNullOrWhiteSpace(fieldPath) || fieldType == null)
            {
                return string.Empty;
            }

            if (fieldType == typeof(string))
            {
                return $"@!string.IsNullOrWhiteSpace({fieldPath})";
            }

            if (typeof(Object).IsAssignableFrom(fieldType))
            {
                return $"@{fieldPath} != null";
            }

            return "@true";
        }
    }

    public sealed class DataAuthoringReferenceRow
    {
        public DataAuthoringReferenceRow(string assetPath, string referenceKind, string assetType = null)
        {
            AssetPath = assetPath ?? string.Empty;
            ReferenceKind = referenceKind ?? string.Empty;
            AssetType = assetType ?? ReferenceKind;
        }

        public string AssetPath { get; }
        public string ReferenceKind { get; }
        public string AssetType { get; }
    }

    public sealed class DataAuthoringReferenceTableResult
    {
        public DataAuthoringReferenceTableResult(IReadOnlyList<DataAuthoringReferenceRow> rows, int totalCount)
        {
            Rows = rows ?? Array.Empty<DataAuthoringReferenceRow>();
            TotalCount = Math.Max(0, totalCount);
        }

        public IReadOnlyList<DataAuthoringReferenceRow> Rows { get; }
        public int TotalCount { get; }
    }

    public static class DataAuthoringReferenceTable
    {
        public static void Draw(DataAuthoringReferenceTableResult result, Action<DataAuthoringReferenceRow> ping, string pingLabel = "Ping")
        {
            var rows = result?.Rows ?? Array.Empty<DataAuthoringReferenceRow>();
            foreach (var row in rows)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(row.ReferenceKind, GUILayout.Width(80f));
                    EditorGUILayout.LabelField(row.AssetPath);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(row.AssetPath)))
                    {
                        if (GUILayout.Button(pingLabel ?? "Ping", GUILayout.Width(64f)))
                        {
                            ping?.Invoke(row);
                        }
                    }
                }
            }
        }
    }
}
