using System.Collections.Generic;
using UnityEngine;

/// The player's ship: the browser game's flight model (mouse yaw and pitch, W/S throttle, A/D roll, Shift afterburner,
/// the planet's pull, drag and the speed cap, the zone edge), the mining laser, the radar pulse, the chase camera, and
/// the cargo ship's hangar: approach control (E), the pad, and departure. The ship is a Unity transform in scene
/// space; true world coordinates are its position plus the floating origin.
public class Ship : MonoBehaviour
{
    public const float TURN = 40f * Mathf.Deg2Rad;   // yaw and pitch: 40 degrees a second at full deflection (30 until 2026-09-15)
    public const float DRIFT_TURN = 50f / 40f;   // the drift: the nose turns at 50 degrees a second (the usual 40)
    public const float REPAIR_RATE = 6f;
    public const float WARP_DUR = 8.6f;
    public const float WARP_LOAD_AT = 4.3f;   // the screen is black from 4.2 s to 5.4 s; the zone swaps underneath

    public Vector3 vel;
    public float throttle;
    public bool overcharge, afterburning, thrusting, braking;
    public bool drifting;   // the drift (Space held): the engine cuts, the ship coasts on, the nose swings twice as fast
    public int target = -1;
    public bool laserOn, firing;
    public float radarCd;
    public bool radarPulsed;   // for the tutorial: R has been pressed since the step began
    public bool mouseSteer = true;   // off in the smoke run, where no one is holding the mouse
    public bool autoFire;            // the smoke run holds the trigger
    public int scanCount = -1;
    public int scanNearest = -1;
    public float scanDist;
    public float shake;
    public float hitCd;

    // the hangar
    public bool docked;
    public bool hold;          // docked at the Hub holding station rather than on a hangar pad
    public int dockSide;
    public bool depWait;        // W has to be released once after docking before it departs (you usually fly in holding it)
    public bool exitPending;    // just left the hangar: the mouth cannot capture the ship until it is clear of the corridor
    public bool flownOut;
    public float hangarT;
    bool _fuelDryWarned, _partsWarned, _warpVoiced;
    float _announceAt = -1f, _colonyAt = -1f;   // the deck announcement and the colony call, a beat after docking

    /// The approach or departure under approach control: a path in the carrier's frame flown over `dur` seconds.
    public class Cut
    {
        public string mode;      // "dock" or "depart"
        public int side, entry;
        public List<Vector3> pts;
        public float t, dur, pt;
        public string phase;     // dock: "fly" then "settle"
        public Vector3 hover, park;
        public Quaternion qEnd;
    }
    public Cut cut;

    /// The jump between zones: a fade to black while the zone swaps underneath, then the arrival.
    public class Warp
    {
        public Data.Zone z;
        public float t;
        public bool loaded, skip, fromHold;
    }
    public Warp warp;

    public Game game;
    public Belt belt;
    public CargoShip carrier;
    public Camera cam;
    Quaternion _camQ = Quaternion.identity;
    LineRenderer _laser;
    Vector3 _prevPos;
    float _nearT;
    public List<int> nearRocks = new List<int>();

    public Vector3 TruePos { get { return transform.position + game.worldOffset; } }
    public Vector3 Forward { get { return transform.forward; } }
    public float Speed { get { return vel.magnitude; } }
    public bool InCinematic { get { return cut != null || warp != null; } }

    public static Vector3 HoldFwd()
    {
        var d = Data.HOLD_DIR;
        return new Vector3(d.x, 0f, d.z).normalized;
    }

    public static Vector3 HoldSide()
    {
        return Vector3.Cross(HoldFwd(), Vector3.up);
    }

    public Transform model;
    public Transform focus;   // the mining dish beam origin on the model
    public Light torch;
    public bool torchOn = true;   // F in flight; not saved, as in the browser

    // ---- the Q lock (the HTML's lock / hover / lockType / lockDist): Q locks whatever the mouse is over, switches to a
    // different hovered target, or releases; the ship steers itself to keep the lock on the nose ray until released
    public const float LOCK_RANGE = 100000f;   // a lock holds out to this distance (well beyond laser reach: it is for navigating to things too)
    public string lockKind = "";               // "", "rock" or "station" (the cargo ship)
    public int lockRock = -1;
    public float lockDist;
    public class HoverInfo { public string kind; public int rock = -1; public Raiders.Raider raider; public float dist; public string name; }
    public Raiders.Raider lockRaider;
    // the autocannon (combat): the raider under the nose (or the locked one within the cone), the shot timer
    public Raiders.Raider raiderTarget;
    public bool gunFiring;
    // the autocannon's heat: 0..1, full after GUN_HEAT_TIME of continuous fire, then a lockout until it cools below
    // GUN_COOL_TO; it cools from full in GUN_COOL_TIME
    public float gunHeat;
    public bool gunOverheated;
    public const float GUN_HEAT_TIME = 5f, GUN_COOL_TIME = 4f, GUN_COOL_TO = 0.4f;
    public Vector3 aimDir = Vector3.forward;   // where the gun points: down the mouse ray, within the forward half
    public string weapon = "laser";   // 1, 2, 3 and the wheel: "laser" cuts rock, "gun" is the autocannon, "rocket" the seekers
    float _gunCd, _gunWarnT, _rocketWarnT;
    public float rocketCd;            // the seeker tube's reload, seconds left
    public static string WeaponName(string w) { return w == "gun" ? "Autocannon" : w == "rocket" ? "Seeker rockets" : "Mining laser"; }
    public HoverInfo hover;                     // what the mouse is over (refreshed at 10 Hz, and afresh on Q)
    int _hoverFrame;

    // ---- recovery, in place of the browser's tow tug (the user's call for the Unity port): a hull breach disables the
    // ship on the spot, and T with a dry tank, or the breach itself, brings the ship straight back to the cargo ship's
    // pad behind a short fade, for the tug's fee (15% of credits), a hull patch to 35% and a tank topped to 30%
    public class Recovery
    {
        public string reason; public float t; public bool done;
        // the death animation on a breach: its kind, how long it runs before the fade (at) and the whole (dur)
        public string kind = ""; public float at = RECOVER_AT, dur = RECOVER_DUR;
        public Vector3 axis = Vector3.up; public float spin, flameT, smokeT, sparkT, popT, arcT, ventT;
    }
    public Recovery recovery;
    public bool disabled;
    bool _lowHullWarned;
    int _fuelStage;   // Vega's fuel warnings given so far on this tank: 1 at 50%, 2 at 25%, 3 at 10%; drops back as the tank refills
    public const float RECOVER_DUR = 3.2f;
    public const float RECOVER_AT = 1.6f;
    public bool CanFly { get { return !disabled && recovery == null; } }

    // free look (the browser's lookYaw / lookPitch): hold the right mouse button to swing the camera without turning
    // the ship; it eases back once the button is released
    public float lookYaw, lookPitch;
    // the camera feel: this frame's control deflections (for the swing and the bank) and the eased camera state
    public const float FOV = 62f;
    float _ctlYaw, _ctlPitch, _ctlRoll;
    float _fov = FOV;
    public float camYaw, camPitch, camRoll;   // the eased control deflections the camera swing uses (the HUD reads them too)
    float _lockT;      // seconds since the lock was set: the lock steering eases its authority in over the first 0.4 s
    bool _autoSteer;   // the lock is steering this frame: the camera swing takes only a quarter of that deflection
    public float burnK;      // the afterburner's intensity, eased: in over a third of a second, out over half
    float _burnKick, _jetT;  // the kick at ignition (decays), the jet puff timer
    public bool rdown;

    // the beam's heat effect (the browser's heatFx): the spot on the stone takes 30 s to reach white heat and cools off
    // over 10 s; it glows, lights the rock and throws sparks
    public float spotHeat;
    int _spotKey = -1;
    float _burnT;
    Vector3 _spotPos;   // true
    Transform _spotGlow;
    Material _spotMat;
    Light _spotLight;

    // the mining dish's rig in Astra's model (mining_dish_yaw / mining_dish_pitch, the focus lens and six rim emitters):
    // it swings onto the beam's target and settles forward when idle; the emitters glow, pulse and flicker (animateDish)
    Transform _dishYaw, _dishPitch;
    Vector3 _dishMount = new Vector3(0f, -3.4f, 7f);
    float _dishYawSign = 1f, _dishPitchSign = 1f;
    public float aimYaw, aimPitch = 0.05f;
    public bool aimed;
    readonly List<Material> _rimMats = new List<Material>();
    readonly List<Transform> _rimBeams = new List<Transform>();
    Material _beamRimMat, _focusMat;
    Transform _focusGlow;

    /// The laser's origin in true coordinates: the dish focus on the model.
    public Vector3 LaserOrigin()
    {
        return DishFocusScene() + game.worldOffset;
    }

    public void Build()
    {
        BuildLaser();
        BuildTorch();
        // Astra's player ship: nose along +Z as imported (glTFast mirrors X, which leaves the nose where Unity wants it),
        // scaled by SHIP_SCALE; its named nodes carry the engines, the nav lights and the dish rig
        var prefab = Resources.Load<GameObject>("Models/player_ship");
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab, transform);
            go.name = "Model";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Data.SHIP_SCALE;
            model = go.transform;
            focus = FindDeep(model, "focus");
            foreach (var n in new[] { "engine_l", "engine_r" })
            {
                var a = FindDeep(model, n);
                if (a == null) continue;
                var lg = new GameObject("EngineGlow");
                lg.transform.SetParent(a, false);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = Data.Hex("#5ed3f0");
                l.intensity = 0f;
                l.range = 140f;
                l.shadows = LightShadows.None;
                _engineLights.Add(l);
                if (_exhaustMat == null) _exhaustMat = SoftMaterial(new Color(0.37f, 0.83f, 0.94f, 0.15f));
                _exhausts.Add(GlowQuad(a, new Vector3(0f, 0f, -0.25f), 5f, _exhaustMat, "Exhaust"));
            }
            foreach (var pair in new[] { new object[] { "nav_l", "#ff5a5a" }, new object[] { "nav_r", "#6bd69a" } })
            {
                var a = FindDeep(model, (string)pair[0]);
                if (a == null) continue;
                var c = Data.Hex((string)pair[1]);
                c.a = 0.85f;
                _navLights.Add(GlowQuad(a, Vector3.zero, 0.9f, SoftMaterial(c), "NavLight").gameObject);
            }
            BuildDishFx();
            BuildPulseFx();
            LoadVariants();
            Debug.Log("ship: model loaded, focus " + (focus != null ? "found" : "missing") + " · dish rig " + (_dishPitch != null ? "found" : "missing") + " · rim emitters " + _rimMats.Count);
            return;
        }
        BuildPlaceholder();
    }

    readonly List<Light> _engineLights = new List<Light>();
    // the fitting variants (laser barrel, cargo pod, engine nacelle, scanner dish, three tiers each): the tier-1 set
    // comes with the model's main scene; the rest live in its second glTF scene, which the editor importer leaves out,
    // so they are read from the GLB in StreamingAssets at run time and shown by refit level as the browser's assembler does
    readonly Dictionary<string, GameObject> _variants = new Dictionary<string, GameObject>();
    static readonly string[] VARIANT_PREFIXES = { "cargo_pod", "engine_nacelle", "scanner_dish", "wings", "laser_barrel" };
    public string variantsState = "pending";

    static string VariantKind(string name)
    {
        foreach (var p in VARIANT_PREFIXES) if (name.StartsWith(p + "_")) return p;
        return null;
    }

    void CollectVariants(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (VariantKind(c.name) != null) _variants[c.name] = c.gameObject;
            CollectVariants(c);
        }
    }

    /// The alternatives from the GLB's second scene, brought in at run time: laser barrels ride the dish's pitch group,
    /// everything else sits on the hull; all hidden until ConfigureModel picks the tiers.
    async void LoadVariants()
    {
        CollectVariants(model);
        ConfigureModel();
        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "player_ship.glb");
            if (!System.IO.File.Exists(path)) { variantsState = "no glb"; return; }
            var bytes = System.IO.File.ReadAllBytes(path);
            var gltf = new GLTFast.GltfImport();
            bool ok = await gltf.Load(bytes);
            if (!ok || this == null || model == null) { variantsState = "load failed"; return; }
            var holder = new GameObject("Variants");
            holder.transform.SetParent(model, false);
            ok = await gltf.InstantiateSceneAsync(holder.transform, 1);
            if (!ok || this == null || model == null) { variantsState = "scene 2 failed"; return; }
            var found = new List<Transform>();
            foreach (var mr in holder.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = mr.transform;
                if (VariantKind(t.name) == null) continue;
                found.Add(t);
            }
            int added = 0;
            foreach (var t in found)
            {
                if (_variants.ContainsKey(t.name)) continue;
                var parent = VariantKind(t.name) == "laser_barrel" && _dishPitch != null ? _dishPitch : model;
                t.SetParent(parent, true);   // the pose in the model's frame is kept
                t.gameObject.SetActive(false);
                _variants[t.name] = t.gameObject;
                added++;
            }
            variantsState = "loaded " + added + " from scene 2";
            ConfigureModel();
        }
        catch (System.Exception e)
        {
            variantsState = "error " + e.Message;
            Debug.LogWarning("ship: variants " + e.Message);
        }
    }

    /// playerVisualTier / configure: each fitting shows the tier its refit level has reached (1 to 3); the wings stay delta.
    public void ConfigureModel()
    {
        if (_variants.Count == 0) return;
        var tiers = new Dictionary<string, int>();
        foreach (var pair in new[] { new[] { "laser", "laser_barrel" }, new[] { "cargo", "cargo_pod" }, new[] { "engine", "engine_nacelle" }, new[] { "scanner", "scanner_dish" } })
        {
            int levels = Data.UPGRADES[pair[0]].levels.Length;
            tiers[pair[1]] = 1 + Mathf.RoundToInt(2f * State.up[pair[0]] / (levels - 1));
        }
        foreach (var kv in _variants)
        {
            string kind = VariantKind(kv.Key);
            bool shown = kind == "wings" ? kv.Key == "wings_delta" : (tiers.ContainsKey(kind) && kv.Key == kind + "_" + tiers[kind]);
            if (kv.Value.activeSelf != shown) kv.Value.SetActive(shown);
        }
    }

    public string VariantReport()
    {
        var shown = new List<string>();
        foreach (var kv in _variants) if (kv.Value.activeSelf) shown.Add(kv.Key);
        shown.Sort();
        return string.Join(", ", shown.ToArray()) + " · " + variantsState;
    }
    // the engines' exhaust glows, the navigation lights (exhausts / navLights) and the radar pulse you can see
    readonly List<Transform> _exhausts = new List<Transform>();
    readonly List<GameObject> _navLights = new List<GameObject>();
    Material _exhaustMat;
    Transform _pulseSphere, _pulseRing;
    Material _pulseSphereMat, _pulseRingMat;
    float _pulseT = -1f, _pulseRange;
    Vector3 _pulseOrigin;   // true

    /// A soft radial glow texture for the sprites (the browser's texSoft / exhaustTex).
    static Texture2D _soft;
    public static Texture2D SoftTexture()
    {
        if (_soft != null) return _soft;
        _soft = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                _soft.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        _soft.wrapMode = TextureWrapMode.Clamp;
        _soft.Apply();
        return _soft;
    }

    /// A billboard quad with a soft additive glow, parented under a model node.
    public static Transform GlowQuad(Transform parent, Vector3 localPos, float size, Material mat, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = MeshUtil.Quad(1f, 1f, 1f, 1f);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<FaceCamera>();
        return go.transform;
    }

    public static Material SoftMaterial(Color c)
    {
        var m = new Material(Game.Sh("BeltRunner/Field"));
        m.mainTexture = SoftTexture();
        m.SetColor("_Color", c);
        return m;
    }

    /// The radar pulse: a faint sphere and a bright ring (in the XY plane, as the browser's) that grow to scanner range.
    void BuildPulseFx()
    {
        var sg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(sg.GetComponent<Collider>());
        sg.name = "PulseSphere";
        _pulseSphereMat = new Material(Game.Sh("BeltRunner/Spark"));
        _pulseSphereMat.SetColor("_Color", new Color(0.37f, 0.83f, 0.94f, 0.05f));
        var smr = sg.GetComponent<MeshRenderer>();
        smr.sharedMaterial = _pulseSphereMat;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _pulseSphere = sg.transform;
        sg.SetActive(false);
        var rg = new GameObject("PulseRing");
        rg.AddComponent<MeshFilter>().sharedMesh = MeshUtil.Torus(1f, 0.0075f, 128, 6);
        _pulseRingMat = new Material(Game.Sh("BeltRunner/Spark"));
        _pulseRingMat.SetColor("_Color", new Color(0.56f, 0.91f, 1f, 0.5f));
        var rmr = rg.AddComponent<MeshRenderer>();
        rmr.sharedMaterial = _pulseRingMat;
        rmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rg.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _pulseRing = rg.transform;
        rg.SetActive(false);
    }

    void TickPulse(float dt)
    {
        if (_pulseT < 0f) return;
        _pulseT += dt;
        float t = _pulseT;
        if (t >= Data.PULSE_TIME + 0.4f)
        {
            _pulseT = -1f;
            _pulseSphere.gameObject.SetActive(false);
            _pulseRing.gameObject.SetActive(false);
            return;
        }
        float r = _pulseRange * Mathf.Min(1f, t / Data.PULSE_TIME);
        float f = 1f - Mathf.Min(1f, t / Data.PULSE_TIME);
        var at = _pulseOrigin - game.worldOffset;
        _pulseSphere.gameObject.SetActive(true);
        _pulseSphere.position = at;
        _pulseSphere.localScale = Vector3.one * Mathf.Max(1f, r) * 2f;
        _pulseSphereMat.SetColor("_Color", new Color(0.37f, 0.83f, 0.94f, 0.05f * f));
        _pulseRing.gameObject.SetActive(true);
        _pulseRing.position = at;
        _pulseRing.localScale = Vector3.one * Mathf.Max(1f, r);
        _pulseRingMat.SetColor("_Color", new Color(0.56f, 0.91f, 1f, 0.55f * f));
    }

    public bool PulseVisible { get { return _pulseT >= 0f; } }

    /// The exhaust glows swell and brighten with thrust, the navigation lights blink, the engine light comes on under thrust.
    void TickEngineFx()
    {
        if (_exhaustMat != null)
        {
            float ex = thrusting ? 0.35f + 0.6f * throttle + Random.value * 0.1f : 0.15f;
            float es = thrusting ? (afterburning ? 30f : 5f + 9f * throttle) + Random.value * (afterburning ? 10f : 4f) : 5f;
            // on the burner the glow goes white-blue and flickers hard
            _exhaustMat.SetColor("_Color", afterburning ? new Color(0.78f, 0.9f, 1f, 0.7f + Random.value * 0.3f) : new Color(0.37f, 0.83f, 0.94f, ex));
            foreach (var q in _exhausts) q.localScale = Vector3.one * es;
            // the plume: puffs of pale jet streaming off every engine while the burner is lit
            if (afterburning && game.explosions != null)
            {
                _jetT -= Time.deltaTime;
                while (_jetT <= 0f)
                {
                    _jetT += 0.025f;
                    foreach (var q in _exhausts)
                    {
                        var p = q.position + game.worldOffset - Forward * Random.Range(2f, 9f) + Random.insideUnitSphere * 1.5f;
                        game.explosions.Jet(p, vel - Forward * Random.Range(160f, 260f) + Random.insideUnitSphere * 12f);
                    }
                }
            }
        }
        bool blink = Mathf.Repeat(State.time, 1.2f) < 0.12f;
        foreach (var l in _navLights) if (l.activeSelf != blink) l.SetActive(blink);
        foreach (var l in _engineLights) l.intensity = thrusting ? (afterburning ? 2.5f : 1.2f) : 0f;
    }

    public static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// The flashlight: the HTML torch (SpotLight 0xfff1d6, 14000 cd, reach 7000, half-angle pi/8, decay 1), on by default,
    /// just below the nose and aimed a touch down. Unity spot falloff is not 1/d, so the range and intensity are chosen
    /// to light a rock at a few hundred units about as the browser does.
    void BuildTorch()
    {
        var go = new GameObject("Torch");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -1.5f, 21f) * Data.SHIP_SCALE;
        go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -4.5f, 2000f).normalized, Vector3.up);
        torch = go.AddComponent<Light>();
        torch.type = LightType.Spot;
        torch.color = Data.Hex("#fff1d6");
        torch.intensity = 6f;
        torch.range = 2600f;
        torch.spotAngle = 45f;
        torch.innerSpotAngle = 26f;
        torch.shadows = LightShadows.None;
    }

    public void ToggleTorch()
    {
        torchOn = !torchOn;
        game.Toast(torchOn ? "Flashlight on" : "Flashlight off", false);
    }

    /// The rig nodes, a glow at each rim emitter, six thin beams from the rims to the focus (shown while the beam
    /// cuts) and a glow at the focus, all under the pitch node so they ride the dish.
    void BuildDishFx()
    {
        _dishYaw = FindDeep(model, "mining_dish_yaw");
        _dishPitch = FindDeep(model, "mining_dish_pitch");
        var mount = FindDeep(model, "dish_mount");
        if (mount != null) _dishMount = model.InverseTransformPoint(mount.position);
        else if (_dishYaw != null) _dishMount = model.InverseTransformPoint(_dishYaw.position);
        if (_dishPitch == null) return;
        CalibrateDish();
        var sparkSh = Game.Sh("BeltRunner/Spark");
        _beamRimMat = new Material(sparkSh);
        _beamRimMat.SetColor("_Color", new Color(1f, 0.77f, 0.4f, 0.8f));
        _focusMat = new Material(sparkSh);
        _focusMat.SetColor("_Color", new Color(1f, 0.77f, 0.4f, 0.1f));
        // the model's focus and rim empties import at the origin (their positions are baked away), so the emitters sit
        // on a ring round the dish bowl (2.6 across, 0.7 ahead of the pitch pivot) and the focus a little way ahead of it
        var fp = FOCUS_LOCAL;
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI * 2f / 6f;
            var rp = new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 1.15f, 1.15f);
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = "RimGlow" + k;
            g.transform.SetParent(_dishPitch, false);
            g.transform.localPosition = rp;
            g.transform.localScale = Vector3.one * 0.44f;
            var rm = new Material(sparkSh);
            rm.SetColor("_Color", new Color(0.56f, 0.91f, 1f, 0.12f));
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterial = rm;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rimMats.Add(rm);
            var d = fp - rp;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(beam.GetComponent<Collider>());
            beam.name = "RimBeam" + k;
            beam.transform.SetParent(_dishPitch, false);
            beam.transform.localPosition = (rp + fp) * 0.5f;
            beam.transform.localRotation = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) < 0.98f ? Vector3.up : Vector3.right) * Quaternion.AngleAxis(90f, Vector3.right);
            beam.transform.localScale = new Vector3(0.05f, d.magnitude * 0.5f, 0.05f);
            var bmr = beam.GetComponent<MeshRenderer>();
            bmr.sharedMaterial = _beamRimMat;
            bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.SetActive(false);
            _rimBeams.Add(beam.transform);
        }
        var fg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(fg.GetComponent<Collider>());
        fg.name = "FocusGlow";
        fg.transform.SetParent(_dishPitch, false);
        fg.transform.localPosition = fp;
        fg.transform.localScale = Vector3.one * 0.9f;
        var fmr = fg.GetComponent<MeshRenderer>();
        fmr.sharedMaterial = _focusMat;
        fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _focusGlow = fg.transform;
    }

    static readonly Vector3 FOCUS_LOCAL = new Vector3(0f, 0f, 2.4f);   // the focus lens, in the pitch node's frame

    /// Where the beam starts, in scene coordinates: the focus ahead of the dish when the rig is there.
    public Vector3 DishFocusScene()
    {
        if (_dishPitch != null) return _dishPitch.TransformPoint(FOCUS_LOCAL);
        return focus != null ? focus.position : transform.position + Forward * 20f + transform.up * -1.5f * Data.SHIP_SCALE;
    }

    /// The rig's yaw and pitch signs are found by trial: a point ahead, right and up in the model's frame is aimed at
    /// with every sign pair, and the pair that turns the dish axis (the pitch node's +Z) closest to it wins (glTFast
    /// mirrors the model's X on import).
    void CalibrateDish()
    {
        if (_dishYaw == null || _dishPitch == null) return;
        var yq = _dishYaw.localRotation;
        var pq = _dishPitch.localRotation;
        var probe = model.TransformPoint(_dishMount + new Vector3(40f, 25f, 100f));
        float bestErr = float.PositiveInfinity; float by = 1f, bp = 1f;
        foreach (float ys in new[] { 1f, -1f })
        {
            foreach (float ps in new[] { 1f, -1f })
            {
                _dishYawSign = ys;
                _dishPitchSign = ps;
                float yaw, pitch;
                DishAngles(probe, out yaw, out pitch);
                ApplyDish(yaw, pitch);
                float err = Vector3.Angle(_dishPitch.TransformDirection(Vector3.forward), probe - _dishPitch.position);
                if (err < bestErr) { bestErr = err; by = ys; bp = ps; }
            }
        }
        _dishYawSign = by;
        _dishPitchSign = bp;
        _dishYaw.localRotation = yq;
        _dishPitch.localRotation = pq;
        Debug.Log("ship: dish rig calibrated · yaw sign " + by + " pitch sign " + bp + " · error " + bestErr.ToString("0.0") + " deg");
    }

    /// The yaw and pitch (in the model's frame, from the dish mount) that would put the focus on a scene point.
    void DishAngles(Vector3 sceneP, out float yaw, out float pitch)
    {
        var L = model.InverseTransformPoint(sceneP) - _dishMount;
        yaw = Mathf.Atan2(L.x, L.z);
        pitch = Mathf.Atan2(L.y, Mathf.Sqrt(L.x * L.x + L.z * L.z));
    }

    void ApplyDish(float yaw, float pitch)
    {
        _dishYaw.localRotation = Quaternion.AngleAxis(_dishYawSign * yaw * Mathf.Rad2Deg, Vector3.up);
        _dishPitch.localRotation = Quaternion.AngleAxis(_dishPitchSign * pitch * Mathf.Rad2Deg, Vector3.right);
    }

    /// How far the dish axis is off the line to the aim point right now, in degrees (for the smoke run).
    public float DishRigError()
    {
        if (_dishPitch == null || target < 0 || target >= belt.count) return 0f;
        var aim = belt.RockPos(target) - game.worldOffset;
        return Vector3.Angle(_dishPitch.TransformDirection(Vector3.forward), aim - _dishPitch.position);
    }

    /// The dish swings onto whatever the beam is going to (fast, a few radians a second) and settles forward when
    /// idle; it only covers the forward half, 90 degrees either side of the nose.
    void TickDish(float dt)
    {
        if (_dishYaw == null || _dishPitch == null) return;
        bool gunAim = weapon != "laser" && !docked;
        bool hasAim = gunAim || (target >= 0 && target < belt.count && !docked) || (raiderTarget != null && !docked);
        float wantYaw = 0f, wantPitch = 0.05f;
        if (gunAim) DishAngles(LaserOrigin() + aimDir * 400f - game.worldOffset, out wantYaw, out wantPitch);
        else if (hasAim) DishAngles((target >= 0 ? belt.RockPos(target) : raiderTarget.pos) - game.worldOffset, out wantYaw, out wantPitch);
        bool inArc = Mathf.Abs(wantYaw) <= Mathf.PI * 0.5f;
        wantYaw = Mathf.Clamp(wantYaw, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
        wantPitch = Mathf.Clamp(wantPitch, -0.7f, 1.3f);
        float rate = 3.5f * dt;
        aimYaw += Mathf.Clamp(wantYaw - aimYaw, -rate, rate);
        aimPitch += Mathf.Clamp(wantPitch - aimPitch, -rate, rate);
        aimed = hasAim && inArc && Mathf.Abs(wantYaw - aimYaw) < 0.05f && Mathf.Abs(wantPitch - aimPitch) < 0.05f;
        ApplyDish(aimYaw, aimPitch);
        if (_rimMats.Count == 0) return;
        float t = State.time;
        bool aiming = !firing && hasAim;
        float ra = laserOn || gunFiring ? 0.7f + Random.value * 0.3f : (aiming ? 0.3f + 0.25f * Mathf.Sin(t * 9f) : 0.12f);
        foreach (var m in _rimMats) m.SetColor("_Color", new Color(0.56f, 0.91f, 1f, ra));
        _focusMat.SetColor("_Color", new Color(1f, 0.77f, 0.4f, laserOn ? 0.85f + Random.value * 0.15f : (aiming ? 0.2f + 0.15f * Mathf.Sin(t * 9f) : 0.1f)));
        if (_focusGlow != null) _focusGlow.localScale = Vector3.one * (laserOn ? 1.4f : 0.9f);
        _beamRimMat.SetColor("_Color", new Color(1f, 0.77f, 0.4f, 0.6f + Random.value * 0.35f));
        foreach (var b in _rimBeams) if (b.gameObject.activeSelf != laserOn) b.gameObject.SetActive(laserOn);
    }

    void BuildLaser()
    {
        var lg = new GameObject("Laser");
        lg.transform.SetParent(transform, false);
        _laser = lg.AddComponent<LineRenderer>();
        _laser.useWorldSpace = true;
        _laser.positionCount = 2;
        _laser.startWidth = 2.2f;
        _laser.endWidth = 1.2f;
        var lm = new Material(Game.Sh("Sprites/Default"));
        lm.color = new Color(1f, 0.62f, 0.2f, 0.95f);
        _laser.material = lm;
        _laser.startColor = new Color(1f, 0.7f, 0.25f);
        _laser.endColor = new Color(1f, 0.45f, 0.1f);
        _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _laser.enabled = false;
    }

    void BuildPlaceholder()
    {
        // a placeholder hull when the model is missing: a stretched body, two wings, an engine block
        var s = Data.SHIP_SCALE;
        var hull = new Material(Game.Sh("Standard"));
        hull.color = new Color(0.78f, 0.8f, 0.84f);
        hull.SetFloat("_Metallic", 0.6f);
        hull.SetFloat("_Glossiness", 0.55f);
        Part(PrimitiveType.Capsule, new Vector3(0f, 0f, 2f) * s, new Vector3(6f, 15f, 4f) * s, Quaternion.Euler(90f, 0f, 0f), hull);   // the capsule is 2 long on its Y axis, laid along the nose
        Part(PrimitiveType.Cube, new Vector3(0f, -0.5f, -4f) * s, new Vector3(34f, 0.8f, 9f) * s, Quaternion.identity, hull);
        Part(PrimitiveType.Cube, new Vector3(0f, 2.5f, -9f) * s, new Vector3(3f, 6f, 5f) * s, Quaternion.identity, hull);
        var glow = new Material(Game.Sh("Standard"));
        glow.color = new Color(0.37f, 0.83f, 0.94f);
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", new Color(0.37f, 0.83f, 0.94f) * 3f);
        Part(PrimitiveType.Sphere, new Vector3(-3f, 0f, -13f) * s, Vector3.one * 3f * s, Quaternion.identity, glow);
        Part(PrimitiveType.Sphere, new Vector3(3f, 0f, -13f) * s, Vector3.one * 3f * s, Quaternion.identity, glow);
    }

    void Part(PrimitiveType kind, Vector3 at, Vector3 size, Quaternion q, Material m)
    {
        var go = GameObject.CreatePrimitive(kind);
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        go.transform.localRotation = q;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// The steering curve: a small dead zone, then a curve that starts gentle and ends at full rate.
    public static float Shape(float v)
    {
        // the mouse response (2026-09-15, quicker on the user's call): a small dead zone, the full turn rate reached
        // at 55% of the way to the screen's edge, and a straighter curve so the nose moves as soon as the mouse does
        float m = Mathf.Abs(v);
        const float dz = 0.025f, full = 0.55f;
        if (m < dz) return 0f;
        float t = Mathf.Min(1f, (m - dz) / (full - dz));
        return Mathf.Sign(v) * t * (0.65f + 0.35f * t);
    }

    /// A level heading (no roll) with the nose along `d`.
    public static Quaternion LevelHeading(Vector3 d)
    {
        if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
        return Quaternion.LookRotation(d.normalized, Vector3.up);
    }

    public void Tick(float dt)
    {
        if (torch != null) torch.enabled = torchOn && !docked && warp == null;
        TickPulse(dt);
        State.TickShield(dt);
        TickFuelWarnings();
        TickEngineFx();
        // passing through a mouth's force field flashes it, under approach control or on your own
        if (carrier != null && !carrier.hold && warp == null)
        {
            var fl = carrier.ToLocalTrue(TruePos);
            if (Mathf.Abs(fl.x) < CargoShip.BAY_X1 + 40f && Mathf.Abs(fl.y) < CargoShip.BAY_Y1 + 40f && Mathf.Abs(Mathf.Abs(fl.z) - CargoShip.BAY_Z_OUT) < 70f) carrier.FlashField(fl.z > 0f ? 1 : -1);
        }
        if (_announceAt > 0f && Time.time >= _announceAt) { _announceAt = -1f; if (docked && !hold) Audio.Announce("hangar_" + Random.Range(1, 5)); }
        if (_colonyAt > 0f && Time.time >= _colonyAt) { _colonyAt = -1f; if (docked && hold) Audio.Say("colony_control"); }
        if (warp != null)
        {
            if (Input.GetKeyDown(KeyCode.Space)) warp.skip = true;
            WarpUpdate(dt);
            return;
        }
        if (cut != null)
        {
            if (Input.GetKeyDown(KeyCode.Space)) SkipCut();
            CutUpdate(dt);
            return;
        }
        if (recovery != null) RecoveryUpdate(dt);
        if (docked)
        {
            DockUpdate(dt);
            return;
        }
        _nearT += dt;
        if (_nearT > 0.5f)
        {
            _nearT = 0f;
            nearRocks = belt.RocksWithin(TruePos, 12000f);
        }
        hitCd = Mathf.Max(0f, hitCd - dt);
        _prevPos = transform.position;
        Fly(dt);
        CarrierContact();
        if (docked) return;
        RockContact();
        CheckBreach();
        if (!CanFly)
        {
            // systems down: nothing answers, the ship drifts until it is recovered
            laserOn = false;
            firing = false;
            target = -1;
            if (_laser != null) _laser.enabled = false;
            hover = null;
            return;
        }
        if (++_hoverFrame % 6 == 0) hover = HoverPick();
        TickLock();
        TickLaser(dt);
        TickDish(dt);
        radarCd = Mathf.Max(0f, radarCd - dt);
        if (Input.GetKeyDown(KeyCode.R)) Radar();
        if (Input.GetKeyDown(KeyCode.G)) ToggleOvercharge();
        if (Input.GetKeyDown(KeyCode.F)) ToggleTorch();
        if (Input.GetKeyDown(KeyCode.H)) StartApproach();   // H: E is yaw now
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetMouseButtonDown(2)) ToggleLock();   // Z or the middle mouse button (Q is yaw now)
        if (Input.GetKeyDown(KeyCode.T)) CallRecovery();
    }

    // ---- the Q lock
    /// What the mouse is over (hoverPick): every live rock within 120 km of the camera and the cargo ship are projected to
    /// the screen, and the nearest one whose disc (with a 10 px minimum so distant rocks stay hoverable) contains the
    /// cursor wins. dist is measured from the nose to the surface, in the same units as laser reach.
    public HoverInfo HoverPick()
    {
        if (docked || cut != null || warp != null || !CanFly || game.hud == null || game.hud.InvOpen || game.hud.MapOpen || game.hud.MenuVisible) return null;
        var m = Input.mousePosition;
        if (m.x < 0f || m.y < 0f || m.x > Screen.width || m.y > Screen.height) return null;
        float f = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
        var origin = LaserOrigin();
        var camTrue = cam.transform.position + game.worldOffset;
        HoverInfo best = null;
        float bd = float.PositiveInfinity;
        bool combat = InCombat;   // under attack only raiders can be picked and locked
        if (!combat) foreach (int i in belt.RocksNear(camTrue, 120000f))
        {
            var p = belt.RockPos(i);
            float cd = (camTrue - p).magnitude;
            if (cd > 120000f || cd >= bd) continue;
            var sp = cam.WorldToScreenPoint(p - game.worldOffset);
            if (sp.z < 0f) continue;
            float r = belt.radius[i];
            float pr = Mathf.Max(10f, r * (Screen.height * 0.5f) / (cd * f)) * 1.15f;
            float dx = sp.x - m.x, dy = sp.y - m.y;
            if (dx * dx + dy * dy > pr * pr) continue;
            bd = cd;
            best = new HoverInfo { kind = "rock", rock = i, dist = Mathf.Max(0f, (origin - p).magnitude - r), name = belt.RockName(i) };
        }
        if (game.raiders != null)
        {
            // raiders are easy to pick: a generous circle round each (at least 150 px, or 2.5 times its drawn size), the one
            // nearest the mouse on screen wins, and a raider beats any rock under the mouse
            float bestPx = float.PositiveInfinity;
            foreach (var r in game.raiders.raiders)
            {
                float cd = (camTrue - r.pos).magnitude;
                if (cd > 120000f) continue;
                var sp = cam.WorldToScreenPoint(r.pos - game.worldOffset);
                if (sp.z < 0f) continue;
                float pr = Mathf.Max(150f, Raiders.RADIUS * (Screen.height * 0.5f) / (cd * f) * 2.5f);
                float dx = sp.x - m.x, dy = sp.y - m.y;
                float px = dx * dx + dy * dy;
                if (px > pr * pr || px >= bestPx) continue;
                bestPx = px;
                bd = 0f;   // nothing else can beat a raider
                best = new HoverInfo { kind = "raider", raider = r, dist = Mathf.Max(0f, (origin - r.pos).magnitude - Raiders.RADIUS), name = "Raider" };
            }
        }
        if (carrier != null && !carrier.hold && !combat)
        {
            var p = carrier.truePos;
            float cd = (camTrue - p).magnitude;
            if (cd < 120000f && cd < bd)
            {
                var sp = cam.WorldToScreenPoint(p - game.worldOffset);
                if (sp.z >= 0f)
                {
                    float pr = Mathf.Max(10f, CargoShip.HALF.x * (Screen.height * 0.5f) / (cd * f)) * 1.15f;
                    float dx = sp.x - m.x, dy = sp.y - m.y;
                    if (dx * dx + dy * dy <= pr * pr) best = new HoverInfo { kind = "station", rock = -1, dist = Mathf.Max(0f, (origin - p).magnitude - CargoShip.HALF.x), name = "Cargo ship" };
                }
            }
        }
        return best;
    }

    public bool HoverIsLock(HoverInfo h)
    {
        return h != null && h.kind == lockKind && (lockKind != "rock" || h.rock == lockRock) && (lockKind != "raider" || h.raider == lockRaider);
    }

    /// Q: lock the hovered target, switch to a different hovered target, or release the current lock.
    public void ToggleLock()
    {
        hover = HoverPick();   // the cursor's position now, even if it moved since the last hover pass
        if (hover != null && !HoverIsLock(hover))
        {
            lockKind = hover.kind;
            _lockT = 0f;
            lockRock = hover.rock;
            lockRaider = hover.raider;
            lockDist = hover.dist;
            game.Toast("Locked on " + hover.name, false);
        }
        else if (lockKind != "")
        {
            ReleaseLock();
            game.Toast("Lock released", false);
        }
        else game.Toast(InCombat ? "Under attack · only raiders can be locked" : "Nothing under the mouse to lock on", true);
    }

    /// The smoke run's Q: a lock on a given rock.
    public void LockOnRock(int i)
    {
        lockKind = "rock";
        _lockT = 0f;
        lockRock = i;
        lockDist = Mathf.Max(0f, (belt.RockPos(i) - LaserOrigin()).magnitude - belt.radius[i]);
        game.Toast("Locked on " + belt.RockName(i), false);
    }

    /// The smoke run's Q on a raider.
    public void LockOnRaider(Raiders.Raider r)
    {
        lockKind = "raider";
        _lockT = 0f;
        lockRaider = r;
        lockRock = -1;
        lockDist = Mathf.Max(0f, (r.pos - LaserOrigin()).magnitude - Raiders.RADIUS);
        game.Toast("Locked on Raider", false);
    }

    /// Under attack: raiders are attacking right now. Only they can be locked; a rock or cargo ship lock is dropped.
    public bool InCombat { get { return game.raiders != null && game.raiders.threat > 0; } }

    public void ReleaseLock()
    {
        lockKind = "";
        lockRock = -1;
        lockRaider = null;
        lockDist = 0f;
    }

    public Vector3 LockPos()
    {
        if (lockKind == "raider" && lockRaider != null) return lockRaider.pos;
        return lockKind == "rock" ? belt.RockPos(lockRock) : carrier.truePos;
    }

    public string LockName()
    {
        return lockKind == "rock" ? belt.RockName(lockRock) : (lockKind == "raider" ? "Raider" : "Cargo ship");
    }

    /// The lock lapses when its rock breaks up or it falls far out of range (the cargo ship never goes away).
    void TickLock()
    {
        if (lockKind == "") return;
        if (lockKind != "raider" && InCombat) { ReleaseLock(); game.Toast("Under attack · lock dropped · only raiders can be locked", true); return; }
        bool gone = (lockKind == "rock" && (lockRock >= belt.count || !belt.alive[lockRock])) || (lockKind == "raider" && (lockRaider == null || lockRaider.dead));
        float r = lockKind == "rock" && !gone ? belt.radius[lockRock] : (lockKind == "raider" ? Raiders.RADIUS : CargoShip.HALF.x);
        lockDist = gone ? 0f : Mathf.Max(0f, (LockPos() - LaserOrigin()).magnitude - r);
        if (gone || lockDist > LOCK_RANGE)
        {
            game.Toast(gone ? (lockKind == "raider" ? "Lock lost · raider destroyed" : "Lock lost · rock broke up") : "Lock lost · out of range", true);
            ReleaseLock();
        }
    }

    // ---- recovery
    /// A breach: plating gone, the ship is disabled and recovery is automatic. Under a quarter, one warning.
    void CheckBreach()
    {
        float hp = State.Stat("hull").hp;
        if (State.hull <= 0f && recovery == null) StartRecovery("breach");
        else if (State.hull > 0f && State.hull < hp * 0.25f && !_lowHullWarned)
        {
            _lowHullWarned = true;
            Audio.Play("alarm");
            game.Toast("Hull under 25% · the pad mends it, one repair part per point", true);
        }
    }

    /// T: a dry tank calls for recovery.
    public void CallRecovery()
    {
        if (docked || recovery != null) return;
        if (State.fuel > 0.5f)
        {
            game.Toast("Fuel in the tank · recovery only comes for a dry ship", true);
            return;
        }
        StartRecovery("fuel");
    }

    public void StartRecovery(string reason)
    {
        if (recovery != null || docked) return;
        recovery = new Recovery { reason = reason };
        ReleaseLock();
        throttle = 0f;
        laserOn = false;
        firing = false;
        if (_laser != null) _laser.enabled = false;
        if (reason == "breach")
        {
            disabled = true;
            game.hud.WreckFlash();
            Audio.Play("alarm");
            StartDeath(recovery);
            game.Toast("Hull breach · systems down · recovery beacon sent", true);
            // raiders that were on you strip the hold and leave
            if (game.raiders != null && game.raiders.AnyAttacking)
            {
                float lost = 0f;
                foreach (var k in Data.ORE_KEYS) { float l = State.cargo[k] * 0.35f; State.cargo[k] -= l; lost += l; }
                game.raiders.StandDown();
                if (lost > 0.5f) game.Toast("Raiders stripped " + Mathf.FloorToInt(lost) + " of cargo from the hold", true);
            }
        }
        else game.Toast("Recovery requested · returning to the cargo ship", false);
    }

    /// The death: the same six ends a raider gets. The blast goes off at once and the model vanishes into it; the
    /// other kinds tumble on for a few seconds, out of control, before the fade. The fade's timing follows the kind.
    void StartDeath(Recovery R)
    {
        float roll = Random.value;
        R.kind = roll < 0.3f ? "blast" : roll < 0.5f ? "burn" : roll < 0.65f ? "chain" : roll < 0.78f ? "runaway" : roll < 0.9f ? "vent" : "dead";
        R.axis = Random.onUnitSphere;
        R.spin = Random.Range(15f, 40f);
        float show = R.kind == "blast" ? 2.4f : R.kind == "chain" ? 2.6f : R.kind == "runaway" ? 3f : 4.5f;   // seconds before the fade starts
        R.at = show + 1.6f;
        R.dur = R.at + 1.6f;
        var p = TruePos;
        switch (R.kind)
        {
            case "blast":
                Audio.Play("boom_big");
                if (game.explosions != null) game.explosions.Raider(p, vel);
                if (model != null) model.gameObject.SetActive(false);
                shake = 1f;
                break;
            case "runaway":
                Audio.Play("hit");
                R.spin = Random.Range(30f, 70f);
                R.axis = (Forward * 0.6f + Random.insideUnitSphere).normalized;
                break;
            case "dead":
                Audio.Play("hit");
                R.spin = Random.Range(50f, 90f);
                break;
            default:
                Audio.Play("hit");
                break;
        }
        if (game.sparks != null) game.sparks.Burst(p, 80, 220f, Data.Hex("#ffb060"), 1.4f, vel);
    }

    /// The wreck's tumble and its fire, frame by frame, until the fade takes it.
    void DeathUpdate(Recovery R, float dt)
    {
        if (R.kind == "" || R.kind == "blast" || R.done) return;
        var p = TruePos;
        switch (R.kind)
        {
            case "burn":
                R.spin = Mathf.Min(220f, R.spin + 45f * dt);
                R.axis = (R.axis + Random.insideUnitSphere * 0.4f * dt).normalized;
                break;
            case "chain": R.spin = Mathf.Min(60f, R.spin + 20f * dt); break;
            case "runaway": vel += Forward * 420f * dt; R.spin = Mathf.Min(140f, R.spin + 30f * dt); break;
            case "vent": break;
            case "dead": break;
        }
        transform.Rotate(R.axis, R.spin * dt, Space.World);
        if (game.explosions == null) return;
        var ex = game.explosions;
        switch (R.kind)
        {
            case "burn":
                R.flameT -= dt; while (R.flameT <= 0f) { R.flameT += 0.045f; ex.Flame(p + Random.insideUnitSphere * 10f, vel + Random.insideUnitSphere * 12f); }
                R.smokeT -= dt; while (R.smokeT <= 0f) { R.smokeT += 0.11f; ex.Smoke(p + Random.insideUnitSphere * 8f, vel + Random.insideUnitSphere * 8f); }
                R.sparkT -= dt; if (R.sparkT <= 0f && game.sparks != null) { R.sparkT = Random.Range(0.08f, 0.3f); game.sparks.Burst(p + Random.insideUnitSphere * 8f, Random.Range(3, 9), 120f, Data.Hex("#ffc070"), 1.2f, vel); }
                break;
            case "chain":
                R.popT -= dt;
                if (R.popT <= 0f)
                {
                    R.popT = Random.Range(0.1f, 0.2f);
                    var at = p + transform.right * Random.Range(-14f, 14f) + Forward * Random.Range(-10f, 8f);
                    ex.Pop(at, vel); ex.Smoke(at, vel + Random.insideUnitSphere * 10f);
                    if (game.sparks != null) game.sparks.Burst(at, 12, 160f, Data.Hex("#ffb060"), 1.3f, vel);
                    Audio.Play("hit", -4f);
                    shake = Mathf.Max(shake, 0.4f);
                }
                break;
            case "runaway":
                R.flameT -= dt; while (R.flameT <= 0f) { R.flameT += 0.03f; ex.Flame(p - Forward * Random.Range(10f, 22f) + Random.insideUnitSphere * 4f, vel - Forward * 60f); }
                R.smokeT -= dt; while (R.smokeT <= 0f) { R.smokeT += 0.07f; ex.Smoke(p - Forward * 18f + Random.insideUnitSphere * 5f, vel - Forward * 40f); }
                break;
            case "vent":
                R.ventT -= dt;
                if (R.ventT <= 0f)
                {
                    R.ventT = Random.Range(0.35f, 0.9f);
                    var at = p + transform.right * Random.Range(-14f, 14f) + Forward * Random.Range(-8f, 8f);
                    var dirOut = (at - p).sqrMagnitude > 1f ? (at - p).normalized : Random.onUnitSphere;
                    for (int k = 0; k < 6; k++) ex.Jet(at + dirOut * k * 4f, vel + dirOut * Random.Range(60f, 140f) + Random.insideUnitSphere * 15f);
                    ex.Smoke(at, vel + dirOut * 40f);
                    if (game.sparks != null) game.sparks.Burst(at, 14, 160f, Data.Hex("#ffd090"), 1.2f, vel + dirOut * 60f);
                    vel -= dirOut * Random.Range(6f, 14f);
                    R.spin = Mathf.Min(160f, R.spin + Random.Range(10f, 30f));
                    R.axis = (R.axis + Random.insideUnitSphere * 0.5f).normalized;
                    Audio.Play("laser_off", -4f);
                }
                R.smokeT -= dt; while (R.smokeT <= 0f) { R.smokeT += 0.2f; ex.Smoke(p + Random.insideUnitSphere * 6f, vel + Random.insideUnitSphere * 6f); }
                break;
            case "dead":
                R.arcT -= dt;
                if (R.arcT <= 0f)
                {
                    R.arcT = Random.Range(0.3f, 1.2f);
                    var at = p + Random.insideUnitSphere * 14f;
                    ex.Arc(at, vel);
                    if (game.sparks != null) game.sparks.Burst(at, Random.Range(6, 16), 90f, Data.Hex("#bfe8ff"), 1.1f, vel);
                    Audio.Play("zap", -6f);
                }
                break;
        }
    }

    /// Vega calls the fuel at 50%, 25% and 10% of the tank, once each on the way down; a refill above a mark re-arms it.
    void TickFuelWarnings()
    {
        float tank = State.Stat("tank").cap;
        if (tank <= 0f) return;
        float f = State.fuel / tank;
        if (f > 0.5f) _fuelStage = 0; else if (f > 0.25f && _fuelStage > 1) _fuelStage = 1; else if (f > 0.1f && _fuelStage > 2) _fuelStage = 2;
        if (docked || !game.started || recovery != null) return;
        if (f <= 0.1f && _fuelStage < 3) { _fuelStage = 3; Audio.Say("vega_fuel10"); game.Toast("Vega · fuel critical, 10%", true); }
        else if (f <= 0.25f && _fuelStage < 2) { _fuelStage = 2; Audio.Say("vega_fuel25"); game.Toast("Vega · fuel low, 25%", true); }
        else if (f <= 0.5f && _fuelStage < 1) { _fuelStage = 1; Audio.Say("vega_fuel50"); game.Toast("Vega · fuel, 50%", false); }
    }

    public string RecoveryStatus()
    {
        if (recovery != null) return recovery.done ? "Recovered · on the pad" : "Recovery · returning to the cargo ship";
        if (disabled) return "Hull breach · drifting";
        return "";
    }

    /// The drift dies away behind the fade; at the midpoint the ship is set down on the nearer dock's pad, the fee is
    /// charged, a breached hull patched and an empty tank topped up, and the bay takes it from there.
    void RecoveryUpdate(float dt)
    {
        var R = recovery;
        R.t += dt;
        DeathUpdate(R, dt);
        if (!docked && R.kind != "runaway") vel *= Mathf.Exp(-(R.kind == "" ? 1.5f : 0.3f) * dt);   // a wreck keeps most of its way
        if (!R.done && R.t >= R.at)
        {
            R.done = true;
            if (model != null && !model.gameObject.activeSelf) model.gameObject.SetActive(true);   // back from the blast
            int side = carrier.NearestSide(TruePos);
            transform.position = carrier.ToTrue(CargoShip.ParkLocal(side)) - game.worldOffset;
            transform.rotation = LevelHeading(carrier.Dir(CargoShip.FaceLocal(side)));
            vel = carrier.vel;
            float fee = Mathf.Floor(State.credits * 0.15f);
            State.credits -= fee;
            bool breach = R.reason == "breach";
            if (breach) State.hull = Mathf.Max(State.hull, Mathf.Round(State.Stat("hull").hp * 0.35f));
            float tank = State.Stat("tank").cap;
            if (State.fuel < tank * 0.3f) State.fuel = tank * 0.3f;
            disabled = false;
            _lowHullWarned = false;
            EnterHangar(side);
            UpdateCamera(1f);
            game.Toast("Recovered to " + CargoShip.BayName(side) + " · " + Data.Fmt(fee) + " cr fee" + (breach ? " · emergency hull patch applied" : ""), false);
        }
        if (R.t >= R.dur) recovery = null;
    }

    void Fly(float dt)
    {
        var eng = State.Stat("engine");
        // steering: the cursor's offset from screen centre yaws and pitches; A/D roll; arrow keys pitch
        float yaw = 0f, pitchUp = 0f, roll = 0f;
        bool flying = CanFly;   // nothing answers while disabled or being recovered: the ship drifts
        var mp = Input.mousePosition;
        float msx = Mathf.Clamp((mp.x - Screen.width * 0.5f) / (Screen.width * 0.5f), -1f, 1f);
        float msy = Mathf.Clamp((mp.y - Screen.height * 0.5f) / (Screen.height * 0.5f), -1f, 1f);
        rdown = mouseSteer && (Input.GetMouseButton(1) || Input.GetKey(KeyCode.C)) && game.hud != null && !game.hud.InvOpen && !game.hud.MapOpen && !game.hud.MenuVisible;   // free look: hold C (or the right button)
        if (rdown)
        {
            // free look: the mouse swings the camera instead of the ship (the ship holds its heading)
            lookYaw += Shape(msx) * 2.2f * dt;
            lookPitch = Mathf.Clamp(lookPitch - Shape(msy) * 1.8f * dt, -1.35f, 1.35f);
        }
        else
        {
            // ease the free-look camera back to straight ahead once the button is released
            lookYaw *= Mathf.Exp(-4f * dt);
            lookPitch *= Mathf.Exp(-4f * dt);
        }
        _autoSteer = flying && lockKind != "";
        if (_autoSteer)
        {
            // Q lock: the ship turns itself to put the locked object on the nose ray (a rock, the cargo ship or a raider,
            // which the ship follows round while the mouse works the gun) (the laser's line, not the camera's);
            // the mouse is ignored until the lock is released (roll is still yours). Proportional: full rate beyond about
            // eleven degrees off, easing in as the nose comes on, and the authority itself eases in over the first 0.4 s
            // of a lock so the turn starts gently rather than snapping to full rate
            _lockT += dt;
            float auth = Mathf.Clamp01(_lockT / 0.4f);
            auth = auth * auth * (3f - 2f * auth);
            // a locked raider is steered onto by its LEAD point, so the nose sits where a bolt fired now would meet it
            var steerAt = lockKind == "raider" && lockRaider != null && !lockRaider.dead ? LeadPoint(lockRaider) : LockPos();
            var L = transform.InverseTransformPoint(steerAt - game.worldOffset) - new Vector3(0f, 0f, 20f);
            float ey = Mathf.Atan2(L.x, L.z);
            float ep = Mathf.Atan2(L.y, Mathf.Sqrt(L.x * L.x + L.z * L.z));
            yaw = Mathf.Clamp(ey * 8f, -1f, 1f) * auth;      // a firmer gain than before (5), so the nose keeps up with a crossing raider
            pitchUp = Mathf.Clamp(ep * 8f, -1f, 1f) * auth;
        }
        else if (flying && mouseSteer && !rdown)
        {
            yaw = Shape(msx);
            pitchUp = Shape(msy);
        }
        if (flying)
        {
            // the user's flight scheme (2026-09-15): A and D roll, S pulls up and W pushes down, Q and E yaw, on top of the mouse aim
            if (Input.GetKey(KeyCode.A)) roll += 1f;
            if (Input.GetKey(KeyCode.D)) roll -= 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.UpArrow)) pitchUp += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.DownArrow)) pitchUp -= 1f;
            if (Input.GetKey(KeyCode.Q)) yaw -= 1f;
            if (Input.GetKey(KeyCode.E)) yaw += 1f;
        }
        yaw = Mathf.Clamp(yaw, -1f, 1f);
        pitchUp = Mathf.Clamp(pitchUp, -1f, 1f);
        drifting = flying && Input.GetKey(KeyCode.Space);
        float turn = TURN * (drifting ? DRIFT_TURN : 1f);   // the drift: the nose swings much faster
        transform.Rotate(Vector3.up, yaw * turn * dt * Mathf.Rad2Deg, Space.Self);
        transform.Rotate(Vector3.right, -pitchUp * turn * dt * Mathf.Rad2Deg, Space.Self);
        transform.Rotate(Vector3.forward, roll * 0.6f * dt * Mathf.Rad2Deg, Space.Self);
        // the camera swing follows the stick, not the lock: an automatic turn that fed it in full swung the view past the
        // target and back as the nose arrived and the deflection dropped away
        _ctlYaw = _autoSteer ? yaw * 0.25f : yaw; _ctlPitch = _autoSteer ? pitchUp * 0.25f : pitchUp; _ctlRoll = Mathf.Clamp(roll, -1f, 1f);

        // throttle: Shift raises, Ctrl lowers (the wheel too, a tenth a notch), X cuts; holding Ctrl at zero fires the retros
        if (flying)
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) throttle = Mathf.Min(1f, throttle + 0.7f * dt);
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) throttle = Mathf.Max(0f, throttle - 0.9f * dt);
            if (Input.GetKey(KeyCode.X)) throttle = 0f;
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && game.hud != null && !game.hud.InvOpen && !game.hud.MapOpen && !game.hud.MenuVisible) throttle = Mathf.Clamp01(throttle + Mathf.Sign(wheel) * 0.1f);
        }
        // the drift: hold Space and the engine cuts (the throttle setting is kept for the release) and the ship coasts on
        // along its momentum while the nose swings at 50° a second, against the usual 30; it is for turning round, not for stopping
        drifting = flying && Input.GetKey(KeyCode.Space);
        float abMult = State.Stat("thrusters").mult;
        afterburning = flying && throttle > 0f && !drifting && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && abMult > 1f && State.fuel > 0f;   // Alt: Shift is the throttle now
        float mult = afterburning ? abMult : 1f;
        thrusting = false;
        braking = false;
        var fwd = Forward;
        float spBefore = vel.magnitude;
        if (State.fuel > 0f)
        {
            if (throttle > 0f && !drifting)
            {
                vel += fwd * eng.thrust * throttle * mult * dt;
                State.fuel = Mathf.Max(0f, State.fuel - Data.FUEL_BURN * throttle * Data.BurnMult(mult) * dt);
                thrusting = true;
            }
            else if (flying && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !drifting)
            {
                float sp = vel.magnitude;
                if (sp > 1f)
                {
                    float f = Mathf.Min(sp, eng.thrust * 0.4f * dt);
                    vel -= vel / sp * f;
                    State.fuel = Mathf.Max(0f, State.fuel - Data.FUEL_BURN * 0.35f * dt);
                    braking = true;
                }
            }
        }
        // the planet's pull: inverse-square from the surface value, always toward the origin (true coordinates)
        var tp = TruePos;
        float d = tp.magnitude;
        if (d > 1f && belt.planetR > 0f)
        {
            float g = Data.GRAVITY_SURFACE * Mathf.Min(1f, (belt.planetR / d) * (belt.planetR / d));
            vel -= tp / d * g * dt;
        }
        // drag, then the speed cap (thrust never pushes past it; anything above only falls away on drag)
        float dragK = throttle > 0f ? 0.32f : 0.064f;   // at zero throttle, and in the drift, the ship all but coasts (drag 0.064, twenty times below the browser's 1.28: near-Newtonian; the S retros are the stopping)
        vel *= Mathf.Exp(-dragK * dt);
        float sp2 = vel.magnitude;
        // the throttle sets the speed: the cap is the engine's top speed times the throttle (the burner multiplies it), so half
        // throttle holds half speed; anything above the cap only falls away on drag
        float lim = Mathf.Max(eng.max * mult * throttle, spBefore * Mathf.Exp(-dragK * dt));
        if (sp2 > lim) vel *= lim / sp2;
        transform.position += vel * dt;
        // the zone edge bounces you back; the planet stops you
        tp = TruePos;
        d = tp.magnitude;
        if (d > belt.worldR)
        {
            vel = Vector3.Reflect(vel, tp / d);
            transform.position -= tp / d * (d - belt.worldR);
        }
        else if (belt.planetR > 0f && d < belt.planetR + Data.SHIP_R)
        {
            vel = Vector3.zero;
            transform.position += tp / d * (belt.planetR + Data.SHIP_R - d);
        }
    }

    /// Rocks: swept along this frame's path in short steps, then resolved against a slightly generous sphere. A hard
    /// hit costs hull and shoves the rock off its rail.
    void RockContact()
    {
        var off = game.worldOffset;
        var tp = TruePos;
        var prev = _prevPos + off;
        float stepLen = (tp - prev).magnitude;
        int kSteps = Mathf.Clamp(Mathf.CeilToInt(stepLen / 24f), 1, 64);
        float reach = 60f + stepLen;
        foreach (var i in nearRocks)
        {
            if (i >= belt.count || !belt.alive[i]) continue;
            var rp = belt.RockPos(i);
            float r = belt.radius[i] * 0.92f;
            if (Mathf.Abs(rp.x - tp.x) > r * 1.5f + reach || Mathf.Abs(rp.y - tp.y) > r * 1.5f + reach || Mathf.Abs(rp.z - tp.z) > r * 1.5f + reach) continue;
            float minD = r + Data.SHIP_R;
            for (int k = 1; k <= kSteps; k++)
            {
                var wp = Vector3.Lerp(prev, tp, (float)k / kSteps);
                var to = wp - rp;
                float d = to.magnitude;
                if (d < minD && d > 0f)
                {
                    var n = to / d;
                    tp = rp + n * minD;
                    transform.position = tp - off;
                    float vn = Vector3.Dot(vel - belt.RockVel(i), n);
                    if (vn < 0f)
                    {
                        Impact(-vn, tp - n * Data.SHIP_R);
                        vel -= n * vn * 1.4f;
                        belt.Bump(i, -n, -vn);
                    }
                    break;
                }
            }
        }
        // scrap: light chunks that the ship shoves aside (and that knock the hull at speed)
        for (int s = 0; s < belt.ScrapCount; s++)
        {
            var sp = belt.ScrapPos(s);
            float sr = belt.ScrapR(s) * 0.9f;
            if (Mathf.Abs(sp.x - tp.x) > sr + reach || Mathf.Abs(sp.y - tp.y) > sr + reach || Mathf.Abs(sp.z - tp.z) > sr + reach) continue;
            var to = tp - sp;
            float d = to.magnitude;
            float minD = sr + Data.SHIP_R;
            if (d < minD && d > 0f)
            {
                var n = to / d;
                tp = sp + n * minD;
                transform.position = tp - off;
                float vn = Vector3.Dot(vel, n);
                if (vn < 0f)
                {
                    Impact(-vn, tp - n * Data.SHIP_R);
                    vel -= n * vn * 1.4f;
                }
                belt.ScrapHit(s, n, vn, Vector3.Dot(belt.ScrapVel(s), n));
            }
        }
    }

    /// impact: a knock above the safe speed costs plating, shakes the camera, sparks and sounds.
    /// The gun's reach at the current level, in world units.
    public float GunReach { get { var g = State.Stat("gun"); return g.reach > 0f ? g.reach : 1800f; } }

    /// Where a bolt fired now would meet the raider (true coordinates): the intercept of a bolt at PLAYER_BOLT_SPEED
    /// with the raider's straight-line motion, or a simple lead when there is no solution. The point leads a little more
    /// than the pure intercept (LEAD_EXTRA), and the raider is taken from where it will be by the time the nose has
    /// swung onto it (NOSE_LAG), since a lock steers the nose and the nose is always a beat behind (the user found the
    /// pure solution missing a lot, 2026-09-15).
    public const float LEAD_EXTRA = 1.25f, NOSE_LAG = 0.22f;
    public Vector3 LeadPoint(Raiders.Raider r)
    {
        var o = LaserOrigin();
        var v = r.vel;
        var p = r.pos + v * NOSE_LAG - o;
        float s = Raiders.PLAYER_BOLT_SPEED;
        float a = Vector3.Dot(v, v) - s * s;
        float b = 2f * Vector3.Dot(p, v);
        float c = Vector3.Dot(p, p);
        float t;
        if (Mathf.Abs(a) < 1e-3f) t = b != 0f ? -c / b : 0f;
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) t = p.magnitude / s;
            else
            {
                float q = Mathf.Sqrt(disc);
                float t1 = (-b - q) / (2f * a), t2 = (-b + q) / (2f * a);
                t = t1 > 0f ? t1 : t2;
                if (t <= 0f) t = p.magnitude / s;
            }
        }
        return r.pos + v * (NOSE_LAG + t * LEAD_EXTRA);
    }

    /// The scene point to put the crosshair on so a bolt fired now flies through `trueTarget`: the aim mapping inverted.
    /// The bolt leaves the dish for the point on the mouse ray at gun range beyond the dish, so the dish sits off the
    /// camera and a target nearer than gun range is not simply where it appears on screen (the parallax).
    public Vector3 AimPointFor(Vector3 trueTarget)
    {
        var o = LaserOrigin() - game.worldOffset;
        var dir = (trueTarget - LaserOrigin()).normalized;
        if (cam == null) return trueTarget - game.worldOffset;
        var w = o - cam.transform.position;
        float R = w.magnitude + GunReach;
        float wd = Vector3.Dot(w, dir);
        float disc = wd * wd - w.sqrMagnitude + R * R;
        return o + dir * (-wd + Mathf.Sqrt(Mathf.Max(0f, disc)));
    }

    /// Damage that is not a collision (a raider's bolt): no speed threshold; the flash, the shake, sparks and the sound.
    public void Hurt(float dmg, Vector3 atTrue, string label)
    {
        if (disabled || docked || recovery != null) return;
        State.Damage(dmg);
        shake = Mathf.Min(1f, 0.25f + dmg / 50f);
        Audio.Play("hit");
        if (game.sparks != null) game.sparks.Burst(atTrue, 40 + Mathf.RoundToInt(dmg) * 2, 220f, Data.Hex("#ff7a4a"), 1.2f);
        if (!string.IsNullOrEmpty(label)) game.Toast(label + " · −" + Mathf.RoundToInt(dmg), true);
        CheckBreach();
    }

    void Impact(float speed, Vector3 atTrue)
    {
        if (hitCd > 0f) return;
        hitCd = 0.5f;
        float dmg = Mathf.Max(0f, speed - 140f) * 0.09f;
        shake = Mathf.Min(1f, 0.25f + speed / 400f);
        Audio.Play("hit");
        if (game.sparks != null) game.sparks.Burst(atTrue, 60 + Mathf.RoundToInt(dmg) * 2, 260f, Data.Hex("#ffb060"), 1.2f);
        if (dmg > 0.5f)
        {
            State.Damage(dmg);
            game.Toast("Collision · -" + Mathf.RoundToInt(dmg), true);
        }
    }

    // ---- the cargo ship: its hull, its hangar mouths, and approach control
    /// Flying into a mouth slowly enough docks the ship; anything else meeting the hull is pushed off it.
    void CarrierContact()
    {
        if (carrier == null || carrier.hold) return;
        var L = carrier.ToLocalTrue(TruePos);
        var rel = vel - carrier.vel;
        if (!carrier.InCorridor(L))
        {
            exitPending = false;   // fully clear of the bay: capture is armed again
        }
        else if (!exitPending && Mathf.Abs(L.z) < CargoShip.BAY_Z_OUT - 150f && rel.magnitude < 520f)
        {
            EnterHangar(carrier.EntrySide(L, Quaternion.Inverse(carrier.basisQ) * rel));
            return;
        }
        Vector3 cpos, cn;
        if (!carrier.Collide(L, Data.SHIP_R, out cpos, out cn)) return;
        transform.position = carrier.ToTrue(cpos) - game.worldOffset;
        var n = carrier.Dir(cn);
        float vn = Vector3.Dot(rel, n);
        if (vn < 0f)
        {
            vel += n * (-vn * 1.4f);
            if (-vn > 120f)
            {
                State.Damage((-vn - 120f) * 0.08f);   // a hard knock against the hull costs plating
                shake = Mathf.Min(1f, 0.3f + -vn / 400f);
            }
        }
    }

    /// E near the carrier: approach control flies the ship in by the nearest mouth, along the deck, to a hover over
    /// the far pad already facing that pad's own mouth, then lets it down.
    public void StartApproach()
    {
        if (docked || cut != null || carrier == null || carrier.hold) return;
        if ((TruePos - carrier.truePos).magnitude >= Data.DOCK_RANGE)
        {
            game.Toast("Too far from the cargo ship for an approach · close to " + Data.Fm(Data.DOCK_RANGE) + " m", true);
            return;
        }
        var l0 = carrier.ToLocalTrue(TruePos);
        int entry = carrier.NearestSide(TruePos);
        int far = -entry;
        var app = CargoShip.OpeningLocal(entry) + new Vector3(0f, 0f, entry * 1400f);
        var mouth = CargoShip.OpeningLocal(entry);
        var deck = new Vector3(0f, -40f, 0f);
        var hover = CargoShip.ParkLocal(far) + new Vector3(0f, 90f, 0f);
        var pts = new List<Vector3> { l0 };
        if ((l0 - app).magnitude > 900f) pts.Add(app);
        pts.Add(mouth);
        pts.Add(deck);
        pts.Add(hover);
        float len = CurveLength(pts);
        cut = new Cut { mode = "dock", side = far, entry = entry, pts = pts, t = 0f, dur = Mathf.Clamp(len / 360f, 6f, 13f), phase = "fly", pt = 0f, hover = hover, park = CargoShip.ParkLocal(far), qEnd = transform.rotation };
        throttle = 0f;
        laserOn = false;
        firing = false;
        _laser.enabled = false;
        Audio.Say(new[] { "approach_control", "approach_1", "approach_2", "approach_3", "approach_4" }[Random.Range(0, 5)]);   // one of five radio calls
        game.Toast("Approach control has the ship · " + CargoShip.BayName(far) + " · Space skips", false);
    }

    /// Shift on the pad or the Depart button: approach control taxis the ship off the pad and straight out of its own
    /// mouth, then hands it over already under way.
    public void StartDeparture()
    {
        if (!docked || cut != null) return;
        if (hold)
        {
            game.Toast("Set a course on the nav map to leave Meridian Colony", false);
            game.OpenMap();
            return;
        }
        int b = dockSide;
        LeaveHangar();
        var pts = new List<Vector3> { CargoShip.ParkLocal(b), new Vector3(0f, -112f, b * 620f), new Vector3(0f, -40f, b * 1000f), new Vector3(0f, 60f, b * 2500f) };
        cut = new Cut { mode = "depart", side = b, pts = pts, t = 0f, dur = 5.2f };
        game.Toast("Departing · approach control has the ship · Space skips", false);
    }

    public void SkipCut()
    {
        if (cut == null) return;
        cut.t = cut.dur;
        if (cut.mode == "dock")
        {
            cut.phase = "settle";
            cut.pt = 99f;
        }
        game.Toast("Skipped", false);
    }

    void CutUpdate(float dt)
    {
        var C = cut;
        C.t += dt;
        float k = Mathf.Min(1f, C.t / C.dur);
        float u = C.mode == "depart" ? (k * k * (2f - k) * 0.5f + k * 0.5f * k) : k * k * (3f - 2f * k);
        var p = CurvePoint(C.pts, Mathf.Min(1f, u));
        var tan = CurveTangent(C.pts, Mathf.Min(u, 0.999f));
        if (C.mode == "hold")
        {
            // the carrier flies the path; the last stretch blends onto the holding heading
            var q = CargoShip.HeadingAlong(tan);
            if (k > 0.9f) q = Quaternion.Slerp(q, CargoShip.HeadingAlong(Data.HOLD_DIR), (k - 0.9f) / 0.1f);
            carrier.SetPose(p, Quaternion.Slerp(carrier.basisQ, q, 1f - Mathf.Exp(-1.8f * dt)));
            transform.position = carrier.transform.position;
            transform.rotation = LevelHeading(carrier.Nose);
            vel = Vector3.zero;
            if (k >= 1f)
            {
                cut = null;
                carrier.SetPose(Data.HOLD_PARK, CargoShip.HeadingAlong(Data.HOLD_DIR));
                transform.position = carrier.transform.position;
                EnterBerth();
            }
            return;
        }
        if (C.mode == "dock")
        {
            vel = carrier.vel;
            if (C.phase == "fly")
            {
                transform.position = carrier.ToTrue(p) - game.worldOffset;
                transform.rotation = Quaternion.Slerp(transform.rotation, LevelHeading(carrier.Dir(tan)), 1f - Mathf.Exp(-5f * dt));
                if (k >= 1f)
                {
                    C.phase = "settle";
                    C.pt = 0f;
                    C.qEnd = transform.rotation;
                }
                return;
            }
            C.pt += dt;
            float tt = Mathf.Max(0f, C.pt - 0.5f);
            float s = Mathf.Min(1f, tt / 2.5f);
            float e = s * s * (3f - 2f * s);
            var bayQ = LevelHeading(carrier.Dir(CargoShip.FaceLocal(C.side)));
            transform.position = carrier.ToTrue(Vector3.Lerp(C.hover, C.park, e)) - game.worldOffset;
            transform.rotation = Quaternion.Slerp(C.qEnd, bayQ, e);
            if (s >= 1f)
            {
                cut = null;
                EnterHangar(C.side);
            }
            return;
        }
        transform.position = carrier.ToTrue(p) - game.worldOffset;
        vel = carrier.vel;
        transform.rotation = Quaternion.Slerp(transform.rotation, LevelHeading(carrier.Dir(tan)), 1f - Mathf.Exp(-5f * dt));
        if (k >= 1f)
        {
            cut = null;
            vel += carrier.Dir(tan) * 340f;
            throttle = 0.35f;
            _camQ = transform.rotation;
            game.Toast("You have the ship", false);
        }
    }

    public void EnterHangar(int side)
    {
        docked = true;
        hold = false;
        dockSide = side;
        throttle = 0f;
        vel = Vector3.zero;
        target = -1;
        laserOn = false;
        firing = false;
        _laser.enabled = false;
        depWait = true;
        exitPending = false;
        hangarT = 0f;
        cut = null;
        ReleaseLock();
        hover = null;
        _lowHullWarned = false;
        lookYaw = 0f;
        lookPitch = 0f;
        Audio.Play("dock");
        // the deck welcomes you back over the intercom, one of four announcements, once the clamps have clunked (not on a
        // session's first dock, and not during the tutorial, whose own line for this step would talk over it)
        if (flownOut && State.tut < 0) _announceAt = Time.time + 0.8f;
        game.Toast("Docked in " + CargoShip.BayName(side) + " · stow cargo in the hangar window", false);
        game.OnDocked(true);
        State.Save();
    }

    public void LeaveHangar()
    {
        if (!docked) return;
        docked = false;
        hold = false;
        exitPending = true;
        flownOut = true;
        vel = carrier.vel;
        _camQ = transform.rotation;
        game.OnDocked(false);
        State.Save();
    }

    void DockUpdate(float dt)
    {
        if (hold)
        {
            // aboard the carrier at its holding point: it drifts gently on station
            float t = State.time;
            var drift = new Vector3(Mathf.Sin(t * 0.21f) * 320f, Mathf.Sin(t * 0.17f) * 240f, Mathf.Cos(t * 0.13f) * 320f);
            float k = 1f - Mathf.Exp(-0.4f * dt);
            carrier.SetPose(Vector3.Lerp(carrier.truePos, Data.HOLD_PARK + drift, k), carrier.basisQ);
            transform.position = carrier.transform.position;
            transform.rotation = LevelHeading(carrier.Nose);
            vel = Vector3.zero;
        }
        else
        {
            // settle onto the pad and stay there, nose out of the mouth, riding along with the carrier
            var park = carrier.ToTrue(CargoShip.ParkLocal(dockSide)) - game.worldOffset;
            float k2 = 1f - Mathf.Exp(-1.4f * dt);
            transform.position = Vector3.Lerp(transform.position, park, k2);
            transform.rotation = Quaternion.Slerp(transform.rotation, LevelHeading(carrier.Dir(CargoShip.FaceLocal(dockSide))), k2);
            vel = carrier.vel;
        }
        // the tank fills from the cargo ship's supply for free; the hull mends from its repair parts, one part per point
        float tank = State.Stat("tank").cap;
        if (State.fuel < tank)
        {
            float rate = 8f + 4f * State.up["tank"];
            float u = Mathf.Min(rate * dt, Mathf.Min(tank - State.fuel, State.shipFuel));
            if (u > 0f)
            {
                State.fuel += u;
                State.shipFuel -= u;
            }
            else if (State.shipFuel < 0.5f && !_fuelDryWarned)
            {
                _fuelDryWarned = true;
                game.Toast("Cargo ship fuel supply is dry · refuel at the Hub", true);
            }
        }
        float hp = State.Stat("hull").hp;
        if (State.hull < hp)
        {
            float u2 = Mathf.Min(REPAIR_RATE * dt, Mathf.Min(hp - State.hull, State.parts));
            if (u2 > 0f)
            {
                State.hull += u2;
                State.parts -= u2;
            }
            else if (State.parts < 0.5f && !_partsWarned)
            {
                _partsWarned = true;
                game.Toast("No repair parts left · restock at the Hub", true);
            }
        }
        // Shift (throttle up) departs, once it has been released since docking; H deposits the hold into the storage
        if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) depWait = false;
        else if (!depWait)
        {
            depWait = true;
            StartDeparture();
        }
        if (Input.GetKeyDown(KeyCode.H)) DepositAll();
    }

    /// Holding station off the colony: the carrier is parked, you are aboard, and the market and services are open.
    public void EnterBerth()
    {
        docked = true;
        hold = true;
        dockSide = 1;
        throttle = 0f;
        vel = Vector3.zero;
        target = -1;
        laserOn = false;
        firing = false;
        _laser.enabled = false;
        depWait = true;
        hangarT = 0f;
        cut = null;
        Audio.Play("dock");
        _colonyAt = Time.time + 0.9f;
        game.Toast("Holding station off Meridian Colony · the market is open", false);
        game.OnDocked(true);
        State.Save();
    }

    /// Every arrival at the Hub: the cargo ship comes in from deep space behind its holding point, sweeps wide past the
    /// outer ring and eases onto station nose toward the hub. The path is in true world coordinates; the ship rides inside.
    public void StartHoldApproach()
    {
        if (docked || cut != null) return;
        var p = Data.HOLD_PARK;
        var f = HoldFwd();
        var r = HoldSide();
        var pts = new List<Vector3> { carrier.truePos, p - f * 95000f + r * 32000f + new Vector3(0, 11000, 0), p - f * 34000f + r * 4000f + new Vector3(0, 1500, 0), p };
        if ((carrier.truePos - pts[1]).magnitude < 20000f) pts.RemoveAt(1);
        float len = CurveLength(pts);
        cut = new Cut { mode = "hold", pts = pts, t = 0f, dur = Mathf.Clamp(len / 6500f, 14f, 28f) };
        throttle = 0f;
        game.Toast("Arriving · Meridian Colony · Space skips", false);
    }

    // ---- the warp: the cargo ship makes the jump, so you have to be aboard it. A fade to black while the zone swaps
    // underneath, then the arrival (the Hub: the holding-station flight; a belt: on the pad in the dock that faces the planet).
    public void StartWarp(Data.Zone z)
    {
        if (warp != null) return;
        if (!docked)
        {
            game.Toast("Dock with the cargo ship before warping · it makes the jump", true);
            return;
        }
        if (z.id == game.zone.id) return;
        warp = new Warp { z = z, t = 0f, loaded = false, skip = false, fromHold = hold };
        game.OnDocked(false);
        Audio.Play("chime");
        Audio.Play("warp_charge");
        game.Toast("Jump · " + z.name + " · " + Data.ZoneLy(game.zone, z) + " ly · Space skips", false);
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    /// 0 = clear, 1 = black: the HUD's fade for the jump.
    public float WarpFade()
    {
        if (recovery != null)
        {
            float rt = recovery.t, at = recovery.at, dur = recovery.dur;
            return rt < at ? SmoothStep(at - 1.2f, at, rt) : 1f - SmoothStep(at + 0.3f, dur, rt);
        }
        if (warp == null) return 0f;
        float t = warp.t;
        if (warp.loaded) return warp.skip ? 0f : 1f - SmoothStep(WARP_LOAD_AT + 1.1f, WARP_LOAD_AT + 2.3f, t);
        return SmoothStep(WARP_LOAD_AT - 1.2f, WARP_LOAD_AT, t);
    }

    void WarpUpdate(float dt)
    {
        var W = warp;
        W.t += dt;
        if (!W.skip && !_warpVoiced && W.t >= 0.4f) { _warpVoiced = true; Audio.Say("warp_ready"); }
        if (!W.loaded && (W.skip || W.t >= WARP_LOAD_AT))
        {
            W.loaded = true;
            _warpVoiced = false;
            Audio.Play("warp_jump");
            docked = false;
            hold = false;
            cut = null;
            game.WarpLoad(W.z);   // swaps the zone and places the carrier and ship for the arrival
        }
        if (W.loaded && (W.skip || W.t >= WARP_DUR))
        {
            warp = null;
            game.WarpDone(W.z);
        }
    }

    // ---- the market, only while holding station at the Hub
    public void Sell(string[] keys, bool fromHold, bool fromStore)
    {
        float cr;
        float units = State.Sell(keys, fromHold, fromStore, out cr);
        if (units < 0.5f)
        {
            game.Toast("Nothing to sell", true);
            return;
        }
        Audio.Play("cash");
        game.Toast("Sold " + Mathf.RoundToInt(units) + " · +" + Data.Fmt(cr) + " cr", false);
        game.OnDocked(true);
    }

    public void RefuelCargoShip()
    {
        float cost; bool partial; string msg;
        float u = State.RefuelCargoShip(out cost, out partial, out msg);
        if (u < 1f) { game.Toast(msg, true); return; }
        _fuelDryWarned = false;
        game.Toast("Refuelled " + Mathf.FloorToInt(u) + " · " + Data.Fmt(cost) + " cr" + (partial ? " · out of credits before full" : ""), false);
        game.OnDocked(true);
    }

    public void BuyParts()
    {
        float cost; bool partial; string msg;
        float n = State.BuyParts(out cost, out partial, out msg);
        if (n < 1f) { game.Toast(msg, true); return; }
        _partsWarned = false;
        game.Toast("Restocked " + Mathf.RoundToInt(n) + " repair parts · " + Data.Fmt(cost) + " cr" + (partial ? " · out of credits before full" : ""), false);
        game.OnDocked(true);
    }

    public void DepositAll()
    {
        float had = State.CargoTotal();
        float moved = State.StowAll();
        if (moved < 0.5f)
        {
            game.Toast(had > 0.5f ? "Cargo ship storage is full" : "Nothing in the hold to stow", true);
            return;
        }
        Audio.Play("stow");
        game.Toast("Stowed " + Mathf.RoundToInt(moved) + " aboard the cargo ship" + (State.CargoTotal() > 0.5f ? " · storage full, the rest stays in the hold" : ""), false);
        State.Save();
        game.OnDocked(true);   // the panel re-reads the hold
    }

    public void TakeAll()
    {
        float moved = State.TakeAll();
        if (moved < 0.5f)
        {
            game.Toast(State.StoreTotal() > 0.5f ? "No room in the hold" : "Storage is empty", true);
            return;
        }
        Audio.Play("stow");
        game.Toast("Took " + Mathf.RoundToInt(moved) + " back aboard", false);
        State.Save();
        game.OnDocked(true);
    }

    // ---- paths: a Catmull-Rom curve through the points
    public static Vector3 CurvePoint(List<Vector3> pts, float u)
    {
        int n = pts.Count - 1;
        float f = Mathf.Clamp(u, 0f, 0.9999f) * n;
        int i = Mathf.FloorToInt(f);
        float t = f - i;
        var p0 = pts[Mathf.Max(i - 1, 0)];
        var p1 = pts[i];
        var p2 = pts[Mathf.Min(i + 1, n)];
        var p3 = pts[Mathf.Min(i + 2, n)];
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
    }

    public static Vector3 CurveTangent(List<Vector3> pts, float u)
    {
        var a = CurvePoint(pts, Mathf.Max(0f, u - 0.004f));
        var b = CurvePoint(pts, Mathf.Min(1f, u + 0.004f));
        var d = b - a;
        return d.sqrMagnitude > 1e-9f ? d.normalized : Vector3.forward;
    }

    public static float CurveLength(List<Vector3> pts)
    {
        float len = 0f;
        var prev = CurvePoint(pts, 0f);
        for (int i = 1; i <= 64; i++)
        {
            var p = CurvePoint(pts, i / 64f);
            len += (p - prev).magnitude;
            prev = p;
        }
        return len;
    }

    // ---- the laser, radar, overcharge
    void TickLaser(float dt)
    {
        firing = autoFire || Input.GetKey(KeyCode.L) || Input.GetMouseButton(0);   // Space is the drift brake now
        float reach = State.Stat("range").reach;
        var fwd = Forward;
        var origin = TruePos + fwd * 20f;
        target = belt.RayHit(origin, fwd, reach);
        laserOn = false;
        _laser.enabled = false;
        // 1, 2 and 3 pick the weapon (the wheel is the throttle now)
        if (CanFly && !docked && game.hud != null && !game.hud.InvOpen && !game.hud.MapOpen && !game.hud.MenuVisible)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) && weapon != "laser") { weapon = "laser"; game.Toast("Mining laser selected", false); }
            if (Input.GetKeyDown(KeyCode.Alpha2) && weapon != "gun") { weapon = "gun"; game.Toast("Autocannon selected", false); }
            if (Input.GetKeyDown(KeyCode.Alpha3) && weapon != "rocket") { weapon = "rocket"; game.Toast("Seeker rockets selected", false); }
        }
        // the autocannon: the raider under the nose (or the locked one, across the forward half); with the cannon selected a
        // rock in the way does not stop the shot
        raiderTarget = null;
        gunFiring = false;
        _gunCd -= dt;
        _gunWarnT -= dt;
        var gun = State.Stat("gun");
        // the gun aims straight down the nose, like the laser: the mouse steers the ship and the nose is the aim
        aimDir = fwd;
        if (game.raiders != null && (target < 0 || weapon != "laser"))
        {
            float gunReach = gun.reach > 0f ? gun.reach : 1800f;
            // the locked raider first: the cannon rides the dish turret, so it tracks a lock across the forward arc
            if (lockKind == "raider" && lockRaider != null && !lockRaider.dead)
            {
                var to = lockRaider.pos - origin;
                if (to.magnitude <= gunReach && Vector3.Dot(to.normalized, fwd) > Mathf.Cos(90f * Mathf.Deg2Rad)) raiderTarget = lockRaider;   // the dish turret covers the whole forward half
            }
            if (raiderTarget == null) raiderTarget = game.raiders.NearestInCone(origin, aimDir, gunReach, Mathf.Cos(4f * Mathf.Deg2Rad));
        }
        // the heat: up while it fires, down while it does not; full is a lockout until it has cooled well down
        bool wantGun = firing && weapon == "gun";
        if (wantGun && !gunOverheated)
        {
            gunHeat = Mathf.Min(1f, gunHeat + dt / GUN_HEAT_TIME);
            if (gunHeat >= 1f) { gunOverheated = true; Audio.Play("laser_off", -2f); game.Toast("Autocannon overheated · let it cool", true); }
        }
        else
        {
            gunHeat = Mathf.Max(0f, gunHeat - dt / GUN_COOL_TIME);
            if (gunOverheated && gunHeat <= GUN_COOL_TO) { gunOverheated = false; Audio.Play("chime", -6f); }
        }
        if (wantGun && !gunOverheated)
        {
            // the cannon selected: bolts straight down the nose
            gunFiring = true;
            if (_gunCd <= 0f)
            {
                _gunCd = 1f / gun.rate;
                game.raiders.Fire(origin, aimDir, gun.mult, true);   // leading the target is the pilot's job
                Audio.Shot("blaster");   // the user's own clip, on its own source so it lands on the shot
            }
            TickSpot(dt, false, _spotPos);
            return;
        }
        if (wantGun) { TickSpot(dt, false, _spotPos); return; }   // overheated: the trigger does nothing until it cools
        // the seekers: one leaves per trigger pull once the tube has reloaded, after the locked raider, else the nearest
        // ahead, else the nearest in reach; the magazine restocks on the pad
        var rk = State.Stat("rocket");
        if (docked || State.rockets < 0) State.rockets = rk.slots;
        rocketCd = Mathf.Max(0f, rocketCd - dt);
        _rocketWarnT -= dt;
        if (weapon == "rocket")
        {
            bool pull = autoFire || Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.L);
            if (pull && !docked && game.raiders != null && rocketCd <= 0f)
            {
                if (State.rockets <= 0)
                {
                    if (_rocketWarnT <= 0f) { _rocketWarnT = 3f; game.Toast("No rockets left · the pad restocks them", true); Audio.Play("laser_off", -4f); }
                }
                else
                {
                    var who = game.raiders.Acquire(origin, fwd, rk.reach, lockKind == "raider" ? lockRaider : null);
                    if (who == null) { if (_rocketWarnT <= 0f) { _rocketWarnT = 3f; game.Toast("No raider within " + Data.Fm(rk.reach) + " m for a rocket", true); } }
                    else
                    {
                        game.raiders.Launch(origin, fwd, vel, who);
                        State.rockets--;
                        rocketCd = rk.rate;
                        Audio.Play("rocket_launch", 2f);
                    }
                }
            }
            TickSpot(dt, false, _spotPos);
            return;
        }
        if (firing && weapon == "laser" && raiderTarget != null && target < 0)
        {
            // the laser selected with a raider in the sights: a reminder, and no beam
            if (_gunWarnT <= 0f) { _gunWarnT = 4f; game.Toast("Raider in the sights · the scroll wheel selects the autocannon", true); }
            TickSpot(dt, false, _spotPos);
            return;
        }
        if (!firing) { TickSpot(dt, false, _spotPos); return; }
        var end = origin + fwd * reach;
        if (target >= 0)
        {
            float oc = (overcharge && State.fuel > 0f) ? State.Stat("overcharge").mult : 1f;
            bool canCut = belt.ore[target] < 0 || Data.ORES[belt.ore[target]].unlock <= State.up["laser"] + 1;
            var rp = belt.RockPos(target);
            end = rp - (rp - origin).normalized * belt.radius[target] * 0.85f;
            if (canCut)
            {
                laserOn = true;
                float rate = State.Stat("laser").rate * oc;
                belt.Damage(target, rate * 5f * dt);
                if (oc > 1f)
                {
                    State.fuel = Mathf.Max(0f, State.fuel - Data.OVER_BURN * oc * dt);
                    if (State.fuel <= 0f)
                    {
                        overcharge = false;
                        game.Toast("Overcharge off · fuel tank dry", true);
                    }
                }
                if (belt.hp[target] <= 0f)
                {
                    game.BreakRock(target);
                    target = -1;
                }
            }
        }
        _laser.enabled = true;
        _laser.SetPosition(0, DishFocusScene());
        _laser.SetPosition(1, end - game.worldOffset);
        TickSpot(dt, laserOn && target >= 0, end);
    }

    /// heatFx.update: the beam takes a full 30 s on a rock to reach white heat and cools off over about 10 s once it
    /// comes off; moving to a new rock leaves half the heat behind. The stone's own surface glow, the spot glow, the
    /// light and the sparks all follow the heat.
    void TickSpot(float dt, bool active, Vector3 hitTrue)
    {
        int key = active ? target : -1;
        if (key >= 0 && key != _spotKey) spotHeat *= 0.5f;
        if (key >= 0) { _spotKey = key; _spotPos = hitTrue; }
        spotHeat = Mathf.Clamp01(spotHeat + (active ? dt / 30f : -dt / 10f));
        float h = spotHeat;
        bool on = h > 0.01f;
        float r = _spotKey >= 0 && _spotKey < belt.count ? belt.radius[_spotKey] : 40f;
        belt.SetSpotHeat(0, _spotPos, on ? h : 0f, Mathf.Min(r * 0.9f, 8f + r * 0.05f + 22f * h));
        if (_spotGlow == null) BuildSpotFx();
        _spotGlow.gameObject.SetActive(on);
        _spotLight.enabled = on;
        if (!on) return;
        var cold = new Color(1f, 0.18f, 0.03f);
        var warm = new Color(1f, 0.6f, 0.23f);
        var white = new Color(1f, 0.95f, 0.82f);
        var col = h < 0.5f ? Color.Lerp(cold, warm, h * 2f) : Color.Lerp(warm, white, (h - 0.5f) * 2f);
        float flick = 0.92f + Random.value * 0.16f;
        var scenePos = _spotPos - game.worldOffset;
        _spotGlow.position = scenePos;
        _spotGlow.localScale = Vector3.one * (4f + 14f * h) * flick;
        _spotMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.2f + 0.6f * h));
        _spotLight.transform.position = scenePos;
        _spotLight.color = col;
        _spotLight.intensity = h * h * 6f * flick;
        if (active && _spotKey >= 0 && game.sparks != null)
        {
            var nrm = (_spotPos - belt.RockPos(_spotKey)).normalized;
            game.sparks.Emit(_spotPos, nrm, h, dt);   // a shower of streaks off the surface, more and hotter as the spot heats
            if (h > 0.3f)
            {
                _burnT += dt;
                if (_burnT > 0.1f)
                {
                    _burnT = 0f;
                    belt.Scorch(_spotKey, _spotPos, Mathf.Min(r * 0.5f, 6f + r * 0.04f + 10f * h));   // the burn trail, once the spot is hot
                }
            }
        }
    }

    /// The spot's glow (an additive sphere) and its light, made on first use.
    void BuildSpotFx()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "LaserSpot";
        Destroy(go.GetComponent<Collider>());
        _spotMat = new Material(Game.Sh("BeltRunner/Spark"));
        _spotMat.SetColor("_Color", new Color(1f, 0.3f, 0.05f, 0.3f));
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _spotMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _spotGlow = go.transform;
        var lg = new GameObject("LaserSpotLight");
        _spotLight = lg.AddComponent<Light>();
        _spotLight.type = LightType.Point;
        _spotLight.range = 220f;
        _spotLight.intensity = 0f;
        _spotLight.shadows = LightShadows.None;
    }

    public void Radar()
    {
        if (radarCd > 0f) return;
        radarCd = Data.PULSE_CD;
        radarPulsed = true;
        Audio.Play("radar_ping");
        float range = State.Stat("scanner").range;
        if (_pulseSphere != null) { _pulseT = 0f; _pulseRange = range; _pulseOrigin = TruePos; }
        scanCount = belt.Scan(TruePos, range, -1, range / Data.PULSE_TIME, out scanNearest, out scanDist);
        if (scanCount == 0) game.Toast("Radar: no ore within " + Data.Fm(range) + " m", true);
        else game.Toast("Radar: " + scanCount + " ore rocks within " + Data.Fm(range) + " m · nearest " + belt.RockName(scanNearest) + " at " + Data.Fm(scanDist) + " m", false);
    }

    void ToggleOvercharge()
    {
        float m = State.Stat("overcharge").mult;
        if (m <= 1f)
        {
            game.Toast("No laser overcharge fitted · it is an upgrade in the hangar window", true);
            return;
        }
        overcharge = !overcharge;
        Audio.Play("chime");
        game.Toast(overcharge ? "Laser overcharge armed · ×" + m + " damage · draws " + (Data.OVER_BURN * m).ToString("0.0") + " fuel/s while cutting" : "Laser overcharge off", false);
    }

    // ---- cameras
    /// The chase camera in flight and during the departure taxi; a camera by the entry mouth during the approach; on
    /// the pad a slow walk round the ship inside the bay.
    public void UpdateCamera(float dt)
    {
        float s = Data.SHIP_SCALE;
        if (warp != null)
        {
            // the exterior shot: behind and beside the carrier as it jumps, ahead of it as it arrives
            if (warp.loaded) CarrierShot(new Vector3(12500f, 3600f, 7200f), new Vector3(600f, 0f, 0f));
            else CarrierShot(new Vector3(-12000f, 3200f, 6900f), new Vector3(2400f, 0f, 0f));
            return;
        }
        if (cut != null && cut.mode == "hold")
        {
            CarrierShot(new Vector3(-12500f, 3600f, 7200f), new Vector3(600f, 0f, 0f));
            return;
        }
        if (docked && hold)
        {
            HoldingCamera(dt);
            return;
        }
        if (cut != null && cut.mode == "dock")
        {
            if (cut.phase == "fly")
            {
                var cp = carrier.ToTrue(new Vector3(760f, 320f, cut.entry * 1750f)) - game.worldOffset;
                cam.transform.position = cp;
                cam.transform.rotation = Quaternion.LookRotation(transform.position + Forward * 60f - cp, Vector3.up);
                return;
            }
            HangarCamera(dt);
            return;
        }
        if (docked)
        {
            HangarCamera(dt);
            return;
        }
        _camQ = Quaternion.Slerp(_camQ, transform.rotation, 1f - Mathf.Exp(-7f * dt));
        // the field of view: out on the afterburner (further with the bigger refits), in on the drift,
        // a touch out with speed otherwise
        var eng = State.Stat("engine");
        float spFrac = eng.max > 0f ? Mathf.Clamp01(vel.magnitude / eng.max) : 0f;
        float fovWant = FOV + 4f * spFrac;
        // the burner's intensity, and a kick in the field of view the instant it lights
        bool wasBurning = burnK > 0.5f;
        burnK = Mathf.Lerp(burnK, afterburning ? 1f : 0f, 1f - Mathf.Exp(-(afterburning ? 3f : 2f) * dt));
        if (afterburning && !wasBurning && burnK > 0.5f) _burnKick = 1f;
        _burnKick *= Mathf.Exp(-5f * dt);
        if (afterburning) fovWant = FOV + 14f + 3f * State.Stat("thrusters").mult + 7f * _burnKick;
        else if (drifting) fovWant = FOV - 9f;
        _fov = Mathf.Lerp(_fov, fovWant, 1f - Mathf.Exp(-(fovWant > _fov ? 3f : 4f) * dt));
        cam.fieldOfView = _fov;
        // the turn: the chase camera hangs back on the outside of the turn, the look point leads into it, and the frame
        // banks a little with the yaw (roll input tips it too); everything eased so it settles rather than snaps
        float k = 1f - Mathf.Exp(-5f * dt);
        camYaw = Mathf.Lerp(camYaw, CanFly ? _ctlYaw : 0f, k);
        camPitch = Mathf.Lerp(camPitch, CanFly ? _ctlPitch : 0f, k);
        camRoll = Mathf.Lerp(camRoll, CanFly ? _ctlRoll : 0f, k);
        // free look turns the camera relative to the hull; the chase offset stays rigid on the ship's position
        var lq = _camQ * Quaternion.AngleAxis(lookYaw * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(lookPitch * Mathf.Rad2Deg, Vector3.right);
        var f = lq * Vector3.forward;
        var u = lq * Vector3.up;
        var r = lq * Vector3.right;
        float bank = (-camYaw * 14f - camRoll * 7f) * Mathf.Deg2Rad;
        u = (Mathf.Cos(bank) * u + Mathf.Sin(bank) * r).normalized;   // the bank: tip the up vector about the view axis
        var camPos = transform.position - f * 88f * s + u * 30f * s - r * camYaw * 30f * s - u * camPitch * 18f * s;
        var look = transform.position + f * 140f * s + u * 10f * s + r * camYaw * 60f * s + u * camPitch * 40f * s;
        if (shake > 0f)
        {
            shake = Mathf.Max(0f, shake - dt * 1.8f);
            float sh = shake * shake * 6f;
            camPos += new Vector3(Random.Range(-sh, sh), Random.Range(-sh, sh), Random.Range(-sh, sh));
        }
        // the burner's rumble: a low jitter, harder with the bigger refits
        if (burnK > 0.02f)
        {
            float rb = burnK * (0.35f + 0.12f * State.Stat("thrusters").mult) * s;
            camPos += new Vector3(Random.Range(-rb, rb), Random.Range(-rb, rb), Random.Range(-rb, rb));
        }
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(look - camPos, u);
    }

    /// Hangar view: a camera on a slow elliptical walk round the pad, kept inside the bay walls, looking at the ship.
    void HangarCamera(float dt)
    {
        hangarT += dt;
        float a = hangarT * 0.16f;
        int side = dockSide != 0 ? dockSide : (cut != null ? cut.side : 1);
        var park = CargoShip.ParkLocal(side);
        var camL = new Vector3(190f * Mathf.Cos(a), park.y + 56f + 28f * Mathf.Sin(a * 0.7f), park.z + 250f * Mathf.Sin(a));
        var cp = carrier.ToTrue(camL) - game.worldOffset;
        cam.transform.position = cp;
        cam.transform.rotation = Quaternion.LookRotation(transform.position + carrier.Dir(new Vector3(0f, 6f, 0f)) - cp, carrier.Dir(Vector3.up));
        _camQ = transform.rotation;
    }

    /// A camera fixed in the carrier's frame (offsets along nose, up, side), looking at a point ahead of it.
    void CarrierShot(Vector3 offset, Vector3 lookAhead)
    {
        var cp = carrier.ToTrue(offset) - game.worldOffset;
        cam.transform.position = cp;
        cam.transform.rotation = Quaternion.LookRotation(carrier.ToTrue(lookAhead) - game.worldOffset - cp, Vector3.up);
        _camQ = transform.rotation;
    }

    /// Holding station: a slow swing round the carrier with the colony beyond it (the HTML's holding shot).
    void HoldingCamera(float dt)
    {
        hangarT += dt;
        var f = HoldFwd();
        var r = HoldSide();
        var c = carrier.transform.position;
        var cp = c - f * 15500f + r * (7000f + Mathf.Sin(hangarT * 0.04f) * 3500f) + new Vector3(0, 7000, 0);
        cam.transform.position = cp;
        cam.transform.rotation = Quaternion.LookRotation(c + f * 22000f + new Vector3(0, -11000, 0) - cp, Vector3.up);
        _camQ = transform.rotation;
    }

    /// The floating origin moved: the camera keeps its place relative to the ship.
    public void OnShift(Vector3 delta)
    {
        cam.transform.position -= delta;
        _prevPos -= delta;
    }
}

/// A sprite that always faces the camera (the browser's billboarded sprites).
public class FaceCamera : MonoBehaviour
{
    public float roll;   // degrees about the view axis, for billboards that should not all sit the same way
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = roll != 0f ? cam.transform.rotation * Quaternion.AngleAxis(roll, Vector3.forward) : cam.transform.rotation;
    }
}
