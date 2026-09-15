using UnityEngine;

// Blender-authored collection: each design has three matching LODs and unique baked maps.
// It replaces only the visual library entry: seeded positions, ore and collision radii stay intact.
public static class LargeRockPrototype
{
    public static bool Apply(Mesh[,] meshes, Material[][] materials, bool[][] tints, Vector4[] finishes)
    {
        bool applied = ApplyVariant("boulder_A", "Models/boulder_prototype", "Materials/LargeBoulder", meshes, materials, tints, finishes);
        foreach (string design in Collection)
            applied |= ApplyVariant(design, "Models/sculpt_" + design, "Materials/SculptCollection/" + design, meshes, materials, tints, finishes);
        return applied;
    }

    public static readonly string[] Collection = { "boulder_B", "cratered_A", "potato_A", "slab_A", "shard_A" };

    static bool ApplyVariant(string design, string modelPath, string materialPath,
        Mesh[,] meshes, Material[][] materials, bool[][] tints, Vector4[] finishes)
    {
        var model = Resources.Load<GameObject>(modelPath);
        var surface = Resources.Load<Material>(materialPath);
        if (model == null || surface == null) return false;
        var lods = new Mesh[3];
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            for (int lod = 0; lod < 3; lod++)
                if (filter.name == design + "_LOD" + lod) lods[lod] = filter.sharedMesh;
        for (int lod = 0; lod < 3; lod++)
            if (lods[lod] == null || lods[lod].subMeshCount != 1) return false;
        int separator = design.LastIndexOf('_');
        int key = RockMeshes.ShapeIndex(design.Substring(0, separator)) * 2 + (design.EndsWith("_B") ? 1 : 0);
        var material = new Material(surface);
        material.enableInstancing = true;
        material.SetVectorArray("_OreFinish", finishes);
        for (int lod = 0; lod < 3; lod++) meshes[key, lod] = lods[lod];
        materials[key] = new[] { material };
        tints[key] = new[] { true };
        return true;
    }
}
