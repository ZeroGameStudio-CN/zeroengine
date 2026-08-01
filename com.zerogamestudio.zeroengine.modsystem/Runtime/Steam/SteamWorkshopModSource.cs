using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroEngine.ModSystem;

namespace ZeroEngine.ModSystem.Steam
{
    public sealed class SteamWorkshopModSource : IAsyncModSource
    {
        private readonly SteamWorkshopManager workshopManager;

        public SteamWorkshopModSource(SteamWorkshopManager workshopManager)
        {
            this.workshopManager = workshopManager;
        }

        public string SourceId => "steam";
        public bool IsAvailable => workshopManager != null && workshopManager.IsInitialized;

        [Obsolete("Use QueryInstalledModFoldersAsync.")]
        public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
        {
            onCompleted?.Invoke(QueryInstalledModFolders());
        }

        public Task<ModSourceQueryResult> QueryInstalledModFoldersAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(QueryInstalledModFolders());
        }

        private ModSourceQueryResult QueryInstalledModFolders()
        {
            if (!IsAvailable)
                return ModSourceQueryResult.Failed(SourceId, "Steam Workshop is not initialized.");

            workshopManager.RefreshSubscribedItems();
            var folders = workshopManager.SubscribedItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.LocalPath) && Directory.Exists(item.LocalPath))
                .Select(item => item.LocalPath)
                .ToArray();

            return ModSourceQueryResult.Success(SourceId, folders);
        }
    }
}
