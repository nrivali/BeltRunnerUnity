// The belt's far rocks as specks: one small camera-facing quad per real rock, sized in pixels whatever its distance,
// sun-lit, dark in the planet's shadow, faded out inside the rocks' draw distance where the real meshes take over.
// So the belt reads as rock all the way round the planet, the way a real belt would: a sprinkling of points, not a
// band. Built once per zone from the rocks' own positions (Game.BuildSpecks); a child of the planet.
Shader "BeltRunner/Specks"
{
    Properties
    {
        _Color ("Albedo, as the rock shader's stone", Color) = (0.36, 0.34, 0.33, 1)
        _Size ("Size (px)", Float) = 1.5
        _Gain ("Gain", Float) = 1.0
        _PlanetR ("Planet radius (u)", Float) = 225000
        _NearFade ("Fade in from (u)", Float) = 120000
        _FarFade ("Fade in to (u)", Float) = 200000
        _Scale ("World units per local unit", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Size, _Gain, _PlanetR, _NearFade, _FarFade, _Scale;
            float4 _BeltSunDir;
            float4 _SpeckSunColor;

            struct appdata
            {
                float4 vertex : POSITION;    // the rock's centre, local to the planet (world / _Scale)
                float2 uv : TEXCOORD0;       // the quad corner, -1..1
                float2 uv1 : TEXCOORD1;      // x the rock's radius (u)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 col : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 q = v.vertex.xyz * _Scale;   // from the planet's centre, world units
                float d = distance(wp, _WorldSpaceCameraPos);
                // the sun: a rock is lit on the sun side and dark in the planet's shadow; a little brighter looking back toward the sun
                float3 s = normalize(_BeltSunDir.xyz);
                float along = dot(q, s);
                float3 perp = q - s * along;
                float shadow = along < 0.0 ? smoothstep(_PlanetR * 0.98, _PlanetR * 1.04, length(perp)) : 1.0;
                // the same light as the near rocks: the stone's albedo under the sun's colour and strength, and the share
                // of the lit side the camera sees (full looking away from the sun, a dark silhouette looking into it)
                float3 view = normalize(_WorldSpaceCameraPos - wp);
                float phase = saturate(dot(view, s) * 0.5 + 0.5);
                float lit = lerp(0.03, 1.0, shadow) * phase * 0.5;
                // the size: pixels, a little more for a big rock, gone inside the draw distance and thinned far out
                float r = v.uv1.x;
                float fade = smoothstep(_NearFade, _FarFade, d) * saturate((r - 16.0) / 50.0);
                float px = _Size * (0.7 + 0.5 * saturate(r / 70.0));
                // every rock catches the sun its own way: a hash of its place varies the brightness a lot
                float3 hp = frac(v.vertex.xyz * 37.13 + 0.17);
                float vary = 0.7 + 0.3 * frac(hp.x * 91.7 + hp.y * 47.3 + hp.z * 13.9);
                lit *= vary;
                o.pos = UnityObjectToClipPos(float4(v.vertex.xyz, 1.0));
                o.pos.xy += v.uv * px * 2.0 / _ScreenParams.xy * o.pos.w;
                o.uv = v.uv;
                float a = fade * _Gain * saturate(0.35 + r / 60.0);
                o.col = float4(_Color.rgb * _SpeckSunColor.rgb * lit, a);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float rr = dot(i.uv, i.uv);
                float disc = 1.0 - smoothstep(0.25, 1.0, rr);
                float a = i.col.a * disc;
                return float4(i.col.rgb * a, a);
            }
            ENDCG
        }
    }
}
