using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.UI;
using ZeroEngine.UI.Toast;

namespace ZeroEngine.UI.Editor
{
    public enum UIValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class UIValidationIssue
    {
        public UIValidationIssue(UIValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public UIValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }
    }

    public static class UIConfigValidator
    {
        public static IReadOnlyList<UIValidationIssue> Validate(
            IEnumerable<UIViewDatabase> viewDatabases = null,
            IEnumerable<ToastSettings> toastSettings = null)
        {
            bool loadAll = viewDatabases == null && toastSettings == null;
            var databases = Resolve(viewDatabases, loadAll);
            var settings = Resolve(toastSettings, loadAll);
            var issues = new List<UIValidationIssue>();

            foreach (var database in databases)
                ValidateViewDatabase(issues, database);
            foreach (var setting in settings)
                ValidateToastSettings(issues, setting);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static List<T> Resolve<T>(IEnumerable<T> source, bool loadAll) where T : UnityEngine.Object
        {
            if (source != null)
                return source.ToList();

            return loadAll ? LoadAssets<T>().ToList() : new List<T>();
        }

        private static void ValidateViewDatabase(List<UIValidationIssue> issues, UIViewDatabase database)
        {
            if (database == null)
                return;

            var entries = database.views ?? new List<UIViewEntry>();
            if (entries.Count == 0)
                Add(issues, UIValidationSeverity.Warning, database, "views", "UI view database has no views.");

            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    Add(issues, UIValidationSeverity.Error, database, $"views[{i}]", "UI view entry is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.viewName))
                {
                    Add(issues, UIValidationSeverity.Error, database, $"views[{i}].viewName", "UI view entry must have a stable view name.");
                }
                else if (!names.Add(entry.viewName.Trim()))
                {
                    Add(issues, UIValidationSeverity.Error, database, $"views[{i}].viewName", $"Duplicate UI view name '{entry.viewName.Trim()}'.");
                }

                if (entry.prefab == null)
                    Add(issues, UIValidationSeverity.Warning, database, $"views[{i}].prefab", "UI view entry has no prefab reference.");
                if (entry.animationDuration <= 0f)
                    Add(issues, UIValidationSeverity.Error, database, $"views[{i}].animationDuration", "UI view animation duration must be positive.");
            }
        }

        private static void ValidateToastSettings(List<UIValidationIssue> issues, ToastSettings settings)
        {
            if (settings == null)
                return;

            var styles = settings.Styles ?? new List<ToastStyle>();
            var severities = new HashSet<ToastSeverity>();
            for (int i = 0; i < styles.Count; i++)
            {
                var style = styles[i];
                if (style == null)
                {
                    Add(issues, UIValidationSeverity.Error, settings, $"Styles[{i}]", "Toast style is empty.");
                    continue;
                }

                if (!severities.Add(style.Severity))
                    Add(issues, UIValidationSeverity.Error, settings, $"Styles[{i}].Severity", $"Duplicate toast style severity '{style.Severity}'.");
                if (style.Duration <= 0f)
                    Add(issues, UIValidationSeverity.Error, settings, $"Styles[{i}].Duration", "Toast style duration must be positive.");
            }

            foreach (ToastSeverity severity in System.Enum.GetValues(typeof(ToastSeverity)))
            {
                if (!severities.Contains(severity))
                    Add(issues, UIValidationSeverity.Warning, settings, "Styles", $"Toast settings are missing style for severity '{severity}'.");
            }
        }

        private static void Add(List<UIValidationIssue> issues, UIValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new UIValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
