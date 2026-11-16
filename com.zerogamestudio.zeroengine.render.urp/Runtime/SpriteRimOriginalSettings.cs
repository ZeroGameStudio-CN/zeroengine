using System;
using UnityEngine;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Numeric modes used by the recovered sprite light blend. Values are part of the shader contract.</summary>
    public enum SpriteRimOriginalBlend { Disabled, Normal, Overlay, Screen, Multiply, Add, ColorDodge, Lighten, Darken }

    /// <summary>
    /// Reconstructed PlayerRim preset. Distances use the original uniform units (100 texture texels),
    /// not ZE art-cell widths. This model intentionally retains additive light-field saturation.
    /// </summary>
    [Serializable]
    public sealed class SpriteRimOriginalSettings
    {
        public SpriteRimOriginalBlend Blend = SpriteRimOriginalBlend.ColorDodge;
        public float LightBlend = 1, MaximumLight = 3;
        public float MaximumSelectionDistance = 4;
        public Color OuterColor = Color.white, InnerColor = Color.black;
        public float MinimumDistance = .04f, MaximumDistance = .015f, DistancePower = .5f;
        public float MinimumScale, MaximumScale = -.05f, ScalePower = .5f;
        public float MinimumAlpha, MaximumAlpha = 1, AlphaPower = .5f;
        public bool UseLocalExposure = true;
        public float MinimumLocalExposure = .9f, GlobalExposure = 1;
        public float Exposure, ExposureThreshold = 1, ExposureOffset;

        public float Distance(float intensity) => Mathf.Lerp(MinimumDistance, MaximumDistance, Mathf.Pow(Mathf.Clamp01(intensity), DistancePower));
        public float Scale(float intensity) => Mathf.Lerp(MinimumScale, MaximumScale, Mathf.Pow(Mathf.Clamp01(intensity), ScalePower));
        public float Alpha(float intensity) => Mathf.Lerp(MinimumAlpha, MaximumAlpha, Mathf.Pow(Mathf.Clamp01(intensity), AlphaPower));
    }
}
