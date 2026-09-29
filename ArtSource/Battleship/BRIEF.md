# Belt Runner · the battleship — a brief for Astra

Written 2026-09-22 from the code on the `combat-rework` branch (`Assets/Scripts/Battlefield.cs`). Everything below is
what the game does today with a hull built from primitives; the ask is a real model that the same code can drive.
`battleship_layout.json` beside this file carries the same numbers machine-readably, and `reference-ingame.png` is a frame
from the running game with the primitive hull, for the layout and the palette.

## 1. What it is and how the fight goes

The Front is a battlefield zone: two battleships 80,000 u (10,000 m on the readout) apart, each held nose-on to the other.
Ours is the cargo ship you already modelled (`cargo_carrier`), fitted for the fight with a set of subsystems bolted to its
hull; theirs is a hull of the cargo ship's size carrying the same set. Each battleship launches fighters from a bay on its
port side (raiders for them, wingmen for us), and its turrets fire on anything hostile in reach. The subsystems have to go
in order, and the side whose core goes first loses the field:

| Order | Subsystem | Count | Health | Open to fire | What it does |
|---|---|---|---|---|---|
| 1 | Turret | 4, on the deck, fore and aft, both sides | 500 | always | A heavy bolt, 10 dmg, 2.5 a second, out to 12,000 u, a third of a degree of spread; the head swings onto the nearest hostile |
| 2 | Shield generator | 2, domes amidships | 260 | once every turret is down | While any stands the hangar and the core each sit under a shield bubble; the generators themselves sit under one while any turret stands |
| 3 | Hangar control | 1, a block aft of the domes | 380 | once the shield is down | While it stands a fighter launches from the bay every 14 s until three are up |
| 3 | Core | 1, the bridge tower forward | 700 | once the shield is down | Its loss destroys the battleship |

A part that is not open to fire wears a shield bubble the code draws (a soft rim sphere, red for theirs and cyan for ours);
a bolt on it flares the bubble and does nothing. A part that dies goes dark (its lamp off, its body to the dark
material, a turret's barrel hidden), burns for a while, and the core's death is three blasts and the big boom.

## 2. Frame, units, axes, scale

- Units are the game's world units, 1 u = 0.125 m on the readout. Build at world scale, as the cargo carrier is: the hull
  box is 7,400 u long, 1,080 u tall, 1,800 u wide (half-extents 3,700 × 540 × 900), the same as the cargo ship's
  collision box, and the origin is the hull's centre.
- Axes in the game's frame: +X the nose (forward), +Y up, +Z port, the side the fighter bay opens on.
- Import: follow the `cargo_carrier` conventions exactly. I load it the same way (glTFast mirrors X on import and the game
  turns the model a half turn about Y, so the nose ends on +X). Do not add a flip or a turn of your own; author it the
  way you authored the carrier and it will land the same way.
- Deliver a self-contained GLB (packed textures, no Draco or meshopt), plus a manifest JSON with every named node's
  position in the frame above, as you did for the cockpit. If a mesh or empty is off from my numbers, your manifest wins:
  I read the positions from it.

## 3. The layout today (positions in the hull frame, unscaled u)

| Part | Position | Size today | Hit disc | Notes |
|---|---|---|---|---|
| Hull | 0, 0, 0 | box 7,400 × 1,080 × 1,800 | — | The ship bounces off three spheres of 1,000 u along its length (x −2,400, 0, 2,400); rocks are shoved out to 4,200 u |
| Deck plate | 1,200, 640, 0 | 3,400 × 200 × 1,200 | — | A raised deck the turrets and domes stand on |
| Engines | −3,900, 0, 0 | block 500 × 900 × 1,500 | — | Three glows at −4,180, 0, z = −450 / 0 / 450, 220 u across |
| Bow trim | 3,702, 0, 0 | 8 × 500 × 1,400 | — | An emissive strip at the bow |
| Turret pad 1 | 2,300, 600, 520 | pad 320 × 60 × 320 | — | Starboard forward is z −520; port is +Z |
| Turret pad 2 | 2,300, 600, −520 | | | |
| Turret pad 3 | −1,900, 600, 520 | | | |
| Turret pad 4 | −1,900, 600, −520 | | | |
| Turret head N | pad + (0, 190, 0) | sphere 150 u radius, barrel 44 × 130 forward of it, an eye lamp | 210 u | The head is a pivot: the code rotates it onto its target (LookRotation of the aim, hull up); at rest it points outward and a little up |
| Generator 1 | 200, 620, 380 | dome 220 u radius, a lamp on top (90 u) | 280 u | Bubble radius when covered: 350 u |
| Generator 2 | 200, 620, −380 | | | |
| Hangar control | −1,200, 600, 0 | block 520 × 300 × 520, a lamp plate on top | 340 u | Bubble radius when covered: 410 u |
| Core (bridge) | 3,000, 760, 0 | tower 560 × 460 × 700, a window strip on its forward face | 400 u | Bubble radius when covered: 470 u |
| Fighter bay | 0, −150, 1,150 | the mouth: a lamp 1,400 × 380 on the port side at z 905 | — | Fighters spawn here and leave along +Z, out of the mouth |
| Light | 0, 1,400, 0 | point light, `#ff8aa0`, range 5,000 | — | The code can keep providing this; a lit hull is all that matters |

Positions, sizes and radii are all yours to improve; keep the parts on the outside of the hull where a bolt can reach
them, keep the four turrets clear of the domes and the bridge, and keep a clearance of at least the bubble radius round
each covered part so its bubble does not intersect the hull.

## 4. Palette and materials

The enemy wears the raiders' colours: hull `#3B3040` (0.23, 0.19, 0.25, metallic 0.3, gloss 0.45), dark `#171420`,
plate `#5C546B` (metallic 0.55, gloss 0.5), trim and lamps emissive: red `#ff2e63` and warm `#ffb070`. Opaque PBR, packed
metallic-roughness and normal maps as on the carrier. No glass anywhere: the shield bubbles are the code's. Emissive parts
that the code switches off on a death (lamps, the bridge windows, a turret's eye) must be separate meshes with their own
material, so I can swap them to the dark material.

Ours, on the cargo ship, wears cyan `#5ED3F0` where theirs wears red. If you make the subsystem set as a separate,
reusable fittings GLB (four turret assemblies, two domes, the hangar block, the bridge tower), I can bolt the same set to
the cargo ship at the same coordinates in its frame and tint the trim per side; that is optional, the enemy hull is the
ask.

## 5. Naming contract (what my loader will look for)

Empties (position only; identity rotation unless noted):

- `turret_1` … `turret_4`: the pad centre of each turret.
- `generator_1`, `generator_2`, `hangar_control`, `core`: the centre of each part.
- `bay`: where fighters spawn, on the port side, with its +Z pointing out of the mouth.
- `collide_1` … `collide_3` (optional): centres of the collision spheres, radius in extras `r`; else I keep mine.

Meshes (separate nodes, each with its own material where it has to change):

- `hull` (any number of static meshes under it).
- `turret_N_head`: the pivoting head with its barrel, pivot at the head's centre, barrel along the node's +Z, +Y up.
  `turret_N_pad` static. `turret_N_lamp`: the eye, emissive.
- `generator_N_body`, `generator_N_lamp`.
- `hangar_body`, `hangar_lamp`.
- `core_body`, `core_lamp` (the bridge windows).
- `bay_lamp`: the mouth's glow. `engine_glow_0` … `engine_glow_2`.

Anything else (greebles, antennae, the deck) is static under `hull`. Nothing animates but the turret heads, which the
code turns. No lights or cameras in the file (a point light is fine if you prefer to place it; name it `light`).

## 6. Where it goes and how it is checked

Drop the GLB at `Assets/StreamingAssets/enemy_battleship.glb` in the combat checkout with the manifest beside it; I load
it at run time like the cockpit. `Builds\Windows\BeltRunner.exe -battle` boots a sandbox 9,000 u off the enemy's bow,
holds the trigger on its nearest open part and shoots ten frames, `battle_1` to `battle_10`, under
`%USERPROFILE%\AppData\LocalLow\nrivali\Belt Runner`, with a log line per frame naming what stands. That is how I will
check the model: the turret heads turning onto the ship, a turret going dark when it falls, the bubbles sitting round the
covered parts, and fighters coming out of the bay.
