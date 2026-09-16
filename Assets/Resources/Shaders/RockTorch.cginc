// The ship's flashlight on the rocks, shared by Rock.shader and SculptRock.shader: the browser's SpotLight (#fff1d6,
// 14,000 cd falling off as 1/d, decay 1, cut off smoothly at 7,000 u, a cone of half-angle pi/8) done in the rock
// surface shaders rather than as a Unity light, since a Unity spot light never reaches a rock drawn by
// DrawMeshInstanced under the belt's 60,000 u mesh bounds (2026-09-16: torch on and off shots of a rock 400 u ahead
// were the same). Ship.Tick sets the globals every frame; each shader adds Torch() to its emission once, in the base
// pass, so it is unshadowed and comes on top of the sun, as the browser's did.
#ifndef BELT_ROCK_TORCH
#define BELT_ROCK_TORCH
#include "UnityCG.cginc"
#include "UnityStandardUtils.cginc"

float4 _TorchPos;      // the flashlight in scene space, w its strength (0 while it is off, docked or warping)
float4 _TorchDir;      // its beam direction, w the cosine of the cone's half-angle
float4 _TorchColor;    // its colour, w its reach

// wp the fragment in scene space, wn its world normal, diffuse its diffuse albedo (the albedo less the specular share)
float3 Torch(float3 wp, float3 wn, float3 diffuse)
{
    if (_TorchPos.w <= 0.0) return float3(0.0, 0.0, 0.0);
    float3 tv = _TorchPos.xyz - wp;
    float d = max(length(tv), 1.0);
    tv /= d;
    float cone = smoothstep(_TorchDir.w, _TorchDir.w + 0.006, dot(-tv, _TorchDir.xyz));   // about a degree of softening at the edge
    float cut = saturate(1.0 - pow(d / _TorchColor.w, 4.0));
    float3 c = _TorchColor.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
        c = GammaToLinearSpace(c);
    #endif
    return diffuse * c * (_TorchPos.w / d) * cut * cut * cone * saturate(dot(wn, tv));
}
#endif
