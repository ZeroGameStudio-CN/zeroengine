using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ZeroEngine.GameFeel
{
    internal static class FeedbackScopeIdentity
    {
        private static long next;
        internal static ulong Next()
        {
            long value = Interlocked.Increment(ref next);
            if (value <= 0) throw new InvalidOperationException("Feedback scope identity space exhausted.");
            return (ulong)value;
        }
    }

    /// <summary>
    /// Optional object-to-contact adapter. Release on pool disable, Reset on world/session exit.
    /// IDs are never recycled, so old attack contacts cannot suppress a new pooled lifetime.
    /// Only first use of a target lifetime allocates; repeated contact lookup reuses that entry.
    /// </summary>
    public sealed class FeedbackScopeIds<TTarget> where TTarget : class
    {
        private sealed class Entry
        {
            internal readonly ulong Id;
            internal Entry()
            {
                Id = FeedbackScopeIdentity.Next();
            }
        }
        private readonly ConditionalWeakTable<TTarget, Entry> ids = new ConditionalWeakTable<TTarget, Entry>();
        public ulong GetId(TTarget target)
        {
            if (target == null) return 0;
            return ids.GetValue(target, static _ => new Entry()).Id;
        }
        public void Release(TTarget target) { if (target != null) ids.Remove(target); }
        public void Reset() => ids.Clear();
    }
}
