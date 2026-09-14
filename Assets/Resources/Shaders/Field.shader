// The hangar mouths' force fields: an unlit additive grid that drifts and flashes, seen from both sides.
Shader "BeltRunner/Field"
{
    Properties
    {
        _MainTex ("Grid", 2D) = "white" {}
        _Color ("Color", Color) = (0.37, 0.83, 0.94, 0.22)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * _Color.a;
                return float4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
