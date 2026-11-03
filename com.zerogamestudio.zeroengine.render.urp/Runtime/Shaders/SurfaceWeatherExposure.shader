Shader "Hidden/ZeroEngine/SurfaceWeatherExposure"
{
    Properties { _ZeroDepthTest ("Depth Test", Float) = 4 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest [_ZeroDepthTest]
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _ZeroCaptureU, _ZeroCaptureV, _ZeroCaptureAxis, _ZeroCaptureParams;
            TEXTURE2D(_ZeroCaptureBaseMap); SAMPLER(sampler_ZeroCaptureBaseMap);
            float4 _ZeroCaptureBaseST;
            float _ZeroCaptureAlphaClip, _ZeroCaptureCutoff;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float depth : TEXCOORD1; };
            V Vert(A input)
            {
                V o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float2 uv = float2(dot(float4(p, 1), _ZeroCaptureU), dot(float4(p, 1), _ZeroCaptureV));
                float depth = dot(p, _ZeroCaptureAxis.xyz);
                float z = saturate((depth - _ZeroCaptureAxis.w) / _ZeroCaptureParams.x);
                #if !UNITY_REVERSED_Z
                    z = 1.0 - z;
                    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3)
                        z = z * 2.0 - 1.0;
                    #endif
                #endif
                o.positionCS = float4((uv * 2.0 - 1.0) * float2(1, _ZeroCaptureParams.y), z, 1);
                o.uv = input.uv * _ZeroCaptureBaseST.xy + _ZeroCaptureBaseST.zw;
                o.depth = depth;
                return o;
            }
            float Frag(V i) : SV_Target
            {
                if (_ZeroCaptureAlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_ZeroCaptureBaseMap, sampler_ZeroCaptureBaseMap, i.uv).a - _ZeroCaptureCutoff);
                return i.depth;
            }
            ENDHLSL
        }
    }
}
