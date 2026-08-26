using System;
using NUnit.Framework;

namespace ZeroEngine.Haptics.Tests
{
    public sealed class HapticResponseTests
    {
        [Test]
        public void RepresentativeIntensityTiers_AreStrictlyOrdered()
        {
            float[] intensities = { 0.3f, 0.5f, 0.8f, 1.4f, 2f, 3.5f, 5f, 6f };
            float previous = 0f;

            foreach (float intensity in intensities)
            {
                Assert.That(
                    HapticResponseResolver.TryResolve(
                        new HapticRequest(intensity, 2f, 0.25f, 0.2f),
                        out HapticResolvedPulse pulse),
                    Is.True);
                Assert.That(pulse.Strength, Is.GreaterThan(previous));
                previous = pulse.Strength;
            }
        }

        [Test]
        public void Sharpness_TransfersEnergyFromLowToHighMotor()
        {
            HapticResolvedPulse soft = HapticResponseResolver.Resolve(
                new HapticRequest(2f, 0f, 0.2f, 0.2f));
            HapticResolvedPulse sharp = HapticResponseResolver.Resolve(
                new HapticRequest(2f, float.MaxValue, 0.2f, 0.2f));

            Assert.That(sharp.Strength, Is.EqualTo(soft.Strength));
            Assert.That(sharp.LowFrequencyMotor, Is.LessThan(soft.LowFrequencyMotor));
            Assert.That(sharp.HighFrequencyMotor, Is.GreaterThan(soft.HighFrequencyMotor));
        }

        [Test]
        public void Duration_IsMonotonicAndBounded()
        {
            float[] sourceDurations = { 0f, 0.05f, 0.09f, 0.2f, 0.25f, 0.55f, 2f, 4f, float.MaxValue };
            float previous = 0f;

            foreach (float sourceDuration in sourceDurations)
            {
                HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                    new HapticRequest(2f, 1f, sourceDuration));
                Assert.That(pulse.Duration, Is.GreaterThanOrEqualTo(previous));
                Assert.That(
                    pulse.Duration,
                    Is.LessThanOrEqualTo(HapticResponseResolver.MaximumDuration));
                previous = pulse.Duration;
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteRequestValues_AreRejected(float invalid)
        {
            Assert.That(
                HapticResponseResolver.TryResolve(
                    new HapticRequest(invalid, 1f, 0.2f),
                    out _),
                Is.False);
            Assert.That(
                HapticResponseResolver.TryResolve(
                    new HapticRequest(1f, invalid, 0.2f),
                    out _),
                Is.False);
            Assert.That(
                HapticResponseResolver.TryResolve(
                    new HapticRequest(1f, 1f, invalid),
                    out _),
                Is.False);
            Assert.That(
                HapticResponseResolver.TryResolve(
                    new HapticRequest(1f, 1f, 0.2f, invalid),
                    out _),
                Is.False);
        }

        [Test]
        public void ExtremeFiniteValues_RespectHardBounds()
        {
            HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                new HapticRequest(
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue));

            Assert.That(pulse.IsPlayable, Is.True);
            Assert.That(
                pulse.Strength,
                Is.LessThanOrEqualTo(HapticResponseResolver.MaximumStrength));
            Assert.That(
                pulse.LowFrequencyMotor,
                Is.LessThanOrEqualTo(HapticResponseResolver.MaximumLowFrequencyMotor));
            Assert.That(
                pulse.HighFrequencyMotor,
                Is.LessThanOrEqualTo(HapticResponseResolver.MaximumHighFrequencyMotor));
            Assert.That(
                pulse.Duration,
                Is.LessThanOrEqualTo(HapticResponseResolver.MaximumDuration));
        }

        [Test]
        public void RandomFiniteInputs_PreserveBoundsAndIntensityMonotonicity()
        {
            var random = new Random(0x485450);
            for (int i = 0; i < 200_000; i++)
            {
                float intensity = SampleFiniteNonNegative(random);
                float sharpness = SampleFiniteNonNegative(random);
                float duration = SampleFiniteNonNegative(random);
                float gain = SampleFiniteNonNegative(random);

                HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                    new HapticRequest(intensity, sharpness, duration, gain));
                HapticResolvedPulse stronger = HapticResponseResolver.Resolve(
                    new HapticRequest(
                        SaturatingDouble(intensity),
                        sharpness,
                        duration,
                        gain));

                if (pulse.Strength > HapticResponseResolver.MaximumStrength
                    || pulse.LowFrequencyMotor
                    > HapticResponseResolver.MaximumLowFrequencyMotor
                    || pulse.HighFrequencyMotor
                    > HapticResponseResolver.MaximumHighFrequencyMotor
                    || pulse.Duration > HapticResponseResolver.MaximumDuration
                    || stronger.Strength < pulse.Strength)
                {
                    Assert.Fail($"Property violation at deterministic sample {i}.");
                }
            }
        }

        [Test]
        public void Arbiter_RejectsSamePulseInsideCooldown()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                new HapticRequest(1f, 1f, 0f));

            Assert.That(arbiter.TryAccept(pulse, 0d, out _), Is.True);
            Assert.That(
                arbiter.TryAccept(pulse, HapticArbiter.CooldownSeconds - 0.000001d, out _),
                Is.False);

            arbiter.Reset();
            Assert.That(arbiter.TryAccept(pulse, 0d, out _), Is.True);
            Assert.That(
                arbiter.TryAccept(pulse, HapticArbiter.CooldownSeconds, out _),
                Is.True);
        }

        [Test]
        public void Arbiter_AllowsClearlyStrongerReplacement()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse weak = HapticResponseResolver.Resolve(
                new HapticRequest(0.3f, 1f, 0.2f));
            HapticResolvedPulse strong = HapticResponseResolver.Resolve(
                new HapticRequest(3f, 1f, 0.2f));

            Assert.That(arbiter.TryAccept(weak, 0d, out _), Is.True);
            Assert.That(arbiter.TryAccept(strong, 0.01d, out _), Is.True);
        }

        [Test]
        public void Arbiter_EnergyBudgetScalesSustainedMaximumPulses()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse maximum = HapticResponseResolver.Resolve(
                new HapticRequest(
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue));

            Assert.That(arbiter.TryAccept(maximum, 0d, out HapticResolvedPulse first), Is.True);
            Assert.That(arbiter.TryAccept(maximum, 0.12d, out HapticResolvedPulse second), Is.True);
            Assert.That(arbiter.TryAccept(maximum, 0.24d, out HapticResolvedPulse third), Is.True);

            Assert.That(first.Strength, Is.EqualTo(maximum.Strength));
            Assert.That(second.Strength, Is.EqualTo(maximum.Strength));
            Assert.That(third.Strength, Is.LessThan(maximum.Strength));
            Assert.That(third.Strength, Is.GreaterThanOrEqualTo(
                HapticResponseResolver.MinimumPlayableStrength));
        }

        [Test]
        public void Arbiter_BudgetScaledWeakPulse_CannotReplaceStrongerActivePulse()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse maximum = HapticResponseResolver.Resolve(
                new HapticRequest(
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue));

            Assert.That(arbiter.TryAccept(maximum, 0d, out _), Is.True);
            Assert.That(arbiter.TryAccept(maximum, 0.12d, out _), Is.True);
            Assert.That(
                arbiter.TryAccept(maximum, 0.24d, out HapticResolvedPulse active),
                Is.True);
            Assert.That(active.Strength, Is.LessThan(maximum.Strength));

            Assert.That(arbiter.TryAccept(maximum, 0.25d, out _), Is.False);
        }

        [Test]
        public void Arbiter_RejectsNonFiniteTimestampWithoutSpendingEnergy()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                new HapticRequest(1f, 1f, 0.2f));
            float before = arbiter.AvailableEnergy;

            Assert.That(arbiter.TryAccept(pulse, double.NaN, out _), Is.False);
            Assert.That(arbiter.AvailableEnergy, Is.EqualTo(before));
        }

        [Test]
        public void Arbiter_ClockRollbackStartsFreshLifecycle()
        {
            var arbiter = new HapticArbiter();
            HapticResolvedPulse pulse = HapticResponseResolver.Resolve(
                new HapticRequest(1f, 1f, 0f));

            Assert.That(arbiter.TryAccept(pulse, 10d, out _), Is.True);
            Assert.That(arbiter.TryAccept(pulse, 9d, out _), Is.True);
        }

        private static float SampleFiniteNonNegative(Random random)
        {
            double exponent = -6d + random.NextDouble() * 44d;
            double value = Math.Pow(10d, exponent) * random.NextDouble();
            return value >= float.MaxValue ? float.MaxValue : (float)value;
        }

        private static float SaturatingDouble(float value)
        {
            return value >= float.MaxValue / 2f ? float.MaxValue : value * 2f;
        }
    }
}
