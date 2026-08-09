using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    public enum ModManagementStatus
    {
        Loaded,
        Failed,
        Disabled,
        RestartRequired
    }

    public readonly struct ModManagementItem
    {
        public ModManagementItem(
            string id,
            string name,
            string author,
            string version,
            string sourceId,
            ModManagementStatus status,
            string reasonCode,
            bool isEnabled)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Author = author ?? string.Empty;
            Version = version ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            Status = status;
            ReasonCode = reasonCode ?? string.Empty;
            IsEnabled = isEnabled;
        }

        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string Version { get; }
        public string SourceId { get; }
        public ModManagementStatus Status { get; }
        public string ReasonCode { get; }
        public bool IsEnabled { get; }
    }

    public enum ModActivationChangeStatus
    {
        Changed,
        Unchanged,
        Rejected,
        PersistenceFailed
    }

    public readonly struct ModActivationChangeResult
    {
        public ModActivationChangeResult(ModActivationChangeStatus status, string reasonCode = "")
        {
            Status = status;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public ModActivationChangeStatus Status { get; }
        public string ReasonCode { get; }
    }

    public interface IModActivationStore
    {
        IReadOnlyCollection<string> DisabledModIds { get; }
        ModActivationChangeResult SetDisabled(string modId, bool disabled);
    }

    public interface IExternalModChangeSignal
    {
        bool RestartRequired { get; }
    }

    public interface IModCatalogActions
    {
        bool IsAvailable { get; }
        void OpenCatalog();
    }
}
