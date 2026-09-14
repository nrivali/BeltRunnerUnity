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
    public Camera cam;
    public Light sun;
    public bool started, paused;

    Transform _pickups;
    GameObject _planet;
    Vector3 _planetTrue;
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
        foreach (var a in args) if (a == "-smoke" || a == "--smoke") _smoke = true;
        if (_smoke) State.Reset();   // the run starts from a fresh pilot every time
        var t0 = Time.realtimeSinceStartup;
        SetupCamera();
        SetupLighting();
        belt = new Belt();
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
        var hudGo = new GameObject("HUD");
        hud = hudGo.AddComponent<Hud>();
        hud.ship = ship;
        hud.Build();
        hud.onStart = StartGame;
        hud.onNewGame = NewGame;
        hud.onQuit = Quit;
        LoadZone(Data.ZoneById(_smoke ? "kessler" : State.zoneId));
        SpawnInZone();
        ship.UpdateCamera(1f);
        Debug.Log("belt: " + belt.count + " rocks in " + belt.ChunkCount + " chunks, built in " + Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " ms");
        if (_smoke)
        {
            ship.mouseSteer = false;
            StartGame();
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

    void SetupLighting()
    {
        // a template scene's own directional light would double the sun
        foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
        var go = new GameObject("Sun");
        sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Data.Hex("#fff3e3");
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.92f;
        sun.shadowBias = 0.08f;
        sun.shadowNormalBias = 0.6f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.045f, 0.05f, 0.07f);
        RenderSettings.fog = false;
        // shadows as the browser casts them: one box a few kilometres round the ship, never the whole belt
        QualitySettings.shadowDistance = 3300f;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
    }

    void LoadZone(Data.Zone z)
    {
        zone = z;
        State.zoneId = z.id;
        hud.zone = z;
        foreach (var p in _drops) if (p != null) Destroy(p.gameObject);
        _drops.Clear();
        ship.nearRocks = new List<int>();
        belt.Clear();
        belt.Build(z, z.id == "kessler" ? SEED : SEED + 11);
        if (colony != null) { Destroy(colony.gameObject); colony = null; }
        if (z.hub)
        {
            colony = new GameObject("Colony").AddComponent<Colony>();
            colony.Build();
        }
        cam.backgroundColor = z.bg;
        sun.transform.rotation = Quaternion.LookRotation(-z.sunDir.normalized, Vector3.up);
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
            foreach (var mr in _planet.GetComponentsInChildren<MeshRenderer>(true)) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
    public void BreakRock(int i)
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
        float total = belt.Kill(i);
        float loose = splits ? total * 0.25f : total;
        if (_smoke) Debug.Log("smoke: break · " + rname + " total=" + total.ToString("0") + " loose=" + loose.ToString("0") + " splits=" + splits);
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
        string oreTxt = oreI >= 0 && loose > 0f ? " · " + Mathf.RoundToInt(loose) + " " + Data.ORES[oreI].name + " loose" : "";
        if (splits) hud.Toast(rname + " broken into " + parts + " " + Belt.CLS_NAME[c - 1].ToLowerInvariant() + " rocks" + oreTxt, false);
        else if (oreI >= 0 && loose > 0f) hud.Toast(rname + " broken" + oreTxt, false);
        else hud.Toast(rname + " broken · scrap only", false);
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
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!started) { }
            else if (hud.MapOpen) hud.CloseMap();
            else if (paused) Resume();
            else Pause();
        }
        belt.Draw();
        if (!started || paused)
        {
            hud.UpdateHud(dt, ship, belt, zone);
            if (_smoke) SmokeStep();
            return;
        }
        if (Input.GetKeyDown(KeyCode.F5)) { State.Save(); hud.Toast("Saved", false); }
        if (Input.GetKeyDown(KeyCode.C)) hud.ToggleControls();
        if (Input.GetKeyDown(KeyCode.F) && ship.docked) hud.ToggleServices();
        if (Input.GetKeyDown(KeyCode.N) && ship.warp == null) hud.ToggleMap();
        State.time += dt;
        State.TickMarket(dt);
        // the carrier drifts round its orbit; a docked ship rides along with it
        var moved = carrier.Tick(dt);
        if (ship.docked) ship.transform.position += moved;
        ship.Tick(dt);
        belt.Tick(dt, ship.TruePos);
        if (colony != null) colony.Tick(dt);
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
        hud.UpdateHud(dt, ship, belt, zone);
        if (_smoke) SmokeStep();
    }

    // ---- `-smoke`: an unattended run through the loop that prints what happened and saves screenshots under
    // persistentDataPath: off the pad, cut the nearest copper rock, collect the ore, back to the carrier under
    // approach control, dock, deposit, depart again
    bool _smoke;
    int _frame;
    int _smokeRock = -1;
    float _smokeHp;
    string _phase = "start";
    int _phaseFrame;
    bool _shotArrival;
    int _mapFrames;

    void Next(string phase)
    {
        _phase = phase;
        _phaseFrame = 0;
    }

    void SmokeStep()
    {
        _frame++;
        _phaseFrame++;
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
                        belt.hp[nearest] = 45f;
                        _smokeHp = belt.hp[nearest];
                        ship.autoFire = true;
                    }
                    Next("mining");
                }
                break;
            case "mining":
                if (_phaseFrame == 60)
                {
                    Shot("smoke_mine");
                    Debug.Log("smoke: cutting " + (_smokeRock >= 0 ? belt.RockName(_smokeRock) : "nothing") + " · target=" + ship.target + " laser_on=" + ship.laserOn + " hp=" + (_smokeRock >= 0 ? belt.hp[_smokeRock].ToString("0") : "-") + " (was " + _smokeHp.ToString("0") + ")");
                }
                if (_phaseFrame > 400 && (_smokeRock < 0 || !belt.alive[_smokeRock] || _phaseFrame > 1500))
                {
                    ship.autoFire = false;
                    Debug.Log("smoke: mined · rock_alive=" + (_smokeRock >= 0 && belt.alive[_smokeRock]) + " pickups_left=" + _drops.Count + " cargo=" + State.CargoTotal().ToString("0") + " fuel=" + State.fuel.ToString("0.0") + " fps=" + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0"));
                    ship.throttle = 1f;
                    Next("collect");
                }
                break;
            case "collect":
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
                if (_phaseFrame == 150)
                {
                    Shot("smoke_pad");
                    var local = carrier.ToLocalTrue(ship.TruePos);
                    Debug.Log("smoke: on the pad · local=" + local.ToString("0") + " · park=" + CargoShip.ParkLocal(ship.dockSide).ToString("0") + " · carrier speed " + carrier.vel.magnitude.ToString("0") + " u/s");
                    ship.StartDeparture();
                    Next("depart2");
                }
                break;
            case "depart2":
                if (ship.cut == null && !ship.docked)
                {
                    var local = carrier.ToLocalTrue(ship.TruePos);
                    Debug.Log("smoke: departed again · local=" + local.ToString("0") + " speed=" + ship.Speed.ToString("0") + " · exit_pending=" + ship.exitPending);
                    // straight back in, then the jump to the Hub with the storage full of ore to sell
                    int entry = carrier.NearestSide(ship.TruePos);
                    var startL = CargoShip.OpeningLocal(entry) + new Vector3(300f, 120f, entry * 3000f);
                    ship.transform.position = carrier.ToTrue(startL) - worldOffset;
                    ship.vel = carrier.vel;
                    ship.throttle = 0f;
                    ship.transform.rotation = Ship.LevelHeading(carrier.Dir(new Vector3(0f, 0f, -entry)));
                    ship.StartApproach();
                    Next("redock");
                }
                if (_phaseFrame > 900)
                {
                    Debug.Log("smoke: FAIL · the departure never finished");
                    Quit();
                }
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
                    Debug.Log("smoke: at the Hub · holding=" + ship.hold + " credits " + before.ToString("0") + " -> " + State.credits.ToString("0") + " · store=" + State.StoreTotal().ToString("0") + " · shipFuel=" + State.shipFuel.ToString("0") + " · carrier at " + carrier.truePos.ToString("0") + " · rocks=" + belt.count);
                    Next("hub");
                }
                if (_phaseFrame > 6000) { Debug.Log("smoke: FAIL · never arrived at the Hub · warp=" + (ship.warp != null) + " zone=" + zone.id + " docked=" + ship.docked + " cut=" + (ship.cut != null)); Quit(); }
                break;
            case "hub":
                if (_phaseFrame == 120)
                {
                    Shot("smoke_market");
                    ship.StartWarp(Data.ZONE_KESSLER);
                    Debug.Log("smoke: warp home requested · warp=" + (ship.warp != null));
                }
                if (_phaseFrame > 130 && ship.warp == null && !zone.hub && ship.docked)
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

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, name + ".png"));
    }

}
