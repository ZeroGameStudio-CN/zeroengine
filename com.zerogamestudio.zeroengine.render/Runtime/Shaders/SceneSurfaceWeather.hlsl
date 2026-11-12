#ifndef ZEROENGINE_SCENE_SURFACE_WEATHER_INCLUDED
#define ZEROENGINE_SCENE_SURFACE_WEATHER_INCLUDED

// The caller supplies world state. These fields never advance time or follow the camera.
float _ZeroSceneWeatherEnabled;
int _ZeroWeatherRegionCount;
float4 _ZeroWeatherDefault;
float4 _ZeroWeatherRegionBounds[32];
float4 _ZeroWeatherRegionStates[32];
float4 _ZeroWeatherExposureU[2], _ZeroWeatherExposureV[2], _ZeroWeatherExposureAxis[2];
float4 _ZeroWeatherExposureSize;
float _ZeroWeatherExposureReady;
float4 _ZeroWeatherSnowColor;
TEXTURE2D(_ZeroWeatherRainExposure);
SAMPLER(sampler_ZeroWeatherRainExposure);
TEXTURE2D(_ZeroWeatherSnowExposure);
SAMPLER(sampler_ZeroWeatherSnowExposure);

float2 ZeroSceneWeatherState(float3 p)
{
    float2 sum = 0.0;
    float total = 0.0, priority = -3.4e38;
    for (int i = 0; i < _ZeroWeatherRegionCount; i++)
    {
        float4 b = _ZeroWeatherRegionBounds[i];
        float4 s = _ZeroWeatherRegionStates[i];
        float outside = max(0.0, max(max(b.x - p.x, p.x - b.z), max(b.y - p.z, p.z - b.w)));
        float weight = s.z > 0.0 ? 1.0 - smoothstep(0.0, s.z, outside) : (outside <= 0.0 ? 1.0 : 0.0);
        if (weight <= 0.0 || s.w < priority) continue;
        if (s.w > priority) { sum = 0.0; total = 0.0; priority = s.w; }
        sum += s.xy * weight;
        total += weight;
    }
    return total > 0.0 ? lerp(_ZeroWeatherDefault.xy, saturate(sum / total), saturate(total)) : _ZeroWeatherDefault.xy;
}

float ZeroExposureSample(float2 uv, float depth, int index)
{
    if (index == 0)
        return step(SAMPLE_TEXTURE2D_LOD(_ZeroWeatherRainExposure, sampler_ZeroWeatherRainExposure, uv, 0).r, depth);
    return step(SAMPLE_TEXTURE2D_LOD(_ZeroWeatherSnowExposure, sampler_ZeroWeatherSnowExposure, uv, 0).r, depth);
}

float ZeroSceneWeatherExposure(float3 p, int index)
{
    if (_ZeroWeatherExposureReady < 0.5) return 1.0;
    float2 uv = float2(dot(float4(p, 1.0), _ZeroWeatherExposureU[index]),
        dot(float4(p, 1.0), _ZeroWeatherExposureV[index]));
    if (any(uv < 0.0) || any(uv > 1.0)) return 1.0;
    float2 pixel = uv * _ZeroWeatherExposureSize.y - 0.5;
    float2 origin = (floor(pixel) + 0.5) * _ZeroWeatherExposureSize.x;
    float2 f = frac(pixel);
    float depth = dot(p, _ZeroWeatherExposureAxis[index].xyz) + _ZeroWeatherExposureAxis[index].w;
    float a = ZeroExposureSample(origin, depth, index);
    float b = ZeroExposureSample(origin + float2(_ZeroWeatherExposureSize.x, 0), depth, index);
    float c = ZeroExposureSample(origin + float2(0, _ZeroWeatherExposureSize.x), depth, index);
    float d = ZeroExposureSample(origin + _ZeroWeatherExposureSize.xx, depth, index);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float2 ZeroSampleSceneSurfaceWeather(float3 p)
{
    return ZeroSceneWeatherState(p) * float2(ZeroSceneWeatherExposure(p, 0), ZeroSceneWeatherExposure(p, 1));
}

float4 ZeroApplySceneSurfaceWeather(float3 albedo, float smoothness, float3 positionWS, float3 normalWS)
{
    if (_ZeroSceneWeatherEnabled < 0.5) return float4(albedo, smoothness);
    float2 state = ZeroSampleSceneSurfaceWeather(positionWS);
    float coverage = state.y * smoothstep(0.35, 0.85, normalize(normalWS).y);
    float grain = frac(sin(dot(floor(positionWS.xz * 35.0), float2(127.1, 311.7))) * 43758.5453);
    float mask = coverage * lerp(0.85, 1.0, grain);
    albedo = lerp(albedo * (1.0 - state.x * 0.38), _ZeroWeatherSnowColor.rgb, mask);
    smoothness = lerp(lerp(smoothness, 0.9, state.x), 0.2, mask);
    return float4(albedo, smoothness);
}
#endif
