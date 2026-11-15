using System;
using System.Threading;

namespace ZeroEngine.TCE
{
    public sealed class TceActionLease : IDisposable
    {
        private Action release;
        public TceActionLease(Action release) => this.release = release ?? throw new ArgumentNullException(nameof(release));
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
