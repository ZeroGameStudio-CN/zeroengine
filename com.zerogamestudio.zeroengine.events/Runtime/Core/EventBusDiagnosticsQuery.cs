namespace ZeroEngine.Events
{
    public readonly struct EventBusDiagnosticsQuery
    {
        public EventBusDiagnosticsQuery(
            int recentEventCount = 8,
            int subscriptionCount = 12,
            string filter = null,
            bool exceptionScopesOnly = false)
        {
            RecentEventCount = recentEventCount < 0 ? 0 : recentEventCount;
            SubscriptionCount = subscriptionCount < 0 ? 0 : subscriptionCount;
            Filter = filter ?? string.Empty;
            ExceptionScopesOnly = exceptionScopesOnly;
        }

        public int RecentEventCount { get; }
        public int SubscriptionCount { get; }
        public string Filter { get; }
        public bool ExceptionScopesOnly { get; }

        public bool HasFilter => !string.IsNullOrWhiteSpace(Filter);
    }
}
