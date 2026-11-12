namespace ZeroEngine.Events
{
    public readonly struct EventSubscriptionOptions
    {
        public EventSubscriptionOptions(object owner, int priority = 0)
        {
            Owner = owner;
            Priority = priority;
        }

        public object Owner { get; }
        public int Priority { get; }

        public static EventSubscriptionOptions ForOwner(object owner, int priority = 0)
        {
            return new EventSubscriptionOptions(owner, priority);
        }
    }
}
