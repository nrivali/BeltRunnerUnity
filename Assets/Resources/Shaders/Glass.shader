// The cockpit glass (2026-09-23, Astra's cockpit_glass v1): a clear pane with a faint haze, a touch more at a glancing
// angle, and up to eight marks laid on it by UV (Glass.cs): each a decal texture, scuffs, a crack web, the shatter or a
// scorch, at a centre, a size and a turn of its own, blended over the pane. The crack textures are white lines with
// alpha, the scorch black with alpha, so one blend does for all. Transparent, no depth write, so the outside stays
// outside for the burner's blur.
Shader "BeltRunner/Glass"
{
    Properties
    {
        _Alpha ("Haze", Float) = 0.05
        _Tex0 ("Scuffs", 2D) = "black" {}
        _Tex1 ("Crack web", 2D) = "black" {}
        _Tex2 ("Shatter", 2D) = "black" {}
        _Tex3 ("Scorch", 2D) = "black" {}
        _Count ("Marks", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _Tex0, _Tex1, _Tex2, _Tex3;
            float _Alpha, _Count;
            float4 _DecalA[8];   // centre.xy, size.xy, in the pane's UV
            float4 _DecalB[8];   // cos, sin of the turn, opacity, kind (0 scuffs, 1 web, 2 shatter, 3 scorch)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wv : TEXCOORD2;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wv = WorldSpaceViewDir(v.vertex);
                return o;
            }

            float4 Mark(float kind, float2 uv, float2 dx, float2 dy)
            {
                if (kind < 0.5) return tex2Dgrad(_Tex0, uv, dx, dy);
                if (kind < 1.5) return tex2Dgrad(_Tex1, uv, dx, dy);
                if (kind < 2.5) return tex2Dgrad(_Tex2, uv, dx, dy);
                return tex2Dgrad(_Tex3, uv, dx, dy);
            }

            float4 frag(v2f i) : SV_Target
            {
                float fr = 1.0 - saturate(abs(dot(normalize(i.wn), normalize(i.wv))));
                float4 c = float4(0.96, 0.98, 1.0, _Alpha * (1.0 + 2.0 * fr * fr));
                // the marks: the pane's UV gradients once, each mark's rotated and scaled from them, so the samples
                // keep their mip level without a gradient inside a branch
                float2 dx = ddx(i.uv), dy = ddy(i.uv);
                int n = (int)_Count;
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    if (k >= n) break;
                    float4 a = _DecalA[k];
                    float4 b = _DecalB[k];
                    float2 d = i.uv - a.xy;
                    float2 r = float2(d.x * b.x + d.y * b.y, -d.x * b.y + d.y * b.x);
                    float2 luv = r / a.zw + 0.5;
                    if (luv.x <= 0.0 || luv.x >= 1.0 || luv.y <= 0.0 || luv.y >= 1.0) continue;
                    float2 ldx = float2(dx.x * b.x + dx.y * b.y, -dx.x * b.y + dx.y * b.x) / a.zw;
                    float2 ldy = float2(dy.x * b.x + dy.y * b.y, -dy.x * b.y + dy.y * b.x) / a.zw;
                    float4 m = Mark(b.w, luv, ldx, ldy);
                    float ma = saturate(m.a * b.z);
                    float3 mc = b.w > 2.5 ? m.rgb : m.rgb * float3(0.88, 0.94, 1.0);   // the cracks a touch cool; the scorch as it is
                    c.rgb = lerp(c.rgb, mc, ma);
                    c.a = max(c.a, ma);
                }
                return c;
            }
            ENDCG
        }
    }
}
