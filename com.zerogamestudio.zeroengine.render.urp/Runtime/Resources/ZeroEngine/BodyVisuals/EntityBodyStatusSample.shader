Shader "ZeroEngine/BodyVisuals/EntityBodyStatusSample"
{
    Properties
    {
        [PerRendererData] _MainTex ("身体贴图", 2D) = "white" {}
        _FlameAtlas ("保留的火焰参考贴图", 2D) = "black" {}
        [Enum(FrostCoat,0,IcePlates,1,DeepFreeze,2)] _Look ("对比方案", Float) = 0
        [Toggle] _UseParticles ("轮廓采样与独立火焰", Float) = 0
        _CrystalSize ("冰晶尺寸（原图像素）", Range(2,12)) = 3
        _IceThickness ("外沿冰层厚度（原图像素）", Range(0,8)) = 1
        _EffectPadding ("附着层留边（侧/上/下像素）", Vector) = (3,10,3,0)
        _VfxGrid ("火焰内部采样档位（0为连续）", Float) = 24
        _Tint ("效果颜色", Color) = (0.3,0.7,1,0.8)
        _Mode ("表现类型", Float) = 2
        _Intensity ("亮度", Float) = 1
        _Opacity ("透明度", Float) = 1
        _PatternScale ("纹理密度", Float) = 9
        _MotionSpeed ("动画速度", Float) = 1.4
        _BodyClock ("实体时钟", Float) = 0
        [HideInInspector] _BodyMask ("实时身体轮廓", 2D) = "black" {}
        [HideInInspector] _BodyPixelSize ("来源像素尺寸", Float) = 0.0625
        [HideInInspector] _BodySourceTexelSize ("原图贴图像素尺寸", Vector) = (1,1,1,1)
        [HideInInspector] _BodyLayer ("表面/冰沿/动态粒子", Float) = 0
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
        TEXTURE2D(_FlameAtlas); SAMPLER(sampler_FlameAtlas);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_TexelSize, _BodyMask_TexelSize, _Flip, _BodyBounds, _EffectPadding, _BodySourceTexelSize;
            float4x4 _BodyWorldToLocal;
            half4 _Tint, _RendererColor;
            float _Mode, _Intensity, _Opacity, _PatternScale, _MotionSpeed, _BodyClock, _BodyLayer, _BodyPixelSize;
            float _Look, _UseParticles, _CrystalSize, _IceThickness, _VfxGrid;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 particle : TEXCOORD1;
            float4 attachment : TEXCOORD2;
            half4 color : COLOR;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 body : TEXCOORD1;
            float4 particle : TEXCOORD2;
            float4 attachment : TEXCOORD3;
            half alpha : COLOR;
        };
        Varyings vert(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz * _Flip.xyz);
            output.positionCS = TransformWorldToHClip(world);
            output.uv = input.uv;
            output.body = (mul(_BodyWorldToLocal, float4(world,1)).xy - _BodyBounds.xy) * _BodyBounds.zw + 0.5;
            output.particle = input.particle;
            output.attachment = input.attachment;
            output.alpha = input.color.a * _RendererColor.a;
            return output;
        }
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
        float2 hash2(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)))) * 43758.5453); }
        float noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(hash(i),hash(i + float2(1,0)),f.x),lerp(hash(i + float2(0,1)),hash(i + 1),f.x),f.y);
        }
        float fbm(float2 p) { return noise(p) * 0.62 + noise(p * 2.07 + 7) * 0.26 + noise(p * 4.13 - 3) * 0.12; }
        float maskAt(float2 body)
        {
            if (any(body < 0) || any(body > 1)) return 0;
            return step(0.5,SAMPLE_TEXTURE2D(_BodyMask,sampler_BodyMask,body).a);
        }
        float3 crystal(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float first = 8, second = 8, seed = 0;
            [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 cell = float2(x,y);
                    float2 delta = cell + 0.15 + hash2(i + cell) * 0.7 - f;
                    float distance = dot(delta,delta);
                    if (distance < first) { second = first; first = distance; seed = hash(i + cell); }
                    else second = min(second,distance);
                }
            return float3(sqrt(first),sqrt(second) - sqrt(first),seed);
        }
        half4 particleFrag(Varyings input)
        {
            float2 q = input.uv;
            if (_VfxGrid > 1) q = (floor(q * _VfxGrid) + 0.5) / _VfxGrid;
            float age = input.particle.x, seed = input.particle.y, kind = input.particle.z;
            float t = _BodyClock * _MotionSpeed;
            float alpha;
            half3 color;
            if (kind > 1.5)
            {
                alpha = 1 - smoothstep(0.3,0.55,abs(q.x - 0.5) + abs(q.y - 0.5));
                color = lerp(half3(1,0.85,0.24),half3(1,0.31,0.03),age);
            }
            else if (kind > 0.5)
            {
                float2 p = (q - 0.5) * float2(1.8,1.6);
                float cloud = fbm(q * 4 + float2(seed * 17, -t * 0.35));
                alpha = (1 - smoothstep(0.3,0.94,length(p) + cloud * 0.28)) * 0.28;
                color = lerp(half3(0.09,0.10,0.13),half3(0.27,0.26,0.25),q.y * 0.5 + cloud * 0.3);
            }
            else
            {
                // A growing, curling tongue with turbulent gaps. Geometry handles attachment and world-space release.
                float curl = sin(q.y * 7 - t * 3.1 + seed * 12) * (0.025 + q.y * 0.15);
                curl += sin(q.y * 13 - t * 4.3 + seed * 29) * q.y * 0.035;
                float field = fbm(float2(q.x * 4 + seed * 13,q.y * 4.4 - t * 2.2));
                float width = pow(saturate(1 - q.y),0.72) * (0.37 + field * 0.2);
                float edge = width - abs(q.x - 0.5 - curl);
                float tongue = saturate(edge / 0.07) * saturate((1 - q.y - (1 - field) * 0.13) * 10);
                float tear = smoothstep(0.2,0.48,field + (1 - q.y) * 0.35);
                alpha = tongue * tear * (0.82 - q.y * 0.22);
                float heat = saturate(edge * 2.1 + (1 - q.y) * 0.12);
                color = lerp(half3(0.63,0.035,0.003),half3(0.95,0.22,0.008),smoothstep(0.10,0.60,heat));
                color = lerp(color,half3(0.98,0.54,0.08),smoothstep(0.76,0.98,heat));
            }
            float occupied = input.attachment.z > 0.5 ? maskAt(input.attachment.xy) : 1;
            return half4(color * saturate(_Intensity),alpha * occupied * input.particle.w * _Tint.a * _Opacity * step(0.001,_Intensity));
        }
        half4 iceEdge(float2 body)
        {
            float2 px = _BodyPixelSize * _BodyBounds.zw;
            float inside = maskAt(body);
            float thickness = _IceThickness * saturate(_BodyClock * 1.6);
            float near = 0, directional = 0;
            [unroll] for (int i = 0; i < 8; i++)
            {
                float angle = i * 0.78539816;
                float occupiedSample = maskAt(body + float2(cos(angle),sin(angle)) * px * thickness);
                near = max(near,occupiedSample);
                directional += occupiedSample * (i < 4 ? 1.0 : -1.0);
            }
            float2 localPixel = ((body - 0.5) / _BodyBounds.zw + _BodyBounds.xy) / max(_BodyPixelSize,0.00001);
            float3 facet = crystal(localPixel / max(_CrystalSize,2));
            float patch = smoothstep(0.18,0.68,noise(localPixel * 0.16 + 17));
            float chosen = _Look < 0.5 ? step(0.55,patch) : _Look < 1.5 ? step(0.27,patch) : 1;
            float edge = near * (1 - inside) * chosen;
            half3 color = lerp(half3(0.15,0.44,0.66),half3(0.58,0.83,0.91),facet.z * 0.55 + saturate(directional * 0.25));
            color = lerp(color,half3(0.81,0.96,1),step(facet.y,0.06) * 0.62);
            return half4(color,edge * (_Look < 0.5 ? 0.45 : 0.78) * _Tint.a * _Opacity * step(0.001,_Intensity));
        }
        half4 fallbackFire(float2 body)
        {
            // Platforms without asynchronous readback retain a bounded GPU-only look with roots clipped to occupied alpha.
            float2 localPixel = ((body - 0.5) / _BodyBounds.zw + _BodyBounds.xy) / max(_BodyPixelSize,0.00001);
            half4 result = 0;
            [unroll] for (int i = 0; i < 8; i++)
            {
                float2 root = float2(0.22 + frac(i * 0.381) * 0.56,0.14 + frac(i * 0.271) * 0.62);
                float occupied = maskAt(root);
                float2 rootPixel = ((root - 0.5) / _BodyBounds.zw + _BodyBounds.xy) / max(_BodyPixelSize,0.00001);
                Varyings particle = (Varyings)0;
                particle.uv = (localPixel - rootPixel) / float2(8,20) + float2(0.5,0);
                particle.particle = float4(frac(_BodyClock * 0.7 + i * 0.17),i * 0.113,0,occupied);
                if (all(particle.uv >= 0) && all(particle.uv <= 1))
                {
                    half4 front = particleFrag(particle);
                    result.rgb = lerp(result.rgb,front.rgb,front.a);
                    result.a = max(result.a,front.a);
                }
            }
            return result;
        }
        half4 frag(Varyings input):SV_Target
        {
            if (_BodyLayer > 1.5) return particleFrag(input);
            if (_BodyLayer > 0.5)
            {
                if (_Mode < 2.5) return iceEdge(input.body);
                return _UseParticles > 0.5 ? half4(0,0,0,0) : fallbackFire(input.body);
            }
            half4 source = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
            float arrive = saturate(_BodyClock * 1.5);
            half3 color;
            float coverage;
            if (_Mode < 2.5)
            {
                // Crystal coordinates follow source UVs through the pose; animated body bounds never slide the frost.
                float2 texel = input.uv * _BodySourceTexelSize.zw;
                float3 facet = crystal(texel / max(_CrystalSize,2));
                float frost = fbm(texel * 0.13 + 5);
                float growth = smoothstep(1 - arrive,1.24 - arrive,frost + 0.15);
                float value = dot(source.rgb,half3(0.2126,0.7152,0.0722));
                float2 px = _BodyPixelSize * _BodyBounds.zw;
                float litRim = (1 - maskAt(input.body + px * float2(-1,1))) * maskAt(input.body);
                if (_Look < 0.5)
                {
                    float powder = smoothstep(0.40,0.77,frost) * growth;
                    half3 frostColor = lerp(half3(0.34,0.55,0.64),half3(0.78,0.88,0.9),value * 0.55 + facet.z * 0.25);
                    color = lerp(source.rgb * half3(0.82,0.94,1),frostColor,powder * 0.72);
                    color = lerp(color,half3(0.79,0.94,0.98),litRim * powder * 0.62);
                    coverage = (0.20 + powder * 0.72) * arrive;
                }
                else
                {
                    float plate = smoothstep(_Look < 1.5 ? 0.32 : 0.10,0.72,frost) * growth;
                    half3 ice = lerp(half3(0.13,0.34,0.52),half3(0.43,0.70,0.80),facet.z);
                    ice *= 0.45 + value * 0.8;
                    color = lerp(source.rgb * half3(0.64,0.82,0.94),ice,plate * 0.74);
                    float edge = (1 - smoothstep(0.025,0.085,facet.y)) * plate;
                    float facets = step(0.77,facet.z) * plate * 0.24;
                    color = lerp(color,half3(0.68,0.90,0.97),edge * 0.57 + litRim * plate * 0.4 + facets);
                    // A sparse moving glint on a stable shell, rather than moving or blinking ice coverage.
                    float glint = pow(saturate(1 - abs(frac(facet.z + _BodyClock * 0.09) - 0.5) * 35),3);
                    color += half3(0.06,0.1,0.12) * glint * edge;
                    coverage = (_Look < 1.5 ? 0.36 + plate * 0.50 : 0.64 + plate * 0.31) * arrive;
                }
            }
            else
            {
                // Restrained heated shading preserves the character; independent flames provide the motion.
                float heat = fbm(input.uv * _BodySourceTexelSize.zw * 0.13 + float2(0,-_BodyClock * 0.3));
                color = source.rgb * half3(1,0.73,0.52) + half3(0.16,0.018,0) * heat;
                coverage = (0.19 + _Look * 0.04) * arrive;
            }
            return half4(saturate(color * _Intensity),source.a * input.alpha * _Tint.a * _Opacity * coverage * step(0.001,_Intensity));
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
        half4 maskFrag(Varyings input):SV_Target
        {
            half a = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a * input.alpha;
            return half4(a,a,a,a);
        }
        ENDHLSL
        Pass
        {
            Name "EntityBodyStatusSample"
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
