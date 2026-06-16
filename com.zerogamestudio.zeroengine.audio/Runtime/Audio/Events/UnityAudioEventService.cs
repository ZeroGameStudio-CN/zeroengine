using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Audio
{
    public sealed class UnityAudioEventService : MonoBehaviour, IAudioEventService
    {
        [SerializeField] private AudioManager _audioManager;
        [SerializeField] private List<AudioCueBinding> _sfxEvents = new();
        [SerializeField] private List<AudioMusicBinding> _musicEvents = new();

        private readonly Dictionary<string, AudioCueSO> _sfxById = new();
        private readonly Dictionary<string, AudioMusicSO> _musicById = new();

        private void Awake()
        {
            if (_audioManager == null)
            {
                _audioManager = FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
            }

            RebuildCache();
        }

        private void OnValidate()
        {
            RebuildCache();
        }

        public void Play(AudioEventId eventId, Vector3 position = default)
        {
            if (_audioManager == null)
            {
                return;
            }

            var key = eventId.ToString();
            if (_sfxById.TryGetValue(key, out var cue))
            {
                _audioManager.PlaySFX(cue, position);
                return;
            }

            if (_musicById.TryGetValue(key, out var music))
            {
                _audioManager.PlayMusic(music);
            }
        }

        public void Stop(AudioEventId eventId)
        {
            if (_audioManager == null)
            {
                return;
            }

            if (_musicById.ContainsKey(eventId.ToString()))
            {
                _audioManager.StopMusic();
            }
        }

        public void SetParameter(AudioParameterId parameterId, float value)
        {
            // The default Unity audio backend has no generic parameter bus.
        }

        public void SetState(AudioParameterId stateGroupId, AudioParameterId stateId)
        {
            // The default Unity audio backend has no generic state bus.
        }

        private void RebuildCache()
        {
            _sfxById.Clear();
            _musicById.Clear();

            foreach (var binding in _sfxEvents)
            {
                if (!string.IsNullOrWhiteSpace(binding.EventId) && binding.Cue != null)
                {
                    _sfxById[binding.EventId.Trim()] = binding.Cue;
                }
            }

            foreach (var binding in _musicEvents)
            {
                if (!string.IsNullOrWhiteSpace(binding.EventId) && binding.Music != null)
                {
                    _musicById[binding.EventId.Trim()] = binding.Music;
                }
            }
        }

        [System.Serializable]
        private sealed class AudioCueBinding
        {
            public string EventId;
            public AudioCueSO Cue;
        }

        [System.Serializable]
        private sealed class AudioMusicBinding
        {
            public string EventId;
            public AudioMusicSO Music;
        }
    }
}
