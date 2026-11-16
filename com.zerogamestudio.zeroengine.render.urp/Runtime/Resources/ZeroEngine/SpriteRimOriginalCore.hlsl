#ifndef ZE_SPRITE_RIM_ORIGINAL_INCLUDED
#define ZE_SPRITE_RIM_ORIGINAL_INCLUDED

// Instruction-equivalent reconstruction of the shipped light/rim/exposure sprite variant.
// Keep this independent of URP so synthetic inputs can be compared with the original DXBC.
float2 OriginalRimShift(float2 uv, float2 center, float2 size, float scale, float2 distance, float2 texel)
{
    return (uv - center * texel) / ((size + scale) / size) + center * texel - distance * texel * 100;
}

float3 OriginalRimBlend(float3 original, float4 light, int mode, float blend, float maximum,
    float exposure, float threshold, float offset, float globalLocalExposure)
{
    float3 target = original;
    float amount = light.a * blend;
    switch (mode)
    {
        case 1: target = light.rgb; break;
        case 2: target = float3(original.r < .5 ? 2 * light.r * original.r : 1 - 2 * (1-original.r) * (1-light.r),
                               original.g < .5 ? 2 * light.g * original.g : 1 - 2 * (1-original.g) * (1-light.g),
                               original.b < .5 ? 2 * light.b * original.b : 1 - 2 * (1-original.b) * (1-light.b)); break;
        case 3: target = 1 - (1-original) * (1-light.rgb); break;
        case 4: target = original * light.rgb; break;
        case 5: target = original + light.rgb; break;
        // The red swizzle on saturated green/blue is present in the shipped shader.
        case 6: target = float3(light.r >= 1 ? light.r : original.r / (1-light.r),
                               light.g >= 1 ? light.r : original.g / (1-light.g),
                               light.b >= 1 ? light.r : original.b / (1-light.b)); break;
        case 7: target = max(original, light.rgb); amount = blend; break;
        case 8: target = min(original, light.rgb); amount = blend; break;
        default: amount = 0; break;
    }
    float3 result = min(original + (target-original) * amount, maximum);
    if (dot(result, float3(.2989,.587,.114)) > threshold) result = result * exp2(exposure) + offset;
    return result * globalLocalExposure;
}

float4 OriginalRimField(float2 uv, float4 color, float4 powers, int shape, float angle)
{
    float attenuation;
    if (shape == 0)
    {
        float distance = length(uv);
        if (distance >= 1) return 0;
        attenuation = pow(1-distance, powers.x);
    }
    else if (shape == 1)
    {
        float2 edge = 1-abs(uv);
        if (any(edge <= 0)) return 0;
        attenuation = pow(edge.x, powers.y) * pow(edge.y, powers.z);
    }
    else
    {
        float distance = length(uv), direction = abs(atan2(uv.x,uv.y));
        // Original DXBC discards the NaN produced outside the cone/radius. Make that domain
        // explicit so other shader compilers cannot turn the negated NaN comparison into coverage.
        if (distance >= 1 || direction >= angle) return 0;
        attenuation = pow(1-direction/angle, powers.w) * pow(1-distance, powers.x);
    }
    float alpha = attenuation * color.a;
    return float4(color.rgb * alpha, alpha);
}
#endif
