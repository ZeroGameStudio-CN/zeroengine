Shader "Hidden/ZeroEngine/WorldShift/StencilMask"
{
    Properties
    {
        [HideInInspector] _StencilRef("Stencil Ref", Float) = 64
        [HideInInspector] _StencilMask("Stencil Mask", Float) = 64
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ClearWorldShiftStencilBit"
            ColorMask 0
            Cull Off
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref 0
                ReadMask [_StencilMask]
                WriteMask [_StencilMask]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "WriteWorldShiftScreenScanMask"
            ColorMask 0
            Cull Off
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilMask]
                WriteMask [_StencilMask]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftReveal.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                return output;
            }

            float2 ProjectWorldToScreenUV(float3 positionWS)
            {
                float4 positionCS = TransformWorldToHClip(positionWS);
                float4 screenPosition = ComputeScreenPos(positionCS);
                return screenPosition.xy / max(screenPosition.w, 0.0001);
            }

            float ProjectWorldRadiusToScreenPixels(float3 originWS, float radius)
            {
                float2 originUV = ProjectWorldToScreenUV(originWS);
                float2 radiusUV = ProjectWorldToScreenUV(originWS + float3(max(radius, 0.0), 0.0, 0.0));
                return max(length((radiusUV - originUV) * _ScreenParams.xy), 0.001);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 originUV = ProjectWorldToScreenUV(_ZEWorldShiftOrigin.xyz);
                float2 originPixel = originUV * _ScreenParams.xy;
                float2 pixel = input.positionCS.xy;

                float radiusPixels = ProjectWorldRadiusToScreenPixels(_ZEWorldShiftOrigin.xyz, _ZEWorldShiftRadius);
                float distancePixels = length(pixel - originPixel);
                half insideMask = (half)step(distancePixels, radiusPixels);
                half targetMask = _ZEWorldShiftDirection >= 0.0h ? insideMask : 1.0h - insideMask;
                clip(targetMask - 0.5h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "WriteWorldShiftStencilMask"
            ColorMask 0
            Cull Off
            ZWrite Off
            ZTest LEqual

            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilMask]
                WriteMask [_StencilMask]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftReveal.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half targetMask = ZEWorldShiftBinaryTargetMask(input.positionWS, input.positionCS);
                clip(targetMask - 0.5h);
                return 0;
            }
            ENDHLSL
        }

    }
}
