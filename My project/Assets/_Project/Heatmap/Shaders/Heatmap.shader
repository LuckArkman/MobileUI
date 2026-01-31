Shader "ARProject/Heatmap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorMin ("Color Min (Safe)", Color) = (0, 0, 1, 0.3) // Blue
        _ColorMax ("Color Max (Danger)", Color) = (1, 0, 0, 0.5) // Red
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ColorMin;
            float4 _ColorMax;

            // Arrays passed from script
            uniform float4 _HeatmapPoints[20]; // x,y,z,intensity
            uniform int _PointsCount;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float totalIntensity = 0;
                
                // Calculate influence from all points
                for(int j = 0; j < _PointsCount; j++)
                {
                    float3 d = i.worldPos - _HeatmapPoints[j].xyz;
                    float dist = length(d);
                    float radius = 1.0; // Influence radius
                    
                    float influence = 1.0 - saturate(dist / radius);
                    totalIntensity += influence * _HeatmapPoints[j].w;
                }

                totalIntensity = saturate(totalIntensity);
                
                fixed4 col = lerp(_ColorMin, _ColorMax, totalIntensity);
                col.a *= totalIntensity; // Fade out if no intensity
                
                return col;
            }
            ENDCG
        }
    }
}
