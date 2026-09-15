# Unity asteroid surface repair

Based on the user's 2026-09-15 reference: dark fractured stone, sunlight reflecting from embedded metallic ore, and deep shadows. This is a material repair of the existing asteroid library, not a change to belt density or mining balance.

## What changed

- Removed the unconditional emission on intact ore. Body damage and laser spot heat retain their independent emission.
- Barren asteroids and ordinary scrap use regolith on both submeshes. Previously their vein faces still used metallic iron, tinted grey, and glowed.
- Instance colours now use the approved Blender surface palette without multiplying it by the default iron colour. Each ore gets its original roughness and metallic factors.
- Restored mipmaps on the LOD 1/2 imported library and trilinear filtering with 4x anisotropy. The actual Unity material textures had only one mip level before the repair; they now have eleven. The game uses the LOD 1 materials across all LOD meshes.
- Replaced the fine gravel regolith with larger fractured stone plates; baked new tangent normal and packed roughness/metallic maps in Blender. Stone crust partly covers the metal within the existing flush vein geometry.
- Added normal-based specular filtering to reduce unresolved glitter. Shapes, triangles, ore masks, instanced draw structure, RNG sequence, ore distribution, orbit drift and collisions are unchanged. Ore pixels now sample the three stone maps as well as the three ore maps; no extra lights or draw calls were added. Frame rate has not been benchmarked.

## Asset provenance

`imagegen-regolith.png` was created with the built-in Imagegen tool, using the user's cockpit/asteroid image as a material reference. The exact prompt is in `imagegen-prompt.txt`. The generated source is 1254 x 1254; the Unity importer currently samples its albedo at 1024 x 1024. This is not a native 4K texture.

`surface-material.blend` and `tools/asteroids/bake_surface.py` contain the Blender material and reproducible bake. The normal and roughness/metallic outputs are 2048 x 2048, with restrained height-derived relief. These are derived material maps, not scanned depth. Roughness is G and metallic is B (zero for stone). The imported ore maps remain from the approved asteroid library.

Runtime maps: `Assets/Resources/Asteroids/regolith_albedo.png`, `regolith_normal.png`, `regolith_metalrough.png`, with committed texture import settings. Albedo is sRGB; the two data maps are linear. Normal maps are stored as RGB and decoded explicitly by the rock shader. Textures use mipmaps, repeat, trilinear filtering, 4x anisotropy and BC7 in the Windows build.

Rebuild the material maps from the Unity project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python tools/asteroids/bake_surface.py
```

## Verification

`dotnet build tools/check --nologo -v quiet`: passed, zero warnings/errors.

The Unity editor rendered the actual instanced rock shader with the runtime material conversion. Inspected all seven ore colours plus barren stone, eight shape families, three LODs, a sun-disabled view, and damaged-rock heat. The rendering utility never enters Play Mode, boots Game, or accesses saves; it restores its temporary quality settings. Screenshots and material diagnostics are under `Logs/rock-look/final`. These are isolated material renders, not screenshots of live gameplay. Global lighting was being updated independently by Claude during this task, so the first before image is not a controlled whole-scene performance comparison.

The Windows player build succeeded with zero errors; output is `Builds/RockLook/BeltRunner.exe`. A separate output folder avoids overwriting the user's running `Builds/Windows` player. The build includes the source available when it compiled; subsequent concurrent edits need a new build. No smoke tests or save operations were run.

Render the material contact sheet (batch editor only; close other Unity editor instances first):

```powershell
& 'C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe' -batchmode -projectPath $PWD -executeMethod RockLookPreview.Render -rockPreview current -logFile Logs/rock-render.log
```

Build to the separate review directory:

```powershell
& 'C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath $PWD -executeMethod RockLookPreview.BuildReviewPlayer -logFile Logs/rock-player-build.log
```
