using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Events.Unity
{
    public interface IEventChannelListener<in TValue>
    {
        void OnEventRaised(TValue value);
    }

    public abstract class EventChannel<TValue> : ScriptableObject
    {
        [SerializeField] private string description;
        private readonly List<IEventChannelListener<TValue>> _listeners = new();

        public string Description => description;
        public int ListenerCount
        {
            get
            {
                PruneDestroyedListeners();
                return _listeners.Count;
            }
        }

        public void Raise(TValue value)
        {
            PruneDestroyedListeners();

            var snapshot = _listeners.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var listener = snapshot[i];
                if (IsMissing(listener))
                {
                    continue;
                }

                listener.OnEventRaised(value);
            }

            PruneDestroyedListeners();
        }

        public void Register(IEventChannelListener<TValue> listener)
        {
            if (IsMissing(listener) || _listeners.Contains(listener))
            {
                return;
            }

            _listeners.Add(listener);
        }

        public void Unregister(IEventChannelListener<TValue> listener)
        {
            if (listener != null)
            {
                _listeners.Remove(listener);
            }
        }

        public void Clear()
        {
            _listeners.Clear();
        }

        public void PruneDestroyedListeners()
        {
            _listeners.RemoveAll(IsMissing);
        }

        private static bool IsMissing(IEventChannelListener<TValue> listener)
        {
            if (listener == null)
            {
                return true;
            }

            return listener is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}
