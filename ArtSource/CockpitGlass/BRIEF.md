# Belt Runner · cockpit glass and window damage — a brief for Astra

Written 2026-09-23 from the code on the `combat-rework` branch (`Assets/Scripts/Ship.cs`, the cockpit view). The ask is
an add-on to your cockpit v2: a glass pane in the window aperture, and a set of damage decals the game will lay on it as
the hull takes hits. `reference-ingame.png` beside this file is the cockpit view as the game draws it today (the camera
now looks 4° down, at a 52° lens); your v2 previews show the aperture itself.

## 1. What it is and how the game will use it

When the ship takes a hit in the cockpit view, the glass should show it: a crack web where the shot came in, scuffs and
scorches as the hull wears down, a shattered starburst when the hull is nearly gone, all wiped clean when the hull is
repaired at the carrier. The game does the placing and the timing; you supply the surface and the marks.

- The game knows where each hit came from (the direction to the shooter), so it will put a decal where the line from the
  pilot's eye toward the shooter crosses the pane, sized by the hit.
- The marks build in tiers with the hull: above 70% only scuffs; 40 to 70% crack webs at the hit points; below 40% the
  shatter starburst over the worst of them, with scorch smears. Every hit flashes its mark for a moment as it lands.
- The pane and its marks live on the cockpit layer, drawn only in the cockpit view, never from the chase camera.
- Repair at the carrier (the hull back to full) clears every mark.

## 2. Frame, units, axes, scale

Exactly your cockpit v2 conventions, so the add-on drops in beside the interior:

- Root name `cockpit_glass`, attached at the ship origin with zero position, identity rotation and the same root scale as
  `cockpit_interior` (`Data.SHIP_SCALE`, currently 3). Author it in the v2 `.blend` frame so the pane sits in the v2
  aperture without any offset of mine.
- The pilot's eye is at `(0, 4.5, 5)` unscaled, as in v2; forward is +Z and up +Y after the glTFast conversion.
- The camera's near plane is 2 world units, 2/3 of an unscaled unit: keep every part of the pane further from the eye
  than that (the aperture already is).
- Deliver a self-contained GLB (packed textures, no Draco or meshopt), a manifest JSON as before, and previews from the
  eye with the marks applied in Blender so I can see the intended look. Your manifest wins over my numbers.

## 3. The pane

- One mesh node per window aperture: `canopy_glass_front`, and `canopy_glass_left` / `canopy_glass_right` if the side
  windows are separate openings in v2. Each fills its aperture edge to edge, set a little inside the frame's outer face
  so it never intersects the struts, and does not touch the deck.
- Single-sided, facing the pilot; no thickness needed. A gentle curve is fine if v2's aperture is curved; flat is fine
  too.
- UVs 0 to 1 across each pane as a flat projection from the pilot's view: U to the pilot's right, V up. State the
  mapping in the manifest. I place decals by UV, so the projection must be clean, with no overlap and no seams inside
  a pane.
- Material: a plain transparent glass, base colour near white, alpha about 0.06, metallic 0, roughness about 0.05, no
  emission, no refraction extension (the game's pipeline is Unity's built-in one; keep it to what KHR core plus
  KHR_materials_transmission-free glTF gives). I will swap the material at run time for one that carries the decals.
- Leave `cockpit_interior.glb` untouched: v2 stays as delivered, with every name preserved; the glass is a second GLB
  loaded beside it.

## 4. The marks (decal textures)

PNG with alpha, 1024 × 1024, each centred on its impact point at the texture's centre, the outer 10% fully transparent
so an edge never shows. Greyscale in the colour channels (white where the mark catches light, black where it darkens),
so I can tint and light them in the game:

| File | What | Notes |
|---|---|---|
| `crack_light.png` | Scuffs and fine scratches | A scatter of short scratches and pits, sparse; reads at a glance as wear, not damage |
| `crack_web.png` | A crack web from a hit | Radial cracks with branches and a few concentric rings from the centre, the lines bright and thin, a faint milky halo close to the centre |
| `crack_shatter.png` | The heavy starburst | A dense spider web from the centre with small chips missing (fully transparent holes with bright edges), the outer cracks reaching to about 80% of the texture |
| `scorch.png` | A scorch smear | A soft blackened smudge, darker at the centre, alpha only (black in the colour channels); this one is dark, not bright |

If you want to go further, an optional `glass_grime.png` at 2048 × 1024, tileable across a pane, a very faint dust and
streak layer for the pane at rest; and an optional second variant of `crack_web.png` so two hits never look identical.

## 5. What is not needed

No gameplay logic, lights, cameras or text in the GLB; no HUD; no changes to the screens or the button row. No damaged
variants of the interior mesh: the decals carry the damage.

## 6. Checks that will make integration quick

- The manifest lists each pane node, its bounds in the v2 frame, its UV mapping and its material name.
- A preview from the eye at v2's lens with `crack_web.png` placed left of centre and `scorch.png` right of centre, so I
  can compare the game's placement against yours.
- The same geometry and glTF validation reports as the cockpit package.
