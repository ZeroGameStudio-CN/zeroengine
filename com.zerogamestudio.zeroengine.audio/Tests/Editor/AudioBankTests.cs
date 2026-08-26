using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Core;

namespace ZeroEngine.Audio.Tests
{
    public sealed class AudioBankTests
    {
        private readonly List<UnityEngine.Object> _createdObjects = new();

        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object createdObject in _createdObjects)
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }

            _createdObjects.Clear();
            ServiceRegistry.ClearForTests();
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

        [Test]
        public void EventService_EnabledLifecycleRegistersAndUnregistersApplicationOwner()
        {
            var serviceObject = new GameObject("AudioEventServiceOwner");
            _createdObjects.Add(serviceObject);
            UnityAudioEventService service = serviceObject.AddComponent<UnityAudioEventService>();

            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(service));

            service.enabled = false;
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.Null);

            service.enabled = true;
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(service));
        }

        [Test]
        public void EventService_SecondEnabledOwnerFailsClosed()
        {
            var firstObject = new GameObject("FirstAudioEventServiceOwner");
            var secondObject = new GameObject("SecondAudioEventServiceOwner");
            _createdObjects.Add(firstObject);
            _createdObjects.Add(secondObject);
            UnityAudioEventService first = firstObject.AddComponent<UnityAudioEventService>();

            LogAssert.Expect(
                LogType.Error,
                "[UnityAudioEventService] Another IAudioEventService owner is already registered. " +
                "Only one enabled application owner is allowed.");
            UnityAudioEventService second = secondObject.AddComponent<UnityAudioEventService>();

            Assert.That(second.enabled, Is.False);
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(first));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _createdObjects.Add(instance);
            return instance;
        }
    }
}
