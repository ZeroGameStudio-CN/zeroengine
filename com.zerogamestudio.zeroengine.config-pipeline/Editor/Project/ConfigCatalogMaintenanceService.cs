using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigCatalogPreparedPlan
    {
        internal ConfigCatalogPreparedPlan(
            ConfigPipelinePlan plan,
            ConfigAssetCatalogPlan catalogPlan)
        {
            Plan = plan;
            CatalogPlan = catalogPlan;
        }

        public ConfigPipelinePlan Plan { get; }
        public ConfigAssetCatalogPlan CatalogPlan { get; }
    }

    public sealed class ConfigCatalogMaintenanceService
    {
        public ConfigCatalogPreparedPlan Plan(
            string projectRoot,
            string configSetId,
            string packageIdentity,
            IReadOnlyList<ConfigAssetBinding> bindings)
        {
            IConfigAssetCatalogEditor editor = ConfigMaintenanceRegistry.GetCatalogEditor(configSetId) ??
                throw new InvalidOperationException("No catalog editor is registered for '" + configSetId + "'.");
            ConfigAssetCatalogPlan catalog = editor.Plan(projectRoot, configSetId, bindings);
            if (catalog.Diagnostics.Any(value => value.Severity == ConfigDiagnosticSeverity.Error))
            {
                throw new ConfigPipelineValidationException(catalog.Diagnostics);
            }

            var artifacts = catalog.Artifacts.ToList();
            ConfigPipelineService.AddRequiredUnityMetas(
                projectRoot,
                configSetId + ":catalog",
                artifacts);
            catalog = new ConfigAssetCatalogPlan(
                artifacts,
                catalog.Changes,
                catalog.Diagnostics);
            ConfigPipelinePlan plan = new ConfigPipelinePlanBuilder().Build(
                projectRoot,
                configSetId + ":catalog",
                packageIdentity,
                editor.InputRelativePaths,
                catalog.Artifacts);
            return new ConfigCatalogPreparedPlan(plan, catalog);
        }

        public ConfigApplyResult Apply(
            string projectRoot,
            string configSetId,
            string packageIdentity,
            IReadOnlyList<ConfigAssetBinding> bindings)
        {
            ConfigCatalogPreparedPlan prepared = Plan(
                projectRoot,
                configSetId,
                packageIdentity,
                bindings);
            return new ConfigTransactionalApplier().Apply(
                projectRoot,
                prepared.Plan,
                packageIdentity,
                prepared.CatalogPlan.Artifacts,
                () => Plan(projectRoot, configSetId, packageIdentity, bindings).Plan.IsCurrent);
        }
    }
}
