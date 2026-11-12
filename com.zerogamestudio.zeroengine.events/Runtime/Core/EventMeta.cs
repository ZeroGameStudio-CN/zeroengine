using System;

namespace ZeroEngine.Events
{
    public readonly struct EventMeta
    {
        public EventMeta(
            EventScope scope,
            string sourceId,
            string correlationId,
            string causationId,
            long utcTicks,
            long sequence)
        {
            Scope = scope;
            SourceId = sourceId ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            CausationId = causationId ?? string.Empty;
            UtcTicks = utcTicks <= 0 ? DateTime.UtcNow.Ticks : utcTicks;
            Sequence = sequence;
        }

        public EventScope Scope { get; }
        public string SourceId { get; }
        public string CorrelationId { get; }
        public string CausationId { get; }
        public long UtcTicks { get; }
        public long Sequence { get; }

        public static EventMeta Create(
            EventScope scope = EventScope.Session,
            string sourceId = null,
            string correlationId = null,
            string causationId = null)
        {
            return new EventMeta(scope, sourceId, correlationId, causationId, DateTime.UtcNow.Ticks, 0);
        }

        public EventMeta WithSequence(long sequence)
        {
            return new EventMeta(Scope, SourceId, CorrelationId, CausationId, UtcTicks, sequence);
        }
    }
}
