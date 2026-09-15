# Ferron orbital detail â€” Unity 0.9.125

Rebuilds the dry planet in Blender to address the smooth, low-detail appearance at gameplay distance. The previous material's broad cellular plateaus are replaced by an orbital geology map with distinct branching rifts, folded mountain ranges, impact structures and dark volcanic plains. Relief follows that artwork rather than an unrelated large-scale noise pattern.

## Assets and reproduction

- `dry-planet-detail.blend`: actual Blender mesh, material setup, packed Imagegen source, camera and sunlight. Baked material textures reference the versioned maps under `Assets/Resources/Planets/Dry` using relative paths.
- `orbital-geology.png`: Imagegen surface artwork, native 1774 Ã— 887. Exact prompt and reference recorded in `imagegen-prompt.json`.
- `tools/planets/build_dry_planet_detail.py`: creates the mesh, generates polar cap mapping, blends the longitude seam, bakes albedo/normal/surface maps and exports `ferron.glb` using Blender.
- `asset-report.json`: geometry and bake dimensions measured by Blender.

The runtime model remains 130,562 vertices / 261,120 triangles. Its terrain is displaced inside the existing unit-radius collision envelope. The three maps remain 4096 Ã— 2048 with mipmaps and BC7 compression, approximately 32 MiB total GPU texture storage. The new orbital features improve readability without increasing the previous mesh or texture budget. The 4K bakes combine the native artwork with procedural mineral variation; the Imagegen source is not native 4K. The albedo is artistic terrain imagery and may retain some illustrated relief; the normal map, directional illumination and day/night shading are evaluated at runtime.

Run `Blender.exe --background --python tools/planets/build_dry_planet_detail.py` from the Unity repository. Output is staged under this source folder; the script never overwrites runtime resources. After reviewing and installing `ferron.glb` and the three maps, run `DryPlanetPreview.Render` and `DryPlanetPreview.BuildPlayer` in batch Unity.

Unity also blends projected albedo and reduces tangent-space normal relief at the UV poles, preventing the longitude singularity from drawing radial streaks.

## Verification

Actual Unity editor renders use the runtime model, material, sunlight and `Post.OnRenderImage` processing. Nine views cover orbital and close detail, both hemispheres, night, terminator, seam, pole and two distances. The belt-distance view uses the gameplay 62Â° FOV and the carrier orbit / planet radius ratio (roughly 420 pixels across at a 1000-pixel image height); the farther view shows the planet around 210 pixels across. This checks detail at the scale requested, not only in a close-up.

Before images preserve the old planet at the same camera settings. Claude adjusted and committed the sky while this work was underway, so the comparison backgrounds differ; his final sky is preserved. No global lighting, gameplay placement, collision rules or Hub homeworld assets are modified.

Unity verifies all mesh normals/tangents/UVs, the collision envelope and 4K texture imports. Compile and Windows build results are in `installation.json`. The only playable Windows build is `Builds/Windows/BeltRunner.exe`; both art build helpers now call the standard builder. No smoke test, Play Mode session, save operation or performance benchmark is run.
