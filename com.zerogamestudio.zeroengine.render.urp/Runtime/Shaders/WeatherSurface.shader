Shader "ZeroEngine/Render/WeatherSurface"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Dry Smoothness", Range(0, 1)) = .5
        _Metallic ("Metallic", Range(0, 1)) = 0
        _SurfaceWetness ("Wetness", Range(0, 1)) = 0
        _SurfaceSnow ("Snow", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Cutoff ("Cutoff", Range(0, 1)) = .5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.zerogamestudio.zeroengine.render/Runtime/Shaders/SurfaceWeatherMath.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor;
                float _SurfaceWetness, _SurfaceSnow, _Smoothness, _Metallic;
                int _SurfaceShelterCount;
                float4 _SurfaceShelterMin[8], _SurfaceShelterMax[8];
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float4 groundLayer = ZeroApplySurfaceWeather(albedo, _Smoothness, input.positionWS,
                    normal, _SurfaceWetness, _SurfaceSnow, _SurfaceShelterCount,
                    _SurfaceShelterMin, _SurfaceShelterMax);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normal;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                float skyWeight = saturate(normal.y), groundWeight = saturate(-normal.y);
                float3 ambient = unity_AmbientSky.rgb * skyWeight + unity_AmbientGround.rgb * groundWeight
                    + unity_AmbientEquator.rgb * (1 - max(skyWeight, groundWeight));
                lighting.bakedGI = max(SampleSH(normal), ambient);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = groundLayer.rgb;
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);
                surface.metallic = _Metallic;
                surface.smoothness = groundLayer.a;
                surface.occlusion = 1;
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, ComputeFogFactor(TransformWorldToHClip(input.positionWS).z));
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
