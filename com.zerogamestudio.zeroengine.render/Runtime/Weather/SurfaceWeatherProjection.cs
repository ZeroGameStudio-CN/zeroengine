using System;
using UnityEngine;

namespace ZeroEngine.Render.Weather
{
    public readonly struct SurfaceWeatherProjection
    {
        public SurfaceWeatherProjection(Bounds bounds, Vector3 skyDirection, int resolution, float bias)
        {
            if (!Finite(skyDirection) || !Finite(bounds.center) || !Finite(bounds.size)
                || !SurfaceWeatherValue.Finite(bias) || bias < 0f
                || skyDirection.sqrMagnitude < .01f || resolution < 16 || resolution > 2048)
                throw new ArgumentException("An exposure projection requires a direction and bounded resolution.");
            Axis = skyDirection.normalized;
            Vector3 hint = Mathf.Abs(Vector3.Dot(Axis, Vector3.up)) > .98f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(hint, Axis).normalized;
            Vector3 up = Vector3.Cross(Axis, right).normalized;
            float u0 = float.PositiveInfinity, u1 = float.NegativeInfinity;
            float v0 = u0, v1 = u1, d0 = u0, d1 = u1;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                float u = Vector3.Dot(p, right), v = Vector3.Dot(p, up), d = Vector3.Dot(p, Axis);
                u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                d0 = Mathf.Min(d0, d); d1 = Mathf.Max(d1, d);
            }
            float width = Mathf.Max(1f, u1 - u0 + 2f), height = Mathf.Max(1f, v1 - v0 + 2f);
            U = new Vector4(right.x / width, right.y / width, right.z / width, -(u0 - 1f) / width);
            V = new Vector4(up.x / height, up.y / height, up.z / height, -(v0 - 1f) / height);
            Depth = new Vector4(Axis.x, Axis.y, Axis.z, d0 - 1f);
            DepthSpan = Mathf.Max(1f, d1 - d0 + 2f);
            Bias = Mathf.Max(bias, Mathf.Max(width, height) / resolution * .85f);
            Resolution = resolution;
        }

        public Vector3 Axis { get; }
        public Vector4 U { get; }
        public Vector4 V { get; }
        public Vector4 Depth { get; }
        public float DepthSpan { get; }
        public float Bias { get; }
        public int Resolution { get; }
        public Vector2 UV(Vector3 p) => new(Dot(U, p), Dot(V, p));
        private static float Dot(Vector4 row, Vector3 p) => row.x * p.x + row.y * p.y + row.z * p.z + row.w;
        private static bool Finite(Vector3 v) => SurfaceWeatherValue.Finite(v.x)
            && SurfaceWeatherValue.Finite(v.y) && SurfaceWeatherValue.Finite(v.z);
    }

    /// <summary>CPU copy of the exact published GPU exposure generation.</summary>
    public sealed class SurfaceWeatherExposureFrame
    {
        private readonly float[] _depths;
        public SurfaceWeatherProjection Projection { get; }

        public SurfaceWeatherExposureFrame(SurfaceWeatherProjection projection, float[] depths)
        {
            if (depths == null || depths.Length != projection.Resolution * projection.Resolution)
                throw new ArgumentException("Exposure pixels do not match their projection.");
            Projection = projection;
            _depths = depths;
        }

        public float Sample(Vector3 position)
        {
            Vector2 uv = Projection.UV(position);
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return 1f;
            float x = uv.x * Projection.Resolution - .5f, y = uv.y * Projection.Resolution - .5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float depth = Vector3.Dot(position, Projection.Axis) + Projection.Bias;
            return Mathf.Lerp(Mathf.Lerp(Visible(ix, iy, depth), Visible(ix + 1, iy, depth), fx),
                Mathf.Lerp(Visible(ix, iy + 1, depth), Visible(ix + 1, iy + 1, depth), fx), fy);
        }

        private float Visible(int x, int y, float depth)
        {
            int size = Projection.Resolution;
            return depth >= _depths[Mathf.Clamp(y, 0, size - 1) * size + Mathf.Clamp(x, 0, size - 1)] ? 1f : 0f;
        }
    }
}
