using System;
using UnityEngine;

namespace ZeroEngine.Render
{
    public enum BodyVisualKind
    {
        [InspectorName("脉冲影子")] ShadowPulse = 0,
        [InspectorName("发光")] Glow = 1,
        [InspectorName("冰霜")] Frozen = 2,
        [InspectorName("灼烧")] Burning = 3,
        [InspectorName("中毒")] Poisoned = 4,
        [InspectorName("电流")] Electrified = 5,
        [InspectorName("流血")] Bleeding = 6,
        [InspectorName("虚弱 / 破绽")] Weakened = 7,
        [InspectorName("共振")] Resonating = 8,
        [InspectorName("冰冻外观")] FrozenSolid = 9
    }

    /// <summary>Rendering-owned presentation values; never apply damage, freeze or gameplay scale.</summary>
    [Serializable]
    public sealed class BodyVisualSettings
    {
        [InspectorName("身体效果")] public BodyVisualKind Kind;
        [InspectorName("外观模板"), Tooltip("可选特效材质；运行时复制使用，不修改资产。冰冻仅为外观，不会暂停角色动作。")] public Material AppearanceTemplate;
        [InspectorName("效果颜色"), ColorUsage(true, true)] public Color Color = new(0.16f, 0.07f, 0.28f, 0.65f);
        [InspectorName("亮度"), Range(0f, 4f)] public float Intensity = 1f;
        [InspectorName("包含当前武器"), Tooltip("默认只覆盖身体；开启后沿通用影子接口覆盖当前活动武器。显式 ShadowVisualSource 的 Renderer 列表始终优先。")] public bool IncludeWeapons;
        [InspectorName("小粒子"), Tooltip("冰霜/冰冻飘落小雪花，灼烧逸出小火星，流血滴落血滴；按实体本地时钟推进。同类合并，异类共享数量上限。")] public bool SmallParticles;
        [Header("影子脉冲")]
        [InspectorName("脉冲起点间隔"), Min(0.02f), Tooltip("两次胀大的起点间隔，必须大于单次时长；中间完全隐藏影子。")] public float PulseInterval = 1.2f;
        [InspectorName("单次脉冲时长"), Min(0.01f)] public float PulseDuration = 0.28f;
        [InspectorName("膨胀幅度"), Range(0f, 1f), Tooltip("围绕身体中心快速放大轮廓，0.35 表示最大放大 35%；不缩放角色或碰撞体。")] public float Expansion = 0.35f;
        [InspectorName("循环脉冲")] public bool Repeat = true;
        [Header("表面纹理")]
        [InspectorName("纹理密度"), Range(1f, 30f)] public float PatternScale = 9f;
        [InspectorName("纹理流动速度"), Range(0f, 5f), Tooltip("按实体本地时钟推进；冰霜为静态晶纹，灼烧向上流动。")] public float MotionSpeed = 1.4f;

        public bool IsIce => Kind == BodyVisualKind.Frozen || Kind == BodyVisualKind.FrozenSolid;
        public bool HasSmallParticles => SmallParticles && (IsIce || Kind == BodyVisualKind.Burning || Kind == BodyVisualKind.Bleeding);

        public bool IsValid => (int)Kind >= 0 && (int)Kind <= (int)BodyVisualKind.FrozenSolid &&
            InRange(Intensity, 0f, 4f) && InRange(Expansion, 0f, 1f) &&
            InRange(PatternScale, 1f, 30f) && InRange(MotionSpeed, 0f, 5f) &&
            InRange(Color.r, 0f, 8f) && InRange(Color.g, 0f, 8f) && InRange(Color.b, 0f, 8f) && InRange(Color.a, 0f, 1f) &&
            (Kind != BodyVisualKind.ShadowPulse ||
             InRange(PulseDuration, 0.01f, 60f) && InRange(PulseInterval, 0.02f, 600f) && PulseInterval > PulseDuration);

        public BodyVisualSettings Copy() => (BodyVisualSettings)MemberwiseClone();

        public static BodyVisualSettings For(BodyVisualKind kind)
        {
            var settings = new BodyVisualSettings { Kind = kind };
            settings.Color = kind switch
            {
                BodyVisualKind.Glow => new Color(0.3f, 0.85f, 1.6f, 0.75f),
                BodyVisualKind.Frozen => new Color(0.22f, 0.7f, 1f, 0.62f),
                BodyVisualKind.Burning => new Color(1.8f, 0.28f, 0.035f, 0.75f),
                BodyVisualKind.Poisoned => new Color(0.72f, 0.24f, 0.95f, 0.78f),
                BodyVisualKind.Electrified => new Color(0.32f, 0.7f, 1.25f, 0.86f),
                BodyVisualKind.Bleeding => new Color(0.8f, 0.055f, 0.035f, 0.95f),
                BodyVisualKind.Weakened => new Color(1f, 0.78f, 0.32f, 0.9f),
                BodyVisualKind.Resonating => new Color(0.35f, 0.95f, 0.82f, 0.8f),
                BodyVisualKind.FrozenSolid => new Color(0.38f, 0.8f, 1.1f, 0.86f),
                _ => settings.Color
            };
            settings.SmallParticles = kind == BodyVisualKind.Bleeding || kind == BodyVisualKind.FrozenSolid;
            return settings;
        }

        /// <summary>One short expansion/fade followed by a completely silent interval.</summary>
        public BodyPulseSample EvaluatePulse(float elapsed)
        {
            if (!IsValid || !InRange(elapsed, 0f, float.MaxValue)) return default;
            if (!Repeat && elapsed >= PulseDuration) return default;
            float phase = Repeat ? elapsed % PulseInterval : elapsed;
            if (phase >= PulseDuration) return default;
            float progress = phase / PulseDuration;
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(progress / 0.2f), 3f);
            return new BodyPulseSample(true, 1f + Expansion * rise, Mathf.Pow(1f - progress, 1.4f));
        }

        internal static bool InRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }

    public readonly struct BodyPulseSample
    {
        public bool Visible { get; }
        public float Scale { get; }
        public float Opacity { get; }
        public BodyPulseSample(bool visible, float scale, float opacity)
        {
            Visible = visible;
            Scale = scale;
            Opacity = opacity;
        }
    }
}
