Shader "ZeroEngine/WorldShift/Reveal Demo"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _WorldShiftLayer ("World Shift Layer", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "WorldShiftReveal"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftReveal.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                half _WorldShiftLayer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half visibleMask = ZEWorldShiftBinaryVisibleMask(input.positionWS, input.positionCS, _WorldShiftLayer);

                if (_ZEWorldShiftActive > 0.5)
                {
                    clip(visibleMask - 0.5h);
                }

                float cleanEdgeRadius = ZEWorldShiftNoisyRadius(input.positionWS);
                float scanDistance = ZEWorldShiftScanDistance(input.positionWS);
                float edgeDistance = abs(cleanEdgeRadius - scanDistance);
                float edgeWidth = max(_ZEWorldShiftWidth, 0.001);
                half active = (half)_ZEWorldShiftActive;
                half coreBand = active * (half)(1.0 - smoothstep(edgeWidth * 0.03, edgeWidth * 0.18, edgeDistance));
                half innerBand = active * (half)(1.0 - smoothstep(edgeWidth * 0.12, edgeWidth * 0.72, edgeDistance));
                half outerBand = active * (half)(1.0 - smoothstep(edgeWidth * 0.3, edgeWidth * 1.65, edgeDistance));
                half techTicks = outerBand * ZEWorldShiftDigitalTickMask(input.positionWS, scanDistance) * 0.24h;
                half pulse = (half)(0.92 + sin(_Time.y * 8.0 + scanDistance * 5.5) * 0.08);

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(input.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 lit = _BaseColor.rgb * (0.22h + ndotl * mainLight.color * 0.78h);
                half rim = pow(1.0h - saturate(dot(normalWS, normalize(_WorldSpaceCameraPos.xyz - input.positionWS))), 3.0h);
                half3 scanGlow =
                    half3(0.82h, 1.0h, 1.0h) * coreBand * 0.92h * pulse +
                    half3(0.28h, 0.95h, 1.0h) * innerBand * 0.34h +
                    _ZEWorldShiftEdgeColor.rgb * outerBand * (0.2h + _WorldShiftLayer * 0.08h) +
                    half3(0.7h, 1.0h, 1.0h) * techTicks;
                half3 color = lit + _EmissionColor.rgb + rim * _EmissionColor.rgb * 0.35h + scanGlow;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
