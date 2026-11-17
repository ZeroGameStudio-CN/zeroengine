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
        public readonly int HitCount;
        internal FeedbackNumberMerge(TDisplay display, double total, int hitCount)
        { Merged = true; Display = display; Total = total; HitCount = hitCount; }
    }

    /// <summary>Immediate first output, followed by same-target/style accumulation.
    /// Reuses warmed windows; consumers retain formatting, rendering and gameplay callbacks.</summary>
    public sealed class FeedbackNumberMerger<TTarget, TStyle, TDisplay, TLifetime>
        where TTarget : class where TStyle : struct, IEquatable<TStyle> where TDisplay : class
    {
        private sealed class TargetState
        {
            internal readonly Dictionary<Key, Window> Windows;
            internal readonly Window[] Slots;
            internal TLifetime Lifetime;
            internal double LastTime;
            internal int Used;
            internal long Generation;
            internal TargetState(int capacity, TLifetime lifetime)
            { Windows = new(capacity); Slots = new Window[capacity]; Lifetime = lifetime; }
            internal void Clear(TLifetime lifetime)
            { Windows.Clear(); Used = 0; Lifetime = lifetime; Generation++; }
        }
        private readonly struct Key : IEquatable<Key>
        {
            internal readonly TStyle Style;
            internal readonly FeedbackNumberContext Context;
            internal readonly bool Unscaled;
            internal Key(TStyle style, FeedbackNumberContext context, bool unscaled)
            { Style = style; Context = context; Unscaled = unscaled; }
            public bool Equals(Key other) => Style.Equals(other.Style) && Unscaled == other.Unscaled &&
                Context.Mode == other.Context.Mode && Context.OwnerId == other.Context.OwnerId &&
                Context.SourceId == other.Context.SourceId && Context.GroupId == other.Context.GroupId;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Style, (int)Context.Mode, Context.OwnerId,
                Context.SourceId, Context.GroupId, Unscaled);
        }
        private sealed class Window
        {
            internal Key Key;
            internal TDisplay Display;
            internal uint Activation;
            internal double Start, Total, Duration, Last, Interval, BeatTime;
            internal long Beat;
            internal long Revision;
            internal int Count;
            internal bool HasInterval, Dense;
        }

        private readonly ConditionalWeakTable<TTarget, TargetState> targets = new();
        private readonly Func<TTarget, TLifetime> capture;
        private readonly Func<TTarget, TLifetime, bool> targetIsCurrent;
        private readonly Func<TDisplay, uint, bool> displayIsCurrent;
        private readonly int styleCapacity;
        private readonly FeedbackNumberPolicy policy;
        private readonly ConditionalWeakTable<TTarget, TargetState>.CreateValueCallback create;
        private long revision, epoch;
        public long Requests { get; private set; }
        public long MergedRequests { get; private set; }

        /// <summary>A synchronous output decision. Commit a newly created display only when ShouldCreate is true.
        /// A default/untracked request still permits individual output. Merged requests need no new display.</summary>
        public readonly struct Request
        {
            internal readonly FeedbackNumberMerger<TTarget, TStyle, TDisplay, TLifetime> Owner;
            internal readonly TTarget Target;
            internal readonly object State, Slot;
            internal readonly long Epoch, Generation, Revision;
            internal readonly double Value, Time, Duration;
            public FeedbackNumberMerge<TDisplay> Merge { get; }
            public bool ShouldCreate => !Merge.Merged;
            public bool IsTracked => Owner != null;
            public double EndsAt => Time + Duration;
            internal Request(FeedbackNumberMerge<TDisplay> merge) : this() { Merge = merge; }
            internal Request(FeedbackNumberMerger<TTarget, TStyle, TDisplay, TLifetime> owner,
                TTarget target, object state, object slot, long epoch, long generation, long revision,
                double value, double time, double duration) : this()
            {
                Owner = owner; Target = target; State = state; Slot = slot;
                Epoch = epoch; Generation = generation; Revision = revision;
                Value = value; Time = time; Duration = duration;
            }
        }

        public FeedbackNumberMerger(Func<TTarget, TLifetime> capture,
            Func<TTarget, TLifetime, bool> targetIsCurrent,
            Func<TDisplay, uint, bool> displayIsCurrent, int styleCapacity = 32, FeedbackNumberPolicy? policy = null)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.targetIsCurrent = targetIsCurrent ?? throw new ArgumentNullException(nameof(targetIsCurrent));
            this.displayIsCurrent = displayIsCurrent ?? throw new ArgumentNullException(nameof(displayIsCurrent));
            if (styleCapacity < 1) throw new ArgumentOutOfRangeException(nameof(styleCapacity));
            this.styleCapacity = styleCapacity;
            this.policy = policy ?? FeedbackNumberPolicy.Default;
            if (this.policy.MaximumWindow <= 0) throw new ArgumentOutOfRangeException(nameof(policy));
            create = target => new TargetState(this.styleCapacity, this.capture(target));
        }

        public FeedbackNumberMerge<TDisplay> Add(TTarget target, TStyle style, TDisplay display,
            uint activation, double value, double now, double duration, bool unscaled)
            => Add(target, style, new FeedbackNumberContext(FeedbackNumberMode.Action, 1, 1, 1),
                display, activation, value, now, duration, unscaled, true);

        /// <summary>Individual/important/unknown requests bypass aggregation. Stream density never changes producer intent.</summary>
        public FeedbackNumberMerge<TDisplay> Add(TTarget target, TStyle style, FeedbackNumberContext context,
            TDisplay display, uint activation, double value, double now, double duration, bool unscaled)
            => Add(target, style, context, display, activation, value, now, duration, unscaled, false);

        private FeedbackNumberMerge<TDisplay> Add(TTarget target, TStyle style, FeedbackNumberContext context,
            TDisplay display, uint activation, double value, double now, double duration, bool unscaled, bool legacy)
        {
            if (display == null) { Requests++; return default; }
            var request = Prepare(target, style, context, value, now, duration, unscaled, legacy);
            if (request.ShouldCreate) Commit(request, display, activation);
            return request.Merge;
        }

        /// <summary>Decide before spawning/formatting a display. The consumer applies Merge.Total to Merge.Display,
        /// or creates one output and calls Commit. No factory, callback or per-request allocation is required.</summary>
        public Request Prepare(TTarget target, TStyle style, FeedbackNumberContext context,
            double value, double now, double duration, bool unscaled) =>
            Prepare(target, style, context, value, now, duration, unscaled, false);

        private Request Prepare(TTarget target, TStyle style, FeedbackNumberContext context,
            double value, double now, double duration, bool unscaled, bool legacy)
        {
            Requests++;
            if (target == null || !Finite(value) || !Finite(now) || now < 0d ||
                !Finite(duration) || duration <= 0d || !context.CanMerge) return default;
            if (!legacy) duration = Math.Min(duration, policy.MaximumWindow);
            var state = targets.GetValue(target, create);
            if (!targetIsCurrent(target, state.Lifetime) || now < state.LastTime)
                state.Clear(capture(target));
            state.LastTime = now;
            var key = new Key(style, context, unscaled);
            bool existing = state.Windows.TryGetValue(key, out var window);
            if (!existing)
            {
                if (state.Used < styleCapacity)
                {
                    int index = state.Used++;
                    window = state.Slots[index] ??= new Window();
                }
                else
                {
                    foreach (var slot in state.Slots)
                    {
                        double retention = slot.Key.Context.Mode == FeedbackNumberMode.AdaptiveStream
                            ? Math.Max(slot.Duration, policy.IdleSeconds) : slot.Duration;
                        if (now - slot.Last < retention && Current(slot)) continue;
                        window = slot; state.Windows.Remove(slot.Key); break;
                    }
                    // Bounded capacity falls back to individual output; no live group is stolen and no damage is lost.
                    if (window == null) return default;
                }
                window.Key = key; window.Dense = window.HasInterval = false;
                window.Display = null; window.Count = 0;
                state.Windows.Add(key, window);
            }

            bool sameBeat = existing && context.BeatId > 0 && context.BeatId == window.Beat;
            if (context.Mode == FeedbackNumberMode.AdaptiveStream && existing && !sameBeat)
            {
                double interval = now - window.BeatTime;
                if (interval >= policy.IdleSeconds) window.Dense = window.HasInterval = false;
                else
                {
                    window.Interval = window.HasInterval ? .5d * (window.Interval + interval) : interval;
                    window.HasInterval = true;
                    window.Dense = window.Interval <= (window.Dense ? policy.ExitInterval : policy.EnterInterval);
                }
            }
            if (!sameBeat) { window.BeatTime = now; window.Beat = context.BeatId; }
            window.Last = now;
            bool eligible = context.Mode == FeedbackNumberMode.Action || context.Mode == FeedbackNumberMode.Accumulate || sameBeat || window.Dense;
            if (existing && eligible && (!legacy || window.Duration == duration) &&
                now - window.Start < window.Duration && Current(window))
            {
                double total = window.Total + value;
                if (!Finite(total)) return default;
                window.Total = total; window.Count++; MergedRequests++;
                window.Revision = ++revision;
                return new Request(new FeedbackNumberMerge<TDisplay>(window.Display, total, window.Count));
            }

            window.Revision = ++revision;
            return new Request(this, target, state, window, epoch, state.Generation, window.Revision,
                value, now, duration);
        }

        /// <summary>Attach successful output once. Failed spawns need no commit. Clear, pooled targets,
        /// slot reuse, re-entry and newer requests invalidate an older decision without stealing their output.</summary>
        public bool Commit(Request request, TDisplay display, uint activation)
        {
            if (!ReferenceEquals(request.Owner, this) || request.Epoch != epoch || display == null ||
                !displayIsCurrent(display, activation) || !targets.TryGetValue(request.Target, out var state) ||
                !ReferenceEquals(request.State, state) || request.Generation != state.Generation ||
                !targetIsCurrent(request.Target, state.Lifetime)) return false;
            var window = (Window)request.Slot;
            if (window.Revision != request.Revision || !state.Windows.TryGetValue(window.Key, out var current) ||
                !ReferenceEquals(current, window)) return false;
            window.Revision = ++revision;
            window.Display = display; window.Activation = activation;
            // The first request owns the deadline; neither hits nor budget changes extend it.
            window.Start = request.Time; window.Total = request.Value; window.Duration = request.Duration; window.Count = 1;
            return true;
        }

        private bool Current(Window window) => window.Display != null && displayIsCurrent(window.Display, window.Activation);

        public void Reset(TTarget target) { if (target != null) targets.Remove(target); }
        public void Clear() { epoch++; targets.Clear(); }
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
