Shader "ZeroEngine/BodyVisuals/EntityBodySpecks"
{
    Properties
    {
        [HideInInspector] _VisualPaletteMode ("Palette mode", Float) = 0
        [HideInInspector] _VisualPaletteInk ("Palette ink", Vector) = (.2,.22,.25,1)

        _Tint ("Color", Color) = (0.72,0.91,1,0.9)
        _Intensity ("Brightness", Float) = 1
        _Opacity ("Opacity", Float) = 1
        _Snow ("Snowflake", Float) = 1
        [HideInInspector] _Blood ("Blood drop", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Name "EntityBodySpecks"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.zerogamestudio.zeroengine.render/Runtime/Shaders/VisualPalette.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _VisualPaletteMode;
            half4 _VisualPaletteInk;
                half4 _Tint;
                float _Intensity, _Opacity, _Snow, _Blood;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 p = (floor(input.uv * 7) + 0.5) / 7 * 2 - 1;
                float shape;
                if (_Snow > 0.5)
                {
                    // Small uneven crystal chips, matching the scene's pixel flecks rather than an icon.
                    float2 cell = floor(min(input.uv,0.999) * 5);
                    float2 crystal = cell - float2(2,2);
                    float slope = input.color.r < .5 ? .45 : -.45;
                    float shard = step(abs(crystal.x + crystal.y * slope),.85) * step(abs(crystal.y),1.5);
                    float shoulder = step(abs(crystal.x),1.5) * step(abs(crystal.y + (input.color.r < .5 ? 0 : 1)),.5);
                    shape = max(shard,shoulder);
                    half3 iceColor = _Tint.rgb * lerp(.68,1,step(crystal.x + crystal.y,0));
                    return half4(iceColor * _Intensity,shape * input.color.a * _Tint.a * _Opacity);
                }
                else if (_Blood > 0.5)
                {
                    // Four authored-looking steps, two tones and whole-cell silhouettes.
                    float frame = min(floor(input.color.g * 4),3);
                    float2 grid = input.color.b > .5 ? float2(9,7) : float2(3,4);
                    float2 cell = floor(min(input.uv,0.999) * grid);
                    float shape, core;
                    if (input.color.b > .5)
                    {
                        float reach = frame < 1 ? 4 : frame < 2 ? 6 : 8;
                        float baseCluster = step(1,cell.x) * step(cell.x,4) * step(1,cell.y) * step(cell.y,3);
                        float middle = step(4,cell.x) * step(cell.x,6) * step(2,cell.y) * step(cell.y,4);
                        float tip = step(7,cell.x) * step(3,cell.y) * step(cell.y,4);
                        float fork = step(5,cell.x) * step(cell.x,6) * step(5,cell.y) * step(cell.y,5);
                        float lower = step(3,cell.x) * step(cell.x,4) * step(cell.y,0);
                        shape = max(max(baseCluster,middle),max(tip,max(fork,lower))) * step(cell.x,reach);
                        float fleck = frac(sin(dot(cell,float2(12.9898,78.233)) + input.color.r * 17) * 43758.5453);
                        shape *= frame < 3 ? 1 : step(.35,fleck);
                        core = step(abs(cell.y - (cell.x < 4 ? 2 : 3)),.1);
                    }
                    else
                    {
                        shape = step(abs(cell.x - 1) + abs(cell.y - 1.5),1.5);
                        core = step(cell.x,.5);
                    }
                    half3 bloodColor = saturate(_Tint.rgb * lerp(.72,1.32,core));
                    return half4(ZEVisualColor(bloodColor * _Intensity, _VisualPaletteMode, _VisualPaletteInk.rgb),shape * input.color.a * _Tint.a * _Opacity);
                }
                else
                {
                    float2 ember = input.uv * 2 - 1;
                    float head = 1 - smoothstep(0.45,0.95,length((ember - float2(0,0.32)) / float2(0.62,0.48)));
                    float wakeWidth = lerp(0.04,0.3,saturate((ember.y + 1) / 1.25));
                    float wake = (1 - smoothstep(wakeWidth * 0.4,wakeWidth + 0.08,abs(ember.x))) *
                        smoothstep(-1,-0.12,ember.y) * (1 - smoothstep(0.1,0.55,ember.y));
                    half3 emberColor = lerp(_Tint.rgb * 0.72,half3(1,0.88,0.48),head * 0.8);
                    return half4(emberColor * _Intensity,max(head,wake * 0.65) * input.color.a * _Tint.a * _Opacity);
                }
                half3 color = lerp(_Tint.rgb, half3(1,0.93,0.64), (1 - _Snow) * step(length(p), 0.35) * 0.65);
                return half4(color * _Intensity, saturate(shape) * input.color.a * _Tint.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
