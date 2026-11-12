using NUnit.Framework;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.World.Tests
{
    [Category("Unit")]
    public sealed class SurfaceWeatherEvolutionTests
    {
        private static SurfaceWeatherRates Rates => new(.1f, .02f, .1f, .01f, .1f, .5f);

        [TestCase(1f, 0f, 1f, 5f, .5f, 0f, TestName = "Advance_ExposedRain_WetsGradually")]
        [TestCase(1f, 0f, 0f, 5f, 0f, 0f, TestName = "Advance_CoveredGround_StaysDry")]
        [TestCase(0f, 1f, 1f, 5f, 0f, .5f, TestName = "Advance_ExposedSnow_AccumulatesGradually")]
        [TestCase(1f, 0f, 1f, 86400f, 1f, 0f, TestName = "Advance_LongRain_ClampsWetness")]
        public void Advance_ExposureAndClock_ProduceBoundedWeather(float rain, float snow,
            float exposure, float delta, float wet, float cover)
        {
            Assert.That(SurfaceWeatherEvolution.TryAdvance(default, Rates, rain, snow, exposure,
                delta, out var next), Is.True);
            Assert.That(next.Wetness, Is.EqualTo(wet).Within(.000001f));
            Assert.That(next.Snow, Is.EqualTo(cover).Within(.000001f));
        }

        [TestCase(0f, 0f, 1f, 5f, .4f, 0f, TestName = "Advance_ClearWeather_DriesRetainedMoisture")]
        [TestCase(1f, 0f, 1f, 1f, .655f, .39f, TestName = "Advance_Rain_MeltsSnowIntoMoisture")]
        public void Advance_ClearAndRain_PreserveMoistureAndMeltSnow(float rain, float snow,
            float exposure, float delta, float wet, float cover)
        {
            var current = new SurfaceWeatherState(.5f, rain > 0f ? .5f : 0f);
            Assert.That(SurfaceWeatherEvolution.TryAdvance(current, Rates, rain, snow, exposure,
                delta, out var next), Is.True);
            Assert.That(next.Wetness, Is.EqualTo(wet).Within(.000001f));
            Assert.That(next.Snow, Is.EqualTo(cover).Within(.000001f));
        }

        [TestCase(-1f, TestName = "Advance_NegativeClock_PreservesState")]
        [TestCase(float.NaN, TestName = "Advance_NonfiniteClock_PreservesState")]
        [TestCase(float.PositiveInfinity, TestName = "Advance_InfiniteClock_PreservesState")]
        [TestCase(86401f, TestName = "Advance_OversizeClock_PreservesState")]
        public void Advance_InvalidDelta_PreservesState(float delta)
        {
            var current = new SurfaceWeatherState(.2f, .3f);
            Assert.That(SurfaceWeatherEvolution.TryAdvance(current, Rates, 1f, 1f, 1f,
                delta, out var next), Is.False);
            Assert.That(next.Wetness, Is.EqualTo(current.Wetness));
            Assert.That(next.Snow, Is.EqualTo(current.Snow));
        }

        [Test]
        public void Advance_ZeroDelta_FreezesState()
        {
            var current = new SurfaceWeatherState(.2f, .3f);
            Assert.That(SurfaceWeatherEvolution.TryAdvance(current, Rates, 1f, 1f, 1f,
                0f, out var next), Is.True);
            Assert.That(next.Wetness, Is.EqualTo(current.Wetness));
            Assert.That(next.Snow, Is.EqualTo(current.Snow));
        }
    }
}
