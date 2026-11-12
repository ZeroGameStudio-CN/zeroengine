using System.Text;

namespace ZeroEngine.Events
{
    public static class EventBusDiagnosticsJsonExporter
    {
        public static string ToJson(EventBusDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "{}";
            }

            var builder = new StringBuilder(1024);
            builder.Append("{\"query\":{");
            AppendProperty(builder, "filter", snapshot.Query.Filter);
            builder.Append(",\"recentEventCount\":").Append(snapshot.Query.RecentEventCount);
            builder.Append(",\"subscriptionCount\":").Append(snapshot.Query.SubscriptionCount);
            builder.Append(",\"exceptionScopesOnly\":").Append(snapshot.Query.ExceptionScopesOnly ? "true" : "false");
            builder.Append("},\"scopes\":[");

            for (var i = 0; i < snapshot.Scopes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendScope(builder, snapshot.Scopes[i]);
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendScope(StringBuilder builder, EventBusScopeDiagnosticsSnapshot scope)
        {
            builder.Append('{');
            AppendProperty(builder, "name", scope.Name);
            builder.Append(",\"isCreated\":").Append(scope.IsCreated ? "true" : "false");
            builder.Append(",\"totalPublished\":").Append(scope.TotalPublished);
            builder.Append(",\"totalSubscriberExceptions\":").Append(scope.TotalSubscriberExceptions);
            builder.Append(",\"totalSubscriptions\":").Append(scope.TotalSubscriptions);
            builder.Append(",\"subscriptions\":[");
            for (var i = 0; i < scope.Subscriptions.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendSubscription(builder, scope.Subscriptions[i]);
            }

            builder.Append("],\"recentEvents\":[");
            for (var i = 0; i < scope.RecentEvents.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendRecentEvent(builder, scope.RecentEvents[i]);
            }

            builder.Append("]}");
        }

        private static void AppendSubscription(StringBuilder builder, EventBusSubscriptionSnapshot subscription)
        {
            builder.Append('{');
            AppendProperty(builder, "eventTypeName", subscription.EventTypeName);
            builder.Append(',');
            AppendProperty(builder, "ownerTypeName", subscription.OwnerTypeName);
            builder.Append(",\"priority\":").Append(subscription.Priority);
            builder.Append('}');
        }

        private static void AppendRecentEvent(StringBuilder builder, EventBusRecentEventSnapshot recent)
        {
            builder.Append('{');
            AppendProperty(builder, "eventTypeName", recent.EventTypeName);
            builder.Append(',');
            AppendProperty(builder, "scope", recent.Scope.ToString());
            builder.Append(',');
            AppendProperty(builder, "sourceId", recent.SourceId);
            builder.Append(',');
            AppendProperty(builder, "correlationId", recent.CorrelationId);
            builder.Append(',');
            AppendProperty(builder, "causationId", recent.CausationId);
            builder.Append(",\"utcTicks\":").Append(recent.UtcTicks);
            builder.Append(",\"sequence\":").Append(recent.Sequence);
            builder.Append('}');
        }

        private static void AppendProperty(StringBuilder builder, string name, string value)
        {
            builder.Append('"')
                .Append(Escape(name))
                .Append("\":\"")
                .Append(Escape(value))
                .Append('"');
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(value[i]);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
