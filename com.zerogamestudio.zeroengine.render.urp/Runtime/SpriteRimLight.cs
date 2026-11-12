using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Optional explicit 2D field. Native scene lights opt in separately through SpriteRimLightSource.</summary>
    [MovedFrom(true, "POB", "POB.Rendering", "SpriteRimReferenceLight")]
    [DisallowMultipleComponent, AddComponentMenu("ZeroEngine/Rendering/二维边缘受光灯")]
    public sealed class SpriteRimLight : MonoBehaviour
    {
        [InspectorName("灯形状")] public LightType Shape = LightType.Point;
        [InspectorName("二维半径")] public Vector2 Radius = new(4f, 4f);
        [ColorUsage(true, true), InspectorName("灯色")] public Color LightColor = Color.white;
        [Min(0f), InspectorName("强度乘子")] public float Intensity = 1f;
        [Min(0f)] public float LightPower = 1f;
        [Min(0f)] public float HorizontalPower = 1f, VerticalPower = 1f;
        [Range(.1f, 90f)] public float SpotHalfAngle = 45f;
        [Min(0f)] public float RangePower = 1f;
        [Range(.1f, 1f)] public float MinimumGlobalExposure = 1f;
        [InspectorName("影响轮廓方向")] public bool AffectsRim = true;

        private bool hasMatrix;
        private Matrix4x4 cachedTransform, cachedWorldToLight;
        private Vector2 cachedRadius;
        internal Matrix4x4 WorldToLight
        {
            get
            {
                var world = transform.localToWorldMatrix;
                var radius = new Vector2(Mathf.Max(.0001f, Mathf.Abs(Radius.x)), Mathf.Max(.0001f, Mathf.Abs(Radius.y)));
                if (!hasMatrix || !cachedTransform.Equals(world) || cachedRadius != radius)
                {
                    cachedTransform = world; cachedRadius = radius; hasMatrix = true;
                    cachedWorldToLight = (world * Matrix4x4.Scale(new Vector3(radius.x, radius.y, 1))).inverse;
                }
                return cachedWorldToLight;
            }
        }

        public bool TryGetInfluence(Vector3 position, out Vector2 awayDirection, out float intensity)
        {
            intensity = 0;
            awayDirection = new Vector2(position.x - transform.position.x, position.y - transform.position.y);
            if (!isActiveAndEnabled || !AffectsRim || !float.IsFinite(Intensity) || Intensity <= 0) return false;
            var local = WorldToLight.MultiplyVector(new Vector3(awayDirection.x, awayDirection.y, 0));
            awayDirection.Normalize();
            float distance = local.magnitude;
            if (Shape == LightType.Point)
            {
                if (distance >= 1) return false;
                intensity = Mathf.Pow(1 - distance, Mathf.Max(0, LightPower));
            }
            else if (Shape == LightType.Directional)
            {
                float x = 1 - Mathf.Abs(local.x), y = 1 - Mathf.Abs(local.y);
                if (x <= 0 || y <= 0 || local.y > 0) return false;
                float angle = transform.eulerAngles.z * Mathf.Deg2Rad;
                awayDirection = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
                intensity = Mathf.Pow(x, Mathf.Max(0, HorizontalPower)) * Mathf.Pow(y, Mathf.Max(0, VerticalPower));
            }
            else if (Shape == LightType.Spot)
            {
                float angle = Mathf.Abs(Mathf.Atan2(local.x, -local.y)) * Mathf.Rad2Deg;
                float limit = Mathf.Clamp(SpotHalfAngle, .1f, 90f);
                if (angle > limit || distance >= 1) return false;
                intensity = Mathf.Pow(1 - angle / limit, Mathf.Max(0, RangePower)) * Mathf.Pow(1 - distance, Mathf.Max(0, LightPower));
            }
            else return false;
            intensity = Mathf.Clamp01(intensity * Intensity);
            return float.IsFinite(intensity) && intensity > 0;
        }
    }
}
