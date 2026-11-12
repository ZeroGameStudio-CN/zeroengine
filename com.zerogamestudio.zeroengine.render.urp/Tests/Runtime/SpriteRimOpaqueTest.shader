Shader "Hidden/ZeroEngine/Tests/SpriteRimOpaque"
{
    Properties { _MainTex("Source alpha",2D)="white"{} _BaseColor("Unlit base",Color)=(.2,.2,.2,1) _SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=0 _ZWrite("Depth write",Float)=1 }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest"}
        Cull Off ZWrite [_ZWrite] ZTest LEqual
        Blend [_SrcBlend] [_DstBlend]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float4 _BaseColor;
        struct Attributes {float3 positionOS:POSITION;float2 uv:TEXCOORD0;};
        struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
        Varyings Vert(Attributes i) {Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS);o.uv=i.uv;return o;}
        float4 Frag(Varyings i):SV_Target {clip(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a-.5);return _BaseColor;}
        ENDHLSL
        Pass { Name "Forward" Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass { Name "Depth" Tags {"LightMode"="DepthOnly"} ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
