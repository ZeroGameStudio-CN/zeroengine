Shader "Hidden/ZeroEngine/SpriteRimOriginal"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "SpriteRimOriginalCore.hlsl"
        TEXTURE2D(_RimOriginalTexture); SAMPLER(sampler_RimOriginalTexture);
        TEXTURE2D(_RimOriginalMask);
        TEXTURE2D_X(_RimOriginalField);
        float4 _RimOriginalTexture_TexelSize, _RimOriginalST, _RimOriginalTint;
        float4 _RimOriginalFrame, _RimOriginalBand, _RimOriginalOuter, _RimOriginalInner;
        float4 _RimOriginalBlend, _RimOriginalExposure;
        float4 _RimOriginalRow0, _RimOriginalRow1, _RimOriginalMaskInfo;
        float4 _RimOriginalSourceColor, _RimOriginalSourcePowers;
        float _RimOriginalSourceShape, _RimOriginalSourceAngle, _RimOriginalSprite;
        struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float4 color:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.world = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(output.world);
            output.uv = input.uv; output.color = input.color;
            return output;
        }
        float4 Field(Varyings input):SV_Target
        {
            float4 result = OriginalRimField(input.uv, _RimOriginalSourceColor, _RimOriginalSourcePowers,
                (int)_RimOriginalSourceShape, _RimOriginalSourceAngle);
            if (!(result.a > 0)) discard;
            return result;
        }
        float4 Actor(Varyings input):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 screen = GetNormalizedScreenSpaceUV(input.positionCS);
            clip(.00002 - abs(input.positionCS.z - SampleSceneDepth(screen)));
            float2 sourceUV = input.uv * _RimOriginalST.xy + _RimOriginalST.zw;
            float4 texel = SAMPLE_TEXTURE2D(_RimOriginalTexture, sampler_RimOriginalTexture, sourceUV);
            if (texel.a == 0) discard;
            #ifndef UNITY_COLORSPACE_GAMMA
            if (_RimOriginalBand.w > .5) texel.rgb = LinearToSRGB(texel.rgb);
            #endif
            float4 original = texel * _RimOriginalTint;
            float occupied;
            if (_RimOriginalSprite > .5)
            {
                float2 shifted = OriginalRimShift(sourceUV, _RimOriginalFrame.xy, _RimOriginalFrame.zw,
                    _RimOriginalBand.z, _RimOriginalBand.xy, _RimOriginalTexture_TexelSize.xy);
                occupied = original.a * SAMPLE_TEXTURE2D(_RimOriginalTexture, sampler_RimOriginalTexture, shifted).a;
            }
            else
            {
                // Posed-mesh adapter: sample the current pose instead of shifting a whole texture atlas.
                float2 uv = float2(dot(float4(input.world,1),_RimOriginalRow0), dot(float4(input.world,1),_RimOriginalRow1));
                float2 step = _RimOriginalMaskInfo.xy;
                float2 scale = (_RimOriginalFrame.zw + _RimOriginalBand.z) / _RimOriginalFrame.zw;
                float2 shifted = (uv-_RimOriginalFrame.xy)/scale + _RimOriginalFrame.xy - _RimOriginalBand.xy*step*100;
                occupied = original.a * (any(shifted<0)||any(shifted>=1) ? 0 : SAMPLE_TEXTURE2D_LOD(_RimOriginalMask,sampler_PointClamp,shifted,0).r);
                original *= input.color;
            }
            float4 light = SAMPLE_TEXTURE2D_X(_RimOriginalField,sampler_LinearClamp,screen);
            float4 modulation = occupied > 0 ? _RimOriginalInner : _RimOriginalOuter;
            light.rgb *= modulation.rgb * modulation.a;
            float3 color = OriginalRimBlend(original.rgb,light,(int)_RimOriginalBlend.x,_RimOriginalBlend.y,_RimOriginalBlend.z,
                _RimOriginalExposure.x,_RimOriginalExposure.y,_RimOriginalExposure.z,_RimOriginalExposure.w);
            #ifndef UNITY_COLORSPACE_GAMMA
            color = SRGBToLinear(color);
            #endif
            if (!(original.a > 0)) discard;
            return float4(color * original.a, original.a);
        }
        ENDHLSL
        Pass
        {
            Name "OriginalLightField"
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Field
            ENDHLSL
        }
        Pass
        {
            Name "OriginalSprite"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Actor
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
