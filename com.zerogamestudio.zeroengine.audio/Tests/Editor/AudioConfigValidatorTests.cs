using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Audio.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.Audio.Editor.Tests
{
    public sealed class AudioConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenAudioCueAndMusicConfig()
        {
            var cue = ScriptableObject.CreateInstance<AudioCueSO>();
            var music = ScriptableObject.CreateInstance<AudioMusicSO>();

            try
            {
                cue.name = "InvalidCue";
                cue.Clips = new AudioClip[] { null };
                cue.SpatialBlend = 2f;
                cue.VolumeRange = new Vector2(1f, 0.5f);
                cue.PitchRange = new Vector2(0f, 5f);
                cue.Cooldown = -1f;

                music.name = "InvalidMusic";
                music.Volume = 2f;

                var issues = AudioConfigValidator.Validate(new[] { cue }, new[] { music });

                AssertError(issues, "Audio cue contains an empty clip reference.");
                AssertError(issues, "Spatial blend must be between 0 and 1.");
                AssertError(issues, "Range minimum cannot exceed maximum.");
                AssertError(issues, "Range must stay within [0.01, 4].");
                AssertError(issues, "Cooldown cannot be negative.");
                AssertError(issues, "Music track must define an intro or loop clip.");
                AssertError(issues, "Music volume must be between 0 and 1.");
            }
            finally
            {
                Object.DestroyImmediate(cue);
                Object.DestroyImmediate(music);
            }
        }

        private static void AssertError(IReadOnlyList<AudioValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == AudioValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
