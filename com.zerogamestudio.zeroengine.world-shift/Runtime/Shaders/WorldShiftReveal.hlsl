#ifndef ZE_WORLD_SHIFT_REVEAL_INCLUDED
#define ZE_WORLD_SHIFT_REVEAL_INCLUDED

float4 _ZEWorldShiftOrigin;
float _ZEWorldShiftRadius;
float _ZEWorldShiftWidth;
float _ZEWorldShiftDirection;
float _ZEWorldShiftActive;
float _ZEWorldShiftNoiseScale;
float _ZEWorldShiftNoiseStrength;
half4 _ZEWorldShiftEdgeColor;

float ZEWorldShiftHash21(float2 value)
{
    value = frac(value * float2(123.34, 456.21));
    value += dot(value, value + 45.32);
    return frac(value.x * value.y);
}

float ZEWorldShiftValueNoise(float2 value)
{
    float2 cell = floor(value);
    float2 local = frac(value);
    local = local * local * (3.0 - 2.0 * local);

    float a = ZEWorldShiftHash21(cell);
    float b = ZEWorldShiftHash21(cell + float2(1.0, 0.0));
    float c = ZEWorldShiftHash21(cell + float2(0.0, 1.0));
    float d = ZEWorldShiftHash21(cell + float2(1.0, 1.0));

    return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
}

float ZEWorldShiftScanNoise(float3 positionWS)
{
    float3 fromOrigin = positionWS - _ZEWorldShiftOrigin.xyz;
    float2 coord = float2(
        fromOrigin.x * 0.45 + fromOrigin.y * 1.3,
        fromOrigin.z * 0.55 - fromOrigin.y * 0.8 + 9.0) * max(_ZEWorldShiftNoiseScale, 0.001);
    float coarse = ZEWorldShiftValueNoise(coord);
    float fine = ZEWorldShiftValueNoise(coord * 3.1 + 17.0);
    return ((coarse - 0.5) * 1.35 + (fine - 0.5) * 0.45) * _ZEWorldShiftNoiseStrength;
}

float ZEWorldShiftNoisyRadius(float3 positionWS)
{
    return _ZEWorldShiftRadius + ZEWorldShiftScanNoise(positionWS);
}

float ZEWorldShiftScanDistance(float3 positionWS)
{
    return distance(positionWS, _ZEWorldShiftOrigin.xyz);
}

float ZEWorldShiftScanSide(float3 positionWS)
{
    return ZEWorldShiftNoisyRadius(positionWS) - ZEWorldShiftScanDistance(positionWS);
}

float ZEWorldShiftDitherThreshold(float4 positionCS)
{
    float2 screenUV = positionCS.xy / max(positionCS.w, 0.0001);
    float2 pixel = floor(screenUV * _ScreenParams.xy);
    return ZEWorldShiftHash21(pixel);
}

half ZEWorldShiftRadiusMask(float3 positionWS)
{
    float side = ZEWorldShiftScanSide(positionWS);
    float width = max(_ZEWorldShiftWidth, 0.001);
    return smoothstep(-width, width, side);
}

half ZEWorldShiftBinaryRadiusMask(float3 positionWS, float4 positionCS)
{
    float side = ZEWorldShiftScanSide(positionWS);
    float width = max(_ZEWorldShiftWidth, 0.001);
    float edgeBand = 1.0 - smoothstep(width * 0.08, width * 0.85, abs(side));
    float ditherOffset = (ZEWorldShiftDitherThreshold(positionCS) - 0.5) * width * 0.22 * edgeBand;
    return (half)step(0.0, side + ditherOffset);
}

half ZEWorldShiftTargetMask(float3 positionWS)
{
    half radiusMask = ZEWorldShiftRadiusMask(positionWS);
    return _ZEWorldShiftDirection >= 0.0h ? radiusMask : 1.0h - radiusMask;
}

half ZEWorldShiftBinaryTargetMask(float3 positionWS, float4 positionCS)
{
    half radiusMask = ZEWorldShiftBinaryRadiusMask(positionWS, positionCS);
    return _ZEWorldShiftDirection >= 0.0h ? radiusMask : 1.0h - radiusMask;
}

half ZEWorldShiftVisibleMask(float3 positionWS, half worldShiftLayer)
{
    half targetMask = ZEWorldShiftTargetMask(positionWS);
    half targetLayer = _ZEWorldShiftDirection >= 0.0h ? 1.0h : 0.0h;
    half isTargetLayer = 1.0h - step(0.5h, abs(worldShiftLayer - targetLayer));
    return lerp(1.0h - targetMask, targetMask, isTargetLayer);
}

half ZEWorldShiftBinaryVisibleMask(float3 positionWS, float4 positionCS, half worldShiftLayer)
{
    half targetMask = ZEWorldShiftBinaryTargetMask(positionWS, positionCS);
    half targetLayer = _ZEWorldShiftDirection >= 0.0h ? 1.0h : 0.0h;
    half isTargetLayer = 1.0h - step(0.5h, abs(worldShiftLayer - targetLayer));
    return lerp(1.0h - targetMask, targetMask, isTargetLayer);
}

half ZEWorldShiftDigitalTickMask(float3 positionWS, float scanDistance)
{
    float3 fromOrigin = positionWS - _ZEWorldShiftOrigin.xyz;
    float track = fromOrigin.x * 1.7 + fromOrigin.z * 0.85 + fromOrigin.y * 0.25;
    float segment = frac(track * 3.2 + _Time.y * 0.42);
    float2 cell = floor(float2(track * 3.2, scanDistance * 2.1));
    half gate = (half)step(0.62, ZEWorldShiftHash21(cell));
    half tick = (half)(1.0 - smoothstep(0.025, 0.095, abs(segment - 0.5)));
    return tick * gate;
}

#endif
