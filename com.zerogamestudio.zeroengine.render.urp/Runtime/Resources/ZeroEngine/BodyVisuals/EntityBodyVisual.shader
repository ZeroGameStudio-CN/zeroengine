Shader "ZeroEngine/BodyVisuals/EntityBodyVisual"
{
    Properties
    {
        [HideInInspector] _VisualPaletteMode ("Palette mode", Float) = 0
        [HideInInspector] _VisualPaletteInk ("Palette ink", Vector) = (.2,.22,.25,1)

        [PerRendererData] _MainTex ("身体贴图", 2D) = "white" {}
        _Tint ("效果颜色", Color) = (0.16,0.07,0.28,0.65)
        _Mode ("表现类型", Float) = 0
        _Intensity ("亮度", Float) = 1
        _Opacity ("透明度", Float) = 1
        _PatternScale ("纹理密度", Float) = 9
        _MotionSpeed ("流动速度", Float) = 1.4
        _BodyClock ("实体时钟", Float) = 0
        [HideInInspector] _BodyPresence ("Presence", Float) = -1
        [HideInInspector] _BodyRetiring ("Retiring", Float) = 0
        [HideInInspector] _BodyMask ("实时身体轮廓", 2D) = "black" {}
        [HideInInspector] _BodyPixelSize ("来源像素尺寸", Float) = 0.0625
        [HideInInspector] _BodyLayer ("表面或附着层", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BodyMask); SAMPLER(sampler_BodyMask);
            CBUFFER_START(UnityPerMaterial)
            float _VisualPaletteMode;
            half4 _VisualPaletteInk;
                float4 _MainTex_TexelSize, _BodyMask_TexelSize, _Flip, _BodyBounds;
                float4x4 _BodyWorldToLocal;
                half4 _Tint, _RendererColor;
                float _Mode, _Intensity, _Opacity, _PatternScale, _MotionSpeed, _BodyClock, _BodyLayer, _BodyPixelSize;
                float _BodyPresence, _BodyRetiring;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 body : TEXCOORD1; half alpha : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz * _Flip.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.body = (mul(_BodyWorldToLocal, float4(world, 1)).xy - _BodyBounds.xy) * _BodyBounds.zw + 0.5;
                output.alpha = input.color.a * _RendererColor.a;
                return output;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            // Every effect cell follows the source texel scale, anchored to the entity instead of its animated bounds.
            float2 pixelCell(float2 body)
            {
                float2 local = (body-0.5) / _BodyBounds.zw + _BodyBounds.xy;
                return floor(local / max(_BodyPixelSize,0.00001));
            }
            float pixelMask(float2 cell)
            {
                float2 local = (floor(cell)+0.5) * _BodyPixelSize;
                float2 uv = (local-_BodyBounds.xy) * _BodyBounds.zw + 0.5;
                if (any(uv < 0) || any(uv > 1)) return 0;
                return step(0.5,SAMPLE_TEXTURE2D(_BodyMask,sampler_BodyMask,uv).a);
            }
            half3 frostColor(float shade)
            {
                half3 tint = _Tint.rgb / max(0.001,max(_Tint.r,max(_Tint.g,_Tint.b)));
                half3 dark = lerp(half3(0.24,0.40,0.54),tint*0.55,0.3);
                half3 middle = lerp(half3(0.56,0.76,0.84),tint,0.12);
                return shade > 1.5 ? half3(0.88,0.96,0.94) : shade > 0.5 ? middle : dark;
            }
            half3 flameColor(float shade)
            {
                half3 tint = _Tint.rgb / max(0.001,max(_Tint.r,max(_Tint.g,_Tint.b)));
                half3 dark = lerp(tint*0.55,half3(0.62,0.12,0.035),0.35);
                half3 middle = lerp(tint,half3(1,0.40,0.065),0.75);
                return shade > 1.5 ? half3(1,0.80,0.25) : shade > 0.5 ? middle : dark;
            }
            float frostCluster(float2 cell, out float shade)
            {
                float width = max(2,floor(27/_PatternScale+0.5));
                float2 tile = floor(cell/width), part = cell-tile*width;
                float seed = hash(tile+17.3);
                float edge = 1-min(min(pixelMask(cell+float2(2,0)),pixelMask(cell-float2(2,0))),
                    min(pixelMask(cell+float2(0,2)),pixelMask(cell-float2(0,2))));
                float cut = part.x+part.y;
                float shape = (cut >= 1 && cut <= width*1.45) ? 1 : 0;
                float patch = step(lerp(0.68,0.38,edge),seed)*shape;
                float top = (1-pixelMask(cell+float2(0,1))) * step(0.3,hash(floor(cell/3)+4.7));
                shade = top > 0.5 ? 2 : part.y >= width-1 ? 2 : cut >= width ? 1 : 0;
                return max(patch*0.82,top);
            }
            half4 atmosphere(float2 body)
            {
                float2 cell = pixelCell(body);
                float inside = pixelMask(cell), alpha = 0;
                half3 color = 0;
                if (_Mode < 2.5)
                {
                    // Sparse two/three-pixel ice tips. No expanded all-body glow.
                    float column = floor(cell.x/4);
                    float chosen = step(0.74,hash(float2(column,13)));
                    float tip = chosen * (1-inside) * (pixelMask(cell+float2(0,1))+
                        pixelMask(cell+float2(0,2))*step(0.5,fmod(abs(cell.x),4)));
                    alpha = min(tip,1)*0.8;
                    color = frostColor(pixelMask(cell+float2(0,1)) > 0.5 ? 1 : 2);
                }
                else
                {
                    // A tiny sprite-like flame in each selected edge tile. Eight discrete owner-clock poses/second.
                    float frame = floor(_BodyClock*_MotionSpeed*6);
                    float2 tileSize = float2(clamp(floor(72/_PatternScale+0.5),6,12),11);
                    float2 tile = floor(cell/tileSize), origin = tile*tileSize;
                    float seed = hash(tile+8.9);
                    float2 root = origin+float2(floor(tileSize.x*0.5),1+floor(seed*2));
                    float rooted = 0;
                    [unroll] for (int i=0;i<3;i++)
                    {
                        float2 candidate = root+float2(i==1 ? -2 : i==2 ? 2 : 0,0);
                        float border = 1-min(min(pixelMask(candidate+float2(2,0)),pixelMask(candidate-float2(2,0))),
                            pixelMask(candidate+float2(0,2)));
                        float valid = pixelMask(candidate)*border;
                        if (rooted < 0.5 && valid > 0.5) { root=candidate; rooted=1; }
                    }
                    rooted *= step(0.18,seed);
                    float phase = fmod(frame+floor(seed*4),4);
                    float limit = clamp(floor(0.17/(_BodyBounds.w*_BodyPixelSize)),3,8);
                    float height = max(3,limit-(phase==0 ? 1 : 0)-floor(seed*2));
                    float2 d = cell-root;
                    float bend = (phase==1 ? 1 : phase==3 ? -1 : 0)*step(height*0.5,d.y);
                    float x = abs(d.x-bend);
                    float width = d.y < 2 ? 2 : d.y < height-2 ? 1 : 0;
                    float flame = (d.y >= 0 && d.y < height && x <= width) ? rooted : 0;
                    float shade = d.y < height*0.4 && x < 0.5 ? 2 : x < width || width < 0.5 ? 1 : 0;
                    color = flameColor(shade);
                    alpha = flame*(1-inside*0.72);
                    float sparkY = height+1+fmod(phase,2);
                    float spark = rooted*step(0.58,seed)*((d.y==sparkY && d.x==bend) ? 1 : 0);
                    if (spark > 0.5) { color=flameColor(2); alpha=0.9; }
                }
                float presence = _BodyPresence < 0 ? 1 : smoothstep(0,1,_BodyPresence);
                if (_Mode < 2.5 && _BodyPresence >= 0)
                    alpha *= smoothstep(hash(floor(cell/3)) * 0.65,0.9,_BodyPresence);
                return half4(saturate(color*_Intensity),alpha*_Tint.a*_Opacity*presence*step(0.001,_Intensity));
            }
            half4 frag(Varyings input) : SV_Target
            {
                if (_BodyLayer > 0.5) return atmosphere(input.body);
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float2 texel = _MainTex_TexelSize.xy;
                half inner = min(min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(texel.x,0)).a,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(texel.x,0)).a),
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0,texel.y)).a,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0,texel.y)).a));
                half edge = saturate((source.a - inner) * 3);
                half3 color = _Tint.rgb;
                half coverage = 1;
                float2 p = input.body * _PatternScale;
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    color *= 1.4 + edge;
                    coverage = 0.12 + edge * 0.88;
                }
                else if (_Mode > 1.5 && _Mode < 2.5)
                {
                    float shade;
                    coverage = frostCluster(pixelCell(input.body),shade);
                    if (_BodyPresence >= 0)
                        coverage *= smoothstep(hash(floor(pixelCell(input.body)/3)) * 0.65,0.9,_BodyPresence);
                    color = frostColor(shade);
                }
                else if (_Mode > 2.5)
                {
                    float2 cell = pixelCell(input.body);
                    float edge = 1-min(pixelMask(cell+float2(1,0)),pixelMask(cell-float2(1,0)));
                    float frame = floor(_BodyClock*_MotionSpeed*6);
                    coverage = edge*step(0.72,hash(floor(cell/3)+floor(frame/4)))*0.18;
                    color = flameColor(0);
                }
                if (_Mode > 0.5 && _BodyPresence >= 0) coverage *= smoothstep(0,1,_BodyPresence);
                if (_Mode > 1.5)
                    return half4(saturate(color*_Intensity),source.a*input.alpha*_Tint.a*saturate(_Opacity)*coverage*step(0.001,_Intensity));
                return half4(color * _Intensity, source.a * input.alpha * _Tint.a * saturate(_Opacity) * coverage);
            }
            Varyings maskVert(Attributes input)
            {
                Varyings output = vert(input);
                output.positionCS = float4(output.body*2-1,0,1);
                #if UNITY_UV_STARTS_AT_TOP
                    output.positionCS.y = -output.positionCS.y;
                #endif
                return output;
            }
            half4 maskFrag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a*input.alpha;
                return half4(alpha,alpha,alpha,alpha);
            }
        ENDHLSL
        Pass
        {
            Name "EntityBodyVisual"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "BodySilhouette"
            Tags { "LightMode"="BodySilhouette" }
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex maskVert
            #pragma fragment maskFrag
            ENDHLSL
        }
    }
}
