using System;
using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    public sealed class ModManagementService
    {
        private readonly ModLoadReport report;
        private readonly IModActivationStore activationStore;
        private readonly IExternalModChangeSignal externalChangeSignal;

        public ModManagementService(
            ModLoadReport report,
            IModActivationStore activationStore,
            IExternalModChangeSignal externalChangeSignal = null)
        {
            this.report = report ?? new ModLoadReport(
                Array.Empty<ModManifest>(),
                Array.Empty<ModLoadIssue>());
            this.activationStore = activationStore;
            this.externalChangeSignal = externalChangeSignal;
        }

        public bool ExternalRestartRequired => externalChangeSignal?.RestartRequired == true;

        public IReadOnlyList<ModManagementItem> BuildSnapshot()
        {
            return ModManagementProjection.Build(report, activationStore?.DisabledModIds);
        }

        public ModActivationChangeResult SetDisabled(string modId, bool disabled)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return new ModActivationChangeResult(
                    ModActivationChangeStatus.Rejected,
                    "mod_id_invalid");
            }

            if (activationStore == null)
            {
                return new ModActivationChangeResult(
                    ModActivationChangeStatus.Rejected,
                    "activation_store_unavailable");
            }

            return activationStore.SetDisabled(modId, disabled);
        }
    }
}
