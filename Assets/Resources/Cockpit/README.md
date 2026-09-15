# Cockpit console

Implemented in the Unity project only. Runtime UI: `Assets/Scripts/CockpitHud.cs`; integration and overlays: `Hud.cs`.

The console.png housing was generated with the built-in imagegen tool from the user-approved HUD concept. The bitmap contains no readouts or controls: Unity renders the live ship gauges, local scanner, flight director, target scan, cargo, collector drone status, and clickable command rail over the blank glass wells. Retained control keys are N, I, Q/MMB, E, G and R. C toggles a compact flight-controls card; the pause menu keeps the complete controls list.

The console samples telemetry at 10 Hz and draws local contacts from the existing nearby-rock cache (at most 96 rocks). The local scope covers up to 6 km; R still pulses the full range of the fitted scanner. Ore colors appear on the scope only while revealed by a radar pulse. There are no extra scene cameras, lights, or render textures. Target drawings are schematic scan graphics, with the actual target's ore color, range, class and integrity alongside them.

HUD scale is respected within a 43% viewport-height limit. Screen content scales uniformly, including at ultrawide aspect ratios. Menus, docking and cinematics hide the flight console. The chase view eases up to keep the player ship visible above it, and mouse pitch uses the unobscured flight area. World markers and prompts stay above the housing. Clicking the console or its menus does not fire or steer the ship; keyboard commands keep their existing bindings.

Visual check: run `Builds/Windows/BeltRunner.exe -smoke cockpit -screen-fullscreen 0 -screen-width 1600 -screen-height 900`. This fixture is save-free and writes cockpit_*.png under the game's persistent-data directory. Run it in the foreground to allow Unity to capture screenshots. The regular full smoke covers mining, lock switching, docking, drones, refits, combat and warp. Every smoke mode now resolves saves under `Smoke/belt-runner-save.json`, so startup, reset, load and save never target normal pilot progress. The combat fixture explicitly selects the autocannon before holding fire.

## Imagegen prompt

Use case: ui-mockup. Asset type: production game UI console BACKPLATE texture for Belt Runner Unity. Image 1 is a STYLE REFERENCE: the approved dark metal cockpit at the bottom. Create ONLY the console housing filling the entire new wide image, no space scene. Landscape 3:1 aspect ratio, 3072x1024 or similar high resolution. Orthographic straight-on, precisely aligned rectangular display openings for live Unity UI overlays. Continuous edge-to-edge gunmetal console, photoreal brushed graphite metal, worn beveled machined edges, black screws, tiny cooling vents, warm amber rim light along upper edge, restrained cyan screen reflection. Five empty black-glass screen wells. Their horizontal spans as percentage of entire image MUST be: left status x=2% to20%; scanner x=21% to35%; widest center flight x=36% to64%; target x=65% to80%; cargo x=81% to98%. All five screen wells span vertical y=10% to77%, screens perfectly flat-facing, thick beveled metal dividers outside these regions. At y=83% to97% leave one long dark recessed command-strip area from x=23% to78%, with no dividers or buttons within it (runtime buttons will be added). Physical vent grilles, small non-labeled toggle switches and unlettered blank metal identification plates on bottom left and bottom right outside command strip. Realistic physically lit metal with controlled specular highlights, fine subtle scratches. Every screen fully EMPTY, near-black uniform glass with very subtle blue teal edge shading. No UI graphics, no text, no letters, no numerals, no radar, no icons, no readouts, no ship, no rocks, no stars, no hands, no wheel. Entire image is console with no margin and no background. Preserve design language from reference. This will be stretched slightly wider as a full-bottom game HUD. Crisp premium realistic industrial spacecraft hardware.


## Validation — 2026-09-15

- `dotnet build tools/check`: zero warnings and errors.
- Unity Windows player build: succeeded, zero errors.
- Cockpit screenshots inspected at 1600×900, 1024×768, and 2560×1080, including HUD scale 0.7 and 1.6, mining, damage warnings and the inventory overlay.
- Actual command-button callbacks verified: lock release and acquire, overcharge, inventory, nav map and auto-dock all succeeded.
- Full smoke completed mining, radar, combat victory and reward, docking, inventory transfers, refits, drone delivery, recovery, Hub arrival and the return warp to Kessler.
- Test save written under `Smoke/belt-runner-save.json`; no normal pilot save was created by the isolated run.

Local QA images and logs are in the ignored `Logs/cockpit-qa/` directory and `Logs/cockpit-*.log`.
