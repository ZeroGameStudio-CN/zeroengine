using System;
using System.Threading;

namespace ZeroEngine.GameFeel
{
    public enum VisualDetailLevel { Full, Simplified, Minimal }

    /// <summary>Appearance policy only. The consumer decides which effects are eligible.</summary>
    public readonly struct VisualEffectSettings
    {
        public readonly int FrameLimit, WindowLimit, ActiveLimit;
        public readonly double WindowSeconds, MergeSeconds;
        public readonly bool Unlimited;

        public VisualEffectSettings(int frameLimit, int windowLimit, int activeLimit,
            double windowSeconds, double mergeSeconds, bool unlimited = false)
        {
            if (frameLimit < 1 || windowLimit < 1 || activeLimit < 1 ||
                !Finite(windowSeconds) || windowSeconds <= 0 || !Finite(mergeSeconds) || mergeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameLimit));
            FrameLimit = frameLimit; WindowLimit = windowLimit; ActiveLimit = activeLimit;
            WindowSeconds = windowSeconds; MergeSeconds = mergeSeconds; Unlimited = unlimited;
        }

        public static VisualEffectSettings For(VisualDetailLevel level) => level switch
        {
            VisualDetailLevel.Full => new VisualEffectSettings(8, 16, 64, .1, .08, true),
            VisualDetailLevel.Minimal => new VisualEffectSettings(3, 5, 24, .1, .16),
            _ => new VisualEffectSettings(8, 16, 64, .1, .08)
        };

        public static float Opacity(float userValue, bool eligible, bool exempt = false)
        {
            if (!eligible || exempt || float.IsNaN(userValue) || float.IsInfinity(userValue)) return 1f;
            return Math.Max(0f, Math.Min(1f, userValue));
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct VisualEffectLease
    {
        private static long nextOwner;
        internal static long CreateOwner() => Interlocked.Increment(ref nextOwner);
        internal readonly long Owner, Generation, Token;
        internal readonly int Slot;
        internal VisualEffectLease(long owner, long generation, long token, int slot)
        { Owner = owner; Generation = generation; Token = token; Slot = slot; }
        public bool IsTracked => Owner != 0;
    }

    /// <summary>
    /// Caller-clocked visible-effect admission across all variants in one view.
    /// Uses the existing reservation coordinator for duplicate/frame admission.
    /// Lifetime admission is independent of damage, spawning and gameplay objects.
    /// </summary>
    public sealed class VisualEffectBudget<TVariant> where TVariant : struct, IEquatable<TVariant>
    {
        private struct Slot { public long Token; }
        private readonly long owner = VisualEffectLease.CreateOwner();
        private readonly Slot[] active;
        private readonly FeedbackReservationCoordinator<TVariant> reservations;
        private long generation = 1, nextToken;
        private double lastTime = double.NegativeInfinity, windowStart = double.NegativeInfinity;
        private int windowAccepted;
        public int ActiveCount { get; private set; }
        public int WindowAcceptedCount => windowAccepted;

        public VisualEffectBudget(int activeCapacity = 128, int variantCapacity = 128)
        {
            if (activeCapacity < 1) throw new ArgumentOutOfRangeException(nameof(activeCapacity));
            active = new Slot[activeCapacity];
            reservations = new FeedbackReservationCoordinator<TVariant>(variantCapacity, 16, 16, 1d);
        }

        public bool TryAcquire(TVariant variant, VisualEffectSettings settings, double now, int frame,
            bool important, out VisualEffectLease lease)
        {
            lease = default;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < lastTime || frame < 0)
                return false;
            lastTime = now;
            if (settings.Unlimited || important) return true;
            if (settings.FrameLimit < 1 || settings.WindowLimit < 1 || settings.ActiveLimit < 1 ||
                settings.WindowSeconds <= 0 || settings.MergeSeconds <= 0) return false;
            if (now - windowStart >= settings.WindowSeconds)
            { windowStart = now; windowAccepted = 0; }
            if (windowAccepted >= settings.WindowLimit || ActiveCount >= settings.ActiveLimit || ActiveCount >= active.Length)
                return false;

            int free = -1;
            for (int i = 0; i < active.Length; i++) if (active[i].Token == 0) { free = i; break; }
            if (free < 0) return false;
            double bucket = Math.Floor(now / settings.MergeSeconds);
            if (bucket >= long.MaxValue - 1d) return false;
            var key = new FeedbackKey((long)bucket + 1L, 1L);
            if (!reservations.TryBegin(variant, key, 1d, now, frame, settings.FrameLimit, false, out var reservation))
                return false;
            reservations.Complete(reservation, true, now);
            long token = ++nextToken;
            if (token == 0) token = ++nextToken;
            active[free].Token = token;
            ActiveCount++; windowAccepted++;
            lease = new VisualEffectLease(owner, generation, token, free);
            return true;
        }

        public bool Release(VisualEffectLease lease)
        {
            if (lease.Owner != owner || lease.Generation != generation || lease.Slot < 0 ||
                lease.Slot >= active.Length || lease.Token == 0 || active[lease.Slot].Token != lease.Token) return false;
            active[lease.Slot] = default;
            ActiveCount--;
            return true;
        }

        public void Reset()
        {
            Array.Clear(active, 0, active.Length);
            ActiveCount = windowAccepted = 0;
            generation++;
            lastTime = windowStart = double.NegativeInfinity;
            reservations.Reset();
        }
    }
}
