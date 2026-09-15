// The frame's finish, as the browser and the Godot port have it: ACES tone mapping at the zone's exposure, and a glow
// from everything above the HDR threshold (the sun disc, the emissive strips, the engines, the beam). Four passes:
// the bright pass at quarter size, a horizontal and a vertical blur, and the composite.
Shader "BeltRunner/Post"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Bloom ("Bloom", 2D) = "black" {}
        _Exposure ("Exposure", Float) = 1.05
        _Threshold ("Glow threshold", Float) = 1.15
        _Intensity ("Glow intensity", Float) = 0.55
        _Burn ("Afterburner", Float) = 0
        _BurnCenter ("Afterburner centre", Vector) = (0.5, 0.5, 0, 0)
        _Rays ("God rays", 2D) = "black" {}
        _SunUV ("Sun in the frame (uv)", Vector) = (0.5, 0.5, 0, 0)
        _RayThreshold ("Ray source threshold", Float) = 1.2
        _RayLen ("Ray length (of the way to the sun)", Float) = 0.85
        _RayDecay ("Ray decay per tap", Float) = 0.94
        _RayGain ("Ray gain", Float) = 0
        _Volume ("Volumetric dust", 2D) = "black" {}
        _Grade ("Grade (saturation, contrast, lift, 0)", Vector) = (1.25, 1.18, 0.015, 0)
        _ShadowTint ("Shadow tint", Color) = (0.92, 0.96, 1.06, 1)
        _HighTint ("Highlight tint", Color) = (1.05, 1.0, 0.93, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _Bloom;
        sampler2D _Rays;
        sampler2D _Volume;
        float _Exposure, _Threshold, _Intensity, _Burn;
        float4 _BurnCenter;
        float4 _SunUV;
        float _RayThreshold, _RayLen, _RayDecay, _RayGain;
        float4 _Grade, _ShadowTint, _HighTint;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        float3 aces(float3 x)
        {
            const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
            return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
        }
        ENDCG

        // 0: the bright pass
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float lum = max(c.r, max(c.g, c.b));
                float k = max(0.0, lum - _Threshold) / max(lum, 1e-4);
                return float4(c * k, 1.0);
            }
            ENDCG
        }
        // 1: horizontal blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 s = float2(_MainTex_TexelSize.x, 0.0);
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.227;
                c += (tex2D(_MainTex, i.uv + s * 1.38).rgb + tex2D(_MainTex, i.uv - s * 1.38).rgb) * 0.316;
                c += (tex2D(_MainTex, i.uv + s * 3.23).rgb + tex2D(_MainTex, i.uv - s * 3.23).rgb) * 0.070;
                return float4(c, 1.0);
            }
            ENDCG
        }
        // 2: vertical blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 s = float2(0.0, _MainTex_TexelSize.y);
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.227;
                c += (tex2D(_MainTex, i.uv + s * 1.38).rgb + tex2D(_MainTex, i.uv - s * 1.38).rgb) * 0.316;
                c += (tex2D(_MainTex, i.uv + s * 3.23).rgb + tex2D(_MainTex, i.uv - s * 3.23).rgb) * 0.070;
                return float4(c, 1.0);
            }
            ENDCG
        }
        // 3: the composite: glow added, exposure, ACES
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c;
                if (_Burn > 0.003)
                {
                    // the afterburner: a radial blur toward the centre (which leads into a turn), the red and blue
                    // fringing apart along the same rays, and the edges pulled darker, all growing with the distance out
                    float2 cen = _BurnCenter.xy;
                    float2 d = i.uv - cen;
                    float dist = length(d * float2(1.0, _MainTex_TexelSize.x / _MainTex_TexelSize.y));
                    float s = _Burn * (0.006 + 0.01 * dist);
                    float fr = _Burn * 0.0045 * dist;
                    float3 acc = 0.0;
                    for (int k = 0; k < 10; k++)
                    {
                        float t = k / 9.0;
                        float2 uv = cen + d * (1.0 - s * t);
                        float2 rd = normalize(d + 1e-5) * fr;
                        float r = tex2D(_MainTex, uv + rd).r;
                        float g = tex2D(_MainTex, uv).g;
                        float b = tex2D(_MainTex, uv - rd).b;
                        acc += float3(r, g, b);
                    }
                    c = acc / 10.0;
                    c *= 1.0 - _Burn * 0.42 * dist * dist;
                }
                else c = tex2D(_MainTex, i.uv).rgb;
                c += tex2D(_Bloom, i.uv).rgb * _Intensity;
                c += tex2D(_Rays, i.uv).rgb * _RayGain;
                // the lens flare: a few faint ghosts of the sun mirrored through the frame's centre, cool-tinted
                if (_RayGain > 0.001)
                {
                    float2 g = 0.5 - i.uv;
                    float3 ghost = tex2D(_Rays, 0.5 + g * 0.55).rgb * float3(0.5, 0.7, 1.0) * 0.10
                                 + tex2D(_Rays, 0.5 + g * 1.35).rgb * float3(0.7, 0.85, 1.0) * 0.07
                                 + tex2D(_Rays, 0.5 + g * 2.2).rgb * float3(1.0, 0.75, 0.9) * 0.05;
                    c += ghost * saturate(_RayGain * 2.5);
                }
                c += tex2D(_Volume, i.uv).rgb;
                c = aces(c * _Exposure);
                // the grade: a split tone (cool shadows, warm highlights), saturation, contrast about mid grey, the blacks crushed
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                c *= lerp(_ShadowTint.rgb, _HighTint.rgb, saturate(l * 1.4));
                c = lerp(l.xxx, c, _Grade.x);
                c = (c - 0.5) * _Grade.y + 0.5;
                c = saturate((c - _Grade.z) / (1.0 - _Grade.z));
                return float4(c, 1.0);
            }
            ENDCG
        }
        // 4: the god rays' source: what is bright near the sun's place in the frame (the disc and its glare), so a rock
        // in front of the sun leaves a dark gap the rays stream round
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float lum = max(c.r, max(c.g, c.b));
                float k = saturate((lum - _RayThreshold) / max(lum, 1e-4));
                float aspect = _MainTex_TexelSize.w / _MainTex_TexelSize.z;
                float2 d = (i.uv - _SunUV.xy) * float2(1.0, aspect);
                float m = 1.0 - smoothstep(0.10, 0.34, length(d));
                return float4(c * k * m, 1.0);
            }
            ENDCG
        }
        // 5: the radial blur toward the sun: taps along the ray from the pixel to the sun, each a little fainter
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                const int N = 24;
                float2 toSun = _SunUV.xy - i.uv;
                float2 step = toSun * _RayLen / N;
                float2 p = i.uv;
                float3 acc = 0.0;
                float w = 1.0, tot = 0.0;
                for (int k = 0; k < N; k++)
                {
                    acc += tex2D(_MainTex, p).rgb * w;
                    tot += w;
                    p += step;
                    w *= _RayDecay;
                }
                return float4(acc / tot, 1.0);
            }
            ENDCG
        }
    }
}
