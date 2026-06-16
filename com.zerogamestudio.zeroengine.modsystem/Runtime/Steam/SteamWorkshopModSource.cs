using System;
using System.IO;
using System.Linq;
using ZeroEngine.ModSystem;

namespace ZeroEngine.ModSystem.Steam
{
    public sealed class SteamWorkshopModSource : IModSource
    {
        private readonly SteamWorkshopManager workshopManager;

        public SteamWorkshopModSource(SteamWorkshopManager workshopManager)
        {
            this.workshopManager = workshopManager;
        }

        public string SourceId => "steam";
        public bool IsAvailable => workshopManager != null && workshopManager.IsInitialized;

        public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
        {
            if (!IsAvailable)
            {
                onCompleted?.Invoke(ModSourceQueryResult.Failed(SourceId, "Steam Workshop is not initialized."));
                return;
            }

            workshopManager.RefreshSubscribedItems();
            var folders = workshopManager.SubscribedItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.LocalPath) && Directory.Exists(item.LocalPath))
                .Select(item => item.LocalPath)
                .ToArray();

            onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, folders));
        }
    }
}
