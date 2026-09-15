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
        _Library ("Imported glTF surface", Float) = 0
        _StoneTex ("Barren regolith albedo", 2D) = "white" {}
        _StoneNormal ("Barren regolith RGB normal", 2D) = "bump" {}
        _StoneMetalRough ("Barren regolith metal-roughness", 2D) = "white" {}
        _StoneColor ("Barren regolith factor", Color) = (1, 1, 1, 1)
        _DetailNormalMap ("Chipped rock RGB normal", 2D) = "bump" {}
        _DetailSurface ("Crevice occlusion / mineral variation / height", 2D) = "white" {}
        _DetailStrength ("Chipped rock relief", Range(0, 2)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert finalcolor:haze addshadow fullforwardshadows
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
        float _Library;
        sampler2D _StoneTex, _StoneNormal, _StoneMetalRough;
        float4 _StoneColor;
        sampler2D _DetailNormalMap, _DetailSurface;
        float _DetailStrength;
        float4 _OreFinish[8];   // roughness, metallic; index zero is barren, then Data.ORE_KEYS
        float _HazeDensity;    // set by Lighting: the belt's haze with distance, on the rock alone
        float4 _HazeColor;

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

        void haze(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            float d = distance(IN.worldPos, _WorldSpaceCameraPos);
            float f = exp(-d * _HazeDensity);
            color.rgb = lerp(_HazeColor.rgb, color.rgb, f);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            int finishIndex = (int)clamp(floor(c.a + 0.5), 0.0, 7.0);
            float ore = _Library * _Tint * step(0.5, finishIndex);
            float4 tex;
            float4 mr;
            float3 normal;
            // glTF normals contain XYZ, not Unity's compressed alpha/green encoding. Select all three
            // regolith maps on a barren vein face so it has neither metal patches nor a visible seam.
            if (_Library > 0.5 && _Tint > 0.5 && finishIndex == 0)
            {
                tex = tex2D(_StoneTex, IN.uv_MainTex) * _StoneColor;
                mr = tex2D(_StoneMetalRough, IN.uv_MainTex);
                normal = tex2D(_StoneNormal, IN.uv_MainTex).xyz * 2.0 - 1.0;
            }
            else
            {
                tex = tex2D(_MainTex, IN.uv_MainTex) * _BaseColor;
                mr = tex2D(_MetalRough, IN.uv_MainTex);
                normal = _Library > 0.5 ? tex2D(_BumpMap, IN.uv_MainTex).xyz * 2.0 - 1.0
                                        : UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), 1.0);
            }
            float crust = 0.0;
            float3 crustColor = 0.0;
            float crustRoughness = 1.0;
            if (ore > 0.5)
            {
                // The same stone plates continue over parts of the vein. This breaks up the perfect
                // ribbon boundary while retaining the broad ore regions and their existing geometry.
                float3 rock = tex2D(_StoneTex, IN.uv_MainTex).rgb;
                float3 mask = rock;
                #ifndef UNITY_COLORSPACE_GAMMA
                    mask = LinearToGammaSpace(mask);
                #endif
                crust = smoothstep(0.22, 0.36, dot(mask, float3(0.3, 0.59, 0.11))) * 0.85;
                crustColor = rock * _StoneColor.rgb;
                crustRoughness = tex2D(_StoneMetalRough, IN.uv_MainTex).g;
                normal = lerp(normal, tex2D(_StoneNormal, IN.uv_MainTex).xyz * 2.0 - 1.0, crust);
            }
            // Baked medium-scale chips remain readable on simplified meshes. Both maps are
            // periodic linear data, shared by every instance, with gentler relief on exposed metal.
            float4 detail = float4(1, 1, 0, 1);
            float relief = _Library * _DetailStrength;
            if (relief > 0.001)
            {
                // the detail tiles by the rock's size (the object scale is its radius), so a big rock keeps the same
                // grain per metre up close as a small one instead of stretching it; the relief firms up a little with size
                float rockR = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float tile = max(1.0, rockR / 22.0);
                relief *= lerp(1.0, 1.35, saturate((rockR - 22.0) / 60.0));
                float2 uv = IN.uv_MainTex * 0.75 * tile;
                detail = tex2D(_DetailSurface, uv);
                float3 dn = tex2D(_DetailNormalMap, uv).xyz * 2.0 - 1.0;
                dn.xy *= relief * lerp(0.90, 0.25, ore * (1.0 - crust));
                dn = normalize(dn);
                normal = normalize(float3(normal.xy + dn.xy, normal.z * dn.z));
            }
            normal.xy *= _BumpScale;
            normal = normalize(normal);
            float3 tint = c.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                tint = GammaToLinearSpace(tint);
            #endif
            float3 col = tex.rgb * lerp(float3(1.0, 1.0, 1.0), tint, _Library > 0.5 ? ore : _Tint);
            col = lerp(col, crustColor, crust);
            float exposedOre = ore * (1.0 - crust);
            float stoneDetail = saturate(relief) * (1.0 - exposedOre * 0.75);
            col *= lerp(1.0, lerp(0.45, 1.18, detail.g) * lerp(0.55, 1.0, detail.r), stoneDetail);
            o.Occlusion = lerp(1.0, detail.r, stoneDetail);
            // the body heat pulses a little, as the browser's does
            float h = saturate(IN.heat) * (0.92 + 0.08 * sin(_Time.y * 7.0 + IN.worldPos.x * 0.05 + IN.worldPos.y * 0.07));
            // Dry, diffuse regolith around reflective mineral facets. Preserve the authored roughness;
            // making the entire rock glossy turns its fine normal detail into sparkling noise.
            o.Albedo = col * lerp(0.28, 1.0, exposedOre) * (1.0 - h * 0.55);
            float2 finish = _OreFinish[finishIndex].xy;
            float roughness = _Library > 0.5 ? mr.g * lerp(1.0, finish.x, ore) : _Roughness * mr.g;
            o.Metallic = _Library > 0.5 ? mr.b * finish.y * ore : _Metallic * mr.b;
            roughness = lerp(roughness, crustRoughness, crust);
            o.Metallic *= 1.0 - crust;
            // Filter unresolved normal-map highlights with distance to limit specular shimmer.
            float variance = max(dot(ddx(normal), ddx(normal)), dot(ddy(normal), ddy(normal)));
            roughness = sqrt(saturate(roughness * roughness + min(0.12, variance * 0.15)));
            o.Smoothness = 1.0 - clamp(roughness, 0.16, 1.0);
            o.Normal = normal;
            float3 hc = h < 0.5 ? lerp(float3(0.9, 0.1, 0.02), float3(1.0, 0.45, 0.12), h * 2.0)
                                : lerp(float3(1.0, 0.45, 0.12), float3(1.0, 0.82, 0.5), (h - 0.5) * 2.0);
            float lum = dot(col, float3(0.3, 0.59, 0.11));
            o.Emission = hc * h * (0.45 + 0.65 * h) * (0.6 + lum * 1.2) * 0.5;
            // Intact ore is reflective, not emissive. Only mining damage and the beam produce heat light.
            // the laser's spot glows where the beam is cooking the stone
            o.Emission += Spot(IN.worldPos, _HeatPos0, _HeatAmt.x) + Spot(IN.worldPos, _HeatPos1, _HeatAmt.y);
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
