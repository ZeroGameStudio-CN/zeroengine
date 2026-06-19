using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Network.Config;

namespace ZeroEngine.Network.Editor
{
    public enum NetworkValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class NetworkValidationIssue
    {
        public NetworkValidationIssue(NetworkValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public NetworkValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class NetworkConfigValidator
    {
        public static IReadOnlyList<NetworkValidationIssue> Validate(IEnumerable<ServerConfig> serverConfigs = null)
        {
            var configs = serverConfigs != null ? serverConfigs.ToList() : LoadAssets<ServerConfig>().ToList();
            var issues = new List<NetworkValidationIssue>();

            foreach (var config in configs)
                ValidateServerConfig(issues, config);

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

        private static void ValidateServerConfig(List<NetworkValidationIssue> issues, ServerConfig config)
        {
            if (config == null)
                return;

            if (config.Environment == ServerEnvironment.Local && string.IsNullOrWhiteSpace(config.DefaultIP))
                Add(issues, NetworkValidationSeverity.Error, config, "DefaultIP", "Local server config must define a default IP.");
            if (config.DefaultPort == 0)
                Add(issues, NetworkValidationSeverity.Error, config, "DefaultPort", "Default port cannot be zero.");
            if (config.MaxPlayers <= 0)
                Add(issues, NetworkValidationSeverity.Error, config, "MaxPlayers", "Max players must be positive.");
            if (config.TargetFrameRate <= 0)
                Add(issues, NetworkValidationSeverity.Error, config, "TargetFrameRate", "Target frame rate must be positive.");
            if (config.OptimizeForHeadless && config.HeadlessTargetFrameRate <= 0)
                Add(issues, NetworkValidationSeverity.Error, config, "HeadlessTargetFrameRate", "Headless target frame rate must be positive when headless optimization is enabled.");
            if (config.EnableVSync && config.TargetFrameRate > 0)
                Add(issues, NetworkValidationSeverity.Warning, config, "EnableVSync", "VSync can override target frame rate in client/editor runs.");
        }

        private static void Add(List<NetworkValidationIssue> issues, NetworkValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new NetworkValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
