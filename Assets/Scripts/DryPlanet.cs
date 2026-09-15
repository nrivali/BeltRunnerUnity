using UnityEngine;
using UnityEngine.Rendering;

/// Shared by the runtime planet and the editor art preview. Ferron's terrain is a Blender
/// mesh; explicit material references retain the full-resolution maps in player builds.
public static class DryPlanet
{
    static Mesh atmosphereMesh;
    static Material atmosphereMaterial;

    public static void Configure(GameObject root)
    {
        var material = Resources.Load<Material>("Materials/DryPlanet");
        if (material == null) { Debug.LogWarning("Dry planet surface material is missing."); return; }
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    public static void AddAtmosphere(Transform parent)
    {
        if (atmosphereMaterial == null)
        {
            var shader = Shader.Find("BeltRunner/Atmo");
            if (shader == null) return;
            atmosphereMaterial = new Material(shader);
            atmosphereMaterial.name = "Ferron amber atmosphere";
            atmosphereMaterial.SetColor("_Color", new Color(0.88f, 0.66f, 0.39f));
            atmosphereMaterial.SetFloat("_Power", 3.5f);
            atmosphereMaterial.SetFloat("_Gain", 1.4f);
        }
        if (atmosphereMesh == null) atmosphereMesh = Shell();
        var go = new GameObject("Dry atmosphere");
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 1.015f;
        go.AddComponent<MeshFilter>().sharedMesh = atmosphereMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = atmosphereMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // A smooth shell avoids visible facets on the narrow atmospheric limb.
    static Mesh Shell()
    {
        const int longitude = 256, latitude = 128;
        var vertices = new Vector3[(longitude + 1) * (latitude + 1)];
        var triangles = new System.Collections.Generic.List<int>();
        for (int y = 0; y <= latitude; y++)
        {
            float polar = Mathf.PI * y / latitude;
            for (int x = 0; x <= longitude; x++)
            {
                float a = 2f * Mathf.PI * x / longitude;
                vertices[y * (longitude + 1) + x] = new Vector3(Mathf.Sin(polar) * Mathf.Cos(a), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(a));
                if (x == longitude || y == latitude) continue;
                int i = y * (longitude + 1) + x, j = i + longitude + 1;
                if (y > 0) { triangles.Add(i); triangles.Add(i + 1); triangles.Add(j); }
                if (y < latitude - 1) { triangles.Add(i + 1); triangles.Add(j + 1); triangles.Add(j); }
            }
        }
        var mesh = new Mesh();
        mesh.name = "Smooth planet atmosphere";
        mesh.vertices = vertices;
        mesh.normals = vertices;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
