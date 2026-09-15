# Large boulder prototype

One Blender-authored asteroid design, integrated as **boulder_A**, one of the game's 28 shape/variant entries. All existing sizes assigned that entry use the prototype; the other 27 entries retain their original assets and shader. Seeded positions, ore amounts, collision radii, mining, orbit rails and LOD thresholds are unchanged.

## Source and runtime assets

- `large-boulder.blend`: editable 1,310,720-triangle source sculpt, packed textures, and three optimized meshes. The high sculpt has actual recessed shear joints, impact scallops, chipped ledges and fine surface relief. Ore is embedded in the surface, with no additional nodes or floating geometry.
- `boulder_prototype.glb`: 60,000 / 3,000 / 720 triangles, one material slot per LOD. All three are closed manifolds with outward volume. Original boulder_A budgets were 42,220 / 2,508 / 646 triangles with two material draws.
- `boulder_albedo.png`, `boulder_stone.png`, `boulder_normal.png`, `boulder_surface.png`: four **4096 x 4096 Blender bakes** on dedicated UV charts. Normal is tangent-space RGB; surface stores cavity occlusion in R, roughness in G, embedded ore mask in B. The stone-only map lets barren instances remove the metal without leaving pale deposits.
- The fine material layer reuses the project's existing ImageGen regolith image, projected in Blender and baked onto the sculpt, together with procedural mineral variation and small-scale bump. These are 4K authored bakes, not a claim that the original ImageGen photograph was native 4K.
- `Assets/Resources/Shaders/SculptRock.shader`: dedicated specular workflow with low basalt reflectance, the existing ore palette, sunlight/sky reflections, the same orbit motion and mining heat, and a small shadow-caster inset for steep cuts. Intact ore has no emission. The existing shared Rock shader is unchanged.
- `Assets/Resources/Materials/LargeBoulder.mat` pins the runtime shader and instancing variant in the player. Imported maps use BC7, mipmaps, trilinear filtering and 8x anisotropy.

## Review and performance

`previews/` contains actual Unity editor renders through the current Kessler lighting and runtime Post composite. Camera FOV is the game's 62 degrees; the example has a 600-unit radius (300 readout metres). Mining view is 1,600 units from the centre (nominal 500 m surface clearance), close view 1,000 units (nominal 200 m clearance). Irregular geometry changes the exact surface clearance. Before and after use the same camera, sun, post-processing and ore.

Final RTX 5070 Ti comparison, 1600 x 1000 with 4x MSAA:

| Scene | Original | Prototype |
| --- | ---: | ---: |
| One close LOD asteroid | 1.34 ms | 1.35 ms |
| 24 close LOD asteroids | 2.66 ms | 2.77 ms |

These are median synchronized **editor render + post + GPU readback wall times**, with warm-up and interleaved samples. They are not full-game FPS or isolated GPU timestamps. The normal game was closed for this measurement. The prototype submits one material draw instead of two. Unity reported approximately 42.7 MiB native allocation per imported 4K texture in the editor; this includes editor/native overhead and is not a dedicated-VRAM measurement. Maps are shared across all instances. Do not extrapolate a separate four-map set to every variant without reviewing texture memory and shared detail materials.

## Rebuild

From the Unity repository:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background -t 12 --python tools/asteroids/build_large_prototype.py
```

The generator stages files here; promote the GLB and four PNGs into their existing Resources locations after reviewing them. Use `LargeRockPreview.Render` in an isolated batch editor for validation; it never enters Play Mode, boots Game, or accesses player saves. `LargeRockPreview.ValidateAndBuild` additionally invokes `Build.Player`, writing only `Builds/Windows/BeltRunner.exe`. Close the game before that step. Do not run smoke tests unless the user asks.

Final validation: dotnet build tools/check passed with zero warnings/errors. The canonical Windows player build succeeded with zero errors on 2026-09-15 (716 MB). Thirteen isolated Unity views passed; no smoke test or save access was used.
