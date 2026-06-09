using System;

namespace ZeroEngine.TCE
{
    public interface ITceActor
    {
        bool IsAlive { get; }
        float DomainTime { get; }
        object NativeObject { get; }
    }

    public interface ITceClock
    {
        float Now { get; }
    }

    public sealed class TceActorClock : ITceClock
    {
        private readonly ITceActor actor;

        public TceActorClock(ITceActor actor)
        {
            this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        }

        public float Now => actor.DomainTime;
    }
}
