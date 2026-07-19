using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Audio
{
    /// <summary>
    /// Pooled Unity audio backend with music, SFX and AudioMixer volume control.
    /// Existing PlaySFX and PlayMusic entry points remain supported.
    /// </summary>
    public class AudioManager : Singleton<AudioManager>
    {
        public const string MasterVolumeParameter = "MasterVolume";
        public const string MusicVolumeParameter = "BGMVolume";
        public const string SfxVolumeParameter = "SFXVolume";

        [Header("Mixer Settings")]
        [SerializeField] private AudioMixer _audioMixer;
        [SerializeField] private AudioMixerGroup _bgmGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;

        [Header("BGM")]
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _secondaryBgmSource;
        [SerializeField] private float _crossFadeTime = 1f;
        [SerializeField] private float _musicFadeOutTime = 1f;

        [Header("SFX Pooling")]
        [SerializeField, Min(1)] private int _initialPoolSize = 10;
        [SerializeField, Min(1)] private int _maximumPoolSize = 32;

        [Header("Persistence")]
        [SerializeField] private bool _persistVolumeWithSaveManager = true;

        private readonly Queue<AudioEmitter> _sfxPool = new();
        private readonly List<AudioEmitter> _activeEmitters = new();
        private readonly List<AudioEmitter> _emitterScratch = new();
        private readonly Dictionary<AudioCueSO, float> _cueCooldowns = new();
        private readonly List<AudioCueSO> _cooldownKeys = new();
        private readonly AudioSource[] _musicSources = new AudioSource[2];

        private GameObject _poolRoot;
        private int _createdEmitterCount;
        private int _activeMusicSourceIndex;
        private Coroutine _bgmRoutine;
        private AudioMusicSO _currentMusic;
        private float _masterVolume = 1f;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;

        public AudioMusicSO CurrentMusic => _currentMusic;
        public int ActiveEmitterCount => _activeEmitters.Count;
        public float CrossFadeTime => _crossFadeTime;
        public float MusicFadeOutTime => _musicFadeOutTime;
        public int InitialPoolSize => _initialPoolSize;
        public int MaximumPoolSize => _maximumPoolSize;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                return;
            }

            InitializePool();
            InitializeBGM();
            InitializeVolume();
        }

        private void Update()
        {
            if (_cueCooldowns.Count == 0)
            {
                return;
            }

            _cooldownKeys.Clear();
            _cooldownKeys.AddRange(_cueCooldowns.Keys);
            foreach (AudioCueSO cue in _cooldownKeys)
            {
                float remaining = _cueCooldowns[cue] - Time.unscaledDeltaTime;
                if (remaining <= 0f)
                {
                    _cueCooldowns.Remove(cue);
                }
                else
                {
                    _cueCooldowns[cue] = remaining;
                }
            }
        }

        public void Configure(
            AudioMixer audioMixer,
            AudioMixerGroup musicGroup,
            AudioMixerGroup sfxGroup,
            bool persistVolumeWithSaveManager)
        {
            _audioMixer = audioMixer;
            _bgmGroup = musicGroup;
            _sfxGroup = sfxGroup;
            _persistVolumeWithSaveManager = persistVolumeWithSaveManager;

            InitializeBGM();
            foreach (AudioSource source in _musicSources)
            {
                if (source != null)
                {
                    source.outputAudioMixerGroup = _bgmGroup;
                }
            }

            foreach (AudioEmitter emitter in _sfxPool)
            {
                emitter.Source.outputAudioMixerGroup = _sfxGroup;
            }

            foreach (AudioEmitter emitter in _activeEmitters)
            {
                if (emitter.CurrentCue == null || emitter.CurrentCue.Group == null)
                {
                    emitter.Source.outputAudioMixerGroup = _sfxGroup;
                }
            }

            ApplyMixerVolumes();
        }

        public void SetVolumePersistenceEnabled(bool enabled)
        {
            _persistVolumeWithSaveManager = enabled;
        }

        /// <summary>
        /// Configures runtime playback policy independently from mixer routing.
        /// Call before activation when pool sizes must apply to initial allocation.
        /// </summary>
        public void ConfigurePlayback(
            float crossFadeTime,
            float musicFadeOutTime,
            int initialPoolSize,
            int maximumPoolSize)
        {
            _crossFadeTime = Mathf.Max(0f, crossFadeTime);
            _musicFadeOutTime = Mathf.Max(0f, musicFadeOutTime);
            _initialPoolSize = Mathf.Max(1, initialPoolSize);
            _maximumPoolSize = Mathf.Max(
                Mathf.Max(_initialPoolSize, maximumPoolSize),
                _createdEmitterCount);

            if (_poolRoot != null)
            {
                while (_createdEmitterCount < _initialPoolSize)
                {
                    CreateNewEmitter();
                }
            }
        }

        #region SFX

        private void InitializePool()
        {
            if (_poolRoot != null)
            {
                return;
            }

            _maximumPoolSize = Mathf.Max(_initialPoolSize, _maximumPoolSize);
            _poolRoot = new GameObject("SFX_Pool");
            _poolRoot.transform.SetParent(transform, false);

            for (int index = 0; index < _initialPoolSize; index++)
            {
                CreateNewEmitter();
            }
        }

        private AudioEmitter CreateNewEmitter()
        {
            if (_createdEmitterCount >= _maximumPoolSize)
            {
                return null;
            }

            var emitterObject = new GameObject($"SFX_Emitter_{_createdEmitterCount}");
            _createdEmitterCount++;
            emitterObject.transform.SetParent(_poolRoot.transform, false);

            AudioSource source = emitterObject.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = _sfxGroup;
            source.playOnAwake = false;

            AudioEmitter emitter = emitterObject.AddComponent<AudioEmitter>();
            emitter.Initialize(OnEmitterFinished);
            emitterObject.SetActive(false);
            _sfxPool.Enqueue(emitter);
            return emitter;
        }

        private AudioEmitter AcquireEmitter()
        {
            if (_sfxPool.Count == 0)
            {
                CreateNewEmitter();
            }

            return _sfxPool.Count > 0 ? _sfxPool.Dequeue() : null;
        }

        private void OnEmitterFinished(AudioEmitter emitter)
        {
            if (emitter == null || !_activeEmitters.Remove(emitter))
            {
                return;
            }

            emitter.OnDespawn();
            emitter.gameObject.SetActive(false);
            emitter.transform.SetParent(_poolRoot.transform, false);
            _sfxPool.Enqueue(emitter);
        }

        public void PlaySFX(AudioCueSO cue, Vector3 position = default)
        {
            TryPlaySFX(cue, position);
        }

        public bool TryPlaySFX(AudioCueSO cue, Vector3 position = default)
        {
            if (cue == null || !cue.HasPlayableClip())
            {
                return false;
            }

            if (cue.Cooldown > 0f && _cueCooldowns.ContainsKey(cue))
            {
                return false;
            }

            if (cue.MaxInstances > 0)
            {
                int instanceCount = 0;
                foreach (AudioEmitter active in _activeEmitters)
                {
                    if (active.CurrentCue == cue)
                    {
                        instanceCount++;
                    }
                }

                if (instanceCount >= cue.MaxInstances)
                {
                    return false;
                }
            }

            AudioEmitter emitter = AcquireEmitter();
            if (emitter == null)
            {
                return false;
            }

            if (cue.Cooldown > 0f)
            {
                _cueCooldowns[cue] = cue.Cooldown;
            }

            emitter.OnSpawn();
            emitter.transform.position = position == default ? transform.position : position;
            emitter.gameObject.SetActive(true);
            _activeEmitters.Add(emitter);

            if (cue.Group == null)
            {
                emitter.Source.outputAudioMixerGroup = _sfxGroup;
            }

            emitter.Play(cue);
            return true;
        }

        public void StopSFX(AudioCueSO cue)
        {
            if (cue == null)
            {
                return;
            }

            _emitterScratch.Clear();
            foreach (AudioEmitter emitter in _activeEmitters)
            {
                if (emitter.CurrentCue == cue)
                {
                    _emitterScratch.Add(emitter);
                }
            }

            foreach (AudioEmitter emitter in _emitterScratch)
            {
                emitter.Stop();
            }
        }

        public void StopAllSFX()
        {
            _emitterScratch.Clear();
            _emitterScratch.AddRange(_activeEmitters);
            foreach (AudioEmitter emitter in _emitterScratch)
            {
                emitter.Stop();
            }
        }

        public void PlaySFX(AudioClip clip, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }

            AudioEmitter emitter = AcquireEmitter();
            if (emitter == null)
            {
                return;
            }

            emitter.OnSpawn();
            emitter.gameObject.SetActive(true);
            _activeEmitters.Add(emitter);

            emitter.Source.clip = clip;
            emitter.Source.volume = Mathf.Clamp01(volume);
            emitter.Source.pitch = 1f;
            emitter.Source.loop = false;
            emitter.Source.spatialBlend = 0f;
            emitter.Source.priority = 128;
            emitter.Source.outputAudioMixerGroup = _sfxGroup;
            emitter.Source.Play();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif
            StartCoroutine(ReturnEmitterDelayed(emitter, clip.length + 0.1f));
        }

        private IEnumerator ReturnEmitterDelayed(AudioEmitter emitter, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            OnEmitterFinished(emitter);
        }

        #endregion

        #region Music

        private void InitializeBGM()
        {
            if (_bgmSource == null)
            {
                _bgmSource = gameObject.AddComponent<AudioSource>();
            }

            if (_secondaryBgmSource == null)
            {
                _secondaryBgmSource = gameObject.AddComponent<AudioSource>();
            }

            _musicSources[0] = _bgmSource;
            _musicSources[1] = _secondaryBgmSource;
            foreach (AudioSource source in _musicSources)
            {
                source.outputAudioMixerGroup = _bgmGroup;
                source.loop = true;
                source.playOnAwake = false;
            }
        }

        public void PlayMusic(AudioMusicSO music, float fadeDuration = -1f)
        {
            if (music == null || (music.IntroClip == null && music.LoopClip == null))
            {
                return;
            }

            AudioSource activeSource = _musicSources[_activeMusicSourceIndex];
            if (_currentMusic == music && activeSource != null && activeSource.isPlaying)
            {
                return;
            }

            float duration = fadeDuration >= 0f
                ? fadeDuration
                : music.TransitionDuration >= 0f
                    ? music.TransitionDuration
                    : _crossFadeTime;
            duration = Mathf.Max(0f, duration);
            _currentMusic = music;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                PlayMusicImmediate(music);
                return;
            }
#endif

            if (_bgmRoutine != null)
            {
                StopCoroutine(_bgmRoutine);
            }

            _bgmRoutine = StartCoroutine(TransitionMusicRoutine(music, duration));
        }

        public void StopMusic(float fadeDuration = -1f)
        {
            AudioMusicSO stoppingMusic = _currentMusic;
            _currentMusic = null;
            float duration = fadeDuration >= 0f
                ? fadeDuration
                : stoppingMusic != null && stoppingMusic.FadeOutDuration >= 0f
                    ? stoppingMusic.FadeOutDuration
                    : _musicFadeOutTime;
            duration = Mathf.Max(0f, duration);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                StopMusicSources();
                return;
            }
#endif

            if (_bgmRoutine != null)
            {
                StopCoroutine(_bgmRoutine);
            }

            if (duration <= 0f || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                StopMusicSources();
                _bgmRoutine = null;
                return;
            }

            _bgmRoutine = StartCoroutine(FadeOutAllMusic(duration));
        }

        private IEnumerator TransitionMusicRoutine(AudioMusicSO music, float duration)
        {
            int previousIndex = _activeMusicSourceIndex;
            int nextIndex = 1 - previousIndex;
            AudioSource previous = _musicSources[previousIndex];
            AudioSource next = _musicSources[nextIndex];

            next.Stop();
            next.clip = music.IntroClip != null ? music.IntroClip : music.LoopClip;
            next.loop = music.IntroClip == null;
            next.volume = 0f;
            next.Play();
            _activeMusicSourceIndex = nextIndex;

            float previousStartVolume = previous.isPlaying ? previous.volume : 0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                previous.volume = Mathf.Lerp(previousStartVolume, 0f, progress);
                next.volume = Mathf.Lerp(0f, music.Volume, progress);
                yield return null;
            }

            previous.Stop();
            previous.clip = null;
            next.volume = music.Volume;

            if (music.IntroClip != null && music.LoopClip != null)
            {
                while (_currentMusic == music && next.isPlaying && next.clip == music.IntroClip)
                {
                    yield return null;
                }

                if (_currentMusic == music)
                {
                    next.clip = music.LoopClip;
                    next.loop = true;
                    next.Play();
                }
            }

            _bgmRoutine = null;
        }

        private IEnumerator FadeOutAllMusic(float duration)
        {
            float firstVolume = _musicSources[0].volume;
            float secondVolume = _musicSources[1].volume;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                _musicSources[0].volume = Mathf.Lerp(firstVolume, 0f, progress);
                _musicSources[1].volume = Mathf.Lerp(secondVolume, 0f, progress);
                yield return null;
            }

            StopMusicSources();
            _bgmRoutine = null;
        }

        private void PlayMusicImmediate(AudioMusicSO music)
        {
            StopMusicSources();
            AudioSource source = _musicSources[0];
            source.clip = music.LoopClip != null ? music.LoopClip : music.IntroClip;
            source.loop = music.LoopClip != null;
            source.volume = music.Volume;
            source.Play();
            _activeMusicSourceIndex = 0;
        }

        private void StopMusicSources()
        {
            foreach (AudioSource source in _musicSources)
            {
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                source.clip = null;
                source.volume = 0f;
            }
        }

        #endregion

        #region Volume

        private void InitializeVolume()
        {
            if (_persistVolumeWithSaveManager)
            {
                _masterVolume = SaveManager.Instance.Load(
                    MasterVolumeParameter,
                    1f,
                    SaveManager.SettingsFile);
                _musicVolume = SaveManager.Instance.Load(
                    MusicVolumeParameter,
                    1f,
                    SaveManager.SettingsFile);
                _sfxVolume = SaveManager.Instance.Load(
                    SfxVolumeParameter,
                    1f,
                    SaveManager.SettingsFile);
            }

            ApplyMixerVolumes();
        }

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            SetMixerVolume(MasterVolumeParameter, _masterVolume);
            SaveVolumeIfEnabled(MasterVolumeParameter, _masterVolume);
        }

        public void SetBGMVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            SetMixerVolume(MusicVolumeParameter, _musicVolume);
            SaveVolumeIfEnabled(MusicVolumeParameter, _musicVolume);
        }

        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            SetMixerVolume(SfxVolumeParameter, _sfxVolume);
            SaveVolumeIfEnabled(SfxVolumeParameter, _sfxVolume);
        }

        public float GetMasterVolume() => _masterVolume;
        public float GetBGMVolume() => _musicVolume;
        public float GetSFXVolume() => _sfxVolume;

        public static float NormalizedToDecibels(float normalizedVolume)
        {
            float clamped = Mathf.Clamp01(normalizedVolume);
            return clamped <= 0.001f ? -80f : Mathf.Log10(clamped) * 20f;
        }

        private void ApplyMixerVolumes()
        {
            SetMixerVolume(MasterVolumeParameter, _masterVolume);
            SetMixerVolume(MusicVolumeParameter, _musicVolume);
            SetMixerVolume(SfxVolumeParameter, _sfxVolume);
        }

        private void SaveVolumeIfEnabled(string parameter, float volume)
        {
            if (_persistVolumeWithSaveManager)
            {
                SaveManager.Instance.Save(parameter, volume, SaveManager.SettingsFile);
            }
        }

        private void SetMixerVolume(string parameter, float volume)
        {
            if (_audioMixer == null)
            {
                return;
            }

            _audioMixer.SetFloat(parameter, NormalizedToDecibels(volume));
        }

        #endregion
    }
}
