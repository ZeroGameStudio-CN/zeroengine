using System.Threading;

namespace ZeroEngine.Events
{
    public readonly struct EventBusOptions
    {
        public EventBusOptions(bool enforcePublisherThread, int publisherThreadId)
        {
            EnforcePublisherThread = enforcePublisherThread;
            PublisherThreadId = publisherThreadId;
        }

        public bool EnforcePublisherThread { get; }
        public int PublisherThreadId { get; }

        public static EventBusOptions CaptureCurrentThread(bool enforcePublisherThread = true)
        {
            return new EventBusOptions(enforcePublisherThread, Thread.CurrentThread.ManagedThreadId);
        }

        public static EventBusOptions Unchecked()
        {
            return new EventBusOptions(false, 0);
        }
    }
}
