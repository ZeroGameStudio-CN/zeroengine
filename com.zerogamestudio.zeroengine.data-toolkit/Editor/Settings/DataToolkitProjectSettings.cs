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
            DataToolkitDefaultInspectorMode defaultInspectorMode = DataToolkitDefaultInspectorMode.FullInspector)
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? "ZGS" : projectId.Trim();
            WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "Data Manager" : windowTitle.Trim();
            MenuPath = string.IsNullOrWhiteSpace(menuPath) ? "Tools/Data Manager" : menuPath.Trim();
            EditorPrefsPrefix = string.IsNullOrWhiteSpace(editorPrefsPrefix) ? ProjectId : editorPrefsPrefix.Trim();
            SearchRoots = NormalizePaths(searchRoots).ToArray();
            ExcludedPaths = NormalizePaths(excludedPaths).ToArray();
            DefaultInspectorMode = defaultInspectorMode;
        }

        public string ProjectId { get; }
        public string WindowTitle { get; }
        public string MenuPath { get; }
        public string EditorPrefsPrefix { get; }
        public IReadOnlyList<string> SearchRoots { get; }
        public IReadOnlyList<string> ExcludedPaths { get; }
        public DataToolkitDefaultInspectorMode DefaultInspectorMode { get; }

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
    }
}
