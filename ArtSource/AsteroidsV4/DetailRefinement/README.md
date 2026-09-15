# Rock surface refinement — Unity 0.9.123

The previous replacement meshes had broad smooth faces and weak medium-scale surface relief. This refinement adds a separate chipped-stone normal layer and crevice/mineral variation, shared across all 28 variants and their LODs. It keeps the approved silhouettes and embedded ore. Exposed ore receives gentler relief and remains metallic, with no intact-ore emission.

`rock-relief.blend` is an editable procedural Blender material. `tools/asteroids/bake_rock_relief.py` recreates it and bakes two 2048 × 2048 maps. Circular UV coordinates in four dimensions make the textures periodic. Multiscale noise supplies chipped surfaces and grain; interrupted fractures avoid a uniform outline around every cell. Runtime maps live in `Assets/Resources/Asteroids/rock_detail_normal.png` and `rock_detail_surface.png`. Both are linear data with mipmaps, BC7 compression, trilinear filtering and 4× anisotropy. The existing Imagegen stone albedo remains unchanged.

The shader combines the new normals with the existing surface normals. Surface-map red stores crevice occlusion, green stores mineral variation, and blue stores height for authoring. Height is not runtime displacement: silhouette detail still comes from the Blender meshes. The existing sunlight, global exposure, haze, ore colours and mining heat are preserved.

The finest mesh now enters at 10 rock radii instead of 6, and returns to the middle LOD beyond 12 instead of 8. This still uses the existing 12,000-unit proximity candidate list; it does not change physics queries, belt density or seeded generation. The effect is intended for the rocks being approached and mined. Extremely large rocks outside that candidate list continue using the batched meshes.

The two shared maps add approximately 10.7 MiB of GPU texture storage including mipmaps, and two texture samples per shaded rock fragment. Geometry assets and their triangle counts are unchanged; extending close LOD visibility can increase near-rock triangle counts and draw calls. A complete gameplay FPS benchmark was not run.

## Validation

- `dotnet build tools/check --nologo -v quiet`: zero warnings/errors.
- Unity editor renders: all seven ores, barren stone, all shape families, multiple LODs, both sun directions, sun disabled, and mining heat. Both new maps imported at 2048 with the expected linear/mipmap/compression settings.
- Matched `unity-detail-before/after` renders use the exact same camera, light, mesh and ore, changing only the added detail layer. `unity-flight-before/after` use the real seeded Belt loader and renderer at the ship's 62° cruise FOV, without entering Play Mode or booting Game.
- Seeded-belt validation loaded 54,975 rocks. The shown flight view submitted 1,903 instances in 369 visible batches, with the target promoted to its finest mesh. The actual promotion/demotion sequence at 8, 11, 13, 11 and 9 radii was **fine, fine, middle, middle, fine**.
- Windows build succeeded with zero errors. Output: `Builds/RockDetail/BeltRunner.exe`. This separate build preserves the user's running `Builds/Windows` and `Builds/RockLook` players. No smoke test or save operation was run.

Logs: `Logs/rock-detail-render-final.log`, `Logs/rock-look/detail-final`, and `Logs/rock-detail-build.log`.

For the general detail-map technique, see [Unity's secondary map documentation](https://docs.unity3d.com/Manual/StandardShaderMaterialParameterDetail.html). Validation claims above come from this project's actual Unity runs.
