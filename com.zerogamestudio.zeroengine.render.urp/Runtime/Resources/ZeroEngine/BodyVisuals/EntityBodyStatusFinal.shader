Shader "ZeroEngine/BodyVisuals/EntityBodyStatusFinal"
{
    Properties
    {
        [PerRendererData] _MainTex ("身体贴图", 2D) = "white" {}
        _FrostMask ("贴体霜纹遮罩", 2D) = "gray" {}
        _FlameAtlas ("火焰序列（4×4）", 2D) = "black" {}
        _FrostAmount ("结霜覆盖", Range(0,1)) = 0.72
        _FrostScale ("霜纹尺度", Range(0.2,4)) = 0.9
        _FireBackRegion ("背侧火焰（位置XY/尺寸WH）", Vector) = (0.30,0.65,0.32,0.4)
        _FireFrontRegion ("袖侧火焰（位置XY/尺寸WH）", Vector) = (0.74,0.44,0.14,0.21)
        [Toggle] _UseLocalizedFire ("局部序列帧火焰", Float) = 1
        _Tint ("效果颜色", Color) = (0.3,0.7,1,0.8)
        _Mode ("表现类型", Float) = 2
        _Intensity ("亮度", Float) = 1
        _Opacity ("透明度", Float) = 1
        _PatternScale ("兼容纹理密度", Float) = 9
        _MotionSpeed ("动画速度", Float) = 1
        _BodyClock ("实体时钟", Float) = 0
        [HideInInspector] _BodyPresence ("Presence", Float) = -1
        [HideInInspector] _BodyRetiring ("Retiring", Float) = 0
        _EffectPadding ("附着层留边（侧/上/下像素）", Vector) = (1,1,1,0)
        [HideInInspector] _BodyMask ("实时身体轮廓", 2D) = "black" {}
        [HideInInspector] _BodyPixelSize ("来源像素尺寸", Float) = 0.0625
        [HideInInspector] _BodySourceTexelSize ("原图贴图像素尺寸", Vector) = (1,1,1,1)
        [HideInInspector] _BodySourceUVRect ("Sprite原图范围", Vector) = (0,0,1,1)
        [HideInInspector] _BodyLayer ("表面/霜边/火焰/火星", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BodyMask); SAMPLER(sampler_BodyMask);
        TEXTURE2D(_FrostMask); SAMPLER(sampler_FrostMask);
        TEXTURE2D(_FlameAtlas); SAMPLER(sampler_FlameAtlas);
        CBUFFER_START(UnityPerMaterial)
            float4 _BodyBounds, _BodySourceTexelSize, _BodySourceUVRect, _EffectPadding, _Flip;
            float4 _FireBackRegion, _FireFrontRegion;
            float4x4 _BodyWorldToLocal;
            half4 _Tint, _RendererColor;
            float _Mode, _Intensity, _Opacity, _PatternScale, _MotionSpeed, _BodyClock, _BodyLayer;
            float _BodyPixelSize, _FrostAmount, _FrostScale, _UseLocalizedFire;
            float _BodyPresence, _BodyRetiring;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 body : TEXCOORD1; half4 color : COLOR; };
        Varyings vert(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz * _Flip.xyz);
            output.positionCS = TransformWorldToHClip(world);
            output.uv = input.uv;
            output.body = (mul(_BodyWorldToLocal, float4(world,1)).xy - _BodyBounds.xy) * _BodyBounds.zw + 0.5;
            output.color = input.color * _RendererColor;
            return output;
        }
        float maskAt(float2 body)
        {
            if (any(body < 0) || any(body > 1)) return 0;
            return step(0.5,SAMPLE_TEXTURE2D(_BodyMask,sampler_BodyMask,body).a);
        }
        half4 frag(Varyings input) : SV_Target
        {
            if (_BodyLayer > 2.5)
            {
                float2 emberOffset = abs(input.uv - 0.5);
                float a = 1 - smoothstep(0.28,0.48,emberOffset.x + emberOffset.y);
                return half4(input.color.rgb,a * input.color.a * _Opacity);
            }
            if (_BodyLayer > 1.5)
            {
                // Native particles select the 4x4 animation tile. Sample authored shapes on a small stable grid.
                float2 tile = min(floor(input.uv * 4),3);
                float2 q = frac(input.uv * 4);
                q = (floor(q * float2(14,18)) + 0.5) / float2(14,18);
                half3 flame = SAMPLE_TEXTURE2D_LOD(_FlameAtlas,sampler_FlameAtlas,(tile + q) * 0.25,0).rgb;
                float luminance = dot(flame,half3(0.25,0.60,0.15));
                float alpha = smoothstep(0.035,0.24,max(flame.r,max(flame.g,flame.b)));
                half3 color = lerp(half3(0.26,0.019,0.003),half3(0.85,0.13,0.009),smoothstep(0.06,0.56,luminance));
                color = lerp(color,half3(0.96,0.45,0.06),smoothstep(0.58,0.97,luminance));
                return half4(color * saturate(_Intensity),alpha * input.color.a * _Tint.a * _Opacity);
            }
            float2 pixel = _BodyPixelSize * _BodyBounds.zw;
            float growth = _BodyPresence < 0 ? smoothstep(0.05,1.25,_BodyClock) : smoothstep(0,1,_BodyPresence);
            if (_BodyLayer > 0.5)
            {
                if (_Mode > 2.5) return 0;
                float outside = 1 - maskAt(input.body);
                float upperEdge = maskAt(input.body - float2(0,pixel.y * 0.7));
                return half4(0.43,0.61,0.67,outside * upperEdge * growth * 0.45 * _Tint.a * _Opacity);
            }
            half4 source = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
            if (_Mode > 2.5)
            {
                float back = saturate(1 - length((input.body - _FireBackRegion.xy) / float2(0.23,0.2)));
                float front = saturate(1 - length((input.body - _FireFrontRegion.xy) / float2(0.13,0.16)));
                float heat = max(back,front) * growth;
                half3 warm = source.rgb * half3(1,0.72,0.51) + half3(0.07,0.012,0.002) * heat;
                return half4(warm,source.a * input.color.a * heat * 0.32 * _Tint.a * _Opacity);
            }
            // Sprite frame offsets are removed; mesh/Spine keeps authored UVs. The texture is static on the posed surface.
            float2 localUV = (input.uv - _BodySourceUVRect.xy) / max(_BodySourceUVRect.zw,float2(0.0001,0.0001));
            float2 uv = localUV * _FrostScale + float2(0.13,0.27);
            float frost = SAMPLE_TEXTURE2D(_FrostMask,sampler_FrostMask,uv).r;
            float upperRim = 1 - maskAt(input.body + float2(0,pixel.y));
            float deposit = smoothstep(0.26,0.65,frost + upperRim * 0.12);
            float face = (1 - smoothstep(0.11,0.21,abs(input.body.x - 0.6))) * smoothstep(0.72,0.82,input.body.y);
            float coverage = deposit * growth * _FrostAmount * (1 - face * 0.75);
            // The accepted steady frost stays intact. Local crystals connect on entry and
            // separate again on retirement instead of fading the whole coat uniformly.
            float crystalSeed = saturate(0.1 + (1 - frost) * 0.45 + localUV.y * 0.24 - upperRim * 0.1);
            if (_BodyPresence >= 0) coverage *= smoothstep(crystalSeed,crystalSeed + 0.18,_BodyPresence);
            float value = dot(source.rgb,half3(0.2126,0.7152,0.0722));
            half3 icy = lerp(half3(0.20,0.32,0.38),half3(0.66,0.77,0.79),saturate(value * 0.85 + frost * 0.24));
            float fine = SAMPLE_TEXTURE2D(_FrostMask,sampler_FrostMask,uv + float2(0.001,0.001)).r;
            float glint = saturate((frost - fine) * 7) * 0.14 + upperRim * deposit * 0.12;
            icy += half3(0.6,0.8,0.9) * glint;
            half3 color = lerp(source.rgb * half3(0.78,0.91,0.97),icy,0.77);
            return half4(saturate(color * _Intensity),source.a * input.color.a * coverage * _Tint.a * _Opacity);
        }
        Varyings maskVert(Attributes input)
        {
            Varyings output = vert(input);
            output.positionCS = float4(output.body * 2 - 1,0,1);
            #if UNITY_UV_STARTS_AT_TOP
                output.positionCS.y = -output.positionCS.y;
            #endif
            return output;
        }
        half4 maskFrag(Varyings input) : SV_Target
        {
            half alpha = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a * input.color.a;
            return half4(alpha,alpha,alpha,alpha);
        }
        ENDHLSL
        Pass
        {
            Name "BodyStatusFinal"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "BodyMask"
            Tags { "LightMode"="BodySilhouette" }
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex maskVert
            #pragma fragment maskFrag
            ENDHLSL
        }
    }
}
