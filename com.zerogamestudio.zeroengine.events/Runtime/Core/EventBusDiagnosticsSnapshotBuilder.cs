using System;
using System.Collections.Generic;

namespace ZeroEngine.Events
{
    public static class EventBusDiagnosticsSnapshotBuilder
    {
        public static EventBusDiagnosticsSnapshot Capture(
            IEnumerable<EventBusDiagnosticsScopeSource> sources,
            EventBusDiagnosticsQuery query = default)
        {
            var scopes = new List<EventBusScopeDiagnosticsSnapshot>();
            if (sources == null)
            {
                return new EventBusDiagnosticsSnapshot(scopes, query);
            }

            foreach (var source in sources)
            {
                if (query.ExceptionScopesOnly && (!source.IsCreated || source.Bus.Diagnostics.TotalSubscriberExceptions <= 0))
                {
                    continue;
                }

                scopes.Add(CaptureScope(source, query));
            }

            return new EventBusDiagnosticsSnapshot(scopes, query);
        }

        private static EventBusScopeDiagnosticsSnapshot CaptureScope(
            EventBusDiagnosticsScopeSource source,
            EventBusDiagnosticsQuery query)
        {
            if (!source.IsCreated || source.Bus == null)
            {
                return new EventBusScopeDiagnosticsSnapshot(
                    source.Name,
                    false,
                    0,
                    0,
                    0,
                    Array.Empty<EventBusSubscriptionSnapshot>(),
                    Array.Empty<EventBusRecentEventSnapshot>());
            }

            var bus = source.Bus;
            return new EventBusScopeDiagnosticsSnapshot(
                source.Name,
                true,
                bus.Diagnostics.TotalPublished,
                bus.Diagnostics.TotalSubscriberExceptions,
                bus.CountAllSubscriptions(),
                CaptureSubscriptions(bus, query),
                CaptureRecentEvents(bus.Diagnostics, query));
        }

        private static EventBusSubscriptionSnapshot[] CaptureSubscriptions(IEventBus bus, EventBusDiagnosticsQuery query)
        {
            if (query.SubscriptionCount <= 0)
            {
                return Array.Empty<EventBusSubscriptionSnapshot>();
            }

            var records = new EventSubscriptionDiagnosticRecord[query.SubscriptionCount];
            var written = bus.CopySubscriptionRecords(records);
            var snapshots = new List<EventBusSubscriptionSnapshot>(written);
            for (var i = 0; i < written; i++)
            {
                var eventTypeName = records[i].EventType?.Name ?? string.Empty;
                var ownerTypeName = records[i].OwnerTypeName ?? string.Empty;
                if (!Matches(query, eventTypeName, ownerTypeName))
                {
                    continue;
                }

                snapshots.Add(new EventBusSubscriptionSnapshot(
                    eventTypeName,
                    ownerTypeName,
                    records[i].Priority));
            }

            return snapshots.ToArray();
        }

        private static EventBusRecentEventSnapshot[] CaptureRecentEvents(
            EventBusDiagnostics diagnostics,
            EventBusDiagnosticsQuery query)
        {
            if (query.RecentEventCount <= 0)
            {
                return Array.Empty<EventBusRecentEventSnapshot>();
            }

            var records = new EventDiagnosticRecord[query.RecentEventCount];
            var written = diagnostics.CopyRecentRecords(records);
            var snapshots = new List<EventBusRecentEventSnapshot>(written);
            for (var i = 0; i < written; i++)
            {
                var meta = records[i].Meta;
                var eventTypeName = records[i].EventType?.Name ?? string.Empty;
                if (!Matches(query, eventTypeName, meta.SourceId, meta.CorrelationId, meta.CausationId))
                {
                    continue;
                }

                snapshots.Add(new EventBusRecentEventSnapshot(
                    eventTypeName,
                    meta.Scope,
                    meta.SourceId,
                    meta.CorrelationId,
                    meta.CausationId,
                    meta.UtcTicks,
                    meta.Sequence));
            }

            return snapshots.ToArray();
        }

        private static bool Matches(EventBusDiagnosticsQuery query, params string[] values)
        {
            if (!query.HasFilter)
            {
                return true;
            }

            var filter = query.Filter.Trim();
            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]) &&
                    values[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
