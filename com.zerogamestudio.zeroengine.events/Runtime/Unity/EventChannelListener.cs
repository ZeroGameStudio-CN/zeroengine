using UnityEngine;
using UnityEngine.Events;

namespace ZeroEngine.Events.Unity
{
    public abstract class EventChannelListener<TChannel, TValue> : MonoBehaviour, IEventChannelListener<TValue>
        where TChannel : EventChannel<TValue>
    {
        [SerializeField] private TChannel channel;
        [SerializeField] private UnityEvent<TValue> response;

        protected virtual void OnEnable()
        {
            channel?.Register(this);
        }

        protected virtual void OnDisable()
        {
            channel?.Unregister(this);
        }

        protected virtual void OnDestroy()
        {
            channel?.Unregister(this);
        }

        public virtual void OnEventRaised(TValue value)
        {
            response?.Invoke(value);
        }
    }
}
