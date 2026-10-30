Shader "ZeroEngine/BodyVisuals/EntityBodySurface"
{
    Properties
    {
        [HideInInspector] _VisualPaletteMode ("Palette mode", Float) = 0
        [HideInInspector] _VisualPaletteInk ("Palette ink", Vector) = (.2,.22,.25,1)

        [PerRendererData] _MainTex ("身体贴图", 2D) = "white" {}
        _FrostMask ("表面纹理遮罩", 2D) = "gray" {}
        [HideInInspector] _FlameAtlas ("Shared flame flipbook", 2D) = "black" {}
        _FrostAmount ("结霜覆盖", Range(0,1)) = 0.72
        _FrostScale ("霜纹尺度", Range(0.2,4)) = 0.9
        _Tint ("效果颜色", Color) = (0.3,0.7,1,0.8)
        _Mode ("表现类型", Float) = 1
        _Intensity ("亮度", Float) = 1
        _Opacity ("透明度", Float) = 1
        _PatternScale ("纹理密度", Float) = 9
        _MotionSpeed ("动画速度", Float) = 1
        _BodyClock ("实体时钟", Float) = 0
        [HideInInspector] _BodyPresence ("Presence", Float) = -1
        [HideInInspector] _BodyRetiring ("Retiring", Float) = 0
        _EffectPadding ("附着层留边（侧/上/下像素）", Vector) = (1,1,1,0)
        [HideInInspector] _BodySurface ("贴体外观", Float) = 1
        [HideInInspector] _BodyMask ("实时身体轮廓", 2D) = "black" {}
        [HideInInspector] _BodyPixelSize ("来源像素尺寸", Float) = 0.0625
        [HideInInspector] _BodySourceTexelSize ("原图贴图像素尺寸", Vector) = (1,1,1,1)
        [HideInInspector] _BodySourceUVRect ("Sprite原图范围", Vector) = (0,0,1,1)
        [HideInInspector] _BodyLayer ("表面/薄边", Float) = 0
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
        #include "Packages/com.zerogamestudio.zeroengine.render/Runtime/Shaders/VisualPalette.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BodyMask); SAMPLER(sampler_BodyMask);
        TEXTURE2D(_FrostMask); SAMPLER(sampler_FrostMask);
        TEXTURE2D(_FlameAtlas); SAMPLER(sampler_FlameAtlas);
        CBUFFER_START(UnityPerMaterial)
            float _VisualPaletteMode;
            half4 _VisualPaletteInk;
            float4 _BodyBounds, _BodySourceTexelSize, _BodySourceUVRect, _EffectPadding, _Flip;
            float4 _FlameAtlas_TexelSize;
            float4x4 _BodyWorldToLocal;
            half4 _Tint, _RendererColor;
            float _Mode, _Intensity, _Opacity, _PatternScale, _MotionSpeed, _BodyClock, _BodyLayer;
            float _BodyPixelSize, _FrostAmount, _FrostScale, _BodySurface;
            float _BodyPresence, _BodyRetiring;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 body : TEXCOORD1; float2 opaqueY : TEXCOORD2; half4 color : COLOR; };
        float2 opaqueVerticalLimits()
        {
            // Sample only the four accent-quad vertices, not every fragment. Transparent
            // Sprite padding must not place markers or low poison puffs below the actual feet.
            float2 limits = float2(1,0);
            [loop] for (int y=0; y<32; y++)
            {
                float v = (y + 0.5) / 32;
                float row = 0;
                [unroll] for (int x=0; x<6; x++)
                    row = max(row,SAMPLE_TEXTURE2D_LOD(_BodyMask,sampler_BodyMask,float2((x + 0.5) / 6,v),0).a);
                if (row > 0.5) { limits.x = min(limits.x,v); limits.y = max(limits.y,v); }
            }
            return limits.y >= limits.x ? saturate(limits + float2(-0.5,0.5) / 32) : float2(0,1);
        }
        Varyings vert(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz * _Flip.xyz);
            output.positionCS = TransformWorldToHClip(world);
            output.uv = input.uv;
            output.body = (mul(_BodyWorldToLocal, float4(world,1)).xy - _BodyBounds.xy) * _BodyBounds.zw + 0.5;
            output.opaqueY = _BodyLayer > 0.5 && ((_Mode > 3.5 && _Mode < 4.5) || (_Mode > 6.5 && _Mode < 8.5)) ?
                opaqueVerticalLimits() : float2(0,1);
            output.color = input.color * _RendererColor;
            return output;
        }
        float maskAt(float2 body)
        {
            if (any(body < 0) || any(body > 1)) return 0;
            return step(0.5,SAMPLE_TEXTURE2D(_BodyMask,sampler_BodyMask,body).a);
        }
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
        float noise(float2 p)
        {
            float2 cell = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(hash(cell),hash(cell + float2(1,0)),f.x),
                lerp(hash(cell + float2(0,1)),hash(cell + 1),f.x),f.y);
        }
        float sourceAlpha(float2 uv)
        {
            if (any(uv < _BodySourceUVRect.xy) || any(uv > _BodySourceUVRect.xy + _BodySourceUVRect.zw)) return 0;
            return step(0.5,SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv).a);
        }
        float electricPulse(float clock)
        {
            // Deterministic irregular bursts: the owner clock freezes both phase and pattern.
            float cycle = floor(clock * 1.35);
            float phase = frac(clock * 1.35);
            float start = 0.06 + hash(float2(cycle,7)) * 0.3;
            float width = 0.045 + hash(float2(cycle,19)) * 0.04;
            float first = smoothstep(start,start + 0.012,phase) * (1 - smoothstep(start + width,start + width + 0.03,phase));
            float echoStart = start + width + 0.055 + hash(float2(cycle,31)) * 0.08;
            float echo = smoothstep(echoStart,echoStart + 0.012,phase) * (1 - smoothstep(echoStart + 0.025,echoStart + 0.055,phase));
            return max(first,echo * 0.52);
        }
        float2 localAt(float2 body) { return (body - 0.5) / _BodyBounds.zw + _BodyBounds.xy; }
        float maskCell(float2 cell)
        {
            return maskAt(((floor(cell) + 0.5) * _BodyPixelSize - _BodyBounds.xy) * _BodyBounds.zw + 0.5);
        }
        half4 smallFlames(float2 body, float clock, float growth)
        {
            float2 cell = floor(localAt(body) / _BodyPixelSize);
            float2 tile = floor(cell / float2(8,10));
            float seed = hash(tile + 8.9);
            float2 root = tile * float2(8,10) + float2(4,1 + floor(seed * 2));
            float rooted = 0;
            [unroll] for (int i=0; i<3; i++)
            {
                float2 candidate = root + float2(i == 1 ? -2 : i == 2 ? 2 : 0,0);
                float edge = 1 - min(min(maskCell(candidate + float2(2,0)),maskCell(candidate - float2(2,0))),
                    maskCell(candidate + float2(0,2)));
                if (rooted < 0.5 && maskCell(candidate) * edge > 0.5) { root = candidate; rooted = 1; }
            }
            rooted *= step(0.4,seed);
            float frame = fmod(floor(clock * 7) + floor(seed * 4),4);
            float height = (4 + floor(seed * 3) - step(2.5,frame)) * growth;
            float2 d = cell - root;
            float bend = (frame == 1 ? 1 : frame == 3 ? -1 : 0) * step(height * 0.45,d.y);
            float width = d.y < 1.5 ? 1.4 : d.y < height - 2 ? 0.8 : 0.2;
            float flame = rooted * step(0,d.y) * (1 - step(height,d.y)) * step(abs(d.x - bend),width);
            float hot = step(abs(d.x),0.4) * (1 - step(height * 0.45,d.y));
            half3 color = lerp(saturate(_Tint.rgb) * 0.85,saturate(_Tint.rgb * 0.5 + half3(0.5,0.55,0.1)),hot);
            if (_FlameAtlas_TexelSize.z > 8)
            {
                // Reuse the project's authored flame shape at a small, source-pixel scale.
                float sheetFrame = fmod(floor(clock * 9) + floor(seed * 16),16);
                float2 tileUV = float2(fmod(sheetFrame,4),floor(sheetFrame / 4));
                float2 q = float2((d.x - bend * 0.4) / 7 + 0.5,d.y / max(9 * growth,0.05));
                float valid = step(0,q.x) * step(q.x,1) * step(0,q.y) * step(q.y,1);
                q = clamp(q,0.01,0.99);
                half3 sheet = SAMPLE_TEXTURE2D_LOD(_FlameAtlas,sampler_FlameAtlas,(tileUV + q) * 0.25,0).rgb;
                float heat = max(sheet.r,max(sheet.g,sheet.b));
                flame = rooted * valid * smoothstep(0.025,0.18,heat);
                // Green marks the hot core; red-only wisps stay orange instead of turning pale yellow.
                color = lerp(saturate(_Tint.rgb * half3(1,0.7,0.6)),
                    saturate(_Tint.rgb * 0.2 + half3(0.8,0.78,0.18)),smoothstep(0.2,0.9,sheet.g));
            }
            if (_BodyRetiring > 0.5) color = lerp(half3(0.42,0.045,0.008),color,growth);
            return half4(color * saturate(_Intensity),flame * growth * _Tint.a * _Opacity * 0.9);
        }
        half4 poisonBubbles(float2 body, float clock, float growth, float2 opaqueY)
        {
            float2 pixel = _BodyPixelSize * _BodyBounds.zw;
            float radius = 3.8; // Keep skull dimensions independent of the current pose.
            float bottom = opaqueY.x + pixel.y * (radius + 1);
            float top = max(bottom + pixel.y * radius * 2,opaqueY.y + pixel.y);
            float alpha = 0;
            half3 color = 0;
            // Each skull has one stable lifetime and path. Pose alpha never decides whether it exists.
            [unroll] for (int i=0; i<5; i++)
            {
                float time = clock / (3.2 + i * 0.13) + i * 0.217;
                float cycle = floor(time), age = frac(time);
                float seed = hash(float2(i + 17,cycle + 39));
                float fade = smoothstep(0,0.18,age) * (1 - smoothstep(0.72,1,age));
                float2 center = float2(0.3 + i * 0.1 + (seed - 0.5) * 0.06,lerp(bottom,top,age));
                center.x += sin(age * 3.2 + seed * 6.28318) * pixel.x * radius * 0.7;
                float2 p = (body - center) / max(pixel * radius,float2(0.00001,0.00001));
                // Rounded cranium, cheekbones and tapered jaw dissolve into the same soft haze.
                float tilt = (seed - 0.5) * 0.32;
                p = float2(p.x + p.y * tilt,p.y - p.x * tilt);
                float head = 1 - smoothstep(0.78,1.08,length((p - float2(0,0.23)) / float2(0.67,0.65)));
                float jawWidth = lerp(0.29,0.43,smoothstep(-0.7,-0.18,p.y));
                float jaw = 1 - smoothstep(0.76,1.12,length((p - float2(0,-0.32)) / float2(jawWidth,0.43)));
                float cheeks = 1 - smoothstep(0.65,1.15,length((float2(abs(p.x),p.y) - float2(0.43,-0.1)) / float2(0.2,0.24)));
                head = max(head,cheeks * 0.86);
                float skull = max(head,jaw);
                float mist = (1 - smoothstep(0.6,1.3,length(p * float2(0.9,1) + float2(0,0.04)))) * 0.18;
                float wisp = (1 - smoothstep(0.35,1,length((p - float2(sin(p.y * 3 + seed * 6.28318) * 0.12,-0.48)) / float2(0.4,0.66)))) * 0.13;
                float amount = fade * saturate(skull * 0.58 + max(mist,wisp));
                if (amount <= alpha) continue;
                float2 socket = float2(abs(p.x) - 0.26,p.y - 0.14);
                socket.y += socket.x * 0.42;
                float eyes = 1 - smoothstep(0.55,1.25,length(socket / float2(0.18,0.21)));
                float noseWidth = lerp(0.12,0.025,saturate((p.y + 0.31) / 0.25));
                float nose = (1 - smoothstep(noseWidth * 0.65,noseWidth + 0.035,abs(p.x))) *
                    smoothstep(-0.36,-0.25,p.y) * (1 - smoothstep(-0.1,-0.03,p.y));
                float mouth = (1 - smoothstep(0.025,0.085,abs(p.y + 0.47 + p.x * p.x * 0.25))) *
                    (1 - smoothstep(0.2,0.34,abs(p.x)));
                float teeth = (1 - smoothstep(0.015,0.055,abs(abs(p.x) - 0.12))) *
                    smoothstep(-0.66,-0.58,p.y) * (1 - smoothstep(-0.44,-0.36,p.y));
                half3 fogTint = lerp(_Tint.rgb,dot(_Tint.rgb,half3(0.299,0.587,0.114)).xxx,0.18);
                float crown = saturate(p.y * 0.45 + 0.55) * skull;
                color = fogTint * lerp(0.64,1.02,crown) + half3(0.035,0.025,0.045) * skull;
                color = lerp(color,fogTint * 0.22,max(eyes,max(nose,max(mouth,teeth) * 0.6)) * 0.78);
                alpha = amount;
            }
            return half4(saturate(color * _Intensity),saturate(alpha * growth) * _Tint.a * _Opacity);
        }
        half4 weaknessMark(float2 body, float clock, float growth, float2 opaqueY)
        {
            float unit = _BodyPixelSize * 2.25; // Pose controls placement, never symbol dimensions.
            float2 local = localAt(body);
            float top = _BodyBounds.y + (opaqueY.y - 0.5) / _BodyBounds.w;
            float2 p = (local - float2(0,top + unit * 1.35)) / unit;
            p = (floor(p * 5) + 0.5) / 5;
            float diamond = 1 - smoothstep(0.11,0.24,abs(abs(p.x) + abs(p.y) - 0.9));
            float split = smoothstep(0.06,0.16,abs(p.x + p.y * 0.27));
            float core = step(abs(p.x),0.13) * step(abs(p.y + 0.12),0.32);
            float alpha = max(diamond * split,core * 0.7) * (0.86 + sin(clock * 2) * 0.14) * growth;
            return half4(saturate(_Tint.rgb * _Intensity),alpha * _Tint.a * _Opacity);
        }
        half4 resonanceRings(float2 body, float clock, float growth, float2 opaqueY)
        {
            float pixel = max(_BodyPixelSize,0.00001);
            float2 foot = float2(0,_BodyBounds.y + (opaqueY.x - 0.5) / _BodyBounds.w + pixel * 0.5);
            float2 delta = localAt(body) - foot;
            delta = (floor(delta / pixel) + 0.5) * pixel;
            float extent = pixel * 10;
            float radius = length(float2(delta.x,delta.y * 4));
            float alpha = 0;
            [unroll] for (int i=0; i<2; i++)
            {
                float phase = frac(clock * 0.85 + i * 0.5);
                float ring = 1 - smoothstep(pixel * 0.4,pixel * 0.95,abs(radius - extent * (0.25 + phase * 0.75)));
                alpha = max(alpha,ring * sin(phase * 3.14159));
            }
            return half4(saturate(_Tint.rgb * _Intensity),alpha * growth * _Tint.a * _Opacity * 0.75);
        }
        half4 BloodSurfaceOriginal(Varyings input) : SV_Target
        {
            float2 pixel = _BodyPixelSize * _BodyBounds.zw;
            float presence = _BodyPresence < 0 ? smoothstep(0.05,1.25,_BodyClock) : saturate(_BodyPresence);
            float growth = _BodyPresence < 0 ? presence : smoothstep(0,1,presence);
            float clock = _BodyClock * _MotionSpeed;
            if (_BodyLayer > 0.5)
            {
                if (_Mode > 2.5 && _Mode < 3.5) return smallFlames(input.body,clock,growth);
                if (_Mode > 3.5 && _Mode < 4.5) return poisonBubbles(input.body,clock,growth,input.opaqueY);
                if (_Mode > 6.5 && _Mode < 7.5) return weaknessMark(input.body,clock,growth,input.opaqueY);
                if (_Mode > 7.5 && _Mode < 8.5) return resonanceRings(input.body,clock,growth,input.opaqueY);
                if (_Mode > 5.5 && _Mode < 6.5) return 0; // Blood drops have their own world-space mesh.
                float outside = 1 - maskAt(input.body);
                float edge = max(max(maskAt(input.body + float2(pixel.x,0)),maskAt(input.body - float2(pixel.x,0))),
                    max(maskAt(input.body + float2(0,pixel.y)),maskAt(input.body - float2(0,pixel.y))));
                if (_Mode > 8.5)
                {
                    float shell = max(max(maskAt(input.body + float2(pixel.x * 2,0)),maskAt(input.body - float2(pixel.x * 2,0))),edge);
                    return half4(0.61,0.87,1,outside * shell * growth * _Tint.a * _Opacity * 0.65);
                }
                if (_Mode < 2.5)
                {
                    float upperEdge = maskAt(input.body - float2(0,pixel.y * 0.7));
                    return half4(0.43,0.61,0.67,outside * upperEdge * growth * 0.45 * _Tint.a * _Opacity);
                }
                float patch = noise(input.body * float2(11,17) - float2(0,floor(clock * 1.35) / 6));
                float pulse = _BodyRetiring > 0.5 ? 0.12 * growth : electricPulse(clock);
                float band = 1 - smoothstep(0.16,0.38,abs(input.body.y - (0.22 + hash(float2(floor(clock * 1.35),43)) * 0.56)));
                return half4(saturate(_Tint.rgb * _Intensity),outside * edge * step(0.55,patch) * pulse * band * growth * _Tint.a * _Opacity);
            }
            // These channels identify the state outside the body; they never recolour its torso.
            if ((_Mode > 2.5 && _Mode < 4.5) || (_Mode > 6.5 && _Mode < 8.5)) return 0;
            half4 source = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
            float2 localUV = (input.uv - _BodySourceUVRect.xy) / max(_BodySourceUVRect.zw,float2(0.0001,0.0001));
            float2 uv = localUV * _FrostScale + float2(0.13,0.27);
            float frost = SAMPLE_TEXTURE2D(_FrostMask,sampler_FrostMask,uv).r;
            float value = dot(source.rgb,half3(0.2126,0.7152,0.0722));
            if (_Mode > 1.5 && _Mode < 2.5)
            {
                float upperRim = 1 - maskAt(input.body + float2(0,pixel.y));
                float deposit = smoothstep(0.26,0.65,frost + upperRim * 0.12);
                float face = (1 - smoothstep(0.11,0.21,abs(input.body.x - 0.6))) * smoothstep(0.72,0.82,input.body.y);
                float coverage = deposit * growth * _FrostAmount * (1 - face * 0.75);
                float crystalSeed = saturate(0.1 + (1 - frost) * 0.45 + localUV.y * 0.24 - upperRim * 0.1);
                if (_BodyPresence >= 0) coverage *= smoothstep(crystalSeed,crystalSeed + 0.18,presence);
                half3 icy = lerp(half3(0.20,0.32,0.38),half3(0.66,0.77,0.79),saturate(value * 0.85 + frost * 0.24));
                float fine = SAMPLE_TEXTURE2D(_FrostMask,sampler_FrostMask,uv + float2(0.001,0.001)).r;
                icy += half3(0.6,0.8,0.9) * (saturate((frost - fine) * 7) * 0.14 + upperRim * deposit * 0.12);
                return half4(saturate(lerp(source.rgb * half3(0.78,0.91,0.97),icy,0.77) * _Intensity),
                    source.a * input.color.a * coverage * _Tint.a * _Opacity);
            }
            float2 texel = _BodySourceTexelSize.xy;
            float rim = 1 - min(min(sourceAlpha(input.uv + float2(texel.x,0)),sourceAlpha(input.uv - float2(texel.x,0))),
                min(sourceAlpha(input.uv + float2(0,texel.y)),sourceAlpha(input.uv - float2(0,texel.y))));
            float2 resolution = max(_BodySourceUVRect.zw * _BodySourceTexelSize.zw,float2(8,8));
            float2 q = (floor(localUV * resolution) + 0.5) / resolution;
            half3 tint = saturate(_Tint.rgb);
            half3 color;
            float coverage;
            if (_Mode > 8.5)
            {
                float2 crystal = q * float2(8,11);
                float2 tile = floor(crystal), f = frac(crystal);
                float plane = step(f.x,f.y);
                float facet = lerp(hash(tile),hash(tile + 37),plane);
                float crack = 1 - smoothstep(0.018,0.065,min(min(f.x,f.y),abs(f.x - f.y)));
                float reveal = smoothstep(0.06 + hash(tile + 9) * 0.52,0.31 + hash(tile + 9) * 0.52,presence);
                color = lerp(source.rgb * half3(0.55,0.82,0.96),lerp(tint * 0.7,half3(0.6,0.83,0.94),facet),0.55);
                color += crack * half3(0.07,0.10,0.13) + rim * half3(0.06,0.10,0.13);
                coverage = growth * reveal * (0.72 + crack * 0.16 + rim * 0.1);
            }
            else if (_Mode > 5.5)
            {
                // Small fixed-pixel scars, never long UV-space cuts across an attack sprite.
                float2 sourceCell = floor(localAt(input.body) / max(_BodyPixelSize,0.00001));
                float2 tile = floor(sourceCell / float2(6,8));
                float2 cell = sourceCell - tile * float2(6,8);
                float chosen = step(.68,hash(tile + 29));
                float cut = step(1,cell.x) * step(cell.x,3) * step(2,cell.y) * step(cell.y,3);
                cut = max(cut,step(3,cell.x) * step(cell.x,3) * step(4,cell.y) * step(cell.y,4));
                cut *= chosen * step(.2,input.body.y) * step(input.body.y,.8);
                color = tint * lerp(.48,.9,step(cell.x,2));
                coverage = cut * growth * .5;
            }
            else if (_Mode > 4.5)
            {
                float tick = floor(clock * 1.35), segment = floor(q.y * 11);
                float bend = lerp(hash(float2(segment,tick)),hash(float2(segment + 1,tick)),frac(q.y * 11));
                float lineA = 0.24 + (bend - 0.5) * 0.24;
                float bendB = lerp(hash(float2(segment + 37,tick)),hash(float2(segment + 38,tick)),frac(q.y * 11));
                float lineB = 0.7 + (bendB - 0.5) * 0.22;
                float width = max(1 / resolution.x,0.012);
                float wire = 1 - smoothstep(width * 0.35,width,min(abs(q.x - lineA),abs(q.x - lineB)));
                float arcCenter = 0.22 + hash(float2(floor(clock * 1.35),43)) * 0.56;
                float band = 1 - smoothstep(0.15,0.38,abs(q.y - arcCenter));
                float branchLine = lineA + (q.y - arcCenter) * 0.7;
                float branch = (1 - smoothstep(width * 0.35,width,abs(q.x - branchLine))) * (1 - smoothstep(0.04,0.15,abs(q.y - arcCenter)));
                wire = max(wire * band,branch * 0.7);
                float pulse = max(electricPulse(clock),(1 - smoothstep(0.08,0.22,_BodyClock)) * 0.65);
                if (_BodyRetiring > 0.5) pulse = 0.14 * growth;
                color = lerp(tint,half3(0.85,0.96,1),wire * 0.9);
                coverage = growth * pulse * saturate(wire * 0.95 + rim * band * 0.42);
            }
            else if (_Mode < 0.5)
            {
                color = lerp(tint * (0.15 + value * 0.45),tint + half3(0.12,0.07,0.2),rim * 0.7);
                coverage = 0.42 + rim * 0.48;
            }
            else
            {
                float shimmer = 0.5 + 0.5 * sin(clock * 3 + q.y * 9 + frost * 3);
                color = lerp(source.rgb * (0.7 + tint * 0.55),tint * (0.45 + value * 0.45) + 0.12,rim * 0.85);
                coverage = growth * (0.2 + rim * 0.6 + shimmer * 0.09);
            }
            return half4(saturate(color * _Intensity),source.a * input.color.a * saturate(coverage) * _Tint.a * _Opacity);
        }
        Varyings maskVert(Attributes input)
        {
            Varyings output = vert(input);
            output.positionCS = float4(output.body * 2 - 1,0,1);
            #if UNITY_UV_STARTS_AT_TOP
                output.positionCS.y = -output.positionCS.y;
            #endif
            return output;
        }
        half4 maskFrag(Varyings input) : SV_Target
        {
            half alpha = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a * input.color.a;
            return half4(alpha,alpha,alpha,alpha);
        }
        half4 frag(Varyings input) : SV_Target
        {
            half4 color = BloodSurfaceOriginal(input);
            return _Mode > 5.5 && _Mode < 6.5 ? ZEVisualColor(color, _VisualPaletteMode, _VisualPaletteInk.rgb) : color;
        }
            ENDHLSL
        Pass
        {
            Name "BodySurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "BodyMask"
            Tags { "LightMode"="BodySilhouette" }
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex maskVert
            #pragma fragment maskFrag
            ENDHLSL
        }
    }
}
