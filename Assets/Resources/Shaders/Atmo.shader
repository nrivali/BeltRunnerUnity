// A planet's atmosphere: an additive fresnel shell a few percent bigger than the planet, brightest at the limb and
// fading both inward over the disc and outward past it, lit on the sun's side (the reference frame's glowing limb).
Shader "BeltRunner/Atmo"
{
    Properties
    {
        _Color ("Colour", Color) = (0.55, 0.7, 1.0, 1)
        _Power ("Fresnel power", Float) = 3.0
        _Gain ("Gain", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Power, _Gain;
            float4 _BeltSunDir;   // set by Lighting.SetZone

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float3 v : TEXCOORD1;
            };

            v2f vert(appdata_base i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, i.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(i.v);
                float d = saturate(dot(n, v));
                // the limb: a fresnel that peaks just outside the planet's own edge and dies at the shell's edge
                float rim = pow(1.0 - d, _Power) * smoothstep(0.0, 0.22, d);
                float lit = saturate(dot(n, normalize(_BeltSunDir.xyz)) * 0.9 + 0.35);
                return float4(_Color.rgb * rim * lit * _Gain, 1.0);
            }
            ENDCG
        }
    }
}
