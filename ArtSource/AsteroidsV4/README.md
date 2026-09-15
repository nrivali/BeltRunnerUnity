# Unity asteroid concept replacement — 0.9.122

This replaces the entire active asteroid mesh library with new Blender models based on the three approved Imagegen concepts: fractured, layered and weathered rock. All 14 game shape keys have two variants and three LODs, for 84 replacement meshes. The active game, mined fragments and rock scrap use these resource meshes through the existing Belt loader.

## Art and materials

- Fractured: lumpy, chunk, boulder, jagged and wedge. Broad support planes form cohesive bodies, cut by irregular shear faults and chipped surfaces.
- Layered: shard, pancake, slab and spindle. Beveled strata and elongated or flattened profiles, with mineral seams following the layers.
- Weathered: potato, cratered, cluster, bean and hollow. Impact depressions, eroded surfaces and carved cavities. The hollow variants have closed, consistently oriented topology through all LODs.

Ore is a flush part of each mesh, with broad surface regions and patches. There are no added protruding ore nodes. The rock and ore retain separate material slots. Seven runtime ore finishes preserve their identifying colours; barren rocks use stone on both slots. The stone is rough and nonmetallic. Ore has a full metallic mask and a roughness texture, with per-ore roughness factors of 0.30–0.38 and metallic factors of 0.82–0.97. Intact ore has no emission; the existing mining heat remains independent.

The built-in Imagegen tool created the new stone base-colour source and the three approved concept boards. Their exact prompts are in `imagegen-prompts.json`. The stone source is **1254 × 1254**, imported by Unity at 1024; it is not a native 2K or 4K albedo. Blender bakes the stone normal and roughness maps at 2048, and generates the metal albedo, subtle normal grain and roughness/metallic maps at 2048. Data maps are linear RGB; albedo is sRGB. All runtime maps have mipmaps, trilinear filtering, 4× anisotropy and BC7 compression.

Unity's rock material applies a 0.5 stone reflectance factor and 0.6 UV scale for the new texture, plus the per-ore finish. The metal uses actual Standard-shader lighting and reflections. Sun-angle previews demonstrate moving highlights. Sun-off previews retain faint sky reflections but lose the strong direct highlights. These are isolated Unity renders, not gameplay screenshots and not the Imagegen concept boards.

## Runtime and budgets

| LOD | Triangles per variant | Total across 28 variants | Previous total |
| --- | ---: | ---: | ---: |
| Close | 41,060–43,734 | 1,185,136 | 1,365,858 |
| Middle | 2,336–2,688 | 70,468 | 69,322 |
| Far | 590–698 | 17,930 | 16,854 |

The runtime GLBs total approximately 60.5 MB, down from 145.6 MB. They contain compact 512-pixel fallback textures; `Belt.ConvertMaterial` binds the full runtime maps from `Resources/Asteroids`. The middle and far budgets remain close to the previous set. Frame rate has not been benchmarked.

Belt density, seeded world generation, ore distribution, mining balance, orbit drift, LOD distances and instanced draw structure are unchanged. The very distant belt still uses the existing speck representation. Collisions remain the game's approximate spheres and do not trace the new surface cavities. Claude's concurrent global lighting work is preserved.

## Source and rebuild

`asteroids.blend` contains the 84 meshes, packed textures, an editable gallery, and a sun-lit preview scene. Lower LODs are hidden in its viewport by default. `surface-materials.blend` holds the material-bake nodes. `concepts/` holds the approved references; `renders/` contains Blender previews and actual Unity renders. Runtime resources are under `Assets/Resources/Models` and `Assets/Resources/Asteroids`, with the existing model and stone-map GUIDs preserved.

Run from the Unity project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python tools/asteroids/bake_concept_surfaces.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python tools/asteroids/build_concepts.py
python tools/asteroids/verify_concepts.py
```

The scripts stage output in this directory and never overwrite runtime assets. `export/` and `textures/` are ignored staging directories; rebake before regenerating models. A `-- --only boulder,slab,cratered` subset is for iteration and must not be installed as the complete library.

`mesh-report.json` records all 84 source meshes; `export-validation.json` records the checks and hashes at installation; `installed-files.json` identifies the exact resource bytes. Export validation checks closed positive-volume meshes, every key and LOD, exact source/export triangle counts, ordered material slots, UVs, normals, tangents and zero material emission. The Unity preview independently checks the actual Belt resource lookup for every model and prevents silent fallback.

## Verification and build

`dotnet build tools/check --nologo -v quiet` passed with zero warnings/errors. The compile stub now includes Unity's `Material.SetTextureScale` API. The Unity editor rendered all seven ores, barren stone, all shape families, three LODs, mining heat, a second sun angle and a sun-disabled comparison. Logs and full output: `Logs/rock-look/concepts-v4-final` and `Logs/rock-concepts-render-final.log`.

The Windows player build succeeded with zero errors (`Logs/rock-concepts-build.log`). Output: **`Builds/RockLook/BeltRunner.exe`**. Launch this build after closing the current game to see the replacement assets. The separate output preserves the user's running `Builds/Windows` executable. No smoke test or save operation was run.
