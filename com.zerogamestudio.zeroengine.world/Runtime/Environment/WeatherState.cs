using System;

namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// State-only weather runtime. Presentation adapters react to its events.
    /// </summary>
    public sealed class WeatherState
    {
        private WeatherType _currentWeatherType = WeatherType.Clear;

        public event Action<EnvironmentEventArgs> OnEnvironmentEvent;

        public WeatherType CurrentWeatherType => _currentWeatherType;

        public void SetWeather(WeatherType type)
        {
            if (_currentWeatherType == type) return;

            WeatherType previous = _currentWeatherType;
            _currentWeatherType = type;
            OnEnvironmentEvent?.Invoke(EnvironmentEventArgs.WeatherChanged(type, previous));
        }

        public void ClearWeather()
        {
            SetWeather(WeatherType.Clear);
        }

        public object ExportSaveData()
        {
            return new WeatherSaveData
            {
                CurrentWeatherType = _currentWeatherType
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not WeatherSaveData saveData) return;
            SetWeather(saveData.CurrentWeatherType);
        }
    }
}
