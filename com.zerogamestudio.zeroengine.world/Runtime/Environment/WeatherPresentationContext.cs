namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// Immutable payload for weather presentation adapters.
    /// </summary>
    public readonly struct WeatherPresentationContext
    {
        public WeatherPresentationContext(
            WeatherType previousWeatherType,
            WeatherType currentWeatherType,
            WeatherPresetSO currentPreset,
            bool immediate)
        {
            PreviousWeatherType = previousWeatherType;
            CurrentWeatherType = currentWeatherType;
            CurrentPreset = currentPreset;
            Immediate = immediate;
        }

        public WeatherType PreviousWeatherType { get; }
        public WeatherType CurrentWeatherType { get; }
        public WeatherPresetSO CurrentPreset { get; }
        public bool Immediate { get; }
        public bool HasPreset => CurrentPreset != null;
    }
}
