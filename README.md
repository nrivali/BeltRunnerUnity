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

Built-in render pipeline, legacy Input Manager, UGUI. Nothing to configure.

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

Not yet ported (see the Godot port's README for the full list of what the browser has): the hyperspace tunnel; colony traffic; Astra's rock, ship, planet and carrier models
(the rocks are the browser's own procedural shapes, the ship a placeholder); sky, nebula, stars and sun disc; the
tutorial and voice; the dish, drones, tow and lock; sparks, scrap, scorches; music and sound.
