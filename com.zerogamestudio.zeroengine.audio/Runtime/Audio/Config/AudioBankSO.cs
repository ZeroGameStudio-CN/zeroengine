using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Audio
{
    [Serializable]
    public sealed class AudioCueEventBinding
    {
        [SerializeField] private string _eventId;
        [SerializeField] private AudioCueSO _cue;

        public AudioCueEventBinding(string eventId, AudioCueSO cue)
        {
            _eventId = eventId;
            _cue = cue;
        }

        public string EventId => _eventId;
        public AudioCueSO Cue => _cue;
    }

    [Serializable]
    public sealed class AudioMusicEventBinding
    {
        [SerializeField] private string _eventId;
        [SerializeField] private AudioMusicSO _music;

        public AudioMusicEventBinding(string eventId, AudioMusicSO music)
        {
            _eventId = eventId;
            _music = music;
        }

        public string EventId => _eventId;
        public AudioMusicSO Music => _music;
    }

    /// <summary>
    /// Addressable-friendly collection of stable audio event ids and Unity audio assets.
    /// Load and register a bank as one content-lifecycle unit.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAudioBank", menuName = "ZeroEngine/Audio/Audio Bank")]
    public sealed class AudioBankSO : ScriptableObject
    {
        [SerializeField] private List<AudioCueEventBinding> _sfxEvents = new();
        [SerializeField] private List<AudioMusicEventBinding> _musicEvents = new();

        public IReadOnlyList<AudioCueEventBinding> SfxEvents => _sfxEvents;
        public IReadOnlyList<AudioMusicEventBinding> MusicEvents => _musicEvents;

        public void CollectValidationErrors(ICollection<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateBindings(_sfxEvents, ids, errors);
            ValidateBindings(_musicEvents, ids, errors);
        }

        public void ConfigureForEditor(
            IEnumerable<AudioCueEventBinding> sfxEvents,
            IEnumerable<AudioMusicEventBinding> musicEvents)
        {
            _sfxEvents = sfxEvents == null
                ? new List<AudioCueEventBinding>()
                : new List<AudioCueEventBinding>(sfxEvents);
            _musicEvents = musicEvents == null
                ? new List<AudioMusicEventBinding>()
                : new List<AudioMusicEventBinding>(musicEvents);
        }

        private static void ValidateBindings(
            IEnumerable<AudioCueEventBinding> bindings,
            ISet<string> ids,
            ICollection<string> errors)
        {
            foreach (AudioCueEventBinding binding in bindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.EventId))
                {
                    errors.Add("Audio bank contains an SFX binding with an empty event id.");
                    continue;
                }

                string id = binding.EventId.Trim();
                if (!ids.Add(id))
                {
                    errors.Add($"Audio bank contains duplicate event id '{id}'.");
                }

                if (binding.Cue == null)
                {
                    errors.Add($"Audio bank event '{id}' has no AudioCueSO.");
                    continue;
                }

                ValidateCue(id, binding.Cue, errors);
            }
        }

        private static void ValidateBindings(
            IEnumerable<AudioMusicEventBinding> bindings,
            ISet<string> ids,
            ICollection<string> errors)
        {
            foreach (AudioMusicEventBinding binding in bindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.EventId))
                {
                    errors.Add("Audio bank contains a music binding with an empty event id.");
                    continue;
                }

                string id = binding.EventId.Trim();
                if (!ids.Add(id))
                {
                    errors.Add($"Audio bank contains duplicate event id '{id}'.");
                }

                if (binding.Music == null)
                {
                    errors.Add($"Audio bank event '{id}' has no AudioMusicSO.");
                    continue;
                }

                ValidateMusic(id, binding.Music, errors);
            }
        }

        private static void ValidateCue(
            string id,
            AudioCueSO cue,
            ICollection<string> errors)
        {
            if (!cue.HasPlayableClip())
            {
                errors.Add($"Audio bank event '{id}' has no playable AudioClip.");
            }

            if (cue.VolumeRange.x < 0f
                || cue.VolumeRange.y > 1f
                || cue.VolumeRange.x > cue.VolumeRange.y)
            {
                errors.Add($"Audio bank event '{id}' has an invalid volume range.");
            }

            if (cue.PitchRange.x <= 0f
                || cue.PitchRange.y <= 0f
                || cue.PitchRange.x > cue.PitchRange.y)
            {
                errors.Add($"Audio bank event '{id}' has an invalid pitch range.");
            }

            if (cue.SpatialBlend < 0f || cue.SpatialBlend > 1f)
            {
                errors.Add($"Audio bank event '{id}' has an invalid spatial blend.");
            }

            if (cue.PanStereo < -1f
                || cue.PanStereo > 1f
                || cue.MinDistance < 0f
                || cue.MaxDistance < cue.MinDistance
                || cue.DopplerLevel < 0f
                || cue.DopplerLevel > 5f
                || cue.Spread < 0f
                || cue.Spread > 360f
                || cue.ReverbZoneMix < 0f
                || cue.ReverbZoneMix > 1.1f)
            {
                errors.Add($"Audio bank event '{id}' has invalid spatial audio settings.");
            }

            if (cue.Cooldown < 0f || cue.MaxInstances < 0 || cue.Priority < 0 || cue.Priority > 256)
            {
                errors.Add($"Audio bank event '{id}' has invalid playback limits.");
            }
        }

        private static void ValidateMusic(
            string id,
            AudioMusicSO music,
            ICollection<string> errors)
        {
            if (music.IntroClip == null && music.LoopClip == null)
            {
                errors.Add($"Audio bank event '{id}' has no playable music clip.");
            }

            if (music.Volume < 0f || music.Volume > 1f)
            {
                errors.Add($"Audio bank event '{id}' has an invalid music volume.");
            }

            if (music.TransitionDuration < -1f || music.FadeOutDuration < -1f)
            {
                errors.Add($"Audio bank event '{id}' has invalid music transition settings.");
            }
        }
    }
}
