Shader "Hidden/ZeroEngine/Blood/Surface"
{
    Properties
    {
        [PerRendererData] _MainTex("Body",2D)="white"{}
        [PerRendererData] _BloodMask("Source colour dye lookup",2D)="black"{}
        _Color("Tint",Color)=(1,1,1,1)
        _StainOpacity("Blood fade",Range(0,1))=1
        _StainLoad("Accumulated blood",Range(0,1))=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" "DisableBatching"="True" "CanUseSpriteAtlas"="True"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1,-1
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            TEXTURE2D(_BloodMask);SAMPLER(sampler_BloodMask);
            float4 _BloodColor,_Color,_StainFlip,_BloodSourceTexel;
            float _StainOpacity,_StainLoad;
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            uint3 Feature(half4 center,half4 neighbour)
            {
                int3 a=(int3)floor(saturate(center.rgb)*31+.5);
                int3 b=(int3)floor(saturate(neighbour.rgb)*31+.5);
                return neighbour.a>.8?(uint3)abs(a-b):uint3(0,0,0);
            }
            V Vert(A v){V o;float3 position=v.positionOS.xyz;position.xy*=_StainFlip.xy;o.positionCS=TransformObjectToHClip(position);o.uv=v.uv;o.color=v.color*_Color;return o;}
            half4 Frag(V v):SV_Target
            {
                half4 body=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv);
                clip(body.a*v.color.a-.2);
                float3 key=saturate(body.rgb);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    key=LinearToSRGB(key);
                #endif
                uint3 bucket=(uint3)floor(saturate(key)*15+.5);
                uint index=bucket.x+bucket.y*16+bucket.z*256;
                float2 lookup=(float2(index%64,index/64)+.5)/64;
                half4 mask=SAMPLE_TEXTURE2D(_BloodMask,sampler_BloodMask,lookup);
                // Source-image folds/shading boundaries provide attached blood detail.
                // Symmetric sampling keeps a mirrored animation's colour treatment identical.
                half4 left=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv-float2(_BloodSourceTexel.x,0));
                half4 right=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv+float2(_BloodSourceTexel.x,0));
                half4 up=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv+float2(0,_BloodSourceTexel.y));
                half4 down=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv-float2(0,_BloodSourceTexel.y));
                // Integer, direction-independent source features stay identical when a pose is mirrored.
                // No frame UV, body/world position or time enters this pattern.
                uint3 a=Feature(body,left),b=Feature(body,right),c=Feature(body,up),d=Feature(body,down);
                uint3 feature=a+b+c+d,detail=a*a+b*b+c*c+d*d;
                uint pattern=bucket.x*374761393u^bucket.y*668265263u^bucket.z*2246822519u;
                pattern^=feature.x*3266489917u^feature.y*1274126177u^feature.z*42595009u;
                pattern^=detail.x*1597334677u^detail.y*3812015801u^detail.z*958282129u;
                pattern^=(uint)floor(mask.r*255+.5)*747796405u;
                pattern=(pattern^(pattern>>13))*1274126177u;pattern^=pattern>>16;
                // Broken, translucent gaps and opaque clusters, with three pixel-art blood shades.
                uint coverage=pattern&7u;
                half density=lerp(.14,1,coverage/7.0);
                half heavy=smoothstep(.45,.9,_StainLoad);
                // Heavy blood deepens the same attached pattern; no new coordinates or moving noise.
                density=lerp(density,.48+.52*density,heavy*.85);
                // Leave flat colour interiors readable; concentrate blotches where original features break them up.
                half features=lerp(.16,1,saturate((feature.x+feature.y+feature.z)/24.0));
                features=lerp(features,.42+.58*features,heavy*.8);
                density*=features;
                uint shade=(pattern>>8)%3u;
                half pigment=shade==0u?.32:shade==1u?.64:1;
                half light=dot(body.rgb,half3(.2126,.7152,.0722));
                half alpha=mask.a*_StainOpacity*density;
                clip(alpha-.005);
                return half4(_BloodColor.rgb*pigment*(.82+.18*light),alpha);
            }
            ENDHLSL
        }
    }
}
