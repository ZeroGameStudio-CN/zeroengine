using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public enum FormulaEditorParameterKind
    {
        String,
        Int,
        Float,
        Bool,
        Object,
        Enum,
    }

    public enum FormulaPreviewInputKind
    {
        Int,
        Float,
        Bool,
    }

    public sealed class FormulaParameterDescriptor
    {
        public FormulaParameterDescriptor(
            string key,
            string displayName,
            FormulaEditorParameterKind kind,
            bool required,
            string description,
            Type enumType = null,
            int defaultIntValue = 0,
            float defaultFloatValue = 0f,
            string defaultStringValue = null)
        {
            Key = key ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Kind = kind;
            Required = required;
            Description = description ?? string.Empty;
            EnumType = enumType;
            DefaultIntValue = defaultIntValue;
            DefaultFloatValue = defaultFloatValue;
            DefaultStringValue = defaultStringValue ?? string.Empty;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public FormulaEditorParameterKind Kind { get; }
        public bool Required { get; }
        public string Description { get; }
        public Type EnumType { get; }
        public int DefaultIntValue { get; }
        public float DefaultFloatValue { get; }
        public string DefaultStringValue { get; }
    }

    public sealed class FormulaProviderDescriptor
    {
        public FormulaProviderDescriptor(
            string id,
            string displayName,
            string category,
            string description,
            float previewValue,
            IReadOnlyList<FormulaParameterDescriptor> parameters,
            string previewInputKey = null,
            FormulaPreviewInputKind previewInputKind = FormulaPreviewInputKind.Float)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Category = category ?? string.Empty;
            Description = description ?? string.Empty;
            PreviewValue = previewValue;
            PreviewInputKey = previewInputKey ?? string.Empty;
            PreviewInputKind = previewInputKind;
            Parameters = parameters == null
                ? Array.Empty<FormulaParameterDescriptor>()
                : new List<FormulaParameterDescriptor>(parameters).AsReadOnly();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public string Description { get; }
        public float PreviewValue { get; }
        public string PreviewInputKey { get; }
        public FormulaPreviewInputKind PreviewInputKind { get; }
        public IReadOnlyList<FormulaParameterDescriptor> Parameters { get; }
    }

    public sealed class FormulaPreviewInputDescriptor
    {
        public FormulaPreviewInputDescriptor(
            string key,
            string displayName,
            FormulaPreviewInputKind kind,
            float defaultValue,
            string description)
        {
            Key = key ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Kind = kind;
            DefaultValue = defaultValue;
            Description = description ?? string.Empty;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public FormulaPreviewInputKind Kind { get; }
        public float DefaultValue { get; }
        public string Description { get; }
    }

    public sealed class FormulaEditorProfile
    {
        public FormulaEditorProfile(
            string profileId,
            string displayName,
            string defaultSearchRoot,
            string workbenchMenuPath,
            string workbenchTitle,
            IReadOnlyList<FormulaProviderDescriptor> providers,
            IReadOnlyList<FormulaPreviewInputDescriptor> previewInputs,
            FormulaAssetQualityRules qualityRules = null,
            string catalogAssetPath = null,
            IReadOnlyList<string> referenceRoots = null,
            IReadOnlyList<string> excludedReferenceRoots = null,
            IReadOnlyList<FormulaPreviewCase> defaultPreviewCases = null)
        {
            ProfileId = profileId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            DefaultSearchRoot = defaultSearchRoot ?? string.Empty;
            WorkbenchMenuPath = workbenchMenuPath ?? string.Empty;
            WorkbenchTitle = workbenchTitle ?? string.Empty;
            Providers = providers == null
                ? Array.Empty<FormulaProviderDescriptor>()
                : new List<FormulaProviderDescriptor>(providers).AsReadOnly();
            PreviewInputs = previewInputs == null
                ? Array.Empty<FormulaPreviewInputDescriptor>()
                : new List<FormulaPreviewInputDescriptor>(previewInputs).AsReadOnly();
            QualityRules = qualityRules ?? FormulaAssetQualityRules.None;
            CatalogAssetPath = catalogAssetPath ?? string.Empty;
            ReferenceRoots = referenceRoots == null
                ? Array.Empty<string>()
                : new List<string>(referenceRoots).AsReadOnly();
            ExcludedReferenceRoots = excludedReferenceRoots == null
                ? Array.Empty<string>()
                : new List<string>(excludedReferenceRoots).AsReadOnly();
            DefaultPreviewCases = defaultPreviewCases == null
                ? Array.Empty<FormulaPreviewCase>()
                : new List<FormulaPreviewCase>(defaultPreviewCases).AsReadOnly();
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public string DefaultSearchRoot { get; }
        public string WorkbenchMenuPath { get; }
        public string WorkbenchTitle { get; }
        public IReadOnlyList<FormulaProviderDescriptor> Providers { get; }
        public IReadOnlyList<FormulaPreviewInputDescriptor> PreviewInputs { get; }
        public FormulaAssetQualityRules QualityRules { get; }
        public string CatalogAssetPath { get; }
        public IReadOnlyList<string> ReferenceRoots { get; }
        public IReadOnlyList<string> ExcludedReferenceRoots { get; }
        public IReadOnlyList<FormulaPreviewCase> DefaultPreviewCases { get; }

        public static FormulaEditorProfile CreateEmpty(string profileId, string displayName)
        {
            return new FormulaEditorProfile(
                profileId,
                displayName,
                string.Empty,
                string.Empty,
                displayName,
                Array.Empty<FormulaProviderDescriptor>(),
                Array.Empty<FormulaPreviewInputDescriptor>());
        }

        public bool TryGetProvider(string providerId, out FormulaProviderDescriptor descriptor)
        {
            foreach (var provider in Providers)
            {
                if (provider.Id == providerId)
                {
                    descriptor = provider;
                    return true;
                }
            }

            descriptor = null;
            return false;
        }
    }

    public sealed class FormulaAssetQualityRules
    {
        public FormulaAssetQualityRules(
            bool warnOnEmptySteps,
            IReadOnlyList<string> temporaryNamePatterns,
            bool warnOnMissingCatalogEntry = false,
            bool warnOnUnreferencedFormula = false)
        {
            WarnOnEmptySteps = warnOnEmptySteps;
            TemporaryNamePatterns = temporaryNamePatterns == null
                ? Array.Empty<string>()
                : new List<string>(temporaryNamePatterns).AsReadOnly();
            WarnOnMissingCatalogEntry = warnOnMissingCatalogEntry;
            WarnOnUnreferencedFormula = warnOnUnreferencedFormula;
        }

        public static FormulaAssetQualityRules None { get; } =
            new FormulaAssetQualityRules(false, Array.Empty<string>());

        public bool WarnOnEmptySteps { get; }
        public IReadOnlyList<string> TemporaryNamePatterns { get; }
        public bool WarnOnMissingCatalogEntry { get; }
        public bool WarnOnUnreferencedFormula { get; }
    }
}
