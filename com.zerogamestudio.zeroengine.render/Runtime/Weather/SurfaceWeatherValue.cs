using System;
using UnityEngine;

namespace ZeroEngine.Render.Weather
{
    public readonly struct SurfaceWeatherValue : IEquatable<SurfaceWeatherValue>
    {
        public SurfaceWeatherValue(float wetness, float snow)
        {
            if (!Valid(wetness) || !Valid(snow)) throw new ArgumentOutOfRangeException(nameof(wetness));
            Wetness = wetness;
            Snow = snow;
        }

        public float Wetness { get; }
        public float Snow { get; }
        public bool Equals(SurfaceWeatherValue other) => Wetness == other.Wetness && Snow == other.Snow;
        public override bool Equals(object obj) => obj is SurfaceWeatherValue other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Wetness, Snow);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Valid(float value) => Finite(value) && value >= 0f && value <= 1f;
    }

    public readonly struct SurfaceWeatherRegion
    {
        public SurfaceWeatherRegion(string id, Bounds bounds, SurfaceWeatherValue value, float feather = 3f, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || !SurfaceWeatherValue.Finite(feather) || feather < 0f
                || !Finite(bounds.center) || !Finite(bounds.size) || bounds.size.x <= 0f || bounds.size.z <= 0f)
                throw new ArgumentException("A weather region requires a stable identity and finite nonempty XZ bounds.");
            Id = id;
            Bounds = bounds;
            Value = value;
            Feather = feather;
            Priority = priority;
        }

        public string Id { get; }
        public Bounds Bounds { get; }
        public SurfaceWeatherValue Value { get; }
        public float Feather { get; }
        public int Priority { get; }
        private static bool Finite(Vector3 v) => SurfaceWeatherValue.Finite(v.x)
            && SurfaceWeatherValue.Finite(v.y) && SurfaceWeatherValue.Finite(v.z);

        public float Weight(Vector3 point)
        {
            Vector3 min = Bounds.min, max = Bounds.max;
            float outside = Mathf.Max(0f, Mathf.Max(Mathf.Max(min.x - point.x, point.x - max.x),
                Mathf.Max(min.z - point.z, point.z - max.z)));
            if (Feather <= 0f) return outside <= 0f ? 1f : 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / Feather));
        }
    }
}
