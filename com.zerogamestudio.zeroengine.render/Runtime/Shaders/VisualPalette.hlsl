#ifndef ZEROENGINE_VISUAL_PALETTE_INCLUDED
#define ZEROENGINE_VISUAL_PALETTE_INCLUDED
// Values are explicit shader-space coefficients; callers choose any colour-space conversion.
half3 ZEVisualColor(half3 source, half mode, half3 ink)
{
    if (mode < .5) return source;
    return max(source.r, max(source.g, source.b)) * ink;
}
half4 ZEVisualColor(half4 source, half mode, half3 ink) { return half4(ZEVisualColor(source.rgb, mode, ink),source.a); }
#endif
