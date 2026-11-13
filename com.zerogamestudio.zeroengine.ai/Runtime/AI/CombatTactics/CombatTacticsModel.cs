using UnityEngine;

namespace ZeroEngine.AI.CombatTactics
{
    /// <summary>Host-selected capabilities; no item grade, actor type or project resource policy.</summary>
    public readonly struct CombatTacticsProfile
    {
        public readonly bool Charge, Accurate, Counter, Predict;
        public readonly float Awareness, SenseInterval, LookAhead, DodgeInterval;

        public CombatTacticsProfile(bool charge, bool accurate, bool counter, bool predict,
            float awareness, float senseInterval, float lookAhead, float dodgeInterval)
        {
            Charge = charge; Accurate = accurate; Counter = counter; Predict = predict;
            Awareness = awareness; SenseInterval = senseInterval; LookAhead = lookAhead; DodgeInterval = dodgeInterval;
        }
    }

    public enum CombatTacticsAction
    {
        Approach, WaitForAttack, DodgeRecovery, Counter, WalkEscape, AvoidDamage, Dodge, WaitForEscape, WaitForTarget, ApproachDodgingTarget, AttackRecovery, HoldAttack, WaitForAttackEligibility, ReleaseAccurate, ReleaseCharge, EndCharge, TapAttack, WaitForAccuracy, Charge
    }

    public struct CombatTacticsContext
    {
        public CombatTacticsProfile Profile;
        public float Now, Delta, ChargeTimeout, DodgeEntry, WalkSpeed, WalkReach;
        public bool HasTarget, BasePrimary, BaseApproaching, BaseDash, CanDash, WeaponActive;
        public bool SupportsHoldAttack, SupportsCharge, SupportsAccurate, Charging, Charged, Accurate;
        public bool InPerfectDodge, CounterWindowOpen, Executing;
        public bool Danger, LeftSafe, RightSafe, JumpSafer, ApproachUnsafe, Grounded, ParryAvailable, TargetDodging;
        public float ImpactTime;
        public int EscapeDirection, WalkEscapeDirection, BaseMoveX, PreferredEscapeDirection;
    }

    public struct CombatTacticsDecision
    {
        public bool Primary, PressPrimary, Dash, Jump, HasMove;
        public int MoveX;
        public CombatTacticsAction Action;
    }

    // This decides input only. Native weapon/dodge code still owns timing, eligibility and damage.
    public sealed class CombatTacticsModel
    {
        private bool holding, holdingCharge, holdingContinuous, nativeChargeSeen, waitingForRecovery, counterRequested;
        private float holdStarted, nativeChargeStarted, releaseUntil, nextTap, nextDash, nextJump;
        private float dodgeEntry = float.NegativeInfinity;
        private int normalStrokes;
        private int avoidanceDirection;
        private float avoidanceUntil;

        public int PreferredEscapeDirection(float now) => now < avoidanceUntil ? avoidanceDirection : 0;

        public void Reset()
        {
            holding = holdingCharge = holdingContinuous = nativeChargeSeen = waitingForRecovery = counterRequested = false;
            holdStarted = nativeChargeStarted = releaseUntil = nextTap = nextDash = nextJump = 0;
            dodgeEntry = float.NegativeInfinity;
            normalStrokes = 0;
            avoidanceDirection = 0;
            avoidanceUntil = 0;
        }

        public CombatTacticsDecision Step(CombatTacticsContext c)
        {
            var profile = c.Profile;
            var result = new CombatTacticsDecision {
                // Reject the movement paired with a native dash when tactical defense does not select it.
                HasMove = profile.Accurate && (c.ApproachUnsafe || c.BaseDash),
                Action = profile.Accurate && c.ApproachUnsafe ? CombatTacticsAction.WaitForAttack : CombatTacticsAction.Approach
            };
            if (!float.IsFinite(c.Now) || !float.IsFinite(c.Delta) || !c.HasTarget) return result;
            if (c.Delta <= 0) { result.Primary = holding; return result; }
            if (profile.Accurate && c.Danger && float.IsFinite(c.ImpactTime))
            {
                avoidanceUntil = Mathf.Max(avoidanceUntil, c.Now + Mathf.Max(0, c.ImpactTime) + .12f);
                avoidanceDirection = c.WalkEscapeDirection != 0 ? c.WalkEscapeDirection : c.EscapeDirection;
            }
            else if (profile.Accurate && c.Now < avoidanceUntil)
            {
                result.HasMove = true;
                result.Action = CombatTacticsAction.WaitForAttack;
            }
            if (c.InPerfectDodge)
            {
                holding = false;
                if (dodgeEntry != c.DodgeEntry) { dodgeEntry = c.DodgeEntry; counterRequested = false; }
                result.Action = CombatTacticsAction.DodgeRecovery;
                if (profile.Counter && c.CounterWindowOpen && !counterRequested && !c.TargetDodging)
                {
                    counterRequested = true;
                    result.Primary = result.PressPrimary = result.HasMove = true;
                    result.Action = CombatTacticsAction.Counter;
                }
                return result;
            }
            if (c.Executing) { result.Action = CombatTacticsAction.Counter; return result; }

            if (profile.Accurate && c.Danger && c.WalkEscapeDirection != 0)
            {
                holding = holding && c.Charging;
                result.Primary = holding;
                result.HasMove = true;
                result.MoveX = c.WalkEscapeDirection;
                result.Action = CombatTacticsAction.WalkEscape;
                return result;
            }
            bool imminent = c.Danger && c.ImpactTime <= (profile.Predict ? .18f : .25f);
            bool parryReady = profile.Counter && c.ParryAvailable && c.CanDash && c.Now >= nextDash;
            if (profile.Accurate && c.Danger && (imminent || parryReady))
            {
                // Sacrifice charge rather than grant carry/accuracy that the weapon does not possess.
                holding = false;
                waitingForRecovery = c.WeaponActive;
                releaseUntil = c.Now + .1f;
                result.HasMove = true;
                result.MoveX = c.EscapeDirection;
                result.Action = CombatTacticsAction.AvoidDamage;
                if (c.CanDash && c.Now >= nextDash)
                {
                    result.Dash = true;
                    nextDash = c.Now + profile.DodgeInterval;
                    result.Action = CombatTacticsAction.Dodge;
                }
                else if (profile.Predict && c.JumpSafer && c.Now >= nextJump)
                { result.Jump = true; nextJump = c.Now + .6f; }
                return result;
            }
            if (profile.Accurate && c.Danger)
            {
                holding = false;
                result.HasMove = true;
                result.Action = CombatTacticsAction.WaitForEscape;
                return result;
            }
            if (!profile.Accurate && c.BaseDash && c.CanDash && c.Now >= nextDash)
            { result.Dash = true; nextDash = c.Now + profile.DodgeInterval; }

            if (c.TargetDodging)
            {
                holding = holding && c.SupportsCharge && (c.Charging || c.Charged || c.Accurate);
                result.Primary = holding;
                // Stop offense, but keep a safe native approach when the target has dodged out of range.
                bool holdPosition = result.HasMove || c.Danger || holding || !c.BaseApproaching;
                result.HasMove = !result.Dash && holdPosition;
                result.Action = result.Dash ? CombatTacticsAction.Dodge : holdPosition ? CombatTacticsAction.WaitForTarget : CombatTacticsAction.ApproachDodgingTarget;
                return result;
            }

            if (waitingForRecovery)
            {
                if (c.WeaponActive || c.Now < releaseUntil) { result.Action = CombatTacticsAction.AttackRecovery; return result; }
                waitingForRecovery = false;
            }
            if (c.Now < releaseUntil) { result.Action = CombatTacticsAction.AttackRecovery; return result; }

            // Native held attacks own their repeat rate and reloads; only charging weapons need a release window.
            if (c.SupportsHoldAttack && !c.SupportsCharge)
            {
                bool wasHolding = holding && holdingContinuous;
                holding = c.BasePrimary && !result.Dash;
                holdingContinuous = holding;
                holdingCharge = false;
                result.Primary = holding;
                result.PressPrimary = holding && !wasHolding;
                if (holding)
                {
                    result.HasMove = true;
                    result.MoveX = 0;
                }
                result.Action = result.Dash ? CombatTacticsAction.Dodge : holding ? CombatTacticsAction.HoldAttack : CombatTacticsAction.Approach;
                return result;
            }
            if (holdingContinuous) holding = holdingContinuous = false;

            bool canCharge = profile.Charge && c.SupportsCharge;
            // V alternates a short normal probe with a charge, and uses normal attacks in unsafe openings.
            if (profile.Predict && !holding && (normalStrokes == 0 || c.Danger)) canCharge = false;
            if (holding)
            {
                canCharge = holdingCharge;
                bool nativeWindow = c.Charging || c.Charged || c.Accurate;
                if (canCharge)
                {
                    if (nativeWindow && !nativeChargeSeen)
                    { nativeChargeSeen = true; nativeChargeStarted = c.Now; }
                    else if (!nativeWindow && nativeChargeSeen)
                    { nativeChargeSeen = false; holdStarted = c.Now; }
                    if (!nativeWindow && !c.BasePrimary)
                    {
                        holding = false;
                        waitingForRecovery = c.WeaponActive;
                        releaseUntil = c.Now + .06f;
                        result.Action = CombatTacticsAction.WaitForAttackEligibility;
                        return result;
                    }
                }
                bool ready = canCharge && (profile.Accurate && c.SupportsAccurate ? c.Accurate : c.Charged);
                float timeoutStart = canCharge && nativeChargeSeen ? nativeChargeStarted : holdStarted;
                float timeout = canCharge && !nativeChargeSeen ? Mathf.Max(4, c.ChargeTimeout * 2) : Mathf.Max(.25f, c.ChargeTimeout);
                if (ready || c.Now >= timeoutStart + timeout)
                {
                    holding = false;
                    waitingForRecovery = true;
                    releaseUntil = c.Now + .12f;
                    result.Action = c.Accurate ? CombatTacticsAction.ReleaseAccurate : c.Charged ? CombatTacticsAction.ReleaseCharge : CombatTacticsAction.EndCharge;
                    if (canCharge) normalStrokes = 0;
                    return result;
                }
                // Normal taps stay below the weapon's first real charge; no damage/state is synthesized.
                if (!canCharge && c.Now >= holdStarted + .065f)
                {
                    holding = false;
                    releaseUntil = c.Now + .06f;
                    nextTap = c.Now + .1f;
                    normalStrokes++;
                    result.Action = CombatTacticsAction.TapAttack;
                    return result;
                }
                result.Primary = true;
                result.Action = canCharge ? profile.Accurate && c.SupportsAccurate ? CombatTacticsAction.WaitForAccuracy : CombatTacticsAction.Charge : CombatTacticsAction.TapAttack;
                return result;
            }
            if (!c.BasePrimary || c.Now < nextTap) return result;
            holding = true;
            holdingCharge = canCharge;
            nativeChargeSeen = false;
            holdStarted = c.Now;
            result.Primary = result.PressPrimary = true;
            result.Action = canCharge ? profile.Accurate && c.SupportsAccurate ? CombatTacticsAction.WaitForAccuracy : CombatTacticsAction.Charge : CombatTacticsAction.TapAttack;
            return result;
        }

        public static float ToOwnerSeconds(float globalSeconds, float ownerScale)
            => float.IsFinite(globalSeconds) && globalSeconds >= 0 && float.IsFinite(ownerScale) && ownerScale >= 0
                ? globalSeconds * ownerScale : float.PositiveInfinity;

        public static int ChooseWalkDirection(float left, float right, int preferred)
        {
            if (preferred < 0 && float.IsFinite(left)) return -1;
            if (preferred > 0 && float.IsFinite(right)) return 1;
            return float.IsFinite(Mathf.Min(left, right)) ? left <= right ? -1 : 1 : 0;
        }

        public static float PredictWalkDistance(float speed, float velocity, float duration,
            AnimationCurve curve, float seconds, float fixedDelta)
        {
            if (curve == null || !float.IsFinite(speed) || !float.IsFinite(velocity) ||
                !float.IsFinite(seconds) || !float.IsFinite(fixedDelta) || speed <= 0 || seconds <= 0 || fixedDelta <= 0) return 0;
            velocity = Mathf.Clamp(velocity, 0, speed);
            float distance = 0, elapsed = 0, step = Mathf.Max(fixedDelta, seconds / 32);
            for (int i = 0; i < 32 && elapsed < seconds; i++, elapsed += step)
            {
                if (velocity < speed * .9f)
                    velocity = Mathf.Clamp(velocity + speed * curve.Evaluate(Mathf.Clamp01(elapsed / Mathf.Max(.001f, duration))), 0, speed);
                else velocity = speed;
                distance += velocity * Mathf.Min(step, seconds - elapsed);
            }
            return distance;
        }

        public static bool TryImpact(Vector2 relative, Vector2 velocity, float radius, float horizon, out float time)
        {
            time = float.PositiveInfinity;
            float distance = relative.sqrMagnitude - radius * radius;
            if (distance <= 0) { time = 0; return true; }
            float speed = velocity.sqrMagnitude;
            float approach = Vector2.Dot(relative, velocity);
            if (speed < .0001f || approach >= 0) return false;
            float discriminant = approach * approach - speed * distance;
            if (discriminant < 0) return false;
            time = (-approach - Mathf.Sqrt(discriminant)) / speed;
            return time >= 0 && time <= horizon;
        }

        // Observe only the attack cue in the animation that is already playing; never select a future boss move.
        public static bool TryAnimationImpact(AnimationEvent[] events, float length, float normalizedTime,
            float speed, bool looping, string eventFunctionName, int attackEventId, out float time)
        {
            time = float.PositiveInfinity;
            if (events == null || !float.IsFinite(length) || length <= 0 || !float.IsFinite(normalizedTime) ||
                normalizedTime < 0 || !float.IsFinite(speed) || speed <= 0 || !looping && normalizedTime >= 1) return false;
            float local = (normalizedTime - Mathf.Floor(normalizedTime)) * length;
            foreach (var cue in events)
                if (cue != null && cue.functionName == eventFunctionName && cue.intParameter == attackEventId &&
                    float.IsFinite(cue.time) && cue.time >= local && cue.time <= length)
                    time = Mathf.Min(time, (cue.time - local) / speed);
            return float.IsFinite(time);
        }
    }
}
