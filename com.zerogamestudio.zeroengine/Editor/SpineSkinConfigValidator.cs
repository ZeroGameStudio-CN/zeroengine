using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.SpineSkin;

namespace ZeroEngine.Editor
{
    public enum SpineSkinValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class SpineSkinValidationIssue
    {
        public SpineSkinValidationIssue(SpineSkinValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public SpineSkinValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }
    }

    public static class SpineSkinConfigValidator
    {
        public static IReadOnlyList<SpineSkinValidationIssue> Validate(IEnumerable<SpineSkinConfig> configs = null)
        {
            var configList = configs != null ? configs.ToList() : LoadAssets<SpineSkinConfig>().ToList();
            var issues = new List<SpineSkinValidationIssue>();

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

        private static void ValidateConfig(List<SpineSkinValidationIssue> issues, SpineSkinConfig config)
        {
            if (config == null)
                return;

            if (string.IsNullOrWhiteSpace(config.SkinNamePattern) || !config.SkinNamePattern.Contains("{slot}"))
                Add(issues, SpineSkinValidationSeverity.Error, config, "SkinNamePattern", "Skin name pattern must include the {slot} token.");
            if (config.GenderNames == null || config.GenderNames.Count == 0)
            {
                Add(issues, SpineSkinValidationSeverity.Error, config, "GenderNames", "Spine skin config must define at least one gender name.");
            }
            else
            {
                var genders = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < config.GenderNames.Count; i++)
                {
                    string gender = config.GenderNames[i]?.Trim();
                    if (string.IsNullOrEmpty(gender))
                        Add(issues, SpineSkinValidationSeverity.Error, config, $"GenderNames[{i}]", "Gender name cannot be empty.");
                    else if (!genders.Add(gender))
                        Add(issues, SpineSkinValidationSeverity.Error, config, $"GenderNames[{i}]", $"Duplicate gender name '{gender}'.");
                }

                if (config.DefaultGenderIndex < 0 || config.DefaultGenderIndex >= config.GenderNames.Count)
                    Add(issues, SpineSkinValidationSeverity.Error, config, "DefaultGenderIndex", "Default gender index is outside the gender list.");
            }

            if (config.MaxCharacterCount < 0)
                Add(issues, SpineSkinValidationSeverity.Error, config, "MaxCharacterCount", "Max character count cannot be negative.");
            if (config.AnimationDuration < 0f)
                Add(issues, SpineSkinValidationSeverity.Error, config, "AnimationDuration", "Animation duration cannot be negative.");
            if (config.ButtonAppearDelay < 0f)
                Add(issues, SpineSkinValidationSeverity.Error, config, "ButtonAppearDelay", "Button appear delay cannot be negative.");

            ValidateSlots(issues, config);
        }

        private static void ValidateSlots(List<SpineSkinValidationIssue> issues, SpineSkinConfig config)
        {
            var slots = config.SkinSlots ?? new List<SkinSlotConfig>();
            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    Add(issues, SpineSkinValidationSeverity.Error, config, $"SkinSlots[{i}]", "Skin slot config is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(slot.SlotId))
                    Add(issues, SpineSkinValidationSeverity.Error, config, $"SkinSlots[{i}].SlotId", "Skin slot must have a stable ID.");
                else if (!ids.Add(slot.SlotId.Trim()))
                    Add(issues, SpineSkinValidationSeverity.Error, config, $"SkinSlots[{i}].SlotId", $"Duplicate skin slot ID '{slot.SlotId.Trim()}'.");
                if (string.IsNullOrWhiteSpace(slot.DisplayName))
                    Add(issues, SpineSkinValidationSeverity.Error, config, $"SkinSlots[{i}].DisplayName", "Skin slot must have a display name.");
                if (slot.IsRequired && string.IsNullOrWhiteSpace(slot.DefaultSkin))
                    Add(issues, SpineSkinValidationSeverity.Error, config, $"SkinSlots[{i}].DefaultSkin", "Required skin slots must define a default skin.");
            }
        }

        private static void Add(List<SpineSkinValidationIssue> issues, SpineSkinValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new SpineSkinValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
