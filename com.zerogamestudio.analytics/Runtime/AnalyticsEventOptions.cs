using System;

namespace ZGS.Analytics
{
    /// <summary>
    /// Controls the identity, logical occurrence time, and local durability of an event envelope.
    /// </summary>
    public readonly struct AnalyticsEventOptions
    {
        public string EventId { get; }
        public long OccurredAtUnixMs { get; }
        public bool Durable { get; }

        internal long SessionEventSequence { get; }

        public AnalyticsEventOptions(
            string eventId = null,
            long occurredAtUnixMs = 0,
            bool durable = false)
            : this(eventId, occurredAtUnixMs, durable, 0)
        {
        }

        private AnalyticsEventOptions(
            string eventId,
            long occurredAtUnixMs,
            bool durable,
            long sessionEventSequence)
        {
            EventId = eventId;
            OccurredAtUnixMs = occurredAtUnixMs;
            Durable = durable;
            SessionEventSequence = sessionEventSequence;
        }

        internal bool TryFreezeEnvelope(out AnalyticsEventOptions frozen)
        {
            string eventId = EventId;
            if (string.IsNullOrEmpty(eventId))
                eventId = "zgs." + Guid.NewGuid().ToString("N");
            else if (!IsValidEventId(eventId))
            {
                frozen = default;
                return false;
            }

            long occurredAtUnixMs = OccurredAtUnixMs > 0
                ? OccurredAtUnixMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long sequence = SessionEventSequence > 0
                ? SessionEventSequence
                : SessionInfo.NextEventSequence();

            frozen = new AnalyticsEventOptions(eventId, occurredAtUnixMs, Durable, sequence);
            return true;
        }

        internal static bool IsValidEventId(string eventId)
        {
            if (string.IsNullOrEmpty(eventId) || eventId.Length > 128)
                return false;

            if (!IsAlphaNumeric(eventId[0]))
                return false;

            for (int i = 1; i < eventId.Length; i++)
            {
                char c = eventId[i];
                if (!IsAlphaNumeric(c) && c != '.' && c != '_' && c != ':' && c != '-')
                    return false;
            }

            return true;
        }

        private static bool IsAlphaNumeric(char c)
        {
            return c >= 'A' && c <= 'Z'
                || c >= 'a' && c <= 'z'
                || c >= '0' && c <= '9';
        }
    }
}
