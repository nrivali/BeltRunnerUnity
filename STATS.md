# Combat stats

The numbers as the code has them today. World units (u) are what the code uses; a readout metre is half a unit, so
900 m on the HUD is 1,800 u. Sources: `Assets/Scripts/Data.cs`, `Ship.cs`, `Raiders.cs`.

## Side by side (base player vs a Kessler Belt raider)

| | Player (base fit) | Raider (Kessler, danger 0.5) |
|---|---|---|
| Hull | 50 | 50 |
| Shield | 50, recharges empty to full in 3 s after 10 s without a hit; sounds when it is stripped, while it is down, and while it recharges (cut by a hit); a breathing red edge on screen while it is down | 50, recharges 10/s after 10 s without a hit |
| Gun | Autocannon: 8 dmg, 6 shots/s, 10,000 u (5,000 m) | 4 dmg, 6 shots/s, 6,000 u (3,000 m) reach |
| Bolt speed | 5,000 u/s (dies after 2.2 s, 11,000 u) | 2,600 u/s (dies after 2.6 s, 6,760 u) |
| Fires when | Trigger held (LMB or L), at the crosshair (the mouse); the dish turret covers the forward half | Nose within 25° of the ship and inside gun reach; rough aim: 0.09 spread (~5°) and the lead over- or under-estimated by 55–115% shot by shot |
| Hit box | Ship radius (SHIP_R) × 0.8 | 60 u for bolts (hull is 28 u for the reticle and ranges; the model is drawn at twice its original size) |
| Top speed | 500 u/s (250 m/s), ×2 on the afterburner from the start, up to ×5 with the refits | 865 u/s, ×1.25 (1,081) on the boost (runs in from beyond 900 u and long runs out), capped on a strafe to hold the circle |
| Thrust | 328 u/s² | 220 u/s² accel (352 on the boost), 320 u/s² braking |
| Turn rate | 30°/s yaw and pitch | 30°/s |
| Damage per second landed | 48 | 24 |
| Time to strip shield + hull (100) | ~4.2 s of raider hits | ~2.1 s of player hits |
| Bounty | | 140 cr, plus 35% chance of 8–28 u of outer-belt ore |

## Player refits (Autocannon and the rest)

| Refit | Levels | Costs |
|---|---|---|
| Autocannon | 8 dmg · 6/s → 12 · 7/s → 18 · 8/s → 26 · 10/s, 5,000 m at every level | 900, 3,200, 9,000 |
| Hull plating | 50 → 80 → 125 → 200 → 300 | 250, 900, 3,000, 9,000 |
| Engines (thrust · top speed, u) | 328 · 500 → 438 · 640 → 562 · 800 → 718 · 980 → 922 · 1,220 (250 m/s base on the readout) | 300, 1,100, 3,500, 10,000 |
| Afterburner | ×2 (fitted from the start) → ×3 → ×4 → ×5 speed on Shift, heavy fuel burn | 3,000, 9,000, 24,000 |
| Fuel tank | 100 → 160 → 250 → 400 → 600 | 150, 600, 2,000, 6,000 |
| Mining laser | 3 → 5 → 8 → 12 → 18 | 350, 1,400, 5,000, 16,000 |
| Laser range | 2,500 → 3,500 → 5,000 → 7,000 u | 2,500, 9,000, 25,000 |
| Laser overcharge | ×1 → ×1.5 → ×2 → ×2.5 → ×3 on G, draws fuel | 600, 2,200, 7,000, 18,000 |
| Cargo hold | 4 → 6 → 8 → 11 → 14 → 18 slots | 200, 700, 2,400, 7,500, 20,000 |
| Scanner | 28,000 → 46,000 → 74,000 → 135,000 u | 400, 1,800, 6,000 |

Shield is fixed at 50 for every fit (no refit yet).

## Player flight

- Newtonian: thrust along the nose, velocity persists. Drag 0.32/s while thrusting, 1.28/s at zero throttle.
- Retros (S at zero throttle): 0.4 × thrust. Drift brake (hold Space): engine cuts, retros at 2.0 × thrust, turn rate ×2 (60°/s), drag stays
  at the thrusting value, nose swings free.
- Q lock on a raider steers the ship to keep the nose on it; the mouse still aims the gun. The LEAD pip marks where to
  put the crosshair for a bolt fired now to meet the raider.
- Losing: hull 0 → a death animation (the raiders' six ends: 30% the blast, else a wreck tumbling for 2.6–4.5 s), then
  recovery, respawn at the cargo ship; 15% of credits as the fee, and if raiders were attacking, 35%
  of every ore in the hold is stripped.

## Raiders

- Scale with zone danger. Only Kessler Belt sets it (0.5); every other zone is 0.
  - Health and shield: round(50 × max(1, 0.5 + danger)) → 50 at both danger 0 and 0.5.
  - Speed 820 + 90 × danger → 865 at Kessler. Bounty 80 + 120 × danger → 140.
  - Holds: min(rich pockets, round(3 + 6 × danger)) → 6 at Kessler, 3 elsewhere. Raiders per hold: 1 + random(0 ..
    min(3, 1 + floor(danger))) → 1 or 2 at Kessler, 1 elsewhere.
- Engage a flying ship within 9,000 u (4,500 m); the music cuts to the combat track (Vanguard, 156 bpm) while any
  raider attacks and drifts back six seconds after the last one stops; give up beyond 14,000 u, or when the ship is disabled or docked.
- The cargo ship's guns cover 9,000 u: raiders inside lose 30 health/s and never engage there.
- Manoeuvres: run in (weaving) until inside 600 u; strafe at a radius of 200–500 u for 2–4 s, speed capped so the
  circle holds; then 50% a long run at full thrust to a point 2,500–5,000 u away (1,250–2,500 m) and a fresh run back in, else 90% another strafe / 10% a short
  break (1.2–2.5 s). Jink when a player bolt is coming their way (miss under 138 u, within 2,500 u): 70% of the time,
  0.8 s hard turn, then 1.6 s cooldown.
- Flight model: heading turns at most 30°/s, accel 220, braking 320, movement only along the nose.
- Destroyed: 30% blow up on the spot; that is the only explosion and the only hull that comes apart. The rest wreck
  and then go dark, drifting on in one piece with their velocity and tumble: burn (~20%: fire and a growing tumble,
  3.5–8 s), chain (~15%: pops along the hull for ~1 s), runaway (~13%: engine jammed open, corkscrews away at
  420 u/s² for 1.5–3 s), vent (~13%: jets of gas burst from the hull every 0.35–0.9 s for 4–9 s), dead (~10%: lights
  out, flat spin with arcs for 10–18 s). Every wreck keeps its momentum;
  the hull ends as debris that drifts for 60 s. The bounty is paid at the kill.

## Combat test (`combat-test.bat`, the `-combat` flag)

- Sandbox: nothing reaches the save. Autocannon fitted (`-gun N` for level N), fuel and credits topped up every frame.
- F9 clear the raiders and spawn three fresh 2,000–4,000 m out · F10 raiders hold their fire · F8 the refit panel anywhere, with a − on each row to
  take a level off. A fresh wave of three spawns 10 s after the last raider dies.
