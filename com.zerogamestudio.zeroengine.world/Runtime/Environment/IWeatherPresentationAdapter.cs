namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// Optional presentation boundary for fog, VFX, audio, and project-specific weather visuals.
    /// </summary>
    public interface IWeatherPresentationAdapter
    {
        void ApplyWeatherPresentation(WeatherPresentationContext context);
        void ClearWeatherPresentation(WeatherType previousWeatherType);
    }
}
