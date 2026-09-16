using System;
using System.Collections.Generic;
using UnityEngine;

/// The game: builds the whole world from code when the player starts (no scene content needed), runs the frame
/// loop, the floating origin, saving, the menu, and the unattended `-smoke` run.
public class Game : MonoBehaviour
{
    public const float SHIFT_AT = 20000f;
    public const ulong SEED = 7;

    public Vector3 worldOffset;
    public Data.Zone zone = Data.ZONE_KESSLER;
    public Belt belt;
    public Ship ship;
    public CargoShip carrier;
    public Colony colony;
    public Hud hud;
    public Tutorial tutorial;
    public Drones drones;
    public Sparks sparks;
    public Explosions explosions;
    public Raiders raiders;
    float _dishToastT = -100f;
    public List<Pickup> Drops { get { return _drops; } }

    public void RemoveDrop(Pickup p)
    {
        _drops.Remove(p);
        Destroy(p.gameObject);
    }
    public Camera cam;
    public Light sun;
    public Lighting lighting;
    public bool started, paused;

    Transform _pickups;
    GameObject _planet;
    Vector3 _planetTrue;
    const float PLANET_GAIN = 0.5f;   // the planet's surface reflectance scale: 50% brightness (the user's call, 2026-09-15)
    float _cullT, _saveT;
    readonly List<Pickup> _drops = new List<Pickup>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<Game>() != null) return;
        new GameObject("Game").AddComponent<Game>();
    }

    void Awake()
    {
        State.Init();
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-smoke" || args[i] == "--smoke")
            {
                _smoke = true;
                // a chapter on its own: `-smoke menus` (the next token names it)
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) _smokeOnly = args[i + 1];
            }
            if (args[i] == "-combat" || args[i] == "--combat") _combat = true;
            if ((args[i] == "-gun" || args[i] == "--gun") && i + 1 < args.Length) int.TryParse(args[i + 1], out _combatGun);
            if ((args[i] == "-rocket" || args[i] == "--rocket") && i + 1 < args.Length) int.TryParse(args[i + 1], out _combatRocket);
            if ((args[i] == "-face" || args[i] == "--face") && i + 1 < args.Length) _combatFace = args[i + 1];   // sun | planet | rock: the sandbox ship turned to face it (lighting checks)
            if ((args[i] == "-torch" || args[i] == "--torch") && i + 1 < args.Length) _combatTorch = args[i + 1] != "0";   // the flashlight on or off in the sandbox
            if (args[i] == "-torchpoint") _combatTorchPoint = true;   // a diagnostic: the flashlight as a point light at the nose, to see what it lights
            if (args[i] == "-rockmin" && i + 1 < args.Length) int.TryParse(args[i + 1], out _combatRockMin);   // -face rock: only a rock of at least this radius (u), so a big one fills the frame
            if (args[i] == "-shader" && i + 1 < args.Length) _combatShader = args[i + 1];   // -face rock: only a rock whose shader name contains this ("Sculpt" for the sculpted collection, "Rock" for the belt's own)
            if (args[i] == "-side" && i + 1 < args.Length) _combatSide = args[i + 1];   // -face rock: "sun" the ship on the rock's lit side, "dark" on its night side (only the flashlight lights it), 30 deg off the sun so the sun stays out of the frame
        }
        if (_combat)
        {
            // a sandbox loadout for testing fights: the autocannon fitted, credits to refit, hull and tank full, no tutorial;
            // nothing this session does reaches the save file
            State.sandbox = true;
            State.tut = -1;
            State.up["gun"] = Mathf.Clamp(Mathf.Max(State.up["gun"], _combatGun), 0, Data.UPGRADES["gun"].costs.Length);
            State.up["rocket"] = Mathf.Clamp(Mathf.Max(State.up["rocket"], _combatRocket), 0, Data.UPGRADES["rocket"].costs.Length);
            State.credits = Mathf.Max(State.credits, 5000f);
            State.hull = State.Stat("hull").hp;
            State.fuel = State.Stat("tank").cap;
        }
        if (_smoke) { State.Reset(); State.controlsShown = true; State.hudScale = 1f; State.tut = 0; State.depot["laser"] = 1; State.depot["collectors"] = 1; }   // a fresh pilot every time, questline and all; the cargo ship upgrades, so the dish and a drone get exercised
        var t0 = Time.realtimeSinceStartup;
        SetupCamera();
        lighting = new Lighting();
        lighting.Setup(cam);
        sun = lighting.sun;
        belt = new Belt();
        sparks = new Sparks();
        explosions = new Explosions(this);
        raiders = new Raiders(this);
        _pickups = new GameObject("Pickups").transform;
        var cgo = new GameObject("CargoShip");
        carrier = cgo.AddComponent<CargoShip>();
        carrier.game = this;
        carrier.Build();
        var shipGo = new GameObject("Ship");
        ship = shipGo.AddComponent<Ship>();
        ship.game = this;
        ship.belt = belt;
        ship.carrier = carrier;
        ship.cam = cam;
        ship.Build();
        Audio.Create();
        Music.Create();
        var hudGo = new GameObject("HUD");
        hud = hudGo.AddComponent<Hud>();
        hud.instantUi = _smoke;   // the smoke run: the hangar window shows at once, no ease, so its screenshots catch it
        if (!_smoke) ApplyDisplay();   // the saved display mode (the smoke runs keep the window the command line gave them)
        hud.ship = ship;
        hud.game = this;
        hud.Build();
        tutorial = new Tutorial { game = this, ship = ship, hud = hud };
        hud.tutorial = tutorial;
        drones = new Drones { game = this, carrier = carrier };
        hud.menu.onStart = StartGame;
        hud.menu.onResume = Resume;
        hud.menu.onNewGame = NewGame;
        hud.menu.onWipe = WipeSave;
        hud.menu.onTutorialRestart = () => { tutorial.Restart(); hud.Toast("Tutorial restarted", false); };
        hud.menu.onSetting = ApplySetting;
        hud.menu.onQuit = Quit;
        // the comm-channel toasts for the soundtrack
        Music.I.onGroove = n => { if (started) hud.Toast("♪ " + n + " · groove on the comm channel", false); };
        Music.I.onTrack = n => { if (started) hud.Toast("♪ Now drifting: " + n, false); };
        Music.I.onCombat = n => { if (started) hud.Toast("♪ " + n + " · combat", false); };
        LoadZone(Data.ZoneById(_smoke ? "kessler" : State.zoneId));
        SpawnInZone();
        ship.UpdateCamera(1f);
        Debug.Log("belt: " + belt.count + " rocks in " + belt.ChunkCount + " chunks, built in " + Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " ms");
        if (_smoke)
        {
            ship.mouseSteer = false;
            StartGame();
        }
        else if (_combat)
        {
            StartGame();
            JumpToHold();
            ship.torchOn = _combatTorch;
            if (_combatTorchPoint && ship.torch != null) { ship.torch.type = LightType.Point; Debug.Log("combat test: torch as a point light"); }
            if (_combatFace == "sun" || _combatFace == "planet" || _combatFace == "rock")
            {
                var d = _combatFace == "sun" ? zone.sunDir.normalized : (_planetTrue - ship.TruePos).normalized;
                if (_combatFace == "rock")
                {
                    // the nearest rock within 40,000 u, and the ship set 400 u off it, so the flashlight can be judged
                    int best = -1; float bd = float.PositiveInfinity;
                    foreach (int ri in belt.RocksWithin(ship.TruePos, 40000f)) { float dd = (belt.RockPos(ri) - ship.TruePos).magnitude; if (dd < bd && belt.radius[ri] < 400f && belt.radius[ri] >= _combatRockMin && (_combatShader == "" || belt.ShaderNameOf(ri).Contains(_combatShader))) { bd = dd; best = ri; } }
                    if (best >= 0)
                    {
                        var rp = belt.RockPos(best);
                        d = (rp - ship.TruePos).normalized;
                        if (_combatSide == "sun" || _combatSide == "dark")
                        {
                            var sd = zone.sunDir.normalized;   // light travels along -sunDir, so the lit face points +sunDir
                            var axis = Vector3.Cross(sd, Vector3.up); if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right; axis.Normalize();
                            d = Quaternion.AngleAxis(30f, axis) * (_combatSide == "dark" ? sd : -sd);   // face the dark (or lit) side; the standoff line below sets the position
                        }
                        ship.transform.position = rp - d * (belt.radius[best] + 400f) - worldOffset;
                        Debug.Log("combat test: facing rock " + best + " r=" + belt.radius[best].ToString("0") + " (" + belt.ShaderNameOf(best) + ") at 400 u · torch " + ship.torchOn
                            + (ship.torch != null ? " · torch forward·ship forward " + Vector3.Dot(ship.torch.transform.forward, ship.Forward).ToString("0.00") + " · torch local " + ship.torch.transform.localPosition + " parent " + ship.torch.transform.parent.name + " scale " + ship.torch.transform.lossyScale : ""));
                    }
                }
                ship.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
                ship.mouseSteer = false;   // or the pointer, wherever it sits, would swing the nose off it
                ship.UpdateCamera(1f);
            }
            hud.Toast("Combat test · sandbox, nothing is saved · F9 spawns a fresh three", false);
        }
        else
        {
            paused = true;
            hud.ShowMenu(true, false);
        }
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = zone.bg;
        cam.nearClipPlane = 2f;
        cam.farClipPlane = 4000000f;
        cam.fieldOfView = 62f;
        cam.allowHDR = true;
    }

    void LoadZone(Data.Zone z)
    {
        zone = z;
        State.zoneId = z.id;
        hud.zone = z;
        foreach (var p in _drops) if (p != null) Destroy(p.gameObject);
        _drops.Clear();
        ship.nearRocks = new List<int>();
        if (drones != null) drones.Reset();
        carrier.dishRock = -1;
        carrier.dishFiring = false;
        sparks.Clear();
        belt.Clear();
        belt.Build(z, z.id == "kessler" ? SEED : SEED + 11);
        raiders.Build(z, belt);
        if (colony != null) { Destroy(colony.gameObject); colony = null; }
        if (z.hub)
        {
            colony = new GameObject("Colony").AddComponent<Colony>();
            colony.Build();
        }
        cam.backgroundColor = z.bg;
        lighting.SetZone(z);
        float r = z.planetR * Data.PLANET_SCALE;
        _planetTrue = z.planetPos;
        if (_planet != null) Destroy(_planet);
        // Astra's planets are unit spheres (Ferron's terrain; Meridian's oceans and a cloud layer) scaled to the planet's radius
        var pp = Resources.Load<GameObject>(z.planetName == "Ferron" ? "Models/ferron" : "Models/homeworld");
        if (pp != null)
        {
            _planet = Instantiate(pp);
            _planet.name = "Planet " + z.planetName;
            _planet.transform.localScale = Vector3.one * r;
            foreach (var mr in _planet.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // the surface sits a step down under this sun, so the day side keeps its detail and the limb glow reads
                if (z.planetName != "Ferron") foreach (var pm in mr.materials) if (pm.HasProperty("_Color")) pm.color = pm.color * PLANET_GAIN;
            }
            if (z.planetName == "Ferron")
            {
                DryPlanet.Configure(_planet);
                // the surface at half brightness (the user's call, 2026-09-15), on a copy so the shared asset is untouched
                foreach (var mr in _planet.GetComponentsInChildren<MeshRenderer>(true)) { var pm = mr.material; if (pm.HasProperty("_Color")) pm.SetColor("_Color", pm.GetColor("_Color") * PLANET_GAIN); }
            }
            Atmosphere(_planet.transform, r, z);
            BuildSpecks(_planet.transform, r);
        }
        else
        {
            _planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _planet.name = "Planet " + z.planetName;
            Destroy(_planet.GetComponent<Collider>());
            _planet.transform.localScale = Vector3.one * r * 2f;
            var pm = new Material(Game.Sh("Standard"));
            pm.color = z.tint;
            pm.SetFloat("_Glossiness", z.central ? 0.05f : 0.45f);
            _planet.GetComponent<MeshRenderer>().sharedMaterial = pm;
            _planet.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// The belt's far rocks as specks (BeltRunner/Specks): one small camera-facing quad per rock, built once from the
    /// rocks' own positions relative to the planet, a child of the planet, so the belt reads as rock all the way round
    /// it past the rocks' draw distance. Nothing at the Hub, which has no belt.
    void BuildSpecks(Transform planet, float r)
    {
        var sh = Shader.Find("BeltRunner/Specks");
        if (sh == null || belt == null || belt.count == 0) return;
        var verts = new List<Vector3>();
        var uv = new List<Vector2>();
        var uv1 = new List<Vector2>();
        var tris = new List<int>();
        Vector2[] corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        for (int i = 0; i < belt.count; i++)
        {
            if (!belt.alive[i]) continue;
            var p = (belt.RockPos(i) - _planetTrue) / r;
            int b = verts.Count;
            for (int c = 0; c < 4; c++) { verts.Add(p); uv.Add(corners[c]); uv1.Add(new Vector2(belt.radius[i], 0f)); }
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }
        var mesh = new Mesh();
        mesh.name = "Specks";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        var go = new GameObject("Specks");
        go.transform.SetParent(planet, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var m = new Material(sh);
        m.SetFloat("_PlanetR", r);
        m.SetFloat("_Scale", r);
        m.SetFloat("_NearFade", Belt.DRAW_DIST * 0.7f);
        m.SetFloat("_FarFade", Belt.DRAW_DIST * 1.1f);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Debug.Log("specks: " + (verts.Count / 4) + " far rocks");
    }

    /// The planet's atmosphere: an additive fresnel shell 3.5% bigger than the planet (BeltRunner/Atmo), in the planet's
    /// tint pulled toward a pale haze, brightest at the sunlit limb.
    void Atmosphere(Transform planet, float r, Data.Zone z)
    {
        if (z.planetName == "Ferron") { DryPlanet.AddAtmosphere(planet); return; }
        var sh = Shader.Find("BeltRunner/Atmo");
        if (sh == null) return;
        var a = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        a.name = "Atmosphere";
        Destroy(a.GetComponent<Collider>());
        a.transform.SetParent(planet, false);
        a.transform.localScale = Vector3.one * 2f * 1.05f;   // the unit sphere is 1 across; the planet model is unit radius
        var m = new Material(sh);
        m.SetColor("_Color", Color.Lerp(z.tint, new Color(0.7f, 0.82f, 1f), 0.6f));
        m.SetFloat("_Power", 2.6f);
        m.SetFloat("_Gain", 2.6f);
        var mr = a.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// Place the carrier and the ship for the zone: on the pad in the dock that faces the planet in a belt; at the Hub, at
    /// the holding point (or, `arriving`, out in deep space where the arrival flight starts).
    void SpawnInZone(bool arriving = false)
    {
        if (zone.hub)
        {
            carrier.hold = true;
            var p = Data.HOLD_PARK;
            if (arriving)
            {
                var start = p - Ship.HoldFwd() * 170000f + Ship.HoldSide() * 70000f + new Vector3(0, 26000, 0);
                worldOffset = start;
                carrier.SetPose(start, CargoShip.HeadingAlong(p - start));
                ship.transform.position = Vector3.zero;
                ship.docked = false;
                ship.hold = false;
                ship.transform.rotation = Ship.LevelHeading(carrier.Nose);
                ApplyOffsets();
            }
            else
            {
                worldOffset = p;
                carrier.SetPose(p, CargoShip.HeadingAlong(Data.HOLD_DIR));
                ship.transform.position = Vector3.zero;
                ship.transform.rotation = Ship.LevelHeading(carrier.Nose);
                ApplyOffsets();
                ship.EnterBerth();
            }
            return;
        }
        carrier.hold = false;
        carrier.ang = _smoke ? Mathf.PI / 2f : UnityEngine.Random.value * Mathf.PI * 2f;
        worldOffset = Vector3.zero;
        carrier.Place();
        int side = carrier.PlanetSide();
        worldOffset = carrier.ToTrue(CargoShip.ParkLocal(side));
        ship.transform.position = Vector3.zero;
        ship.transform.rotation = Ship.LevelHeading(carrier.Dir(CargoShip.FaceLocal(side)));
        ship.vel = Vector3.zero;
        ApplyOffsets();
        ship.EnterHangar(side);
    }

    void ApplyOffsets()
    {
        belt.ApplyOffset(worldOffset);
        _planet.transform.position = _planetTrue - worldOffset;
        if (colony != null) colony.transform.position = -worldOffset;
        carrier.Place();
        belt.Cull(ship.TruePos);
    }

    public void WarpLoad(Data.Zone z)
    {
        LoadZone(z);
        SpawnInZone(true);
        ship.UpdateCamera(1f);
    }

    /// The jump is over: the Hub arrival flight starts; in a belt the ship is already on its pad.
    public void WarpDone(Data.Zone z)
    {
        if (z.hub) ship.StartHoldApproach();
        else hud.Toast("Arrived · " + z.name, false);
    }

    public void OpenMap()
    {
        hud.OpenMap();
    }

    /// The ship docked or left: the services panel follows.
    public void OnDocked(bool isDocked)
    {
        hud.OnDocked(isDocked);
    }

    // ---- start, pause, resume, new game (the browser's startGame / pauseGame / resumeGame / newGame / resetSave)
    void StartGame()
    {
        started = true;
        paused = false;
        hud.ShowMenu(false, false);
        if (!_smoke) hud.Toast("Kessler Belt · " + belt.count + " rocks charted · R pulses the radar", false);
    }

    void Pause()
    {
        if (!started || paused) return;
        paused = true;
        hud.ShowMenu(true, true);
    }

    void Resume()
    {
        paused = false;
        hud.ShowMenu(false, false);
    }

    void NewGame()
    {
        State.Reset();
        LoadZone(Data.ZONE_KESSLER);
        SpawnInZone();
        ship.throttle = 0f;
        ship.UpdateCamera(1f);
        StartGame();
        hud.Toast("New pilot · saved game wiped", false);
    }

    /// Wipe the save (Settings, or the services panel's Reset save): a fresh pilot, back at the start menu.
    public void WipeSave()
    {
        State.Reset();
        LoadZone(Data.ZONE_KESSLER);
        SpawnInZone();
        ship.throttle = 0f;
        ship.UpdateCamera(1f);
        started = false;
        paused = true;
        hud.ShowMenu(true, false);
        hud.Toast("Saved game wiped", false);
    }

    /// A setting changed in the menu: sound and volume go to the mixer, the HUD size to the canvas; all are saved.
    void ApplySetting(string key, float v)
    {
        if (key == "sound") State.soundOn = v > 0.5f;
        else if (key == "volume") State.volume = Mathf.Clamp01(v);
        else if (key == "hud") { State.hudScale = Mathf.Clamp(v, 0.7f, 1.6f); hud.SetScale(State.hudScale); }
        else if (key == "music") State.musicOn = v > 0.5f;
        else if (key == "music_volume") State.musicVolume = Mathf.Clamp01(v);
        else if (key == "brightness") State.brightness = Mathf.Clamp(v, 0.5f, 1.5f);   // the post pass reads it every frame
        else if (key == "display") { State.display = Mathf.Clamp(Mathf.RoundToInt(v), 0, 2); ApplyDisplay(); }
        if (Audio.I != null) Audio.I.ApplySettings();
        if (Music.I != null) Music.I.ApplySettings();
        State.Save();
    }

    /// The display setting: full screen at the desktop resolution (exclusive, or a borderless window), or a 1280x720
    /// window. The desktop resolution is what Screen.currentResolution reports from a window, so it is read there.
    public void ApplyDisplay()
    {
        var r = Screen.currentResolution;
        int w = r.width, h = r.height;
        switch (State.display)
        {
            case 0: Screen.SetResolution(w, h, FullScreenMode.ExclusiveFullScreen); break;
            case 1: Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow); break;
            default: Screen.SetResolution(1280, 720, FullScreenMode.Windowed); break;
        }
        Debug.Log("display: " + (State.display == 0 ? "full screen" : State.display == 1 ? "borderless" : "windowed") + " · " + w + "x" + h);
    }

    void Quit()
    {
        if (started) State.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Toast(string msg, bool bad)
    {
        hud.Toast(msg, bad);
    }

    // ---- the combat test (-combat, and F9 at any time): the ship set down 1,500 u off a raider hold, facing it, with
    // the autocannon fitted, so a fight starts within a second or two; F9 spawns a fresh three
    bool _combat;
    int _combatGun = 0, _combatRocket = 0;
    string _combatFace = "";
    bool _combatTorch = true, _combatTorchPoint;
    string _combatSide = "", _combatShader = "";
    int _combatRockMin;
    int _combatFrame;

    /// The combat test's fight: every raider in the zone is cleared and three fresh ones are spawned 2,000 to 4,000 m
    /// out (4,000 to 8,000 u) in random directions round the ship, homed on the ship's position so they engage at once.
    /// The ship stays where it is (leaving the hangar first if docked) with the autocannon selected.
    public void JumpToHold()
    {
        if (ship.InCinematic || ship.recovery != null) { hud.Toast("Not during a cutscene", true); return; }
        if (raiders == null) return;
        if (ship.docked) ship.LeaveHangar();
        raiders.respawn = true;  // ten seconds after the last raider dies, a fresh wave
        ship.weapon = "gun";
        ship.ReleaseLock();
        raiders.Clear();
        // clear of the cargo ship's gun cover first (raiders inside SAFE_R die and never engage): if the ship is within it,
        // or too close for raiders spawned up to 8,000 u out to be clear of it, move 22,000 u out along the nose
        if (carrier != null && !carrier.hold && (ship.TruePos - carrier.truePos).magnitude < Raiders.SAFE_R + 9000f)
        {
            var away = ship.Forward; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward; else away.Normalize();
            ship.transform.position = carrier.truePos + away * 22000f - worldOffset;
            ship.transform.rotation = Ship.LevelHeading(away);
            ship.vel = Vector3.zero;
            ship.throttle = 0f;
            ship.exitPending = false;
            ship.UpdateCamera(1f);
        }
        var here = ship.TruePos;
        for (int i = 0; i < 3; i++)
        {
            var dir = UnityEngine.Random.onUnitSphere;
            if (Vector3.Dot(dir, ship.Forward) < -0.2f) dir = -dir;   // mostly ahead of the ship, never straight behind
            raiders.Make(here + dir * UnityEngine.Random.Range(4000f, 8000f), here);
        }
        Debug.Log("combat test: 3 raiders spawned 4,000 to 8,000 u out · gun Lv" + State.up["gun"] + " · at " + here.ToString("0"));
        hud.Toast("Test · 3 raiders inbound, 2,000 to 4,000 m out · autocannon Lv" + State.up["gun"], false);
    }

    /// A shader by name, falling back down a list a build always carries, so a missing one never stops the game.
    public static Shader Sh(string name)
    {
        var s = Shader.Find(name);
        if (s == null) s = Shader.Find("Legacy Shaders/Diffuse");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s == null) Debug.LogWarning("shader " + name + " missing and no fallback found");
        return s;
    }

    // ---- ore
    public Pickup SpawnPickup(string ore, float units, Vector3 at, Vector3 drift)
    {
        var p = Pickup.Make(_pickups, ore, units, at, drift);
        _drops.Add(p);
        return p;
    }

    /// A rock breaks (the browser's breakRock): big rocks break into smaller mineable rocks (colossal → giants → large →
    /// small), a quarter of their ore coming loose at once and the rest riding in the fragments; a small rock's ore all
    /// comes loose. The lumps drift with the rock's orbital velocity.
    public void BreakRock(int i, bool byDish = false)
    {
        string rname = belt.RockName(i);
        int oreI = belt.ore[i];
        int c = belt.cls[i];
        var p = belt.RockPos(i);
        var at = p - worldOffset;
        float r = belt.radius[i];
        var v = belt.RockVel(i);
        int bi = belt.beltOf[i];
        bool splits = c > 0;
        sparks.Burst(p, Mathf.Min(600, 80 + Mathf.RoundToInt(r * 1.2f)), 160f + r * 0.6f, Data.Hex("#ffb060"), 1.5f);
        if ((p - ship.TruePos).magnitude < 60000f) belt.SpawnScrap(i, v);   // the scrap of a break the pilot can see
        float total = belt.Kill(i);
        float loose = splits ? total * 0.25f : total;
        if (_smoke) Debug.Log("smoke: break · " + rname + " total=" + total.ToString("0") + " loose=" + loose.ToString("0") + " splits=" + splits);
        bool near = (p - ship.TruePos).magnitude < 3000f;
        if (near || !byDish) Audio.Play("rock_break", c > 0 ? 0f : -4f);
        if (oreI >= 0 && loose > 0f)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(loose / 40f), 1, 8);
            string oreKey = Data.ORE_KEYS[oreI];
            for (int n = 0; n < k; n++)
            {
                var dir = UnityEngine.Random.onUnitSphere;
                SpawnPickup(oreKey, loose / k, at + dir * r * UnityEngine.Random.Range(0.1f, 0.5f), v + dir * UnityEngine.Random.Range(20f, 60f));
            }
        }
        int parts = 0;
        if (splits) parts = SplitRock(c, r, oreI, total, p, v, bi);
        // the dish works on its own, so its breaks are only announced every 20 s or so; your own always are
        bool quiet = byDish && State.time - _dishToastT < 20f;
        if (byDish && !quiet) _dishToastT = State.time;
        if (quiet) return;
        string who = byDish ? "Cargo ship dish: " : "";
        string oreTxt = oreI >= 0 && loose > 0f ? " · " + Mathf.RoundToInt(loose) + " " + Data.ORES[oreI].name + " loose" : "";
        if (splits) hud.Toast(who + rname + " broken into " + parts + " " + Belt.CLS_NAME[c - 1].ToLowerInvariant() + " rocks" + oreTxt, false);
        else if (oreI >= 0 && loose > 0f) hud.Toast(who + rname + " broken" + oreTxt, false);
        else hud.Toast(who + rname + " broken · scrap only", false);
    }

    /// splitRock: three to five fragments of the next class down, about half of them carrying three quarters of the
    /// parent's ore between them (always at least one), the rest plain stone; directions kept some 70 degrees apart.
    int SplitRock(int c, float r, int oreI, float total, Vector3 p, Vector3 v, int bi)
    {
        int k = 3 + UnityEngine.Random.Range(0, c == 1 ? 3 : 2);
        var carry = new bool[k];
        int nCarry = 0;
        for (int n = 0; n < k; n++) { carry[n] = oreI >= 0 && UnityEngine.Random.value < 0.5f; if (carry[n]) nCarry++; }
        if (oreI >= 0 && nCarry == 0) { carry[UnityEngine.Random.Range(0, k)] = true; nCarry = 1; }
        float share = nCarry > 0 ? total * 0.75f / nCarry : 0f;
        var dirs = new List<Vector3>();
        int tries = 0;
        while (dirs.Count < k && tries < 200)
        {
            tries++;
            var d = UnityEngine.Random.onUnitSphere;
            bool apart = true;
            foreach (var o in dirs) if (Vector3.Dot(o, d) >= 0.35f) { apart = false; break; }
            if (apart) dirs.Add(d);
        }
        while (dirs.Count < k) dirs.Add(UnityEngine.Random.onUnitSphere);
        float size0 = bi < belt.belts.Count ? belt.belts[bi].size0 : 16f;
        for (int n = 0; n < k; n++)
        {
            float fr = Mathf.Max(size0 * 0.6f, r * Mathf.Pow(0.12f / k, 1f / 3f) * UnityEngine.Random.Range(0.85f, 1.15f));
            bool barren = !carry[n] || share < 1.5f;
            var d = dirs[n];
            belt.AddFragment(c - 1, fr, oreI, barren, p + d * r * UnityEngine.Random.Range(0.6f, 0.85f), barren ? 0f : share * UnityEngine.Random.Range(0.8f, 1.2f), v + d * UnityEngine.Random.Range(30f, 70f), bi);
        }
        return k;
    }

    // ---- the frame
    void Update()
    {
        // the pointer stays inside the game while it is being played; the pause menu and the start menu let it out
        Cursor.lockState = started && !paused && Application.isFocused ? CursorLockMode.Confined : CursorLockMode.None;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // the pause menu comes down first, then whatever panel is open, then the pause menu goes up
            if (!started) { }
            else if (paused) Resume();
            else if (hud.MapOpen) hud.CloseMap();
            else if (hud.InvOpen) hud.ToggleInventory();
            else Pause();
        }
        belt.Draw();
        sparks.Draw();
        if (!started || paused)
        {
            hud.UpdateHud(dt, ship, belt, carrier, zone, started);
        hud.menu.Tick(dt);
            if (_smoke) SmokeStep();
            return;
        }
        if (Input.GetKeyDown(KeyCode.F5)) { State.Save(); hud.Toast(State.sandbox ? "Sandbox · nothing is saved" : "Saved", false); }
        // the test keys: only in the combat test (combat-test.bat), never in the game proper
        if (Input.GetKeyDown(KeyCode.F9) && _combat) JumpToHold();
        if (Input.GetKeyDown(KeyCode.F10) && _combat && raiders != null) { raiders.holdFire = !raiders.holdFire; hud.Toast(raiders.holdFire ? "Test · raiders hold their fire" : "Test · raiders fire again", false); }
        // V: the volumetric dust on or off, to compare the looks
        if (Input.GetKeyDown(KeyCode.V) && started && lighting != null && lighting.post != null && !hud.InvOpen && !hud.MapOpen && !hud.MenuVisible)
        {
            lighting.post.volumetric = !lighting.post.volumetric;
            hud.Toast(lighting.post.volumetric ? "Volumetric dust on" : "Volumetric dust off", false);
        }
        if (Input.GetKeyDown(KeyCode.F1)) hud.ToggleControls();   // F1: C is free look now
        if (Input.GetKeyDown(KeyCode.F) && ship.docked) hud.ToggleServices();
        if (Input.GetKeyDown(KeyCode.F8) && _combat) hud.ToggleServices();   // the test: the refits anywhere (F is the torch in flight)
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.I)) hud.ToggleInventory();
        if (Input.GetKeyDown(KeyCode.N) && ship.warp == null) hud.ToggleMap();
        State.time += dt;
        State.TickMarket(dt);
        // the carrier drifts round its orbit; a docked ship rides along with it
        var moved = carrier.Tick(dt);
        carrier.BumpRocks(belt);   // rocks that drift into the hull are shoved off it
        if (ship.docked) ship.transform.position += moved;
        ship.Tick(dt);
        belt.Tick(dt, ship.TruePos, ship.nearRocks);   // the rails' drift time, fragments cooling, free rocks and scrap coasting, broken rocks growing back
        sparks.Tick(dt, worldOffset);
        explosions.Tick(dt, worldOffset);
        if (ship.warp == null) belt.UpdateLod0(ship.TruePos, ship.nearRocks);   // the rocks close to the ship draw their finest mesh
        if (colony != null) colony.Tick(dt);
        lighting.Update(ship.TruePos, carrier.truePos);
        if (ship.warp == null)
        {
            carrier.TickDish(dt, belt);
            drones.Tick(dt);
            raiders.Tick(dt);
        }
        Audio.I.Engine(ship.throttle, ship.afterburning, ship.braking, ship.docked || ship.InCinematic);
        bool inFlight = !ship.docked && !ship.InCinematic && ship.CanFly;
        if (Music.I != null) Music.I.combat = raiders != null && raiders.threat > 0 && !ship.docked;   // the combat track
        Audio.I.ShieldLoop(State.shield <= 0f && inFlight, State.sinceHit >= Data.SHIELD_WAIT && State.shield < State.ShieldMax && inFlight);
        Audio.I.RaiderEngine(raiders != null && inFlight ? raiders.nearest : 1e9f, raiders != null && raiders.nearestBoosting);
        Audio.I.RaiderBoost(raiders != null && inFlight ? raiders.nearestBoost : 1e9f);
        Audio.I.Laser(ship.firing && ship.weapon == "laser" && !ship.docked && !ship.InCinematic, ship.laserOn);   // the laser's sounds are the laser's: the gun has its own
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var p = _drops[i];
            if (p == null) { _drops.RemoveAt(i); continue; }
            if (p.Tick(dt, ship.transform.position))
            {
                Destroy(p.gameObject);
                _drops.RemoveAt(i);
            }
        }
        // floating origin
        if (ship.transform.position.magnitude > SHIFT_AT)
        {
            var delta = ship.transform.position;
            worldOffset += delta;
            ship.transform.position = Vector3.zero;
            belt.ApplyOffset(worldOffset);
            _planet.transform.position = _planetTrue - worldOffset;
            if (colony != null) colony.transform.position = -worldOffset;
            carrier.Place();
            foreach (var p in _drops) if (p != null) p.transform.localPosition -= delta;
            ship.OnShift(delta);
        }
        ship.UpdateCamera(dt);
        _cullT += dt;
        if (_cullT > 0.25f)
        {
            _cullT = 0f;
            belt.Cull(ship.TruePos);
        }
        _saveT += dt;
        if (_saveT > 30f)
        {
            _saveT = 0f;
            State.Save();
        }
        hud.UpdateHud(dt, ship, belt, carrier, zone, started);
        hud.menu.Tick(dt);
        tutorial.Update(dt);
        if (_combat) { State.fuel = State.Stat("tank").cap; State.credits = Mathf.Max(State.credits, 9999999f); State.rockets = State.Stat("rocket").slots; }   // the test: fuel never runs out, nor credits
        if (_combat && _combatFace == "rock" && _combatFrame == 300) ship.torchOn = false;   // the flashlight check (-face rock): a second shot with it off, same view
        if (_combat && _combatFace == "rock" && _combatFrame == 330) Shot("combat_test_off");
        if (_combat && ++_combatFrame == 240) { Shot("combat_test"); Debug.Log("combat test: " + raiders.Stats() + " · hull " + State.hull.ToString("0") + " · lock " + ship.lockKind + " · target " + (ship.raiderTarget != null)); }
        if (_smoke) SmokeStep();
    }

    // ---- `-smoke`: an unattended run through the loop that prints what happened and saves screenshots under
    // persistentDataPath: off the pad, cut the nearest copper rock, collect the ore, back to the carrier under
    // approach control, dock, deposit, depart again
    bool _smoke;
    string _smokeOnly = "";   // "" for the whole run, or one chapter: "menus"
    int _frame;
    int _smokeRock = -1;
    float _smokeHp;
    string _phase = "start";
    int _phaseFrame;
    bool _shotArrival;
    int _mapFrames;
    bool _droneDone;
    float _smokeCr;
    int _smokeBig = -1;
    Raiders.Raider _smokeRaider;
    bool _shotBreak;

    void Next(string phase)
    {
        _phase = phase;
        _phaseFrame = 0;
    }

    int _tutLast = -1;
    int _tutFrames;

    /// The run drives the questline too: it reports every step it reaches and presses Next on the ones that wait for it.
    void SmokeTutorial()
    {
        if (!tutorial.Active) return;
        int i = tutorial.StepIndex;
        if (i != _tutLast)
        {
            Debug.Log("smoke: tutorial step " + (i + 1) + " " + Tutorial.STEPS[i].id + " · last voice " + (Audio.I != null ? Audio.I.lastVoice : "-") + " playing " + (Audio.I != null && Audio.I.VoicePlaying));
            _tutLast = i;
            _tutFrames = 0;
        }
        _tutFrames++;
        var s = Tutorial.STEPS[i];
        if (!s.Auto && _tutFrames == 120) tutorial.Advance();
        if (s.id == "inv" && _tutFrames == 60) hud.ToggleInventory();
        if (s.id == "inv" && _tutFrames == 61) hud.ToggleInventory();
    }

    void SmokeStep()
    {
        _frame++;
        _phaseFrame++;
        if (_smokeOnly == "menus") { SmokeMenus(); return; }
        SmokeTutorial();
        switch (_phase)
        {
            case "start":
                if (_phaseFrame == 10)
                {
                    Debug.Log("smoke: zone=" + zone.id + " rocks=" + belt.count + " chunks=" + belt.ChunkCount + " docked=" + ship.docked + " dock=" + CargoShip.BayName(ship.dockSide) + " services=" + hud.ServicesVisible + " · " + belt.DrawReport());
                    Shot("smoke_launch");
                }
                if (_phaseFrame == 20)
                {
                    Debug.Log("smoke: models · ship " + (ship.model != null ? "Astra" : "placeholder") + " · carrier " + (carrier.model != null ? "Astra, " + carrier.anchors.Count + " anchors, pad_neg " + (carrier.anchors.ContainsKey("pad_neg") ? carrier.anchors["pad_neg"].ToString("0") : "-") + ", engine_0 " + (carrier.anchors.ContainsKey("engine_0") ? carrier.anchors["engine_0"].ToString("0") : "-") : "placeholder") + " · rock library " + belt.libraryShapes + " shapes");
                    ship.StartDeparture();
                    Next("leaving");
                }
                break;
            case "leaving":
                if (_phaseFrame == 90) Shot("smoke_taxi");
                if (ship.cut == null && !ship.docked)
                {
                    Debug.Log("smoke: launched · speed=" + ship.Speed.ToString("0") + " throttle=" + ship.throttle.ToString("0.00"));
                    int nearest; float dist;
                    belt.Scan(ship.TruePos, 200000f, Data.OreIndex("copper"), 0f, out nearest, out dist);
                    _smokeRock = nearest;
                    if (nearest >= 0)
                    {
                        belt.SetFree(nearest, Vector3.zero);   // a rock knocked off its rail and at rest, so the parked ship keeps the beam on it
                        var rp = belt.RockPos(nearest) - worldOffset;
                        var dir = (rp - ship.transform.position).normalized;
                        ship.transform.position = rp - dir * (belt.radius[nearest] + 560f);
                        ship.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        ship.vel = Vector3.zero;
                        ship.throttle = 0f;
                        ship.UpdateCamera(1f);
                        belt.hp[nearest] = 150f;   // long enough under the beam for the spot to heat up and scorch
                        _smokeHp = belt.hp[nearest];
                        ship.LockOnRock(nearest);   // what Q does with the mouse on the rock
                        Debug.Log("smoke: locked · kind=" + ship.lockKind + " rock=" + ship.lockRock + " dist=" + ship.lockDist.ToString("0") + " · " + ship.LockName());
                        ship.autoFire = true;
                    }
                    Next("mining");
                }
                break;
            case "mining":
                if (_phaseFrame == 200 || _phaseFrame == 400)
                {
                    Debug.Log("smoke: dish · " + carrier.DishStats() + " · drones " + drones.Stats() + " · stowed by drones " + State.droneUnits.ToString("0") + " · pickups " + _drops.Count);
                }
                if (_phaseFrame == 100) ship.spotHeat = 0.6f;   // the spot pre-heated, so the burn trail shows within the run (it takes 30 s on its own)
                if (_phaseFrame == 400) { Shot("smoke_scorch"); Debug.Log("smoke: scorch · spot heat " + ship.spotHeat.ToString("0.00") + " · scorches " + belt.BurnCount + " · sparks " + sparks.Count); }
                if (_phaseFrame == 60)
                {
                    Shot("smoke_mine");
                    Debug.Log("smoke: cutting " + (_smokeRock >= 0 ? belt.RockName(_smokeRock) : "nothing") + " · target=" + ship.target + " laser_on=" + ship.laserOn + " hp=" + (_smokeRock >= 0 ? belt.hp[_smokeRock].ToString("0") : "-") + " (was " + _smokeHp.ToString("0") + ") · lod0 rocks " + belt.Lod0Count + " (target lod0 " + (_smokeRock >= 0 && belt.IsLod0(_smokeRock)) + " r=" + (_smokeRock >= 0 ? belt.radius[_smokeRock].ToString("0") : "-") + ") · sparks " + sparks.Count + " · spot heat " + ship.spotHeat.ToString("0.00") + " · scorches " + belt.BurnCount + " · near rocks " + ship.nearRocks.Count + " · ship dish aimed " + ship.aimed + " yaw " + ship.aimYaw.ToString("0.00") + " pitch " + ship.aimPitch.ToString("0.00") + " rig error " + ship.DishRigError().ToString("0.0") + " deg");
                }
                if (_smokeRock >= 0 && !belt.alive[_smokeRock] && !_shotBreak) { _shotBreak = true; Shot("smoke_break"); }   // the sparks and the scrap of the break
                if (_phaseFrame > 400 && (_smokeRock < 0 || !belt.alive[_smokeRock] || _phaseFrame > 2400))
                {
                    ship.autoFire = false;
                    Debug.Log("smoke: mined · rock_alive=" + (_smokeRock >= 0 && belt.alive[_smokeRock]) + " pickups_left=" + _drops.Count + " cargo=" + State.CargoTotal().ToString("0") + " fuel=" + State.fuel.ToString("0.0") + " fps=" + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0"));
                    Debug.Log("smoke: fx · sparks " + sparks.Count + " · scrap " + belt.ScrapCount + " · lod0 rocks " + belt.Lod0Count + " · near rocks " + ship.nearRocks.Count + " · spot heat " + ship.spotHeat.ToString("0.00") + " · scorches " + belt.BurnCount + " · fittings " + ship.VariantReport());
                    Next("closeup");
                }
                break;
            case "closeup":
                // park three radii off the nearest giant, so the close-up LOD 0 mesh and the scrap show in the shot
                if (_phaseFrame == 1)
                {
                    _smokeBig = -1;
                    float bd = float.PositiveInfinity;
                    foreach (int i in belt.RocksNear(ship.TruePos, 300000f))
                    {
                        if (belt.cls[i] < 2) continue;
                        float d = (belt.RockPos(i) - ship.TruePos).magnitude;
                        if (d < bd) { bd = d; _smokeBig = i; }
                    }
                    if (_smokeBig >= 0)
                    {
                        var rp = belt.RockPos(_smokeBig) - worldOffset;
                        var dir = (rp - ship.transform.position).normalized;
                        ship.transform.position = rp - dir * belt.radius[_smokeBig] * 3f;
                        ship.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        ship.vel = Vector3.zero;
                        ship.throttle = 0f;
                        ship.UpdateCamera(1f);
                    }
                    Debug.Log("smoke: closeup · rock " + _smokeBig + " " + (_smokeBig >= 0 ? belt.RockName(_smokeBig) + " r=" + belt.radius[_smokeBig].ToString("0") : "none"));
                }
                if (_phaseFrame == 45)
                {
                    Shot("smoke_closeup");
                    Debug.Log("smoke: closeup · lod0 " + (_smokeBig >= 0 && belt.IsLod0(_smokeBig)) + " · lod0 rocks " + belt.Lod0Count + " · near rocks " + ship.nearRocks.Count + " · at " + (_smokeBig >= 0 ? (belt.RockPos(_smokeBig) - ship.TruePos).magnitude.ToString("0") : "-") + " u · scrap " + belt.ScrapCount);
                }
                if (_phaseFrame == 60) Next("combat");
                break;
            case "combat":
                // a raider hold: the autocannon fitted, the nearest raider locked, the trigger held until it dies
                if (_phaseFrame == 1)
                {
                    _smokeRaider = null;
                    float bd = float.PositiveInfinity;
                    foreach (var r in raiders.raiders) { float d = (r.pos - ship.TruePos).magnitude; if (d < bd) { bd = d; _smokeRaider = r; } }
                    if (_smokeRaider == null) { Debug.Log("smoke: combat · no raiders in the zone"); ship.throttle = 1f; Next("collect"); break; }
                    var rp = _smokeRaider.pos - worldOffset;
                    var dir = (rp - ship.transform.position).normalized;
                    ship.transform.position = rp - dir * 700f;
                    ship.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    ship.vel = Vector3.zero;
                    ship.throttle = 0f;
                    ship.UpdateCamera(1f);
                    ship.LockOnRaider(_smokeRaider);
                    ship.autoFire = true;
                    _smokeCr = State.credits;
                    Debug.Log("smoke: combat · " + raiders.Stats() + " · raider hp " + _smokeRaider.hp.ToString("0") + " state " + _smokeRaider.state + " · gun " + Data.Describe("gun", State.up["gun"]) + " · hull " + State.hull.ToString("0"));
                }
                if (_phaseFrame == 120) Shot("smoke_combat");
                // four seconds in, one seeker away (at the locked raider if it still lives, else the nearest), then back to the cannon
                if (_phaseFrame == 240) ship.weapon = "rocket";
                if (_phaseFrame == 242) { ship.weapon = "gun"; Debug.Log("smoke: rocket · fired " + raiders.rocketsFired + " · in flight " + raiders.rockets.Count + " · aboard " + State.rockets + " of " + State.Stat("rocket").slots); }
                if (_phaseFrame % 150 == 0) Debug.Log("smoke: combat · " + raiders.Stats() + " · raider hp " + (_smokeRaider.dead ? "dead" : _smokeRaider.hp.ToString("0")) + " state " + _smokeRaider.state + " · lock " + ship.lockKind + " target " + (ship.raiderTarget != null) + " firing " + ship.gunFiring + " · hull " + State.hull.ToString("0") + " · threat " + raiders.threat);
                if (_smokeRaider.dead || _phaseFrame > 900)
                {
                    Debug.Log("smoke: combat " + (_smokeRaider.dead ? "won" : "TIMED OUT") + " · " + raiders.Stats() + " · credits " + _smokeCr.ToString("0") + " -> " + State.credits.ToString("0") + " · hull " + State.hull.ToString("0") + " · cargo " + State.CargoTotal().ToString("0"));
                    ship.autoFire = false;
                    ship.ReleaseLock();
                    ship.throttle = 1f;
                    Next("collect");
                }
                break;
            case "collect":
                if (_phaseFrame == 20) ship.Radar();   // the pulse you can see, and the marks it leaves as it reaches the rocks
                if (_phaseFrame == 80) { Shot("smoke_radar"); Debug.Log("smoke: radar · pulse visible " + ship.PulseVisible + " · marked so far " + belt.Marked(State.time, ship.TruePos, 999).Count + " of " + ship.scanCount + " · nearest " + (ship.scanNearest >= 0 ? belt.RockName(ship.scanNearest) + " at " + Data.Fm(ship.scanDist) : "none")); }
                if (_phaseFrame == 240) Debug.Log("smoke: radar · pulse visible " + ship.PulseVisible + " · marked " + belt.Marked(State.time, ship.TruePos, 999).Count + " of " + ship.scanCount);
                if (_phaseFrame == 300 || _drops.Count == 0)
                {
                    Shot("smoke_flight");
                    Debug.Log("smoke: flight · speed=" + ship.Speed.ToString("0") + " cargo=" + State.CargoTotal().ToString("0") + " pickups_left=" + _drops.Count + " offset=" + worldOffset.ToString("0"));
                    // back to the carrier with a hold worth depositing: park 3,000 off the nearer mouth and ask approach control for the ship
                    State.AddCargo("copper", 120f);
                    State.AddCargo("gold", 30f);
                    int entry = carrier.NearestSide(ship.TruePos);
                    var startL = CargoShip.OpeningLocal(entry) + new Vector3(300f, 120f, entry * 3000f);
                    ship.transform.position = carrier.ToTrue(startL) - worldOffset;
                    ship.vel = carrier.vel;
                    ship.throttle = 0f;
                    ship.transform.rotation = Ship.LevelHeading(carrier.Dir(new Vector3(0f, 0f, -entry)));
                    ship.UpdateCamera(1f);
                    ship.StartApproach();
                    Debug.Log("smoke: approach requested · cut=" + (ship.cut != null) + " dist=" + (ship.TruePos - carrier.truePos).magnitude.ToString("0"));
                    Next("approach");
                }
                break;
            case "approach":
                if (_phaseFrame == 120) Shot("smoke_approach");
                if (ship.docked)
                {
                    Shot("smoke_dock");
                    float before = State.StoreTotal();
                    ship.DepositAll();
                    Debug.Log("smoke: docked in " + CargoShip.BayName(ship.dockSide) + " · store " + before.ToString("0") + " -> " + State.StoreTotal().ToString("0") + " · hold=" + State.CargoTotal().ToString("0") + " · fuel=" + State.fuel.ToString("0.0") + "/" + State.Stat("tank").cap.ToString("0") + " shipFuel=" + State.shipFuel.ToString("0") + " · services " + hud.ServicesVisible);
                    Next("docked");
                }
                if (_phaseFrame > 4000)
                {
                    Debug.Log("smoke: FAIL · the approach never docked · cut=" + (ship.cut != null) + " local=" + carrier.ToLocalTrue(ship.TruePos).ToString("0"));
                    Quit();
                }
                break;
            case "docked":
                if (_phaseFrame == 10)
                {
                    // a lump of iron adrift off the mouth: the collector drone should fetch it and stow it
                    SpawnPickup("iron", 40f, carrier.ToTrue(new Vector3(1200f, 200f, 2600f)) - worldOffset, carrier.vel);
                }
                if (_phaseFrame % 600 == 0 || State.droneUnits > 0.5f && !_droneDone)
                {
                    Debug.Log("smoke: drones · " + drones.Stats() + " · stowed " + State.droneUnits.ToString("0") + " · pickups " + _drops.Count + " · dish " + carrier.DishStats());
                    if (State.droneUnits > 0.5f) _droneDone = true;
                }
                // the inventory beside the services panel: both grids, then the drag and drop driven as the pointer would do it: a
                // storage stack dropped on the hold grid, a hold stack dropped on the storage grid, a small stack, then one let go
                // outside the grids (jettisoned into the hangar)
                if (_phaseFrame == 90) hud.ShowWindow("inv");   // the hangar window on its inventory tab (docking opened it already)
                if (_phaseFrame == 100)
                {
                    float h0 = State.CargoTotal();
                    bool ok = hud.SmokeDrop(true);
                    Debug.Log("smoke: drag storage -> hold · " + ok + " · hold " + h0.ToString("0") + " -> " + State.CargoTotal().ToString("0") + " · store=" + State.StoreTotal().ToString("0") + " · slots hold " + hud.holdSlots.Count + " store " + hud.storeSlots.Count);
                }
                if (_phaseFrame == 110)
                {
                    float s0 = State.StoreTotal();
                    bool ok = hud.SmokeDrop(false);
                    Debug.Log("smoke: drag hold -> storage · " + ok + " · store " + s0.ToString("0") + " -> " + State.StoreTotal().ToString("0") + " · hold=" + State.CargoTotal().ToString("0"));
                }
                if (_phaseFrame == 115) hud.SmokeDrop(true, 10f);   // a small stack to throw away
                if (_phaseFrame == 118)
                {
                    // the fittings follow the refit tiers: a level-2 laser shows the second barrel, then back
                    int was = State.up["laser"];
                    State.up["laser"] = 2;
                    ship.ConfigureModel();
                    Debug.Log("smoke: fittings at laser Lv3 · " + ship.VariantReport());
                    State.up["laser"] = was;
                    ship.ConfigureModel();
                    Debug.Log("smoke: fittings at laser Lv1 · " + ship.VariantReport());
                }
                if (_phaseFrame == 120)
                {
                    Shot("smoke_inventory");
                    int before = _drops.Count;
                    bool ok = hud.SmokeJettisonFirst();   // what a drag let go outside the grids does
                    Debug.Log("smoke: jettison · " + ok + " · hold=" + State.CargoTotal().ToString("0") + " · lumps " + before + " -> " + _drops.Count + " · noPick=" + (_drops.Count > 0 ? _drops[_drops.Count - 1].noPick.ToString("0") : "-"));
                }
                if (_phaseFrame == 125) hud.CloseWindow();   // the screenshot is taken at the end of the frame, so the window closes a few frames on
                if (_phaseFrame == 150 || (_phaseFrame > 150 && _phaseFrame % 30 == 0 && _droneDone) || _phaseFrame == 7000)
                {
                    if (_phaseFrame == 150 && !_droneDone) { Shot("smoke_pad"); break; }   // the first shot; the run then waits for the drone
                    Shot("smoke_pad");
                    var local = carrier.ToLocalTrue(ship.TruePos);
                    Debug.Log("smoke: on the pad · local=" + local.ToString("0") + " · park=" + CargoShip.ParkLocal(ship.dockSide).ToString("0") + " · carrier speed " + carrier.vel.magnitude.ToString("0") + " u/s · drone stowed " + State.droneUnits.ToString("0") + " · force field flashes " + carrier.fieldFlashes + " · hull bumps " + carrier.bumps);
                    ship.StartDeparture();
                    Next("depart2");
                }
                break;
            case "depart2":
                if (ship.cut == null && !ship.docked)
                {
                    var local = carrier.ToLocalTrue(ship.TruePos);
                    Debug.Log("smoke: departed again · local=" + local.ToString("0") + " speed=" + ship.Speed.ToString("0") + " · exit_pending=" + ship.exitPending);
                    var plays = new List<string>();
                    foreach (var kv in Audio.I.plays) plays.Add(kv.Key + "x" + kv.Value);
                    Debug.Log("smoke: audio plays · " + string.Join(" ", plays.ToArray()) + " · loops " + Audio.I.LoopState());
                    // out of fuel just off the mouth: T calls for recovery, which sets the ship back down on a pad
                    State.fuel = 0f;
                    ship.vel = Vector3.zero;
                    _smokeCr = State.credits;
                    ship.CallRecovery();
                    Debug.Log("smoke: recovery requested · " + (ship.recovery != null ? ship.recovery.reason : "none") + " · fuel=" + State.fuel.ToString("0") + " credits=" + State.credits.ToString("0") + " · lock=" + ship.lockKind);
                    Next("recover");
                }
                if (_phaseFrame > 900)
                {
                    Debug.Log("smoke: FAIL · the departure never finished");
                    Quit();
                }
                break;
            case "recover":
                if (_phaseFrame == 40) Shot("smoke_recovery");   // the fade closing over the dry ship
                if (ship.docked && ship.recovery == null)
                {
                    Debug.Log("smoke: recovered · docked=" + ship.docked + " dock=" + CargoShip.BayName(ship.dockSide) + " · credits " + _smokeCr.ToString("0") + " -> " + State.credits.ToString("0") + " (fee " + (_smokeCr - State.credits).ToString("0") + ") · fuel=" + State.fuel.ToString("0") + " · local=" + carrier.ToLocalTrue(ship.TruePos).ToString("0") + " · services " + hud.ServicesVisible);
                    Next("redock");
                }
                if (_phaseFrame > 900) { Debug.Log("smoke: FAIL · recovery never docked · recovery=" + (ship.recovery != null) + " docked=" + ship.docked); Quit(); }
                break;
            case "redock":
                if (ship.docked && !hud.MapOpen && _phaseFrame < 3990) hud.OpenMap();
                if (ship.docked && hud.MapOpen && _mapFrames++ == 3)
                {
                    Shot("smoke_map");
                }
                if (ship.docked && _mapFrames > 5)
                {
                    hud.CloseMap();
                    ship.StartWarp(Data.ZONE_HUB);
                    Debug.Log("smoke: warp to the Hub requested · warp=" + (ship.warp != null) + " ly=" + Data.ZoneLy(zone, Data.ZONE_HUB));
                    Next("warp");
                }
                if (_phaseFrame > 4000) { Debug.Log("smoke: FAIL · never re-docked"); Quit(); }
                break;
            case "warp":
                if (_phaseFrame == 200) Shot("smoke_warp");
                if (ship.warp == null && zone.hub && ship.cut != null && _phaseFrame % 60 == 0 && !_shotArrival)
                {
                    _shotArrival = true;
                    Shot("smoke_arrival");
                }
                if (ship.warp == null && zone.hub && ship.docked && ship.hold)
                {
                    Shot("smoke_hub");
                    float before = State.credits;
                    ship.Sell(Data.ORE_KEYS, true, true);
                    ship.RefuelCargoShip();
                    Debug.Log("smoke: music · " + (Music.I != null ? Music.I.Report() : "none") + " · fps " + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0"));
                    Debug.Log("smoke: at the Hub · holding=" + ship.hold + " credits " + before.ToString("0") + " -> " + State.credits.ToString("0") + " · store=" + State.StoreTotal().ToString("0") + " · shipFuel=" + State.shipFuel.ToString("0") + " · carrier at " + carrier.truePos.ToString("0") + " · rocks=" + belt.count);
                    Next("hub");
                }
                if (_phaseFrame > 6000) { Debug.Log("smoke: FAIL · never arrived at the Hub · warp=" + (ship.warp != null) + " zone=" + zone.id + " docked=" + ship.docked + " cut=" + (ship.cut != null)); Quit(); }
                break;
            case "hub":
                // screenshots land at the end of their frame, so each page change comes the frame after its shot
                if (_phaseFrame == 120) Shot("smoke_market");
                if (_phaseFrame == 121) Pause();   // the pause menu over the frozen game, then its settings and controls pages
                if (_phaseFrame == 150) Shot("smoke_menu");
                if (_phaseFrame == 151) hud.menu.ShowPage("settings");
                if (_phaseFrame == 170) Shot("smoke_settings");
                if (_phaseFrame == 171) hud.menu.ShowPage("controls");
                if (_phaseFrame == 190) Shot("smoke_controls");
                if (_phaseFrame == 191) Resume();
                if (_phaseFrame == 210)
                {
                    ship.StartWarp(Data.ZONE_KESSLER);
                    Debug.Log("smoke: warp home requested · warp=" + (ship.warp != null) + " · fonts missing " + Ui.fontsMissing);
                }
                if (_phaseFrame > 220 && ship.warp == null && !zone.hub && ship.docked)
                {
                    Shot("smoke_home");
                    Debug.Log("smoke: home · zone=" + zone.id + " docked=" + ship.docked + " hold=" + ship.hold + " dock=" + CargoShip.BayName(ship.dockSide) + " local=" + carrier.ToLocalTrue(ship.TruePos).ToString("0") + " rocks=" + belt.count);
                    State.Save();
                    Debug.Log("smoke: saved to " + State.SavePath + " · screenshots in " + Application.persistentDataPath);
                    Quit();
                }
                if (_phaseFrame > 4000) { Debug.Log("smoke: FAIL · never got home"); Quit(); }
                break;
        }
    }

    /// `-smoke menus`: the hangar window's three tabs, the pause menu's three pages and the nav map, each shot where it
    /// stands, from the pad the game starts on; some credits so the upgrade rows show as buyable. Nothing is saved.
    void SmokeMenus()
    {
        int f = _phaseFrame;
        if (f == 5) { State.credits = 5000f; hud.ShowWindow("inv"); }
        if (f == 30) { Shot("menus_inventory"); Debug.Log("smoke menus: inventory · open=" + hud.InvOpen + " · hold slots " + hud.holdSlots.Count + " · store slots " + hud.storeSlots.Count); }
        if (f == 31) hud.ShowWindow("ship");
        if (f == 50) { Shot("menus_ship_upgrades"); Debug.Log("smoke menus: ship upgrades · credits " + State.credits); }
        if (f == 51) hud.ShowWindow("depot");
        if (f == 70) Shot("menus_cargo_upgrades");
        if (f == 71) hud.CloseWindow();
        if (f == 75) Pause();
        if (f == 95) Shot("menus_pause");
        if (f == 96) hud.menu.ShowPage("settings");
        if (f == 115) Shot("menus_settings");
        if (f == 116) hud.menu.ShowPage("controls");
        if (f == 135) Shot("menus_controls");
        if (f == 136) { hud.menu.ShowPage("main"); Resume(); }
        if (f == 140) hud.ToggleMap();
        if (f == 160) { Shot("menus_navmap"); Debug.Log("smoke menus: nav map · open=" + hud.MapOpen); }
        if (f == 161) hud.CloseMap();
        if (f == 170)
        {
            Debug.Log("smoke menus: done · screenshots menus_*.png in " + Application.persistentDataPath + " · fonts missing " + Ui.fontsMissing);
            Quit();
        }
    }

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, name + ".png"));
    }

}
