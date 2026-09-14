# Belt Runner 3D · Unity port

Rules for anyone (person or agent) working here:

- This is a port of `belt-runner-3d.html` in the sibling repo `BeltRunner`. The browser game is the reference and is never
  edited from here. Port behaviour and numbers from it (or from the Godot port in `BeltRunnerGodot`, which is a faithful
  transcription) rather than redesigning.
- Units are the browser's world units; a readout metre is half a unit (`Data.METRE`).
- The whole world is built from code at start-up (`Game.Boot`): no scene content, no prefabs, no editor-only assets, so
  the project opens in any Unity 6 editor and plays from an empty scene.
- Keep the belt deterministic from its seed (`Rng`), for multiplayer later.
- Built-in render pipeline, legacy Input Manager, UGUI. No packages beyond `com.unity.ugui`.
- `-smoke` on the command line runs an unattended check that prints `smoke:` lines and saves screenshots under
  `Application.persistentDataPath`.
- `tools/check` compiles every script against a stub of the Unity API with the .NET SDK, for when no editor is to hand;
  it proves syntax and types, not behaviour. Run the editor before claiming anything works.
