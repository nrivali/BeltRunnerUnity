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
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _Bloom;
        float _Exposure, _Threshold, _Intensity, _Burn;
        float4 _BurnCenter;

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
                    float s = _Burn * (0.045 + 0.06 * dist);
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
                return float4(aces(c * _Exposure), 1.0);
            }
            ENDCG
        }
    }
}
