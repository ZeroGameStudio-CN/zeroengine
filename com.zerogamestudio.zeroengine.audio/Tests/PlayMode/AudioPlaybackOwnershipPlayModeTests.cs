namespace ZeroEngine.Audio.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using ZeroEngine.Core;
    using Object = UnityEngine.Object;

    public sealed partial class AudioPlaybackOwnershipPlayModeTests
    {
        private readonly List<Object> _owned = new();
        private GameObject _root;
        private AudioManager _manager;
        private UnityAudioEventService _service;
        private AudioCueSO _cue;
        private AudioClip _clip;
        private AudioMusicSO _musicA, _musicB;
        private UnityEngine.Random.State _originalRandom, _lastRandom;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include), Is.Null,
                "Do not destroy or adopt another audio owner.");
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.Null);
            _originalRandom = _lastRandom = UnityEngine.Random.state;
            _clip = Own(AudioClip.Create("Owned silent playback", 4800, 1, 48000, false));
            _cue = Own(ScriptableObject.CreateInstance<AudioCueSO>());
            _cue.Clips = new[] { _clip }; _cue.Loop = true; _cue.Cooldown = 0f; _cue.MaxInstances = 0;
            _cue.VolumeRange = Vector2.zero; _cue.PitchRange = Vector2.one; _cue.SpatialBlend = 0f;
            _musicA = Music(); _musicB = Music();
            CreateOwner();
        }

        private AudioMusicSO Music()
        {
            var music = Own(ScriptableObject.CreateInstance<AudioMusicSO>());
            music.LoopClip = _clip; music.Volume = 0f; music.TransitionDuration = music.FadeOutDuration = 0f;
            return music;
        }

        private void CreateOwner(int maximum = 8)
        {
            _root = Own(new GameObject("Owned audio backend"));
            _root.SetActive(false);
            _manager = _root.AddComponent<AudioManager>();
            _manager.SetVolumePersistenceEnabled(false);
            _manager.Configure(null, null, null, false);
            _manager.ConfigurePlayback(0f, 0f, 1, maximum);
            var bank = Own(ScriptableObject.CreateInstance<AudioBankSO>());
            bank.ConfigureForEditor(new[] { new AudioCueEventBinding("owned.loop", _cue) }, new[] {
                new AudioMusicEventBinding("owned.music.a", _musicA), new AudioMusicEventBinding("owned.music.b", _musicB) });
            _service = _root.AddComponent<UnityAudioEventService>();
            _service.Configure(_manager, new[] { bank });
            if (Object.FindFirstObjectByType<AudioListener>() == null) _root.AddComponent<AudioListener>();
            _root.SetActive(true);
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(_service));
        }

        [UnityTest]
        public IEnumerator Sfx_SameCueIsolationStaleReuseAndRepeatedStop()
        {
            Play(() => Assert.That(_manager.TryPlaySFX(_cue), Is.True));
            IAudioPlayback owned = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out owned), Is.True));
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(2));
            Assert.That(owned.IsCurrent && owned.IsLooping, Is.True);
            Assert.That(owned.TryStop(), Is.True);
            Assert.That(owned.TryStop(), Is.False);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            IAudioPlayback newer = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out newer), Is.True));
            owned.Dispose();
            Assert.That(newer.IsCurrent, Is.True);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(2));
            newer.Dispose();
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_OldRawClipTimerCannotReturnScopedReuse()
        {
            _manager.ConfigurePlayback(0f, 0f, 1, 1);
            _manager.PlaySFX(_clip, 0f);
            _manager.StopAllSFX();
            IAudioPlayback current = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out current), Is.True));
            yield return new WaitForSecondsRealtime(.4f);
            yield return null;
            Assert.That(current.IsCurrent, Is.True, "The old raw-clip timer cannot retire the newer generation.");
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            current.Dispose();
        }

        [UnityTest]
        public IEnumerator Sfx_LegacyAndEmitterStartsInvalidateFormerOwnership()
        {
            IAudioPlayback old = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out old), Is.True));
            var emitter = _manager.GetComponentsInChildren<AudioEmitter>().Single(e => e.CurrentCue == _cue);
            Play(() => emitter.Play(_cue));
            Assert.That(old.IsCurrent || old.TryStop(), Is.False);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            _manager.StopAllSFX();
            Play(() => Assert.That(_manager.TryPlaySFX(_cue), Is.True));
            old.Dispose();
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            _manager.StopSFX(_cue);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Music_SameTrackAndLegacyRequestTransferTheTransition()
        {
            var old = _manager.PlayMusicScoped(_musicA);
            var current = _manager.PlayMusicScoped(_musicA);
            Assert.That(old.IsCurrent || old.TryStop(), Is.False);
            Assert.That(current.IsCurrent && current.IsLooping, Is.True);
            old.Dispose();
            Assert.That(_manager.CurrentMusic, Is.SameAs(_musicA));
            current.Dispose();
            Assert.That(_manager.CurrentMusic, Is.Null);
            var beforeLegacy = _manager.PlayMusicScoped(_musicA);
            _manager.PlayMusic(_musicA);
            Assert.That(beforeLegacy.IsCurrent || beforeLegacy.TryStop(), Is.False);
            Assert.That(_manager.CurrentMusic, Is.SameAs(_musicA));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Music_DifferentEventStopAndOldHandlePreserveTheNewTrack()
        {
            var old = ((IAudioPlaybackService)_service).PlayScoped(new AudioEventId("owned.music.a"));
            _service.Play(new AudioEventId("owned.music.b"));
            old.Dispose();
            _service.Stop(new AudioEventId("owned.music.a"));
            Assert.That(_manager.CurrentMusic, Is.SameAs(_musicB));
            _service.Stop(new AudioEventId("owned.music.b"));
            Assert.That(_manager.CurrentMusic, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RejectedPlaybackAndDestroyedManagerCannotStopReplacement()
        {
            _cue.MaxInstances = 1;
            IAudioPlayback old = null, rejected = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out old), Is.True));
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out rejected), Is.False));
            Assert.That(rejected, Is.Null);
            var oldMusic = _manager.PlayMusicScoped(_musicA);
            Object.DestroyImmediate(_root);
            Assert.That(old.IsCurrent || old.TryStop(), Is.False);
            CreateOwner();
            IAudioPlayback current = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out current), Is.True));
            _manager.PlayMusic(_musicB);
            old.Dispose(); oldMusic.Dispose();
            Assert.That(current.IsCurrent, Is.True);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            Assert.That(_manager.CurrentMusic, Is.SameAs(_musicB));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyedEmitterCanRetireItsRecordWithoutStoppingAnotherVoice()
        {
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, new Vector3(-5f, 0f, 0f)), Is.True));
            IAudioPlayback owned = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out owned, new Vector3(5f, 0f, 0f)), Is.True));
            var emitter = _manager.GetComponentsInChildren<AudioEmitter>().Single(e => e.transform.position.x == 5f);
            Object.DestroyImmediate(emitter.gameObject);
            Assert.That(owned.IsCurrent, Is.False);
            Assert.That(owned.TryStop(), Is.True);
            Assert.That(owned.TryStop(), Is.False);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneShotFinishesNaturallyAndKeepsItsCapturedLoopPolicy()
        {
            _cue.Loop = false;
            IAudioPlayback oneShot = null;
            Play(() => Assert.That(_manager.TryPlaySFX(_cue, out oneShot), Is.True));
            Assert.That(oneShot.IsLooping, Is.False);
            _cue.Loop = true;
            yield return new WaitForSecondsRealtime(.4f);
            yield return null;
            Assert.That(oneShot.IsCurrent || oneShot.TryStop(), Is.False);
            Assert.That(oneShot.IsLooping, Is.False);
            Assert.That(_manager.ActiveEmitterCount, Is.Zero);
        }

        private void Play(Action action) { action(); _lastRandom = UnityEngine.Random.state; }
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [TearDown]
        public void TearDown()
        {
            if (_manager != null) { _manager.StopMusic(0f); _manager.StopAllSFX(); }
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.Null);
            if (UnityEngine.Random.state.Equals(_lastRandom)) UnityEngine.Random.state = _originalRandom;
        }
    }
}
