// Sparks and the laser's spot glow: unlit, additive, instanced, each instance its own colour (premultiplied by its
// fade). No depth write, so streaks never punch holes in each other.
Shader "BeltRunner/Spark"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 col : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.col.rgb * i.col.a, i.col.a);
            }
            ENDCG
        }
    }
}
