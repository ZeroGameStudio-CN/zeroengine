using UnityEngine;

namespace ZeroEngine.Audio
{
    /// <summary>
    /// Definition for a Music Track, supporting Intro + Loop structure.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAudioMusic", menuName = "ZeroEngine/Audio/Audio Music")]
    public class AudioMusicSO : ScriptableObject
    {
        [Tooltip("Played once at the beginning.")]
        public AudioClip IntroClip;

        [Tooltip("Looped indefinitely after Intro finishes (or immediately if Intro is null).")]
        public AudioClip LoopClip;

        [Range(0f, 1f)]
        public float Volume = 1f;

        [Header("Transitions")]
        [Min(-1f)]
        [Tooltip("Crossfade duration in seconds. -1 uses the AudioManager default.")]
        public float TransitionDuration = -1f;

        [Min(-1f)]
        [Tooltip("Fade-out duration in seconds when this track stops. -1 uses the AudioManager default.")]
        public float FadeOutDuration = -1f;

        private void OnValidate()
        {
            Volume = Mathf.Clamp01(Volume);
            TransitionDuration = Mathf.Max(-1f, TransitionDuration);
            FadeOutDuration = Mathf.Max(-1f, FadeOutDuration);
        }
    }
}
