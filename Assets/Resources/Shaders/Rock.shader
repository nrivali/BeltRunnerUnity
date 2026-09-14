// The rock surface: an instanced Standard-lit stone whose vertices ride the rock's orbit rail in the vertex stage
// (so 54,000 rocks drift without any per-frame CPU work), textured from Astra's rock library (albedo, normal and
// metal-roughness maps; the ore-vein surfaces take the instance colour), and reddening then whitening with the body
// heat the laser leaves in it (the browser's heat look).
Shader "BeltRunner/Rock"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Rail ("Rail (angle, radius, on rail, heat)", Vector) = (0, 1, 0, 0)
        _MainTex ("Albedo", 2D) = "white" {}
        _BaseColor ("Base colour factor", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _MetalRough ("Metal-roughness (B metal, G rough)", 2D) = "white" {}
        _Metallic ("Metallic factor", Range(0, 1)) = 0
        _Roughness ("Roughness factor", Range(0, 1)) = 1
        _Tint ("Tint by instance colour", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.5

        float _BeltTime;
        float4 _HeatPos0, _HeatPos1;   // the laser spots in scene space, w = radius
        float4 _HeatAmt;               // x the ship's beam, y the dish's
        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetalRough;
        float4 _BaseColor;
        float _BumpScale;
        float _Metallic;
        float _Roughness;
        float _Tint;

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Rail)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float heat;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);
            float4 rail = UNITY_ACCESS_INSTANCED_PROP(Props, _Rail);
            if (rail.z > 0.5)
            {
                // the drift since the belt was built: a rotation by 28 / radius radians a second round the planet,
                // written so nothing large is subtracted from anything large
                float th = 28.0 * _BeltTime / max(rail.y, 1.0);
                float c = cos(th) - 1.0;
                float s = sin(th);
                float ca = cos(rail.x);
                float sa = sin(rail.x);
                float3 drift = float3(rail.y * (ca * c + sa * s), 0.0, rail.y * (sa * c - ca * s));
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz + drift;
                v.vertex.xyz = mul(unity_WorldToObject, float4(wp, 1.0)).xyz;
            }
            o.heat = rail.w;
        }

        float3 Spot(float3 wp, float4 hp, float h)
        {
            if (h <= 0.001) return float3(0.0, 0.0, 0.0);
            float d = distance(wp, hp.xyz);
            float g = h * smoothstep(hp.w, hp.w * 0.12, d);
            float3 hc = h < 0.5 ? lerp(float3(1.0, 0.16, 0.03), float3(1.0, 0.58, 0.2), h * 2.0) : lerp(float3(1.0, 0.58, 0.2), float3(1.0, 0.95, 0.82), (h - 0.5) * 2.0);
            return hc * (0.55 + h * 0.6) * g * 0.85;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            float4 tex = tex2D(_MainTex, IN.uv_MainTex) * _BaseColor;
            float3 col = tex.rgb * lerp(float3(1.0, 1.0, 1.0), c.rgb, _Tint);
            // the body heat pulses a little, as the browser's does
            float h = saturate(IN.heat) * (0.92 + 0.08 * sin(_Time.y * 7.0 + IN.worldPos.x * 0.05 + IN.worldPos.y * 0.07));
            o.Albedo = col * (1.0 - h * 0.55);
            float4 mr = tex2D(_MetalRough, IN.uv_MainTex);
            o.Metallic = _Metallic * mr.b;
            o.Smoothness = 1.0 - _Roughness * mr.g;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            float3 hc = h < 0.5 ? lerp(float3(0.9, 0.1, 0.02), float3(1.0, 0.45, 0.12), h * 2.0)
                                : lerp(float3(1.0, 0.45, 0.12), float3(1.0, 0.82, 0.5), (h - 0.5) * 2.0);
            float lum = dot(col, float3(0.3, 0.59, 0.11));
            o.Emission = hc * h * (0.45 + 0.65 * h) * (0.6 + lum * 1.2) * 0.5;
            // the laser's spot glows where the beam is cooking the stone
            o.Emission += Spot(IN.worldPos, _HeatPos0, _HeatAmt.x) + Spot(IN.worldPos, _HeatPos1, _HeatAmt.y);
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
