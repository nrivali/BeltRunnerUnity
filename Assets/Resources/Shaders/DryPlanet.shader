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
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _NormalMap, _SurfaceMap;
        float4 _Color;
        float _NormalStrength;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 data=tex2D(_SurfaceMap,IN.uv_MainTex).rgb;
            float3 n=tex2D(_NormalMap,IN.uv_MainTex).xyz*2.0-1.0;
            n.xy*=_NormalStrength;
            o.Normal=normalize(n);
            float3 pigment=tex2D(_MainTex,IN.uv_MainTex).rgb;
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
