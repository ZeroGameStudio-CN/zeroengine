using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Settings;

namespace ZeroEngine.Persistence.Editor
{
    public enum PersistenceValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class PersistenceValidationIssue
    {
        public PersistenceValidationIssue(PersistenceValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public PersistenceValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class PersistenceConfigValidator
    {
        public static IReadOnlyList<PersistenceValidationIssue> Validate(IEnumerable<SettingsDefinitionSO> settingsDefinitions = null)
        {
            var definitions = settingsDefinitions != null ? settingsDefinitions.ToList() : LoadAssets<SettingsDefinitionSO>().ToList();
            var issues = new List<PersistenceValidationIssue>();

            foreach (var definition in definitions)
                ValidateSettingsDefinition(issues, definition);

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

        private static void ValidateSettingsDefinition(List<PersistenceValidationIssue> issues, SettingsDefinitionSO asset)
        {
            if (asset == null)
                return;

            var settings = asset.Settings ?? new List<SettingDefinition>();
            if (settings.Count == 0)
                Add(issues, PersistenceValidationSeverity.Warning, asset, "Settings", "Settings definition is empty.");

            var keys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                string path = $"Settings[{i}]";
                if (setting == null)
                {
                    Add(issues, PersistenceValidationSeverity.Error, asset, path, "Setting definition is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(setting.Key))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Key", "Setting must have a stable key.");
                else if (!keys.Add(setting.Key.Trim()))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Key", $"Duplicate setting key '{setting.Key.Trim()}'.");

                if (string.IsNullOrWhiteSpace(setting.DisplayName))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.DisplayName", "Setting must have a display name.");
                if (string.IsNullOrWhiteSpace(setting.Description))
                    Add(issues, PersistenceValidationSeverity.Warning, asset, $"{path}.Description", "Setting should have a description.");

                ValidateSettingValue(issues, asset, path, setting);
            }

            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                if (setting == null || string.IsNullOrWhiteSpace(setting.DependsOnKey))
                    continue;

                if (!keys.Contains(setting.DependsOnKey.Trim()))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"Settings[{i}].DependsOnKey", $"DependsOnKey '{setting.DependsOnKey}' does not exist in this definition.");
                if (string.IsNullOrWhiteSpace(setting.DependsOnValue))
                    Add(issues, PersistenceValidationSeverity.Warning, asset, $"Settings[{i}].DependsOnValue", "Dependent settings should declare the required value.");
            }
        }

        private static void ValidateSettingValue(List<PersistenceValidationIssue> issues, SettingsDefinitionSO asset, string path, SettingDefinition setting)
        {
            bool numeric = setting.ValueType == SettingValueType.Int
                           || setting.ValueType == SettingValueType.Float
                           || setting.ValueType == SettingValueType.Slider;

            if (numeric && setting.MinValue > setting.MaxValue)
                Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.MinValue", "Minimum value cannot exceed maximum value.");
            if (numeric && setting.Step <= 0f)
                Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Step", "Numeric setting step must be positive.");

            switch (setting.ValueType)
            {
                case SettingValueType.Bool:
                    if (!string.Equals(setting.DefaultValue, "true", System.StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(setting.DefaultValue, "false", System.StringComparison.OrdinalIgnoreCase)
                        && setting.DefaultValue != "0"
                        && setting.DefaultValue != "1")
                        Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.DefaultValue", "Bool setting default value must be true, false, 0, or 1.");
                    break;
                case SettingValueType.Int:
                    if (!int.TryParse(setting.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.DefaultValue", "Int setting default value must parse as an integer.");
                    break;
                case SettingValueType.Float:
                case SettingValueType.Slider:
                    if (!float.TryParse(setting.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.DefaultValue", "Float or slider setting default value must parse as a number.");
                    break;
                case SettingValueType.Enum:
                    ValidateOptions(issues, asset, path, setting);
                    break;
            }
        }

        private static void ValidateOptions(List<PersistenceValidationIssue> issues, SettingsDefinitionSO asset, string path, SettingDefinition setting)
        {
            var options = setting.Options ?? new List<SettingOption>();
            if (options.Count == 0)
            {
                Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Options", "Enum setting must define at least one option.");
                return;
            }

            var values = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null)
                {
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Options[{i}]", "Setting option is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(option.Value))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Options[{i}].Value", "Setting option must have a value.");
                else if (!values.Add(option.Value.Trim()))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Options[{i}].Value", $"Duplicate option value '{option.Value.Trim()}'.");
                if (string.IsNullOrWhiteSpace(option.DisplayName))
                    Add(issues, PersistenceValidationSeverity.Error, asset, $"{path}.Options[{i}].DisplayName", "Setting option must have a display name.");
            }
        }

        private static void Add(List<PersistenceValidationIssue> issues, PersistenceValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new PersistenceValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
