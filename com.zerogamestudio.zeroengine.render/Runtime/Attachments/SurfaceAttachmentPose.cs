using UnityEngine;

namespace ZeroEngine.Render.Attachments
{
    /// <summary>Caller-clocked elastic attachment in the XY plane; owns no collision or lifetime rules.</summary>
    public struct SurfaceAttachmentPose
    {
        private const float SettleSeconds = 0.23f;
        private const float AngularRate = 4f * Mathf.PI;
        private const float Damping = 4.6f;
        private const float TaperStart = 0.72f;
        private Vector3 point, initialRight, initialUp, restingRight;
        private Vector2 tip;
        private float startedAt, turn, kick;

        public bool Active { get; private set; }
        public Vector3 LastContact { get; private set; }

        /// <summary>
        /// Evaluates a normalized first swing and a quicker, smaller opposite swing.
        /// The caller supplies phase in units of the first half-cycle and scales the result by its peak angle.
        /// </summary>
        public static float SampleCounterRecoil(float phase)
        {
            if (!(phase > 0f)) return 0f;

            // Increase the phase speed smoothly after crossing the resting direction.
            float afterCenter = Mathf.Max(0f, phase - 1f);
            phase += .44f * afterCenter * afterCenter;
            if (phase >= 2f) return 0f;

            float taper = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1.72f, 2f, phase));
            float wave = Mathf.Sin(Mathf.PI * phase) * Mathf.Exp(-1.45f * phase) / .537f;
            return wave * taper;
        }

        private void LimitSurfaceRebound(Vector2 right, Vector2 normal)
        {
            if (turn == 0f || normal.sqrMagnitude < 0.0001f) return;

            // Bound by the first positive and negative extrema, so grazing hits never
            // clip the curve at the surface and hold the shaft there mid-rebound.
            float rest = Mathf.Abs(turn);
            float phase = Mathf.Atan2(rest, Mathf.Abs(kick));
            float peakPhase = Mathf.Atan(AngularRate / Damping);
            float peak = Mathf.Sqrt(turn * turn + kick * kick) * Mathf.Sin(peakPhase) *
                Mathf.Exp(-Damping * (phase + peakPhase) / AngularRate);
            float opposite = peak * Mathf.Exp(-Damping * Mathf.PI / AngularRate) - rest;
            float incidence = Vector2.SignedAngle(-normal, right) * Mathf.Sign(turn);
            float scale = Mathf.Clamp01((90f - incidence) / (rest + peak));
            if (opposite > 0f) scale = Mathf.Min(scale, Mathf.Clamp01((90f + incidence) / opposite));
            turn *= scale;
            kick *= scale;
        }

        public void Begin(Transform projectile, Transform reference, Vector3 contact, float degrees,
            Vector2 localTip, float now, Vector2 surfaceNormal = default)
        {
            point = reference ? reference.InverseTransformPoint(contact) : contact;
            initialRight = reference ? reference.InverseTransformVector(projectile.right) : projectile.right;
            initialUp = reference ? reference.InverseTransformVector(projectile.up) : projectile.up;
            tip = localTip;
            turn = degrees;
            // Even a nearly straight landing can flex once. A configured zero turn stays straight.
            kick = turn == 0f ? 0f : Mathf.Sign(turn) * Mathf.Min(5f + 0.2f * Mathf.Abs(turn), 8f);
            LimitSurfaceRebound(projectile.right, surfaceNormal);
            restingRight = Direction(turn);
            startedAt = now;
            Active = true;
        }

        public Vector3 Contact(Transform reference, float depth)
        {
            var contact = reference ? reference.TransformPoint(point) : point;
            contact.z = depth;
            return contact;
        }

        // Static contacts can stop updating after this returns true; an attached target still moves.
        public bool Apply(Transform projectile, SpriteRenderer renderer, Transform reference, float now)
        {
            float progress = Mathf.Max(0f, now - startedAt) / SettleSeconds;
            bool settled = turn == 0f || progress >= 1f;
            Vector3 right = restingRight;
            if (!settled)
            {
                // Analytic damped spring: impact starts at the incoming angle, then
                // alternates around the rest angle with a progressively smaller excursion.
                float phase = AngularRate * progress;
                float decay = Mathf.Exp(-Damping * progress);
                float taper = Mathf.SmoothStep(1f, 0f, (progress - TaperStart) / (1f - TaperStart));
                right = Direction(turn + (kick * Mathf.Sin(phase) - turn * Mathf.Cos(phase)) * decay * taper);
            }

            var position = Contact(reference, projectile.position.z);
            LastContact = position;
            if (reference) right = reference.TransformVector(right);
            projectile.rotation = Quaternion.AngleAxis(Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg, Vector3.forward);
            if (tip != Vector2.zero && renderer)
            {
                var visualTip = new Vector3(renderer.flipX ? -tip.x : tip.x, renderer.flipY ? -tip.y : tip.y, 0f);
                var offset = renderer.transform.TransformPoint(visualTip) - projectile.position;
                position.x -= offset.x;
                position.y -= offset.y;
            }
            projectile.position = position;
            return settled;
        }

        private Vector3 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return initialRight * Mathf.Cos(radians) + initialUp * Mathf.Sin(radians);
        }
    }
}
