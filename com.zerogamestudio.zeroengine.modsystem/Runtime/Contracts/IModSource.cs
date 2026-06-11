using System;

namespace ZeroEngine.ModSystem
{
    public interface IModSource
    {
        string SourceId { get; }
        bool IsAvailable { get; }
        void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted);
    }
}
