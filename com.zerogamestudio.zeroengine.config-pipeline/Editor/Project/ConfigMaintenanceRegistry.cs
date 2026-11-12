using System;
using System.Collections.Generic;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public interface IConfigAssetResolver
    {
        ConfigDiagnostic Validate(
            string configSetId,
            string fieldPath,
            string contentId,
            string expectedType);
    }

    public interface IConfigAssetCatalogEditor
    {
        IReadOnlyList<string> InputRelativePaths { get; }

        ConfigAssetCatalogPlan Plan(
            string projectRoot,
            string configSetId,
            IReadOnlyList<ConfigAssetBinding> bindings);
    }

    public sealed class ConfigAssetCatalogPlan
    {
        public ConfigAssetCatalogPlan(
            IReadOnlyList<ConfigArtifact> artifacts,
            IReadOnlyList<ConfigAssetBindingChange> changes,
            IReadOnlyList<ConfigDiagnostic> diagnostics)
        {
            Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlyList<ConfigArtifact> Artifacts { get; }
        public IReadOnlyList<ConfigAssetBindingChange> Changes { get; }
        public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
    }

    public enum ConfigAssetBindingChangeKind
    {
        Added,
        Removed,
        Changed
    }

    public sealed class ConfigAssetBindingChange
    {
        public ConfigAssetBindingChange(string contentId, ConfigAssetBindingChangeKind kind)
        {
            ContentId = contentId ?? throw new ArgumentNullException(nameof(contentId));
            Kind = kind;
        }

        public string ContentId { get; }
        public ConfigAssetBindingChangeKind Kind { get; }
    }

    public sealed class ConfigAssetBinding
    {
        public ConfigAssetBinding(string contentId, string assetGuid, string expectedType)
        {
            ContentId = contentId ?? throw new ArgumentNullException(nameof(contentId));
            AssetGuid = assetGuid ?? throw new ArgumentNullException(nameof(assetGuid));
            ExpectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        }

        public string ContentId { get; }
        public string AssetGuid { get; }
        public string ExpectedType { get; }
    }

    public static class ConfigMaintenanceRegistry
    {
        private static readonly Dictionary<string, List<IConfigValidator>> Validators =
            new Dictionary<string, List<IConfigValidator>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IConfigAssetResolver> AssetResolvers =
            new Dictionary<string, IConfigAssetResolver>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IConfigAssetCatalogEditor> CatalogEditors =
            new Dictionary<string, IConfigAssetCatalogEditor>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<IConfigMigration>> Migrations =
            new Dictionary<string, List<IConfigMigration>>(StringComparer.Ordinal);

        public static void RegisterValidator(string configSetId, IConfigValidator validator)
        {
            if (!Validators.TryGetValue(configSetId, out List<IConfigValidator> values))
            {
                values = new List<IConfigValidator>();
                Validators.Add(configSetId, values);
            }

            values.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
        }

        public static void RegisterAssetResolver(string configSetId, IConfigAssetResolver resolver)
        {
            AssetResolvers.Add(configSetId, resolver ?? throw new ArgumentNullException(nameof(resolver)));
        }

        public static void RegisterCatalogEditor(string configSetId, IConfigAssetCatalogEditor editor)
        {
            CatalogEditors.Add(configSetId, editor ?? throw new ArgumentNullException(nameof(editor)));
        }

        public static void RegisterMigration(string configSetId, IConfigMigration migration)
        {
            if (!Migrations.TryGetValue(configSetId, out List<IConfigMigration> values))
            {
                values = new List<IConfigMigration>();
                Migrations.Add(configSetId, values);
            }

            values.Add(migration ?? throw new ArgumentNullException(nameof(migration)));
        }

        internal static IReadOnlyList<IConfigValidator> GetValidators(string configSetId)
        {
            return Validators.TryGetValue(configSetId, out List<IConfigValidator> values)
                ? values
                : Array.Empty<IConfigValidator>();
        }

        internal static IConfigAssetResolver GetAssetResolver(string configSetId)
        {
            AssetResolvers.TryGetValue(configSetId, out IConfigAssetResolver value);
            return value;
        }

        internal static IConfigAssetCatalogEditor GetCatalogEditor(string configSetId)
        {
            CatalogEditors.TryGetValue(configSetId, out IConfigAssetCatalogEditor value);
            return value;
        }

        internal static IReadOnlyList<IConfigMigration> GetMigrations(string configSetId)
        {
            return Migrations.TryGetValue(configSetId, out List<IConfigMigration> values)
                ? values
                : Array.Empty<IConfigMigration>();
        }

        internal static void ClearConfigSetForTests(string configSetId)
        {
            Validators.Remove(configSetId);
            AssetResolvers.Remove(configSetId);
            CatalogEditors.Remove(configSetId);
            Migrations.Remove(configSetId);
        }
    }
}
