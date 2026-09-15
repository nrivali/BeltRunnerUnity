using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Batch art validation only. Never enters Play Mode, boots Game or accesses saves.
public static class DryPlanetPreview
{
    static Camera camera;
    static GameObject planet;
    static Lighting lighting;
    static int frame, shot;
    static bool baseline;
    static readonly string[] shots = { "orbit", "terrain", "far-side", "terminator", "night", "belt-distance", "far-distance", "seam", "north-pole" };
    static string output;
    static ShadowQuality shadows;
    static ShadowResolution resolution;
    static int cascades;
    static float shadowDistance;

    public static void Prepare()
    {
        AssetDatabase.Refresh();
        foreach (string suffix in new[] { "albedo", "normal", "surface" })
        {
            string path = "Assets/Resources/Planets/Dry/dry_" + suffix + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("Missing planet map " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = suffix == "albedo";
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        const string materialPath = "Assets/Resources/Materials/DryPlanet.mat";
        var shader = Shader.Find("BeltRunner/DryPlanet");
        if (shader == null || !shader.isSupported) throw new Exception("Dry planet shader unavailable.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        material.shader = shader;
        material.SetColor("_Color", new Color(.8f, .8f, .8f, 1));
        material.SetFloat("_NormalStrength", 1f);
        material.SetTexture("_MainTex", Resources.Load<Texture2D>("Planets/Dry/dry_albedo"));
        material.SetTexture("_NormalMap", Resources.Load<Texture2D>("Planets/Dry/dry_normal"));
        material.SetTexture("_SurfaceMap", Resources.Load<Texture2D>("Planets/Dry/dry_surface"));
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    public static void RenderBaseline() { baseline=true; Render(); }

    public static void Render()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this preview in a batch editor.");
        Prepare();
        shadows=QualitySettings.shadows; resolution=QualitySettings.shadowResolution;
        cascades=QualitySettings.shadowCascades; shadowDistance=QualitySettings.shadowDistance;
        output=Path.GetFullPath(baseline?"Logs/planet-detail-before":"Logs/planet-detail-after");
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        camera=new GameObject("Planet preview camera").AddComponent<Camera>();
        camera.nearClipPlane=1; camera.farClipPlane=100000; camera.fieldOfView=39;
        camera.allowHDR=true; camera.allowMSAA=true;
        camera.renderingPath=RenderingPath.Forward;
        lighting=new Lighting(); lighting.Setup(camera); lighting.SetZone(Data.ZONE_KESSLER);
        // Post is a play-mode component; invoke its actual renderer explicitly for editor art checks.
        if(lighting.post==null) throw new Exception("Runtime post-processing is missing.");
        lighting.post.enabled=false;
        camera.depthTextureMode|=DepthTextureMode.Depth;
        var asset=Resources.Load<GameObject>("Models/ferron");
        if (asset == null) throw new Exception("Ferron model did not import.");
        planet=UnityEngine.Object.Instantiate(asset);
        planet.transform.localScale=Vector3.one*400;
        DryPlanet.Configure(planet);
        Validate();
        DryPlanet.AddAtmosphere(planet.transform);
        frame=0;shot=0;
        EditorApplication.update+=Tick;
    }

    static void Validate()
    {
        var report=new System.Text.StringBuilder();
        int count=0,triangles=0;
        foreach (var mf in planet.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh=mf.sharedMesh;
            if(mesh==null || mesh.normals.Length!=mesh.vertexCount || mesh.uv.Length!=mesh.vertexCount || mesh.tangents.Length!=mesh.vertexCount) throw new Exception("Incomplete terrain geometry.");
            foreach(var v in mesh.vertices) if(v.magnitude<.99f || v.magnitude>1.0001f) throw new Exception("Terrain outside its collision envelope.");
            triangles+=mesh.triangles.Length/3;count++;
            var material=mf.GetComponent<MeshRenderer>().sharedMaterial;
            if(material.shader.name!="BeltRunner/DryPlanet") throw new Exception("Runtime material was not applied.");
        }
        if(count!=1 || triangles<200000) throw new Exception("Expected the new detailed Ferron terrain.");
        report.AppendLine("Terrain meshes="+count+" triangles="+triangles+"; normals, tangents, UVs and collision envelope passed.");
        foreach(string suffix in new[]{"albedo","normal","surface"})
        {
            var t=Resources.Load<Texture2D>("Planets/Dry/dry_"+suffix);
            if(t==null || t.width!=4096 || t.height!=2048 || t.mipmapCount<10 || t.isDataSRGB!=(suffix=="albedo")) throw new Exception("Planet map import mismatch: "+suffix);
            report.AppendLine(suffix+": "+t.width+"x"+t.height+" "+t.format+" mips="+t.mipmapCount+" sRGB="+t.isDataSRGB);
        }
        File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());
        Debug.Log("dry-planet validation: "+report);
    }

    static void Tick()
    {
        if(++frame<20)return;
        try
        {
            camera.fieldOfView=shot>=5 && shot<=6?62:shot==1?33:39;
            Vector3 view=shot==2?new Vector3(0,.2f,1):shot==3?new Vector3(-1,.25f,-.1f):shot==4?-Data.ZONE_KESSLER.sunDir.normalized:new Vector3(0,.16f,-1);
            if(shot==7)view=new Vector3(1,.15f,0);
            if(shot==8)view=new Vector3(.02f,1,0);
            float range=shot==5?400*Data.DEPOT_ORBIT/(Data.ZONE_KESSLER.planetR*Data.PLANET_SCALE):shot==6?3200:shot==1?1030:1450;
            camera.transform.position=view.normalized*range;
            camera.transform.rotation=Quaternion.LookRotation(-camera.transform.position,Vector3.up);
            var previous=RenderTexture.active;var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf);rt.antiAliasing=4;
            camera.targetTexture=rt;camera.Render();
            var composite=new RenderTexture(1600,1000,0,RenderTextureFormat.ARGBHalf);
            typeof(Post).GetMethod("OnRenderImage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(lighting.post,new object[]{rt,composite});
            RenderTexture.active=composite;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(output,shots[shot]+".png"),image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(composite);
            Debug.Log("dry-planet preview: "+shots[shot]);
            frame=15;if(++shot<shots.Length)return;
            File.AppendAllText(Path.Combine(output,"validation.txt"),shots.Length+" editor views rendered through the actual runtime Post.OnRenderImage composite. Belt-distance uses the game FOV and carrier-orbit/planet-radius ratio.\n");Finish(0);
        }
        catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }

    static void Finish(int code)
    {
        EditorApplication.update-=Tick;
        QualitySettings.shadows=shadows;QualitySettings.shadowResolution=resolution;
        QualitySettings.shadowCascades=cascades;QualitySettings.shadowDistance=shadowDistance;
        EditorApplication.Exit(code);
    }

    public static void BuildPlayer()
    {
        Prepare();
        Build.Player();
    }
}
