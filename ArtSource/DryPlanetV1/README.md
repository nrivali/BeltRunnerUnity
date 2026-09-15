# Dry Ferron — Unity 0.9.124

Replaces Ferron in the Kessler mining zone with the approved dry-world concept: weathered ochre plateaus, dark basalt basins, branching canyons, individual impact structures and a thin amber atmospheric shell. There are no oceans, lakes, ice, water clouds or emissive terrain. The existing game belt supplies the surrounding rocks; the planet model does not add a separate decorative ring or move any gameplay rocks. Meridian in the Hub retains its ocean-world asset.

## Source and materials

- `approved-concept.png`: the approved Imagegen dry-planet concept.
- `imagegen-minerals.png`: a native 1254 × 1254 mineral colour tile generated with the built-in Imagegen tool. Its exact prompt is in `imagegen-prompts.json`.
- `dry-planet.blend`: the final editable model, packed maps, the procedural geology material, an export material, and a lit Blender preview scene. `Dry planet geology source` remains in the material datablocks for editing.
- `tools/planets/build_dry_planet.py`: regenerates the model and bakes maps in this directory, without touching runtime resources.
- `asset-report.json`: mesh counts, crater profiles, texture sizes and terrain bounds.

Blender generates the global terrain from spherical coordinates, with warped canyon networks, plateau masks, mineral detail and 29 separately placed crater profiles. It bakes **4096 × 2048** albedo, tangent-space RGB normals and surface data (R occlusion, G roughness, B height). These maps contain procedural detail baked at that resolution; the Imagegen tile itself is not a native 4K image. Terrain displacement follows the same baked height field and remains inside the unchanged unit-radius collider. Normal-map relief supplies detail smaller than the geometry can resolve.

The model has 130,562 vertices and 261,120 triangles. Unity's full-resolution maps are explicitly referenced by `Materials/DryPlanet`; the GLB holds compact 1024 × 512 fallbacks. Albedo is sRGB, data maps are linear, and all three use mipmaps, BC7, trilinear filtering and 8× anisotropy. They require approximately 32 MiB of GPU texture storage including mipmaps. No gameplay FPS benchmark was run.

The planet shader is nonmetallic and rough, with actual directional lighting. It converts the sampled pigment to reflectance in the project's Gamma rendering mode so the existing HDR/ACES composite preserves the brown terrain instead of washing it out. This change does not modify global lighting or exposure. The final Unity previews and player build include Claude's colour-grade commit `9747a79`. The shared runtime `DryPlanet` helper applies the material and creates a smooth amber shell at 1.015 radii. The existing atmospheric shader remains in use; this is a lightweight visual atmosphere, not a full physical atmospheric simulation.

## Verification

Blender verified a closed, outward-facing mesh. The Unity editor loaded the exact runtime GLB and material, checked every terrain vertex against the collision envelope, verified UVs/normals/tangents and all full-resolution texture imports, and rendered orbital, close terrain, opposite hemisphere, terminator and night views. Previews in this directory are actual Unity renders; the concept is labelled separately.

`dotnet build tools/check --nologo -v quiet` passed. The compile stub now includes Unity's `Mesh.normals` property. The Windows player build succeeded with zero errors at `Builds/DryPlanet/BeltRunner.exe` so existing player folders are preserved. No smoke test, Play Mode session or player save operation is performed by the art preview.

Validation logs: `Logs/dry-planet-unity.log`, `Logs/dry-planet-preview/validation.txt`, `Logs/dry-planet-build.log`. Installation hashes and the preserved Hub/model-GUID checks are recorded in `installation.json`.

## Rebuild

Run Blender with `--background --python tools/planets/build_dry_planet.py`. Generated `textures/`, `ferron.glb` and `geology-source.blend` are staging output and ignored here; the final runtime resources are versioned under `Assets/Resources`. Copy validated output into those runtime paths, then run Unity's `DryPlanetPreview.Render` and `DryPlanetPreview.BuildPlayer` batch methods. The preview never boots `Game` or accesses saves.
