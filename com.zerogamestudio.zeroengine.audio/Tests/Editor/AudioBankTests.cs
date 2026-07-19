using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Audio.Tests
{
    public sealed class AudioBankTests
    {
        private readonly List<UnityEngine.Object> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object createdObject in _createdObjects)
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void AudioEventId_TrimsAndUsesOrdinalEquality()
        {
            var first = new AudioEventId(" piece.pickup ");
            var second = new AudioEventId("piece.pickup");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.ToString(), Is.EqualTo("piece.pickup"));
            Assert.Throws<ArgumentException>(() => new AudioEventId("   "));
        }

        [Test]
        public void CueRandomClip_SkipsMissingEntries()
        {
            AudioCueSO cue = Create<AudioCueSO>();
            AudioClip clip = AudioClip.Create("valid", 32, 1, 22050, false);
            _createdObjects.Add(clip);
            cue.Clips = new AudioClip[] { null, clip, null };

            for (int index = 0; index < 20; index++)
            {
                Assert.That(cue.GetRandomClip(), Is.SameAs(clip));
            }

            Assert.That(cue.HasPlayableClip(), Is.True);
        }

        [Test]
        public void BankValidation_ReportsDuplicateIdsAcrossSfxAndMusic()
        {
            AudioCueSO cue = Create<AudioCueSO>();
            AudioMusicSO music = Create<AudioMusicSO>();
            AudioBankSO bank = Create<AudioBankSO>();
            bank.ConfigureForEditor(
                new[] { new AudioCueEventBinding("shared.event", cue) },
                new[] { new AudioMusicEventBinding("shared.event", music) });

            var errors = new List<string>();
            bank.CollectValidationErrors(errors);

            Assert.That(errors, Has.Some.Contains("duplicate event id 'shared.event'"));
        }

        [Test]
        public void BankValidation_ReportsInvalidCueAndMusicDefinitions()
        {
            AudioCueSO cue = Create<AudioCueSO>();
            cue.Clips = new AudioClip[] { null };
            cue.VolumeRange = new Vector2(0.8f, 0.2f);
            cue.PitchRange = new Vector2(1.2f, 0.8f);
            cue.PanStereo = 2f;
            cue.MinDistance = 5f;
            cue.MaxDistance = 2f;
            AudioMusicSO music = Create<AudioMusicSO>();
            music.Volume = 2f;
            music.TransitionDuration = -2f;
            AudioBankSO bank = Create<AudioBankSO>();
            bank.ConfigureForEditor(
                new[] { new AudioCueEventBinding("piece.pickup", cue) },
                new[] { new AudioMusicEventBinding("music.gallery", music) });

            var errors = new List<string>();
            bank.CollectValidationErrors(errors);

            Assert.That(errors, Has.Some.Contains("no playable AudioClip"));
            Assert.That(errors, Has.Some.Contains("invalid volume range"));
            Assert.That(errors, Has.Some.Contains("invalid pitch range"));
            Assert.That(errors, Has.Some.Contains("invalid spatial audio settings"));
            Assert.That(errors, Has.Some.Contains("no playable music clip"));
            Assert.That(errors, Has.Some.Contains("invalid music volume"));
            Assert.That(errors, Has.Some.Contains("invalid music transition settings"));
        }

        [TestCase(0f, -80f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(1f, 0f)]
        public void NormalizedVolume_MapsToExpectedDecibels(float normalized, float expected)
        {
            Assert.That(
                AudioManager.NormalizedToDecibels(normalized),
                Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void EventService_RejectsCrossBankDuplicateAndKeepsExistingBank()
        {
            AudioCueSO firstCue = Create<AudioCueSO>();
            AudioCueSO secondCue = Create<AudioCueSO>();
            AudioBankSO firstBank = Create<AudioBankSO>();
            AudioBankSO secondBank = Create<AudioBankSO>();
            firstBank.ConfigureForEditor(
                new[] { new AudioCueEventBinding("piece.pickup", firstCue) },
                null);
            secondBank.ConfigureForEditor(
                new[] { new AudioCueEventBinding("piece.pickup", secondCue) },
                null);

            var serviceObject = new GameObject("AudioEventServiceTests");
            _createdObjects.Add(serviceObject);
            UnityAudioEventService service = serviceObject.AddComponent<UnityAudioEventService>();

            Assert.That(service.RegisterBank(firstBank, out string firstError), Is.True, firstError);
            Assert.That(service.RegisterBank(secondBank, out string secondError), Is.False);
            Assert.That(secondError, Does.Contain("Duplicate audio event id 'piece.pickup'"));
            Assert.That(service.HasEvent(new AudioEventId("piece.pickup")), Is.True);
            Assert.That(service.UnregisterBank(firstBank), Is.True);
            Assert.That(service.HasEvent(new AudioEventId("piece.pickup")), Is.False);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _createdObjects.Add(instance);
            return instance;
        }
    }
}
