using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Editor-only: builds the Windows player from the command line, for the smoke run.
///   Unity.exe -batchmode -quit -projectPath <project> -executeMethod Build.Player -logFile <log>
/// The game needs no scene content, but a build needs a scene, so an empty one is made under Assets/Scenes.
public static class Build
{
    const string SCENE = "Assets/Scenes/Main.unity";

    public static void Player()
    {
        if (!File.Exists(SCENE))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, SCENE);
            AssetDatabase.Refresh();
        }
        // the world is built from code, so no asset references these shaders and a build would leave them out
        AlwaysInclude("Standard", "Sprites/Default", "BeltRunner/Rock", "BeltRunner/Sky", "BeltRunner/Post", "BeltRunner/Spark", "BeltRunner/Field", "BeltRunner/Scorch", "Legacy Shaders/Diffuse", "UI/Default");
        // the belt draws with GPU instancing; the build strips instancing variants unless a material asset with
        // instancing enabled uses the shader, so the rock material lives as an asset the game loads from Resources
        RockMaterialAsset();
        PlayerSettings.companyName = "nrivali";
        PlayerSettings.productName = "Belt Runner";
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;   // borderless full screen at the desktop resolution; Alt+Enter drops to a window; the smoke runs pass -screen-fullscreen 0
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { SCENE },
            locationPathName = "Builds/Windows/BeltRunner.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("build: " + report.summary.result + " · " + report.summary.totalErrors + " errors · " + (report.summary.totalSize / (1024 * 1024)) + " MB · " + report.summary.outputPath);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public const string ROCK_MAT = "Assets/Resources/Materials/Rock.mat";

    [MenuItem("Belt Runner/Create rock material asset")]
    public static void RockMaterialAsset()
    {
        if (File.Exists(ROCK_MAT)) return;
        var sh = Shader.Find("BeltRunner/Rock");
        if (sh == null) { Debug.LogWarning("build: BeltRunner/Rock shader not found, no material asset made"); return; }
        Directory.CreateDirectory("Assets/Resources/Materials");
        var m = new Material(sh);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, ROCK_MAT);
        AssetDatabase.SaveAssets();
        Debug.Log("build: created " + ROCK_MAT);
    }

    /// Adds the named shaders to Project Settings › Graphics › Always Included Shaders (once each), and keeps every
    /// instancing variant in the build.
    static void AlwaysInclude(params string[] names)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0) { Debug.LogWarning("build: GraphicsSettings.asset not found"); return; }
        var so = new SerializedObject(assets[0]);
        var strip = so.FindProperty("m_InstancingStripping");
        if (strip != null && strip.intValue != 2) { strip.intValue = 2; Debug.Log("build: instancing variants kept (Keep All)"); }
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in names)
        {
            var sh = Shader.Find(name);
            if (sh == null) { Debug.LogWarning("build: shader " + name + " not found"); continue; }
            bool have = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { have = true; break; }
            if (have) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            Debug.Log("build: always include shader " + name);
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }
}
