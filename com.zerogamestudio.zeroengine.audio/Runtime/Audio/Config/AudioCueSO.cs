using UnityEngine;
using UnityEngine.Audio;

namespace ZeroEngine.Audio
{
    /// <summary>
    /// Rich definition for a Sound Effect (SFX).
    /// Supports randomization, spatial settings, and cooldowns.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAudioCue", menuName = "ZeroEngine/Audio/Audio Cue")]
    public class AudioCueSO : ScriptableObject
    {
        [Header("Clips")]
        [Tooltip("Randomly selects one clip from this array to play.")]
        public AudioClip[] Clips;

        [Header("Settings")]
        [Tooltip("Audio Mixer Group to route this sound to.")]
        public AudioMixerGroup Group;

        [Tooltip("Loop the sound?")]
        public bool Loop = false;

        [Range(0f, 1f)]
        [Tooltip("0 = 2D (UI/BGM), 1 = 3D (World).")]
        public float SpatialBlend = 1f;

        [Header("Randomization")]
        [Tooltip("Random volume range.")]
        public Vector2 VolumeRange = new Vector2(0.9f, 1.0f);

        [Tooltip("Random pitch range.")]
        public Vector2 PitchRange = new Vector2(0.9f, 1.1f);

        [Header("Spam Protection")]
        [Tooltip("Minimum time (seconds) before this cue can be played again.")]
        public float Cooldown = 0.1f;

        [Min(0)]
        [Tooltip("Maximum simultaneous instances. Zero means unlimited.")]
        public int MaxInstances;

        [Range(0, 256)]
        [Tooltip("Unity AudioSource priority. Lower values are more important.")]
        public int Priority = 128;
        
        // --- Helper Methods ---

        public bool HasPlayableClip()
        {
            if (Clips == null)
            {
                return false;
            }

            foreach (AudioClip clip in Clips)
            {
                if (clip != null)
                {
                    return true;
                }
            }

            return false;
        }
        
        public AudioClip GetRandomClip()
        {
            if (Clips == null || Clips.Length == 0) return null;

            int start = Random.Range(0, Clips.Length);
            for (int offset = 0; offset < Clips.Length; offset++)
            {
                AudioClip clip = Clips[(start + offset) % Clips.Length];
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        public float GetRandomVolume()
        {
            return Random.Range(
                Mathf.Min(VolumeRange.x, VolumeRange.y),
                Mathf.Max(VolumeRange.x, VolumeRange.y));
        }

        public float GetRandomPitch()
        {
            return Random.Range(
                Mathf.Min(PitchRange.x, PitchRange.y),
                Mathf.Max(PitchRange.x, PitchRange.y));
        }

        private void OnValidate()
        {
            VolumeRange = new Vector2(
                Mathf.Clamp01(VolumeRange.x),
                Mathf.Clamp01(VolumeRange.y));
            PitchRange = new Vector2(
                Mathf.Clamp(PitchRange.x, 0.01f, 3f),
                Mathf.Clamp(PitchRange.y, 0.01f, 3f));
            Cooldown = Mathf.Max(0f, Cooldown);
            MaxInstances = Mathf.Max(0, MaxInstances);
        }
    }
}
