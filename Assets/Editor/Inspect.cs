using System.Text;
using UnityEditor;
using UnityEngine;

/// Editor-only: prints what the imported models contain (node tree, meshes, materials, shader property names,
/// bounds), so the code that wires them in can be written against facts.
///   Unity.exe -batchmode -quit -projectPath <project> -executeMethod Inspect.Models -logFile <log>
public static class Inspect
{
    static readonly string[] MODELS =
    {
        "Assets/Resources/Models/asteroids_lod1.glb", "Assets/Resources/Models/asteroids_lod2.glb", "Assets/Resources/Models/player_ship.glb",
        "Assets/Resources/Models/cargo_carrier.glb", "Assets/Resources/Models/ferron.glb", "Assets/Resources/Models/homeworld.glb", "Assets/Resources/Models/garden_habitat.glb",
    };

    public static void Models()
    {
        foreach (var path in MODELS)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder();
            sb.Append("inspect: ==== " + path + (go == null ? "  (NOT IMPORTED as a GameObject)" : "") + "\n");
            if (go == null)
            {
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path)) sb.Append("  asset " + a.GetType().Name + " " + a.name + "\n");
                Debug.Log(sb.ToString());
                continue;
            }
            int lines = 0;
            Walk(go.transform, 0, sb, ref lines);
            Debug.Log(sb.ToString());
        }
    }

    static void Walk(Transform t, int depth, StringBuilder sb, ref int lines)
    {
        if (lines > 400) return;
        lines++;
        var mf = t.GetComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();
        var ind = new string(' ', depth * 2);
        sb.Append(ind + t.name + " pos=" + t.localPosition.ToString("0.##") + " rot=" + t.localEulerAngles.ToString("0.#") + " scl=" + t.localScale.ToString("0.###"));
        if (mf != null && mf.sharedMesh != null)
        {
            var m = mf.sharedMesh;
            sb.Append("  mesh=" + m.name + " v=" + m.vertexCount + " sub=" + m.subMeshCount + " bounds=" + m.bounds.size.ToString("0.#") + " centre=" + m.bounds.center.ToString("0.#") + " colors=" + (m.colors.Length > 0) + " uv=" + (m.uv.Length > 0));
        }
        if (mr != null)
        {
            foreach (var mat in mr.sharedMaterials)
            {
                if (mat == null) { sb.Append("  mat=null"); continue; }
                sb.Append("\n" + ind + "    mat=" + mat.name + " shader=" + mat.shader.name);
                var sh = mat.shader;
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    var pn = sh.GetPropertyName(i);
                    var pt = sh.GetPropertyType(i);
                    if (pt == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        var tex = mat.GetTexture(pn);
                        if (tex != null) sb.Append(" " + pn + "=" + tex.name + "(" + tex.width + "x" + tex.height + ")");
                    }
                    else if (pt == UnityEngine.Rendering.ShaderPropertyType.Color)
                    {
                        sb.Append(" " + pn + "=" + mat.GetColor(pn).ToString("0.##"));
                    }
                    else if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range)
                    {
                        var v = mat.GetFloat(pn);
                        if (pn.ToLower().Contains("metal") || pn.ToLower().Contains("rough") || pn.ToLower().Contains("smooth") || pn.ToLower().Contains("emiss")) sb.Append(" " + pn + "=" + v.ToString("0.##"));
                    }
                }
            }
        }
        sb.Append("\n");
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb, ref lines);
    }
}
