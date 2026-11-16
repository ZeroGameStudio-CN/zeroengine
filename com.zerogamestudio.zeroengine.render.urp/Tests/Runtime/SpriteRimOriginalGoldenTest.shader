Shader "Hidden/ZeroEngine/Tests/OriginalRimGolden"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Runtime/Resources/ZeroEngine/SpriteRimOriginalCore.hlsl"
            float4 _Original, _Light;
            float _Mode;
            float4 frag(v2f_img input):SV_Target
            {
                return float4(OriginalRimBlend(_Original.rgb,_Light,(int)_Mode,1,3,-.3,.18,.015,.87),1);
            }
            ENDHLSL
        }
    }
}
