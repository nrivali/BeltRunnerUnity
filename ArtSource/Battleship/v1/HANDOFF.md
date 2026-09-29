# Belt Runner enemy battleship

This is the enemy hull requested by `ArtSource/Battleship/BRIEF.md` dated 2026-09-22. It has a tapered armored hull, four aimable deck turrets, two raised generator domes, aft hangar control, a forward bridge/core, a recessed port fighter bay, and three exhausts. The main hull retains the requested 7,400-unit length and fits the 1,800-by-1,080-unit nominal collision envelope; its tapered pressure hull is slightly shallower. Armor, lights, engines and deck fittings extend beyond that base envelope. The manifest records the actual model bounds separately from the unchanged collision dimensions.

`enemy_battleship.glb` is the runtime asset. `enemy_battleship.manifest.json` is its positioning and naming contract. The package also includes editable Blender source, a standalone builder, geometry-validation reports, and three preview renders. The renders use Blender lighting and AgX; they are not game captures.

## Import exactly like the carrier

- Asset root: `enemy_battleship`, with identity transform and world-scale geometry.
- Runtime local position: zero. Runtime scale: **one**. Do not use the player's ship scale of three.
- Keep the carrier's existing import convention: glTFast mirrors X, then the game applies its existing 180-degree turn about Y. Add no other flip or turn.
- Final gameplay frame: +X nose, +Y up, +Z port. All manifest positions are in that final frame, relative to the hull center.
- In the raw glTF file, positions are `(game X, game Y, -game Z)`. This places the asymmetric bay on gameplay port after the same transform chain already used by `cargo_carrier`.
- Position-only anchors have identity rotations in the file. Use their positions for hit centers. `bay` is oriented so its imported +Z points outward. The turret heads have their outward/upward rest pose and local geometry prepared for Unity's normal +Z-forward `LookRotation`.

The asset is self-contained glTF 2.0 with embedded metallic-roughness and normal maps. It uses opaque materials only. There is no glass, no shield geometry, no exported camera, and no exported light. `KHR_materials_emissive_strength` is used for the fixtures. There is no Draco or meshopt dependency.

The intended runtime locations are:

```text
Assets/StreamingAssets/enemy_battleship.glb
Assets/StreamingAssets/enemy_battleship.manifest.json
```

The current primitive `BuildEnemyHull` / `FitSubsystems` implementation still needs Claude's loader integration. Bind the imported renderers and pivots instead of creating a duplicate primitive subsystem set.

## Final placement

Use the manifest values in preference to the placeholder coordinates. Health, attack order, hit radii, bubble radii and firing muzzle offset remain as specified in the brief. Parts were raised for bubble clearance and the turret mounts moved outward for traverse clearance.

| Anchor | Final position, gameplay units | Notes |
| --- | --- | --- |
| `turret_1` | `(2300, 620, 600)` | Fore port pad |
| `turret_2` | `(2300, 620, -600)` | Fore starboard pad |
| `turret_3` | `(-1900, 620, 600)` | Aft port pad |
| `turret_4` | `(-1900, 620, -600)` | Aft starboard pad |
| Each turret head | Pad + `(0, 230, 0)` | This offset changed from 190 |
| `generator_1` | `(200, 1040, 450)` | Bubble radius 350 |
| `generator_2` | `(200, 1040, -450)` | Bubble radius 350 |
| `hangar_control` | `(-1100, 1100, 0)` | Bubble radius 410 |
| `core` | `(3000, 1210, 0)` | Bubble radius 470 |
| `bay` | `(0, -110, 1180)` | Fighter spawn; exit +Z |
| `bay_mouth` | `(0, -110, 963)` | Clear opening approximately 1490 × 480 |
| `collide_1`, `collide_2`, `collide_3` | `(-2400,0,0)`, `(0,0,0)`, `(2400,0,0)` | Extras `r = 1000` |

The covered enclosures sit on flared mounting stems. Those stems belong to the damageable body mesh. The spheres clear the separate static hull and deck; they naturally cross their own mounting stems. The validation report records the closest static-hull distance for each sphere. No shield meshes are included in the asset.

## Turret hierarchy and aiming

For each N = 1…4:

```text
turret_N                     position-only empty at the pad
  turret_N_pad               static foundation mesh
  turret_N_head              rotating mesh; origin at the head center
    turret_N_barrel          independent mesh, hide on death
    turret_N_lamp            independent eye mesh and exclusive material
    turret_N_muzzle          empty at imported head-local (0, 0, 410)
```

The barrel is authored along the imported head node's **local +Z**, with +Y up. Keep the current `head.rotation = Quaternion.LookRotation(aim, hullUp)` behavior. Do not apply an additional corrective head rotation. The barrel tip and muzzle empty are 410 units forward of the head pivot, matching the current firing offset. Update the old hard-coded head height to 230 or read the head node/manifest directly.

The barrel is a separate child so death can hide it while leaving the wrecked turret head. The head, pad and barrel each have their own material. The head mesh itself contains all of its non-emissive housing details; replacing its material darkens the complete housing.

## Bodies, lamps and death handling

All names in brief section 5 are present. `hull` is the primary hull mesh and parents all static armor, deck, bay structure, nozzles, antennae and decorative fittings. The eight subsystem anchors are siblings of `hull` under `enemy_battleship`.

| Subsystem | Damageable body | Switchable emission |
| --- | --- | --- |
| Turret N | `turret_N_head` | `turret_N_lamp` |
| Generator N | `generator_N_body` | `generator_N_lamp` |
| Hangar control | `hangar_body` | `hangar_lamp` |
| Core | `core_body` | `core_lamp` |
| Fighter mouth | Static bay structure | `bay_lamp` |
| Engines | Static nozzle structure | `engine_glow_0` … `engine_glow_2` |

Each listed lamp/window/glow is a separate mesh with an exclusive material. Each damageable body uses one material slot. Bridge windows are opaque emissive surfaces grouped in `core_lamp`, so the complete strip switches off together. Generator status insets and its crown are similarly grouped by generator, without sharing a material between generators.

Additional hull fixtures are `hull_lamp_01` … `hull_lamp_08` and `bow_lamp`, each with a separate material. **Use `switchable_emission` in the manifest to find every emitter that must go dark when the battleship dies.** Each entry gives its mesh, material and death owner. `death_behavior` provides the per-subsystem mapping. The bay glow can also be disabled when hangar control dies and launches stop.

Changing a lamp to the game's dark material or disabling its MeshRenderer both work. Preserve the surrounding non-emissive body geometry. Every required body/lamp node has its actual final position recorded under `nodes` in the manifest.

## Validation and remaining integration

The supplied checks cover:

- glTF validity and absence of external files or compression decoders.
- All named nodes, node positions, opaque materials and exclusive lamp ownership.
- Four head pivots and their child hierarchies, with six simulated aim poses each.
- Local +Z firing, the 410-unit muzzle offset, and turret sweep clearance from the covered-part spheres.
- Shield-sphere clearance from all static hull geometry, including deck and trim.
- Nine clear rays through the fighter launch throat, with `bay` pointing to gameplay port.
- Clear centerline views of all three engine glows through the exhaust mouths.

See `geometry-validation.json` and `gltf-validation.json` for the final results. This is an asset handoff; no game code was changed and no Unity runtime result is claimed. Claude can use the brief's `-battle` check after wiring the loader and updating the subsystem positions. No game process was closed or replaced while making this asset.

## Rebuild the asset

```text
blender --background --python-exit-code 1 --python source/build_enemy_battleship.py -- --out /absolute/path/to/new-output
blender --background --python-exit-code 1 --python source/validate_enemy_battleship.py -- /absolute/path/to/new-output/enemy_battleship.glb
```

The builder is standalone and recreates its geometry and numerical material maps. `--draft` produces smaller previews; `--skip-render` writes the model, manifest and editable Blender source without rendering. Choose a new output directory to retain an earlier revision. Preview lights and cameras are created after exporting the runtime GLB.
