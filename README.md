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
| Start menu at launch, pause menu on Escape: Continue / Resume, New game (click twice to wipe), Controls, Settings (sound, volume, HUD size, tutorial restart, wipe save), Quit | `Menu.cs`, `Game.cs` (`WipeSave`, `ApplySetting`) | rebuilt to #intro; settings saved with the game (`hud`, `controls`); no music rows yet, the music engine is not ported |

The smoke run now also captures the inventory and the three menu pages, and drives the drag and drop the way the
pointer would (storage to hold, hold to storage, a stack let go outside the grids). Escape closes the pause menu first,
then the nav map, then the inventory, and only then pauses.

Still to come from the browser HUD: the hover readout beside the cursor and the Q lock (milestone 12), the tow status,
and the music settings.

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
