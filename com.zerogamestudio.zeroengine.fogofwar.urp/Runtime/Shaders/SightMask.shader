Shader "Hidden/ZeroEngine/FogOfWar/SightMask"
{
    Properties { _MainTex ("Source sight", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
        fixed4 frag() : SV_Target { return 1; }
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        int _BlurPairCount;
        float _BlurCenterWeight;
        float4 _BlurKernel[24];
        // Separable Gaussian filtering affects presentation R only. Geometry/terrain stay unfiltered.
        fixed4 SoftSight(float2 uv, float2 axis)
        {
            float2 texel = axis * _MainTex_TexelSize.xy;
            float value = tex2D(_MainTex, uv).r * _BlurCenterWeight;
            [loop] for (int tap = 0; tap < _BlurPairCount; tap++)
            {
                float2 offset = texel * _BlurKernel[tap].x;
                value += (tex2D(_MainTex, uv + offset).r + tex2D(_MainTex, uv - offset).r) * _BlurKernel[tap].y;
            }
            return fixed4(value, 0, 0, 1);
        }
        fixed4 fragHorizontal(v2f_img i) : SV_Target { return SoftSight(i.uv, float2(1,0)); }
        fixed4 fragVertical(v2f_img i) : SV_Target { return SoftSight(i.uv, float2(0,1)); }
        ENDCG
        Pass
        {
            Name "CURRENT_SIGHT"
            ColorMask R
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Name "TERRAIN"
            ColorMask G
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Name "BLUR_HORIZONTAL"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragHorizontal
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "BLUR_VERTICAL"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragVertical
            #pragma target 3.0
            ENDCG
        }
    }
}
