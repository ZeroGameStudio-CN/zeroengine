using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    /// <summary>Owns replaceable per-recipient effect contributions without knowing a project's stat/resource model.</summary>
    public sealed class TceEffectScope : IDisposable
    {
        private readonly Dictionary<TceObjectRef, IDisposable> contributions = new();
        private bool disposed;
        public void Release(TceObjectRef receiver)
        {
            if (!contributions.TryGetValue(receiver, out var contribution)) return;
            contributions.Remove(receiver);
            contribution.Dispose();
        }
        public bool Replace(TceObjectRef receiver, Func<IDisposable> apply)
        {
            if (disposed || !receiver.IsCurrent) return false;
            if (contributions.TryGetValue(receiver, out var previous))
            { contributions.Remove(receiver); previous.Dispose(); }
            var contribution = apply?.Invoke();
            if (contribution == null) return false;
            if (disposed || !receiver.IsCurrent) { contribution.Dispose(); return false; }
            contributions[receiver] = contribution;
            return true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var leases = new List<IDisposable>(contributions.Values);
            contributions.Clear();
            foreach (var lease in leases) lease.Dispose();
        }
    }
}
