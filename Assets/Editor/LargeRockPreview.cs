using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

// Isolated editor art comparison. No Game.Boot, Play Mode, smoke runner or player save access.
public static class LargeRockPreview
{
    static Camera camera;
    static Lighting lighting;
    static Mesh[] oldMeshes = new Mesh[3], newMeshes = new Mesh[3];
    static Material[] oldMaterials, newMaterials;
    static bool after, buildAfterPreview;
    static int frame, shot, count = 1, lod;
    static float radius = 600;
    static Color oreColor;
    static string output;
    static RenderTexture sceneRT, composite;
    static Texture2D fence;
    static MethodInfo post;
    static ShadowQuality savedShadows;
    static ShadowResolution savedResolution;
    static int savedCascades;
    static float savedDistance;
    static readonly string[] shots = { "mining-before", "mining-after", "close-before", "close-after", "approach-before", "approach-after", "barren-after", "gold-after", "iron-after", "lod1-after", "lod2-after", "unlit-after", "mining-heat-after" };

    public static void Prepare()
    {
        AssetDatabase.Refresh();
        foreach (string suffix in new[] { "albedo", "stone", "normal", "surface" })
        {
            string path = "Assets/Resources/Asteroids/LargeBoulder/boulder_" + suffix + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("Missing sculpt map: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = suffix == "albedo" || suffix == "stone";
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        const string matPath = "Assets/Resources/Materials/LargeBoulder.mat";
        var shader = Shader.Find("BeltRunner/SculptRock");
        if (shader == null || !shader.isSupported) throw new Exception("Rock shader missing or unsupported.");
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var material = new Material(shader); material.name = "LargeBoulder";
        material.enableInstancing = true;
        material.SetFloat("_BumpScale", 1); material.SetFloat("_StoneReflectance", .025f);
        material.SetTextureScale("_MainTex", Vector2.one);
        material.SetTexture("_MainTex", Resources.Load<Texture2D>("Asteroids/LargeBoulder/boulder_albedo"));
        material.SetTexture("_StoneTex", Resources.Load<Texture2D>("Asteroids/LargeBoulder/boulder_stone"));
        material.SetTexture("_BumpMap", Resources.Load<Texture2D>("Asteroids/LargeBoulder/boulder_normal"));
        material.SetTexture("_MetalRough", Resources.Load<Texture2D>("Asteroids/LargeBoulder/boulder_surface"));
        if(existing == null) AssetDatabase.CreateAsset(material,matPath);
        else { EditorUtility.CopySerialized(material,existing); Object.DestroyImmediate(material); material=existing; }
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
    }

    public static void ValidateAndBuild() { buildAfterPreview=true; Render(); }

    public static void Render()
    {
        savedShadows = QualitySettings.shadows; savedResolution = QualitySettings.shadowResolution;
        savedCascades = QualitySettings.shadowCascades; savedDistance = QualitySettings.shadowDistance;
        try { RenderInternal(); } catch(Exception ex) { Debug.LogException(ex); Finish(1); }
    }

    static void RenderInternal()
    {
        if (!Application.isBatchMode) throw new Exception("Run in an isolated batch editor.");
        Prepare();
        savedShadows = QualitySettings.shadows; savedResolution = QualitySettings.shadowResolution;
        savedCascades = QualitySettings.shadowCascades; savedDistance = QualitySettings.shadowDistance;
        output = Path.GetFullPath("Logs/large-rock-prototype"); Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        camera = new GameObject("Asteroid comparison camera").AddComponent<Camera>();
        camera.nearClipPlane = 1; camera.farClipPlane = 50000; camera.fieldOfView = Ship.FOV;
        camera.allowHDR = true; camera.allowMSAA = true; camera.renderingPath = RenderingPath.Forward;
        camera.depthTextureMode |= DepthTextureMode.Depth;
        lighting = new Lighting(); lighting.Setup(camera); lighting.SetZone(Data.ZONE_KESSLER);
        lighting.post.enabled = false;
        post = typeof(Post).GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.NonPublic);
        Shader.SetGlobalFloat("_BeltTime", 0); Shader.SetGlobalVector("_HeatAmt", Vector4.zero);
        var belt = new Belt(); int key = RockMeshes.ShapeIndex("boulder") * 2;
        for (int level = 0; level < 3; level++)
        {
            newMeshes[level] = belt.MeshFor(key, level);
            var root = Resources.Load<GameObject>("Models/asteroids_lod" + level);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.StartsWith("boulder_A_")) oldMeshes[level] = mf.sharedMesh;
            if (oldMeshes[level] == null || newMeshes[level] == null || oldMeshes[level] == newMeshes[level])
                throw new Exception("Comparison failed to select distinct old/new LOD " + level);
        }
        newMaterials = ((Material[][])typeof(Belt).GetField("_subMats", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(belt))[key];
        var legacy = Resources.Load<GameObject>("Models/asteroids_lod1");
        foreach (var mf in legacy.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.StartsWith("boulder_A_")) continue;
            var src = mf.GetComponent<MeshRenderer>().sharedMaterials;
            oldMaterials = new Material[src.Length]; Material stone = null;
            foreach (var sm in src) if (sm.name == "Barren_Regolith") stone = sm;
            var convert = typeof(Belt).GetMethod("ConvertMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < src.Length; i++) oldMaterials[i] = (Material)convert.Invoke(belt, new object[] { src[i], stone });
        }
        var report = new System.Text.StringBuilder();
        report.AppendLine("Unity " + Application.unityVersion + "; GPU " + SystemInfo.graphicsDeviceName);
        report.AppendLine("One of 28 variants replaced: boulder_A. Original 28 keys, instancing, orbit, radii and ore palette retained.");
        report.AppendLine("Real Kessler lighting + runtime Post composite; 1600x1000, 4x MSAA, FOV " + Ship.FOV);
        for (int i = 0; i < 3; i++)
        {
            var mesh = newMeshes[i];
            if (mesh.subMeshCount != 1 || mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount)
                throw new Exception("Missing runtime vertex data at LOD " + i);
            foreach (var uv in mesh.uv) if (uv.x < -0.0001f || uv.x > 1.0001f || uv.y < -0.0001f || uv.y > 1.0001f) throw new Exception("UV outside unique chart atlas.");
            report.AppendLine("LOD " + i + ": " + oldMeshes[i].triangles.Length / 3 + " -> " + mesh.triangles.Length / 3 + " triangles; 2 -> 1 material draws.");
        }
        report.AppendLine("Sculpt material="+newMaterials[0].shader.name);
        foreach(string suffix in new[]{"albedo","stone","normal","surface"}) {
            var texture=Resources.Load<Texture2D>("Asteroids/LargeBoulder/boulder_"+suffix);
            if(texture.width!=4096||texture.height!=4096)throw new Exception("Map did not import at 4K: "+suffix);
            report.AppendLine(suffix+": "+texture.width+"x"+texture.height+" "+texture.format+"; "+(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture)/1048576f).ToString("F1")+" MiB native texture allocation");
        }
        File.WriteAllText(Path.Combine(output, "validation.txt"), report.ToString());
        sceneRT = new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf); sceneRT.antiAliasing = 4;
        composite = new RenderTexture(1600,1000,0,RenderTextureFormat.ARGBHalf);
        fence = new Texture2D(1,1,TextureFormat.RGB24,false);
        Camera.onPreCull += Submit; EditorApplication.update += Tick;
    }

    static Color Ore(string key)
    {
        if (key == "barren") return new Color(1,1,1,0);
        var colorMethod = typeof(Belt).GetMethod("OreSurfaceColor", BindingFlags.Static | BindingFlags.NonPublic);
        return (Color)colorMethod.Invoke(null,new object[] { Data.OreIndex(key) });
    }
    static void View(float distance)
    {
        camera.transform.position = new Vector3(-.5f,.2f,-.84f).normalized * distance;
        camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position, Vector3.up);
    }
    static void Submit(Camera cam)
    {
        if (cam != camera) return;
        var mesh = after ? newMeshes[lod] : oldMeshes[lod];
        var materials = after ? newMaterials : oldMaterials;
        var matrices = new Matrix4x4[count]; var colors = new Vector4[count]; var rails = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = count == 1 ? Vector3.zero : camera.transform.right * ((i%6-2.5f)*radius*2.15f) + camera.transform.up * ((i/6-1.5f)*radius*2.1f);
            matrices[i] = Matrix4x4.TRS(position, Quaternion.Euler(15,38,10), Vector3.one*radius);
            colors[i] = oreColor; rails[i] = new Vector4(0,1,0,shot==12?.65f:0);
        }

        // Runtime close LODs submit each asteroid individually, so the stress measurement does too.
        for(int i=0;i<count;i++)
        {
            var single = new MaterialPropertyBlock();
            single.SetVectorArray("_Color",new[]{colors[i]});single.SetVectorArray("_Rail",new[]{rails[i]});
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                Graphics.DrawMeshInstanced(mesh,sub,materials[sub],new[]{matrices[i]},1,single,ShadowCastingMode.On,true,0,camera);
        }
    }
    static void RenderFrame(bool synchronize)
    {
        var previous=RenderTexture.active;
        camera.targetTexture=sceneRT;camera.Render();post.Invoke(lighting.post,new object[]{sceneRT,composite});
        if(synchronize) { RenderTexture.active=composite;fence.ReadPixels(new Rect(0,0,1,1),0,0);fence.Apply(); }
        camera.targetTexture=null;RenderTexture.active=previous;
    }
    static void Save(string name)
    {
        RenderFrame(false);var previous=RenderTexture.active;RenderTexture.active=composite;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
        File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=previous;Object.DestroyImmediate(image);
    }
    static void Benchmark()
    {
        foreach(var mat in newMaterials)mat.SetFloat("_BumpScale",1);
        lighting.sun.shadows=LightShadows.Hard;
        var log=new System.Text.StringBuilder();
        log.AppendLine("Synchronized editor render+post+GPU readback wall time, not player FPS or isolated GPU time. 8 warm-up renders, 20 interleaved samples per version; median reported.");
        foreach(int copies in new[]{1,24})
        {
            count=copies;lod=0;radius=copies==1?600:180;View(copies==1?1600:2100);lighting.sun.enabled=true;oreColor=Ore("copper");
            var times=new[]{new List<double>(),new List<double>()};
            for(int sample=-8;sample<40;sample++)
            {
                after=(sample&1)==1;var watch=Stopwatch.StartNew();RenderFrame(true);watch.Stop();
                if(sample>=0)times[after?1:0].Add(watch.Elapsed.TotalMilliseconds);
            }
            foreach(var t in times)t.Sort();
            log.AppendLine(copies+" close LOD asteroid(s): old "+((times[0][9]+times[0][10])*.5).ToString("F2")+" ms; prototype "+((times[1][9]+times[1][10])*.5).ToString("F2")+" ms.");
        }
        File.AppendAllText(Path.Combine(output,"validation.txt"),log.ToString());
    }
    static void Tick()
    {
        if(++frame<25)return;
        try
        {
            after=shot%2==1||shot>=6;count=1;radius=600;lod=shot==9?1:shot==10?2:0;
            oreColor=Ore(shot==6?"barren":shot==7?"gold":shot==8?"iron":"copper");
            lighting.sun.enabled=shot!=11;
            View(shot==2||shot==3?1000:shot==4||shot==5?6400:shot==9||shot==10?8500:1600);
            Save(shots[shot]);Debug.Log("large-rock preview: "+shots[shot]);
            frame=23;
            if(++shot<shots.Length)return;
            if(Process.GetProcessesByName("BeltRunner").Length==0)Benchmark();
            else File.AppendAllText(Path.Combine(output,"validation.txt"),"Benchmark deferred: player was running.\n");
            Finish(0);
        }
        catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Finish(int code)
    {
        Camera.onPreCull-=Submit;EditorApplication.update-=Tick;
        QualitySettings.shadows=savedShadows;QualitySettings.shadowResolution=savedResolution;
        QualitySettings.shadowCascades=savedCascades;QualitySettings.shadowDistance=savedDistance;
        if(code==0 && buildAfterPreview)
        {
            if(Process.GetProcessesByName("BeltRunner").Length>0)throw new Exception("Close the standard player before rebuilding it.");
            Build.Player();
        }
        EditorApplication.Exit(code);
    }
}
