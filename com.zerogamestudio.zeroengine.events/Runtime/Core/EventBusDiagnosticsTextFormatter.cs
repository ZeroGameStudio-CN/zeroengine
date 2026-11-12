using System.Text;

namespace ZeroEngine.Events
{
    public static class EventBusDiagnosticsTextFormatter
    {
        public static string Format(EventBusDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(512);
            foreach (var scope in snapshot.Scopes)
            {
                AppendScope(builder, scope);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendScope(StringBuilder builder, EventBusScopeDiagnosticsSnapshot scope)
        {
            if (!scope.IsCreated)
            {
                builder.Append(scope.Name)
                    .Append(": not-created")
                    .AppendLine();
                return;
            }

            builder.Append(scope.Name)
                .Append(": published=")
                .Append(scope.TotalPublished)
                .Append(", exceptions=")
                .Append(scope.TotalSubscriberExceptions)
                .Append(", subscriptions=")
                .Append(scope.TotalSubscriptions)
                .AppendLine();

            for (var i = 0; i < scope.Subscriptions.Count; i++)
            {
                var subscription = scope.Subscriptions[i];
                builder.Append("  sub ")
                    .Append(subscription.EventTypeName)
                    .Append(" owner=")
                    .Append(string.IsNullOrEmpty(subscription.OwnerTypeName) ? "none" : subscription.OwnerTypeName)
                    .Append(" priority=")
                    .Append(subscription.Priority)
                    .AppendLine();
            }

            for (var i = 0; i < scope.RecentEvents.Count; i++)
            {
                var recent = scope.RecentEvents[i];
                builder.Append("  event ")
                    .Append(recent.EventTypeName)
                    .Append(" source=")
                    .Append(string.IsNullOrEmpty(recent.SourceId) ? "none" : recent.SourceId)
                    .Append(" correlation=")
                    .Append(string.IsNullOrEmpty(recent.CorrelationId) ? "none" : recent.CorrelationId)
                    .Append(" sequence=")
                    .Append(recent.Sequence)
                    .AppendLine();
            }
        }
    }
}
