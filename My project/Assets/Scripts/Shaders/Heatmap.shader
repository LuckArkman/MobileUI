Shader "AR/Heatmap"
{
    Properties
    {
        _HeatColor ("Heat Color", Color) = (1,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 _HeatColor;
            fixed4 frag() : SV_Target
            {
                return _HeatColor;
            }
            ENDCG
        }
    }
}
