// The radar pulse's shell (2026-09-18): a soft rim on an expanding sphere, additive, both sides, brightest where the
// surface is seen edge-on and clear where it faces the camera, so it reads as a bubble from outside and as a faint
// haze from within (where the chase camera always is); the pulse itself shows on the rocks (Scan in RockTorch.cginc).
Shader "BeltRunner/Pulse"
{
    Properties
    {
        _Color ("Color", Color) = (0.37, 0.83, 0.94, 0.3)
        _Rim ("Rim power", Float) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Rim;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wv : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wv = _WorldSpaceCameraPos - wp;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float f = 1.0 - abs(dot(normalize(i.wn), normalize(i.wv)));
                float a = pow(saturate(f), _Rim) * _Color.a;
                return float4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
