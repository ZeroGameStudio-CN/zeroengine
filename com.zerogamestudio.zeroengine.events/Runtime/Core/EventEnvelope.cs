namespace ZeroEngine.Events
{
    public readonly struct EventEnvelope<TEvent> where TEvent : IGameEvent
    {
        public EventEnvelope(TEvent payload, EventMeta meta)
        {
            Payload = payload;
            Meta = meta;
        }

        public TEvent Payload { get; }
        public EventMeta Meta { get; }
    }
}
