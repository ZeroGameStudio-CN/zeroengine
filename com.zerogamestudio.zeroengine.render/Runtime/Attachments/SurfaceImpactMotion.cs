using UnityEngine;

namespace ZeroEngine.Render.Attachments
{
    /// <summary>Continuous entry, angular recoil and visible-tip compensation in the XY plane.</summary>
    public struct SurfaceImpactMotion
    {
        public const float DefaultSurfaceSpread = 5f;
        private SurfaceAttachmentPose pose;
        private Vector2 impactTip;
        private Vector3 entryOffset, forwardEntry, incomingRight, incomingUp;
        private float impactStart, handoffTime, reboundRate, flightSpin;
        private float springAge, entrySlope, turnSide, bladeTurn, coastTime, sweepTransfer;
        private float minimumEntryTime, reboundOnset;
        private float counterRecoilWeight, counterRecoilPeak;
        public float EntryDuration => Mathf.Max(Mathf.Max(handoffTime, sweepTransfer), minimumEntryTime);
        private bool forwardOnly;
        public bool Active => pose.Active;
        public bool IsEntering(float now) => forwardOnly && now - impactStart < EntryDuration;
        public Vector3 LastContact => pose.LastContact;
        public void Begin(Transform projectile, Transform reference, Vector3 contact, float degrees,
            Vector2 localTip, float now, Vector2 surfaceNormal = default)
        {
            handoffTime = sweepTransfer = minimumEntryTime = 0f;
            forwardOnly = false;
            pose.Begin(projectile, reference, contact, degrees, localTip, now, surfaceNormal);
        }

        public void BeginImpact(Transform projectile, Transform reference, Vector3 contact, Vector2 localTip,
            Vector2 entryDisplacement, float now, Vector2 normal, SurfaceImpact impact,
            float preferredSide = 0f, float swingScale = 1f, float settledBladeTurn = 0f, float entryBrakeDistance = 0f,
            float sweptTransferSeconds = 0f, float minimumEntrySeconds = 0f, float reboundRateScale = 1f,
            float reboundOnsetSeconds = 0f, float counterRecoilBlend = 0f, Vector2 recoilClearance = default)
        {
            impactStart = now;
            impactTip = localTip;
            handoffTime = reference ? .064f : .046f;
            springAge = 0f;
            bladeTurn = settledBladeTurn;
            Vector2 direction = impact.Direction;
            forwardOnly = entryBrakeDistance > 0f;
            minimumEntryTime = forwardOnly ? minimumEntrySeconds : 0f;
            reboundOnset = forwardOnly ? Mathf.Max(0f, reboundOnsetSeconds) : 0f;
            var adjustment = forwardOnly ? entryDisplacement : Vector2.ClampMagnitude(entryDisplacement, .38f);
            float forwardGap = Mathf.Max(0f, -Vector2.Dot(adjustment, direction));
            var forward = (Vector3)(-direction * forwardGap);
            sweepTransfer = forwardOnly && ((Vector3)adjustment - forward).sqrMagnitude > 1e-8f ? sweptTransferSeconds : 0f;
            entryOffset = reference ? reference.InverseTransformVector(adjustment) : (Vector3)adjustment;
            forwardEntry = reference ? reference.InverseTransformVector(forward) : forward;
            incomingRight = reference ? reference.InverseTransformVector(projectile.right) : projectile.right;
            incomingUp = reference ? reference.InverseTransformVector(projectile.up) : projectile.up;
            // A monotone cubic carries the actual incoming speed into a gap, up to the
            // slope at which it would overshoot. Sideways alignment starts gently.
            entrySlope = forwardGap > .001f ? Mathf.Clamp(impact.Speed * handoffTime / forwardGap, 0f, 3f) : 0f;
            if (forwardOnly)
            {
                // Preserve incoming speed and spend it over the final fraction of the insertion.
                float incomingSpeed = Mathf.Max(.001f, impact.Speed);
                float braking = Mathf.Min(forwardGap, entryBrakeDistance);
                coastTime = (forwardGap - braking) / incomingSpeed;
                handoffTime = coastTime + 2f * braking / incomingSpeed;
                entrySlope = forwardGap > 0f ? incomingSpeed / forwardGap : 0f;
                swingScale *= .65f;
            }

            float incidence = Mathf.Clamp01(Vector2.Dot(-normal, direction));
            float tangent = Vector2.Dot(new Vector2(-normal.y, normal.x), direction);
            turnSide = preferredSide != 0f ? preferredSide :
                Mathf.Abs(tangent) > .10f ? Mathf.Sign(tangent) : impact.HeadOnSide;
            float speed = Mathf.InverseLerp(4f, 24f, impact.Speed);
            float force = .65f * speed + .35f * (1f - incidence);
            float swing = Mathf.Lerp(5.5f, 10.5f, force) * impact.Swing * swingScale;
            reboundRate = (reference ? 1.04f : 1.28f) * impact.Rate * reboundRateScale;
            flightSpin = Mathf.Clamp(impact.Spin, -920f, 920f) * Mathf.InverseLerp(.15f, .65f, incidence);
            if (impact.Spin != 0f)
            {
                swing *= .88f;
                reboundRate *= 1.08f;
            }
            counterRecoilWeight = forwardOnly ? Mathf.Clamp01(counterRecoilBlend) : 0f;
            if (counterRecoilWeight > 0f)
            {
                // Limit both directions before the swing starts so a shallow hit stays planted.
                float mainClearance = turnSide > 0f ? recoilClearance.x : recoilClearance.y;
                float reverseClearance = turnSide > 0f ? recoilClearance.y : recoilClearance.x;
                float limit = Mathf.Max(0f, Mathf.Min(mainClearance, reverseClearance * 4f));
                counterRecoilPeak = turnSide * Mathf.Min(swing * 1.12f, limit);
            }
            pose.Begin(projectile, reference, contact, turnSide * swing, localTip, now, normal);
        }

        public Vector3 Contact(Transform reference, float depth) => pose.Contact(reference, depth);

        public Vector2 TailSide(Transform reference)
        {
            var up = reference ? reference.TransformVector(incomingUp) : incomingUp;
            return ((Vector2)up).normalized * -turnSide;
        }

        public bool Apply(Transform projectile, SpriteRenderer renderer, Transform reference, float now)
        {
            if (forwardOnly)
            {
                float elapsed = Mathf.Max(0f, now - impactStart - EntryDuration);
                // A seated shaft should build angular speed from rest as it begins to rebound.
                if (reboundOnset > 0f)
                    elapsed -= reboundOnset * (1f - Mathf.Exp(-elapsed / reboundOnset));
                springAge = Mathf.Max(0f, elapsed) * reboundRate;
                now = impactStart + springAge;
            }
            else if (handoffTime > 0f)
            {
                float elapsed = Mathf.Max(0f, now - impactStart);
                float onset = handoffTime * .36f;
                // Start the spring at zero speed so the short spin carry owns the first instant.
                float springTime = elapsed - onset * (1f - Mathf.Exp(-elapsed / onset));
                springAge = springTime * reboundRate;
                now = impactStart + springAge;
            }
            return pose.Apply(projectile, renderer, reference, now);
        }

        public void ApplyPresentation(Transform projectile, SpriteRenderer renderer, Transform reference, float now)
        {
            if (!forwardOnly && handoffTime <= 0f) return;
            float elapsed = Mathf.Max(0f, now - impactStart);
            var target = renderer.transform;
            if (forwardOnly)
            {
                float duration = EntryDuration;
                if (elapsed < duration)
                {
                    float portion = 0f;
                    if (elapsed < handoffTime)
                    {
                        portion = 1f - elapsed * entrySlope;
                        if (elapsed >= coastTime)
                        {
                            float brakeTime = handoffTime - coastTime;
                            float left = (handoffTime - elapsed) / brakeTime;
                            portion = .5f * entrySlope * brakeTime * left * left;
                        }
                    }
                    var pathOffset = forwardEntry * Mathf.Clamp01(portion);
                    // A proven between-frame hit may have already moved with the body.
                    // Keep the first visible pose continuous, then transfer that motion.
                    if (sweepTransfer > 0f)
                        pathOffset += (entryOffset - forwardEntry) * (1f - Mathf.SmoothStep(0f, 1f, elapsed / sweepTransfer));
                    target.position += reference ? reference.TransformVector(pathOffset) : pathOffset;
                    return;
                }
                elapsed -= duration;
            }

            float progress = forwardOnly ? 1f : Mathf.Clamp01(elapsed / handoffTime);
            float remaining = 1f - progress;
            float angle = flightSpin * elapsed * remaining * remaining * remaining;
            angle += bladeTurn * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.016f, .125f, elapsed));

            // Ease the underlying Render rotation back toward the incoming attitude.
            if (springAge > .043f || counterRecoilWeight > 0f)
            {
                var start = reference ? reference.TransformVector(incomingRight) : incomingRight;
                float rootSwing = Vector2.SignedAngle(start, projectile.right);
                float relax = Mathf.SmoothStep(0f, .70f, Mathf.InverseLerp(.043f, .178f, springAge));
                angle -= rootSwing * relax;
                if (counterRecoilWeight > 0f)
                    angle = Mathf.Lerp(angle, EvaluateCounterRecoil() - rootSwing, counterRecoilWeight);
            }

            if (angle != 0f)
            {
                var tip = VisualTip(renderer, impactTip);
                target.rotation = Quaternion.AngleAxis(angle, Vector3.forward) * target.rotation;
                target.position += tip - VisualTip(renderer, impactTip);
            }

            if (remaining <= 0f) return;
            float lateralWeight = 1f - Mathf.SmoothStep(0f, 1f, progress);
            float carriedSpeed = -entrySlope * progress * remaining * remaining;
            var displacement = entryOffset * lateralWeight + forwardEntry * carriedSpeed;
            var offset = reference ? reference.TransformVector(displacement) : displacement;
            target.position += (Vector3)(Vector2)offset;
        }

        public static Vector2 GetRecoilClearance(Vector2 normal, Vector2 exposedShaft)
        {
            float shaftAngle = Vector2.SignedAngle(normal, exposedShaft);
            return new Vector2(89f - shaftAngle, 89f + shaftAngle);
        }

        private float EvaluateCounterRecoil()
        {
            return counterRecoilPeak * SurfaceAttachmentPose.SampleCounterRecoil(springAge / .102f);
        }

        public static Vector3 VisualTip(SpriteRenderer renderer, Vector2 tip) =>
            renderer.transform.TransformPoint(new Vector3(renderer.flipX ? -tip.x : tip.x,
                renderer.flipY ? -tip.y : tip.y));
    }

    public readonly struct SurfaceImpact
    {
        public readonly Vector2 Direction;
        public readonly float Speed, Spin, Swing, Rate, HeadOnSide;
        public readonly long Sequence;

        public SurfaceImpact(Vector2 direction, float speed, float spin, long sequence)
        {
            Direction = float.IsFinite(direction.x) && float.IsFinite(direction.y) && direction.sqrMagnitude > .0001f
                ? direction.normalized : Vector2.right;
            Speed = float.IsFinite(speed) ? Mathf.Max(0f, speed) : 0f;
            Spin = float.IsFinite(spin) ? spin : 0f;
            Sequence = sequence;

            // These samples only describe this impact. They never draw from gameplay randomness.
            ulong sample = unchecked((ulong)sequence * 0x9E3779B97F4A7C15UL);
            sample ^= sample >> 33;
            Swing = Mathf.Lerp(.78f, 1.22f, ((sample >> 48) & 255) / 255f);
            Rate = Mathf.Lerp(.85f, 1.15f, ((sample >> 32) & 255) / 255f);
            HeadOnSide = (sample >> 63) == 0 ? -1f : 1f;
        }
    }

}
