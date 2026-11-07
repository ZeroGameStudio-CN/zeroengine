using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// Deduplicates pending IDs and serializes writes with reset operations. Call from one
    /// owning thread. The supplied writer must confirm persistence before it returns.
    /// </summary>
    public sealed class AchievementUploadQueue
    {
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeLane = new(1, 1);

        public int Count => _pending.Count;
        public bool IsRunning { get; private set; }
        public bool Paused { get; set; }
        public bool Add(string id) => !string.IsNullOrEmpty(id) && _pending.Add(id);
        public bool Contains(string id) => _pending.Contains(id);
        public void Clear() => _pending.Clear();

        public async Task FlushAsync(Func<IReadOnlyList<string>, CancellationToken, Task> upload,
            CancellationToken cancellationToken = default)
        {
            if (upload == null) throw new ArgumentNullException(nameof(upload));
            if (IsRunning || Paused || _pending.Count == 0) return;
            IsRunning = true;
            try
            {
                while (!Paused && _pending.Count > 0)
                {
                    await _writeLane.WaitAsync(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (Paused) return;
                        var batch = new List<string>(_pending);
                        await upload(batch, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (var id in batch) _pending.Remove(id);
                    }
                    finally { _writeLane.Release(); }
                }
            }
            finally { IsRunning = false; }
        }

        public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await _writeLane.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            }
            finally { _writeLane.Release(); }
        }
    }
}
