using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// Editor-only: adds the packages the project needs from the command line, so the manifest never has to guess a
/// version.   Unity.exe -batchmode -quit -projectPath <project> -executeMethod Packages.Add -logFile <log>
public static class Packages
{
    static readonly string[] WANTED = { "com.unity.cloud.gltfast" };

    public static void Add()
    {
        foreach (var id in WANTED)
        {
            var req = Client.Add(id);
            while (!req.IsCompleted) Thread.Sleep(50);
            if (req.Status == StatusCode.Success) Debug.Log("packages: added " + req.Result.packageId);
            else Debug.LogError("packages: " + id + " failed: " + (req.Error != null ? req.Error.message : "?"));
        }
    }
}
