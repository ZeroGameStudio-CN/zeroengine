using System;

namespace ZeroEngine.Events
{
    public readonly struct EventDiagnosticRecord
    {
        public EventDiagnosticRecord(Type eventType, EventMeta meta)
        {
            EventType = eventType;
            Meta = meta;
        }

        public Type EventType { get; }
        public EventMeta Meta { get; }
    }

    public readonly struct EventSubscriptionDiagnosticRecord
    {
        public EventSubscriptionDiagnosticRecord(Type eventType, string ownerTypeName, int priority)
        {
            EventType = eventType;
            OwnerTypeName = ownerTypeName ?? string.Empty;
            Priority = priority;
        }

        public Type EventType { get; }
        public string OwnerTypeName { get; }
        public int Priority { get; }
    }

    public sealed class EventBusDiagnostics
    {
        private readonly Type[] _eventTypes;
        private readonly EventMeta[] _metas;
        private int _nextIndex;

        public EventBusDiagnostics(int capacity = 128)
        {
            if (capacity < 1)
            {
                capacity = 1;
            }

            _eventTypes = new Type[capacity];
            _metas = new EventMeta[capacity];
        }

        public int Capacity => _eventTypes.Length;
        public long TotalPublished { get; private set; }
        public long TotalSubscriberExceptions { get; private set; }
        public Type LastEventType { get; private set; }
        public EventMeta LastMeta { get; private set; }

        public int CopyRecentRecords(EventDiagnosticRecord[] destination)
        {
            if (destination == null || destination.Length == 0)
            {
                return 0;
            }

            var capacity = _eventTypes.Length;
            var available = (int)Math.Min(TotalPublished, capacity);
            var written = 0;

            for (var i = 0; i < available && written < destination.Length; i++)
            {
                var index = (_nextIndex - available + i + capacity) % capacity;
                var eventType = _eventTypes[index];
                if (eventType == null)
                {
                    continue;
                }

                destination[written++] = new EventDiagnosticRecord(eventType, _metas[index]);
            }

            return written;
        }

        internal void Record(Type eventType, EventMeta meta)
        {
            LastEventType = eventType;
            LastMeta = meta;
            TotalPublished++;

            _eventTypes[_nextIndex] = eventType;
            _metas[_nextIndex] = meta;
            _nextIndex = (_nextIndex + 1) % _eventTypes.Length;
        }

        internal void RecordSubscriberException()
        {
            TotalSubscriberExceptions++;
        }
    }
}
