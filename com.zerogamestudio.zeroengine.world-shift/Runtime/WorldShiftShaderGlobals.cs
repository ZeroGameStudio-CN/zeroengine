using UnityEngine;

namespace ZeroEngine.WorldShift
{
    public static class WorldShiftShaderGlobals
    {
        public static readonly int ScanOriginId = Shader.PropertyToID("_ZEWorldShiftOrigin");
        public static readonly int ScanRadiusId = Shader.PropertyToID("_ZEWorldShiftRadius");
        public static readonly int ScanWidthId = Shader.PropertyToID("_ZEWorldShiftWidth");
        public static readonly int ScanDirectionId = Shader.PropertyToID("_ZEWorldShiftDirection");
        public static readonly int ScanActiveId = Shader.PropertyToID("_ZEWorldShiftActive");
        public static readonly int ScanNoiseScaleId = Shader.PropertyToID("_ZEWorldShiftNoiseScale");
        public static readonly int ScanNoiseStrengthId = Shader.PropertyToID("_ZEWorldShiftNoiseStrength");
        public static readonly int ScanEdgeColorId = Shader.PropertyToID("_ZEWorldShiftEdgeColor");

        public static void SetActive(
            Vector3 origin,
            float radius,
            float width,
            float direction,
            float noiseScale,
            float noiseStrength,
            Color edgeColor)
        {
            Apply(origin, radius, width, direction, 1f, noiseScale, noiseStrength, edgeColor);
        }

        public static void SetInactive(
            Vector3 origin,
            float radius,
            float width,
            float direction,
            float noiseScale,
            float noiseStrength,
            Color edgeColor)
        {
            Apply(origin, radius, width, direction, 0f, noiseScale, noiseStrength, edgeColor);
        }

        private static void Apply(
            Vector3 origin,
            float radius,
            float width,
            float direction,
            float active,
            float noiseScale,
            float noiseStrength,
            Color edgeColor)
        {
            Shader.SetGlobalVector(ScanOriginId, origin);
            Shader.SetGlobalFloat(ScanRadiusId, radius);
            Shader.SetGlobalFloat(ScanWidthId, width);
            Shader.SetGlobalFloat(ScanDirectionId, direction);
            Shader.SetGlobalFloat(ScanActiveId, active);
            Shader.SetGlobalFloat(ScanNoiseScaleId, noiseScale);
            Shader.SetGlobalFloat(ScanNoiseStrengthId, noiseStrength);
            Shader.SetGlobalColor(ScanEdgeColorId, edgeColor);
        }
    }
}
