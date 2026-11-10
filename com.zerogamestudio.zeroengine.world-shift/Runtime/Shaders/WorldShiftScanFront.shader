Shader "ZeroEngine/WorldShift/Scan Front"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (0.78, 1, 1, 1)
        _GlowColor ("Glow Color", Color) = (0.05, 0.62, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.26
        _CoreWidth ("Core Width", Range(0.005, 0.2)) = 0.032
        _GlowWidth ("Glow Width", Range(0.03, 0.45)) = 0.15
        _SegmentCount ("Segment Count", Range(1, 96)) = 36
        _SegmentGap ("Segment Gap", Range(0, 0.85)) = 0.28
        _TickIntensity ("Tick Intensity", Range(0, 1)) = 0.32
        _PacketIntensity ("Packet Intensity", Range(0, 1)) = 0.28
        _Flicker ("Flicker", Range(0, 1)) = 0.08
        _ScrollSpeed ("Scroll Speed", Range(0, 8)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "WorldShiftScanFront"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _GlowColor;
                half _Alpha;
                half _CoreWidth;
                half _GlowWidth;
                half _SegmentCount;
                half _SegmentGap;
                half _TickIntensity;
                half _PacketIntensity;
                half _Flicker;
                half _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash11(float value)
            {
                value = frac(value * 0.1031);
                value *= value + 33.33;
                value *= value + value;
                return frac(value);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _ScrollSpeed;
                float across = abs(input.uv.y - 0.5);
                half coreMask = (half)(1.0 - smoothstep(_CoreWidth, _CoreWidth + 0.018h, across));
                half glowMask = (half)(1.0 - smoothstep(_GlowWidth, _GlowWidth + 0.12h, across));

                float segmentValue = frac(input.uv.x * max(_SegmentCount, 1.0h) - time * 0.18);
                half segment = (half)smoothstep(_SegmentGap, _SegmentGap + 0.08h, segmentValue);
                half pulse = (half)(1.0 + sin(_Time.y * 7.0 + input.uv.x * 17.0) * _Flicker);

                half tickEdge = (half)min(segmentValue, 1.0 - segmentValue);
                half tickMask = (1.0h - smoothstep(0.015h, 0.052h, tickEdge)) *
                    (1.0h - smoothstep(_GlowWidth * 0.55h, _GlowWidth * 0.55h + 0.08h, (half)across));
                half tickGate = (half)(0.65 + Hash11(floor(input.uv.x * max(_SegmentCount, 1.0h))) * 0.35);

                float packetValue = abs(frac(input.uv.x * 4.0 - time * 0.36) - 0.5);
                half packet = (half)(1.0 - smoothstep(0.035, 0.11, packetValue)) *
                    coreMask *
                    (half)(0.75 + Hash11(floor(input.uv.x * 4.0)) * 0.25);

                half baseAlpha = (glowMask * 0.34h + coreMask * 0.85h) * segment;
                half tickAlpha = tickMask * tickGate * _TickIntensity * 0.32h;
                half packetAlpha = packet * _PacketIntensity;
                half alpha = saturate((baseAlpha + tickAlpha + packetAlpha) * pulse * _Alpha);
                half3 color =
                    _GlowColor.rgb * (glowMask * 0.72h + tickAlpha * 0.8h) +
                    _CoreColor.rgb * (coreMask * 1.35h + packetAlpha * 1.7h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
