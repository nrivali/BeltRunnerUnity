// Ore reflects the live directional sun, including Unity's N.L and shadow attenuation.
// Do not sample reflection probes: their baked sun remains bright on the night side and
// inside cast shadows. Ambient diffuse and intentional mining emission remain available.
#ifndef BELT_ROCK_SUNLIGHT
#define BELT_ROCK_SUNLIGHT
#include "UnityPBSLighting.cginc"

inline half4 BeltSunBRDF(half3 diffuse, half3 specular, half oneMinusReflectivity,
    half smoothness, float3 normal, float3 viewDir, UnityGI gi)
{
    gi.indirect.specular = 0;
    #if defined(SPOT)
        return half4(0, 0, 0, 0);   // the ship's flashlight, the game's one spot light: the rock takes it in surf (Torch in Rock.shader), never here
    #endif
    #if !defined(DIRECTIONAL) && !defined(DIRECTIONAL_COOKIE)
        return half4(diffuse * (gi.light.color * saturate(dot(normal, gi.light.dir)) + gi.indirect.diffuse), 1);
    #endif
    return UNITY_BRDF_PBS(diffuse, specular, oneMinusReflectivity, smoothness,
        normal, viewDir, gi.light, gi.indirect);
}

inline half4 LightingSunlitSpecular(SurfaceOutputStandardSpecular s, float3 viewDir, UnityGI gi)
{
    s.Normal = normalize(s.Normal);
    half oneMinusReflectivity;
    half3 diffuse = EnergyConservationBetweenDiffuseAndSpecular(s.Albedo, s.Specular, oneMinusReflectivity);
    half alpha;
    diffuse = PreMultiplyAlpha(diffuse, s.Alpha, oneMinusReflectivity, alpha);
    half4 c = BeltSunBRDF(diffuse, s.Specular, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, gi);
    c.a = alpha;
    return c;
}
inline void LightingSunlitSpecular_GI(SurfaceOutputStandardSpecular s, UnityGIInput data, inout UnityGI gi)
{
    gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal);
}

inline half4 LightingSunlitMetallic(SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
{
    s.Normal = normalize(s.Normal);
    half oneMinusReflectivity; half3 specular;
    half3 diffuse = DiffuseAndSpecularFromMetallic(s.Albedo, s.Metallic, specular, oneMinusReflectivity);
    half alpha;
    diffuse = PreMultiplyAlpha(diffuse, s.Alpha, oneMinusReflectivity, alpha);
    half4 c = BeltSunBRDF(diffuse, specular, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, gi);
    c.a = alpha;
    return c;
}
inline void LightingSunlitMetallic_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
{
    gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal);
}
#endif
