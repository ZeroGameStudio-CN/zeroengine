using System;
using System.Collections.Generic;

namespace ZeroEngine.Events
{
    public readonly struct EventPublishResult
    {
        public EventPublishResult(int deliveredCount, IReadOnlyList<Exception> exceptions)
        {
            DeliveredCount = deliveredCount;
            Exceptions = exceptions ?? Array.Empty<Exception>();
        }

        public int DeliveredCount { get; }
        public IReadOnlyList<Exception> Exceptions { get; }
        public bool Success => Exceptions.Count == 0;

        public static EventPublishResult Empty()
        {
            return new EventPublishResult(0, Array.Empty<Exception>());
        }
    }
}
