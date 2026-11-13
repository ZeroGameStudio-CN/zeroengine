using System;
using System.Collections.Generic;

namespace ZeroEngine.Interaction.Core
{
    /// <summary>One interactor's focus and input session. All methods and callbacks run on its owning thread.</summary>
    public sealed class InteractionAgent : IDisposable
    {
        private readonly InteractionWorld world;
        private readonly IInteractionClock clock;
        private readonly IInteractionRange range;
        private readonly object actor;
        private readonly double maxDistanceSquared;
        private readonly List<InteractionRegistration> candidates = new List<InteractionRegistration>();
        private InteractionRequest pending;
        private bool disposed, pressed, trackingHold, holdFired, committing;
        private InteractionPolicy policy;
        private double pressedAt, nextRepeat, lastTime;
        private long requestId;
        public string ActorId { get; }
        public InteractionRegistration Focus { get; private set; }
        public InteractionRequest Pending => pending;
        public double Progress { get; private set; }
        public bool IsDisposed => disposed;
        public event Action<InteractionRegistration, InteractionRegistration> FocusChanged;
        public event Action<InteractionResult> Completed;

        public InteractionAgent(InteractionWorld world, string actorId, object actor, IInteractionClock clock,
            IInteractionRange range, double maxDistance)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.range = range ?? throw new ArgumentNullException(nameof(range));
            if (string.IsNullOrWhiteSpace(actorId)) throw new ArgumentException("Actor ID is required.", nameof(actorId));
            if (!Numbers.Finite(maxDistance) || maxDistance < 0 || !Numbers.Finite(maxDistance * maxDistance))
                throw new ArgumentOutOfRangeException(nameof(maxDistance));
            if (!Numbers.Finite(clock.Now)) throw new ArgumentException("Clock must return finite values.", nameof(clock));
            ActorId = actorId; this.actor = actor; maxDistanceSquared = maxDistance * maxDistance; lastTime = clock.Now;
            world.Attach(this);
            world.Removed += OnRemoved;
        }

        public void RefreshFocus()
        {
            if (disposed || world.IsDisposed) return;
            world.CopyTargets(candidates);
            InteractionRegistration best = null;
            double bestDistance = double.MaxValue;
            int bestPriority = int.MinValue;
            foreach (var candidate in candidates)
            {
                if (!Admitted(candidate, out var distance)) continue;
                int priority = candidate.Target.Priority;
                if (best != null && (priority < bestPriority || priority == bestPriority &&
                    (distance > bestDistance || distance == bestDistance && StringComparer.Ordinal.Compare(candidate.Id, best.Id) >= 0))) continue;
                best = candidate; bestDistance = distance; bestPriority = priority;
            }
            SetFocus(best);
        }

        public bool Press()
        {
            if (disposed || committing || pressed) return false;
            if (!AdvanceClock(out _)) return false;
            RefreshFocus();
            if (pending != null || Focus == null) return false;
            pressed = true;
            policy = Focus.Target.Policy;
            if (policy.Mode == InteractionMode.Press) return Execute(InteractionGesture.Tap);
            // Reject invalid/default hold policies instead of entering an unfinishable session.
            if (policy.HoldSeconds <= 0 || !Numbers.Finite(policy.HoldSeconds)) { pressed = false; return false; }
            pressedAt = ReadTime(); trackingHold = true; holdFired = false; Progress = 0;
            return true;
        }

        public void Release()
        {
            if (disposed) return;
            if (!AdvanceClock(out _)) return;
            RefreshFocus();
            // The release edge can arrive before the frame's Tick at the hold threshold.
            if (pressed && trackingHold && !holdFired && ReadTime() - pressedAt >= policy.HoldSeconds)
            {
                holdFired = true;
                Execute(InteractionGesture.Hold);
            }
            bool tap = pressed && trackingHold && !holdFired && policy.Mode == InteractionMode.TapOrHold;
            pressed = false; trackingHold = false; Progress = 0;
            if (pending != null && pending.Context.Gesture != InteractionGesture.Tap)
                Settle(pending, InteractionStatus.Cancelled, "Hold released.");
            if (tap && Focus != null) Execute(InteractionGesture.Tap);
        }

        public void Tick()
        {
            if (disposed) return;
            if (!AdvanceClock(out var now)) return;
            RefreshFocus();
            if (pending != null && !Validate(pending, out _, out var denial))
                Settle(pending, InteractionStatus.Cancelled, denial);
            if (!trackingHold || !pressed || Focus == null) return;
            Progress = Math.Min(1, Math.Max(0, (now - pressedAt) / policy.HoldSeconds));
            if (pending != null || committing) return;
            if (!holdFired && now - pressedAt >= policy.HoldSeconds)
            {
                holdFired = true; nextRepeat = now + policy.RepeatSeconds;
                Execute(InteractionGesture.Hold);
            }
            else if (holdFired && policy.RepeatSeconds > 0 && now >= nextRepeat)
            {
                // A frame jump never causes an unbounded catch-up burst of business mutations.
                nextRepeat = now + policy.RepeatSeconds;
                Execute(InteractionGesture.Repeat);
            }
        }

        public void Cancel(string reason = "Interaction cancelled.")
        {
            pressed = false; trackingHold = false; holdFired = false; Progress = 0;
            if (pending != null) Settle(pending, InteractionStatus.Cancelled, reason);
        }

        private void SetFocus(InteractionRegistration target)
        {
            if (ReferenceEquals(Focus, target)) return;
            var previous = Focus;
            // Preserve the physical press latch until Release; a new target cannot inherit an old hold.
            trackingHold = false; holdFired = false; Progress = 0;
            Focus = target;
            if (pending != null) Settle(pending, InteractionStatus.Cancelled, "Focus changed.");
            FocusChanged?.Invoke(previous, Focus);
        }

        private void OnRemoved(InteractionRegistration registration)
        {
            if (ReferenceEquals(Focus, registration)) SetFocus(null);
        }

        private bool Admitted(InteractionRegistration registration, out double distance)
        {
            distance = 0;
            return registration != null && registration.IsRegistered && registration.Target.IsAvailable &&
                range.TryGetSquaredDistance(registration.Target, out distance) && Numbers.Finite(distance) &&
                distance >= 0 && distance <= maxDistanceSquared;
        }

        private double ReadTime()
        {
            double now = clock.Now;
            if (!Numbers.Finite(now)) throw new InvalidOperationException("Interaction clock returned a non-finite value.");
            return now;
        }

        private bool AdvanceClock(out double now)
        {
            now = ReadTime();
            bool reset = now < lastTime;
            lastTime = now;
            if (!reset) return true;
            Cancel("Clock reset.");
            return false;
        }

        private bool Execute(InteractionGesture gesture)
        {
            if (committing || pending != null || Focus == null || disposed) return false;
            if (!AdvanceClock(out var now)) return false;
            var target = Focus;
            if (!Admitted(target, out var distance)) return false;
            var context = new InteractionContext(ActorId, actor, target, gesture, now, distance);
            var request = new InteractionRequest(this, target, context, checked(++requestId));
            pending = request;
            try
            {
                var check = target.Target.Check(context);
                if (!check.Allowed) { Settle(request, InteractionStatus.Denied, check.Reason); return false; }
                if (!IsCurrent(request)) { Settle(request, InteractionStatus.Cancelled, "Target lifetime changed."); return false; }
                target.Target.Execute(request);
                return true;
            }
            catch (Exception exception)
            {
                Settle(request, InteractionStatus.Faulted, exception.Message);
                return false;
            }
        }

        internal bool IsCurrent(InteractionRequest request) => !disposed && !world.IsDisposed &&
            !request.IsSettled && ReferenceEquals(pending, request) && ReferenceEquals(Focus, request.Registration) &&
            Admitted(request.Registration, out _);

        private bool Validate(InteractionRequest request, out InteractionContext context, out string denial)
        {
            context = default;
            denial = "Target is no longer available or in range.";
            if (!IsCurrent(request) || !Admitted(request.Registration, out var distance)) return false;
            if (!AdvanceClock(out var now)) { denial = "Clock reset."; return false; }
            context = new InteractionContext(ActorId, actor, request.Registration, request.Context.Gesture, now, distance);
            try
            {
                var check = request.Registration.Target.Check(context);
                denial = check.Reason;
                return check.Allowed && IsCurrent(request);
            }
            catch (Exception exception) { denial = exception.Message; return false; }
        }

        internal bool Commit(InteractionRequest request, Action mutation)
        {
            if (request.IsSettled || !ReferenceEquals(pending, request)) return false;
            if (!Validate(request, out _, out var denial))
            { Settle(request, InteractionStatus.Denied, denial); return false; }
            // Settle before user code so callbacks, pooling and reentry cannot commit twice.
            request.MarkSettled(); pending = null; request.ClearCallbacks(); committing = true;
            var status = InteractionStatus.Succeeded;
            string reason = null;
            try { mutation?.Invoke(); }
            catch (Exception exception) { status = InteractionStatus.Faulted; reason = exception.Message; }
            finally { committing = false; }
            Completed?.Invoke(new InteractionResult(request.Context, request.Id, status, reason));
            return status == InteractionStatus.Succeeded;
        }

        internal bool Settle(InteractionRequest request, InteractionStatus status, string reason)
        {
            if (request.IsSettled || !ReferenceEquals(pending, request)) return false;
            request.MarkSettled(); pending = null;
            try { if (status == InteractionStatus.Cancelled) request.NotifyCancelled(reason); }
            finally
            {
                request.ClearCallbacks();
                Completed?.Invoke(new InteractionResult(request.Context, request.Id, status, reason));
            }
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            world.Removed -= OnRemoved;
            world.Detach(this);
            try { Cancel("Interactor disposed."); }
            finally { Focus = null; candidates.Clear(); FocusChanged = null; Completed = null; }
        }
    }
}
