using System;
using UnityEngine;

namespace ZeroEngine.Cinematic
{
    public enum CinematicPerformanceCueKind
    {
        Actor = 0,
        Audio = 1
    }

    [Serializable]
    public sealed class CinematicPerformanceTimedCue
    {
        [SerializeField] private float _seconds;
        [SerializeField] private CinematicPerformanceCueKind _kind;
        [SerializeField] private string _id;
        public float Seconds => _seconds;
        public CinematicPerformanceCueKind Kind => _kind;
        public string Id => _id;
    }

    [Serializable]
    public sealed class CinematicPerformanceLightPulse
    {
        [SerializeField] private float _seconds;
        [SerializeField] private float _durationSeconds;
        [SerializeField] private float _sourceOffsetSeconds;
        public float Seconds => _seconds;
        public float DurationSeconds => _durationSeconds;
        public float SourceOffsetSeconds => _sourceOffsetSeconds;
    }

    /// <summary>Authored presentation time, independent of rule results and playback speed.</summary>
    [Serializable]
    public sealed class CinematicPerformanceTimingPattern
    {
        [SerializeField] private string _sourceSequence;
        [SerializeField] private float _durationSeconds;
        [SerializeField] private float[] _contacts = Array.Empty<float>();
        [SerializeField] private CinematicPerformanceTimedCue[] _cues = Array.Empty<CinematicPerformanceTimedCue>();
        [SerializeField] private CinematicPerformanceLightPulse[] _lightPulses = Array.Empty<CinematicPerformanceLightPulse>();
        public string SourceSequence => _sourceSequence;
        public float DurationSeconds => _durationSeconds;
        public int ContactCount => _contacts?.Length ?? 0;
        public int CueCount => _cues?.Length ?? 0;
        public float ContactAt(int index) => _contacts[index];
        public CinematicPerformanceTimedCue CueAt(int index) => _cues[index];
        public bool HasLightPulses => _lightPulses != null && _lightPulses.Length > 0;
        public float EvaluateLight(float elapsed, AnimationCurve envelope)
        {
            float value = 0f;
            foreach (var pulse in _lightPulses ?? Array.Empty<CinematicPerformanceLightPulse>())
                if (elapsed >= pulse.Seconds && elapsed < pulse.Seconds + pulse.DurationSeconds)
                    value = Mathf.Max(value, envelope.Evaluate(pulse.SourceOffsetSeconds + elapsed - pulse.Seconds));
            return Mathf.Clamp01(value);
        }
        public bool HasActorCues
        {
            get
            {
                foreach (var cue in _cues ?? Array.Empty<CinematicPerformanceTimedCue>())
                    if (cue.Kind == CinematicPerformanceCueKind.Actor) return true;
                return false;
            }
        }

        public void Validate()
        {
            if (!Finite(_durationSeconds) || _durationSeconds <= 0f || ContactCount == 0)
                throw new InvalidOperationException("A timing pattern requires a finite duration and contacts.");
            float previous = -1f;
            foreach (float time in _contacts)
            {
                if (!Finite(time) || time < 0f || time >= _durationSeconds || time <= previous)
                    throw new InvalidOperationException("Contact times must increase inside their pattern.");
                previous = time;
            }
            previous = -1f;
            foreach (var cue in _cues ?? Array.Empty<CinematicPerformanceTimedCue>())
            {
                if (cue == null || !Finite(cue.Seconds) || cue.Seconds < 0f || cue.Seconds > _durationSeconds
                    || cue.Seconds < previous || string.IsNullOrWhiteSpace(cue.Id)
                    || !Enum.IsDefined(typeof(CinematicPerformanceCueKind), cue.Kind))
                    throw new InvalidOperationException("Timed cues must be ordered, identified and inside their pattern.");
                previous = cue.Seconds;
            }
            foreach (var pulse in _lightPulses ?? Array.Empty<CinematicPerformanceLightPulse>())
                if (pulse == null || !Finite(pulse.Seconds) || !Finite(pulse.DurationSeconds)
                    || !Finite(pulse.SourceOffsetSeconds) || pulse.Seconds < 0f || pulse.DurationSeconds <= 0f
                    || pulse.SourceOffsetSeconds < 0f || pulse.Seconds + pulse.DurationSeconds > _durationSeconds + .0001f)
                    throw new InvalidOperationException("Light pulses must have a finite range inside their pattern.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
