using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Events
{
    public readonly struct EventBusDiagnosticsScopeSource
    {
        private EventBusDiagnosticsScopeSource(string name, IEventBus bus, bool isCreated)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
            Bus = bus;
            IsCreated = isCreated && bus != null;
        }

        public string Name { get; }
        public IEventBus Bus { get; }
        public bool IsCreated { get; }

        public static EventBusDiagnosticsScopeSource Created(string name, IEventBus bus)
        {
            if (bus == null)
            {
                return Missing(name);
            }

            return new EventBusDiagnosticsScopeSource(name, bus, true);
        }

        public static EventBusDiagnosticsScopeSource Missing(string name)
        {
            return new EventBusDiagnosticsScopeSource(name, null, false);
        }
    }

    public sealed class EventBusDiagnosticsSnapshot
    {
        public EventBusDiagnosticsSnapshot(
            IEnumerable<EventBusScopeDiagnosticsSnapshot> scopes,
            EventBusDiagnosticsQuery query)
        {
            Scopes = (scopes ?? Array.Empty<EventBusScopeDiagnosticsSnapshot>())
                .Where(scope => scope != null)
                .ToArray();
            Query = query;
        }

        public IReadOnlyList<EventBusScopeDiagnosticsSnapshot> Scopes { get; }
        public EventBusDiagnosticsQuery Query { get; }
        public bool HasSubscriberExceptions => Scopes.Any(scope => scope.TotalSubscriberExceptions > 0);
    }

    public sealed class EventBusScopeDiagnosticsSnapshot
    {
        public EventBusScopeDiagnosticsSnapshot(
            string name,
            bool isCreated,
            long totalPublished,
            long totalSubscriberExceptions,
            int totalSubscriptions,
            IEnumerable<EventBusSubscriptionSnapshot> subscriptions,
            IEnumerable<EventBusRecentEventSnapshot> recentEvents)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
            IsCreated = isCreated;
            TotalPublished = totalPublished;
            TotalSubscriberExceptions = totalSubscriberExceptions;
            TotalSubscriptions = totalSubscriptions;
            Subscriptions = (subscriptions ?? Array.Empty<EventBusSubscriptionSnapshot>()).ToArray();
            RecentEvents = (recentEvents ?? Array.Empty<EventBusRecentEventSnapshot>()).ToArray();
        }

        public string Name { get; }
        public bool IsCreated { get; }
        public long TotalPublished { get; }
        public long TotalSubscriberExceptions { get; }
        public int TotalSubscriptions { get; }
        public IReadOnlyList<EventBusSubscriptionSnapshot> Subscriptions { get; }
        public IReadOnlyList<EventBusRecentEventSnapshot> RecentEvents { get; }
    }

    public readonly struct EventBusSubscriptionSnapshot
    {
        public EventBusSubscriptionSnapshot(string eventTypeName, string ownerTypeName, int priority)
        {
            EventTypeName = eventTypeName ?? string.Empty;
            OwnerTypeName = ownerTypeName ?? string.Empty;
            Priority = priority;
        }

        public string EventTypeName { get; }
        public string OwnerTypeName { get; }
        public int Priority { get; }
    }

    public readonly struct EventBusRecentEventSnapshot
    {
        public EventBusRecentEventSnapshot(
            string eventTypeName,
            EventScope scope,
            string sourceId,
            string correlationId,
            string causationId,
            long utcTicks,
            long sequence)
        {
            EventTypeName = eventTypeName ?? string.Empty;
            Scope = scope;
            SourceId = sourceId ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            CausationId = causationId ?? string.Empty;
            UtcTicks = utcTicks;
            Sequence = sequence;
        }

        public string EventTypeName { get; }
        public EventScope Scope { get; }
        public string SourceId { get; }
        public string CorrelationId { get; }
        public string CausationId { get; }
        public long UtcTicks { get; }
        public long Sequence { get; }
    }
}
