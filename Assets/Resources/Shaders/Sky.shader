// The sky, ported from the Godot port's sky shader (itself the browser's sky texture as a shader): a nebula of two
// noise fields with a dust band round the ecliptic, a couple of thousand hashed stars, and the sun as a hard HDR disc
// with a small optical glare, so the glow pass picks it up. The reflection bake gets a gentler disc (_DiscGain): at
// full strength every metal ore vein mirrors it as a white blob.
Shader "BeltRunner/Sky"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.020, 0.027, 0.063, 1)
        _NebulaA ("Nebula A", Color) = (0.22, 0.125, 0.376, 1)
        _NebulaB ("Nebula B", Color) = (0.094, 0.188, 0.282, 1)
        _BandColor ("Dust band", Color) = (0.118, 0.125, 0.165, 1)
        _SunColor ("Sun", Color) = (1, 0.96, 0.9, 1)
        _SunDir ("Sun direction", Vector) = (0.55, 0.42, -0.72, 0)
        _SunRadius ("Sun radius (rad)", Float) = 0.009
        _StarGain ("Star gain", Float) = 1
        _DiscGain ("Disc gain", Float) = 6
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _BaseColor, _NebulaA, _NebulaB, _BandColor, _SunColor, _SunDir;
            float _SunRadius, _StarGain, _DiscGain;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            float fbm(float3 p)
            {
                return vnoise(p) * 0.5 + vnoise(p * 2.0 + 7.0) * 0.3 + vnoise(p * 4.1 + 3.0) * 0.2;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // the nebula: two noise fields, a dust band round the ecliptic tilted by a slow wave
                float n1 = fbm(d * 2.2 + 3.0);
                float n2 = fbm(d * 3.1 + 13.0);
                float lon = atan2(d.z, d.x);
                float band = exp(-pow((d.y - 0.07 * sin(lon + 1.2)) / 0.075, 2.0)) * (0.35 + 0.65 * n2);
                float neb = max(0.0, n1 - 0.52) * 2.2;
                float neb2 = max(0.0, n2 - 0.6) * 2.4;
                float3 col = _BaseColor.rgb + (neb * _NebulaA.rgb + neb2 * _NebulaB.rgb) * 0.7 + band * _BandColor.rgb;
                // stars: a sparse hash over direction cells, a brighter few among them
                float3 sp = d * 240.0;
                float3 cell = floor(sp);
                float h = hash(cell);
                if (h > 0.9955)
                {
                    float3 c = float3(hash(cell + 1.0), hash(cell + 2.0), hash(cell + 3.0));
                    float dist = length(frac(sp) - 0.5 - (c - 0.5) * 0.5);
                    float bright = h > 0.9993 ? 2.4 : 0.9;
                    float s = smoothstep(0.11, 0.0, dist) * bright * _StarGain;
                    col += s * lerp(float3(0.72, 0.76, 0.83), float3(0.9, 0.92, 0.97), hash(cell + 5.0));
                }
                // the sun: a hard disc and a small optical glare, both in HDR
                float cs = dot(d, normalize(_SunDir.xyz));
                float a = acos(clamp(cs, -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunRadius * 0.97, _SunRadius, a);
                float r = a / _SunRadius;
                float halo = 1.15 * exp(-r * 1.9) + 0.24 * exp(-r * 0.6);
                col += _SunColor.rgb * (disc * _DiscGain + halo * 0.35);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
