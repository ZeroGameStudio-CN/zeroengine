#ifndef ZEROENGINE_SURFACE_WEATHER_INCLUDED
#define ZEROENGINE_SURFACE_WEATHER_INCLUDED

// zero.render.surface_weather.v1: an optional material layer, never a weather owner.
// The consumer owns normalized state, roof bounds and its original lighting/UV model.
float4 ZeroApplySurfaceWeather(float3 albedo, float smoothness, float3 positionWS,
    float3 normalWS, float wetness, float snow, int shelterCount,
    float4 shelterMin[8], float4 shelterMax[8])
{
    if (wetness <= 0.0 && snow <= 0.0) return float4(albedo, smoothness);
    float exposure = 1.0;
    for (int i = 0; i < 8; i++)
    {
        if (i >= shelterCount) break;
        if (positionWS.y <= shelterMax[i].y
            && all(positionWS.xz >= shelterMin[i].xz)
            && all(positionWS.xz <= shelterMax[i].xz)) exposure = 0.0;
    }
    float wet = saturate(wetness * exposure);
    float coverage = saturate(snow * exposure) * smoothstep(0.35, 0.85, normalize(normalWS).y);
    float grain = frac(sin(dot(floor(positionWS.xz * 35.0), float2(127.1, 311.7))) * 43758.5453);
    float mask = coverage > 0.999 ? 1.0 : coverage * lerp(0.85, 1.0, grain);
    albedo = lerp(albedo * (1.0 - wet * 0.38), float3(0.87, 0.93, 0.98), mask);
    smoothness = lerp(lerp(smoothness, 0.9, wet), 0.2, mask);
    return float4(albedo, smoothness);
}

#endif
