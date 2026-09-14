# Belt Runner 3D · Unity port

A port of the browser game `belt-runner-3d.html` (repo `BeltRunner`, v0.9.120) to Unity 6. The browser game stays the
reference and is never edited from here; the Godot port (`BeltRunnerGodot`) is a faithful transcription of the same
game and is the second reference. See `AGENTS.md` for the rules.

## Running it

1. Install Unity Hub and a Unity 6 editor (6000.0 LTS; the project asks for 6000.0.58f1 and any 6000.0.x will do).
2. Add this folder as a project in the Hub and open it. The first open imports the two packages in
   `Packages/manifest.json` (UGUI and the built-in modules) and generates `ProjectSettings` and `Library`.
3. Press Play in any scene, including the empty default one. Everything is built from code at start-up
   (`Game.Boot`): there is no scene content, no prefab and no editor-only asset.

Built-in render pipeline, legacy Input Manager, UGUI. Nothing to configure.

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

Launch a player build (or the editor) with `-smoke` on the command line: it starts without the menu, cuts the nearest
copper rock, flies, saves, and quits, printing `smoke:` lines to the log and saving `smoke_launch.png`,
`smoke_mine.png` and `smoke_flight.png` under `Application.persistentDataPath`.

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

Not yet ported (see the Godot port's README for the full list of what the browser has): the cargo ship, hangar,
approach control and the pad; the Hub, colony, market and warp; Astra's rock, ship, planet and carrier models
(the rocks are the browser's own procedural shapes, the ship a placeholder); sky, nebula, stars and sun disc; the
tutorial and voice; the dish, drones, tow and lock; sparks, scrap, scorches; music and sound.
