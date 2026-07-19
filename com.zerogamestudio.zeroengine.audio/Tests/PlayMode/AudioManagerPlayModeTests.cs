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
            foreach (AudioManager existing in Object.FindObjectsOfType<AudioManager>(true))
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
