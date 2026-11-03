using System;
using UnityEngine;

namespace ZeroEngine.Render.Weather
{
    public interface ISurfaceWeatherExposure
    {
        bool IsExposureReady { get; }
        float RainExposure(Vector3 position);
        float SnowExposure(Vector3 position);
    }

    /// <summary>Optional shared view of one scene exposure owner, including precipitation and contact consumers.</summary>
    public static class SurfaceWeatherExposure
    {
        public static ISurfaceWeatherExposure Current { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Current = null;
        public static void Register(ISurfaceWeatherExposure owner)
        {
            if (owner == null || Current != null && !ReferenceEquals(Current, owner))
                throw new InvalidOperationException("Only one scene exposure owner can publish at a time.");
            Current = owner;
        }
        public static void Unregister(ISurfaceWeatherExposure owner)
        {
            if (ReferenceEquals(Current, owner)) Current = null;
        }
    }
}
