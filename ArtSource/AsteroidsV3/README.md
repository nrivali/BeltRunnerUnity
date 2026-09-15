# Detailed Blender asteroid library for Unity

Remade for the user's 2026-09-15 reference: rough, fractured rock with reflective ore embedded in the body. These are new meshes, not just subdivided copies of the previous low-poly models.

## Geometry and materials

The library preserves the game's 14 shape families and two variations per family: lumpy, chunk, potato, shard, pancake, cratered, cluster, slab, spindle, bean, boulder, jagged, wedge and hollow. Every variant has three LODs, for 84 meshes. Displaced fracture plates, chipped ridges, layered ledges and craters create actual geometry and shadowing. Hollow variants have a carved pocket or through-cavity, with remeshed closed topology.

Ore follows broad, irregular surface regions in the same closed mesh. There are no separate protruding ore nodes. Both material slots, all node keys, the origin and glTF axis convention match the existing Unity loader. Shapes retain their family proportions, but the surface and silhouette have changed. Gameplay colliders remain the existing approximate spheres; they do not trace every new fracture or cavity.

The existing Imagegen stone albedo and Blender-baked normal/roughness maps are retained from `ArtSource/Asteroids`, along with the approved ore PBR textures and runtime ore finishes. The GLBs embed those textures; Unity also loads the dedicated compressed stone maps from `Resources/Asteroids`. No new image generation was needed for this geometry rebuild.

| Level | Triangles per variant | Total across 28 variants | Previous total |
| --- | ---: | ---: | ---: |
| Close, LOD 0 | 46,324–50,402 | 1,365,858 | 248,304 |
| Middle, LOD 1 | 2,308–2,606 | 69,322 | 69,370 |
| Far, LOD 2 | 560–652 | 16,854 | 17,390 |

Only nearby rocks receive LOD 0 through the existing distance selection. Middle and far geometry budgets remain close to the previous library. Mesh memory and close-range triangle cost increase; runtime frame rate has not been benchmarked. Belt density, RNG sequence, ore distribution, orbital drift, collisions and instanced draw structure were not changed by this update.

## Files and rebuilding

- `asteroids.blend`: all 84 editable meshes, packed textures, sun/fill lighting and a close-up camera. Lower LODs are hidden in the viewport by default; the high-detail variants are arranged as a gallery.
- `mesh-report.json`: geometry counts, proportions, ore coverage and topology checks for every source mesh.
- `export-validation.json`: SHA-256 hashes, GLB counts and compatibility checks at integration time.
- `renders/geometry.png`: Blender render.
- `renders/unity-copper.png`, `renders/unity-ores.png`: actual Unity editor renders using the runtime instanced shader and material conversion. They are isolated asset previews, not gameplay screenshots.
- Runtime models: `Assets/Resources/Models/asteroids_lod0.glb`, `asteroids_lod1.glb`, `asteroids_lod2.glb`. Existing import metadata and GUIDs are preserved.
- Generator: `tools/asteroids/build_models.py` (Blender 5.2.1 LTS).

From the Unity project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python tools/asteroids/build_models.py
```

This stages new GLBs under `export/`; it does not overwrite the live models. `export/` is ignored to avoid checking in a second copy of the runtime assets. `-- --only lumpy,boulder` makes a subset for iteration; never integrate a subset as the full library. The generator uses the current runtime GLB as its material source and the dedicated stone maps as overrides. Seeds control the family variations; belt generation itself is unchanged.

## Validation

All 84 saved Blender meshes pass mesh validation, have closed manifold topology and positive volume. GLB checks confirm every expected node, two material slots in the correct order, UVs, normals, tangents, exact source triangle counts and centered family envelopes. Unity's actual `Belt` resource lookup loads all 84 meshes, including the detailed near models, without falling back to procedural geometry.

The batch editor rendered all seven ore finishes, barren stone, all 14 shape families, three LODs, a sun-disabled view and mining heat. Full renders and import diagnostics are under `Logs/rock-look/geometry-v3`. The preview does not enter Play Mode, boot Game or access saves.

`dotnet build tools/check --nologo -v quiet` passed with no warnings or errors. The Windows player build for `0.9.121-unity` succeeded with zero errors, recorded in `Logs/rock-geometry-build-final.log`; output is `Builds/RockLook/BeltRunner.exe`, separate from the user's running `Builds/Windows` game. No smoke test was run.
