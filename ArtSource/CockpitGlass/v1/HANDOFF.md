# Cockpit glass add-on for v2

Load `cockpit_glass.glb` alongside the existing `cockpit_interior.glb`. The v2 interior is unchanged. Use `cockpit_glass_manifest.json` as the authority for pane bounds, planes, UV projection and the review placements.

## Import contract

- Root: `cockpit_glass`, at the ship origin, identity position and rotation, authored scale 1. Apply the same `Data.SHIP_SCALE` (currently 3) as the cockpit interior, exactly once.
- Use the normal glTFast import path. No extra half-turn, mirror, axis correction or eye offset.
- Unscaled Unity eye: `(0, 4.5, 5)`; +X right, +Y up, +Z forward.
- Mesh nodes: `canopy_glass_front`, `canopy_glass_left`, `canopy_glass_right`. Each is one flat, single-sided surface facing the pilot. The side panes run inboard of the existing mullions and roof ribs.
- Material: `Canopy_glass_clear`, alpha BLEND, near-white base color, alpha 0.06, metallic 0, roughness 0.05, no emission or transmission extension. Replace this material with the game's decal material.
- GLB contains only the root and the three pane nodes: 3 draw calls, 6,144 triangles, 3,267 vertices. No textures are needed by the plain glass GLB. Decal PNGs are separate runtime inputs.

## UVs and placing marks

The manifest's UVs use **U right and V up**, as seen by the pilot facing each pane. Front uses ship forward; left and right use a view turned 90 degrees toward that side. Each pane has one continuous UV island. Perspective projection from the eye keeps marks visually stable as their hit positions change.

The raw GLB stores V downward, as glTF specifies. The project's glTFast importer flips it back. **Unity mesh UVs already have V up; do not flip them again.** This was checked against the installed package's `ConvertUVsFloatToFloatInterleavedJob` and the exported vertex data.

Intersect the eye-to-shooter ray with the pane's triangles and interpolate UVs, or use the manifest's projection formula after confirming that the intersection lies inside the pane. Its bounds are an AABB, not an exact polygon: use the pane corners or triangle mesh for the containment check. The four corner UVs outline a trapezoid within 0..1; some corners of the UV square lie outside the physical pane.

The 32 by 32 subdivisions keep interpolated UVs within 0.125 texel of the analytic projection at 1024 pixels. All triangles are coplanar; these subdivisions are for projection accuracy, not curvature.

The manifest records exact UV centers and sizes for the Blender review marks. To make a projected square, use `heightUV = widthUV * projectedSpan.x / projectedSpan.y`. Every PNG's impact coordinate is `(0.5, 0.5)`; the light-wear scatter uses that as its placement anchor.

## Decals

| File | Intended use |
| --- | --- |
| `textures/crack_light.png` | Sparse surface wear: short scratches and pits |
| `textures/crack_web.png` | Branching impact web with a faint center |
| `textures/crack_shatter.png` | Dense severe fracture with transparent chip holes |
| `textures/scorch.png` | Black soot, with shape and density stored in alpha |

All are 1024 × 1024 RGBA PNGs with straight alpha and grayscale RGB. Scorch RGB is exactly black. The outer 112 pixels on every side are fully transparent, exceeding the required 10% guard band. Use Clamp wrapping. Preserve the alpha channel when importing. Do not use a JPEG or opaque conversion.

The art was generated with built-in imagegen. `source/imagegen-prompts.json` records the selected prompt set; `source/generated-originals/` preserves the source artwork. Exporting resamples the artwork to 800 × 800 within the 1024 canvas, normalizes grayscale RGB, and preserves generated alpha. The scorch exporter writes pure black RGB as specified.

## Review and validation

- `preview-eye-72.png`: original v2 lens, straight ahead; crack web left of center and scorch right of center.
- `preview-eye-52.png`: current game lens, looking 4 degrees down; identical physical marks.
- `preview-left.png` / `preview-right.png`: freelook from the same eye, showing light wear and severe damage on the side panes.
- `cockpit_glass_review.blend`: editable inspection scene including the unchanged v2 geometry as context. Cameras, lights and the `PREVIEW_ONLY_glass_damage` collection are review-only and excluded from the GLB. Do not export the entire review scene into the game.
- Neutral blue review background keeps dark soot visible. These are Blender previews, not screenshots of an integrated Unity build.
- `geometry-validation.json`: normals, near distance, clearances and zero triangle intersections with v2.
- `coverage-diagnostic.json` and `aperture-coverage.svg`: 68,000 sampled eye rays; all 31,900 unobstructed window rays cross a pane. Teal means a covered opening; dark means the interior blocks the ray.
- `export-validation.json`: exported GLB transforms, bounds, winding-related normal direction, UV topology and glTFast conversion checks.
- `blender-import-validation.json`: successful fresh GLB import, node names, UV layers, and verified vertical review FOV.
- `gltf-validation.json`: Khronos validation, zero errors and zero warnings. Three informational messages note that the plain glass material does not yet use its UV channel; those UVs are deliberately supplied for runtime decals.
- `texture-validation.json`: PNG size, channels, alpha margin, grayscale and chip-hole inspection.
- `v2-preservation.json`: source v2 SHA256 hashes checked before and after the work.

No gameplay code, tier logic, hit flash, repair wipe or Unity integration is included. The package supplies the panes and marks for that work.

## Message to Claude

> Integrate this cockpit v2 glass add-on using `cockpit_glass_manifest.json` and the notes above. Load `cockpit_glass.glb` beside `cockpit_interior` with the same ship-origin transform and scale. Keep v2 unchanged. The three `canopy_glass_*` meshes have pilot-facing UVs; glTFast already restores V-up, so do not flip V again. Use the four PNGs in `textures/` for the hit placement, hull tiers, flash and repair wipe described in `ArtSource/CockpitGlass/BRIEF.md`. The 72-degree preview's exact comparison placements are in `preview_placements`; the 52-degree preview uses the current 4-degree-down camera.
