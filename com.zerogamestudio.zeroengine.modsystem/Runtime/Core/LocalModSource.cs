using System;
using System.IO;

namespace ZeroEngine.ModSystem
{
    public sealed class LocalModSource : IModSource
    {
        private readonly string modsRoot;

        public LocalModSource(string modsRoot, string sourceId = "local")
        {
            this.modsRoot = modsRoot;
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "local" : sourceId;
        }

        public string SourceId { get; }
        public bool IsAvailable => !string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot);

        public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
        {
            if (!IsAvailable)
            {
                onCompleted?.Invoke(ModSourceQueryResult.Failed(SourceId, $"Local mod folder does not exist: {modsRoot}"));
                return;
            }

            onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, Directory.GetDirectories(modsRoot)));
        }
    }
}
