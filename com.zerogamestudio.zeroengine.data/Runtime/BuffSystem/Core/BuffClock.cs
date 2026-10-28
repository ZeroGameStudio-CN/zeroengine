namespace ZeroEngine.BuffSystem.Runtime
{
    /// <summary>Clock arithmetic only; hosts choose their time source and expiry/tick ordering.</summary>
    public static class BuffClock
    {
        public static float SelectDurationDelta(bool globalGameplay, float ownerDelta, float globalDelta) =>
            globalGameplay ? globalDelta : ownerDelta;

        public static float AdvanceDuration(float remaining, float delta) => remaining - delta;

        // Absolute anchors preserve a host's first-tick and pause semantics.
        public static bool TryTickAt(float now, ref float lastTick, float interval)
        {
            if (interval <= 0 || !(now - lastTick >= interval)) return false;
            lastTick = now;
            return true;
        }

        // Legacy aggregate Buffs accumulate deltas, emitting at most one tick and dropping overshoot.
        public static bool TryAdvanceTick(float delta, ref float accumulated, float interval)
        {
            if (!(interval > 0)) return false;
            accumulated += delta;
            if (!(accumulated >= interval)) return false;
            accumulated = 0;
            return true;
        }
    }

    /// <summary>A generation is advanced by the host before resetting or reusing its activation.</summary>
    public sealed class BuffLifecycle
    {
        public int Version { get; private set; }
        public bool IsReleased { get; private set; }
        public void AdvanceGeneration() { unchecked { Version++; } }
        public void Activate() => IsReleased = false;
        public void Release() => IsReleased = true;
        public bool IsCurrent(int version) => Version == version && !IsReleased;
    }
}
