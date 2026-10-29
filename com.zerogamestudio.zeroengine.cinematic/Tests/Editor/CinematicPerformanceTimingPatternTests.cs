using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPerformanceTimingPatternTests
    {
        [Test]
        public void AuthoredFields_RoundTripWithoutChangingContactOrCueIdentity()
        {
            var pattern = JsonUtility.FromJson<CinematicPerformanceTimingPattern>(
                "{\"_sourceSequence\":\"test.sequence\",\"_durationSeconds\":2.0,\"_contacts\":[0.25,1.0],"
                + "\"_cues\":[{\"_seconds\":0.25,\"_kind\":0,\"_id\":\"slash\"},{\"_seconds\":1.0,\"_kind\":1,\"_id\":\"impact\"}]}" );
            pattern.Validate();
            var copy = JsonUtility.FromJson<CinematicPerformanceTimingPattern>(JsonUtility.ToJson(pattern));
            copy.Validate();
            Assert.That(copy.SourceSequence, Is.EqualTo("test.sequence"));
            Assert.That(copy.ContactCount, Is.EqualTo(2));
            Assert.That(copy.ContactAt(1), Is.EqualTo(1f));
            Assert.That(copy.CueAt(0).Kind, Is.EqualTo(CinematicPerformanceCueKind.Actor));
            Assert.That(copy.CueAt(1).Kind, Is.EqualTo(CinematicPerformanceCueKind.Audio));
            Assert.That(copy.CueAt(1).Id, Is.EqualTo("impact"));
            Assert.That(copy.HasActorCues, Is.True);
        }

        [TestCase(0f), TestCase(-1f), TestCase(float.NaN), TestCase(float.PositiveInfinity)]
        public void InvalidDuration_IsRejected(float duration)
        {
            var pattern = Pattern(duration, .25f);
            Assert.Throws<InvalidOperationException>(pattern.Validate);
        }

        [TestCase(-.1f, .25f), TestCase(.5f, .25f), TestCase(.25f, .25f), TestCase(.25f, 2f)]
        public void InvalidContactOrderingOrRange_IsRejected(float first, float second)
        {
            var pattern = Pattern(2f, first, second);
            Assert.Throws<InvalidOperationException>(pattern.Validate);
        }

        [Test]
        public void EmptyContacts_AreRejected()
        {
            Assert.Throws<InvalidOperationException>(Pattern(2f).Validate);
        }

        [Test]
        public void AudioOnlyCues_DoNotClaimAnActorCue()
        {
            var pattern = Pattern(2f, .25f);
            var cue = new CinematicPerformanceTimedCue();
            Set(cue, "_seconds", .25f);
            Set(cue, "_kind", CinematicPerformanceCueKind.Audio);
            Set(cue, "_id", "impact");
            Set(pattern, "_cues", new[] { cue });
            pattern.Validate();
            Assert.That(pattern.HasActorCues, Is.False);
        }

        [Test]
        public void UnknownCueChannel_IsRejected()
        {
            var pattern = Pattern(2f, .25f);
            var cue = new CinematicPerformanceTimedCue();
            Set(cue, "_seconds", .25f);
            Set(cue, "_kind", (CinematicPerformanceCueKind)99);
            Set(cue, "_id", "unknown");
            Set(pattern, "_cues", new[] { cue });
            Assert.Throws<InvalidOperationException>(pattern.Validate);
        }

        [Test]
        public void OverlappingLightPulses_UseMaximumRatherThanSumming()
        {
            var pattern = Pattern(4f, 1f);
            Set(pattern, "_lightPulses", new[] { Pulse(1f, 1f), Pulse(1f, 1f) });
            pattern.Validate();
            var envelope = AnimationCurve.Linear(0f, .35f, 2f, .35f);
            Assert.That(pattern.EvaluateLight(1.5f, envelope), Is.EqualTo(.35f).Within(.00001f));
            Assert.That(pattern.EvaluateLight(.9f, envelope), Is.Zero);
            Assert.That(pattern.EvaluateLight(2f, envelope), Is.Zero);
        }

        [Test]
        public void PulseOutsidePattern_IsRejected()
        {
            var pattern = Pattern(2f, .25f);
            Set(pattern, "_lightPulses", new[] { Pulse(1.5f, 1f) });
            Assert.Throws<InvalidOperationException>(pattern.Validate);
        }

        private static CinematicPerformanceTimingPattern Pattern(float duration, params float[] contacts)
        {
            var pattern = new CinematicPerformanceTimingPattern();
            Set(pattern, "_durationSeconds", duration);
            Set(pattern, "_contacts", contacts);
            return pattern;
        }

        private static CinematicPerformanceLightPulse Pulse(float seconds, float duration)
        {
            var pulse = new CinematicPerformanceLightPulse();
            Set(pulse, "_seconds", seconds);
            Set(pulse, "_durationSeconds", duration);
            return pulse;
        }

        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
