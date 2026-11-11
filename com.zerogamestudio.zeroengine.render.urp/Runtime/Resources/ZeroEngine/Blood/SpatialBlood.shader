Shader "Hidden/ZeroEngine/Blood/Spatial"
{
    Properties
    {
        _BaseColor ("Blood tint", Color) = (1,.1,.1,1)
        [HideInInspector] _Shadow ("Contact shadow", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
        [HideInInspector] _PixelWorldSize ("Source world texel XY", Vector) = (.0625,.0625,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Shadow;
            float4 _PixelWorldSize;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local : TEXCOORD0;
                half2 tint : TEXCOORD1;
                float4 gridAxes : TEXCOORD2;
                float2 gridOrigin : TEXCOORD3;
                float3 gridClip : TEXCOORD4;
            };
            float2 BloodScreenPoint(float4 clip)
            {
                return (clip.xy / max(.00001, clip.w) * .5 + .5) * _ScreenParams.xy;
            }
            float2 BloodGridCoordinate(float2 screen, float2 origin, float4 axes)
            {
                float2 delta = screen - origin;
                float determinant = axes.x * axes.w - axes.y * axes.z;
                return float2(delta.x * axes.w - delta.y * axes.z,
                    axes.x * delta.y - axes.y * delta.x) / determinant;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 center = TransformObjectToWorld(float3(0,0,0));
                float2 step = max(_PixelWorldSize.xy, float2(.0001,.0001));
                output.gridOrigin = BloodScreenPoint(TransformWorldToHClip(center));
                float2 x = BloodScreenPoint(TransformWorldToHClip(center + float3(step.x,0,0))) - output.gridOrigin;
                float2 y = BloodScreenPoint(TransformWorldToHClip(center + float3(0,step.y,0))) - output.gridOrigin;
                x *= max(1, length(x)) / max(.00001, length(x));
                y *= max(1, length(y)) / max(.00001, length(y));
                if (abs(x.x * y.y - x.y * y.x) < .0001)
                {
                    x = float2(max(1,length(x)),0);
                    y = float2(0,max(1,length(y)));
                }
                output.gridAxes = float4(x,y);
                float2 cell = floor(BloodGridCoordinate(BloodScreenPoint(output.positionCS), output.gridOrigin, output.gridAxes) + .5);
                float2 screen = output.gridOrigin + x * cell.x + y * cell.y;
                output.positionCS.xy = (screen / _ScreenParams.xy * 2 - 1) * output.positionCS.w;
                output.gridClip = output.positionCS.xyw;
                output.local = input.positionOS.xyz;
                output.tint = input.color.ra;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half alpha = _BaseColor.a * input.tint.y;
                clip(alpha - .015);
                if (_Shadow > .5)
                {
                    half edge = floor(saturate(1 - length(input.local.xz) * .85) * 3) / 3;
                    return half4(_BaseColor.rgb, alpha * edge);
                }
                // Whole pixel blocks dissolve together, retaining solid volume/depth.
                float2 screen = (input.gridClip.xy / input.gridClip.z * .5 + .5) * _ScreenParams.xy;
                float2 cell = floor(BloodGridCoordinate(screen, input.gridOrigin, input.gridAxes));
                half threshold = frac(sin(dot(cell, float2(12.9898,78.233))) * 43758.5453);
                clip(alpha - threshold * .98);
                // C# supplies the authored blood hue with the current ink remap.
                // A fixed shallow-edge shade keeps the main silhouette sprite-flat.
                return half4(_BaseColor.rgb * input.tint.x, 1);
            }
            ENDHLSL
        }
    }
}
