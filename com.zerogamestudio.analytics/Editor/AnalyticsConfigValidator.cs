using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.Analytics.Editor
{
    public enum AnalyticsValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class AnalyticsValidationIssue
    {
        public AnalyticsValidationIssue(AnalyticsValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public AnalyticsValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }
    }

    public static class AnalyticsConfigValidator
    {
        public static IReadOnlyList<AnalyticsValidationIssue> Validate(IEnumerable<ZGSAnalyticsConfig> configs = null)
        {
            var configList = configs != null ? configs.ToList() : LoadAssets<ZGSAnalyticsConfig>().ToList();
            var issues = new List<AnalyticsValidationIssue>();

            foreach (var config in configList)
                ValidateConfig(issues, config);

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

        private static void ValidateConfig(List<AnalyticsValidationIssue> issues, ZGSAnalyticsConfig config)
        {
            if (config == null || !config.EnableAnalytics)
                return;

            if (string.IsNullOrWhiteSpace(config.appId))
                Add(issues, AnalyticsValidationSeverity.Error, config, "appId", "Enabled analytics config must define an app ID.");
            if (string.IsNullOrWhiteSpace(config.zgsServerUrl))
            {
                Add(issues, AnalyticsValidationSeverity.Error, config, "zgsServerUrl", "Enabled analytics config must define a server URL.");
            }
            else if (!System.Uri.TryCreate(config.zgsServerUrl, System.UriKind.Absolute, out var uri)
                     || (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps))
            {
                Add(issues, AnalyticsValidationSeverity.Error, config, "zgsServerUrl", "Analytics server URL must be an absolute HTTP or HTTPS URL.");
            }

            if (string.IsNullOrWhiteSpace(config.zgsSecret))
                Add(issues, AnalyticsValidationSeverity.Error, config, "zgsSecret", "Enabled analytics config must define an authentication secret.");
        }

        private static void Add(List<AnalyticsValidationIssue> issues, AnalyticsValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new AnalyticsValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
