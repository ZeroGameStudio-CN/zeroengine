using System;
using UnityEngine;

namespace ZeroEngine.Activity.Unity
{
    /// <summary>Explicit adapter callbacks; never disables a GameObject, component, AI, or collider itself.</summary>
    public sealed class ActivityAgent : MonoBehaviour, IActivityGate
    {
        private ActivityWorldDriver world;
        private ActivityController controller;
        private Func<ActivityLevel> leastActiveAllowed;
        private Func<double> clock;
        private Action<ActivityLevel> apply;
        private ActivityLevel applied = ActivityLevel.Full;
        private bool recoveryPending;
        private bool errorReported;
        private bool applying;
        public bool IsFaulted { get; private set; }
        public ActivityLevel Level => controller?.Level ?? ActivityLevel.Full;

        public void Configure(ActivityWorldDriver owner, ActivityPolicy policy, Func<double> gameplayClock,
            Func<ActivityLevel> allowedLevel, Action<ActivityLevel> applyLevel)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (gameplayClock == null) throw new ArgumentNullException(nameof(gameplayClock));
            if (allowedLevel == null) throw new ArgumentNullException(nameof(allowedLevel));
            var candidate = new ActivityController(policy);
            Wake();
            if (recoveryPending) throw new InvalidOperationException("Previous activity adapter has not recovered.");
            if (world) world.Unregister(this);
            world = owner;
            controller = candidate;
            clock = gameplayClock;
            leastActiveAllowed = allowedLevel;
            apply = applyLevel;
            IsFaulted = errorReported = false;
            recoveryPending = true;
            if (isActiveAndEnabled) world.Register(this);
            Wake();
        }

        public IDisposable KeepActive()
        {
            if (controller == null) throw new InvalidOperationException("Configure the activity agent first.");
            IDisposable handle = controller.KeepActive();
            TryApply();
            return handle;
        }

        internal void Evaluate(double distance)
        {
            if (controller == null) return;
            if (IsFaulted) { Wake(); return; }
            try
            {
                controller.Evaluate(distance, clock(), leastActiveAllowed());
                if (!TryApply()) Wake();
            }
            catch (Exception exception) { ReportFailure(exception); Wake(); }
        }

        public bool TakeDecisionTick(double gameplayTime)
        {
            bool result = controller == null || controller.TakeDecisionTick(gameplayTime);
            if (!TryApply()) Wake();
            return result || IsFaulted;
        }
        public void Wake() { controller?.Wake(); TryApply(); }
        private bool TryApply()
        {
            // A callback may Wake/KeepActive. Apply that final state after it returns, without recursive callbacks.
            if (applying) return true;
            applying = true;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    ActivityLevel next = Level;
                    if (next == applied && !recoveryPending) return true;
                    apply?.Invoke(next);
                    applied = next;
                    recoveryPending = Level != applied;
                    if (!recoveryPending) return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                recoveryPending = true;
                ReportFailure(exception);
                return false;
            }
            finally { applying = false; }
        }
        private void ReportFailure(Exception exception)
        {
            IsFaulted = true;
            controller?.Wake();
            if (errorReported) return;
            errorReported = true;
            Debug.LogException(exception, this);
        }
        private void OnEnable() { if (world) world.Register(this); Wake(); }
        private void OnDisable() { if (world) world.Unregister(this); Wake(); }
    }
}
