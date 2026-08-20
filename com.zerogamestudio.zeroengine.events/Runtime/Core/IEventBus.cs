using System;

namespace ZeroEngine.Events
{
    public interface IEventBus
    {
        EventBusDiagnostics Diagnostics { get; }

        EventSubscriptionToken Subscribe<TEvent>(
            Action<EventEnvelope<TEvent>> handler,
            EventSubscriptionOptions options = default)
            where TEvent : IGameEvent;

        bool Unsubscribe(EventSubscriptionToken token);
        int UnsubscribeOwner(object owner);
        int CountSubscriptions<TEvent>() where TEvent : IGameEvent;
        int CountSubscriptionsForOwner(object owner);
        int CountAllSubscriptions();
        int CopySubscriptionRecords(EventSubscriptionDiagnosticRecord[] destination);

        EventPublishResult Publish<TEvent>(TEvent payload, EventMeta meta = default)
            where TEvent : IGameEvent;
    }
}
