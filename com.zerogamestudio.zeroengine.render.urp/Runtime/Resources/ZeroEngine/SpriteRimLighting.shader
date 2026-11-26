Shader "Hidden/ZeroEngine/SpriteRimLighting"
{
    Properties
    {
        [HideInInspector] _RimPrimaryWeights("Primary light weights", Vector) = (1,0,0,0)
        [HideInInspector] _RimPrimaryDirection("Primary contour direction", Vector) = (1,1,1,0)
        [HideInInspector] _RimLightPriorities("Light priorities at actor center", Vector) = (0,0,0,0)
        [HideInInspector] _RimReferenceBandLimits("Reference band limits and exposure", Vector) = (1,1,0,1)
    }
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
        TEXTURE2D(_RimRefMask); SAMPLER(sampler_RimRefMask);
        TEXTURE2D_X(_RimEnhanceSceneColor);
        float4 _RimEnhanceSceneScaleBias, _RimEnhanceInnerWrap;
        float4 _RimPrimaryWeights, _RimPrimaryDirection, _RimLightPriorities;
        float4 _RimReferenceBandLimits;
        static const float ReferenceFillStrength = .40;
        int _RimEnhanceLightCount;
        float4 _RimEnhanceRows0[4], _RimEnhanceRows1[4], _RimEnhanceRows2[4], _RimEnhanceColors[4];
        float4 _RimEnhanceShapes[4], _RimEnhancePowers[4], _RimEnhanceBands[4];
        // Native source position and inverse range, already known when selecting the lamps.
        float4 _RimNativeGeometry[4];
        // Positive w selects a URP shadow index. Negative w selects a box and encodes its opacity.
        float4 _RimEnhanceShadows[4];
        float4x4 _RimEnhanceFallbackBoxes[4];
        float _RimEnhanceAdditionalShadowDistanceSq;
        float _RimEnhanceContourStrength;
        float _RimReferenceEdgeResponse;
        float4 _RimRefActorST, _RimRefRow0, _RimRefRow1, _RimRefMaskInfo, _RimRefFrame, _RimRefStyle;
        float4 _RimRefEdgeTint, _RimRefInnerTint;
        float3 _RimRefCenterWS;
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
        float BackContourCoverage(float2 uv, float2 step)
        {
            return min(min(FilteredMaskAlpha(uv + float2(step.x, 0)), FilteredMaskAlpha(uv - float2(step.x, 0))),
                min(FilteredMaskAlpha(uv + float2(0, step.y)), FilteredMaskAlpha(uv - float2(0, step.y))));
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

        float3 IndependentColorDodge(float3 baseColor, float3 field)
        {
            float3 dodge = baseColor / max(1 - field, .000001);
            return float3(field.r >= 1 ? field.r : dodge.r,
                field.g >= 1 ? field.g : dodge.g, field.b >= 1 ? field.b : dodge.b);
        }

        #if defined(ZE_RIM_NATIVE_REFERENCE)
        float3 CompositeNativeDodge(float3 original, float4 field, float3 tint, float exposure)
        {
            // The compatibility renderer writes these factors to an RGBA16F attachment.
            float4 light = f16tof32(f32tof16(float4(field.rgb * tint, field.a * _RimRefStyle.z)));
            if (all(light.rgb <= 0)) return original;
            bool limitAddedHdr = _RimRefStyle.w >= 2;
            bool gammaBlend = fmod(_RimRefStyle.w, 2) > .5;
            float3 incomingBase = limitAddedHdr ? min(original, 1) : original;
            float3 baseColor = gammaBlend ? LinearToSRGB(incomingBase) : incomingBase;
            float3 dodge = IndependentColorDodge(baseColor, light.rgb);
            float3 color = min(lerp(baseColor, dodge, light.a), _RimRefStyle.y);
            if (limitAddedHdr) color = saturate(color);
            float3 lit = gammaBlend ? SRGBToLinear(color) : color;
            return original + (lit - incomingBase) * exposure;
        }

        float ReferenceSideWeight(float2 offset, float2 texel)
        {
            float2 offsetCells = float2(dot(offset, _RimRefRow0.xy), dot(offset, _RimRefRow1.xy)) / texel;
            return smoothstep(.25, .75, length(offsetCells));
        }

        float3 FitNativeGain(float3 gain, float3 scene)
        {
            if (_RimRefStyle.w < 2) return gain;
            float3 headroom = max(1 - scene, 0);
            float3 fit = min(1, headroom / max(gain, .000001));
            return gain * min(fit.r, min(fit.g, fit.b));
        }

        float NativeRearFacing(float3 toward, float3 view)
        {
            return saturate(-dot(toward, view) * rsqrt(max(dot(toward, toward), .00000001)));
        }

        float3 CompositeOneSide(float3 original, float4 field, float2 uv, float2 texel, float2 away, float score,
            float rearFacing, float opposingAlpha, float3 keyHue, out float coverage)
        {
            float response = pow(score, 1 / max(_RimEnhanceInnerWrap.w, .001));
            float distance = lerp(_RimReferenceBandLimits.x, _RimReferenceBandLimits.y, response);
            float2 direction = float2(dot(away, normalize(_RimRefRow0.xyz).xy), dot(away, normalize(_RimRefRow1.xyz).xy));
            float2 artDirection = direction / max(length(direction), .00001);
            float2 artStep = artDirection * texel;
            float2 scale = (_RimRefFrame.zw + _RimReferenceBandLimits.z * response) / max(_RimRefFrame.zw, 1);
            float2 shifted = (uv - _RimRefFrame.xy) / max(scale, .00001) + _RimRefFrame.xy - direction * distance * texel;
            float exposure = lerp(1, _RimReferenceBandLimits.w, score);
            float edgeExposure = exposure;
            float4 tint = _RimRefEdgeTint;
            bool keyEdge = MaskAlpha(shifted) <= 0;
            coverage = keyEdge ? 1 : 0;
            if (!keyEdge)
            {
                tint = _RimRefInnerTint;
                coverage = any(tint.rgb * tint.a > 0) ? 1 : 0;
                if (_RimEnhanceInnerWrap.y > 0)
                {
                    float wrap = _RimEnhanceInnerWrap.y * (1 - FilteredMaskAlpha(shifted - artStep));
                    if (wrap > 0)
                    {
                        tint = _RimRefEdgeTint;
                        edgeExposure *= wrap;
                        coverage = 1;
                    }
                }
            }

            float3 lit = CompositeNativeDodge(original, field, tint.rgb * (tint.a * response), edgeExposure);
            float farSide = dot((uv - _RimRefFrame.xy) / texel, artDirection);
            float rearWeight = smoothstep(.05, .65, rearFacing) * smoothstep(0, 2.5, farSide);
            float backWrap = _RimEnhanceInnerWrap.y * rearWeight;
            UNITY_BRANCH
            if (rearWeight <= 0 || (backWrap <= 0 && opposingAlpha <= 0)) return lit;

            // Light-facing details on the far half and the inner cell of a back edge
            // retain the key hue. The outer edge stays available for the opposing fill.
            float contour = keyEdge ? 1 :
                backWrap > 0 ? saturate(FilteredMaskAlpha(uv + artStep) - FilteredMaskAlpha(uv + 2 * artStep)) : 0;
            float fieldPeak = max(field.r, max(field.g, field.b));
            float3 hue = field.rgb / max(fieldPeak, .000001);
            bool gammaBlend = fmod(_RimRefStyle.w, 2) > .5;
            if (gammaBlend) hue = SRGBToLinear(hue);
            float materialWeight = lerp(1, .55, smoothstep(.03, .35, Luminance(max(original, 0))));
            float edgeWeight = contour * materialWeight * _RimRefStyle.z * exposure * _RimRefEdgeTint.a;
            float3 reflected = FitNativeGain(hue * _RimRefEdgeTint.rgb * (backWrap * fieldPeak * edgeWeight), original);
            float3 existing = max(lit - original, 0);
            if (max(reflected.r, max(reflected.g, reflected.b)) > max(existing.r, max(existing.g, existing.b)))
            {
                lit = original + reflected;
                coverage = 1;
            }

            // Both visible fields can strengthen this narrow far contour in the key's hue.
            float support = .26 * field.a * saturate(opposingAlpha) * rearWeight * edgeWeight;
            if (support > 0)
            {
                float3 sourceHue = gammaBlend ? SRGBToLinear(keyHue) : keyHue;
                lit += FitNativeGain(sourceHue * _RimRefEdgeTint.rgb * support, lit);
                coverage = 1;
            }
            return lit;
        }

        float3 MixNativeKeyAndFill(float3 key, float3 fill, float3 original)
        {
            float keyPeak = max(key.r, max(key.g, key.b));
            float fillPeak = max(fill.r, max(fill.g, fill.b));
            // Mix the hues where the two edges meet. Preserve the stronger peak without adding both.
            float3 gain = (key * keyPeak + fill * fillPeak) *
                (max(keyPeak, fillPeak) / max(keyPeak * keyPeak + fillPeak * fillPeak, .00000001));
            return FitNativeGain(gain, original);
        }
        #endif

        float3 CompositeColor(float3 original, float4 light, float3 darkEdgeLight, float3 contourLight, float4 meta)
        {
            bool gammaBlend = fmod(meta.z, 2) > .5;
            bool limitAddedHdr = meta.z >= 2;
            float3 incomingHdr = limitAddedHdr ? max(original - 1, 0) : 0;
            float3 incomingBase = limitAddedHdr ? min(original, 1) : original;
            float3 baseColor = gammaBlend ? LinearToSRGB(incomingBase) : incomingBase;
            float contourStrength = _RimEnhanceLightCount > 0 && _RimEnhanceShapes[0].x > 2.5 ? _RimEnhanceContourStrength : 0;
            UNITY_BRANCH
            if (_RimReferenceEdgeResponse > .5)
            {
                // Reference response over the already shaded scene. Texture alpha only
                // determines the contour; it must never replace the actor's base color.
                if (all(light.rgb <= 0)) return original;
            #if defined(ZE_RIM_NATIVE_REFERENCE)
                float3 referenceDodge = IndependentColorDodge(baseColor, light.rgb);
            #else
                float3 referenceDodge = baseColor / max(1 - light.rgb, .000001);
                // Keep the existing explicit 2D field response.
                referenceDodge = float3(light.r >= 1 ? light.r : referenceDodge.r,
                    light.g >= 1 ? (light.r > 0 ? light.r : light.g) : referenceDodge.g,
                    light.b >= 1 ? (light.r > 0 ? light.r : light.b) : referenceDodge.b);
            #endif
                float3 referenceColor = min(lerp(baseColor, referenceDodge, light.a), meta.y);
                if (limitAddedHdr) referenceColor = saturate(referenceColor);
                float3 referenceResponse = gammaBlend ? SRGBToLinear(referenceColor) : referenceColor;
                // Exposure scales only the added rim. The unlit interior, surface tint
                // and existing HDR/emission remain exactly as the material rendered them.
                return original + max(referenceResponse - incomingBase, 0) * meta.x;
            }
            float3 dodge = IndependentColorDodge(baseColor, light.rgb);
            // The clear-core style also keeps its soft shell material-dependent at
            // saturation. Fade the change in near zero to retain the legacy setting.
            float paintedResponse = smoothstep(0, .15, contourStrength);
            dodge = lerp(dodge, baseColor / max(1 - light.rgb, .15), paintedResponse);
            float3 color = min(lerp(baseColor, dodge, light.a), meta.y) * meta.x;
            if (limitAddedHdr) color = saturate(color);
            float3 response = gammaBlend ? SRGBToLinear(color) : color;
            float incomingPeak = max(max(incomingBase.r, incomingBase.g), incomingBase.b);
            // Dark art needs a colored contribution independent of the original channel values.
            // Let it fade out before it reaches highlights, in the same color space as the scene.
            float darkFillScale = lerp(1, .26, contourStrength);
            response += darkEdgeLight * (saturate(1 - incomingPeak * 4) * darkFillScale);
            if (limitAddedHdr)
            {
                // The profile raises dark contours independently of incoming bright art and HDR emission.
                float darkLimit = _RimEnhanceInnerWrap.z;
                float responsePeak = max(max(response.r, response.g), response.b);
                response *= min(1, max(incomingPeak, darkLimit) / max(responsePeak, .000001));
            }
            UNITY_BRANCH
            if (contourStrength > 0)
            {
                // The one-cell contour retains the painted RGB response. An independent
                // screen fill would flatten dark cloth and pale hair into a similar neon line.
                float3 core = saturate(contourLight);
                float3 gain = baseColor * core / max(1 - core, .05);
                // Core already contains geometric coverage, distance and visibility.
                // Blending by its peak again would attenuate the same light twice.
                float coreBlend = saturate(_RimRefStyle.z * contourStrength);
                float3 coreColor = min(baseColor + gain * coreBlend, meta.y) * meta.x;
                if (limitAddedHdr) coreColor = saturate(coreColor);
                float3 coreResponse = gammaBlend ? SRGBToLinear(coreColor) : coreColor;
                // A small bounded floor keeps genuinely black detail readable; it does
                // not replace the material-dependent response or grow with dodge gain.
                float3 floorLight = gammaBlend ? SRGBToLinear(core) : core;
                coreResponse += floorLight * (.032 * _RimRefStyle.z * contourStrength * meta.x) * saturate(1 - incomingPeak * 16);
                if (limitAddedHdr) coreResponse = saturate(coreResponse);
                response = max(response, coreResponse);
            }
            return response + incomingHdr;
        }
        float EnhancedPower(float value, float power)
        {
            return power <= 0 ? 1 : pow(saturate(value), power);
        }
        float4 BoundReferenceField(float4 field)
        {
        #if defined(ZE_RIM_NATIVE_REFERENCE)
            // Limit strength with the peak channel so a saturated native field keeps its hue.
            float3 rgb = max(field.rgb, 0);
            float peak = max(rgb.r, max(rgb.g, rgb.b));
            return float4(rgb / max(1, peak), saturate(field.a));
        #else
            return saturate(field);
        #endif
        }
        float4 ReferenceFieldContribution(float3 color, float alpha, float visibility)
        {
            // Quantize the bounded source in the reference's RGBA8 UNorm field before SrcAlpha/One blending.
            float4 source = round(BoundReferenceField(float4(color * alpha, alpha)) * 255) / 255;
            return round(source * source.a * visibility * 255) / 255;
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
        struct RimColorMix
        {
            float3 weightedColor;
            float weights;
            float peak;
        };

        float AccumulateRimColor(inout RimColorMix mix, float3 color, bool clearCore)
        {
            float peak = max(0, max(max(color.r, color.g), color.b));
            // A weaker lamp must retain a little color beside the dominant one.
            // The clear core uses a gentler weight; the soft inward field stays unchanged.
            float weight = clearCore ? sqrt(peak) : peak;
            mix.weightedColor += color * weight;
            mix.weights += peak * weight;
            mix.peak = max(mix.peak, peak);
            return peak;
        }

        float3 ResolveRimColor(RimColorMix mix)
        {
            return mix.weightedColor * (mix.peak / max(mix.weights, .00000001));
        }

        struct RimResponseMix
        {
            float3 addedColor;
            float magnitudes;
            float fourthPowers;
        };

        void AccumulateRimResponse(inout RimResponseMix mix, float3 gain)
        {
            float magnitude = max(gain.r, max(gain.g, gain.b));
            mix.addedColor += gain;
            mix.magnitudes += magnitude;
            float squared = magnitude * magnitude;
            mix.fourthPowers += squared * squared;
        }

        float3 ResolveRimResponse(RimResponseMix mix)
        {
            // Fuse material responses in linear color. A fourth-power envelope
            // preserves one lamp and allows only gentle growth on overlapping edges.
            // Color still includes every local contribution, without max-channel picking.
            return mix.addedColor * (sqrt(sqrt(mix.fourthPowers)) / max(mix.magnitudes, .00000001));
        }

        void ResolveReferenceContourGains(float3 original, float4 light, float3 hue, float4 meta,
            inout float3 keyGain, out float3 fillGain)
        {
            bool gammaBlend = fmod(meta.z, 2) > .5;
            bool limitAddedHdr = meta.z >= 2;
            float3 incomingBase = limitAddedHdr ? min(original, 1) : original;
            float3 baseColor = gammaBlend ? LinearToSRGB(incomingBase) : incomingBase;
            float materialPeak = max(incomingBase.r, max(incomingBase.g, incomingBase.b));
            float shadowWeight = 1 - smoothstep(.014, .035, materialPeak);
            float profileCeiling = limitAddedHdr ? saturate(meta.y) : meta.y;
            if (gammaBlend) profileCeiling = SRGBToLinear(profileCeiling.xxx).x;
            // A finite response keeps the outer contour and the weaker fill readable on dark paint.
            float3 dodge = baseColor / max(1 - light.rgb, lerp(.1, .04, shadowWeight));
            float3 color = lerp(baseColor, dodge, light.a);
            float3 paint = gammaBlend ? SRGBToLinear(color) : color;
            float3 paintedGain = max(min(paint, profileCeiling) - incomingBase, 0) * meta.x;
            hue = saturate(hue);
            float3 gain = max(keyGain, paintedGain) * hue;
            float budget = max(materialPeak, max(keyGain.r, max(keyGain.g, keyGain.b)));
            float peak = max(gain.r, max(gain.g, gain.b));
            fillGain = gain * min(1, budget / max(peak, .00000001));

            float fieldPeak = max(light.r, max(light.g, light.b));
            if (shadowWeight <= 0 || fieldPeak <= 0) return;

            // Reserve extra headroom for painted details as the masked light approaches saturation.
            // Compress the RGB gain together to keep the channel ratios of the shaded paint.
            float detail = smoothstep(.7, .96, fieldPeak);
            float detailRoom = min(.24, 2.4 * sqrt(materialPeak)) * detail;
            float responseWeight = saturate(light.a) * meta.x;
            float ceiling = min((.13 + 3 * materialPeak + detailRoom) * responseWeight,
                max(0, profileCeiling - materialPeak));
            float3 detailGain = max(paint - incomingBase, 0) * meta.x;
            float paintPeak = max(detailGain.r, max(detailGain.g, detailGain.b));

            // Reflection supplies color on black paint and recedes as painted detail becomes visible.
            float3 fieldHue = light.rgb / max(fieldPeak, .000001);
            float3 reflectedHue = fieldHue * lerp(fieldHue, hue, .2);
            float reflection = .11 * fieldPeak * fieldPeak * responseWeight;
            float reflectionRoom = saturate(1 - detail * paintPeak / max(ceiling, .000001));
            float3 shadowGain = detailGain + reflectedHue * (reflection * reflectionRoom);
            float shadowPeak = max(shadowGain.r, max(shadowGain.g, shadowGain.b));
            float knee = ceiling * .7;
            float over = max(shadowPeak - knee, 0);
            float headroom = ceiling - knee;
            float compressed = min(shadowPeak, knee) + headroom * over / max(headroom + over, .000001);
            shadowGain *= compressed / max(shadowPeak, .000001);
            keyGain = lerp(keyGain, shadowGain, shadowWeight);
            fillGain = lerp(fillGain, shadowGain * hue, shadowWeight);
        }

        float3 ArtCellWorldPosition(float3 positionWS, float2 cellOffset)
        {
            float3 axisX = TransformObjectToWorldDir(float3(1, 0, 0), false);
            float3 axisY = TransformObjectToWorldDir(float3(0, 1, 0), false);
            return positionWS +
                axisX * (cellOffset.x / max(dot(axisX, _RimRefRow0.xyz), .000001)) +
                axisY * (cellOffset.y / max(dot(axisY, _RimRefRow1.xyz), .000001));
        }

        bool IntersectsShadowBox(float3 source, float3 cell)
        {
            // Segment versus the unit box, using the box axes and their cross products with the segment.
            float3 center = (source + cell) * .5;
            float3 halfSegment = (cell - source) * .5;
            float3 radius = abs(halfSegment);
            return all(abs(center) <= 1 + radius) &&
                all(abs(cross(center, halfSegment)) <= radius.yzx + radius.zxy);
        }

        float4 EnhancedFrag(ActorVaryings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 cellOffset;
            float2 uv = CoveredArtCell(input, cellOffset);
            float2 texel = max(_RimRefMaskInfo.xy, .000001);
            float3 energy = 0; float blend = 0;
        #if defined(ZE_RIM_NATIVE_REFERENCE)
            const bool referenceResponse = true;
        #else
            bool referenceResponse = _RimReferenceEdgeResponse > .5;
        #endif
            float3 darkEdgeLight = 0;
            float3 contourLight = 0;
            // Reference edges keep each light under its own mask.
            // Keep the older strongest-contribution mix only for custom ZE profiles.
            bool mixNativeLights = !referenceResponse && _RimEnhanceLightCount > 1 && _RimEnhanceShapes[0].x > 2.5;
            RimColorMix rimMix = (RimColorMix)0;
            RimColorMix darkMix = (RimColorMix)0;
            RimColorMix contourMix = (RimColorMix)0;
            RimResponseMix fillMix = (RimResponseMix)0;
            float3 primaryGain = 0;
            float primaryCoverage = 0;
            float unshadowedAlpha = 0;
            float visibleAlpha = 0;
            float3 cellWS = ArtCellWorldPosition(input.positionWS, cellOffset);
            float4 alphas = 0, visibilities = 0;
            float4 referenceFields[4];
            float2 weightedDirection = 0;
            float directionalWeight = 0;
            float visiblePrimary = 0, fallbackWeight = -1;
            float4 visiblePriorities = 0, horizontalDirections = 0;
        #if defined(ZE_RIM_NATIVE_REFERENCE)
            float4 oneSideField = 0;
            float4 leftField = 0, rightField = 0, centeredField = 0;
            float4 referenceScores = 0, nativePowers = 0;
            float2 referenceOffset = 0;
            float referenceScore = 0, strongestVisibleScore = 0;
            float referenceRear = 0;
            float3 viewRay = GetWorldSpaceNormalizeViewDir(input.positionWS);
            float2 viewSlope = abs(viewRay.z) > .00001 ? viewRay.xy / viewRay.z : 0;
        #endif
            int fallbackPrimary = 0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                if (i >= _RimEnhanceLightCount) break;
            #if defined(ZE_RIM_NATIVE_REFERENCE)
                float4 geometry = _RimNativeGeometry[i];
                float3 toward = geometry.xyz - input.positionWS;
                float rangeSq = dot(toward, toward) * (geometry.w * geometry.w);
                float cone = 1;
                if (_RimEnhanceShapes[i].x > 3.5)
                {
                    float cosine = dot(-toward, _RimEnhanceRows2[i].xyz) / max(sqrt(rangeSq), .00001);
                    float2 bounds = _RimEnhanceShapes[i].yz;
                    cone = saturate((cosine - bounds.x) / max(bounds.y - bounds.x, .00001));
                }
                float intensity = _RimEnhanceColors[i].a;
                float2 projectedRay = toward.xy - viewSlope * toward.z;
                float alpha = saturate(1 - length(projectedRay) * geometry.w) * cone * intensity;
                float fade = saturate(1 - rangeSq);
                nativePowers[i] = saturate(fade * fade * cone * intensity);
                float2 centerOffset = _RimRefCenterWS.xy - geometry.xy;
                float centerScore = saturate(saturate(1 - length(centerOffset) * geometry.w) * intensity);
            #else
                float3 q = float3(dot(float4(input.positionWS, 1), _RimEnhanceRows0[i]),
                    dot(float4(input.positionWS, 1), _RimEnhanceRows1[i]),
                    dot(float4(input.positionWS, 1), _RimEnhanceRows2[i]));
                float alpha = EnhancedFalloff(q, _RimEnhanceShapes[i], _RimEnhancePowers[i]) * _RimEnhanceColors[i].a;
                if (_RimEnhanceShapes[i].x > 2.5) alpha = saturate(alpha);
            #endif
                float visibility = 1;
                float4 shadow = _RimEnhanceShadows[i];
            #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
                UNITY_BRANCH
                if (_RimEnhanceAdditionalShadowDistanceSq > 0 && shadow.w > 0 && alpha > 0)
                {
                    half3 direction = SafeNormalize(shadow.xyz - cellWS);
                    half realtime = AdditionalLightRealtimeShadow((int)shadow.w - 1, cellWS, direction);
                    // Keep projected art-cell shadows readable in wide gameplay cameras.
                    // The rim fades over at most the last 8% of URP's existing shadow distance.
                    float3 cameraOffset = cellWS - _WorldSpaceCameraPos;
                    float distanceSq = dot(cameraOffset, cameraOffset);
                    float rimFade = smoothstep(_RimEnhanceAdditionalShadowDistanceSq * (.92 * .92),
                        _RimEnhanceAdditionalShadowDistanceSq, distanceSq);
                    visibility = lerp(realtime, 1, min(rimFade, GetAdditionalLightShadowFade(cellWS)));
                }
            #endif
                UNITY_BRANCH
                if (shadow.w < 0 && alpha > 0)
                {
                    float3 localCell = mul(_RimEnhanceFallbackBoxes[i], float4(cellWS, 1)).xyz;
                    if (IntersectsShadowBox(shadow.xyz, localCell)) visibility += shadow.w;
                }
                unshadowedAlpha = max(unshadowedAlpha, alpha);
                visibleAlpha = max(visibleAlpha, alpha * visibility);
                alphas[i] = alpha;
                visibilities[i] = visibility;
                if (referenceResponse)
                {
                    float3 fieldColor = _RimEnhanceColors[i].rgb;
                    if (_RimEnhanceShapes[i].x > 2.5) fieldColor *= _RimEnhanceColors[i].a;
                    float4 field = ReferenceFieldContribution(fieldColor, alpha, visibility);
                    referenceFields[i] = field;
                #if defined(ZE_RIM_NATIVE_REFERENCE)
                    oneSideField += field;
                    referenceScores[i] = centerScore;
                #endif
                    float available = max(field.r, max(field.g, field.b));
                    if (available > 0)
                    {
                        visiblePrimary += _RimPrimaryWeights[i] * visibility;
                        visiblePriorities[i] = _RimLightPriorities[i] * visibility;
                        float2 away = _RimEnhanceBands[i].xy;
                        // Signed world X. A centered rear lamp supplies energy but has little lateral direction.
                        horizontalDirections[i] = _RimEnhanceShapes[i].x > 2.5
                            ? _RimEnhancePowers[i].z * _RimEnhancePowers[i].y
                            : dot(away / max(length(away), .00001), _RimPrimaryDirection.zw);
                    #if defined(ZE_RIM_NATIVE_REFERENCE)
                        float score = centerScore * visibility;
                        if (score > strongestVisibleScore)
                        {
                            strongestVisibleScore = score;
                            referenceScore = centerScore;
                            referenceOffset = centerOffset;
                            referenceRear = NativeRearFacing(toward, viewRay);
                        }
                        float side = horizontalDirections[i];
                        if (side > .035) leftField += field;
                        else if (side < -.035) rightField += field;
                        else centeredField += field;
                    #endif
                    }
                    if (available > fallbackWeight) { fallbackWeight = available; fallbackPrimary = i; }
                }
                if (_RimEnhanceShapes[i].x > 2.5)
                {
                    // Recover the selection score from its response. Weight by this art cell's
                    // shadow once, so a blocked opposing lamp cannot thin the visible side's rim.
                    float weight = pow(_RimEnhanceBands[i].w, _RimEnhanceInnerWrap.w) * visibility;
                    weightedDirection += _RimEnhancePowers[i].zw * weight;
                    directionalWeight += weight;
                }
            }
            float4 primaryWeights = 0;
            float dominantSide = 0, sideConfidence = 0;
        #if defined(ZE_RIM_NATIVE_REFERENCE)
            float oneSideWeight = 0;
            bool opposingSides = any(leftField.rgb > 0) && any(rightField.rgb > 0);
            float3 primaryColor = 0;
            float primaryWeight = 0;
            float3 primaryHue = 0;
            float primaryField = 0, primaryRear = 0;
            float farStrandCoverage = 0;
        #endif
            if (referenceResponse)
            {
                float4 fallbackWeights = float4(fallbackPrimary == 0, fallbackPrimary == 1,
                    fallbackPrimary == 2, fallbackPrimary == 3);
                primaryWeights = lerp(fallbackWeights, _RimPrimaryWeights, saturate(visiblePrimary));
                float4 lateralPower = horizontalDirections * visiblePriorities;
                float lateralTotal = dot(abs(lateralPower), float4(1, 1, 1, 1));
                float visibleTotal = dot(visiblePriorities, float4(1, 1, 1, 1));
                float signedPower = dot(lateralPower, float4(1, 1, 1, 1));
                float balance = signedPower / max(lateralTotal, .000001);
            #if defined(ZE_RIM_NATIVE_REFERENCE)
                if (referenceScore > 0 && !opposingSides)
                {
                    float oppositeShare = max(0, .5 * (lateralTotal - abs(signedPower))) / max(lateralTotal, .000001);
                    // Blend only near the projected center or as an opposing light first becomes visible.
                    oneSideWeight = ReferenceSideWeight(referenceOffset, texel) * (1 - smoothstep(0, .05, oppositeShare));
                }
            #endif
                dominantSide = balance >= 0 ? 1 : -1;
                sideConfidence = smoothstep(.05, .35, abs(balance)) *
                    smoothstep(.035, .2, lateralTotal / max(visibleTotal, .000001));
                // Retain the stable key in a tie. A stronger group can take over from an opposing key.
                float4 group = max(dominantSide * lateralPower, 0);
                float opposedKey = -dominantSide * dot(primaryWeights, horizontalDirections);
                float transfer = sideConfidence * smoothstep(0, .12, opposedKey);
                primaryWeights = lerp(primaryWeights,
                    group / max(dot(group, float4(1, 1, 1, 1)), .000001), transfer);
            }
            float coherence = saturate(length(weightedDirection) / max(directionalWeight, .000001));
            float nativeDirectionalStyle = smoothstep(.55, .9, coherence);
            float2 sceneUV = GetNormalizedScreenSpaceUV(input.positionCS) * _RimEnhanceSceneScaleBias.xy + _RimEnhanceSceneScaleBias.zw;
            float4 original = SAMPLE_TEXTURE2D_X(_RimEnhanceSceneColor, sampler_PointClamp, sceneUV);
        #if defined(ZE_RIM_NATIVE_REFERENCE)
            float3 oneSideColor = original.rgb;
            UNITY_BRANCH
            if (oneSideWeight > 0)
            {
                float2 away = referenceOffset / max(length(referenceOffset), .00001);
                float coverage;
                oneSideColor = CompositeOneSide(original.rgb, BoundReferenceField(oneSideField), uv, texel,
                    away, referenceScore, referenceRear, 0, 0, coverage);
                if (oneSideWeight >= 1) return float4(oneSideColor, original.a);
            }
        #endif
            float exposure = _RimRefStyle.x;
            if (visibleAlpha < unshadowedAlpha)
            {
                if (visibleAlpha <= 0) return original;
                exposure = lerp(1, exposure, visibleAlpha / unshadowedAlpha);
            }
            [unroll] for (int i = 0; i < 4; i++)
            {
                if (i >= _RimEnhanceLightCount) break;
                float alpha = alphas[i], visibility = visibilities[i];
                if (visibility <= 0 || alpha <= 0) continue;
                float amount = alpha * alpha * visibility;
                float4 band = _RimEnhanceBands[i];
                float backFacing = 0;
                float3 nativeToLight = 0;
                float bandWidth = _RimEnhancePowers[i].x;
                float directionalStyle = 1;
                float projected = 1;
                if (_RimEnhanceShapes[i].x > 2.5)
                {
                    directionalStyle = nativeDirectionalStyle;
                    float thinWidth = lerp(min(bandWidth, 1), bandWidth, directionalStyle);
                    band.xy *= thinWidth / max(bandWidth, .00001);
                    bandWidth = thinWidth;
                #if defined(ZE_RIM_NATIVE_REFERENCE)
                    projected = smoothstep(0, .2, _RimEnhancePowers[i].y);
                    UNITY_BRANCH
                    if (projected < 1)
                    {
                        float3 toLight = _RimNativeGeometry[i].xyz - cellWS;
                        float3 view = GetWorldSpaceNormalizeViewDir(cellWS);
                        backFacing = saturate(-dot(toLight, view) / max(length(toLight), .00001));
                    }
                #else
                    // Native matrices contain rotation and uniform inverse range. Recover the
                    // light vector without adding another per-light upload or guessing a side at XY=0.
                    float3 cellQ = float3(dot(float4(cellWS, 1), _RimEnhanceRows0[i]),
                        dot(float4(cellWS, 1), _RimEnhanceRows1[i]), dot(float4(cellWS, 1), _RimEnhanceRows2[i]));
                    float3 toLight = -(_RimEnhanceRows0[i].xyz * cellQ.x + _RimEnhanceRows1[i].xyz * cellQ.y +
                        _RimEnhanceRows2[i].xyz * cellQ.z) / max(dot(_RimEnhanceRows0[i].xyz, _RimEnhanceRows0[i].xyz), .00000001);
                    float3 view = GetWorldSpaceNormalizeViewDir(cellWS);
                    nativeToLight = toLight;
                    float depth = dot(toLight, view);
                    float lightDistance = max(length(toLight), .00001);
                    backFacing = saturate(-depth / lightDistance);
                    if (referenceResponse) projected = smoothstep(0, .2, _RimEnhancePowers[i].y);
                    else if (backFacing > 0) band.xy *= saturate(_RimEnhancePowers[i].y);
                #endif
                }
                float2 scale = (_RimRefFrame.zw + band.z) / max(_RimRefFrame.zw, 1);
            #if !defined(ZE_RIM_NATIVE_REFERENCE)
                UNITY_BRANCH
                if (_RimEnhanceContourStrength > 0 && _RimEnhanceShapes[i].x > 2.5)
                {
                    // A full art-cell comparison retains islands and internal gaps that a
                    // fractionally shifted, filtered mask can almost erase on dark artwork.
                    float2 coreShift = band.xy / max(length(band.xy), .00001) * min(bandWidth, 1);
                    float2 coreUV = (uv - _RimRefFrame.xy) / max(scale, .00001) + _RimRefFrame.xy - coreShift * texel;
                    float centerFade = smoothstep(0, .2, saturate(_RimEnhancePowers[i].y));
                    float coreCoverage = saturate(1 - MaskAlpha(coreUV)) * centerFade;
                    float3 centerToLight = nativeToLight + cellWS - _RimRefCenterWS;
                    float3 centerView = GetWorldSpaceNormalizeViewDir(_RimRefCenterWS);
                    float centerBackFacing = saturate(-dot(centerToLight, centerView) / max(length(centerToLight), .00001));
                    if (centerBackFacing > 0 && centerFade < 1)
                        coreCoverage = max(coreCoverage, saturate(1 - BackContourCoverage(uv, texel * min(bandWidth, 1))) * centerBackFacing * (1 - centerFade));
                    float3 hue = saturate(_RimEnhanceColors[i].rgb);
                    // A clear core uses native irradiance once. The squared soft-shell
                    // field and its response weight must not dim this one-cell contour.
                    float3 coreColor = hue * max(_RimRefEdgeTint.rgb, 0) *
                        (alpha * visibility * coreCoverage * max(_RimRefEdgeTint.a, 0));
                    if (mixNativeLights) AccumulateRimColor(contourMix, coreColor, true);
                    else contourLight += coreColor;
                }
            #endif
                float2 shifted = (uv - _RimRefFrame.xy) / max(scale, .00001) + _RimRefFrame.xy - band.xy * texel;
                // Filter only contour coverage, never source art: every art cell retains a uniform shade.
                float directionalOccupied = FilteredMaskAlpha(shifted);
                float rearOccupied = 1;
                // Symmetric backlight stays on one art-cell contour, even when the weak
                // directional profile requests a wider band. Do not spread it over the body.
                float rearWidth = min(bandWidth, 1) * backFacing;
                if (backFacing > 0 && !referenceResponse) rearOccupied = BackContourCoverage(uv, texel * rearWidth);
                float occupied = min(directionalOccupied, rearOccupied);
                float tintOccupied = occupied;
                float hardOccupied = 1;
                if (referenceResponse)
                {
                    // Retain the reference's definite alpha boundary. Filtering is
                    // reserved for the faint inner shell, not the bright outer edge.
                    hardOccupied = MaskAlpha(shifted);
                    float edge = hardOccupied > 0 ? 0 : projected;
                    if (backFacing > 0 && projected < 1)
                    {
                        // Fade between definite art cells rather than flipping a
                        // fractionally shifted binary mask near the center.
                        float2 worldRight = float2(_RimRefRow0.x, _RimRefRow1.x) / texel;
                        float2 sideStep = worldRight / max(length(worldRight), .00001) * texel * min(bandWidth, 1);
                        float leftEdge = MaskAlpha(uv - sideStep) > 0 ? 0 : 1;
                        float rightEdge = MaskAlpha(uv + sideStep) > 0 ? 0 : 1;
                        float leftKey = lerp(step(0, _RimPrimaryDirection.y), step(0, dominantSide), sideConfidence);
                        float backFill = lerp(ReferenceFillStrength, .08, sideConfidence);
                        float rearEdge = max(leftEdge * lerp(backFill, 1, leftKey),
                            rightEdge * lerp(1, backFill, leftKey));
                        edge += rearEdge * backFacing * (1 - projected);
                    }
                    tintOccupied = 1 - saturate(edge);
                }
                float4 tint = lerp(_RimRefEdgeTint, _RimRefInnerTint, tintOccupied);
                float3 modulation = tint.rgb * tint.a;
                float wrap = 0;
                float wrapCoverage = 0;
                if (_RimEnhanceInnerWrap.y > 0 && occupied > 0 && (!referenceResponse || tintOccupied == 1))
                {
                    float2 inward = band.xy / max(length(band.xy), .00001) * texel;
                    int steps = (int)_RimEnhanceInnerWrap.x;
                    float wrapSpan = _RimEnhanceShapes[i].x > 2.5 ? lerp(1.0 / max(steps, 1), 1, directionalStyle) : 1;
                    float coverage = 0;
                    [unroll] for (int tap = 1; tap <= 4; tap++)
                    {
                        if (tap > steps) break;
                        coverage += saturate(directionalOccupied - FilteredMaskAlpha(shifted - inward * (tap * wrapSpan)));
                    }
                    coverage /= max(steps, 1);
                    if (_RimEnhanceShapes[i].x > 2.5) coverage *= lerp(.15, 1, directionalStyle);
                    if (referenceResponse) coverage *= projected;
                    else if (backFacing > 0)
                    {
                        // Retain the single-side transition; taper its normalized direction
                        // to zero at the projected center instead of flipping a full-strength shell.
                        coverage *= saturate(_RimEnhancePowers[i].y);
                        // The extra all-around contour gets only a faint one-cell inner transition.
                        float rearInner = BackContourCoverage(uv, texel * (rearWidth + backFacing));
                        coverage = max(coverage, .15 * saturate(rearOccupied - rearInner));
                    }
                    // Never add inward-wrap energy to the bright outer core.
                    wrapCoverage = coverage;
                    wrap = _RimEnhanceInnerWrap.y * coverage;
                    modulation += _RimRefEdgeTint.rgb * _RimRefEdgeTint.a * wrap;
                }
                // The reference 2D field includes the light multiplier in RGB as well
                // as alpha. Keep that response when mapping native lamps to this style.
                float3 fieldColor = _RimEnhanceColors[i].rgb;
                if (_RimReferenceEdgeResponse > .5 && _RimEnhanceShapes[i].x > 2.5) fieldColor *= _RimEnhanceColors[i].a;
                if (referenceResponse)
                {
                    if (all(referenceFields[i].rgb <= 0)) continue;
                    float key = primaryWeights[i];
                    float minority = sideConfidence * smoothstep(0, .35, -horizontalDirections[i] * dominantSide);
                    float3 hue = fieldColor / max(max(fieldColor.r, max(fieldColor.g, fieldColor.b)), .00000001);
                #if defined(ZE_RIM_NATIVE_REFERENCE)
                    UNITY_BRANCH
                    if (key > 0)
                    {
                        // The key's group supplies its direct field. Its mask also controls
                        // the support from the opposing field on far contour details.
                        float side = horizontalDirections[i];
                        float4 sameSide = side > .035 ? leftField + centeredField
                            : side < -.035 ? rightField + centeredField : centeredField;
                        float opposingAlpha = side > .035 ? rightField.a : side < -.035 ? leftField.a : 0;
                        sameSide = BoundReferenceField(sameSide);
                        float2 offset = _RimRefCenterWS.xy - _RimNativeGeometry[i].xy;
                        float sideWeight = opposingSides ? ReferenceSideWeight(offset, texel) : 0;
                        float coverage = max(1 - tintOccupied, saturate(2 * wrapCoverage)) * visibility;
                        float3 keyColor = original.rgb;
                        if (sideWeight < 1)
                        {
                            float3 keyTint = wrap > 0 ? _RimRefEdgeTint.rgb * _RimRefEdgeTint.a : tint.rgb * tint.a;
                            float response = pow(referenceScores[i], 1 / max(_RimEnhanceInnerWrap.w, .001));
                            keyColor = max(original.rgb, CompositeNativeDodge(original.rgb, sameSide,
                                keyTint * response, exposure * (wrap > 0 ? wrap : 1)));
                        }
                        if (sideWeight > 0)
                        {
                            // A directional key uses the standalone contour and wrap.
                            float keyCoverage;
                            float2 away = offset / max(length(offset), .00001);
                            float3 toKey = _RimNativeGeometry[i].xyz - input.positionWS;
                            float rear = NativeRearFacing(toKey, viewRay);
                            primaryHue += hue * key;
                            primaryField += referenceFields[i].a * key;
                            // Carry the shared reflection according to view depth. Moving a sprite
                            // toward a screen edge must not change which lamp is behind it.
                            primaryRear += NativeRearFacing(toKey, -GetViewForwardDir()) * key;
                            float3 single = CompositeOneSide(original.rgb, sameSide, uv, texel,
                                away, referenceScores[i], rear, opposingAlpha, hue, keyCoverage);
                            keyColor = sideWeight >= 1 ? single : lerp(keyColor, single, sideWeight);
                            coverage = lerp(coverage, keyCoverage, sideWeight);
                        }
                        if (opposingSides)
                        {
                            primaryColor += keyColor * key;
                            primaryWeight += key;
                        }
                        else primaryGain += (keyColor - original.rgb) * key;
                        primaryCoverage += coverage * key;
                    }
                    UNITY_BRANCH
                    if (key < 1)
                    {
                        float keyDirection = dot(primaryWeights, horizontalDirections);
                        // Same-side lamps are already represented by the key field.
                        float opposite = 1 - smoothstep(0, .12, keyDirection * horizontalDirections[i]);
                        float edge = 1 - lerp(directionalOccupied, step(.5, hardOccupied), .65);
                        float coverage = max(projected * edge, 1 - tintOccupied) + wrap;
                        float facingOtherSide = smoothstep(.025, .22, -keyDirection * horizontalDirections[i]);
                        UNITY_BRANCH
                        if (opposingSides && facingOtherSide > 0 && tintOccupied < 1)
                        {
                            // Thin strands also expose an outside edge toward the other lamp.
                            // Use that lamp's contour where the opposite field is visible.
                            float2 insideStep = band.xy / max(length(band.xy), .00001) * texel;
                            float2 interior = uv + 3 * insideStep;
                            float thin = 1 - FilteredMaskAlpha(interior);
                            float3 paint = max(original.rgb, 0);
                        #if defined(UNITY_COLORSPACE_GAMMA)
                            paint = SRGBToLinear(paint);
                        #endif
                            float dark = 1 - smoothstep(.018, .052, Luminance(paint));
                            UNITY_BRANCH
                            if (dark > 0 && thin < 1)
                            {
                                // A diagonal strand can meet another occupied cell on the
                                // center tap. Look to either side of that tap at dark joints.
                                float2 artStep = insideStep / texel;
                                float2 tangent = float2(-artStep.y, artStep.x) * texel;
                                float joint = 1 - min(FilteredMaskAlpha(interior + tangent),
                                    FilteredMaskAlpha(interior - tangent));
                                // At the bend into a panel, the inward taps can all be
                                // occupied while the dark art still exposes a corner.
                                float corner = 1 - min(FilteredMaskAlpha(uv + tangent),
                                    FilteredMaskAlpha(uv - tangent));
                                thin = lerp(thin, max(thin, max(joint, corner)), dark);
                            }
                            float strand = thin * (1 - tintOccupied) * facingOtherSide * visibility *
                                (1 - key) * smoothstep(0, .22, referenceFields[i].a);
                            farStrandCoverage = max(farStrandCoverage, strand);
                        }
                        float pale = smoothstep(.025, .6, dot(original.rgb, float3(.2126, .7152, .0722)));
                        float power = nativePowers[i];
                        // A neutral lamp needs less fill to read clearly against the key.
                        float colorWeight = 1 - .35 * min(hue.r, min(hue.g, hue.b));
                        float gain = power * power * visibility * coverage * lerp(.314, .274, pale) *
                            _RimRefStyle.z * (1 - key) * opposite * lerp(1, .32, minority) * colorWeight;
                        if (fmod(_RimRefStyle.w, 2) > .5) hue = SRGBToLinear(hue);
                        hue = lerp(hue, 1, .1 * pale);
                        AccumulateRimResponse(fillMix, hue * _RimRefEdgeTint.rgb * (_RimRefEdgeTint.a * gain));
                    }
                #else
                    float4 incident = referenceFields[i];
                    // The key resolves the combined field like the compatibility path.
                    // A weaker share of the opposite field keeps the fill readable.
                    // Fade that support at each lamp's cone/range boundary and in shadow.
                    float support = smoothstep(0, .25, incident.a) * visibility;
                    UNITY_BRANCH
                    if (support > 0)
                    {
                        float2 direction = band.xy / max(length(band.xy), .00001);
                        [unroll] for (int j = 0; j < 4; j++)
                        {
                            if (j >= _RimEnhanceLightCount) break;
                            if (j == i) continue;
                            float2 other = _RimEnhanceBands[j].xy;
                            float opposition = saturate(-dot(direction, other) / max(length(other), .00001));
                            float3 otherColor = _RimEnhanceColors[j].rgb;
                            float3 otherHue = otherColor / max(max(otherColor.r, max(otherColor.g, otherColor.b)), .00000001);
                            // Aligned lamps with the same color do not inflate the shared field.
                            float3 difference = abs(hue - otherHue);
                            float complement = max(difference.r, max(difference.g, difference.b));
                            float keyShare = smoothstep(0, .12, max(opposition, complement));
                            incident += referenceFields[j] * (lerp(.5 * opposition * (1 - minority), keyShare, key) * support);
                        }
                    }
                    incident = BoundReferenceField(incident);
                    float4 localLight = float4(incident.rgb * saturate(modulation * band.w), incident.a * _RimRefStyle.z);
                    float materialPeak = max(original.r, max(original.g, original.b));
                    float darkPaint = 1 - smoothstep(.014, .035, materialPeak);
                    float fieldFloor = min(localLight.r, min(localLight.g, localLight.b));
                    // Limit only the saturated neutral part of a strong field on dark paint.
                    // Preserve the channel differences from colored lights.
                    float4 paintedLight = localLight;
                    paintedLight.rgb -= max(0, fieldFloor - .96) * darkPaint;
                    float3 localColor = CompositeColor(original.rgb, paintedLight, 0, 0,
                        float4(exposure, _RimRefStyle.y, _RimRefStyle.w, 1));
                    float3 referenceGain = max(localColor - original.rgb, 0);
                    float3 controlledGain = referenceGain;
                    float3 tinted;
                    ResolveReferenceContourGains(original.rgb, localLight, hue,
                        float4(exposure, _RimRefStyle.y, _RimRefStyle.w, 1), controlledGain, tinted);
                    // Keep the compatibility response on painted key pixels, with
                    // a small bounded reflection where little paint color is visible.
                    float responsePeak = max(referenceGain.r, max(referenceGain.g, referenceGain.b));
                    float reflectionPeak = max(controlledGain.r, max(controlledGain.g, controlledGain.b));
                    float3 paintedGain = referenceGain * max(1, reflectionPeak / max(responsePeak, .000001));
                    float painted = smoothstep(0, .0005, materialPeak) * smoothstep(.004, .045, responsePeak);
                    primaryGain += lerp(controlledGain, paintedGain, painted) * key;
                    primaryCoverage += (1 - tintOccupied) * key;
                    float fillStrength = lerp(ReferenceFillStrength, .08, minority);
                    AccumulateRimResponse(fillMix, tinted * (fillStrength * (1 - key)));
                #endif
                    continue;
                }
                float3 rimColor = saturate(fieldColor * amount) * modulation * band.w;
                // Native Shape.w opts into dark contour fill. Use the smooth field directly
                // so dark edges remain readable as the actor approaches or leaves the light.
                float3 darkColor = _RimEnhanceShapes[i].w > 0 ?
                    _RimEnhanceColors[i].rgb * (alpha * visibility * _RimEnhanceShapes[i].w) * saturate(modulation) : 0;
                if (mixNativeLights)
                {
                    float rimPeak = AccumulateRimColor(rimMix, rimColor, false);
                    if (rimPeak > 0) blend = max(blend, amount);
                    AccumulateRimColor(darkMix, darkColor, false);
                }
                else
                {
                    energy += rimColor;
                    darkEdgeLight += darkColor;
                    blend += amount;
                }
            }
            if (referenceResponse)
            {
            #if defined(ZE_RIM_NATIVE_REFERENCE)
                float fillVisibility = 1 - smoothstep(0, 1, primaryCoverage);
                float3 fill = ResolveRimResponse(fillMix) * fillVisibility;
                if (opposingSides)
                {
                    // Limit the added fill against the rendered key without recompositing the key.
                    float3 keyScene = primaryColor + original.rgb * (1 - saturate(primaryWeight));
                    float hueRange = max(primaryHue.r, max(primaryHue.g, primaryHue.b)) -
                        min(primaryHue.r, min(primaryHue.g, primaryHue.b));
                    // The key-hued reflection can strengthen an existing faint key contour.
                    // Only the independently colored fill yields to the key's coverage.
                    float strandWeight = farStrandCoverage * smoothstep(.10, .35, hueRange) *
                        smoothstep(.03, .32, primaryField) * smoothstep(.04, .42, primaryRear);
                    UNITY_BRANCH
                    if (strandWeight > 0)
                    {
                        // Share luminance across these narrow contours and carry the key hue.
                        // A broad edge still uses the independent fill above.
                        float3 tint = _RimRefEdgeTint.rgb * _RimRefEdgeTint.a;
                        float3 incident = CompositeNativeDodge(original.rgb, saturate(oneSideField), tint, exposure);
                        float3 hue = primaryHue;
                        if (fmod(_RimRefStyle.w, 2) > .5) hue = SRGBToLinear(hue);
                        float pale = smoothstep(.07, .30, Luminance(max(original.rgb, 0)));
                        float amount = max(0, Luminance(incident - keyScene)) * lerp(1, .24, pale);
                        float3 strand = hue * (amount / max(Luminance(hue), .000001));
                        fill = lerp(fill, strand, strandWeight);
                    }
                    original.rgb = keyScene + FitNativeGain(fill, keyScene);
                    return original;
                }
                float3 gain = MixNativeKeyAndFill(primaryGain, fill, original.rgb);
                original.rgb = lerp(original.rgb + gain, oneSideColor, oneSideWeight);
            #else
                // The secondary lights may reveal another direction or meet the
                // soft inner wrap without changing the key's fully covered edge.
                float3 gain = primaryGain + ResolveRimResponse(fillMix) * (1 - saturate(primaryCoverage));
                if (_RimRefStyle.w >= 2) gain = min(gain, max(1 - min(original.rgb, 1), 0));
                original.rgb += gain;
            #endif
                return original;
            }
            if (mixNativeLights)
            {
                energy = ResolveRimColor(rimMix);
                darkEdgeLight = ResolveRimColor(darkMix);
                contourLight = ResolveRimColor(contourMix);
                float mixedPeak = max(max(contourLight.r, contourLight.g), contourLight.b);
                contourLight *= contourMix.peak / max(mixedPeak, .00000001);
            }
            float4 light = float4(saturate(energy), saturate(blend) * _RimRefStyle.z);
            original.rgb = CompositeColor(original.rgb, light, min(darkEdgeLight, .25) * _RimRefStyle.z, contourLight,
                float4(exposure, _RimRefStyle.y, _RimRefStyle.w, 1));
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
            #pragma multi_compile_local_fragment _ ZE_RIM_NATIVE_REFERENCE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
