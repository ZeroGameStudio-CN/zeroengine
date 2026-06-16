using NUnit.Framework;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.Tests.World
{
    [TestFixture]
    [Category("Unit")]
    public sealed class WeatherStateTests
    {
        [Test]
        public void SetWeather_ChangesTypeAndRaisesEvent()
        {
            var state = new WeatherState();
            EnvironmentEventArgs received = default;
            state.OnEnvironmentEvent += args => received = args;

            state.SetWeather(WeatherType.Rain);

            Assert.That(state.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(received.Type, Is.EqualTo(EnvironmentEventType.WeatherChanged));
            Assert.That(received.Weather, Is.EqualTo(WeatherType.Rain));
            Assert.That(received.PreviousWeather, Is.EqualTo(WeatherType.Clear));
        }

        [Test]
        public void ClearWeather_ReturnsToClearAndRaisesEvent()
        {
            var state = new WeatherState();
            state.SetWeather(WeatherType.Snow);

            state.ClearWeather();

            Assert.That(state.CurrentWeatherType, Is.EqualTo(WeatherType.Clear));
        }

        [Test]
        public void ExportImport_RestoresWeatherType()
        {
            var state = new WeatherState();
            state.SetWeather(WeatherType.Fog);
            object data = state.ExportSaveData();
            var restored = new WeatherState();

            restored.ImportSaveData(data);

            Assert.That(restored.CurrentWeatherType, Is.EqualTo(WeatherType.Fog));
        }
    }
}
