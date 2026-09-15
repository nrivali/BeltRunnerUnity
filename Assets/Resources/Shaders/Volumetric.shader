// Volumetric dust: a ray march from the camera along each pixel's view ray, through a thin dust that scatters the sun
// toward the eye (Henyey-Greenstein, forward-peaked), stopped by the depth buffer, and shadowed by the sun's own
// shadow map (copied to _BeltCascadeShadow after Unity draws it; the four cascades' matrices and bounds are Unity's
// own globals), so a rock between the sun and the dust cuts a dark shaft through it. Half resolution, dithered per
// pixel, added to the frame by the post composite. A little value noise gives the dust drift and clumps.
Shader "BeltRunner/Volumetric"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            UNITY_DECLARE_SHADOWMAP(_BeltCascadeShadow);
            float4 _VolC0, _VolC1, _VolC2, _VolC3;   // the far-plane corners from the camera: bottom-left, bottom-right, top-left, top-right
            float4 _VolSun;         // direction to the sun
            float4 _VolSunColor;
            float4 _VolParams;      // x density per unit, y reach in units, z steps, w intensity
            float4 _VolParams2;     // x anisotropy g, y noise scale (1/units), z time, w noise strength 0..1

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

            // 1 in the sun, 0 in a rock's shadow; the first of the four cascades whose sphere holds the point; lit
            // outside them all
            float ShadowAt(float3 wp)
            {
                float3 d0 = wp - unity_ShadowSplitSpheres[0].xyz;
                float3 d1 = wp - unity_ShadowSplitSpheres[1].xyz;
                float3 d2 = wp - unity_ShadowSplitSpheres[2].xyz;
                float3 d3 = wp - unity_ShadowSplitSpheres[3].xyz;
                float4 sc;
                if (dot(d0, d0) < unity_ShadowSplitSpheres[0].w) sc = mul(unity_WorldToShadow[0], float4(wp, 1.0));
                else if (dot(d1, d1) < unity_ShadowSplitSpheres[1].w) sc = mul(unity_WorldToShadow[1], float4(wp, 1.0));
                else if (dot(d2, d2) < unity_ShadowSplitSpheres[2].w) sc = mul(unity_WorldToShadow[2], float4(wp, 1.0));
                else if (dot(d3, d3) < unity_ShadowSplitSpheres[3].w) sc = mul(unity_WorldToShadow[3], float4(wp, 1.0));
                else return 1.0;
                return UNITY_SAMPLE_SHADOW(_BeltCascadeShadow, sc.xyz);
            }

            float4 frag(v2f i) : SV_Target
            {
                // the depth texture and the view rays are in the screen's own orientation; the source may be flipped
                float2 uvd = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0.0) uvd.y = 1.0 - uvd.y;
                #endif
                float d01 = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvd));
                float3 ray = lerp(lerp(_VolC0.xyz, _VolC1.xyz, uvd.x), lerp(_VolC2.xyz, _VolC3.xyz, uvd.x), uvd.y);   // to the far plane
                float rayLen = length(ray);
                float3 dir = ray / rayLen;
                // the march runs to the surface or the reach, whichever is nearer, in `steps` even steps, dithered
                float sMax = min(d01, _VolParams.y / rayLen);
                int steps = (int)_VolParams.z;
                float ds = sMax / steps;
                float stepLen = ds * rayLen;
                float dither = frac(52.9829189 * frac(0.06711056 * i.pos.x + 0.00583715 * i.pos.y));
                float g = _VolParams2.x;
                float cosT = dot(dir, normalize(_VolSun.xyz));
                float phase = (1.0 - g * g) / (4.0 * 3.14159265 * pow(max(1e-3, 1.0 + g * g - 2.0 * g * cosT), 1.5));
                float density = _VolParams.x;
                float T = 1.0;
                float acc = 0.0;
                float s = ds * dither;
                for (int k = 0; k < 96; k++)
                {
                    if (k >= steps) break;
                    float3 wp = _WorldSpaceCameraPos + ray * s;
                    float n = vnoise(wp * _VolParams2.y + float3(_VolParams2.z * 0.03, 0.0, _VolParams2.z * 0.02));
                    n = n * 0.65 + vnoise(wp * _VolParams2.y * 2.3 + 11.0) * 0.35;
                    float dn = density * lerp(1.0 - _VolParams2.w, 1.0 + _VolParams2.w, n);
                    float sh = ShadowAt(wp);
                    acc += sh * dn * T * stepLen;
                    T *= exp(-dn * stepLen);
                    s += ds;
                }
                return float4(_VolSunColor.rgb * acc * phase * _VolParams.w, 1.0);
            }
            ENDCG
        }
    }
}
