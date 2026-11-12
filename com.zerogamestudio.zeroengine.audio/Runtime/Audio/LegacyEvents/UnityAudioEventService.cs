using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Audio.Events
{
    public sealed class UnityAudioEventService : MonoBehaviour, IAudioEventService
    {
        [Serializable]
        private sealed class AudioEventBinding
        {
            public string eventId;
            public AudioCueSO cue;
            public AudioMusicSO music;
        }

        [SerializeField] private List<AudioEventBinding> _bindings = new();

        public void Post(AudioEventId eventId, Vector3 position = default)
        {
            if (!eventId.IsValid || !TryGetManager(out var manager))
            {
                return;
            }

            var binding = FindBinding(eventId);
            if (binding?.music != null)
            {
                manager.PlayMusic(binding.music);
                return;
            }

            if (binding?.cue != null)
            {
                manager.PlaySFX(binding.cue, position);
            }
        }

        public void Post(AudioEventId eventId, GameObject target)
        {
            Post(eventId, target != null ? target.transform.position : default);
        }

        public void SetParameter(AudioParameterId parameterId, float value)
        {
            if (!parameterId.IsValid || !TryGetManager(out var manager))
            {
                return;
            }

            switch (parameterId.Value)
            {
                case "volume.master":
                case "MasterVolume":
                    manager.SetMasterVolume(value);
                    break;
                case "volume.bgm":
                case "BGMVolume":
                    manager.SetBGMVolume(value);
                    break;
                case "volume.sfx":
                case "SFXVolume":
                    manager.SetSFXVolume(value);
                    break;
            }
        }

        public void Stop(AudioEventId eventId)
        {
            if (eventId.IsValid && TryGetManager(out var manager))
            {
                manager.StopMusic();
            }
        }

        private AudioEventBinding FindBinding(AudioEventId eventId)
        {
            foreach (var binding in _bindings)
            {
                if (binding != null && string.Equals(binding.eventId, eventId.Value, StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            return null;
        }

        private static bool TryGetManager(out AudioManager manager)
        {
            manager = AudioManager.Instance;
            return manager != null;
        }
    }
}
