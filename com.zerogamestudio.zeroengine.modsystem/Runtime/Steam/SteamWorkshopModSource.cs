using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem.Steam
{
    public sealed class SteamWorkshopModSource : IAsyncModSource, IExternalModChangeSignal
    {
        private readonly ISteamWorkshopClient client;
        private readonly string sourceId;
        private readonly string manifestFileName;
        private readonly TimeSpan queryTimeout;
        private readonly object snapshotLock = new();
        private string[] cachedInstalledModFolders = Array.Empty<string>();
        private bool hasCompletedInitialQuery;
        private volatile bool restartRequired;

        public SteamWorkshopModSource(
            ISteamWorkshopClient client,
            string sourceId,
            string manifestFileName,
            TimeSpan queryTimeout)
        {
            this.client = client;
            this.sourceId = string.IsNullOrWhiteSpace(sourceId) ? "steam-workshop" : sourceId;
            this.manifestFileName = string.IsNullOrWhiteSpace(manifestFileName)
                ? "mod.json"
                : manifestFileName;
            this.queryTimeout = queryTimeout;
        }

        public string SourceId => sourceId;
        public bool IsAvailable => client?.IsAvailable == true;
        public bool RestartRequired => restartRequired;

        [Obsolete("Use QueryInstalledModFoldersAsync.")]
        public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
        {
            _ = CompleteLegacyQueryAsync(onCompleted);
        }

        public async Task<ModSourceQueryResult> QueryInstalledModFoldersAsync(
            CancellationToken cancellationToken)
        {
            if (!IsAvailable)
            {
                return ModSourceQueryResult.Failed(
                    SourceId,
                    SteamWorkshopReasonCodes.SteamUnavailable);
            }

            WorkshopQueryResult query = await client.QuerySubscribedItemsAsync(
                queryTimeout,
                cancellationToken);
            if (query.Status == WorkshopOperationStatus.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            if (!query.Succeeded)
                return ModSourceQueryResult.Failed(SourceId, query.ReasonCode);

            string[] folders = query.Items
                .Where(item => item != null && item.IsInstalled && !string.IsNullOrWhiteSpace(item.InstallPath))
                .Select(item => item.InstallPath)
                .Where(folder => Directory.Exists(folder) && File.Exists(Path.Combine(folder, manifestFileName)))
                .OrderBy(folder => folder, StringComparer.Ordinal)
                .ToArray();

            lock (snapshotLock)
            {
                bool changed = !cachedInstalledModFolders.SequenceEqual(folders, StringComparer.Ordinal);
                if (hasCompletedInitialQuery && changed)
                    restartRequired = true;
                cachedInstalledModFolders = folders;
                hasCompletedInitialQuery = true;
            }

            return ModSourceQueryResult.Success(SourceId, folders);
        }

        private async Task CompleteLegacyQueryAsync(Action<ModSourceQueryResult> onCompleted)
        {
            ModSourceQueryResult result;
            try
            {
                result = await QueryInstalledModFoldersAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                result = ModSourceQueryResult.Failed(SourceId, SteamWorkshopReasonCodes.QueryCancelled);
            }
            catch
            {
                result = ModSourceQueryResult.Failed(SourceId, SteamWorkshopReasonCodes.QueryFailed);
            }

            try
            {
                onCompleted?.Invoke(result);
            }
            catch
            {
                // Legacy consumer callbacks must not escape into the source lifecycle.
            }
        }
    }
}
