Shader "Hidden/ZeroEngine/SpriteRimLighting"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D(_RimRefActorTexture); SAMPLER(sampler_RimRefActorTexture);
        TEXTURE2D(_RimRefMask); SAMPLER(sampler_RimRefMask);
        TEXTURE2D_X(_RimEnhanceSceneColor);
        float4 _RimEnhanceSceneScaleBias, _RimEnhanceInnerWrap;
        int _RimEnhanceLightCount;
        float4 _RimEnhanceRows0[4], _RimEnhanceRows1[4], _RimEnhanceRows2[4], _RimEnhanceColors[4];
        float4 _RimEnhanceShapes[4], _RimEnhancePowers[4], _RimEnhanceBands[4];
        float4 _RimRefActorST, _RimRefRow0, _RimRefRow1, _RimRefMaskInfo, _RimRefFrame, _RimRefStyle;
        float4 _RimRefEdgeTint, _RimRefInnerTint;
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
        float MaskAlpha(float2 uv)
        {
            if (any(uv < 0) || any(uv >= 1)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_RimRefMask, sampler_RimRefMask, uv, 0).r;
        }
        float FilteredMaskAlpha(float2 uv)
        {
            if (any(uv < 0) || any(uv >= 1)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_RimRefMask, sampler_LinearClamp, uv, 0).r;
        }
        float2 CoveredArtCell(ActorVaryings input)
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
            uv = (floor(uv / texel) + .5) * texel;
            clip(MaskAlpha(uv) - .00001);
            return uv;
        }
        float3 CompositeColor(float3 original, float4 light, float4 meta)
        {
            bool gammaBlend = fmod(meta.z, 2) > .5;
            bool limitAddedHdr = meta.z >= 2;
            float3 incomingHdr = limitAddedHdr ? max(original - 1, 0) : 0;
            float3 incomingBase = limitAddedHdr ? min(original, 1) : original;
            float3 baseColor = gammaBlend ? LinearToSRGB(incomingBase) : incomingBase;
            float3 dodge = baseColor / max(1 - light.rgb, .000001);
            // Saturated channels retain their own light color.
            dodge = float3(light.r >= 1 ? light.r : dodge.r, light.g >= 1 ? light.g : dodge.g, light.b >= 1 ? light.b : dodge.b);
            float3 color = min(lerp(baseColor, dodge, light.a), meta.y) * meta.x;
            if (limitAddedHdr) color = saturate(color);
            float3 response = gammaBlend ? SRGBToLinear(color) : color;
            if (limitAddedHdr)
            {
                // Match the incoming highlight peak so white art does not acquire a glowing white border.
                // The midtone floor still lets dark art receive light, while incoming HDR excess stays separate.
                float incomingPeak = max(max(incomingBase.r, incomingBase.g), incomingBase.b);
                float responsePeak = max(max(response.r, response.g), response.b);
                response *= min(1, max(incomingPeak, .25) / max(responsePeak, .000001));
            }
            return response + incomingHdr;
        }
        float EnhancedPower(float value, float power)
        {
            return power <= 0 ? 1 : pow(saturate(value), power);
        }
        float EnhancedFalloff(float3 p, float4 shape, float4 powers)
        {
            if (shape.x > 2.5)
            {
                float distanceSquared = dot(p, p);
                float attenuation = pow(saturate(1 - distanceSquared), 2);
                if (shape.x > 3.5)
                {
                    float cosine = p.z / max(length(p), .00001);
                    attenuation *= saturate((cosine - shape.y) / max(shape.z - shape.y, .00001));
                }
                return attenuation;
            }
            float2 q = p.xy;
            float distance = length(q);
            if (shape.x < .5)
                return distance >= 1 ? 0 : EnhancedPower(1 - distance, powers.x);
            if (abs(q.x) > 1 || q.y > 0 || q.y < -1) return 0;
            if (shape.x < 1.5)
                return EnhancedPower(1 - abs(q.x), powers.y) * EnhancedPower(1 - abs(q.y), powers.z);
            float angle = abs(atan2(q.x, -q.y));
            return distance >= 1 || angle > shape.y ? 0 :
                EnhancedPower(1 - angle / max(shape.y, .00001), powers.w) * EnhancedPower(1 - distance, powers.x);
        }
        float4 EnhancedFrag(ActorVaryings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = CoveredArtCell(input);
            float2 texel = max(_RimRefMaskInfo.xy, .000001);
            float3 energy = 0; float blend = 0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                if (i >= _RimEnhanceLightCount) break;
                float3 q = float3(dot(float4(input.positionWS, 1), _RimEnhanceRows0[i]),
                    dot(float4(input.positionWS, 1), _RimEnhanceRows1[i]),
                    dot(float4(input.positionWS, 1), _RimEnhanceRows2[i]));
                float falloff = EnhancedFalloff(q, _RimEnhanceShapes[i], _RimEnhancePowers[i]);
                float alpha = falloff * _RimEnhanceColors[i].a;
                if (_RimEnhanceShapes[i].x > 2.5) alpha = saturate(alpha);
                float amount = alpha * alpha;
                float4 band = _RimEnhanceBands[i];
                float2 scale = (_RimRefFrame.zw + band.z) / max(_RimRefFrame.zw, 1);
                float2 shifted = (uv - _RimRefFrame.xy) / max(scale, .00001) + _RimRefFrame.xy - band.xy * texel;
                // Filter only contour coverage, never source art: every art cell retains a uniform shade.
                float occupied = FilteredMaskAlpha(shifted);
                float4 tint = lerp(_RimRefEdgeTint, _RimRefInnerTint, occupied);
                float3 modulation = tint.rgb * tint.a;
                if (_RimEnhanceInnerWrap.y > 0 && occupied > 0)
                {
                    float2 inward = band.xy / max(length(band.xy), .00001) * texel;
                    int steps = (int)_RimEnhanceInnerWrap.x;
                    float coverage = 0;
                    [unroll] for (int tap = 1; tap <= 4; tap++)
                    {
                        if (tap > steps) break;
                        coverage += saturate(occupied - FilteredMaskAlpha(shifted - inward * tap));
                    }
                    // Averaged contour shells fade inward per art cell and add nothing to the bright outer core.
                    modulation += _RimRefEdgeTint.rgb * _RimRefEdgeTint.a *
                        (_RimEnhanceInnerWrap.y * coverage / max(steps, 1));
                }
                energy += saturate(_RimEnhanceColors[i].rgb * amount) * modulation * band.w;
                blend += amount;
            }
            float2 sceneUV = GetNormalizedScreenSpaceUV(input.positionCS) * _RimEnhanceSceneScaleBias.xy + _RimEnhanceSceneScaleBias.zw;
            float4 original = SAMPLE_TEXTURE2D_X(_RimEnhanceSceneColor, sampler_PointClamp, sceneUV);
            float4 light = float4(saturate(energy), saturate(blend) * _RimRefStyle.z);
            original.rgb = CompositeColor(original.rgb, light, float4(_RimRefStyle.x, _RimRefStyle.y, _RimRefStyle.w, 1));
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
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
