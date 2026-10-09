Shader "Hidden/ZeroRender/FlowPatternFixture"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Runtime/Shaders/FlowPattern.hlsl"
            int _Kind;
            float4 _Args;
            float _Extra;
            float4 frag(v2f_img input) : SV_Target
            {
                float result = 0;
                if (_Kind == 0) result = zero_flow_period(_Args.x, _Args.y) / 10;
                else if (_Kind == 1) result = zero_flow_phase(_Args.x, _Args.y, _Args.z) / 10;
                else if (_Kind == 2) result = zero_flow_dash(_Args.x, _Args.y, _Args.z);
                else if (_Kind == 3) result = zero_flow_tracer(_Args.x, _Args.y);
                else if (_Kind == 4) result = zero_flow_chevron(_Args.x, _Args.y, _Args.z, _Args.w, _Extra);
                else if (_Kind == 5) result = zero_flow_open_dash(_Args.x, _Args.y, _Args.z);
                return float4(result, result, result, 1);
            }
            ENDCG
        }
    }
}
