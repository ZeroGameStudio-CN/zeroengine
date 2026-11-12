using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console.Unity
{
    public sealed class UnityConsoleDispatcher : IConsoleDispatcher, IDisposable
    {
        private readonly SynchronizationContext context;
        private readonly int mainThread;
        private int disposed;

        public UnityConsoleDispatcher()
        {
            context = SynchronizationContext.Current;
            mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<ConsoleResult> DispatchAsync(Func<Task<ConsoleResult>> work, CancellationToken cancellation)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (Volatile.Read(ref disposed) != 0) return Task.FromResult(ConsoleResult.Failure("host-disabled", "Unity Console host is disabled."));
            if (Thread.CurrentThread.ManagedThreadId == mainThread)
            {
                cancellation.ThrowIfCancellationRequested();
                return work();
            }
            if (context == null) return Task.FromResult(ConsoleResult.Failure("main-thread-unavailable", "No Unity main-thread context was captured."));
            var result = new TaskCompletionSource<ConsoleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(async _ =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref disposed) != 0) { result.TrySetResult(ConsoleResult.Failure("host-disabled", "Unity Console host is disabled.")); return; }
                    result.TrySetResult(await work());
                }
                catch (OperationCanceledException) { result.TrySetCanceled(); }
                catch (Exception exception) { result.TrySetException(exception); }
            }, null);
            return result.Task;
        }

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    }
}
