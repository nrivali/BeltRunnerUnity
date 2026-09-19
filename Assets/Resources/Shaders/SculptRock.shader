// Shared material for Blender-sculpted rocks with matched unique atlases. Shared world motion and heat
// follow Rock.shader; explicit dielectric reflectance keeps dark basalt from looking waxy
// in this game's gamma-colour renderer followed by its ACES post effect.
Shader "BeltRunner/SculptRock"
{
    Properties
    {
        _Color ("Ore colour / index", Color) = (1,1,1,0)
        _Rail ("Orbit and heat", Vector) = (0,1,0,0)
        _MainTex ("Unique stone and ore albedo", 2D) = "white" {}
        _StoneTex ("Barren stone albedo", 2D) = "white" {}
        _BumpMap ("Baked sculpt RGB normal", 2D) = "bump" {}
        _MetalRough ("R cavity / G roughness / B ore", 2D) = "white" {}
        _BumpScale ("Sculpt normal strength", Float) = 1
        _PackedAlbedo ("Reconstruct neutral metal from packed roughness", Float) = 0
        _Brightness ("Stone brightness", Float) = 1.25
        _StoneReflectance ("Basalt reflectance", Range(0,0.1)) = 0.025
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf SunlitSpecular exclude_path:deferred vertex:vert finalcolor:haze addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.5
        #include "RockSunlight.cginc"
        #include "RockTorch.cginc"
        sampler2D _MainTex, _StoneTex, _BumpMap, _MetalRough;
        float _BumpScale, _StoneReflectance, _PackedAlbedo, _Brightness, _BeltTime, _HazeDensity;
        float4 _HazeColor, _HeatPos0, _HeatPos1, _HeatAmt;
        float4 _OreFinish[8];
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Rail)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float2 uv_MainTex; float3 worldPos; float3 worldNormal; float heat; INTERNAL_DATA };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            UNITY_SETUP_INSTANCE_ID(v);
            float4 rail=UNITY_ACCESS_INSTANCED_PROP(Props,_Rail);
            if(rail.z>0.5)
            {
                float th=28.0*_BeltTime/max(rail.y,1.0);
                float c=cos(th)-1.0, s=sin(th), ca=cos(rail.x), sa=sin(rail.x);
                float3 drift=float3(rail.y*(ca*c+sa*s),0,rail.y*(sa*c-ca*s));
                v.vertex.xyz=mul(unity_WorldToObject,float4(mul(unity_ObjectToWorld,v.vertex).xyz+drift,1)).xyz;
            }
            #if defined(UNITY_PASS_SHADOWCASTER)
                v.vertex.xyz-=v.normal*0.003;
            #endif
            o.heat=rail.w;
        }
        float3 Spot(float3 wp,float4 hp,float h)
        {
            if(h<=0.001)return 0;
            float g=h*smoothstep(hp.w,hp.w*0.12,distance(wp,hp.xyz));
            float3 hc=h<0.5?lerp(float3(1,.16,.03),float3(1,.58,.2),h*2):lerp(float3(1,.58,.2),float3(1,.95,.82),(h-.5)*2);
            return hc*(.55+h*.6)*g*.85;
        }
        void haze(Input IN,SurfaceOutputStandardSpecular o,inout fixed4 color)
        {
            color.rgb=lerp(_HazeColor.rgb,color.rgb,exp(-distance(IN.worldPos,_WorldSpaceCameraPos)*_HazeDensity));
        }
        void surf(Input IN,inout SurfaceOutputStandardSpecular o)
        {
            float4 tint=UNITY_ACCESS_INSTANCED_PROP(Props,_Color);
            int index=(int)clamp(floor(tint.a+.5),0,7);
            float3 surface=tex2D(_MetalRough,IN.uv_MainTex).rgb;
            float ore=surface.b*step(.5,index);
            float3 albedo=index==0?tex2D(_StoneTex,IN.uv_MainTex).rgb:tex2D(_MainTex,IN.uv_MainTex).rgb;
            // The collection stores only stone, normal and packed surface maps. Its neutral
            // metal albedo and roughness share the same authored grain, avoiding a fourth 4K map.
            if (_PackedAlbedo > .5)
            {
                float metalValue = .40 + saturate((surface.g - .63) / .29) * .39;
                #ifdef UNITY_COLORSPACE_GAMMA
                    metalValue = LinearToGammaSpace(float3(metalValue,metalValue,metalValue)).r;
                #endif
                albedo = lerp(tex2D(_StoneTex,IN.uv_MainTex).rgb,metalValue.xxx,ore);
            }
            float3 color=tint.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                color=GammaToLinearSpace(color);
            #endif
            float3 metal=albedo*color;
            float3 normal=tex2D(_BumpMap,IN.uv_MainTex).xyz*2-1;
            normal.xy*=_BumpScale;normal=normalize(normal);
            float roughness=surface.g*lerp(1,_OreFinish[index].x,ore);
            float variance=max(dot(ddx(normal),ddx(normal)),dot(ddy(normal),ddy(normal)));
            roughness=sqrt(saturate(roughness*roughness+min(.12,variance*.15)));
            float h=saturate(IN.heat)*(.92+.08*sin(_Time.y*7+IN.worldPos.x*.05+IN.worldPos.y*.07));
            // Specular workflow gives the stone its own low reflectance; ore takes the
            // established mineral colour and reflectivity, with almost no diffuse component.
            float metallic=ore*_OreFinish[index].y;
            o.Albedo=albedo*.36*(1-metallic)*(1-h*.55)*_Brightness;
            o.Specular=lerp(_StoneReflectance.xxx,metal,metallic);
            o.Smoothness=1-clamp(roughness,.16,1);
            o.Normal=normal;o.Occlusion=surface.r;
            float3 hc=h<.5?lerp(float3(.9,.1,.02),float3(1,.45,.12),h*2):lerp(float3(1,.45,.12),float3(1,.82,.5),(h-.5)*2);
            o.Emission=hc*h*(.45+.65*h)*(.6+dot(albedo,float3(.3,.59,.11))*1.2)*.5;
            o.Emission+=Spot(IN.worldPos,_HeatPos0,_HeatAmt.x)+Spot(IN.worldPos,_HeatPos1,_HeatAmt.y);
            o.Emission+=Torch(IN.worldPos,WorldNormalVector(IN,normal),o.Albedo*(1-SpecularStrength(o.Specular)));   // the flashlight (RockTorch.cginc), once, in the base pass
            o.Emission+=Scan(IN.worldPos);   // the radar pulse sweeping over the stone (RockTorch.cginc)
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
