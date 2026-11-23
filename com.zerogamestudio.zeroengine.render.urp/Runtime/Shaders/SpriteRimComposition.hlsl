#ifndef ZEROENGINE_SPRITE_RIM_COMPOSITION_INCLUDED
#define ZEROENGINE_SPRITE_RIM_COMPOSITION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_RimRefMask); SAMPLER(sampler_RimRefMask);
float4 _RimEnhanceInnerWrap;
int _RimEnhanceLightCount;
float4 _RimEnhanceRows0[4], _RimEnhanceRows1[4], _RimEnhanceRows2[4], _RimEnhanceColors[4];
float4 _RimEnhanceShapes[4], _RimEnhancePowers[4], _RimEnhanceBands[4];
// Positive w selects a URP shadow index. Negative w selects a box and encodes its opacity.
float4 _RimEnhanceShadows[4];
float4x4 _RimEnhanceFallbackBoxes[4];
float _RimEnhanceAdditionalShadowDistanceSq;
float4 _RimRefRow0, _RimRefRow1, _RimRefMaskInfo, _RimRefFrame, _RimRefStyle;
float4 _RimRefEdgeTint, _RimRefInnerTint;

float2 ZeroSpriteRimArtCell(float3 positionWS, out float2 cellOffset)
{
    float2 step = max(_RimRefMaskInfo.xy, .000001);
    float2 uv = float2(dot(float4(positionWS, 1), _RimRefRow0),
                       dot(float4(positionWS, 1), _RimRefRow1));
    float2 cell = (floor(uv / step) + .5) * step;
    cellOffset = cell - uv;
    return cell;
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
float3 CompositeColor(float3 original, float4 light, float3 darkEdgeLight, float4 meta)
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
    float incomingPeak = max(max(incomingBase.r, incomingBase.g), incomingBase.b);
    // Dark art needs a colored contribution independent of the original channel values.
    // Let it fade out before it reaches highlights, in the same color space as the scene.
    response += darkEdgeLight * saturate(1 - incomingPeak * 4);
    if (limitAddedHdr)
    {
        // The profile raises dark contours independently of incoming bright art and HDR emission.
        float darkLimit = _RimEnhanceInnerWrap.z;
        float responsePeak = max(max(response.r, response.g), response.b);
        response *= min(1, max(incomingPeak, darkLimit) / max(responsePeak, .000001));
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
struct RimColorMix
{
    float3 weightedColor;
    float weights;
    float peak;
};

float AccumulateRimColor(inout RimColorMix mix, float3 color)
{
    float peak = max(0, max(max(color.r, color.g), color.b));
    mix.weightedColor += color * peak;
    mix.weights += peak * peak;
    mix.peak = max(mix.peak, peak);
    return peak;
}

float3 ResolveRimColor(RimColorMix mix)
{
    return mix.weightedColor * (mix.peak / max(mix.weights, .00000001));
}

float3 ZeroSpriteRimCellWorldPosition(float3 positionWS, float2 cellOffset, float3 axisX, float3 axisY)
{
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

float3 ZeroComposeSpriteRim(float3 original, float2 uv, float3 geometryWS, float3 cellWS)
{
    float2 texel = max(_RimRefMaskInfo.xy, .000001);
    float3 energy = 0; float blend = 0;
    float3 darkEdgeLight = 0;
    // Native lights can illuminate the same art cell. Bound brightness by the
    // strongest contribution and let stronger colors lead a continuous hue mix.
    bool mixNativeLights = _RimEnhanceLightCount > 1 && _RimEnhanceShapes[0].x > 2.5;
    RimColorMix rimMix = (RimColorMix)0;
    RimColorMix darkMix = (RimColorMix)0;
    float unshadowedAlpha = 0;
    float visibleAlpha = 0;
    float4 alphas = 0, visibilities = 0;
    float2 weightedDirection = 0;
    float directionalWeight = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i >= _RimEnhanceLightCount) break;
        float3 q = float3(dot(float4(geometryWS, 1), _RimEnhanceRows0[i]),
            dot(float4(geometryWS, 1), _RimEnhanceRows1[i]),
            dot(float4(geometryWS, 1), _RimEnhanceRows2[i]));
        float falloff = EnhancedFalloff(q, _RimEnhanceShapes[i], _RimEnhancePowers[i]);
        float alpha = falloff * _RimEnhanceColors[i].a;
        if (_RimEnhanceShapes[i].x > 2.5) alpha = saturate(alpha);
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
        if (_RimEnhanceShapes[i].x > 2.5)
        {
            // Recover the selection score from its response. Weight by this art cell's
            // shadow once, so a blocked opposing lamp cannot thin the visible side's rim.
            float weight = pow(_RimEnhanceBands[i].w, _RimEnhanceInnerWrap.w) * visibility;
            weightedDirection += _RimEnhancePowers[i].zw * weight;
            directionalWeight += weight;
        }
    }
    float coherence = saturate(length(weightedDirection) / max(directionalWeight, .000001));
    float nativeDirectionalStyle = smoothstep(.55, .9, coherence);
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i >= _RimEnhanceLightCount) break;
        float alpha = alphas[i], visibility = visibilities[i];
        if (visibility <= 0) continue;
        float amount = alpha * alpha * visibility;
        float4 band = _RimEnhanceBands[i];
        float backFacing = 0;
        float bandWidth = _RimEnhancePowers[i].x;
        float directionalStyle = 1;
        if (_RimEnhanceShapes[i].x > 2.5)
        {
            directionalStyle = nativeDirectionalStyle;
            float thinWidth = lerp(min(bandWidth, 1), bandWidth, directionalStyle);
            band.xy *= thinWidth / max(bandWidth, .00001);
            bandWidth = thinWidth;
            // Native matrices contain rotation and uniform inverse range. Recover the
            // light vector without adding another per-light upload or guessing a side at XY=0.
            float3 cellQ = float3(dot(float4(cellWS, 1), _RimEnhanceRows0[i]),
                dot(float4(cellWS, 1), _RimEnhanceRows1[i]), dot(float4(cellWS, 1), _RimEnhanceRows2[i]));
            float3 toLight = -(_RimEnhanceRows0[i].xyz * cellQ.x + _RimEnhanceRows1[i].xyz * cellQ.y +
                _RimEnhanceRows2[i].xyz * cellQ.z) / max(dot(_RimEnhanceRows0[i].xyz, _RimEnhanceRows0[i].xyz), .00000001);
            float3 view = GetWorldSpaceNormalizeViewDir(cellWS);
            float depth = dot(toLight, view);
            float lightDistance = max(length(toLight), .00001);
            backFacing = saturate(-depth / lightDistance);
            // Near the projected center the directional band vanishes continuously;
            // only a light behind the visible actor grows a symmetric contour.
            if (backFacing > 0) band.xy *= saturate(_RimEnhancePowers[i].y);
        }
        float2 scale = (_RimRefFrame.zw + band.z) / max(_RimRefFrame.zw, 1);
        float2 shifted = (uv - _RimRefFrame.xy) / max(scale, .00001) + _RimRefFrame.xy - band.xy * texel;
        // Filter only contour coverage, never source art: every art cell retains a uniform shade.
        float directionalOccupied = FilteredMaskAlpha(shifted);
        float rearOccupied = 1;
        // Symmetric backlight stays on one art-cell contour, even when the weak
        // directional profile requests a wider band. Do not spread it over the body.
        float rearWidth = min(bandWidth, 1) * backFacing;
        if (backFacing > 0) rearOccupied = BackContourCoverage(uv, texel * rearWidth);
        float occupied = min(directionalOccupied, rearOccupied);
        float4 tint = lerp(_RimRefEdgeTint, _RimRefInnerTint, occupied);
        float3 modulation = tint.rgb * tint.a;
        if (_RimEnhanceInnerWrap.y > 0 && occupied > 0)
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
            if (backFacing > 0)
            {
                // Retain the single-side transition; taper its normalized direction
                // to zero at the projected center instead of flipping a full-strength shell.
                coverage *= saturate(_RimEnhancePowers[i].y);
                // The extra all-around contour gets only a faint one-cell inner transition.
                float rearInner = BackContourCoverage(uv, texel * (rearWidth + backFacing));
                coverage = max(coverage, .15 * saturate(rearOccupied - rearInner));
            }
            // Never add inward-wrap energy to the bright outer core.
            modulation += _RimRefEdgeTint.rgb * _RimRefEdgeTint.a *
                (_RimEnhanceInnerWrap.y * coverage);
        }
        float3 rimColor = saturate(_RimEnhanceColors[i].rgb * amount) * modulation * band.w;
        // Native Shape.w opts into dark contour fill. Use the smooth field directly
        // so dark edges remain readable as the actor approaches or leaves the light.
        float3 darkColor = _RimEnhanceShapes[i].w > 0 ?
            _RimEnhanceColors[i].rgb * (alpha * visibility * _RimEnhanceShapes[i].w) * saturate(modulation) : 0;
        if (mixNativeLights)
        {
            if (AccumulateRimColor(rimMix, rimColor) > 0) blend = max(blend, amount);
            AccumulateRimColor(darkMix, darkColor);
        }
        else
        {
            energy += rimColor;
            darkEdgeLight += darkColor;
            blend += amount;
        }
    }
    if (mixNativeLights)
    {
        energy = ResolveRimColor(rimMix);
        darkEdgeLight = ResolveRimColor(darkMix);
    }
    float exposure = _RimRefStyle.x;
    if (visibleAlpha < unshadowedAlpha)
    {
        if (visibleAlpha <= 0) return original;
        exposure = lerp(1, exposure, visibleAlpha / unshadowedAlpha);
    }
    float4 light = float4(saturate(energy), saturate(blend) * _RimRefStyle.z);
    return CompositeColor(original, light, min(darkEdgeLight, .25) * _RimRefStyle.z,
        float4(exposure, _RimRefStyle.y, _RimRefStyle.w, 1));
}

float3 ZeroApplyProjectedSpriteRim(float3 original, float3 geometryWS, float3 artRight, float3 artUp)
{
    UNITY_BRANCH
    if (_RimEnhanceLightCount <= 0) return original;

    float2 cellOffset;
    float2 cell = ZeroSpriteRimArtCell(geometryWS, cellOffset);
    if (MaskAlpha(cell) <= .00001) return original;

    float3 cellWS = ZeroSpriteRimCellWorldPosition(geometryWS, cellOffset, artRight, artUp);
    return ZeroComposeSpriteRim(original, cell, geometryWS, cellWS);
}

#endif
