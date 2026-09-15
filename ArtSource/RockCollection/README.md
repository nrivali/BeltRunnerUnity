# Sculpted rock collection — 0.9.127

Five additional designs extend the approved boulder_A prototype. All geometry is authored
in Blender 5.2.1 LTS; the image-generated regolith from AsteroidsV4 is projected on the sculpt
and baked with its actual fractures. Ore is an embedded surface mask, never raised nodes.

| Runtime variant | Silhouette | Ore layout |
| --- | --- | --- |
| boulder_B | Broad broken block, four deep shear joints | Branching mineral network |
| cratered_A | Rounded impact body, nine eroded cavities | Irregular concentrated pockets |
| potato_A | Long weathered body, broken ledges | Broad warped ribbons |
| slab_A | Thin layered slab, chipped terraces | Interrupted mineral strata |
| shard_A | Tapered angular splinter | Separated mineral plates |

The original boulder_A remains installed: six of the 28 visual entries now use the detailed
sculpt workflow. Visual substitutions preserve shape keys, seeded layout, ore assignment,
collision radii, orbital movement, batching and the existing LOD selection distances.

Each source sculpt has 1,310,720 triangles. Runtime GLBs contain 60,000 / 3,000 / 720 triangles
and one material per LOD. Lower LODs inherit the same padded, unique six-chart UV atlas.
Every mesh is manifold and has positive volume; exact counts are in the per-design JSON files.

Three 4096² maps per design:

- Stone albedo: sRGB, also used for barren instances.
- RGB tangent-space normal: linear, with sculpt relief and fine regolith bump baked together.
- Packed surface: linear R cavity occlusion, G roughness, B ore coverage.

All use BC7, mipmaps, trilinear filtering, anisotropy 8 and clamped UVs. The shader reconstructs
neutral metal reflectance from the same grain encoded in roughness (G=.63+.29*grain;
linear metal=.40+.39*grain), saving a fourth 4K texture. Textures are shared across all seven
ore colors and instances, not duplicated for each ore. Fifteen BC7 4K mip chains add about
320 MiB of GPU texture storage; this is a deliberate quality/memory tradeoff.

`SculptRock.shader` keeps the explicit low stone reflectance of the approved prototype.
`RockSunlight.cginc` uses Unity's direct PBR BRDF and shadow attenuation for the directional
sun, omits environment-probe specular, and allows only diffuse response to local point/spot
lights. Both legacy and sculpted rocks use this lighting. Ambient readability and intentional
mining heat/laser spots remain. Claude's 125% rock brightness and other lighting/planet changes
are preserved; the same stone brightness is now applied to the sculpt material too.

The .blend files contain the editable high sculpt, hidden runtime LODs and packed original
regolith. Baked maps are linked by relative paths to Assets/Resources/Asteroids/SculptCollection
so source files remain below GitHub's per-file limit. Keep the repository folder structure.

## Reproduce

From the Unity repository, for each key in the table:

    "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -t 12 --python tools/asteroids/build_rock_collection.py -- boulder_B

Import maps/materials, render the actual runtime shaders and verify the integration:

    Unity.exe -batchmode -projectPath <this repo> -executeMethod RockCollectionPreview.Render -logFile Logs/rock-collection-render.log

The editor art test does not enter Play Mode, run Game.Boot, touch saves or launch a smoke
run. It checks all LODs/atlases/maps and compares sunlight, a shadow-only occluder, sun-off,
an adversarial bright reflection probe, point light and mining heat on new, prototype and
legacy rocks. Outputs are under Logs/rock-collection. Reviewed images and the measured
validation report accompany this source directory.

Build only the standard Builds/Windows/BeltRunner.exe when the player is closed.
