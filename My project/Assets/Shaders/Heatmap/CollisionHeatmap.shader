Shader "LuckArkman/XR/CollisionHeatmap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0,1,0,0.1)
        _HeatRadius ("Heat Radius", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            float4 _BaseColor;
            float _HeatRadius;
            
            // Global Arrays definidos no HeatmapManager
            uniform float4 _HeatmapPoints[50];
            uniform int _HeatmapCount;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float totalIntensity = 0;
                
                for (int j = 0; j < _HeatmapCount; j++) {
                    float dist = distance(i.worldPos, _HeatmapPoints[j].xyz);
                    float intensity = _HeatmapPoints[j].w;
                    
                    // Queda radial da intensidade
                    float weight = 1.0 - saturate(dist / _HeatRadius);
                    totalIntensity += weight * intensity;
                }

                totalIntensity = saturate(totalIntensity);
                
                // Color Gradient: Verde (Seguro) -> Amarelo (Atenção) -> Vermelho (Perigo)
                fixed4 color;
                if (totalIntensity < 0.5) {
                    color = lerp(fixed4(0, 1, 0, 0.2), fixed4(1, 1, 0, 0.5), totalIntensity * 2);
                } else {
                    color = lerp(fixed4(1, 1, 0, 0.5), fixed4(1, 0, 0, 0.8), (totalIntensity - 0.5) * 2);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
