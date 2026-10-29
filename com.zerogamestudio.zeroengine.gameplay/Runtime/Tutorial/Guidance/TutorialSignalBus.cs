using System;
using System.Collections.Generic;

namespace ZeroEngine.Tutorial.Guidance
{
    /// <summary>A context-local event stream. Disposing a subscriber prevents late delivery.</summary>
    public sealed class TutorialSignalBus
    {
        private readonly List<Subscription> listeners = new List<Subscription>();

        public IDisposable Subscribe(Action<string, string> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var subscription = new Subscription(this, listener);
            listeners.Add(subscription);
            return subscription;
        }

        public void Publish(string key, string value = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A signal needs a key.", nameof(key));
            foreach (var listener in listeners.ToArray())
                if (listener.Owner == this) listener.Callback(key, value ?? string.Empty);
        }

        private sealed class Subscription : IDisposable
        {
            internal TutorialSignalBus Owner;
            internal readonly Action<string, string> Callback;
            internal Subscription(TutorialSignalBus owner, Action<string, string> callback) { Owner = owner; Callback = callback; }
            public void Dispose()
            {
                var owner = Owner;
                Owner = null;
                owner?.listeners.Remove(this);
            }
        }
    }
}
