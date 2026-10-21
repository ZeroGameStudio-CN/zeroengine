using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitProjectSettings
    {
        public DataToolkitProjectSettings(
            string projectId,
            string windowTitle,
            string menuPath,
            string editorPrefsPrefix,
            IEnumerable<string> searchRoots,
            IEnumerable<string> excludedPaths,
            DataToolkitDefaultInspectorMode defaultInspectorMode = DataToolkitDefaultInspectorMode.FullInspector,
            IEnumerable<DataToolkitSafeInspectorRule> safeInspectorRules = null,
            DataToolkitUiText uiText = null)
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? "ZGS" : projectId.Trim();
            WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "Data Manager" : windowTitle.Trim();
            MenuPath = string.IsNullOrWhiteSpace(menuPath) ? "Tools/Data Manager" : menuPath.Trim();
            EditorPrefsPrefix = string.IsNullOrWhiteSpace(editorPrefsPrefix) ? ProjectId : editorPrefsPrefix.Trim();
            SearchRoots = NormalizePaths(searchRoots).ToArray();
            ExcludedPaths = NormalizePaths(excludedPaths).ToArray();
            DefaultInspectorMode = defaultInspectorMode;
            SafeInspectorRules = NormalizeSafeInspectorRules(safeInspectorRules).ToArray();
            UiText = uiText ?? DataToolkitUiText.English;
        }

        public string ProjectId { get; }
        public string WindowTitle { get; }
        public string MenuPath { get; }
        public string EditorPrefsPrefix { get; }
        public IReadOnlyList<string> SearchRoots { get; }
        public IReadOnlyList<string> ExcludedPaths { get; }
        public DataToolkitDefaultInspectorMode DefaultInspectorMode { get; }
        public IReadOnlyList<DataToolkitSafeInspectorRule> SafeInspectorRules { get; }
        public DataToolkitUiText UiText { get; }

        public string PrefKey(string suffix)
        {
            return $"{EditorPrefsPrefix}_{suffix}";
        }

        private static IEnumerable<string> NormalizePaths(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                yield break;
            }

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                yield return path.Replace('\\', '/').TrimEnd('/');
            }
        }

        private static IEnumerable<DataToolkitSafeInspectorRule> NormalizeSafeInspectorRules(
            IEnumerable<DataToolkitSafeInspectorRule> rules)
        {
            return (rules ?? Array.Empty<DataToolkitSafeInspectorRule>()).Where(rule => rule != null);
        }
    }

    public sealed class DataToolkitUiText
    {
        public static DataToolkitUiText English { get; } = new DataToolkitUiText();

        public DataToolkitUiText(
            string refresh = "Refresh",
            string diagnostics = "Diagnostics",
            string dataTypes = "Data Types",
            string assets = "Assets",
            string ping = "Ping",
            string openFullInspector = "Open Full Inspector",
            string selectAssetPrompt = "Select a data asset from the middle column.",
            string largeAssetDeferred = "This asset is large, so the full inspector is deferred to keep Data Manager responsive.",
            string fullInspectorHidden = "The full inspector is hidden by default to keep Data Manager responsive.",
            string type = "Type",
            string path = "Path",
            string size = "Size",
            string unknown = "(unknown)",
            string assetSummaryFormat = "{0} types / {1}{2} assets",
            string browse = "Browse Data",
            string inspector = "Asset Details")
        {
            Refresh = refresh;
            Diagnostics = diagnostics;
            DataTypes = dataTypes;
            Assets = assets;
            Ping = ping;
            OpenFullInspector = openFullInspector;
            SelectAssetPrompt = selectAssetPrompt;
            LargeAssetDeferred = largeAssetDeferred;
            FullInspectorHidden = fullInspectorHidden;
            Type = type;
            Path = path;
            Size = size;
            Unknown = unknown;
            AssetSummaryFormat = assetSummaryFormat;
            Browse = browse;
            Inspector = inspector;
        }

        public string Refresh { get; }
        public string Diagnostics { get; }
        public string DataTypes { get; }
        public string Assets { get; }
        public string Ping { get; }
        public string OpenFullInspector { get; }
        public string SelectAssetPrompt { get; }
        public string LargeAssetDeferred { get; }
        public string FullInspectorHidden { get; }
        public string Type { get; }
        public string Path { get; }
        public string Size { get; }
        public string Unknown { get; }
        public string AssetSummaryFormat { get; }
        public string Browse { get; }
        public string Inspector { get; }
    }
}
