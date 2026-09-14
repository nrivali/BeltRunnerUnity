# Belt Runner 3D · Unity port

A port of the browser game `belt-runner-3d.html` (repo `BeltRunner`, v0.9.120) to Unity 6. The browser game stays the
reference and is never edited from here; the Godot port (`BeltRunnerGodot`) is a faithful transcription of the same
game and is the second reference. See `AGENTS.md` for the rules.

## Running it

1. Install Unity Hub and a Unity 6 editor (`winget install Unity.UnityHub` and `winget install Unity.Unity.6000`; the
   project is set to 6000.6.0f1, the version winget installs, and any Unity 6 will do). Sign in to the Hub once for
   the free Personal licence.
2. Add this folder as a project in the Hub and open it. The first open imports the packages in `Packages/manifest.json`
   (UGUI and the built-in modules) and fills in `Library`.
3. Press Play in `Assets/Scenes/Main.unity` (an empty scene) or any other. Everything is built from code at start-up
   (`Game.Boot`): there is no scene content and no prefab. The only assets are the rock shader and its material,
   which exist so a build keeps the shader and its GPU-instancing variants.

Built-in render pipeline, legacy Input Manager, UGUI, and the Unity glTFast package for the GLB models (the first open fetches it from the Unity registry). Nothing to configure.

### The combat test

    Builds\Windows\BeltRunner.exe -combat

boots straight into a fight: the menu is skipped, the ship is set down 1,500 u off a raider hold facing it with the
autocannon at its base level (`-gun 2` for a refit level), 5,000 cr to refit with, hull full, a tank that never runs dry, the tutorial off. It is
a sandbox: nothing the session does reaches the save file. `combat-test.bat` at the root runs it. In any session F9
jumps to the next raider hold, so a fight can be re-run without flying back. In the test a raider killed comes back where it died three seconds on, so the same hold can be fought again and again.

### Building and the smoke run from the command line

```bash
"/c/Program Files/Unity 6000.6.0f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "" -executeMethod Build.Player -logFile Logs/build.log
./Builds/Windows/BeltRunner.exe -smoke -screen-width 1280 -screen-height 720 -logFile Logs/smoke.log
```

`Build.Player` (in `Assets/Editor/Build.cs`) makes the empty scene if it is missing, adds the shaders the code asks for
to Always Included Shaders, keeps instancing variants, and builds `Builds/Windows/BeltRunner.exe`.

### Without an editor

`tools/check` compiles every script against a stub of the Unity API with the .NET SDK; `tools/sim` runs the
engine-free logic (belt generation, rock meshes, the nose ray, scans, rails, kills and respawns) and prints what it
finds:

```bash
dotnet build tools/check
dotnet run --project tools/sim
```

They prove syntax, types and the pure logic, not the rendering or the feel. The editor does that.

### The smoke run

With `-smoke` on the command line the player starts without the menu, cuts the nearest copper rock through, waits for
the ore, flies, saves, and quits, printing `smoke:` lines to the log and saving `smoke_launch.png`, `smoke_mine.png`,
`smoke_broken.png`, `smoke_flight.png`, `smoke_taxi.png`, `smoke_approach.png`, `smoke_dock.png` and `smoke_pad.png` under `Application.persistentDataPath`
(`%USERPROFILE%AppDataocallow
rivalibelt runner`).

## Combat — raiders at the rich pockets, the autocannon

New to the Unity build (2026-09-14), on the user's brief: raider ships only, a dedicated gun, a lost fight costs
cargo, raiders live only at the rich pockets. The raiders themselves are the browser's scrapped pirates
(makePirate / updateHazards) brought back with their numbers; the gun, the holds' placement and the HUD are new.

| Piece | Where | Notes |
|---|---|---|
| Raider holds: a hold of one to three raiders at some of the rich pockets (three holds, plus six per point of zone danger; Kessler's danger is 0.5, so six holds), wandering round home. Within 9,000 u (4,500 m) of a flying ship outside the cargo ship's gun cover they attack: close in, strafe, and fire the player's own base autocannon (8 damage, 4 bolts a second, 900 m, bolts at 2,600 u/s) whenever their nose is within 25° of the ship, since the guns are fixed forward. Each carries a 50-point shield that soaks damage first and recharges (10 a second) after ten seconds without a hit, over 50 health. They fly a weaving run in, a strafing pass at a radius (200 to 500 u) and direction of their own, then mostly another pass, sometimes a short breakaway or a long run out to a point 1,500 to 3,000 m from the ship before coming back in, and jink aside when a bolt is coming their way (seven times in ten, once every 1.6 s at most). They fly like ships: the nose turns at 35° a second at most, thrust and braking are limited, and they only ever move along the nose, so nothing they do is a snap the player could not match. They give up beyond 14,000 u, or when the ship is disabled or docked | `Assets/Scripts/Raiders.cs` | primitives for the hull (cone body, swept wing, red trim, an eye, an exhaust glow); health 50 and shield 50, speed 865, bounty 140 cr at danger 0.5 |
| The cargo ship's guns: raiders inside 9,000 u of the carrier lose 30 health a second and never engage there; the hangar is a refuge | `Raiders.cs` (`SAFE_R`) | the browser's rule |
| The autocannon, fitted from the start (8 damage at 4 shots a second, 900 m) with three refit levels up to 26 damage at 8 a second and 1,700 m (900, 3,200 and 9,000 cr), selected with the scroll wheel (which swaps between the laser and the cannon; the WEAPON readout shows which) and fired with the trigger at the crosshair, which is the mouse: bolts leave the dish for the point under the cursor at gun range (held to the forward half, the arc the dish turret covers), no auto-aim; with a raider locked (or under the crosshair) an amber LEAD pip marks where a bolt fired now would meet it, so the pilot puts the crosshair on the pip. A raider lock only tracks; the ship steers itself onto rocks and the cargo ship only | `Assets/Scripts/Data.cs` (`gun`), `Assets/Scripts/Ship.cs` (`TickLaser`) | the ship always has it |
| Hover and lock: raiders are hover targets ("Raider") and Q locks them; the target pane shows "Pirate raider", its health and "Hostile"; the reticle sits on the raider under the nose; attacking raiders carry a red RAIDER marker with their range; the readouts gain THREAT | `Ship.cs` (`HoverPick`), `Assets/Scripts/Hud.cs` | |
| The ship's shield: 50 points that soak any damage (bolts, collisions, hull knocks) before the hull, and recharge at 10 a second once ten seconds have passed without a hit; a SHIELD gauge sits beside HULL. Hull plating is 50 at the first level now (80, 125, 200, 300 with the refits). Hits: a raider's bolt costs shield then hull with the flash, the shake, sparks and a toast (no speed threshold, unlike a collision). At zero hull, raiders that were on you strip 35% of the hold and stand down, then recovery brings the ship back to the pad as usual | `Ship.cs` (`Hurt`, `StartRecovery`) | |
| A kill: the boom and the sparks, the bounty, and a 35% chance of 8 to 28 units of an outer-belt ore salvaged into the hold | `Raiders.cs` (`Kill`) | |
| The nav map lists the raiders under a belt zone; the menu's Controls page explains them | `Hud.cs` (`RefreshMap`), `Assets/Scripts/Menu.cs` | |

The smoke run visits the nearest hold with the autocannon fitted, locks the raider and holds the trigger until it
dies, printing the raiders' state, the shots, the hits taken and the bounty. The save carries the refit as `gun`.

## Milestone 17 — the soundtrack

| Piece | Where | Status |
|---|---|---|
| The procedural music engine: eight tracks (Drift, Halcyon, Aurum, Frost, Sable, Cinder, Meridian, Umbra), each a pad colour of five chord voices through a slowly breathing lowpass, a sub, a tempo-locked echo, ambient layers (sparkle, wind, a wandering melody, a pulse, a choir swell) and a groove (kick, snare or clap, rim, hats, shaker, bass, arp, chord stabs, a lead). Ambient for two or three minutes, the groove for a minute or so, then on to the next track | `Assets/Scripts/Music.cs` | ported from MUSIC_PROC by way of the Godot port: every recipe, pattern, envelope and gain copied; rendered sample by sample at 22,050 Hz on its own thread into a ring buffer that `OnAudioFilterRead` drains at the mixer's rate (the pads resampled from loops rendered once per recipe, the noise hits rendered once per recipe) |
| The comm-channel toasts: "♪ Now drifting: …" when a track starts, "… · groove on the comm channel" when the groove comes in | `Assets/Scripts/Game.cs` | ported |
| Settings: Music on/off and a Music volume, separate from the sound-effects volume (the music source ignores the listener volume), saved with the game as `music` and `music_volume` | `Assets/Scripts/Menu.cs`, `Assets/Scripts/GameState.cs` | ported from the browser's sliders |

The smoke run prints the engine's state at the Hub: the track, the mode, the step, the voice peak, the output peak,
the time rendered, the render cost against real time and the mixer underruns (none).

## Milestone 16 — the burn trail and the fitting variants

| Piece | Where | Status |
|---|---|---|
| The burn trail: once the beam's spot is hot, a scorch decal is stamped where the beam is every 0.1 s, laid on the surface facing the rock's centre with a random turn, the same spot never restamped, up to 64 a rock, gone when the rock breaks | `Assets/Scripts/Belt.cs` (`Scorch`, `DrawBurns`), `Assets/Resources/Shaders/Scorch.shader` | ported from addBurn as instanced quads kept relative to the rock's centre (rocks never turn), drawn with a depth offset over the stone |
| The ship's fitting variants: three tiers of laser barrel, cargo pod, engine nacelle and scanner dish, shown by refit level (tier = 1 + round(2 · level / top level)); the tier-1 set comes with the model's main scene, the rest live in its second glTF scene, which the editor importer leaves out, so they are read from the GLB at run time with glTFast and hung on the hull (laser barrels on the dish's pitch group) | `Assets/Scripts/Ship.cs` (`LoadVariants`, `ConfigureModel`), `Assets/StreamingAssets/player_ship.glb` | ported from the assembler's configure(); a refit bought in the services panel swaps the fitting at once |
| The beam's spot heat, the sun's single shadow box and LOD 0 bounds that follow the drift | `Ship.cs`, `Lighting.cs`, `Belt.cs` | already in from milestones 8 and 13 |

The smoke run pre-heats the spot to show a scorch within the run (it takes 30 s of cutting on its own), then switches
the laser to level 3 and back on the pad and prints which fittings show.

Not ported: the wing choices and hull/accent paint (the browser's customisation, which the port has no menu for; the
delta wings stay).

## Milestone 15 — the curved hull, the radar pulse, exhaust and navigation lights

| Piece | Where | Status |
|---|---|---|
| The cargo ship's curved pressure hull as the collision surface outside the passage: the profile exported with the model (18 stations of a superellipse cross-section and three engine envelopes, the `hull_collision_profile` node's extras) with the nearest surface point and normal found as hull-contact.js does; the box rules stay inside the bay | `Assets/Scripts/CargoShip.cs` (`HullContact`, `Collide`) | ported from hull-contact.js rawContact; the profile is written into the script, since glTFast does not surface node extras |
| Rocks that drift into the hull are set on its surface and shoved off it | `CargoShip.cs` (`BumpRocks`), `Assets/Scripts/Belt.cs` (`PlaceFree`) | ported from depotBumpRocks |
| The radar pulse you can see: a faint sphere and a bright ring growing to scanner range over 2.6 s, and rocks marked only once the pulse reaches them | `Assets/Scripts/Ship.cs` (`BuildPulseFx`, `TickPulse`), `Belt.cs` (`Scan`, `markFrom`) | ported from pulseSphere / pulseRing (the timed marks were in since milestone 1) |
| The engines' exhaust glows swell and brighten with thrust (wide open on the afterburner), the red and green navigation lights blink, the engine light comes on under thrust | `Ship.cs` (`TickEngineFx`, `GlowQuad`, `FaceCamera`) | ported from the exhaust sprites, navLights and shipLight; soft billboard quads on the Field shader |

The smoke run pulses the radar on the way back from the cut, photographs the ring, and prints how many of the marks
have landed as the pulse spreads; it counts the rocks the hull shoved on the way to the pad.

## Milestone 14 — fields, markers, the dish's effects, force fields, rock-on-rock

| Piece | Where | Status |
|---|---|---|
| The charted fields carry names (K1-A…, rich pockets KP-1…) and ride their rails; the FIELD readout names the one you are in; a marker points to the nearest field's edge while you are outside one | `Assets/Scripts/Belt.cs` (`FieldAt`, `NearestField`), `Assets/Scripts/Hud.cs` | ported with milestone 9 |
| A cyan marker on every collector drone with what it is doing, its load and its range; the cargo ship marker names the near dock within 4,500 m | `Hud.cs` (`PlaceMarker`) | ported with milestone 9 |
| The ship's mining dish swings onto the beam's target (a few radians a second, forward half only) and settles forward when idle; six rim emitters glow faintly, pulse while it slews onto a rock and flicker hard while it fires; six rim beams converge on the focus while the beam cuts; the beam starts at the focus | `Assets/Scripts/Ship.cs` (`TickDish`, `BuildDishFx`, `CalibrateDish`) | ported from shipDishAnglesTo / animateDish, driving the model's yaw and pitch nodes; the rig's signs are found by trial at start-up (glTFast mirrors X), and the smoke run prints the rig error |
| The cargo ship dish's beam gets its soft sheath and a glow where it lands, flickering | `Assets/Scripts/CargoShip.cs` (`BuildDish`, `TickDish`) | ported (the glow came with milestone 6, the sheath here) |
| Force fields across both hangar mouths: a shimmering drifting grid that flashes whenever the ship or a drone passes through | `CargoShip.cs` (`BuildForceFields`, `FlashField`, `TickFields`), `Assets/Resources/Shaders/Field.shader`, `Drones.cs`, `Ship.cs` | ported |
| Rock-on-rock contact for rocks that are adrift: overlap pushes both out, mass-weighted, with a soft bounce, knocking the other off its rail | `Belt.cs` (`TickPairs`) | ported from rockPair (the scrap pairs came with milestone 13) |

The player ship's focus and rim empties import at the model origin (their offsets are baked away), so the emitters sit
on a ring round the dish bowl and the focus a little way ahead of it, in the pitch node's frame. The smoke run counts
the force field flashes on the way to the pad.

## Milestone 13 — collisions, sparks, scrap, heat, LOD 0, free look

| Piece | Where | Status |
|---|---|---|
| Ship–rock collision: the frame's path is swept in 24-unit steps against the rocks within reach (refreshed twice a second), resolved against a slightly generous sphere; a knock above 140 u/s costs plating (0.09 a unit over), shakes the camera, flashes, sparks and sounds; plating gone → recovery | `Assets/Scripts/Ship.cs` (`RockContact`, `Impact`) | ported earlier; the impact point and the spark burst came with this milestone |
| bumpRock: a hit knocks the rock off its rail, small rocks taking the whole hit and big ones barely noticing, never faster than the ship hit it | `Assets/Scripts/Belt.cs` (`Bump`) | ported earlier |
| Sparks: a burst of glowing streaks on a rock break or a hull hit, each stretched along its own velocity and thinning as it dies | `Assets/Scripts/Sparks.cs`, `Assets/Resources/Shaders/Spark.shader` | ported from SPARKS (instanced boxes, additive, per-instance colour) |
| Scrap: 5 to 16 small hot chunks off a breaking rock (4 to 12 % of its radius), coasting and spinning, cooling from white-hot over 30 s, bouncing off nearby rocks, one another and the ship (which they shove), shrinking away after half an hour | `Belt.cs` (`SpawnScrap`, `TickScrap`, `ScrapHit`), `Ship.cs` (`RockContact`) | ported from spawnDebris / chunkRock / collideDebris; drawn as one instanced batch of the far lumpy mesh |
| Heat: a damaged rock glows red, then orange, then near-white as its health goes (pulsing slightly); fresh fragments start hot and cool over 30 s; the laser's spot glows where the ship's beam is cooking the stone, with a light and a shower of sparks that grow as the spot heats over 30 s | `Assets/Resources/Shaders/Rock.shader` (`Spot`, the body heat), `Belt.cs` (`glow`, `SetSpotHeat`), `Ship.cs` (`TickSpot`) | ported from heatable() / setRockHeat / heatFx; the spot rides in shader globals |
| LOD 0 up close: a rock nearer than six of its radii leaves its chunk's batch and draws on its own with Astra's finest mesh (back at eight) | `Belt.cs` (`UpdateLod0`, `Draw`), `Assets/Resources/Models/asteroids_lod0.glb` | ported (the browser picks by projected size, 100 px) |
| Free look: hold the right mouse button to swing the camera without turning the ship; it eases back on release | `Ship.cs` (`Fly`, `UpdateCamera`) | ported |

Not ported: the scorch decals the beam leaves on rocks, the dish's own spot heat, and the ship's wing and fitting
variants. The smoke run reports sparks, scrap, LOD 0 rocks and the near-rock count at the cut, photographs the break,
then parks three radii off the nearest giant and checks that it draws at LOD 0.

## Milestone 12 — the Q lock and recovery

The browser's tow tug is not ported: at the user's request the Unity port brings a stranded ship straight back to the
cargo ship instead. Everything else in the milestone follows the browser.

| Piece | Where | Status |
|---|---|---|
| Hover pick: every live rock within 120 km of the camera and the cargo ship are projected to the screen; the nearest whose disc (10 px minimum) holds the cursor is the hover, shown as a label beside the cursor with its range from the nose | `Assets/Scripts/Ship.cs` (`HoverPick`), `Assets/Scripts/Hud.cs` | ported from hoverPick / .hoverLbl (the pick runs at 10 Hz for the label, and afresh on Q) |
| Q: lock the hovered target, switch to a different hovered target, or release; the lock holds out to 50,000 m and lapses when the rock breaks up or falls out of range | `Ship.cs` (`ToggleLock`, `TickLock`) | ported |
| Lock steering: the ship turns itself to put the locked object on the nose ray (proportional, full rate beyond about seven degrees off); the mouse is ignored, roll stays yours; the laser still only cuts what the crosshair is on | `Ship.cs` (`Fly`) | ported |
| HUD: the target pane follows the lock (LOCKED TARGET; the cargo ship as a carrier with no health bar), the RANGE readout shows the lock's distance against the beam's reach, heavier reticle corners when the crosshair is on the lock, Q hints in the hint bar, Q and T rows in the controls list and the menu | `Hud.cs`, `Menu.cs` | ported |
| Recovery: T with a dry tank calls it (a hull breach calls it by itself, with the flash and the alarm, and disables the ship: no thrust, no steering, it drifts); the screen fades, the ship is set down on the pad of the nearer dock, and the fade lifts | `Ship.cs` (`CallRecovery`, `StartRecovery`, `RecoveryUpdate`) | replaces the tug flight (the user's call); 3.2 s end to end |
| Recovered: 15% of credits as the fee, a breached hull patched to 35%, an empty tank topped to 30%, then the pad's own refuel and repair | `Ship.cs` (`RecoveryUpdate`) | the browser's tow fee and patch |
| A warning once the hull is under a quarter | `Ship.cs` (`CheckBreach`) | ported |
| Tutorial lock step: press Q on a copper rock, as the browser's | `Assets/Scripts/Tutorial.cs` | ported |

The smoke run locks the tutorial rock with Q (what the mouse would do), runs dry off the mouth after the second
departure, calls for recovery and prints the fee and the fuel once the ship is back on the pad.

Not ported: the tug model, its beam and its flight; the free look while disabled.

## Milestone 9 — the HUD and the menus

The browser HUD's stylesheet, rebuilt in UGUI: `Assets/Scripts/Ui.cs` holds the palette, the three type families the
browser loads from Google Fonts (Chakra Petch, IBM Plex Sans, IBM Plex Mono, bundled under `Assets/Resources/Fonts`,
Open Font Licence) and the custom controls, each a small `MaskableGraphic` that draws one piece of the CSS;
`Assets/Scripts/Hud.cs` is the layout; `Assets/Scripts/Menu.cs` is the start and pause menu.

| Piece | Where | Status |
|---|---|---|
| Chamfered glass panes with cyan corner brackets, segmented glowing gauges, amber chamfered buttons, key chips, glowing mono readings, dim links that turn red | `Ui.cs` (`Pane`, `SegBar`, `Gauge`, `Face`/`Btn`, `Chip`, `Link`, `Box`) | rebuilt from the CSS (.pane, .bar, .btn, kbd, .link) |
| Status pane bottom-centre (hull, fuel, big speed, thrust, cargo), readouts top-right (zone, speed, cargo ship, field; laser, range, radar), target pane top-centre (name, size, range, health, warning) | `Hud.cs` (`BuildStatus`, `BuildReadouts`, `BuildTarget`) | rebuilt to the browser's layout |
| Boresight brackets on the rock under the nose (amber while cutting), the cargo ship's diamond marker with an edge arrow when off screen, the nearest field's dashed marker, a marker on every collector drone, radar blips in the ore's colour with name-and-range labels for the nearest four | `Ui.cs` (`Reticle`, `Marker`, `Blips`), `Hud.cs` (`PlaceMarker`, `UpdateHud`), `Belt.cs` (`FieldAt`, `NearestField`) | ported |
| The hint bar above the status pane (approach control, auto-dock, hold to mine, cutting…), the cargo-full notice, toasts with an amber or red edge fading in and out, the vignette, the red flash on a hull knock, the letterbox bars and caption for cutscenes, the version tag | `Hud.cs`, `Ui.cs` (`Vignette`) | ported |
| Flight controls list bottom-left with key chips (C hides it, remembered in the save) | `Hud.cs` (`BuildControls`), `GameState.cs` (`controlsShown`) | rebuilt |
| Cargo ship services: a glass side panel on the right with balance, gauges, the market table at the Hub with per-ore sell links and today's prices, the hold, refit rows with level pips and price buttons, Depart / Warp to the Hub / Hide (F) and Reset save (click twice) | `Hud.cs` (`BuildServices`, `RefreshServices`, `Market`) | rebuilt to #station; the body scrolls |
| Inventory: a glass side panel on the left with credits, the hold's slot grid (ore colour along the top, ✕ jettisons) and the storage grid while docked | `Hud.cs` (`BuildInventory`, `RefreshInventory`, `Slot`), `GameState.cs` (`Stacks`) | rebuilt to #inv |
| Drag and drop: a hold stack dragged onto the storage grid is stowed, a storage stack dragged onto the hold grid comes back aboard, a hold stack let go anywhere else is jettisoned (it drifts off behind the ship and cannot be scooped up for a minute); a double-click moves a stack across too; the slots that would take the stack light up amber | `Hud.cs` (`Slot`, `BeginDrag`, `EndDrag`, `Jettison`), `GameState.cs` (`StowStack`, `TakeStack`, `Jettison`) | ported from wireInventoryDrag / stowStack / takeStack / jettisonSlot with UGUI's drag handlers |
| Nav computer: the chart drawn as the browser's SVG (grid, dashed lanes with distances, zone nodes, click to pick), the picked zone's details and the warp button | `Ui.cs` (`Chart`), `Hud.cs` (`BuildMap`, `RefreshMap`) | rebuilt |
| Tutorial card with the pulsing amber rings round the HUD pieces each step talks about | `Hud.cs` (`BuildTutorial`, `ShowTutorial`), `Ui.cs` (`Rings`), `Tutorial.cs` (`ring`, `ring2`) | ported |
| Start menu at launch, pause menu on Escape: Continue / Resume, New game (click twice to wipe), Controls, Settings (sound, volume, music, music volume, HUD size, tutorial restart, wipe save), Quit | `Menu.cs`, `Game.cs` (`WipeSave`, `ApplySetting`) | rebuilt to #intro; settings saved with the game (`hud`, `controls`; the music rows came with milestone 17) |

The smoke run now also captures the inventory and the three menu pages, and drives the drag and drop the way the
pointer would (storage to hold, hold to storage, a stack let go outside the grids). Escape closes the pause menu first,
then the nav map, then the inventory, and only then pauses.

Everything the browser HUD shows is in now (the hover readout, the Q lock and the recovery status came with
milestone 12, the music settings with milestone 17).

## Milestone 8 — lighting and the sky

| Piece | Where | Status |
|---|---|---|
| The sky: a nebula of two noise fields with a dust band round the ecliptic, hashed stars, and the sun as a hard HDR disc with an optical glare, as a skybox shader; the reflection map is baked from it with a gentler disc so the metal ore veins do not mirror a white blob | `Assets/Resources/Shaders/Sky.shader`, `Assets/Scripts/Lighting.cs` | ported from the Godot port sky shader |
| The finish: ACES tone mapping at the zone exposure and a glow from everything above the HDR threshold (the sun, the emissive strips, the engines, the beam), as an image effect on the camera | `Assets/Resources/Shaders/Post.shader`, `Lighting.Post` | the Godot environment glow and tonemap, done by hand (no post-processing package) |
| The sun per zone (colour, strength, disc size, exposure from Astra profiles), the faint ambient, sky reflections on the hulls, one soft shadow box round the ship growing near the carrier | `Assets/Scripts/Lighting.cs` | ported |
| The flashlight: a spot light under the nose, on by default, F toggles it in flight | `Assets/Scripts/Ship.cs` (`BuildTorch`) | ported; Unity spot falloff is not the browser 1/d, so the range and strength are chosen by eye |

## Milestone 6 — the cargo ship dish and the collector drones

| Piece | Where | Status |
|---|---|---|
| The cargo ship upgrades: the mast dish (three levels of reach and rate) and the collector drones (one to three ships with a hold, a speed and a range), bought from the services panel, saved as `depot` with the drones' tally `droneUnits` | `Assets/Scripts/Data.cs` (`DEPOT_UPGRADES`), `Assets/Scripts/GameState.cs` (`BuyDepot`), `Assets/Scripts/Hud.cs` | ported |
| The dish: the model's own yaw and pitch rig slewed at 0.45 rad/s onto the nearest ore rock its level can open, only where the beam clears the hull and within the pitch limits; it fires once both axes are within a degree or so, cuts the rock and leaves the ore adrift; its breaks are announced every 20 s at most | `Assets/Scripts/CargoShip.cs` (`TickDish`, `TurretAngles`, `MuzzleLocal`, `InArc`) | ported; the smoke run checks the rig maths against the model's focus node (error 0) |
| The drones: stubby cargo drones at their docks off mouth 1, out to the nearest unclaimed lump in range, home in through the nearest mouth, down the lane to the drop-off pad, a pause to unload into the storage, out the far mouth; each claims its lump | `Assets/Scripts/Drones.cs` | ported from makeDrone / updateCollectors |
| The smoke run grants both upgrades, prints the dish state while cutting, drops a lump of iron off the mouth while docked and waits for the drone to stow it | `Assets/Scripts/Game.cs` | |

## Milestone 5 — the tutorial and the voice lines

| Piece | Where | Status |
|---|---|---|
| The Flight Ops questline: fourteen steps (launch, the stick, the HUD, the radar, a copper rock under the nose, cutting it, the hold, heading home, the pad, stowing, refits, the Hub, departing, done); steps with a wait watch for the deed, the rest take Next (Enter); Replay and Skip; progress saved as `tut` | `Assets/Scripts/Tutorial.cs`, `Assets/Scripts/Hud.cs` (`BuildTutorial`, `ShowTutorial`) | ported from TUT; the highlight rings came with milestone 9 |
| The voice: every step spoken by its recording (`Resources/Sfx/tut_*`), approach control's five radio calls, the hangar deck's four intercom announcements, colony control, the jump's warp-ready call; radio lines open with a squelch burst and close with one, intercom lines get the PA chime and a tannoy chain (high-pass, low-pass, overdrive, hangar reverb) | `Assets/Scripts/Audio.cs` | ported from SFX via the Godot port; the 53 ElevenLabs recordings copied in |
| Sound effects: dock, stow, cash, chime, pickup, rock break, hit, radar ping, warp charge and jump, laser on/off/bite; the loops (engine idle, thrust with pitch, boost, retros, laser beam and cut, space hum) faded toward per-frame targets | `Assets/Scripts/Audio.cs` (`Engine`, `Laser`, `Sfx`) | ported |
| The inventory (Tab or I): the hold's stacks and the storage | `Assets/Scripts/Hud.cs` (`ToggleInventory`) | replaced by the slot grid of milestone 9 |
| The smoke run drives the questline, pressing Next where it waits, and prints every step and the play counts | `Assets/Scripts/Game.cs` (`SmokeTutorial`) | |

## Milestone 4 — Astra's models

| Piece | Where | Status |
|---|---|---|
| Astra's GLBs (rocks LOD 1 and 2, the carrier, the player ship, Ferron, the homeworld, the garden habitat colony; ~140 MB) imported by the Unity glTFast package from `Assets/Resources/Models` | `Packages/manifest.json` (`com.unity.cloud.gltfast`), `Assets/Editor/Packages.cs`, `Assets/Editor/Inspect.cs` | in |
| Rocks from the library: 28 shape variants at unit radius, each with a regolith surface and an ore-vein surface; the vein takes the instance colour (the ore's colour); the rock shader now carries the albedo, normal and metal-roughness maps copied from the glTF materials | `Assets/Scripts/Belt.cs` (`LoadLibrary`, `ConvertMaterial`), `Assets/Resources/Shaders/Rock.shader` | ported from the Godot port's `_convert_material` |
| The player ship at SHIP_SCALE with its engine glows lit by the throttle and the beam leaving the dish's focus node | `Assets/Scripts/Ship.cs` (`Build`) | ported |
| The carrier model with its anchors (mouths, pads, dish mount, engines, drone docks), turned so the nose is at +X (glTFast mirrors X on import), the warm hangar lamps and engine glows | `Assets/Scripts/CargoShip.cs` (`Build`) | ported |
| The planets as Astra's unit spheres scaled to the radius; the colony as the garden habitat at x1000 with the rings turning | `Assets/Scripts/Game.cs` (`LoadZone`), `Assets/Scripts/Colony.cs` (`Build`) | ported |

Every placeholder from milestones 1 to 3 stays in the code as the fallback when a model is missing.

## Milestone 3 — the Hub, the colony, the market, the warp

| Piece | Where | Status |
|---|---|---|
| The Hub zone: Meridian Colony at the origin, no belts and no central gravity, the homeworld hanging below the colony lanes, its own sun and sky colour | `Assets/Scripts/Data.cs` (`ZONE_HUB`), `Assets/Scripts/Game.cs` (`LoadZone`) | ported |
| Meridian Colony: two habitat rings with modules, beacons and pylons, six spokes with lift cars, the hub sphere and core, pads and docking arms, the comms dish, four solar wings, the cargo terminals; the ring assembly turns, the beacons cycle | `Assets/Scripts/Colony.cs` | simplified port of buildColonyAt (plain meshes, the same proportions) |
| Holding station: the carrier parked off the colony, drifting gently, the market and services open; the slow swing camera | `Assets/Scripts/Ship.cs` (`EnterBerth`, `HoldingCamera`), `CargoShip.hold` | ported |
| The arrival flight: from deep space behind the holding point, wide past the outer ring, onto station nose toward the hub; the exterior camera | `Assets/Scripts/Ship.cs` (`StartHoldApproach`, the `hold` cut) | ported |
| The warp: docked only, a fade to black while the zone swaps underneath, then the arrival (the Hub flight, or a belt pad facing the planet); Space skips | `Assets/Scripts/Ship.cs` (`StartWarp`, `WarpUpdate`, `WarpFade`), `Game.WarpLoad` / `WarpDone` | the simple fade version; the hyperspace tunnel comes later |
| The market: prices drifting every 90 s, exclusive ores +50%, sell everything / hold / storage, refuel the cargo ship supply, restock repair parts, prices today | `Assets/Scripts/GameState.cs` (`Sell`, `RefuelCargoShip`, `BuyParts`), `Assets/Scripts/Hud.cs` (`RefreshMarket`) | ported; shown in the refits place while holding station |
| The nav map (N): the charted zones, distance in light-years, the fuel supply, Jump; the services panel offers Warp to the Hub in a belt and Nav map at the Hub | `Assets/Scripts/Hud.cs` (`BuildMap`, `RefreshMap`) | rebuilt, plainer than the browser chart |
| The smoke run continues: dock again, jump to the Hub, arrive, sell and refuel, jump home, land on the pad; thirteen screenshots | `Assets/Scripts/Game.cs` (`SmokeStep`) | |

## Milestone 2 — the cargo ship, the hangar, approach control, the pad

| Piece | Where | Status |
|---|---|---|
| The cargo ship: the carrier with the through-hangar, orbiting the planet at 925,000 u and 102 u/s, the ship riding along while docked; placeholder hull of decks, mid-band slabs, tapered bow and stern, engine bells, bridge, window rows, mouth lights, pads | `Assets/Scripts/CargoShip.cs` | ported from DEPOT / STATION / placeDepot via the Godot port |
| Hull collision: the box hull with the prow cone, the hangar corridor clamped to its walls, a hard knock costing plating; flying slowly into a mouth docks the ship | `Assets/Scripts/Ship.cs` (`CarrierContact`), `CargoShip.Collide` | ported from depotCollide |
| Approach control (E within 2,250 m): a Catmull-Rom path in by the nearest mouth, along the deck, to a hover over the far pad, then the settle onto it; the departure taxi out of the pad's own mouth on W or the Depart button; Space skips | `Assets/Scripts/Ship.cs` (`StartApproach`, `StartDeparture`, `CutUpdate`) | ported |
| The pad: fuel from the cargo ship's supply, hull mended from its repair parts, E deposits the hold into the 50-slot storage, Take all, W departs after a release | `Assets/Scripts/Ship.cs` (`DockUpdate`), `Assets/Scripts/GameState.cs` | ported |
| The services panel: docked status, credits, storage / fuel supply / parts, the hold and what is stored, Deposit all / Take all, the nine refits with level, what the next level gives and a buy button, Depart, Hide (F) | `Assets/Scripts/Hud.cs` (`BuildServices`, `RefreshServices`) | rebuilt in UGUI, plainer than the browser's |
| Cameras: a fixed camera by the entry mouth during the approach, a slow walk round the pad while docked | `Assets/Scripts/Ship.cs` (`UpdateCamera`, `HangarCamera`) | ported |
| The smoke run now goes pad → depart → mine → collect → approach → dock → deposit → depart, with eight screenshots | `Assets/Scripts/Game.cs` (`SmokeStep`) | |

Not yet: the cargo ship upgrades (dish, drones); the force fields; the curved hull; Astra's carrier model.

## Milestone 1 — one belt, flight, the laser, ore, HUD, save

| Piece | Where | Status |
|---|---|---|
| Game data: ores, refits and their levels, the belts, the Kessler zone, the readout units (a metre is half a unit) | `Assets/Scripts/Data.cs` | ported from the HTML's tables |
| The belt: three base belts, the ring belt, 7/7/9 charted fields, 30 rich pockets, ~55,000 rocks in four size classes on orbit rails at 28 u/s, deterministic from a seed | `Assets/Scripts/Belt.cs`, `Assets/Scripts/Rng.cs` | ported from makeAsteroid / makeField and the Godot port's belt |
| Rock rendering: the browser's fourteen procedural shape families (seed polyhedra displaced by value noise, grit, craters, a waist), two detail levels, drawn as GPU instances per 250 km chunk with the rail drift computed in the vertex shader | `Assets/Scripts/RockMeshes.cs`, `Assets/Resources/Shaders/Rock.shader` | ported from rockGeometry; the shader mirrors the Godot ROCK_SHADER |
| Flight: mouse yaw and pitch through the steering curve, W/S throttle, X cut, S retros, A/D roll, Shift afterburner, the planet's pull, drag and the speed cap, the zone edge, the chase camera | `Assets/Scripts/Ship.cs` | ported |
| The mining laser: the nose ray with the browser's aiming slack, damage by laser level (and overcharge on G), ore locked behind laser levels, body heat in the shader as a rock weakens, break-up into fragments with loose ore | `Assets/Scripts/Ship.cs`, `Assets/Scripts/Game.cs` (`BreakRock`, `SplitRock`) | ported from breakRock / splitRock |
| Rock collision: a swept sphere along the frame's path, hull damage over 140 u/s, the rock shoved off its rail | `Assets/Scripts/Ship.cs` (`RockContact`) | ported |
| Ore pickups: pulled aboard within 900 u, stacked into the hold's slots | `Assets/Scripts/Pickup.cs`, `Assets/Scripts/GameState.cs` | ported |
| The radar pulse (R): ore rocks within scanner range, marked as the pulse reaches them | `Assets/Scripts/Ship.cs`, `Assets/Scripts/Belt.cs` (`Scan`, `Marked`) | ported |
| HUD: hull, fuel, speed, thrust, cargo along the bottom; zone, laser, range, radar and credits top right; the target top centre; toasts; the controls list (C); a start/pause menu with Continue, New game (twice to wipe) and Quit | `Assets/Scripts/Hud.cs` | rebuilt in UGUI, plainer than the browser's |
| Floating origin: the world re-centres on the ship every 20,000 u; rocks, planet, pickups and camera shift together | `Assets/Scripts/Game.cs` | ported |
| Save: JSON under `Application.persistentDataPath` with the browser save's field names; F5 quick-saves, autosave every 30 s | `Assets/Scripts/GameState.cs` | ported |
| Lighting: a directional sun from the zone's sun direction, one soft shadow box a few kilometres round the ship (as the browser casts), a dark flat ambient | `Assets/Scripts/Game.cs` (`SetupLighting`) | approximated; Astra's lighting module is not ported yet |

Not yet ported (see the Godot port's README for the full list of what the browser has): the hyperspace tunnel; colony traffic; the rock LOD 0 library up close; the
tutorial and voice; the dish, drones, tow and lock; sparks, scrap, scorches; music and sound.
