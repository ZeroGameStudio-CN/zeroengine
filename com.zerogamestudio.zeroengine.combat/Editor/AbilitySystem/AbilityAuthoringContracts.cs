using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.AbilitySystem.Editor
{
    public sealed class AbilityAuthoringProfile
    {
        public AbilityAuthoringProfile(
            string profileId,
            string title,
            IAbilityAuthoringAssetAdapter adapter,
            string menuPath = "ZGS/Ability/Ability Workbench",
            string description = null)
        {
            ProfileId = RequireText(profileId, nameof(profileId));
            Title = RequireText(title, nameof(title));
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            MenuPath = string.IsNullOrWhiteSpace(menuPath) ? "ZGS/Ability/Ability Workbench" : menuPath;
            Description = string.IsNullOrWhiteSpace(description) ? Title : description;
        }

        public string ProfileId { get; }

        public string Title { get; }

        public IAbilityAuthoringAssetAdapter Adapter { get; }

        public string MenuPath { get; }

        public string Description { get; }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value;
        }
    }

    public interface IAbilityAuthoringAssetAdapter
    {
        string DefaultAssetFolder { get; }

        string GetCreateAssetPath();

        IReadOnlyList<AbilityAuthoringAssetRecord> GetAssets();

        bool MatchesSearch(AbilityAuthoringAssetRecord record, string search);

        Object CreateAsset();

        Object DuplicateAsset(Object source);

        void PrepareAsset(Object asset);

        SerializedProperty FindAbilityProperty(SerializedObject serializedObject);

        void DrawProjectSections(SerializedObject serializedObject, Object asset);

        AbilityAuthoringValidationResult ValidateAsset(Object asset);
    }

    public sealed class AbilityAuthoringAssetRecord
    {
        public AbilityAuthoringAssetRecord(
            Object asset,
            string assetPath,
            string id,
            string displayName,
            string subtitle,
            Texture icon,
            string searchText = null)
        {
            Asset = asset;
            AssetPath = assetPath ?? string.Empty;
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Subtitle = subtitle ?? string.Empty;
            Icon = icon;
            SearchText = string.IsNullOrWhiteSpace(searchText)
                ? $"{Id} {DisplayName} {Subtitle} {AssetPath}"
                : searchText;
        }

        public Object Asset { get; }

        public string AssetPath { get; }

        public string Id { get; }

        public string DisplayName { get; }

        public string Subtitle { get; }

        public Texture Icon { get; }

        public string SearchText { get; }

        public bool MatchesSearch(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public enum AbilityAuthoringValidationStatus
    {
        Success,
        Warning,
        Error
    }

    public sealed class AbilityAuthoringValidationResult
    {
        private AbilityAuthoringValidationResult(
            AbilityAuthoringValidationStatus status,
            string message,
            IReadOnlyList<string> details)
        {
            Status = status;
            Message = string.IsNullOrWhiteSpace(message) ? status.ToString() : message;
            Details = details ?? Array.Empty<string>();
        }

        public AbilityAuthoringValidationStatus Status { get; }

        public string Message { get; }

        public IReadOnlyList<string> Details { get; }

        public bool Succeeded => Status != AbilityAuthoringValidationStatus.Error;

        public static AbilityAuthoringValidationResult Success(string message = "校验通过。", IReadOnlyList<string> details = null)
        {
            return new AbilityAuthoringValidationResult(AbilityAuthoringValidationStatus.Success, message, details);
        }

        public static AbilityAuthoringValidationResult Warning(string message, IReadOnlyList<string> details = null)
        {
            return new AbilityAuthoringValidationResult(AbilityAuthoringValidationStatus.Warning, message, details);
        }

        public static AbilityAuthoringValidationResult Error(string message, IReadOnlyList<string> details = null)
        {
            return new AbilityAuthoringValidationResult(AbilityAuthoringValidationStatus.Error, message, details);
        }
    }
}
