using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Render.Urp
{
    [MovedFrom(true, "POB", "POB.Rendering", "SpriteRimLightingProfile")]
    [CreateAssetMenu(menuName = "ZeroEngine/Rendering/Pixel Rim Lighting", fileName = "PixelRimLighting")]
    public sealed class SpriteRimLightingProfile : ScriptableObject
    {
        [Range(0f, 1f), InspectorName("受光强度")] public float Strength = .85f;
        [Min(0f), InspectorName("弱光边带（美术像素）")] public float WeakDistance = 2f;
        [Min(0f), InspectorName("强光边带（美术像素）")] public float StrongDistance = 1f;
        [Min(.001f), InspectorName("灯强响应曲线指数")] public float ResponsePower = .5f;
        [InspectorName("强光轮廓缩放（美术像素）")] public float StrongScale = -.05f;
        [ColorUsage(true, true), InspectorName("边带调制色")] public Color EdgeTint = Color.white;
        [ColorUsage(true, true), InspectorName("内部调制色")] public Color InnerTint = Color.black;
        [Min(.01f), InspectorName("合成颜色上限")] public float MaximumLight = 3f;
        [Range(.01f, 1f), InspectorName("最小局部曝光")] public float MinimumExposure = 1f;
        [InspectorName("Gamma 空间混色")] public bool GammaBlend = true;
        [Tooltip("Preserve incoming highlights and HDR emission while limiting added Bloom.")]
        [InspectorName("保留原有 HDR，限制新增泛光")] public bool LimitAddedHdr = true;
        [Range(1, 4), InspectorName("绕光宽度（美术像素）")] public int InnerWrapWidth = 2;
        [Tooltip("Filters only silhouette coverage. Source artwork remains point sampled."), Range(0f, .35f)]
        [InspectorName("内侧绕光强度")] public float InnerWrapStrength = .05f;
    }
}
