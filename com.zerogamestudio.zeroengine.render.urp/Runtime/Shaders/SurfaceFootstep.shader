Shader "ZeroEngine/Render/SurfaceFootstep"
{
    Properties
    {
        _Color ("Ripple Color", Color) = (0.64, 0.87, 0.94, 0.55)
        _Thickness ("Ring Thickness", Range(0.01, 0.15)) = 0.045
        [HideInInspector] _FootstepStyle ("Preview Style", Float) = 0
        [HideInInspector] _PreviewTint ("Preview Tint", Color) = (1,1,1,1)
        [HideInInspector] _Tuned ("Use Profile", Float) = 0
        [HideInInspector] _FragmentCount ("Fragment Count", Float) = 7
        [HideInInspector] _FragmentSize ("Fragment Size", Float) = 1
        [HideInInspector] _Spread ("Spread", Float) = 1
        [HideInInspector] _SingleFragment ("Independent Fragments", Float) = 0
        [HideInInspector] _Glow ("Soft Glow Gain", Float) = 0
        [HideInInspector] _Shimmer ("Water Highlights", Float) = 0
        [HideInInspector] _WaveDetail ("Wave Detail", Float) = 0
        [HideInInspector] _WaveSpeed ("Wave Motion", Float) = 1
        [HideInInspector] _FadeHold ("Wave Fade Start", Float) = 0
        [HideInInspector] _Tumble ("Fragment Tumble", Float) = 0
        [HideInInspector] _ShapeVariation ("Shape Variation", Float) = 0
        [HideInInspector] _TravelDirection ("Sprint Travel Direction", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Thickness;
                float _FootstepStyle;
                half4 _PreviewTint;
                float _Tuned, _FragmentCount, _FragmentSize, _Spread;
                float _SingleFragment;
                float _Glow, _Shimmer, _WaveDetail, _WaveSpeed, _Tumble, _ShapeVariation;
                float _FadeHold;
                float _TravelDirection;
            CBUFFER_END
            // Tuned streams pack UV.xy, AgePercent.z and StableRandomX.w; no global clock.
            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                // Independent fragments carry their own varied tint; don't multiply the author tint twice.
                output.color = _SingleFragment > 0.5 ? input.color : input.color * lerp(_Color, _PreviewTint, _Tuned);
                return output;
            }
            #define vec2 float2
            #define vec3 float3
            #define vec4 float4
            #define mix lerp
            #define ze_atan2 atan2
            #include "Packages/com.zerogamestudio.zeroengine.render/Runtime/Shaders/SurfaceFootstepMath.glslinc"
            #undef vec2
            #undef vec3
            #undef vec4
            #undef mix
            #undef ze_atan2
            half4 frag(Varyings input) : SV_Target { return ze_surface_footstep(input.uv, input.color); }

            ENDHLSL
        }
    }
}
