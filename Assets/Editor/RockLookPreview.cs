using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-only material contact sheet. Does not enter Play Mode, boot Game, or access player saves.
public static class RockLookPreview
{
    static Belt belt;
    static Material[][] materials;
    static Camera cam;
    static Lighting lighting;
    static string output;
    static int frame;
    static int shot;
    static readonly string[] shots = { "ores", "copper", "barren", "distance", "unlit", "shapes", "mining-heat", "shapes-2", "sun-right", "detail-before", "detail-after", "approach-before", "approach-after", "flight-before", "flight-after" };
    static readonly string[] shapes = { "lumpy", "chunk", "boulder", "cratered", "jagged", "potato", "shard", "slab" };
    static MethodInfo colorMethod;
    static bool flightReady;
    static ShadowQuality savedShadows;
    static ShadowResolution savedResolution;
    static int savedCascades;
    static float savedDistance;

    public static void Render()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use this contact sheet from a batch editor, not the working scene.");
        PrepareTextures();
        savedShadows = QualitySettings.shadows;
        savedResolution = QualitySettings.shadowResolution;
        savedCascades = QualitySettings.shadowCascades;
        savedDistance = QualitySettings.shadowDistance;
        string tag = "current";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-rockPreview") tag = args[i + 1];
        output = Path.GetFullPath("Logs/rock-look/" + tag);
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        cam = new GameObject("Material preview camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -1600);
        cam.nearClipPlane = 1;
        cam.farClipPlane = 50000;
        cam.fieldOfView = 39;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        cam.renderingPath = RenderingPath.Forward;
        lighting = new Lighting();
        lighting.Setup(cam);
        lighting.SetZone(Data.ZONE_KESSLER);
        lighting.sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.65f, -0.35f, 0.65f));
        Shader.SetGlobalFloat("_BeltTime", 0);
        Shader.SetGlobalVector("_HeatAmt", Vector4.zero);
        belt = new Belt();
        materials = (Material[][])typeof(Belt).GetField("_subMats", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(belt);
        colorMethod = typeof(Belt).GetMethod("RockColor", BindingFlags.NonPublic | BindingFlags.Instance);
        Dump();
        Camera.onPreCull += Submit;
        frame = 0;
        shot = 0;
        EditorApplication.update += Tick;
    }

    static int Key(string shape)
    {
        for (int i = 0; i < RockMeshes.SHAPES.Length; i++) if (RockMeshes.SHAPES[i].key == shape) return i * 2;
        throw new Exception("Missing shape " + shape);
    }

    static void Draw(string shape, int ore, Vector3 position, float radius, int lod, Quaternion rotation, int variant = 0)
    {
        int key = Key(shape) + variant;
        var matrix = Matrix4x4.TRS(position, rotation, Vector3.one * radius);
        if (belt.ore.Count == 0) belt.ore.Add(ore); else belt.ore[0] = ore;
        var color = (Color)colorMethod.Invoke(belt, new object[] { 0 });
        var block = new MaterialPropertyBlock();
        block.SetVectorArray("_Color", new Vector4[] { color });
        block.SetVectorArray("_Rail", new Vector4[] { new Vector4(0, 1, 0, shot == 6 ? 0.65f : 0f) });
        for (int sub = 0; sub < belt.MeshFor(key, lod).subMeshCount; sub++)
            Graphics.DrawMeshInstanced(belt.MeshFor(key, lod), sub, materials[key][sub], new[] { matrix }, 1, block, ShadowCastingMode.On, true, 0, cam);
    }

    static void Submit(Camera camera)
    {
        if (camera != cam) return;
        if (shot >= 13) { belt.Draw(); return; }
        if (shot == 9 || shot == 10)
            Draw("boulder", 1, Vector3.zero, 400, 0, Quaternion.Euler(25, -30, 14));
        else if (shot == 11 || shot == 12)
        {
            for (int i = 0; i < 3; i++)
                Draw(new[] { "boulder", "slab", "cratered" }[i], i, new Vector3((i - 1) * 380, 0, 0), 180, shot == 11 ? 1 : 0, Quaternion.Euler(25, -30, 14));
        }
        else if (shot == 0)
        {
            for (int i = 0; i < 8; i++)
                Draw("lumpy", i == 7 ? -1 : i, new Vector3((i % 4 - 1.5f) * 385, (0.5f - i / 4) * 420, 0), 170, 0, Quaternion.Euler(15, 28, 10));
        }
        else if (shot == 1 || shot == 2 || shot == 4 || shot == 6 || shot == 8)
            Draw("boulder", shot == 2 ? -1 : 1, Vector3.zero, 400, 0, Quaternion.Euler(25, -30, 14));
        else if (shot == 3)
        {
            for (int i = 0; i < 3; i++)
                Draw("lumpy", 1, new Vector3((i - 1) * 540, 0, 0), 250 / (1 + i), i, Quaternion.Euler(15, 28, 10));
        }
        else if (shot == 7)
        {
            var remaining = new[] { "pancake", "cluster", "spindle", "bean", "wedge", "hollow", "hollow", "cratered" };
            for (int i = 0; i < remaining.Length; i++)
                Draw(remaining[i], 1, new Vector3((i % 4 - 1.5f) * 385, (0.5f - i / 4) * 420, 0), 150, 0, Quaternion.Euler(15, 38, 10), i >= 6 ? 1 : 0);
        }
        else
        {
            for (int i = 0; i < 8; i++)
                Draw(shapes[i], 1, new Vector3((i % 4 - 1.5f) * 385, (0.5f - i / 4) * 420, 0), 165, 0, Quaternion.Euler(15, 28, 10));
        }
    }

    static void Tick()
    {
        if (++frame < 25) return;
        try
        {
            if (shot >= 13 && !flightReady) PrepareFlight();
            bool before = shot == 9 || shot == 11 || shot == 13;
            foreach (var group in materials) foreach (var mat in group) mat.SetFloat("_DetailStrength", before ? 0f : 1f);
            cam.fieldOfView = shot >= 11 ? Ship.FOV : 39;
            lighting.sun.enabled = shot != 4;
            if (shot < 13) lighting.sun.transform.rotation = Quaternion.LookRotation(shot == 8 ? new Vector3(-0.7f, -0.15f, 0.65f) : new Vector3(0.65f, -0.35f, 0.65f));
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGBHalf);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            string path = Path.Combine(output, shots[shot] + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = old;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("rock-preview: " + path);
            frame = 20;
            if (++shot < shots.Length) return;
            Camera.onPreCull -= Submit;
            EditorApplication.update -= Tick;
            RestoreQuality();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.update -= Tick;
            Camera.onPreCull -= Submit;
            RestoreQuality();
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    // Exercise the real seeded Belt loader, proximity query, promotion, demotion and instanced
    // rendering at the ship's cruise FOV. No Game, State.Init, Play Mode or player save access.
    static void PrepareFlight()
    {
        belt.Clear();
        belt.Build(Data.ZONE_KESSLER, Game.SEED);
        int target = -1;
        for (int i = 0; i < belt.count; i++)
            if (belt.ore[i] == 1 && belt.meshKey[i] == Key("boulder") && belt.radius[i] > 120 && belt.radius[i] < 600)
            { target = i; break; }
        if (target < 0) throw new Exception("No copper boulder for the flight validation.");
        var direction = (Data.ZONE_KESSLER.sunDir.normalized + new Vector3(0.35f, 0.15f, -0.1f)).normalized;
        var center = belt.RockPos(target);
        var report = new System.Text.StringBuilder();
        foreach (float radii in new[] { 8f, 11f, 13f, 11f, 9f })
        {
            var viewer = center + direction * (belt.radius[target] * radii);
            belt.UpdateLod0(viewer, belt.RocksWithin(viewer, 12000));
            // Sequence explicitly checks both sides of the hysteresis band.
            bool actual = belt.IsLod0(target);
            report.AppendLine("distance/radius=" + radii + " detailed=" + actual + " total detailed=" + belt.Lod0Count);
        }
        string transitions = report.ToString();
        var rows = transitions.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var expectedStates = new[] { true, true, false, false, true };
        for (int i = 0; i < rows.Length; i++)
            if (!rows[i].Contains("detailed=" + expectedStates[i])) throw new Exception("LOD hysteresis failed: " + rows[i]);
        var from = center + direction * (belt.radius[target] * 8f);
        belt.UpdateLod0(from, belt.RocksWithin(from, 12000));
        belt.ApplyOffset(from);
        belt.Cull(from);
        cam.transform.position = Vector3.zero;
        cam.transform.rotation = Quaternion.LookRotation(center - from, Vector3.up);
        cam.farClipPlane = Belt.DRAW_DIST;
        lighting.SetZone(Data.ZONE_KESSLER);
        lighting.Update(from, from + Vector3.one * 100000);
        report.AppendLine("seed=" + Game.SEED + " rocks=" + belt.count + " target=" + target + " radius=" + belt.radius[target]);
        report.AppendLine(belt.DrawReport());
        File.WriteAllText(Path.Combine(output, "flight-validation.txt"), report.ToString());
        Debug.Log("rock-flight-validation: " + report);
        flightReady = true;
    }

    static void RestoreQuality()
    {
        QualitySettings.shadows = savedShadows;
        QualitySettings.shadowResolution = savedResolution;
        QualitySettings.shadowCascades = savedCascades;
        QualitySettings.shadowDistance = savedDistance;
    }

    static void PrepareTextures()
    {
        AssetDatabase.Refresh();
        foreach (var textureName in new[] { "regolith_albedo", "regolith_normal", "regolith_metalrough", "ore_albedo", "ore_normal", "ore_metalrough", "rock_detail_normal", "rock_detail_surface" })
        {
            var path = "Assets/Resources/Asteroids/" + textureName + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            // RGB glTF-style normal maps are sampled explicitly in Rock.shader, so keep them as linear data.
            if (importer.mipmapEnabled && importer.sRGBTexture == (textureName.EndsWith("_albedo")) && importer.anisoLevel == 4 && importer.filterMode == FilterMode.Trilinear && importer.textureCompression == TextureImporterCompression.CompressedHQ) continue;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = textureName.EndsWith("_albedo");
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }

    static void Dump()
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(belt.DrawReport());
        lines.AppendLine("Color space: " + QualitySettings.activeColorSpace);
        // Validate the meshes after glTFast import and Belt's resource lookup, so a missing LOD cannot
        // silently pass a render by falling back to the older procedural mesh.
        if (belt.libraryShapes != 28) throw new Exception("Expected all 28 asteroid variants.");
        for (int lod = 0; lod < 3; lod++)
        {
            int total = 0, smallest = int.MaxValue, largest = 0;
            for (int key = 0; key < 28; key++)
            {
                var mesh = belt.MeshFor(key, lod);
                if (mesh == null || mesh.subMeshCount != 2 || mesh.normals.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount)
                    throw new Exception("Incomplete asteroid geometry key=" + key + " lod=" + lod);
                int count = mesh.triangles.Length / 3;
                if (lod == 0 && count < 30000) throw new Exception("Detailed asteroid LOD did not load: " + key);
                total += count;
                smallest = Math.Min(smallest, count);
                largest = Math.Max(largest, count);
            }
            lines.AppendLine("LOD " + lod + ": 28 meshes, triangles " + smallest + ".." + largest + ", total " + total);
        }
        var model = Resources.Load<GameObject>("Models/asteroids_lod1");
        var mf = model.GetComponentsInChildren<MeshFilter>(true)[0];
        lines.AppendLine("Mesh " + mf.name + " vertices=" + mf.sharedMesh.vertexCount + " tangents=" + mf.sharedMesh.tangents.Length);
        foreach (var m in mf.GetComponent<MeshRenderer>().sharedMaterials)
        {
            lines.AppendLine(m.name + " shader=" + m.shader.name + " color=" + m.GetColor("baseColorFactor") + " roughness=" + m.GetFloat("roughnessFactor") + " metallic=" + m.GetFloat("metallicFactor"));
            foreach (var p in new[] { "baseColorTexture", "normalTexture", "metallicRoughnessTexture" })
            {
                var t = m.GetTexture(p) as Texture2D;
                lines.AppendLine(p + "=" + (t != null ? t.name + " " + t.width + "x" + t.height + " " + t.format + " mips=" + t.mipmapCount + " linear=" + !t.isDataSRGB : "null") + " scale=" + m.GetTextureScale(p) + " offset=" + m.GetTextureOffset(p));
            }
        }
        foreach (var name in new[] { "regolith_albedo", "regolith_normal", "regolith_metalrough", "ore_albedo", "ore_normal", "ore_metalrough", "rock_detail_normal", "rock_detail_surface" })
        {
            var tex = Resources.Load<Texture2D>("Asteroids/" + name);
            if (tex != null) lines.AppendLine("Override " + name + " " + tex.width + "x" + tex.height + " mips=" + tex.mipmapCount + " format=" + tex.format + " sRGB=" + tex.isDataSRGB + " filter=" + tex.filterMode + " anisotropy=" + tex.anisoLevel);
        }
        File.WriteAllText(Path.Combine(output, "materials.txt"), lines.ToString());
        Debug.Log(lines.ToString());
    }

    public static void BuildReviewPlayer()
    {
        Build.Player();
    }
}
