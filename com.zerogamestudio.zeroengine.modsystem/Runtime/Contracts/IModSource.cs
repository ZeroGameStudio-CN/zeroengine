using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem
{
    public interface IModSource
    {
        string SourceId { get; }
        bool IsAvailable { get; }

        [Obsolete("Use IAsyncModSource.QueryInstalledModFoldersAsync for awaitable discovery.")]
        void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted);
    }

    /// <summary>
    /// Awaitable source contract used by the production orchestrator. The legacy callback
    /// contract remains on <see cref="IModSource"/> for one compatibility cycle.
    /// </summary>
    public interface IAsyncModSource : IModSource
    {
        Task<ModSourceQueryResult> QueryInstalledModFoldersAsync(CancellationToken cancellationToken);
    }
}
