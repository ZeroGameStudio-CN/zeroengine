using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZeroEngine.GameFeel
{
    /// <summary>Per-target cadence with caller-owned clocks and pooled-lifetime validation.</summary>
    public sealed class FeedbackCadenceLimiter<TTarget, TLifetime> where TTarget : class
    {
        private sealed class State
        {
            internal TLifetime Lifetime;
            internal double Last = double.NegativeInfinity;
        }

        private readonly ConditionalWeakTable<TTarget, State> targets = new();
        private readonly Func<TTarget, TLifetime> capture;
        private readonly Func<TTarget, TLifetime, bool> isCurrent;
        private readonly ConditionalWeakTable<TTarget, State>.CreateValueCallback create;

        public FeedbackCadenceLimiter(Func<TTarget, TLifetime> capture,
            Func<TTarget, TLifetime, bool> isCurrent)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            create = target => new State { Lifetime = this.capture(target) };
        }

        public bool TryAcquire(TTarget target, double now, double interval, bool bypass = false)
        {
            if (bypass || target == null || interval <= 0d) return true;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d ||
                double.IsNaN(interval) || double.IsInfinity(interval)) return false;
            var state = targets.GetValue(target, create);
            if (!isCurrent(target, state.Lifetime) || now < state.Last)
            {
                state.Lifetime = capture(target);
                state.Last = double.NegativeInfinity;
            }
            if (now - state.Last < interval) return false;
            state.Last = now;
            return true;
        }

        public void Reset(TTarget target) { if (target != null) targets.Remove(target); }
        public void Reset() => targets.Clear();
    }

    public readonly struct FeedbackNumberMerge<TDisplay> where TDisplay : class
    {
        public readonly bool Merged;
        public readonly TDisplay Display;
        public readonly double Total;
        internal FeedbackNumberMerge(TDisplay display, double total)
        { Merged = true; Display = display; Total = total; }
    }

    /// <summary>Immediate first output, followed by same-target/style accumulation.
    /// Reuses warmed windows; consumers retain formatting, rendering and gameplay callbacks.</summary>
    public sealed class FeedbackNumberMerger<TTarget, TStyle, TDisplay, TLifetime>
        where TTarget : class where TStyle : struct, IEquatable<TStyle> where TDisplay : class
    {
        private sealed class TargetState
        {
            internal readonly Dictionary<TStyle, Window> Styles = new();
        }
        private sealed class Window
        {
            internal TDisplay Display;
            internal uint Activation;
            internal TLifetime Lifetime;
            internal double Start, Total, Duration;
            internal bool Unscaled;
        }

        private readonly ConditionalWeakTable<TTarget, TargetState> targets = new();
        private readonly Func<TTarget, TLifetime> capture;
        private readonly Func<TTarget, TLifetime, bool> targetIsCurrent;
        private readonly Func<TDisplay, uint, bool> displayIsCurrent;
        private readonly int styleCapacity;
        public long Requests { get; private set; }
        public long MergedRequests { get; private set; }

        public FeedbackNumberMerger(Func<TTarget, TLifetime> capture,
            Func<TTarget, TLifetime, bool> targetIsCurrent,
            Func<TDisplay, uint, bool> displayIsCurrent, int styleCapacity = 32)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.targetIsCurrent = targetIsCurrent ?? throw new ArgumentNullException(nameof(targetIsCurrent));
            this.displayIsCurrent = displayIsCurrent ?? throw new ArgumentNullException(nameof(displayIsCurrent));
            if (styleCapacity < 1) throw new ArgumentOutOfRangeException(nameof(styleCapacity));
            this.styleCapacity = styleCapacity;
        }

        public FeedbackNumberMerge<TDisplay> Add(TTarget target, TStyle style, TDisplay display,
            uint activation, double value, double now, double duration, bool unscaled)
        {
            Requests++;
            if (target == null || display == null || !Finite(value) || !Finite(now) || now < 0d ||
                !Finite(duration) || duration <= 0d) return default;
            var styles = targets.GetValue(target, static _ => new TargetState()).Styles;
            if (styles.TryGetValue(style, out var window))
            {
                if (window.Unscaled == unscaled && window.Duration == duration &&
                    now >= window.Start && now - window.Start < duration &&
                    displayIsCurrent(window.Display, window.Activation) &&
                    targetIsCurrent(target, window.Lifetime))
                {
                    window.Total += value;
                    MergedRequests++;
                    return new FeedbackNumberMerge<TDisplay>(window.Display, window.Total);
                }
            }
            else
            {
                // Exhaustion preserves the first/individual output instead of losing a number.
                if (styles.Count >= styleCapacity) return default;
                window = new Window();
                styles.Add(style, window);
            }
            window.Display = display; window.Activation = activation;
            window.Lifetime = capture(target); window.Start = now;
            window.Total = value; window.Duration = duration; window.Unscaled = unscaled;
            return default;
        }

        public void Reset(TTarget target) { if (target != null) targets.Remove(target); }
        public void Clear() => targets.Clear();
        public void Reset() { Clear(); Requests = MergedRequests = 0; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Double-buffered presentation updates. Re-entry cannot overwrite a newer queued version.</summary>
    public sealed class FeedbackUpdateQueue<TDisplay, TVersion> where TDisplay : class
    {
        private Dictionary<TDisplay, TVersion> pending = new(), work = new();
        private bool flushing, clearRequested;
        public void Enqueue(TDisplay display, TVersion version)
        { if (display != null) pending[display] = version; }

        public void Flush(Func<TDisplay, TVersion, bool> apply)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            if (flushing) return;
            (pending, work) = (work, pending);
            flushing = true;
            try
            {
                foreach (var update in work)
                {
                    if (clearRequested) break;
                    bool done = apply(update.Key, update.Value);
                    if (!done && !clearRequested && !pending.ContainsKey(update.Key))
                        pending.Add(update.Key, update.Value);
                }
            }
            finally
            {
                work.Clear(); flushing = false; clearRequested = false;
            }
        }

        public void Clear()
        {
            pending.Clear();
            if (flushing) clearRequested = true;
            else work.Clear();
        }
    }

    /// <summary>Weak target ownership for the existing reservation/budget contract.
    /// Resetting a pooled target or a session invalidates its outstanding tickets.</summary>
    public sealed class TargetFeedbackReservations<TTarget, TVariant>
        where TTarget : class where TVariant : struct, IEquatable<TVariant>
    {
        public readonly struct Ticket
        {
            internal readonly TargetFeedbackReservations<TTarget, TVariant> Owner;
            internal readonly FeedbackReservationCoordinator<TVariant> Coordinator;
            internal readonly FeedbackReservation<TVariant> Reservation;
            internal readonly long Epoch;
            public bool IsTracked => Coordinator != null;
            internal Ticket(TargetFeedbackReservations<TTarget, TVariant> owner,
                FeedbackReservationCoordinator<TVariant> coordinator,
                FeedbackReservation<TVariant> reservation, long epoch)
            { Owner = owner; Coordinator = coordinator; Reservation = reservation; Epoch = epoch; }
        }
        private readonly ConditionalWeakTable<TTarget, FeedbackReservationCoordinator<TVariant>> targets = new();
        private readonly ConditionalWeakTable<TTarget, FeedbackReservationCoordinator<TVariant>>.CreateValueCallback create;
        private long epoch;
        public FeedbackReservationDecision LastDecision { get; private set; }

        public TargetFeedbackReservations(int variantCapacity, int identityCapacity,
            int reservationCapacity, double retentionSeconds)
        {
            if (variantCapacity <= 0 || identityCapacity <= 0 || reservationCapacity <= 0 ||
                double.IsNaN(retentionSeconds) || double.IsInfinity(retentionSeconds) || retentionSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(variantCapacity));
            create = _ => new FeedbackReservationCoordinator<TVariant>(variantCapacity,
                identityCapacity, reservationCapacity, retentionSeconds);
        }

        public bool TryBegin(TTarget target, TVariant variant, FeedbackKey key, double strength,
            double now, int frame, int frameBudget, bool important, out Ticket ticket)
        {
            ticket = default;
            if (target == null) { LastDecision = FeedbackReservationDecision.InvalidKey; return false; }
            var coordinator = targets.GetValue(target, create);
            bool accepted = coordinator.TryBegin(variant, key, strength, now, frame, frameBudget,
                important, out var reservation);
            LastDecision = accepted ? reservation.Decision : coordinator.LastDecision;
            if (accepted) ticket = new Ticket(this, coordinator, reservation, epoch);
            return accepted;
        }

        public FeedbackReservationCommitResult Complete(Ticket ticket, bool played, double now)
        {
            if (!ReferenceEquals(ticket.Owner, this)) return FeedbackReservationCommitResult.WrongCoordinator;
            if (ticket.Epoch != epoch || ticket.Coordinator == null)
                return FeedbackReservationCommitResult.StaleReservation;
            return ticket.Coordinator.Complete(ticket.Reservation, played, now);
        }

        public void Reset(TTarget target)
        {
            if (target == null) return;
            if (targets.TryGetValue(target, out var coordinator)) coordinator.Reset();
            // Keep warmed arrays with the weak target; Reset already invalidates old tickets.
        }
        public void Reset() { targets.Clear(); epoch++; LastDecision = default; }
    }
}
