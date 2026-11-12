using System;
using System.Collections.Generic;

namespace ZeroEngine.Events
{
    public sealed class EventBusQueue
    {
        private interface IQueuedEvent
        {
            EventPublishResult Publish(IEventBus bus);
        }

        private readonly object _gate = new();
        private readonly Queue<IQueuedEvent> _queue = new();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        public void Enqueue<TEvent>(TEvent payload, EventMeta meta = default)
            where TEvent : IGameEvent
        {
            lock (_gate)
            {
                _queue.Enqueue(new QueuedEvent<TEvent>(payload, meta));
            }
        }

        public int Flush(IEventBus bus, int maxEvents = int.MaxValue)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            var flushed = 0;
            while (flushed < maxEvents)
            {
                IQueuedEvent queued;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        break;
                    }

                    queued = _queue.Dequeue();
                }

                queued.Publish(bus);
                flushed++;
            }

            return flushed;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _queue.Clear();
            }
        }

        private readonly struct QueuedEvent<TEvent> : IQueuedEvent
            where TEvent : IGameEvent
        {
            private readonly TEvent _payload;
            private readonly EventMeta _meta;

            public QueuedEvent(TEvent payload, EventMeta meta)
            {
                _payload = payload;
                _meta = meta;
            }

            public EventPublishResult Publish(IEventBus bus)
            {
                return bus.Publish(_payload, _meta);
            }
        }
    }
}
