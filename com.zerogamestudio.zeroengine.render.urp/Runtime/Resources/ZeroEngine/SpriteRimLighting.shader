Shader "Hidden/ZeroEngine/SpriteRimLighting"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D(_RimRefActorTexture); SAMPLER(sampler_RimRefActorTexture);
        #include "../../Shaders/SpriteRimComposition.hlsl"
        TEXTURE2D_X(_RimEnhanceSceneColor);
        float4 _RimEnhanceSceneScaleBias, _RimRefActorST;
        struct ActorAttributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct ActorVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float alpha : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        ActorVaryings ActorVert(ActorAttributes input)
        {
            ActorVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv; output.alpha = input.color.a;
            return output;
        }
        float2 CoveredArtCell(ActorVaryings input, out float2 cellOffset)
        {
            float2 sourceUV = input.uv * _RimRefActorST.xy + _RimRefActorST.zw;
            float alpha = SAMPLE_TEXTURE2D(_RimRefActorTexture, sampler_RimRefActorTexture, sourceUV).a * input.alpha;
            clip(alpha - .00001);
            float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
            float sceneDepth = SampleSceneDepth(screenUV);
            // Only recolor pixels already written by this opaque/alpha-clipped actor. Dissolved holes and occluders stay intact.
            clip(.00002 - abs(input.positionCS.z - sceneDepth));
            float2 texel = max(_RimRefMaskInfo.xy, .000001);
            float2 uv = float2(dot(float4(input.positionWS,1),_RimRefRow0), dot(float4(input.positionWS,1),_RimRefRow1));
            float2 cell = (floor(uv / texel) + .5) * texel;
            cellOffset = cell - uv;
            clip(MaskAlpha(cell) - .00001);
            return cell;
        }
        float4 EnhancedFrag(ActorVaryings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 cellOffset;
            float2 cell = CoveredArtCell(input, cellOffset);
            float3 right = TransformObjectToWorldDir(float3(1, 0, 0), false);
            float3 up = TransformObjectToWorldDir(float3(0, 1, 0), false);
            float3 cellWS = ZeroSpriteRimCellWorldPosition(input.positionWS, cellOffset, right, up);
            float2 sceneUV = GetNormalizedScreenSpaceUV(input.positionCS) * _RimEnhanceSceneScaleBias.xy + _RimEnhanceSceneScaleBias.zw;
            float4 original = SAMPLE_TEXTURE2D_X(_RimEnhanceSceneColor, sampler_PointClamp, sceneUV);
            original.rgb = ZeroComposeSpriteRim(original.rgb, cell, input.positionWS, cellWS);
            return original;
        }
        ENDHLSL
        Pass
        {
            Name "EnhancedDirectComposite"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ActorVert
            #pragma fragment EnhancedFrag
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
