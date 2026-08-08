using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem
{
    public sealed class LocalModSource : IAsyncModSource
    {
        private readonly string modsRoot;

        public LocalModSource(string modsRoot, string sourceId = "local")
        {
            this.modsRoot = modsRoot;
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "local" : sourceId;
        }

        public string SourceId { get; }
        public bool IsAvailable => !string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot);

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
            return !IsAvailable
                ? ModSourceQueryResult.Failed(SourceId, $"Local mod folder does not exist: {modsRoot}")
                : ModSourceQueryResult.Success(SourceId, Directory.GetDirectories(modsRoot));
        }
    }
}
