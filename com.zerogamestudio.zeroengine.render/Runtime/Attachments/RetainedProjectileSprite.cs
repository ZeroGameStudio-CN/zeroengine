using System;
using UnityEngine;

namespace ZeroEngine.Render.Attachments
{
    /// <summary>Visual lease for a resolved projectile contact on a moving 2D or 3D anchor.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RetainedProjectileSprite : MonoBehaviour
    {
        private SpriteRenderer visual;
        private Transform anchor;
        private Vector3 localContact, localAxis, localTip;
        private Quaternion localRest;
        private Color home;
        private float lifetime, fade, swing, halfCycle;
        private double started;
        private Func<bool> isContactCurrent;
        private Func<double> clock;
        private Action<RetainedProjectileSprite> returnToOwner;
        private bool playing;

        public bool IsPlaying => playing;

        public void Begin(Transform reference, Vector3 contact, Quaternion incomingRotation, Vector3 worldRotationAxis,
            Vector3 tip, float duration, float fadeSeconds, float peakDegrees, float firstHalfCycle,
            Func<bool> canFollow, Action<RetainedProjectileSprite> release, Func<double> time = null)
        {
            if (playing) throw new InvalidOperationException("End the current projectile visual lease first.");
            if (!reference) throw new ArgumentNullException(nameof(reference));
            if (!(duration > 0f) || !float.IsFinite(duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            if (!isActiveAndEnabled) throw new InvalidOperationException("Activate the visual before beginning its lease.");

            visual = GetComponent<SpriteRenderer>();
            home = visual.color;
            anchor = reference;
            localContact = reference.InverseTransformPoint(contact);
            localRest = Quaternion.Inverse(reference.rotation) * incomingRotation;
            localTip = tip;
            localAxis = reference.InverseTransformDirection(worldRotationAxis).normalized;
            lifetime = duration;
            fade = float.IsFinite(fadeSeconds) ? Mathf.Clamp(fadeSeconds, 0f, duration) : 0f;
            swing = float.IsFinite(peakDegrees) ? Mathf.Clamp(peakDegrees, -25f, 25f) : 0f;
            halfCycle = float.IsFinite(firstHalfCycle) ? Mathf.Max(.02f, firstHalfCycle) : .10f;
            isContactCurrent = canFollow;
            returnToOwner = release;
            clock = time;
            started = ReadClock();
            playing = true;
            ApplyPose(0f);
        }

        private double ReadClock() => clock != null ? clock() : Time.unscaledTimeAsDouble;

        private void LateUpdate() => Tick(ReadClock());

        public void Tick(double now)
        {
            if (!playing) return;
            if (!anchor || isContactCurrent?.Invoke() == false || !double.IsFinite(now))
            {
                End();
                return;
            }
            float age = (float)Math.Max(0d, now - started);
            if (age >= lifetime)
            {
                End();
                return;
            }
            ApplyPose(age);
        }

        private void ApplyPose(float age)
        {
            Vector3 point = anchor.TransformPoint(localContact);
            float angle = swing * SurfaceAttachmentPose.SampleCounterRecoil(age / halfCycle);
            var rotation = anchor.rotation * localRest;
            if (localAxis.sqrMagnitude > .0001f)
                rotation = Quaternion.AngleAxis(angle, anchor.TransformDirection(localAxis)) * rotation;
            transform.rotation = rotation;
            transform.position = point - transform.TransformVector(localTip);

            Color color = home;
            if (fade > 0f) color.a *= Mathf.Clamp01((lifetime - age) / fade);
            visual.color = color;
        }

        public void End()
        {
            if (!playing) return;
            playing = false;
            var callback = returnToOwner;
            returnToOwner = null;
            isContactCurrent = null;
            clock = null;
            anchor = null;
            if (visual) visual.color = home;
            callback?.Invoke(this);
        }

        private void OnDisable() => End();
    }
}
