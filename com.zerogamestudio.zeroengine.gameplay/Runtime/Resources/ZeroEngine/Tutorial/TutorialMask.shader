Shader "ZeroEngine/Tutorial/FocusMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position : SV_POSITION; fixed4 color : COLOR; float2 local : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color, _BorderColor;
            float4 _ClipRect;
            float4 _Holes[16], _Shapes[16];
            int _HoleCount;
            float _BorderWidth, _Feather;
            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.local = input.vertex.xy;
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float distanceToHole = 1e8;
                float borderDistance = 1e8;
                for (int index = 0; index < 16; index++)
                {
                    if (index >= _HoleCount) break;
                    float2 halfSize = _Holes[index].zw;
                    float2 p = input.local - _Holes[index].xy;
                    float radius = clamp(_Shapes[index].y, 0, min(halfSize.x, halfSize.y));
                    float2 q = abs(p) - halfSize;
                    float d;
                    if (_Shapes[index].x < .5) d = length(max(q,0)) + min(max(q.x,q.y),0);
                    else if (_Shapes[index].x < 1.5) d = length(p) - length(halfSize);
                    else
                    {
                        q += radius;
                        d = length(max(q,0)) + min(max(q.x,q.y),0) - radius;
                    }
                    distanceToHole = min(distanceToHole, d);
                    if (_Shapes[index].z > .5) borderDistance = min(borderDistance, abs(d));
                }
                fixed4 color = input.color;
                color.a *= smoothstep(-_Feather, _Feather, distanceToHole);
                float border = _BorderWidth > 0 ? 1 - smoothstep(_BorderWidth, _BorderWidth + _Feather, borderDistance) : 0;
                color.rgb = lerp(color.rgb, _BorderColor.rgb, border * _BorderColor.a);
                color.a = max(color.a, border * _BorderColor.a);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.local, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - .001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
