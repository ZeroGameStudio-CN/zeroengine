using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeroEngine.Audio.Tests
{
    public sealed class AudioManagerPlayModeTests
    {
        private readonly List<Object> _createdObjects = new();
        private AudioManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            foreach (AudioManager existing in Object.FindObjectsByType<AudioManager>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                Object.Destroy(existing.gameObject);
            }

            yield return null;

            var managerObject = new GameObject("AudioManager under test");
            managerObject.SetActive(false);
            _manager = managerObject.AddComponent<AudioManager>();
            typeof(AudioManager)
                .GetField("_persistVolumeWithSaveManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(_manager, false);
            managerObject.SetActive(true);
            _createdObjects.Add(managerObject);

            var listenerObject = new GameObject("AudioListener under test");
            listenerObject.AddComponent<AudioListener>();
            _createdObjects.Add(listenerObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_manager != null)
            {
                _manager.StopMusic(0f);
                _manager.StopAllSFX();
            }

            foreach (Object createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    Object.Destroy(createdObject);
                }
            }

            _createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneShot_ReturnsEmitterToPoolAfterPlayback()
        {
            AudioCueSO cue = CreateCue("short one-shot", false, 2205);

            Assert.That(_manager.TryPlaySFX(cue), Is.True);
            Assert.That(_manager.ActiveEmitterCount, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(0.25f);

            Assert.That(_manager.ActiveEmitterCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Loop_EnforcesConcurrencyCooldownAndStopsExplicitly()
        {
            AudioCueSO cue = CreateCue("loop", true, 22050);
            cue.MaxInstances = 1;
            cue.Cooldown = 0f;

            Assert.That(_manager.TryPlaySFX(cue), Is.True);
            Assert.That(_manager.TryPlaySFX(cue), Is.False);
            _manager.StopSFX(cue);
            Assert.That(_manager.ActiveEmitterCount, Is.Zero);

            cue.MaxInstances = 0;
            cue.Cooldown = 0.05f;
            Assert.That(_manager.TryPlaySFX(cue), Is.True);
            _manager.StopSFX(cue);
            Assert.That(_manager.TryPlaySFX(cue), Is.False);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(_manager.TryPlaySFX(cue), Is.True);
            _manager.StopSFX(cue);
        }

        [UnityTest]
        public IEnumerator CuePlayback_AppliesSpatialAudioSettings()
        {
            AudioCueSO cue = CreateCue("spatial loop", true, 22050);
            cue.SpatialBlend = 0.75f;
            cue.PanStereo = -0.2f;
            cue.RolloffMode = AudioRolloffMode.Linear;
            cue.MinDistance = 2f;
            cue.MaxDistance = 24f;
            cue.DopplerLevel = 0.4f;
            cue.Spread = 35f;
            cue.ReverbZoneMix = 0.65f;

            Assert.That(_manager.TryPlaySFX(cue), Is.True);
            var emitters = (List<AudioEmitter>)typeof(AudioManager)
                .GetField("_activeEmitters", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_manager);
            AudioSource source = emitters?.Single().Source;

            Assert.That(source, Is.Not.Null);
            Assert.That(source.spatialBlend, Is.EqualTo(0.75f));
            Assert.That(source.panStereo, Is.EqualTo(-0.2f));
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.EqualTo(2f));
            Assert.That(source.maxDistance, Is.EqualTo(24f));
            Assert.That(source.dopplerLevel, Is.EqualTo(0.4f));
            Assert.That(source.spread, Is.EqualTo(35f));
            Assert.That(source.reverbZoneMix, Is.EqualTo(0.65f));

            _manager.StopSFX(cue);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaybackPolicy_CanBeConfiguredAfterInitialization()
        {
            _manager.ConfigurePlayback(0.35f, 0.6f, 12, 18);

            Assert.That(_manager.CrossFadeTime, Is.EqualTo(0.35f));
            Assert.That(_manager.MusicFadeOutTime, Is.EqualTo(0.6f));
            Assert.That(_manager.InitialPoolSize, Is.EqualTo(12));
            Assert.That(_manager.MaximumPoolSize, Is.EqualTo(18));
            Assert.That(
                Object.FindObjectsByType<AudioEmitter>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length,
                Is.GreaterThanOrEqualTo(12));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MusicCrossfade_UsesUnscaledTimeAndStopsPreviousSource()
        {
            AudioMusicSO first = CreateMusic("first");
            AudioMusicSO second = CreateMusic("second");
            _manager.PlayMusic(first, 0f);
            yield return null;

            Time.timeScale = 0f;
            _manager.PlayMusic(second, 0.05f);
            yield return new WaitForSecondsRealtime(0.12f);

            AudioSource[] sources = (AudioSource[])typeof(AudioManager)
                .GetField("_musicSources", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_manager);
            Assert.That(_manager.CurrentMusic, Is.SameAs(second));
            Assert.That(sources, Is.Not.Null);
            Assert.That(sources.Count(source => source.isPlaying), Is.EqualTo(1));
            Assert.That(sources.Single(source => source.isPlaying).clip, Is.SameAs(second.LoopClip));
            Assert.That(sources.Single(source => !source.isPlaying).clip, Is.Null);

            _manager.StopMusic(0.05f);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(sources, Has.All.Matches<AudioSource>(source => !source.isPlaying));
        }

        private AudioCueSO CreateCue(string name, bool loop, int sampleCount)
        {
            AudioClip clip = AudioClip.Create(name, sampleCount, 1, 22050, false);
            var cue = ScriptableObject.CreateInstance<AudioCueSO>();
            cue.Clips = new[] { clip };
            cue.Loop = loop;
            cue.Cooldown = 0f;
            _createdObjects.Add(clip);
            _createdObjects.Add(cue);
            return cue;
        }

        private AudioMusicSO CreateMusic(string name)
        {
            AudioClip clip = AudioClip.Create(name, 22050, 1, 22050, false);
            var music = ScriptableObject.CreateInstance<AudioMusicSO>();
            music.LoopClip = clip;
            _createdObjects.Add(clip);
            _createdObjects.Add(music);
            return music;
        }
    }
}
