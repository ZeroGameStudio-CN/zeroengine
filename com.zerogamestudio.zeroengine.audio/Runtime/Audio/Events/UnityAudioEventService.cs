using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Audio
{
    /// <summary>
    /// Stable event-id facade over the Unity AudioManager backend.
    /// Audio banks can be registered and unregistered with a content scope.
    /// </summary>
    public sealed class UnityAudioEventService : MonoBehaviour, IAudioEventService
    {
        [SerializeField] private AudioManager _audioManager;
        [SerializeField] private List<AudioBankSO> _banks = new();
        [SerializeField] private List<AudioCueBinding> _sfxEvents = new();
        [SerializeField] private List<AudioMusicBinding> _musicEvents = new();

        private readonly Dictionary<string, AudioCueSO> _sfxById = new();
        private readonly Dictionary<string, AudioMusicSO> _musicById = new();

        private void Awake()
        {
            ResolveAudioManager();
            RebuildCacheWithLogging();
        }

        private void OnValidate()
        {
            RebuildCacheWithLogging();
        }

        public void Configure(AudioManager audioManager, IEnumerable<AudioBankSO> banks = null)
        {
            _audioManager = audioManager;
            _banks = banks == null ? new List<AudioBankSO>() : new List<AudioBankSO>(banks);
            RebuildCacheWithLogging();
        }

        public bool RegisterBank(AudioBankSO bank, out string error)
        {
            error = null;
            if (bank == null)
            {
                error = "Audio bank is null.";
                return false;
            }

            if (_banks.Contains(bank))
            {
                return true;
            }

            _banks.Add(bank);
            if (TryRebuildCache(out error))
            {
                return true;
            }

            _banks.Remove(bank);
            TryRebuildCache(out _);
            return false;
        }

        public bool UnregisterBank(AudioBankSO bank)
        {
            if (bank == null || !_banks.Remove(bank))
            {
                return false;
            }

            TryRebuildCache(out _);
            return true;
        }

        public bool HasEvent(AudioEventId eventId)
        {
            string key = eventId.ToString();
            return _sfxById.ContainsKey(key) || _musicById.ContainsKey(key);
        }

        public void Play(AudioEventId eventId, Vector3 position = default)
        {
            ResolveAudioManager();
            if (_audioManager == null)
            {
                return;
            }

            string key = eventId.ToString();
            if (_sfxById.TryGetValue(key, out AudioCueSO cue))
            {
                _audioManager.PlaySFX(cue, position);
                return;
            }

            if (_musicById.TryGetValue(key, out AudioMusicSO music))
            {
                _audioManager.PlayMusic(music);
            }
        }

        public void Stop(AudioEventId eventId)
        {
            ResolveAudioManager();
            if (_audioManager == null)
            {
                return;
            }

            string key = eventId.ToString();
            if (_sfxById.TryGetValue(key, out AudioCueSO cue))
            {
                _audioManager.StopSFX(cue);
            }

            if (_musicById.ContainsKey(key))
            {
                _audioManager.StopMusic();
            }
        }

        public void SetParameter(AudioParameterId parameterId, float value)
        {
            // The default Unity backend intentionally exposes concrete volume methods
            // instead of guessing generic middleware parameter semantics.
        }

        public void SetState(AudioParameterId stateGroupId, AudioParameterId stateId)
        {
            // Mixer snapshot/state mapping is project-specific.
        }

        private void ResolveAudioManager()
        {
            if (_audioManager == null)
            {
                _audioManager = FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
            }
        }

        private void RebuildCacheWithLogging()
        {
            if (!TryRebuildCache(out string error) && !string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[UnityAudioEventService] {error}", this);
            }
        }

        private bool TryRebuildCache(out string error)
        {
            _sfxById.Clear();
            _musicById.Clear();
            error = null;

            foreach (AudioCueBinding binding in _sfxEvents)
            {
                if (!TryAddCue(binding?.EventId, binding?.Cue, out error))
                {
                    return false;
                }
            }

            foreach (AudioMusicBinding binding in _musicEvents)
            {
                if (!TryAddMusic(binding?.EventId, binding?.Music, out error))
                {
                    return false;
                }
            }

            foreach (AudioBankSO bank in _banks)
            {
                if (bank == null)
                {
                    continue;
                }

                foreach (AudioCueEventBinding binding in bank.SfxEvents)
                {
                    if (!TryAddCue(binding?.EventId, binding?.Cue, out error))
                    {
                        return false;
                    }
                }

                foreach (AudioMusicEventBinding binding in bank.MusicEvents)
                {
                    if (!TryAddMusic(binding?.EventId, binding?.Music, out error))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryAddCue(string eventId, AudioCueSO cue, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(eventId) || cue == null)
            {
                return true;
            }

            string key = eventId.Trim();
            if (_sfxById.ContainsKey(key) || _musicById.ContainsKey(key))
            {
                error = $"Duplicate audio event id '{key}'.";
                return false;
            }

            _sfxById.Add(key, cue);
            return true;
        }

        private bool TryAddMusic(string eventId, AudioMusicSO music, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(eventId) || music == null)
            {
                return true;
            }

            string key = eventId.Trim();
            if (_sfxById.ContainsKey(key) || _musicById.ContainsKey(key))
            {
                error = $"Duplicate audio event id '{key}'.";
                return false;
            }

            _musicById.Add(key, music);
            return true;
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
