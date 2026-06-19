using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Audio.Editor.Tests
{
    public sealed class AudioCueTests
    {
        [Test]
        public void GetRandomClipReturnsNullWhenNoClipsAreConfigured()
        {
            var cue = ScriptableObject.CreateInstance<AudioCueSO>();
            try
            {
                cue.Clips = null;

                Assert.IsNull(cue.GetRandomClip());

                cue.Clips = new AudioClip[0];

                Assert.IsNull(cue.GetRandomClip());
            }
            finally
            {
                Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void FixedVolumeAndPitchRangesReturnConfiguredValue()
        {
            var cue = ScriptableObject.CreateInstance<AudioCueSO>();
            try
            {
                cue.VolumeRange = new Vector2(0.35f, 0.35f);
                cue.PitchRange = new Vector2(1.25f, 1.25f);

                Assert.That(cue.GetRandomVolume(), Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(cue.GetRandomPitch(), Is.EqualTo(1.25f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void AudioMusicDefaultsToFullVolume()
        {
            var music = ScriptableObject.CreateInstance<AudioMusicSO>();
            try
            {
                Assert.AreEqual(1f, music.Volume);
                Assert.IsNull(music.IntroClip);
                Assert.IsNull(music.LoopClip);
            }
            finally
            {
                Object.DestroyImmediate(music);
            }
        }
    }
}
