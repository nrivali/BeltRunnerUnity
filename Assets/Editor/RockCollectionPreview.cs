using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

// Batch editor art/lighting verification, never Play Mode, Game.Boot or player save data.
public static class RockCollectionPreview
{
    static Camera camera;
    static Lighting lighting;
    static Belt belt;
    static Material[][] materials;
    static Mesh mesh;
    static Material[] activeMaterials;
    static Color ore;
    static float heat;
    static int frame;
    static bool build;
    static RenderTexture sceneRT, composite;
    static MethodInfo post;
    static ShadowQuality savedShadows;
    static ShadowResolution savedResolution;
    static int savedCascades;
    static float savedDistance;
    static readonly string output = Path.GetFullPath("Logs/rock-collection");
    static readonly StringBuilder report = new StringBuilder();

    public static void Prepare()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory("Assets/Resources/Materials/SculptCollection");
        foreach(string design in LargeRockPrototype.Collection)
        {
            foreach(string suffix in new[]{"stone","normal","surface"})
            {
                string path="Assets/Resources/Asteroids/SculptCollection/"+design+"/"+design+"_"+suffix+".png";
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null)throw new Exception("Missing map: "+path);
                importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=suffix=="stone";importer.maxTextureSize=4096;
                importer.mipmapEnabled=true;importer.isReadable=false;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;
                importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            var sh=Shader.Find("BeltRunner/SculptRock");
            if(sh==null||!sh.isSupported)throw new Exception("Sculpt shader unavailable");
            var mat=new Material(sh){name=design,enableInstancing=true};
            mat.SetFloat("_PackedAlbedo",1);mat.SetFloat("_BumpScale",1);
            mat.SetFloat("_StoneReflectance",.025f);mat.SetFloat("_Brightness",1.25f);
            string prefix="Asteroids/SculptCollection/"+design+"/"+design;
            var stone=Resources.Load<Texture2D>(prefix+"_stone");
            mat.SetTexture("_MainTex",stone);mat.SetTexture("_StoneTex",stone);
            mat.SetTexture("_BumpMap",Resources.Load<Texture2D>(prefix+"_normal"));
            mat.SetTexture("_MetalRough",Resources.Load<Texture2D>(prefix+"_surface"));
            string pathMat="Assets/Resources/Materials/SculptCollection/"+design+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(pathMat);
            if(existing==null)AssetDatabase.CreateAsset(mat,pathMat);
            else {EditorUtility.CopySerialized(mat,existing);Object.DestroyImmediate(mat);EditorUtility.SetDirty(existing);}
        }
        AssetDatabase.SaveAssets();
        foreach(string name in new[]{"BeltRunner/SculptRock","BeltRunner/Rock"})
            foreach(var msg in ShaderUtil.GetShaderMessages(Shader.Find(name)))
                if(msg.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new Exception(name+": "+msg.message);
    }

    public static void ValidateAndBuild(){build=true;Render();}
    public static void Render()
    {
        savedShadows=QualitySettings.shadows;savedResolution=QualitySettings.shadowResolution;
        savedCascades=QualitySettings.shadowCascades;savedDistance=QualitySettings.shadowDistance;
        try
        {
            if(!Application.isBatchMode)throw new Exception("Use an isolated batch editor.");
            Prepare();Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            camera=new GameObject("Rock collection art camera").AddComponent<Camera>();
            camera.nearClipPlane=1;camera.farClipPlane=50000;camera.fieldOfView=Ship.FOV;
            camera.allowHDR=true;camera.allowMSAA=true;camera.renderingPath=RenderingPath.Forward;
            camera.depthTextureMode|=DepthTextureMode.Depth;
            lighting=new Lighting();lighting.Setup(camera);lighting.SetZone(Data.ZONE_KESSLER);
            lighting.post.enabled=false;
            post=typeof(Post).GetMethod("OnRenderImage",BindingFlags.Instance|BindingFlags.NonPublic);
            Shader.SetGlobalFloat("_BeltTime",0);Shader.SetGlobalVector("_HeatAmt",Vector4.zero);
            belt=new Belt();materials=(Material[][])typeof(Belt).GetField("_subMats",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(belt);
            sceneRT=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf){antiAliasing=4};
            composite=new RenderTexture(1600,1000,0,RenderTextureFormat.ARGBHalf);
            report.AppendLine("Unity "+Application.unityVersion+"; "+SystemInfo.graphicsDeviceName);
            report.AppendLine("Isolated editor, real Kessler lighting and runtime Post; 1600x1000, FOV "+Ship.FOV+"; no Game.Boot or saves.");
            Camera.onPreCull+=Submit;EditorApplication.update+=Tick;
        }
        catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static int Key(string design)
    {
        int separator=design.LastIndexOf('_');
        return RockMeshes.ShapeIndex(design.Substring(0,separator))*2+(design.EndsWith("_B")?1:0);
    }
    static Color Ore(string name)
    {
        return (Color)typeof(Belt).GetMethod("OreSurfaceColor",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Data.OreIndex(name)});
    }
    static void Select(string design,int lod=0)
    {
        int key=Key(design);mesh=belt.MeshFor(key,lod);activeMaterials=materials[key];
        if(mesh==null||activeMaterials==null)throw new Exception("Missing integrated design "+design);
    }
    static void View(float distance)
    {
        camera.transform.position=new Vector3(-.5f,.2f,-.84f).normalized*distance;
        camera.transform.rotation=Quaternion.LookRotation(-camera.transform.position,Vector3.up);
    }
    static void Submit(Camera cam)
    {
        if(cam!=camera||mesh==null)return;
        var props=new MaterialPropertyBlock();props.SetVectorArray("_Color",new Vector4[]{ore});
        props.SetVectorArray("_Rail",new[]{new Vector4(0,1,0,heat)});
        var matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.Euler(15,38,10),Vector3.one*600);
        for(int sub=0;sub<mesh.subMeshCount;sub++)
            Graphics.DrawMeshInstanced(mesh,sub,activeMaterials[sub],new[]{matrix},1,props,ShadowCastingMode.On,true,0,camera);
    }
    static Color[] Capture(string name=null,bool linear=false)
    {
        var previous=RenderTexture.active;
        camera.targetTexture=sceneRT;camera.Render();
        if(!linear)post.Invoke(lighting.post,new object[]{sceneRT,composite});
        RenderTexture.active=linear?sceneRT:composite;
        var image=new Texture2D(1600,1000,linear?TextureFormat.RGBAFloat:TextureFormat.RGB24,false,linear);
        image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
        if(name!=null)File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        Color[] pixels=linear?image.GetPixels():null;
        Object.DestroyImmediate(image);camera.targetTexture=null;RenderTexture.active=previous;
        return pixels;
    }
    static double Energy(Color[] pixels)
    {
        double sum=0;
        for(int y=250;y<750;y++)for(int x=500;x<1100;x++)
        {
            var p=pixels[y*1600+x];sum+=p.r*.2126+p.g*.7152+p.b*.0722;
        }
        return sum/(500*600);
    }
    static double Difference(Color[] a,Color[] b)
    {
        double max=0;
        for(int y=250;y<750;y++)for(int x=500;x<1100;x++)
        {
            int i=y*1600+x;
            max=Math.Max(max,Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b));
        }
        return max;
    }
    static void CheckLighting(string design)
    {
        Select(design);ore=Ore("copper");heat=0;View(1900);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        Shader.SetGlobalFloat("_HazeDensity",0);
        lighting.sun.enabled=true;double lit=Energy(Capture(linear:true));Capture(design+"-sunlight");
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name="Invisible shadow-only test occluder";
        blocker.transform.position=Data.ZONE_KESSLER.sunDir.normalized*2200;
        blocker.transform.rotation=Quaternion.LookRotation(Data.ZONE_KESSLER.sunDir);
        blocker.transform.localScale=new Vector3(3000,3000,80);
        blocker.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;
        Capture(linear:true);double shadow=Energy(Capture(linear:true));Capture(design+"-cast-shadow");
        Object.DestroyImmediate(blocker);
        lighting.sun.enabled=false;Capture(linear:true);
        double dark=Energy(Capture(linear:true));Capture(design+"-sun-off");
        if(lit<.02||shadow>lit*.35||dark>lit*.2)
            throw new Exception(design+" sunlight/shadow regression: lit="+lit+" shadow="+shadow+" off="+dark);
        // A bright probe is deliberately adversarial: it must not bring ore highlights back.
        var cube=new Cubemap(16,TextureFormat.RGBAHalf,false);
        var white=new Color[256];for(int i=0;i<white.Length;i++)white[i]=Color.white*4;
        for(int face=0;face<6;face++)cube.SetPixels(white,(CubemapFace)face);cube.Apply();
        var oldMode=RenderSettings.defaultReflectionMode;var oldProbe=RenderSettings.customReflectionTexture;
        float oldIntensity=RenderSettings.reflectionIntensity;
        RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=cube;
        RenderSettings.reflectionIntensity=0;var noProbe=Capture(linear:true);
        RenderSettings.reflectionIntensity=5;var brightProbe=Capture(linear:true);
        double delta=Difference(noProbe,brightProbe);
        RenderSettings.defaultReflectionMode=oldMode;RenderSettings.customReflectionTexture=oldProbe;RenderSettings.reflectionIntensity=oldIntensity;
        Object.DestroyImmediate(cube);
        if(delta>.001)throw new Exception(design+" reflected environment glints with no sunlight: "+delta);
        // Zero diffuse isolates specular from a bright local point light. Only the sun
        // may create ore glints; local illumination and mining emission are separate.
        foreach(var mat in activeMaterials)mat.SetFloat("_Brightness",0);
        var local=new GameObject("Point light regression fixture").AddComponent<Light>();
        local.type=LightType.Point;local.transform.position=camera.transform.position;
        local.range=10000;local.intensity=20;local.color=Color.white;local.renderMode=LightRenderMode.ForcePixel;
        double point=Energy(Capture(linear:true));
        Object.DestroyImmediate(local.gameObject);
        foreach(var mat in activeMaterials)mat.SetFloat("_Brightness",1.25f);
        if(point>.001)throw new Exception(design+" glints under point light without sun: "+point);
        report.AppendLine(design+" point-only specular energy="+point.ToString("F6"));
        heat=.65f;double hot=Energy(Capture(linear:true));Capture(design+"-mining-heat");heat=0;
        if(hot<dark+.02)throw new Exception("Mining heat was accidentally removed.");
        report.AppendLine(design+": sunlight="+lit.ToString("F5")+"; cast shadow="+shadow.ToString("F5")+"; sun off="+dark.ToString("F5")+"; bright-probe max delta="+delta.ToString("F6")+"; mining heat="+hot.ToString("F5"));
        lighting.sun.enabled=true;Shader.SetGlobalFloat("_HazeDensity",.000012f);camera.clearFlags=CameraClearFlags.Skybox;
    }
    static void Tick()
    {
        if(++frame<25)return;
        EditorApplication.update-=Tick;
        try
        {
            string[] colors={"copper","gold","iron","platinum","crystal"};int index=0;
            foreach(string design in LargeRockPrototype.Collection)
            {
                for(int lod=0;lod<3;lod++)
                {
                    Select(design,lod);
                    if(mesh.subMeshCount!=1||activeMaterials[0].shader.name!="BeltRunner/SculptRock")throw new Exception("Design not installed: "+design);
                    if(mesh.uv.Length!=mesh.vertexCount||mesh.normals.Length!=mesh.vertexCount||mesh.tangents.Length!=mesh.vertexCount)throw new Exception("Missing vertex data");
                    foreach(var uv in mesh.uv)if(uv.x<-.001f||uv.x>1.001f||uv.y<-.001f||uv.y>1.001f)throw new Exception("UV outside atlas: "+design);
                    report.AppendLine(design+" LOD "+lod+": "+mesh.triangles.Length/3+" triangles, one material.");
                }
                foreach(string suffix in new[]{"stone","normal","surface"})
                {
                    var map=Resources.Load<Texture2D>("Asteroids/SculptCollection/"+design+"/"+design+"_"+suffix);
                    if(map==null||map.width!=4096||map.height!=4096)throw new Exception("Missing full-resolution map");
                    report.AppendLine(design+" "+suffix+": "+map.width+"x"+map.height+" "+map.format);
                }
                Select(design);ore=Ore(colors[index++]);View(design=="shard_A"?2400:2000);Capture(design);
                View(1100);Capture(design+"-close");
                ore=Ore("barren");View(2000);Capture(design+"-barren");
                ore=Ore("copper");Select(design,1);View(7500);Capture(design+"-lod1");
                Select(design,2);View(12000);Capture(design+"-lod2");
                Debug.Log("Rock collection validated: "+design);
            }
            CheckLighting("boulder_B");CheckLighting("boulder_A");CheckLighting("chunk_A");
            report.AppendLine("Passed: sunlit, cast-shadow, sun-off, hostile reflection probe and mining-heat checks for new, prototype and legacy rock materials.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());
            Finish(0);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"validation.txt"),report+"\nFAILED: "+ex);Debug.LogException(ex);Finish(1);}
    }
    static void Finish(int code)
    {
        Camera.onPreCull-=Submit;EditorApplication.update-=Tick;
        QualitySettings.shadows=savedShadows;QualitySettings.shadowResolution=savedResolution;
        QualitySettings.shadowCascades=savedCascades;QualitySettings.shadowDistance=savedDistance;
        if(code==0&&build)
        {
            if(Process.GetProcessesByName("BeltRunner").Length>0){Debug.LogError("Close the standard player before rebuilding.");code=1;}
            else Build.Player();
        }
        EditorApplication.Exit(code);
    }
}
