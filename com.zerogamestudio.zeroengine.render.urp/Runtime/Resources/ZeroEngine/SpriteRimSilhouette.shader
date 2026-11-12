Shader "Hidden/ZeroEngine/SpriteRimSilhouette"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_RimCaptureTexture); SAMPLER(sampler_RimCaptureTexture);
            float4x4 _RimCaptureMatrix;
            float4 _RimCaptureST;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half alpha : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS);
                output.positionCS = mul(_RimCaptureMatrix, float4(world, 1));
                output.uv = input.uv * _RimCaptureST.xy + _RimCaptureST.zw;
                output.alpha = input.color.a;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_RimCaptureTexture, sampler_RimCaptureTexture, input.uv).a * input.alpha;
                return a.xxxx;
            }
            ENDHLSL
        }
    }
}
