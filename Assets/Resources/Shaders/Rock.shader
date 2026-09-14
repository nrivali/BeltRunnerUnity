// The rock surface: an instanced Standard-lit stone whose vertices ride the rock's orbit rail in the vertex stage
// (so 54,000 rocks drift without any per-frame CPU work), tinted per instance, and reddening then whitening with
// the body heat the laser leaves in it (the browser's heat look).
Shader "BeltRunner/Rock"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Rail ("Rail (angle, radius, on rail, heat)", Vector) = (0, 1, 0, 0)
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

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Rail)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Input
        {
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

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            float h = saturate(IN.heat);
            o.Albedo = c.rgb * (1.0 - h * 0.55);
            o.Metallic = 0.04;
            o.Smoothness = 0.18;
            float3 hc = h < 0.5 ? lerp(float3(0.9, 0.1, 0.02), float3(1.0, 0.45, 0.12), h * 2.0)
                                : lerp(float3(1.0, 0.45, 0.12), float3(1.0, 0.82, 0.5), (h - 0.5) * 2.0);
            o.Emission = hc * h * (0.45 + 0.65 * h) * 0.5;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
