using System;

namespace ZeroEngine.Activity
{
    public enum ActivityLevel
    {
        Full = 0,
        Reduced = 1,
        Dormant = 2
    }

    /// <summary>Optional gate for expensive decisions, not a replacement gameplay clock.</summary>
    public interface IActivityGate
    {
        ActivityLevel Level { get; }
        bool TakeDecisionTick(double gameplayTime);
        void Wake();
    }

    public readonly struct ActivityPolicy
    {
        public readonly double FullDistance;
        public readonly double DormantDistance;
        public readonly double Hysteresis;
        public readonly double DowngradeDelay;
        public readonly double ReducedInterval;

        public ActivityPolicy(double fullDistance, double dormantDistance, double hysteresis,
            double downgradeDelay, double reducedInterval)
        {
            if (!Finite(fullDistance) || fullDistance < 0 || !Finite(dormantDistance)
                || dormantDistance < fullDistance || !Finite(hysteresis) || hysteresis < 0
                || !Finite(downgradeDelay) || downgradeDelay < 0
                || !Finite(reducedInterval) || reducedInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(fullDistance), "Invalid activity policy.");
            FullDistance = fullDistance;
            DormantDistance = dormantDistance;
            Hysteresis = hysteresis;
            DowngradeDelay = downgradeDelay;
            ReducedInterval = reducedInterval;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Single-threaded, one instance per participant. Missing observations fail open to Full.
    /// The caller supplies monotonic policy time and its own gameplay time for decisions.
    /// </summary>
    public sealed class ActivityController : IActivityGate
    {
        private readonly ActivityPolicy policy;
        private ActivityLevel pendingLevel;
        private double pendingSince;
        private double lastPolicyTime = double.NegativeInfinity;
        private double nextDecision = double.NegativeInfinity;
        private double lastDecisionTime = double.NegativeInfinity;
        private int keepActiveCount;

        public ActivityLevel Level { get; private set; } = ActivityLevel.Full;

        public ActivityController(ActivityPolicy policy)
        {
            // A default struct is not a valid policy.
            if (policy.ReducedInterval <= 0) throw new ArgumentException("Uninitialized policy.", nameof(policy));
            this.policy = policy;
        }

        public IDisposable KeepActive()
        {
            keepActiveCount++;
            Wake();
            return new KeepActiveHandle(this);
        }

        public ActivityLevel Evaluate(double distance, double policyTime, ActivityLevel leastActiveAllowed)
        {
            if (!ActivityPolicy.Finite(policyTime) || policyTime < lastPolicyTime
                || !ActivityPolicy.Finite(distance) || distance < 0
                || leastActiveAllowed < ActivityLevel.Full || leastActiveAllowed > ActivityLevel.Dormant)
            {
                Wake();
                if (ActivityPolicy.Finite(policyTime)) lastPolicyTime = policyTime;
                return Level;
            }
            lastPolicyTime = policyTime;
            if (keepActiveCount > 0 || leastActiveAllowed == ActivityLevel.Full)
            {
                Wake();
                return Level;
            }

            double fullBoundary = policy.FullDistance + (Level == ActivityLevel.Full ? policy.Hysteresis : 0);
            double dormantBoundary = policy.DormantDistance + (Level != ActivityLevel.Dormant ? policy.Hysteresis : 0);
            ActivityLevel desired = distance <= fullBoundary ? ActivityLevel.Full
                : distance <= dormantBoundary ? ActivityLevel.Reduced : ActivityLevel.Dormant;
            if (desired > leastActiveAllowed) desired = leastActiveAllowed;

            if (desired <= Level)
            {
                if (desired < Level) nextDecision = double.NegativeInfinity;
                Level = pendingLevel = desired;
                pendingSince = policyTime;
            }
            else
            {
                if (pendingLevel != desired)
                {
                    pendingLevel = desired;
                    pendingSince = policyTime;
                }
                if (policyTime - pendingSince >= policy.DowngradeDelay) Level = desired;
            }
            return Level;
        }

        public bool TakeDecisionTick(double gameplayTime)
        {
            if (!ActivityPolicy.Finite(gameplayTime)) { Wake(); return true; }
            if (gameplayTime < lastDecisionTime) nextDecision = double.NegativeInfinity;
            lastDecisionTime = gameplayTime;
            if (Level == ActivityLevel.Full) return true;
            if (Level == ActivityLevel.Dormant || gameplayTime < nextDecision) return false;
            nextDecision = gameplayTime + policy.ReducedInterval;
            return true;
        }

        public void Wake()
        {
            Level = pendingLevel = ActivityLevel.Full;
            nextDecision = double.NegativeInfinity;
        }

        private sealed class KeepActiveHandle : IDisposable
        {
            private ActivityController owner;
            public KeepActiveHandle(ActivityController owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                owner.keepActiveCount--;
                owner = null;
            }
        }
    }
}
