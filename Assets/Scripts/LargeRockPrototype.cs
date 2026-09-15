using UnityEngine;

// One Blender-authored boulder variant, with three matching LODs and unique baked maps.
// It replaces only the visual library entry: seeded positions, ore and collision radii stay intact.
public static class LargeRockPrototype
{
    public static bool Apply(Mesh[,] meshes, Material[][] materials, bool[][] tints, Vector4[] finishes)
    {
        var model = Resources.Load<GameObject>("Models/boulder_prototype");
        var surface = Resources.Load<Material>("Materials/LargeBoulder");
        if (model == null || surface == null) return false;
        var lods = new Mesh[3];
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            for (int lod = 0; lod < 3; lod++)
                if (filter.name == "boulder_A_LOD" + lod) lods[lod] = filter.sharedMesh;
        for (int lod = 0; lod < 3; lod++)
            if (lods[lod] == null || lods[lod].subMeshCount != 1) return false;
        int key = RockMeshes.ShapeIndex("boulder") * 2;
        var material = new Material(surface);
        material.enableInstancing = true;
        material.SetVectorArray("_OreFinish", finishes);
        for (int lod = 0; lod < 3; lod++) meshes[key, lod] = lods[lod];
        materials[key] = new[] { material };
        tints[key] = new[] { true };
        return true;
    }
}
