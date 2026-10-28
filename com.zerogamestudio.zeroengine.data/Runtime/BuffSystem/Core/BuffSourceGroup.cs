using System.Collections.Generic;

namespace ZeroEngine.BuffSystem.Runtime
{
    /// <summary>Live source lookup plus independent restored cohorts, without assuming entity identities.</summary>
    public sealed class BuffSourceGroup<TSource, THandler> where THandler : class
    {
        private readonly Dictionary<TSource, THandler> bySource = new Dictionary<TSource, THandler>();
        private readonly List<THandler> handlers = new List<THandler>();
        private readonly Stack<int> freeSlots = new Stack<int>();
        public IEnumerable<THandler> Values
        {
            get { foreach (var handler in handlers) if (handler != null) yield return handler; }
        }
        public int Count => handlers.Count - freeSlots.Count;

        public THandler this[TSource source]
        {
            get => bySource[source];
            set
            {
                if (bySource.TryGetValue(source, out var previous)) handlers[handlers.IndexOf(previous)] = value;
                else AddHandler(value);
                bySource[source] = value;
            }
        }

        public void Add(TSource source, THandler handler)
        {
            bySource.Add(source, handler);
            AddHandler(handler);
        }
        public bool TryGetValue(TSource source, out THandler handler) => bySource.TryGetValue(source, out handler);
        public bool ContainsKey(TSource source) => bySource.ContainsKey(source);
        public bool Contains(THandler handler) => handlers.Contains(handler);

        public void AddRestored(TSource source, THandler handler, bool hasSourceIdentity)
        {
            if (hasSourceIdentity && !bySource.ContainsKey(source)) bySource.Add(source, handler);
            AddHandler(handler);
        }

        private void AddHandler(THandler handler)
        {
            // Retain Dictionary's deleted-slot reuse order for existing consumers.
            if (freeSlots.Count > 0) handlers[freeSlots.Pop()] = handler;
            else handlers.Add(handler);
        }

        public bool Remove(TSource source, THandler handler)
        {
            int index = handlers.IndexOf(handler);
            if (index < 0) return false;
            handlers[index] = null;
            freeSlots.Push(index);
            if (source != null && bySource.TryGetValue(source, out var indexed) && ReferenceEquals(indexed, handler)) bySource.Remove(source);
            return true;
        }
    }
}
