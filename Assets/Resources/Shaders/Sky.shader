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
                float3 col = _BaseColor.rgb;
                // the nebulae: three coloured clouds in their own parts of the sky, domain-warped so they wisp (no galaxy band: the user found it too much)
                float3 warp = float3(fbm(d * 2.0 + 7.0), fbm(d * 2.0 + 19.0), fbm(d * 2.0 + 31.0)) - 0.5;
                float3 dw = d + warp * 0.35;
                float nb1 = fbm(dw * 2.4 + 3.0);
                float nb2 = fbm(dw * 2.9 + 13.0);
                float nb3 = fbm(dw * 2.1 + 27.0);
                float c1 = smoothstep(0.55, 0.85, nb1) * smoothstep(0.55, 0.95, dot(d, normalize(float3(-0.7, 0.35, 0.6))));
                float c2 = smoothstep(0.55, 0.85, nb2) * smoothstep(0.5, 0.95, dot(d, normalize(float3(0.75, -0.25, 0.6))));
                float c3 = smoothstep(0.55, 0.85, nb3) * smoothstep(0.5, 0.95, dot(d, normalize(float3(0.1, -0.6, -0.8))));
                col += (_NebulaA.rgb * c1 + _NebulaB.rgb * c2 + float3(0.55, 0.28, 0.12) * c3) * 0.05;   // a bare trace: the user's reference sky is black
                // stars: a hash over direction cells, a few bright and coloured among them
                float3 sp = d * 260.0;
                float3 cell = floor(sp);
                float h = hash(cell);
                float thresh = 0.9905;   // dense, small, white
                if (h > thresh)
                {
                    float3 c = float3(hash(cell + 1.0), hash(cell + 2.0), hash(cell + 3.0));
                    float dist = length(frac(sp) - 0.5 - (c - 0.5) * 0.5);
                    float big = h > 0.9993 ? 1.0 : 0.0;
                    float bright = lerp(0.8, 2.2, big);
                    float s = smoothstep(0.1 + big * 0.05, 0.0, dist) * bright * _StarGain;
                    float hue = hash(cell + 5.0);
                    float3 sc = hue < 0.15 ? float3(0.98, 0.9, 0.8) : (hue > 0.85 ? float3(0.85, 0.9, 1.0) : float3(0.95, 0.96, 0.98));
                    col += s * sc;
                }
                // the sun: a hard disc and a small optical glare, both in HDR
                float cs = dot(d, normalize(_SunDir.xyz));
                float a = acos(clamp(cs, -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunRadius * 0.97, _SunRadius, a);
                float r = a / _SunRadius;
                // the glare, as the user's sunrise reference has it: a white-hot disc in a broad, soft orange glow that
                // falls away smoothly a long way out
                float halo = 1.3 * exp(-r * 1.1) + 0.5 * exp(-r * 0.32) + 0.18 * exp(-r * 0.09);
                col += float3(1.0, 0.97, 0.92) * disc * _DiscGain + float3(1.0, 0.55, 0.26) * halo * 0.7;
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
