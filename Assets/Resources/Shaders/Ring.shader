// The belt seen from afar: a thick ring volume round the planet standing in for the rocks the renderer does not draw
// past Belt.DRAW_DIST, so the belt reads all the way round the planet, from inside it (a band across the sky, edge
// on) as well as from above. Drawn as the inside faces of a box round the whole belt; each pixel intersects its view
// ray with the annular slab (three sub-belts by radius, each a gaussian across its spread), clips it to the depth
// buffer, and marches a few dithered samples of procedural density (streaks along the orbit, grain), lit by the sun
// with a little back-scatter toward it and dark in the planet's shadow, fading out near the camera where the real
// rocks take over. Transparent, after the opaques.
Shader "BeltRunner/Ring"
{
    Properties
    {
        _Color ("Colour", Color) = (0.72, 0.68, 0.64, 1)
        _Belt0 ("Belt 0 (rMin, rMax, half spread, gain)", Vector) = (285000, 585000, 32000, 1)
        _Belt1 ("Belt 1", Vector) = (605000, 925000, 45000, 1)
        _Belt2 ("Belt 2", Vector) = (945000, 1275000, 57000, 1)
        _PlanetR ("Planet radius (u)", Float) = 225000
        _Dens ("Density per unit", Float) = 0.0000012
        _NearFade ("Fade in from (u)", Float) = 90000
        _FarFade ("Fade in to (u)", Float) = 220000
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Front

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _Color, _Belt0, _Belt1, _Belt2;
            float _PlanetR, _Dens, _NearFade, _FarFade;
            float4 _BeltSunDir;   // set by Lighting.SetZone
            float4 _RingSunColor;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float4 spos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            float hash2(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030) + float2(0.31, 0.17));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float vnoise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), f.x), lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), f.x), f.y);
            }

            // the belt's density at a point round the planet: which sub-belt the radius falls in, the gaussian across
            // its spread, the streaks along the orbit
            float Density(float3 q)
            {
                float r = length(q.xz);
                float prof = 0.0;
                float f = 0.0;
                float4 b[3] = { _Belt0, _Belt1, _Belt2 };
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float fi = (r - b[i].x) / max(1.0, b[i].y - b[i].x);
                    float e = smoothstep(0.0, 0.12, fi) * (1.0 - smoothstep(0.88, 1.0, fi));
                    float y = q.y / max(1.0, b[i].z);
                    float p = e * exp(-y * y * 2.0) * b[i].w;
                    if (p > prof) { prof = p; f = fi; }
                }
                if (prof <= 0.001) return 0.0;
                float a = atan2(q.z, q.x) / 6.2831853;
                float streak = vnoise2(float2(a * 6.0, r * 0.00002)) * 0.55 + vnoise2(float2(a * 28.0, r * 0.00009)) * 0.3 + vnoise2(float2(a * 110.0, r * 0.0004)) * 0.15;
                return prof * smoothstep(0.3, 0.8, streak);
            }

            // the ray's span through the annular slab: the outer cylinder, the slab, minus the inner cylinder's core
            bool Span(float3 o, float3 d, float rIn, float rOut, float h, out float t0, out float t1)
            {
                t0 = 0.0; t1 = 0.0;
                // the slab
                float ty0 = -1e12, ty1 = 1e12;
                if (abs(d.y) > 1e-6) { float a = (-h - o.y) / d.y, b = (h - o.y) / d.y; ty0 = min(a, b); ty1 = max(a, b); }
                else if (abs(o.y) > h) return false;
                // the outer cylinder
                float A = dot(d.xz, d.xz), B = 2.0 * dot(o.xz, d.xz), C = dot(o.xz, o.xz) - rOut * rOut;
                float disc = B * B - 4.0 * A * C;
                if (disc < 0.0) return false;
                float sq = sqrt(disc);
                float tc0 = (-B - sq) / (2.0 * A), tc1 = (-B + sq) / (2.0 * A);
                t0 = max(max(ty0, tc0), 0.0);
                t1 = min(ty1, tc1);
                if (t1 <= t0) return false;
                // the inner cylinder: the ray may cross the core; keep the near part of the span (the far part is behind the planet mostly)
                float Ci = dot(o.xz, o.xz) - rIn * rIn;
                float di = B * B - 4.0 * A * Ci;
                if (di > 0.0)
                {
                    float sqi = sqrt(di);
                    float ti0 = (-B - sqi) / (2.0 * A), ti1 = (-B + sqi) / (2.0 * A);
                    if (ti0 > t0 && ti0 < t1) t1 = ti0;          // enters the core: stop there
                    else if (ti0 <= t0 && ti1 > t0) t0 = max(t0, ti1);   // starts in the core: begin where it leaves
                }
                return t1 > t0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 dir = normalize(i.wp - cam);
                float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 o = cam - center;
                float rIn = _Belt0.x, rOut = _Belt2.y, h = max(_Belt0.z, max(_Belt1.z, _Belt2.z)) * 1.6;
                float t0, t1;
                if (!Span(o, dir, rIn, rOut, h, t0, t1)) discard;
                // the scene's depth clips the span
                float2 suv = i.spos.xy / i.spos.w;
                float eye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv));
                float along = dot(dir, -UNITY_MATRIX_V[2].xyz);
                float tScene = eye / max(0.05, along);
                t1 = min(t1, tScene);
                if (t1 <= t0) discard;
                const int N = 20;
                float dt = (t1 - t0) / N;
                float dither = frac(52.9829189 * frac(0.06711056 * i.pos.x + 0.00583715 * i.pos.y));
                float3 s = normalize(_BeltSunDir.xyz);
                float back = 1.0 + 0.8 * pow(saturate(dot(dir, s)), 6.0);
                float T = 1.0;
                float3 acc = 0.0;
                float t = t0 + dt * dither;
                for (int k = 0; k < N; k++)
                {
                    float3 q = o + dir * t;
                    float dens = Density(q) * smoothstep(_NearFade, _FarFade, t);
                    if (dens > 0.0005)
                    {
                        float alongS = dot(q, s);
                        float3 perp = q - s * alongS;
                        float shadow = alongS < 0.0 ? smoothstep(_PlanetR * 0.97, _PlanetR * 1.06, length(perp)) : 1.0;
                        float lit = lerp(0.05, 1.0, shadow);
                        float a = 1.0 - exp(-dens * _Dens * dt);
                        acc += _Color.rgb * _RingSunColor.rgb * lit * back * a * T;
                        T *= 1.0 - a;
                    }
                    t += dt;
                }
                float alpha = 1.0 - T;
                return float4(acc, alpha);
            }
            ENDCG
        }
    }
}
