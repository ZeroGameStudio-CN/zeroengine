using System;
using System.Collections;
using UnityEngine;
using ZeroEngine.Pool;

namespace ZeroEngine.Audio
{
    /// <summary>
    /// A pooled object that handles playing an AudioCue.
    /// Manages the AudioSource lifecycle and auto-returns to pool.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioEmitter : MonoBehaviour, IPoolable
    {
        private AudioSource _source;
        private Coroutine _playingRoutine;
        private Action<AudioEmitter> _onFinishCallback;
        private bool _isFinishing;

        public AudioCueSO CurrentCue { get; private set; }

        public AudioSource Source
        {
            get
            {
                if (_source == null) _source = GetComponent<AudioSource>();
                return _source;
            }
        }

        public bool IsPlaying => Source.isPlaying;

        public void Initialize(Action<AudioEmitter> onFinishCallback)
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _onFinishCallback = onFinishCallback;
        }

        public void OnSpawn()
        {
            _isFinishing = false;
            CurrentCue = null;
        }

        public void OnDespawn()
        {
            if (_playingRoutine != null) StopCoroutine(_playingRoutine);
            _playingRoutine = null;
            
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }

            CurrentCue = null;
        }

        public void Play(AudioCueSO cue)
        {
            if (_playingRoutine != null) StopCoroutine(_playingRoutine);

            CurrentCue = cue;

            AudioClip clip = cue.GetRandomClip();
            if (clip == null)
            {
                HandleFinish();
                return;
            }

            // Apply Settings
            Source.clip = clip; // Ensure Source property is used
            if (cue.Group != null)
            {
                _source.outputAudioMixerGroup = cue.Group;
            }
            _source.volume = cue.GetRandomVolume();
            _source.pitch = cue.GetRandomPitch();
            _source.loop = cue.Loop;
            _source.spatialBlend = cue.SpatialBlend;
            _source.panStereo = cue.PanStereo;
            _source.rolloffMode = cue.RolloffMode;
            _source.minDistance = cue.MinDistance;
            _source.maxDistance = cue.MaxDistance;
            _source.dopplerLevel = cue.DopplerLevel;
            _source.spread = cue.Spread;
            _source.reverbZoneMix = cue.ReverbZoneMix;
            _source.priority = cue.Priority;

            _source.Play();

            if (!cue.Loop)
            {
                // Schedule return
                _playingRoutine = StartCoroutine(WaitForFinish(clip.length / Mathf.Abs(_source.pitch)));
            }
        }

        public void Stop()
        {
            HandleFinish();
        }

        private IEnumerator WaitForFinish(float duration)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) yield break;
#endif
            yield return new WaitForSecondsRealtime(duration + 0.1f);
            HandleFinish();
        }

        private void HandleFinish()
        {
            if (_isFinishing)
            {
                return;
            }

            _isFinishing = true;
            _onFinishCallback?.Invoke(this);
        }
    }
}
