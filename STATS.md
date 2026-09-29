# Combat stats

The numbers as the code has them today. World units (u) are what the code uses; a readout metre is eight units (2026-09-16; it was half a unit), so
225 m on the HUD is 1,800 u. Sources: `Assets/Scripts/Data.cs`, `Ship.cs`, `Raiders.cs`.

## Side by side (base player vs a Kessler Belt raider)

| | Player (base fit) | Raider (Kessler, danger 0.5) |
|---|---|---|
| Hull | 50 | 50 flat, the player's base (2026-09-23, one for one; scaled with the zone's danger before) |
| Shield | 50 at the base refit (80, 120, 170, 240 with the Shield refit), recharges empty to full in 3 s after 5 s without a hit (2026-09-23; 10 s before); sounds when it is stripped, while it is down, and while it recharges (cut by a hit); a breathing red edge on screen while it is down | 50, recharges empty to full in 3 s after 5 s without a hit, the player's own (2026-09-23, one for one; 10/s and 10 s before) a hit |
| Gun | Autocannon: 8 dmg, 6 shots/s, 8,000 u (1,000 m since 2026-09-23; 10,000 u until 2026-09-22, 20,000 for a day); overheats after 5 s of continuous fire, locked out until cooled below 40% (4 s from full to cold) | 8 dmg, the player's own (2026-09-23, one for one; 6 from 2026-09-22, 4 before), 6 shots/s, 8,000 u (1,000 m) reach (10,000 for two days, 6,000 u until the 2026-09-21 speeds) |
| Bolt speed (the 2026-09-21 speeds) | 32,000 u/s (2026-09-23, doubled; dies after 0.3 s, 9,600 u; 16,000 and 0.6 s before; 0.8 then 1.3 s for a day each) | 32,000 u/s too (doubled 2026-09-23; 16,000 since 2026-09-22 (dies after 0.8 s, 12,800 u; 9,000 for a day) |
| Rockets | Seeker rockets: 4 aboard (restocked on the pad), one per trigger pull, 6 s reload; 8,000 u/s (accelerating at 6,000 u/s² from the ship's speed plus 1,000), turning 70°/s onto the raider with a velocity lead, 12 s of flight; contact within 70 u destroys the raider outright with the blast; goes after the locked raider, else the nearest within 30° of the nose, else the nearest within 12,000 u (1,500 m) | none |
| Fires when | Trigger held (LMB or L), straight down the nose like the laser; the mouse steers the nose onto the LEAD pip; 2 selects it, 1 the laser, 3 the rockets, the wheel cycles | Nose within 40° of the mark (25° until 2026-09-22) and inside gun reach; aim: 0.02 spread (~1.1°) and the lead 90–110% of true shot by shot (0.09 and 55–115% until 2026-09-22, 0.04 and 80–115% for a day) |
| Hit box | 91 u, SHIP_R × 1.9 (× 0.8, 38 u, until 2026-09-22; × 1.5 for a day): the same generosity the raiders get | 120 u for bolts (2026-09-23, the raider twice its size; 240 for an hour at four times, 60 before) (hull is 28 u for the reticle and ranges; the model is drawn at four times its original size since 2026-09-23, eight for an hour, twice before) |
| Top speed | 1,600 u/s (200 m/s on the readout, the base and the max since 2026-09-23; 2,000 for a day, 500 u/s until 2026-09-21), ×2 on the afterburner, the only level | 1,600 u/s (2026-09-23), ×2 (3,200) on the boost, the player's own afterburner (one for one; ×1.25 before) (runs in from beyond 3,000 u and long runs out), capped on a strafe to hold the circle |
| Thrust | 1,050 u/s² (328 until 2026-09-21, 2,624 then 1,312 for a day each) | 1,050 u/s² accel, the player's own (2026-09-23, one for one; 1,100 before; 1.6× on the boost), 1,600 u/s² braking |
| Turn rate | 25°/s yaw and pitch, 35°/s in the drift (2026-09-23, halved again; 50 and 70 before) | the player's own, 25°/s today (2026-09-23, one for one; 30 before) |
| Damage per second landed | 48 | 48 (2026-09-23, one for one; 36 before) |
| Time to strip shield + hull (100) | ~2.1 s of raider hits (2026-09-23; 2.8 before) | ~2.1 s of player hits |
| Bounty | | 140 cr paid at the kill, plus 1–3 lumps of outer-belt ore (10–30 u each; 25% the first is 40–60) thrown from the wreck to be collected |

## Player refits (Autocannon and the rest)

| Refit | Levels | Costs |
|---|---|---|
| Autocannon | 8 dmg · 6/s → 12 · 7/s → 18 · 8/s → 26 · 10/s, 1,000 m at every level (1,250 until 2026-09-22, 2,500 for a day) | 900, 3,200, 9,000 |
| Seeker rockets (magazine · reload · chase) | 4 · 6 s · 1,500 m → 6 · 5 s · 1,750 m → 8 · 4 s · 2,000 m → 10 · 3 s · 2,250 m | 1,200, 4,000, 11,000 |
| Hull plating | 50 → 80 → 125 → 200 → 300 | 250, 900, 3,000, 9,000 |
| Shield | 50 → 80 → 120 → 170 → 240, always empty to full in 3 s | 500, 1,800, 5,500, 14,000 |
| Engines (thrust · top speed, u) | 1,050 · 1,600, one level only (200 m/s on the readout, the base and the max, 2026-09-23: "lower max thrust speed to 200"; 2,000 for a day, 4,000 before, refits 500 to 1,220 u until 2026-09-21) | none |
| Afterburner | ×2 on Shift, the only level (2026-09-22: "only x2"; ×3 to ×5 refits until then), heavy fuel burn | none |
| Fuel tank | 100 → 160 → 250 → 400 → 600 | 150, 600, 2,000, 6,000 |
| Mining laser | 3 → 5 → 8 → 12 → 18 | 350, 1,400, 5,000, 16,000 |
| Laser range | 2,500 → 3,500 → 5,000 → 7,000 u | 2,500, 9,000, 25,000 |
| Laser overcharge | ×1 → ×1.5 → ×2 → ×2.5 → ×3 on G, draws fuel | 600, 2,200, 7,000, 18,000 |
| Cargo hold | 4 → 6 → 8 → 11 → 14 → 18 slots | 200, 700, 2,400, 7,500, 20,000 |
| Scanner | 28,000 → 46,000 → 74,000 → 135,000 u | 400, 1,800, 6,000 |

Shield is fixed at 50 for every fit (no refit yet).

## Player flight

- Newtonian: thrust along the nose, velocity persists; the throttle sets the speed cap (half throttle holds half the top
  speed). Drag 0.32/s while thrusting, 3/s at zero throttle (no coasting, 2026-09-23: the ship stops in a second or two; the drift alone coasts at 0.128; coasts about
  15 × speed in distance: from top speed about 3,000 m; the S retros do the stopping).
- Retros (S at zero throttle): 0.4 × thrust. Drift (hold Space): engine cuts, no retros, the ship coasts on at the
  coasting drag, turn rate 35°/s (the usual 25), nose swings free: for turning round, not stopping.
- Both weapons fire straight down the nose; the mouse steers. Every raider in reach carries a LEAD pip, drawn at the
  crosshair's distance along the line to the lead point; a Q or MMB lock on a raider steers the nose onto that point.
- Losing: hull 0 → a death animation (the raiders' six ends: 30% the blast, else a wreck tumbling for 2.6–4.5 s), then
  recovery, respawn at the cargo ship; 15% of credits as the fee, and if raiders were attacking, 35%
  of every ore in the hold is stripped.

## Raiders

- Scale with zone danger. Only Kessler Belt sets it (0.5); every other zone is 0.
  - Health and shield: round(50 × max(1, 0.5 + danger)) → 50 at both danger 0 and 0.5.
  - Speed 1,600 flat (200 m/s), the same as the player's and no danger scaling (2026-09-22; 820 + 90 × danger until 2026-09-21). Bounty 80 + 120 × danger → 140.
  - Holds: min(rich pockets, round(3 + 6 × danger)) → 6 at Kessler, 3 elsewhere. Raiders per hold: 1 + random(0 ..
    min(3, 1 + floor(danger))) → 1 or 2 at Kessler, 1 elsewhere.
- Ambushes (2026-09-19): in any belt but the Hub's, while the ship flies clear of the cargo ship's cover with no fight on,
  a clock of random(150 .. 300) / (1 + danger) s runs (100–200 s at Kessler, 150–300 elsewhere). When it runs out, with no
  raider within 26,000 u and the outpost not engaged, 1 + random(0 .. 2 + floor(2 × danger)) raiders (1–3 at Kessler,
  1–2 elsewhere) drop in 12,000–16,000 u out, behind or beside the ship (never within 72° of the nose), on the attack at
  once, with the alarm. An ambusher that gives up is removed after 30 s idle or once the ship is 21,000 u away.
- Engage a flying ship within 16,000 u (2,000 m); the music cuts to the combat track (Vanguard, 156 bpm) while any
  raider attacks and drifts back six seconds after the last one stops; give up beyond 26,000 u, or when the ship is disabled or docked.
- The cargo ship's guns cover 9,000 u: raiders inside lose 30 health/s and never engage there.
- Manoeuvres: run in (weaving) until inside 2,000 u; strafe at a radius of 1,400 u for 2–4 s, speed capped so the
  circle holds; then 50% a long run at full thrust to a point 5,000–9,000 u away (625–1,125 m; 8,000–16,000 for a day) and a fresh run back in, else 90% another strafe / 10% a short
  break (1.2–2.5 s). Jink when a player bolt is coming their way (miss under 138 u, within 8,000 u): 70% of the time,
  0.8 s hard turn, then 1.6 s cooldown.
- Flight model: heading turns at most 30°/s, accel 1,100, braking 1,600 (220 and 320 until the 2026-09-21 speeds), movement only along the nose.
- Destroyed: 30% blow up on the spot; that is the only explosion and the only hull that comes apart. The rest wreck
  and then go dark, drifting on in one piece with their velocity and tumble: burn (~20%: fire and a growing tumble,
  3.5–8 s), chain (~15%: pops along the hull for ~1 s), runaway (~13%: engine jammed open, corkscrews away at
  420 u/s² for 1.5–3 s), vent (~13%: jets of gas burst from the hull every 0.35–0.9 s for 4–9 s), dead (~10%: lights
  out, flat spin with arcs for 10–18 s). Every wreck keeps its momentum;
  the hull ends as debris that drifts for 60 s. The bounty is paid at the kill.

## The raider outpost (`Assets/Scripts/Outpost.cs`, the raid contract)

| | Turret (one of three) | Core |
|---|---|---|
| Health | 150, no shield of its own | 500 under a shield that soaks every hit while any turret stands |
| Gun | A bolt at 6 dmg, 2.5 shots/s, 10,000 u (1,250 m) reach (7,000 u until 2026-09-19), flying at 28,000 u/s (doubled 2026-09-23; the raiders' bolt does 9,000), fired only with the ship on the mount (a slew of about 4/s exponential) | none |
| Rocket battery (2026-09-19) | | A seeker at the ship from the top of the core every 8 s while the core stands and the ship is engaged within 11,000 u, two in the air at a time; 8,000 u/s top speed, accelerating at 6,000 u/s² from 1,000, turning 55°/s (the player's do 70), 12 s of flight; a hit within 1.2 ship radii does 22 (most of a base shield, or nearly half a base hull) |
| Countermeasures | | B drops a flare: a burning decoy thrown back off the ship's way, 6 s; every seeker within 8,000 u of the ship goes for it 85% of the time and pops harmlessly on it. 4 flares aboard, restocked on the pad, one every 1.5 s. A player bolt passing within 45 u of a seeker shoots it down |
| Aim | The lead 75–110% of true shot by shot, spread 0.045 (~2.5°): half the raiders' | |
| Hit box | 190 u disc round the head (the head is 150 u) | 640 u disc (the core is 600 u); the shield bubble 820 u |
| Damage per second landed | 15 per turret, 45 with all three bearing | |
| Wakes | Inside 12,000 u (1,500 m) of a flying ship; the hangar launches raiders two in the air at a time, one every 9 s, six a raid, while the core stands | |
| Pays | | 2,500 cr + 750 per outpost already put down under contract, plus 5–8 lumps of outer-belt ore (20–60 u); rebuilt, and the contract re-posted, 600 s of play after the raid |

At the base autocannon (48 dps) a turret takes about 3 s of hits and the core about 10 s, with the gun's 5 s heat limit and 4 s cool-down in between; at the top refit (260 dps) the whole outpost falls in about 4 s of fire.

## The battlefield (`Assets/Scripts/Battlefield.cs`, The Front, the combat branch, 2026-09-21)

Two battleships 80,000 u apart (10,000 m): ours is the cargo ship on station, theirs a hull of the same size. Each carries the same subsystems, which fall in order.

| Subsystem | Health | Open to fire | Does |
|---|---|---|---|
| Turrets × 4 (deck, fore and aft, both sides) | 500 each | always | Artillery (2026-09-23, "cinematic"; "way stronger" on the 21st, "very accurate" on the 22nd): 30 dmg, 0.8/s each (a shot every 1.25 s), 28,000 u/s (doubled 2026-09-23), 14,000 u reach (1,750 m, a defensive bubble for the staged battle, 2026-09-23; 80,000 and 40,000 u for a while that day, 12,000 u before), lead 90–110% of true, spread 0.02 (about a degree); the bolt drawn 16 × 90 with an 84 u glow. The day before: 10 dmg, 2.5/s, 24,000 u/s, lead 97–103%, spread 0.005; at the nearest hostile (for theirs: the player, our wingmen; for ours: their fighters) |
| Shield generators × 2 (domes amidships) | 260 each | once every turret is down | While any stands the hangar and the core each wear a shield bubble (70 u past the hit disc; 2026-09-22, one 2,400 u bubble over the aft hull before, which had the aft turrets inside it and unkillable); a hit on a covered part flares its bubble and does nothing, and nothing else on the hull soaks a bolt. The generators themselves wear one while any turret stands |
| Hangar control (aft) | 380 | once the shield is down | While it stands a fighter launches every 5 s until 5 are up (2026-09-23; 9 for a while that day, every 14 s until 3 before) (the first 6 s in) |
| Core (the bridge tower, forward) | 700 | once the shield is down | Its loss ends the battle: theirs, the field is won (8,000 cr + 1,000 per field already won); ours, it is lost |

- Fighters are the zone's raiders (Kessler's numbers: 50 hull, 50 shield, 1,600 u/s, the base autocannon) on either team; ours carry no bounty. Their mark each frame: for theirs, the player when flying within 7,000 u, else the nearest wingman, else our nearest exposed subsystem; for ours, the nearest raider, else their nearest exposed subsystem. The attack manoeuvres are the raiders' own, flown at the mark.
- Bolts of either side hit the other side's fighters (within 120 u since the raiders grew on 2026-09-23; 60 before) and the other side's battleship; a raider's still hits the ship. The player's bolts never hit a wingman.
- At the base autocannon (48 dps): a turret ~10.5 s of hits, all four ~45 s; a generator ~5.5 s; the hangar ~8 s; the core ~15 s, with the gun's heat cycle in between; about 110 s of fire for the whole ship, before the fighters and the turrets' fire on the player.
- The stages (2026-09-23): the ships 48,000 u apart (6,000 m) with a 8,000 u neutral zone at the midpoint. One meter from -10 (theirs) to +10 (ours): a fighter kill by a wingman or a turret 1, by the player 2, the player's loss -3, and a drift of a notch every 15 s toward the middle while neutral. An end starts that side's attack (90 s, or until the defenders drag the meter back past the middle); only then are the other ship's parts exposed, in the usual order, and the damage stays between attacks. The attacking side's launches come in at the neutral zone. The fifth push to an end breaks the other ship.
- The enemy turrets' 14,000 u reach (1,750 m; 80,000 and 40,000 u for a while on 2026-09-23, 12,000 u before) is a bubble round the ship that never covers the neutral zone, so the guns only matter to an attack; a rock between blocks the shot; the four together land about 24 dmg/s (a 30-point shot every 1.25 s each) when every shot lands, which at a degree of spread they no longer do on a ship they have the lead on, against a 50-point base shield.

## Combat test (`combat-test.bat`, the `-combat` flag)

- Sandbox: nothing reaches the save. Autocannon fitted (`-gun N` for level N), fuel and credits topped up every frame.
- F9 clear the raiders and spawn three fresh 500–1,000 m out · F10 raiders hold their fire · F8 the refit panel anywhere, with a − on each row to
  take a level off. A fresh wave of three spawns 10 s after the last raider dies.
