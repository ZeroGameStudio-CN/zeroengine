using System;

namespace ZeroEngine.EnvironmentSystem
{
    public readonly struct SurfaceWeatherState
    {
        public SurfaceWeatherState(float wetness, float snow)
        {
            if (!Unit(wetness) || !Unit(snow)) throw new ArgumentOutOfRangeException(nameof(wetness));
            Wetness = wetness;
            Snow = snow;
        }
        public float Wetness { get; }
        public float Snow { get; }
        internal static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }

    public readonly struct SurfaceWeatherRates
    {
        public SurfaceWeatherRates(float wetting, float drying, float accumulation, float melting,
            float rainMelting, float meltWater)
        {
            if (!SurfaceWeatherState.Unit(wetting) || !SurfaceWeatherState.Unit(drying)
                || !SurfaceWeatherState.Unit(accumulation) || !SurfaceWeatherState.Unit(melting)
                || !SurfaceWeatherState.Unit(rainMelting) || !SurfaceWeatherState.Unit(meltWater))
                throw new ArgumentOutOfRangeException(nameof(wetting));
            Wetting = wetting; Drying = drying; Accumulation = accumulation;
            Melting = melting; RainMelting = rainMelting; MeltWater = meltWater;
        }
        public float Wetting { get; }
        public float Drying { get; }
        public float Accumulation { get; }
        public float Melting { get; }
        public float RainMelting { get; }
        public float MeltWater { get; }
    }

    /// <summary>zero.environment.surface_weather.v1. Explicit clock and exposure; no engine, rendering or global weather ownership.</summary>
    public static class SurfaceWeatherEvolution
    {
        public static bool TryAdvance(SurfaceWeatherState current, SurfaceWeatherRates rates,
            float rain, float snow, float exposure, float deltaSeconds, out SurfaceWeatherState next)
        {
            next = current;
            if (!SurfaceWeatherState.Unit(rain) || !SurfaceWeatherState.Unit(snow)
                || !SurfaceWeatherState.Unit(exposure) || float.IsNaN(deltaSeconds)
                || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f || deltaSeconds > 86400f) return false;
            float exposedRain = rain * exposure;
            float snowAdded = snow * exposure * rates.Accumulation * deltaSeconds;
            float meltCapacity = (rates.Melting * (1f - snow * exposure) + rain * exposure * rates.RainMelting) * deltaSeconds;
            float melted = Math.Min(current.Snow + snowAdded, meltCapacity);
            float snowCover = Clamp(current.Snow + snowAdded - melted);
            float wetness = Clamp(current.Wetness + exposedRain * rates.Wetting * deltaSeconds
                + melted * rates.MeltWater - rates.Drying * (1f - exposedRain) * deltaSeconds);
            next = new SurfaceWeatherState(wetness, snowCover);
            return true;
        }
        private static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
