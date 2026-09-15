Shader "BeltRunner/DryPlanet"
{
    Properties
    {
        _Color ("Surface reflectance", Color) = (0.8,0.8,0.8,1)
        _MainTex ("Dry terrain albedo", 2D) = "white" {}
        _NormalMap ("Terrain RGB normal", 2D) = "bump" {}
        _SurfaceMap ("Occlusion / roughness / height", 2D) = "white" {}
        _NormalStrength ("Terrain relief", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _NormalMap, _SurfaceMap;
        float4 _Color;
        float _NormalStrength;
        struct Input { float2 uv_MainTex; };
        void vert(inout appdata_full v)
        {
            float pole=smoothstep(0.025,0.13,min(v.texcoord.y,1.0-v.texcoord.y));
            v.normal=normalize(lerp(normalize(v.vertex.xyz),v.normal,pole));
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 data=tex2D(_SurfaceMap,IN.uv_MainTex).rgb;
            float3 n=tex2D(_NormalMap,IN.uv_MainTex).xyz*2.0-1.0;
            // Fade tangent-space relief where longitude tangents converge at a pole.
            n.xy*=_NormalStrength*smoothstep(0.015,0.10,min(IN.uv_MainTex.y,1.0-IN.uv_MainTex.y));
            o.Normal=normalize(lerp(float3(0,0,1),n,smoothstep(0.025,0.13,min(IN.uv_MainTex.y,1.0-IN.uv_MainTex.y))));
            float3 pigment=tex2D(_MainTex,IN.uv_MainTex).rgb;
            // A projected terrain patch keeps albedo continuous at the UV pole itself.
            float latitude=min(IN.uv_MainTex.y,1.0-IN.uv_MainTex.y);
            float longitude=IN.uv_MainTex.x*6.2831853;
            float radius=sin(IN.uv_MainTex.y*3.14159265);
            float2 capUV=0.5+float2(cos(longitude),sin(longitude))*radius*0.55;
            pigment=lerp(tex2D(_MainTex,capUV).rgb,pigment,smoothstep(0.055,0.13,latitude));
            // The game's HDR composite expects reflectance values even in its Gamma project.
            #ifdef UNITY_COLORSPACE_GAMMA
                pigment=GammaToLinearSpace(pigment);
            #endif
            o.Albedo=pigment*_Color.rgb;
            o.Occlusion=data.r;
            o.Metallic=0;
            o.Smoothness=1.0-clamp(data.g,0.75,1.0);
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
