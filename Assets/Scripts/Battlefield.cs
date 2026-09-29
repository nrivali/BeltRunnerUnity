using System.Collections.Generic;
using UnityEngine;

/// The battlefield (2026-09-21, the user's, on the combat branch): a contested zone (Data.Zone.battle) with a battleship on
/// each side, SEPARATION apart. Ours is the cargo ship, fitted for the fight with the same subsystems as the enemy's and
/// held on station facing it; theirs is a hull of the same size built from primitives. Each side spawns fighters from its
/// hangar (raiders on team 1 for them, wingmen on team 0 for us, Raiders.Raider.team), and each battleship carries the
/// subsystems that have to go in order: the four turrets first (they cover the shield generators), then the two shield
/// generators (while any stands the hangar and the core sit under the shield), then the hangar (no more fighters) and
/// the core. The side whose core goes first loses the field. Positions are true world coordinates, like the raiders'.
public class Battlefield
{
    public const float SEPARATION = 48000f;          // 6,000 m on the readout between the two battleships (2026-09-23, the staged battle; 80,000 u, 10,000 m, before)
    // the staged battle (2026-09-23, the user's): the fight holds the middle until a side fills the meter with kills, then that
    // side attacks the other ship until it is pushed back or its clock runs out; the fifth push to an end breaks the ship
    public const float NEUTRAL_R = 8000f;            // the neutral zone: a sphere this big at the midpoint, where the fighters hold and fight
    public const float FILL = 10f;                   // kills to fill the meter: a wingman's or a turret's kill 1, yours 2, your death -3
    public const float ATTACK_T = 90f;               // an attack phase's clock, seconds
    public const int PUSH_CAP = 5;                   // the push that breaks a battleship: the fifth to its end
    public const float DRIFT = 1f / 15f;             // the meter's drift toward the middle in neutral, notches a second
    public const float RALLY_R = 2500f;              // attackers launched in an attack phase come in at the neutral zone, this far spread
    public static int attacking = -1;                // the side in its attack phase: -1 none (neutral), 0 ours, 1 theirs; static so a base's Exposed can read it
    public float morale;                             // the meter: + toward our end (FILL), - toward theirs
    public float attackT;                            // the attack clock
    public readonly int[] pushes = { 0, 0 };
    public string phaseState = "neutral";            // for the tests
    public Vector3 Mid { get { return ally != null && enemy != null ? (ally.pos + enemy.pos) * 0.5f : Vector3.zero; } }
    public string Phase { get { return attacking < 0 ? "NEUTRAL" : attacking == 0 ? "ATTACK" : "DEFEND"; } }
    public const int TURRET = 0, GENERATOR = 1, HANGAR = 2, CORE = 3;
    public const float TURRET_HP = 500f, GENERATOR_HP = 260f, HANGAR_HP = 380f, CORE_HP = 700f;   // the turrets 200 until the user's "way stronger" (2026-09-21)
    public const float TURRET_R = 150f, GENERATOR_R = 220f, HANGAR_R = 260f, CORE_R = 320f;
    public const float TURRET_REACH = 14000f, TURRET_DMG = 30f, TURRET_RATE = 0.8f, TURRET_SPREAD = 0.02f, TURRET_SLEW = 3.5f, TURRET_BOLT_SPEED = 28000f;   // artillery (2026-09-23, the user's: "less accurate · bigger and longer · slower firing, more damage · cinematic"): a shot every 1.25 s at 30, about a degree of spread, 14,000 u/s, and a 40,000 u reach, 5,000 m, since 2026-09-23 (the user asked for fire from 5,000 m; 12,000 u before); the day before 10 dmg at 2.5/s, 0.005 and 24,000 ("very accurate"); 9,000 u, 6 dmg, 2/s, 0.05 and 4,200 u/s at first
    public const float SHIELD_R = 2400f;             // the bubble over the aft hull: the hangar and the core sit inside it
    public const int ALOFT = 5;                      // fighters a side keeps in the air while its hangar stands (2026-09-23, the user asked for five a side; 9 for a while that day, 3 before)
    public const float LAUNCH_EVERY = 5f, FIRST_LAUNCH = 6f;   // one every 5 s, so nine are up in under a minute (14 s until 2026-09-23)
    public const float TURRET_SOUND_R = 2000f;      // a turret shot is heard at full inside this of the muzzle, 6 dB down a doubling beyond, silent past 30 dB down, 64,000 u (2026-09-23; the raider gun figure, 300 u, before, which at these ranges was silence)
    public const float ENGAGE = 12000f;              // a fighter goes for the player inside this, else for the nearest fighter or an exposed subsystem (7,000 until the 2026-09-21 speeds)
    public const float REWARD_BASE = 8000f, REWARD_STEP = 1000f;
    public const float BUMP_RANGE = 5200f;
    public const float ENEMY_SCALE = 2f;             // the enemy hull at twice its authored size (2026-09-23, the user's: "double the size of the battleship"); ours, the cargo ship, as it is
    static readonly Vector3 HULL_HALF = new Vector3(3700f, 540f, 900f);   // the cargo ship's box, for the enemy hull too

    public class Subsystem
    {
        public int kind;
        public string name;
        public Vector3 local;      // in the base's frame: +X the nose, +Y up
        public float hp, maxHp, r, hitR, fireCd, flameT;
        public bool dead;
        public Transform node, head;
        public MeshRenderer headMr, barrelMr, eyeMr;
        public Vector3 aim = Vector3.up, rest = Vector3.up;
        public Transform bubble;   // the part's own shield bubble (2026-09-22): shown while the part is covered, flares on a hit
        public float flash;
        public float hitT = -100f;   // when the player last damaged it: the HUD shows its health for a while after (2026-09-23)
        public Vector3 headOffset = new Vector3(0f, TURRET_R + 40f, 0f);   // a turret's head above its pad: 190 on the primitives, read from the model when there is one (230 on Astra's)
        public Vector3 HeadLocal { get { return kind == TURRET ? local + headOffset : local; } }
    }

    public class Base
    {
        public int team;                // 0 ours, 1 theirs
        public string name;
        public Vector3 pos;
        public Quaternion q = Quaternion.identity;
        public float scale = 1f;        // the hull's size against its authored one (the enemy's ENEMY_SCALE, 2026-09-23)
        public Transform placeholder;   // the primitives that stand in until the model lands, at that scale
        public bool destroyed;
        public Transform root;          // the enemy hull's root; the cargo ship's transform for ours
        public Transform shield;
        public float shieldFlash;
        public int launched, kills;
        public float launchT = FIRST_LAUNCH;
        public readonly List<Subsystem> subs = new List<Subsystem>();
        public readonly List<Raiders.Raider> fighters = new List<Raiders.Raider>();
        public Vector3 ToTrue(Vector3 local) { return q * local + pos; }
        public Vector3 Dir(Vector3 local) { return q * local; }
        public int Up(int kind) { int n = 0; foreach (var s in subs) if (s.kind == kind && !s.dead) n++; return n; }
        public bool Shielded { get { return !destroyed && Up(GENERATOR) > 0; } }
        /// What can be hit now: the turrets always; the generators once the turrets are down; the hangar and the core once
        /// the shield is down.
        public bool Exposed(Subsystem s)
        {
            if (s.dead || destroyed) return false;
            if (attacking != (team == 1 ? 0 : 1)) return false;   // only while the other side is in its attack phase (2026-09-23)
            if (s.kind == TURRET) return true;
            if (s.kind == GENERATOR) return Up(TURRET) == 0;
            return !Shielded;
        }
        public Subsystem Core { get { foreach (var s in subs) if (s.kind == CORE) return s; return null; } }
        public Subsystem Hangar { get { foreach (var s in subs) if (s.kind == HANGAR) return s; return null; } }
        public Vector3 bayLocal = new Vector3(0f, -150f, 1150f);   // where the fighters leave from, out of the port mouth (the model's `bay` when there is one)
        public Vector3 Bay { get { return ToTrue(bayLocal); } }
        public readonly List<MeshRenderer> lit = new List<MeshRenderer>();   // the hull's lamps and glows, all out when the core goes
        public MeshRenderer bayLamp;   // the mouth's glow, out when the hangar dies
        public string Status
        {
            get
            {
                if (destroyed) return "core destroyed";
                int t = Up(TURRET), g = Up(GENERATOR);
                if (t > 0) return t + " turret" + (t > 1 ? "s" : "") + " · shield up";
                if (g > 0) return g + " shield generator" + (g > 1 ? "s" : "") + " · shield up";
                var c = Core;
                return (Hangar != null && !Hangar.dead ? "hangar up · " : "hangar down · ") + "core " + Mathf.CeilToInt(c != null ? c.hp : 0f) + " / " + Mathf.RoundToInt(CORE_HP);
            }
        }
    }

    public Game game;
    public bool active;                 // the zone is a battlefield and the battle is on
    public string state = "";           // "" (not here), "battle", "won", "lost"
    public Base ally, enemy;
    public Vector3 allyStation;         // where the cargo ship holds, facing the enemy
    Material _hull, _dark, _plate, _trim, _trimAlly, _lamp, _shieldMat, _shieldAllyMat;
    readonly List<MeshRenderer> _lit = new List<MeshRenderer>();
    float _blink, _smokeT;
    int _bumpFrame;
    List<int> _bumpIds = new List<int>();
    bool _built;

    public Battlefield(Game g)
    {
        game = g;
        _hull = new Material(Game.Sh("Standard"));
        _hull.color = new Color(0.23f, 0.19f, 0.25f);
        _hull.SetFloat("_Metallic", 0.3f);
        _hull.SetFloat("_Glossiness", 0.45f);
        _dark = new Material(Game.Sh("Standard"));
        _dark.color = new Color(0.09f, 0.08f, 0.11f);
        _dark.SetFloat("_Metallic", 0.2f);
        _dark.SetFloat("_Glossiness", 0.25f);
        _plate = new Material(Game.Sh("Standard"));
        _plate.color = new Color(0.36f, 0.33f, 0.4f);
        _plate.SetFloat("_Metallic", 0.55f);
        _plate.SetFloat("_Glossiness", 0.5f);
        _trim = new Material(Game.Sh("Standard"));
        _trim.color = Data.Hex("#ff2e63");
        _trim.EnableKeyword("_EMISSION");
        _trim.SetColor("_EmissionColor", Data.Hex("#ff2e63") * 1.5f);
        _trimAlly = new Material(Game.Sh("Standard"));
        _trimAlly.color = Ui.CYAN;
        _trimAlly.EnableKeyword("_EMISSION");
        _trimAlly.SetColor("_EmissionColor", Ui.CYAN * 1.5f);
        _lamp = new Material(Game.Sh("Standard"));
        _lamp.color = Data.Hex("#ffb070");
        _lamp.EnableKeyword("_EMISSION");
        _lamp.SetColor("_EmissionColor", Data.Hex("#ffb070") * 2f);
        _shieldMat = new Material(Game.Sh("BeltRunner/Pulse"));
        _shieldMat.SetColor("_Color", new Color(1f, 0.28f, 0.45f, 0.2f));
        _shieldMat.SetFloat("_Rim", 2.6f);
        _shieldAllyMat = new Material(Game.Sh("BeltRunner/Pulse"));
        _shieldAllyMat.SetColor("_Color", new Color(0.37f, 0.83f, 0.94f, 0.18f));
        _shieldAllyMat.SetFloat("_Rim", 2.6f);
    }

    public bool Active { get { return active && state == "battle"; } }
    public static float Reward() { return REWARD_BASE + REWARD_STEP * State.battlesWon; }
    public string Status { get { return enemy == null ? "" : "theirs · " + enemy.Status + "  ·  ours · " + ally.Status; } }
    public string Sig { get { return state + "|" + (enemy != null ? enemy.Status : "") + "|" + (ally != null ? ally.Status : ""); } }

    public void Clear()
    {
        if (enemy != null && enemy.root != null) Object.Destroy(enemy.root.gameObject);
        if (ally != null) foreach (var s in ally.subs)
        {
            // ours ride on the cargo ship, so every piece goes by hand: the pad or body, a turret's head, the lamp, the bubble
            if (s.node != null) Object.Destroy(s.node.gameObject);
            if (s.head != null) Object.Destroy(s.head.gameObject);
            if (s.eyeMr != null) Object.Destroy(s.eyeMr.gameObject);
            if (s.bubble != null) Object.Destroy(s.bubble.gameObject);
        }
        if (ally != null && ally.shield != null) Object.Destroy(ally.shield.gameObject);
        ally = null; enemy = null;
        _lit.Clear(); _bumpIds.Clear();
        active = false; state = ""; _built = false;
        attacking = -1; morale = 0f; attackT = 0f; pushes[0] = pushes[1] = 0; phaseState = "neutral";
    }

    /// Called on every zone load: the field is only built in a battle zone; the rest is set up when the ship is placed.
    public void Build(Data.Zone zone)
    {
        Clear();
        _built = zone.battle;
    }

    /// The ship is placed (Game.SpawnInZone): the cargo ship holds station where its orbit had it, the enemy battleship
    /// sits SEPARATION out along its nose, and both are fitted. A fresh battle every visit.
    public void Start(CargoShip carrier)
    {
        if (!_built) return;
        allyStation = carrier.truePos;
        var nose = carrier.Nose;
        ally = new Base { team = 0, name = "our battleship", pos = carrier.truePos, q = carrier.basisQ, root = carrier.transform };
        enemy = new Base { team = 1, name = "enemy battleship", pos = allyStation + nose * SEPARATION, scale = ENEMY_SCALE };
        enemy.q = CargoShip.HeadingAlong(allyStation - enemy.pos);
        carrier.station = true;
        attacking = -1; morale = 0f; attackT = 0f; pushes[0] = pushes[1] = 0; phaseState = "neutral";   // the stages start in the middle
        BuildEnemyHull();
        FitSubsystems(enemy, enemy.placeholder != null ? enemy.placeholder : enemy.root, _trim, _shieldMat);   // on the placeholder, at the hull's scale
        FitSubsystems(ally, carrier.transform, _trimAlly, _shieldAllyMat);
        LoadEnemyModel();   // Astra's hull replaces the primitives when it lands (2026-09-22)
        enemy.root.position = enemy.pos - game.worldOffset;
        enemy.root.rotation = enemy.q;
        active = true;
        state = "battle";
        game.Toast("The Front · enemy battleship " + Data.Fm(SEPARATION) + " m ahead · its turrets first, then the shield generators, then the core", false);
        if (game.hud != null) game.hud.Dirty();
    }

    static MeshRenderer Prim(PrimitiveType kind, Transform parent, Vector3 at, Vector3 scale, Material mat, string name = "Part")
    {
        var go = GameObject.CreatePrimitive(kind);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    /// The enemy hull: a box the cargo ship's size, a bridge tower forward, an engine bank aft with its glow, a hangar
    /// mouth on the port side, red trim.
    void BuildEnemyHull()
    {
        var root = new GameObject("EnemyBattleship").transform;
        enemy.root = root;
        // the placeholder stands in at the hull's own scale until the model lands (2026-09-23)
        var vis = new GameObject("Placeholder").transform;
        vis.SetParent(root, false);
        vis.localScale = Vector3.one * enemy.scale;
        enemy.placeholder = vis;
        Prim(PrimitiveType.Cube, vis, Vector3.zero, HULL_HALF * 2f, _hull, "Hull");
        Prim(PrimitiveType.Cube, vis, new Vector3(-3900f, 0f, 0f), new Vector3(500f, 900f, 1500f), _dark, "Engines");
        for (int i = -1; i <= 1; i++) enemy.lit.Add(Prim(PrimitiveType.Sphere, vis, new Vector3(-4180f, 0f, i * 450f), Vector3.one * 220f, _lamp, "EngineGlow"));
        Prim(PrimitiveType.Cube, vis, new Vector3(1200f, 640f, 0f), new Vector3(3400f, 200f, 1200f), _plate, "Deck");
        enemy.bayLamp = Prim(PrimitiveType.Cube, vis, new Vector3(0f, -150f, 905f), new Vector3(1400f, 380f, 12f), _lamp, "Mouth");
        enemy.lit.Add(enemy.bayLamp);
        enemy.lit.Add(Prim(PrimitiveType.Cube, vis, new Vector3(3702f, 0f, 0f), new Vector3(8f, 500f, 1400f), _trim, "Bow"));
        for (int i = 0; i < 4; i++) enemy.lit.Add(Prim(PrimitiveType.Cube, vis, new Vector3(-2600f + i * 1400f, 548f, -905f), new Vector3(700f, 6f, 6f), _trim, "Trim"));
        AddDeckLight(root, 1400f * enemy.scale, enemy.scale);
    }

    /// A light over the deck, so the hull reads on its night side.
    static void AddDeckLight(Transform root, float height, float scale)
    {
        var lgo = new GameObject("Light");
        lgo.transform.SetParent(root, false);
        lgo.transform.localPosition = new Vector3(0f, height, 0f);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Data.Hex("#ff8aa0");
        l.intensity = 0.6f;   // a hull this size washed out white at 1.2 over 7,000 u
        l.range = 5000f * scale;
        l.shadows = LightShadows.None;
    }

    // ---- Astra's hull (2026-09-22, the v1 handoff: `enemy_battleship.glb` and its manifest in StreamingAssets)
    public string modelState = "pending";

    /// Loaded with glTFast under the enemy root at scale one and turned a half turn about Y, as the cargo carrier is
    /// (glTFast mirrors X), so its nose lands on +X and its bay on +Z; then its named nodes are bound to the subsystems
    /// in place of the primitives. Until it lands, or if it never does, the primitive hull stands.
    async void LoadEnemyModel()
    {
        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "enemy_battleship.glb");
            if (!System.IO.File.Exists(path)) { modelState = "no glb"; return; }
            var bytes = System.IO.File.ReadAllBytes(path);
            var gltf = new GLTFast.GltfImport();
            bool ok = await gltf.Load(bytes);
            if (!ok || enemy == null || enemy.root == null) { modelState = "load failed"; return; }
            var holder = new GameObject("Model");
            holder.transform.SetParent(enemy.root, false);
            holder.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);   // the carrier's half turn: glTFast's X mirror put the nose on -X
            holder.transform.localScale = Vector3.one * enemy.scale;   // twice the authored size (2026-09-23)
            ok = await gltf.InstantiateSceneAsync(holder.transform, 0);
            if (!ok || enemy == null || enemy.root == null) { modelState = "scene failed"; return; }
            BindEnemyModel(holder.transform);
        }
        catch (System.Exception e)
        {
            modelState = "error " + e.Message;
            Debug.LogWarning("battleship: " + e);
        }
    }

    /// A turret assembly back to its authored size on a hull at another scale (2026-09-23, the user's: "make them their
    /// original size"): each of the turret's nodes that is not under another of them is scaled about the anchor, so the
    /// head, pad, barrel, lamp and muzzle shrink together whatever the hierarchy, and the pad stays on the deck.
    static void ScaleTurret(Transform h, string key, Transform anchor, float k)
    {
        string[] names = { key, key + "_head", key + "_pad", key + "_barrel", key + "_lamp", key + "_muzzle" };
        var nodes = new List<Transform>();
        foreach (var n in names) { var t = Ship.FindDeep(h, n); if (t != null) nodes.Add(t); }
        var origin = anchor.position;
        foreach (var t in nodes)
        {
            bool under = false;
            foreach (var o in nodes) if (o != t && t.IsChildOf(o)) { under = true; break; }
            if (under) continue;
            t.localScale = t.localScale * k;
            t.position = origin + (t.position - origin) * k;
        }
    }

    /// The model's nodes onto the subsystems: the anchors' positions (read from the nodes themselves, in the root's frame,
    /// so the import chain is what counts), the turret heads as the pivots the code turns with their barrels and eyes,
    /// the bodies as what goes dark, the lamps as what goes out, the bay, the hull's own lamps and glows; the primitives
    /// go, and the bubbles are made afresh at the new positions.
    void BindEnemyModel(Transform h)
    {
        var b = enemy;
        for (int i = b.root.childCount - 1; i >= 0; i--) { var c = b.root.GetChild(i); if (c != h) Object.Destroy(c.gameObject); }
        b.lit.Clear();
        int ti = 0, gi = 0;
        foreach (var s in b.subs)
        {
            string key, bodyName, lampName;
            if (s.kind == TURRET) { ti++; key = "turret_" + ti; bodyName = key + "_head"; lampName = key + "_lamp"; }
            else if (s.kind == GENERATOR) { gi++; key = "generator_" + gi; bodyName = key + "_body"; lampName = key + "_lamp"; }
            else if (s.kind == HANGAR) { key = "hangar_control"; bodyName = "hangar_body"; lampName = "hangar_lamp"; }
            else { key = "core"; bodyName = "core_body"; lampName = "core_lamp"; }
            var anchor = Ship.FindDeep(h, key);
            var body = Ship.FindDeep(h, bodyName);
            var lamp = Ship.FindDeep(h, lampName);
            if (s.kind == TURRET && anchor != null && b.scale != 1f) ScaleTurret(h, key, anchor, 1f / b.scale);   // the turrets at their authored size on the doubled hull (2026-09-23)
            if (anchor != null) s.local = b.root.InverseTransformPoint(anchor.position);
            if (s.kind == TURRET)
            {
                if (body != null) { s.head = body; s.headMr = body.GetComponent<MeshRenderer>(); s.headOffset = b.root.InverseTransformPoint(body.position) - s.local; }
                var barrel = Ship.FindDeep(h, key + "_barrel");
                s.barrelMr = barrel != null ? barrel.GetComponent<MeshRenderer>() : null;
                var pad = Ship.FindDeep(h, key + "_pad");
                s.node = pad != null ? pad : anchor;
            }
            else
            {
                s.headMr = body != null ? body.GetComponent<MeshRenderer>() : null;
                s.node = body != null ? body : anchor;
            }
            s.eyeMr = lamp != null ? lamp.GetComponent<MeshRenderer>() : null;
            if (s.dead)   // fell while the model was loading: its looks follow
            {
                if (s.headMr != null) s.headMr.sharedMaterial = _dark;
                if (s.barrelMr != null) s.barrelMr.gameObject.SetActive(false);
                if (s.eyeMr != null) s.eyeMr.enabled = false;
            }
        }
        foreach (var s in b.subs)
        {
            if (s.kind == TURRET) continue;
            var bm = Prim(PrimitiveType.Sphere, b.root, s.local, Vector3.one * (s.hitR + 70f) * 2f, _shieldMat, "PartShield");
            bm.sharedMaterial = new Material(_shieldMat);
            bm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.bubble = bm.transform;
            bm.gameObject.SetActive(false);
        }
        var bay = Ship.FindDeep(h, "bay");
        if (bay != null) b.bayLocal = b.root.InverseTransformPoint(bay.position);
        foreach (var mr in h.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.name;
            if (n.StartsWith("hull_lamp") || n == "bow_lamp" || n.StartsWith("engine_glow") || n == "bay_lamp") b.lit.Add(mr);
            if (n == "bay_lamp") b.bayLamp = mr;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (b.destroyed) foreach (var mr in b.lit) mr.enabled = false;
        AddDeckLight(b.root, 1800f * b.scale, b.scale);
        var t1 = b.subs[0];
        modelState = "loaded · turret 1 at " + t1.local.ToString("0") + " head +" + t1.headOffset.ToString("0") + " · core at " + b.Core.local.ToString("0") + " · bay " + b.bayLocal.ToString("0") + " · lamps " + b.lit.Count;
        Debug.Log("battleship: " + modelState);
    }

    /// The subsystems on a hull, in its frame: four turrets on the deck, two shield generator domes amidships, the hangar
    /// control aft, the core as the bridge tower forward; and the shield bubble over the aft hull.
    void FitSubsystems(Base b, Transform parent, Material trim, Material shieldMat)
    {
        Vector3[] tl = { new Vector3(2300f, 600f, 520f), new Vector3(2300f, 600f, -520f), new Vector3(-1900f, 600f, 520f), new Vector3(-1900f, 600f, -520f) };
        for (int i = 0; i < 4; i++)
        {
            var s = new Subsystem { kind = TURRET, name = "turret " + (i + 1), local = tl[i], hp = TURRET_HP, maxHp = TURRET_HP, r = TURRET_R, hitR = TURRET_R + 60f };
            var rest = new Vector3(0f, 0.35f, Mathf.Sign(tl[i].z)).normalized;
            s.rest = rest; s.aim = rest;
            var pad = Prim(PrimitiveType.Cube, parent, s.local, new Vector3(320f, 60f, 320f), _plate, "TurretPad");
            s.node = pad.transform;
            var head = new GameObject("Head").transform;
            head.SetParent(parent, false);
            head.localPosition = s.HeadLocal;
            s.head = head;
            s.headMr = Prim(PrimitiveType.Sphere, head, Vector3.zero, Vector3.one * TURRET_R * 2f, _plate, "Turret");
            s.barrelMr = Prim(PrimitiveType.Cylinder, head, new Vector3(0f, 0f, TURRET_R + 120f), new Vector3(44f, 130f, 44f), _dark, "Barrel");
            s.barrelMr.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            s.eyeMr = Prim(PrimitiveType.Sphere, head, new Vector3(0f, TURRET_R * 0.55f, TURRET_R * 0.6f), Vector3.one * 44f, trim, "Eye");
            head.localRotation = Quaternion.LookRotation(rest, Vector3.up);
            b.subs.Add(s);
        }
        Vector3[] gl = { new Vector3(200f, 620f, 380f), new Vector3(200f, 620f, -380f) };
        for (int i = 0; i < 2; i++)
        {
            var s = new Subsystem { kind = GENERATOR, name = "shield generator " + (i + 1), local = gl[i], hp = GENERATOR_HP, maxHp = GENERATOR_HP, r = GENERATOR_R, hitR = GENERATOR_R + 60f };
            s.headMr = Prim(PrimitiveType.Sphere, parent, s.local, Vector3.one * GENERATOR_R * 2f, _plate, "Generator");
            s.node = s.headMr.transform;
            s.eyeMr = Prim(PrimitiveType.Sphere, parent, s.local + new Vector3(0f, GENERATOR_R * 0.8f, 0f), Vector3.one * 90f, trim, "GeneratorLamp");
            b.subs.Add(s);
        }
        {
            var s = new Subsystem { kind = HANGAR, name = "hangar control", local = new Vector3(-1200f, 600f, 0f), hp = HANGAR_HP, maxHp = HANGAR_HP, r = HANGAR_R, hitR = HANGAR_R + 80f };
            s.headMr = Prim(PrimitiveType.Cube, parent, s.local, new Vector3(520f, 300f, 520f), _dark, "HangarControl");
            s.node = s.headMr.transform;
            s.eyeMr = Prim(PrimitiveType.Cube, parent, s.local + new Vector3(0f, 156f, 0f), new Vector3(530f, 8f, 530f), trim, "HangarLamp");
            b.subs.Add(s);
        }
        {
            var s = new Subsystem { kind = CORE, name = "core", local = new Vector3(3000f, 760f, 0f), hp = CORE_HP, maxHp = CORE_HP, r = CORE_R, hitR = CORE_R + 80f };
            s.headMr = Prim(PrimitiveType.Cube, parent, s.local, new Vector3(560f, 460f, 700f), _plate, "Bridge");
            s.node = s.headMr.transform;
            s.eyeMr = Prim(PrimitiveType.Cube, parent, s.local + new Vector3(282f, 60f, 0f), new Vector3(8f, 160f, 600f), trim, "BridgeGlass");
            b.subs.Add(s);
        }
        // the shield as a bubble on each part it covers (2026-09-22: one bubble over the aft hull had the aft turrets inside
        // it, so a bolt going for them was soaked before it arrived and they could not be killed; the core sat outside it):
        // the generators wear one while any turret stands, the hangar and the core while any generator does
        foreach (var s in b.subs)
        {
            if (s.kind == TURRET) continue;
            var bm = Prim(PrimitiveType.Sphere, parent, s.local, Vector3.one * (s.hitR + 70f) * 2f, shieldMat, "PartShield");
            bm.sharedMaterial = new Material(shieldMat);   // its own, so its flash is its own
            bm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.bubble = bm.transform;
            bm.gameObject.SetActive(false);
        }
        // a hull at another scale (the enemy's, 2026-09-23): the parts' places, offsets and discs follow it; the placeholder
        // parts above sit under a parent at that scale, so they already show right
        if (b.scale != 1f) foreach (var s in b.subs) { s.local *= b.scale; if (s.kind == TURRET) continue; s.headOffset *= b.scale; s.r *= b.scale; s.hitR *= b.scale; }   // the turrets keep their size and discs (the user's, later the same day)
    }

    public void Tick(float dt)
    {
        if (!active) return;
        var off = game.worldOffset;
        var ship = game.ship;
        var carrier = game.carrier;
        _blink += dt;
        // ours rides with the cargo ship; theirs holds where it was built
        ally.pos = carrier.truePos;
        ally.q = carrier.basisQ;
        enemy.root.position = enemy.pos - off;
        enemy.root.rotation = enemy.q;
        BumpRocks();
        TickPhase(dt);
        foreach (var b in new[] { ally, enemy })
        {
            // each covered part's bubble: up while the part is covered, flaring when a bolt is soaked
            var baseCol = b.team == 1 ? new Color(1f, 0.28f, 0.45f) : new Color(0.37f, 0.83f, 0.94f);
            foreach (var s in b.subs)
            {
                if (s.bubble == null) continue;
                bool up = !s.dead && !b.destroyed && !b.Exposed(s);
                if (s.bubble.gameObject.activeSelf != up) s.bubble.gameObject.SetActive(up);
                if (!up) continue;
                s.flash = Mathf.Max(0f, s.flash - dt * 3f);
                var m = s.bubble.GetComponent<MeshRenderer>().sharedMaterial;
                m.SetColor("_Color", new Color(baseCol.r + 0.4f * s.flash, baseCol.g + 0.4f * s.flash, baseCol.b + 0.3f * s.flash, 0.22f + 0.5f * s.flash + 0.04f * Mathf.Sin(_blink * 5f)));
            }
            if (b.destroyed)
            {
                _smokeT -= dt;
                if (_smokeT <= 0f && game.explosions != null && (b.pos - ship.TruePos).magnitude < 40000f)
                {
                    _smokeT = Random.Range(0.3f, 0.7f);
                    game.explosions.Smoke(b.ToTrue(b.Core.local) + Random.insideUnitSphere * 300f, Random.onUnitSphere * 10f + Vector3.up * 8f);
                }
                TickFlames(b, dt);
                continue;
            }
            TickTurrets(b, dt);
            TickHangar(b, dt);
            TickFlames(b, dt);
        }
        if (state != "battle") return;
        if (enemy.destroyed) Won();
        else if (ally.destroyed) Lost();
    }

    /// A base's turrets: each picks the nearest hostile in reach (for theirs: the player, our wingmen; for ours: their
    /// fighters), swings onto it and fires the outpost's bolt with a lead and a spread.
    void TickTurrets(Base b, float dt)
    {
        var ship = game.ship;
        bool shipUp = game.started && !ship.docked && ship.warp == null && ship.cut == null && ship.CanFly;
        foreach (var s in b.subs)
        {
            if (s.kind != TURRET || s.dead) continue;
            var hp = b.ToTrue(s.HeadLocal);
            Vector3 tp = Vector3.zero, tv = Vector3.zero;
            float td = float.PositiveInfinity;
            // the nearest hostile the turret can see: above its deck and not behind another part of its own ship (2026-09-23)
            if (b.team == 1 && shipUp && CanSee(b, s, hp, ship.TruePos)) { float d = (ship.TruePos - hp).magnitude; if (d < td) { td = d; tp = ship.TruePos; tv = ship.vel; } }
            foreach (var r in game.raiders.raiders)
            {
                if (r.dead || r.team == b.team) continue;
                float d = (r.pos - hp).magnitude;
                if (d < td && CanSee(b, s, hp, r.pos)) { td = d; tp = r.pos; tv = r.vel; }
            }
            bool have = td < TURRET_REACH * 1.15f;
            var to = have ? tp - hp : Vector3.zero;
            var want = have && td > 1f ? to / td : b.Dir(s.rest);
            s.aim = Vector3.Slerp(s.aim, want, 1f - Mathf.Exp(-dt * TURRET_SLEW)).normalized;
            s.head.rotation = Quaternion.LookRotation(s.aim, b.Dir(Vector3.up));
            s.fireCd -= dt;
            if (!have || td >= TURRET_REACH || Vector3.Dot(s.aim, to / Mathf.Max(1f, td)) < 0.995f || game.raiders.holdFire) continue;
            if (b.team == 1) game.raiders.threat++;
            if (s.fireCd > 0f) continue;
            s.fireCd = 1f / TURRET_RATE;
            float leadErr = Random.Range(0.9f, 1.1f);   // the lead a tenth off either way (0.97 to 1.03 for a day, 0.75 to 1.1 before)
            var muzzle = hp + s.aim * (TURRET_R + 260f);   // the turrets are their authored size on any hull
            var dir = (tp + tv * (td / TURRET_BOLT_SPEED) * leadErr - muzzle).normalized + new Vector3(Random.Range(-TURRET_SPREAD, TURRET_SPREAD), Random.Range(-TURRET_SPREAD, TURRET_SPREAD), Random.Range(-TURRET_SPREAD, TURRET_SPREAD));
            game.raiders.Fire(muzzle, dir.normalized, TURRET_DMG, false, TURRET_BOLT_SPEED, b.team, true);   // a heavy bolt, drawn large
            if (game.sparks != null) game.sparks.Burst(muzzle, 44, 220f, Data.Hex("#ffd090"), 2.2f);   // a muzzle flash to match (2026-09-22; bigger 2026-09-23)
            float dd = (muzzle - ship.TruePos).magnitude;
            float att = Audio.Falloff(dd, TURRET_SOUND_R);   // 8 dB a doubling past 2,000 u, gone past 19,000 (2026-09-23; it carried to 64,000)
            if (att > Audio.SILENT) Audio.Heavy("blaster", att);   // the big gun: the blaster pitched down, louder, carrying (2026-09-23)
        }
    }

    /// Whether a deck turret can see a point (2026-09-23, the user's: "make sure the turrets can't shoot through the ship
    /// itself"): the line from its head to the point has to climb, or at least not dip more than a whisker below the
    /// deck plane (the hull is under the deck, so anything below it is behind the ship), and must not pass through the
    /// hit sphere of another part of the same ship, standing or wrecked (the bridge tower, the domes, the hangar block,
    /// the other turrets' heads).
    static bool CanSee(Base b, Subsystem from, Vector3 head, Vector3 p)
    {
        var to = p - head;
        float d = to.magnitude;
        if (d < 1f) return true;
        if (Vector3.Dot(to / d, b.Dir(Vector3.up)) < -0.08f) return false;
        foreach (var o in b.subs)
        {
            if (o == from) continue;
            var c = b.ToTrue(o.HeadLocal);
            float u = Mathf.Clamp01(Vector3.Dot(c - head, to) / (d * d));
            if (u <= 0f || u >= 1f) continue;
            if ((head + to * u - c).magnitude < o.hitR * 0.8f) return false;
        }
        if (Solids.Blocked(head, p, out _)) return false;   // nor through a rock, the other hull or its own (2026-09-23, the user asked)
        return true;
    }

    /// A base's hangar: while it stands, fighters launch until ALOFT are up, one every LAUNCH_EVERY seconds; they leave
    /// out of the port mouth already on the attack.
    void TickHangar(Base b, float dt)
    {
        b.fighters.RemoveAll(r => r.dead);
        var h = b.Hangar;
        if (h == null || h.dead || b.fighters.Count >= ALOFT) { b.launchT = Mathf.Min(b.launchT, 3f); return; }
        b.launchT -= dt;
        if (b.launchT > 0f) return;
        b.launchT = LAUNCH_EVERY;
        // in its attack phase a side's fighters come in at the neutral zone, the forward rally point, not the hangar (2026-09-23)
        bool rally = attacking == b.team;
        var at = rally ? Mid + Random.insideUnitSphere * RALLY_R : b.Bay + Random.insideUnitSphere * 40f;
        var r = game.raiders.Make(at, b.pos, b.team);
        r.state = "attack";
        r.heading = rally ? ((b.team == 0 ? enemy.pos : ally.pos) - at).normalized : b.Dir(new Vector3(0f, 0f, 1f));
        r.spd = 800f;
        r.fireCd = 1.5f;
        b.fighters.Add(r);
        b.launched++;
        if (game.hud != null) game.hud.Feed(b.team == 0 ? "Wingman launched · " + b.fighters.Count + " up" : "Enemy fighter launched", b.team == 1);   // the feed, not a toast (2026-09-22)

    }

    void TickFlames(Base b, float dt)
    {
        if (game.explosions == null) return;
        foreach (var s in b.subs)
        {
            if (s.flameT <= 0f) continue;
            s.flameT -= dt;
            var p = b.ToTrue(s.HeadLocal);
            if (Random.value < dt * 6f) game.explosions.Flame(p + Random.insideUnitSphere * s.r * 0.6f, Random.onUnitSphere * 15f);
            if (Random.value < dt * 3f) game.explosions.Smoke(p + Random.insideUnitSphere * s.r * 0.6f, Random.onUnitSphere * 8f);
        }
    }

    /// Rocks that drift into the enemy hull are shoved off it (the cargo ship does its own).
    void BumpRocks()
    {
        var belt = game.belt;
        _bumpFrame++;
        if (_bumpFrame % 30 == 1) _bumpIds = belt.RocksWithin(enemy.pos, BUMP_RANGE * enemy.scale);
        foreach (int i in _bumpIds)
        {
            if (i >= belt.count || !belt.alive[i]) continue;
            float r = belt.radius[i];
            var to = belt.RockPos(i) - enemy.pos;
            float d = to.magnitude;
            float min = 4200f * enemy.scale + r;
            if (d >= min || d < 1f) continue;
            var n = to / d;
            belt.PlaceFree(i, enemy.pos + n * min, n * 40f);
        }
    }

    /// What a fighter goes for (Raiders.Tick asks every frame): for theirs, the player when flying inside ENGAGE, else
    /// the nearest of our wingmen, else our nearest exposed subsystem; for ours, the nearest enemy fighter, else their
    /// nearest exposed subsystem. `any` false when the side has nothing left to attack (a base that has fallen).
    public Vector3 PickTarget(Raiders.Raider r, out Vector3 vel, out bool any)
    {
        vel = Vector3.zero; any = false;
        var ship = game.ship;
        var foe = r.team == 1 ? ally : enemy;
        var own = r.team == 1 ? enemy : ally;
        bool onAttack = attacking == r.team, onDefence = attacking >= 0 && attacking != r.team;
        // the stages (2026-09-23): the fighter's home by the phase, the neutral zone, short of the other ship on the attack,
        // off its own on the defence; it holds and fights there when it has no mark
        r.home = onAttack ? foe.pos + (Mid - foe.pos).normalized * 6000f : onDefence ? own.pos + (Mid - own.pos).normalized * 4000f : Mid;
        // the player, for theirs, when near; on the defence only when the player is on their ship
        if (r.team == 1 && game.started && !ship.docked && ship.warp == null && ship.cut == null && ship.CanFly)
        {
            float dp = (ship.TruePos - r.pos).magnitude;
            if (dp < ENGAGE && (!onDefence || (ship.TruePos - own.pos).magnitude < TURRET_REACH * 1.5f)) { any = true; vel = ship.vel; return ship.TruePos; }
        }
        // the nearest fighter of the other side: on the defence only one that is on its own ship
        Raiders.Raider bestR = null; float bd = float.PositiveInfinity;
        foreach (var o in game.raiders.raiders)
        {
            if (o.dead || o.team == r.team) continue;
            if (onDefence && (o.pos - own.pos).magnitude > TURRET_REACH * 1.5f) continue;
            float d = (o.pos - r.pos).magnitude;
            if (d < bd) { bd = d; bestR = o; }
        }
        if (bestR != null && bd < 12000f && !onAttack) { any = true; vel = bestR.vel; return bestR.pos; }
        // on the attack: the other ship's exposed parts, in the order they open; a fighter right in the way first
        if (onAttack)
        {
            if (bestR != null && bd < 3000f) { any = true; vel = bestR.vel; return bestR.pos; }
            Subsystem bestS = null; bd = float.PositiveInfinity;
            foreach (var s in foe.subs)
            {
                if (!foe.Exposed(s)) continue;
                float d = (foe.ToTrue(s.HeadLocal) - r.pos).magnitude;
                if (d < bd) { bd = d; bestS = s; }
            }
            if (bestS != null) { any = true; vel = foe.team == 0 ? game.carrier.vel : Vector3.zero; return foe.ToTrue(bestS.HeadLocal); }
        }
        if (bestR != null && !onDefence) { any = true; vel = bestR.vel; return bestR.pos; }
        return r.home;
    }

    /// The stages (2026-09-23): in neutral the meter drifts toward the middle and fills with kills; at an end that side's
    /// attack phase starts (its fifth push breaks the other ship); an attack ends when the defenders drag the meter back
    /// past the middle or the clock runs out, and the fight returns to the middle with the meter reset.
    void TickPhase(float dt)
    {
        if (state != "battle") return;
        if (attacking < 0)
        {
            // the ends first, then the drift: a full meter drifted before it was read would never start the attack
            if (morale >= FILL - 0.01f) { StartAttack(0); return; }
            if (morale <= -FILL + 0.01f) { StartAttack(1); return; }
            if (morale > 0f) morale = Mathf.Max(0f, morale - DRIFT * dt);
            else if (morale < 0f) morale = Mathf.Min(0f, morale + DRIFT * dt);
        }
        else
        {
            attackT -= dt;
            bool back = attacking == 0 ? morale <= 0f : morale >= 0f;
            if (back || attackT <= 0f) EndAttack(back);
        }
    }

    void StartAttack(int team)
    {
        attacking = team;
        attackT = ATTACK_T;
        pushes[team]++;
        morale = team == 0 ? FILL : -FILL;
        phaseState = (team == 0 ? "attack" : "defend") + " · push " + pushes[team];
        if (pushes[team] >= PUSH_CAP)
        {
            // the fifth push breaks the other ship: its core goes
            var target = team == 0 ? enemy : ally;
            var core = target.Core;
            if (core != null && !core.dead) Damage(target, core, core.hp + 1f, target.ToTrue(core.HeadLocal), team == 0 ? "The fifth push" : "Their fifth push");
            return;
        }
        if (team == 0) { game.Toast("Attack · the enemy battleship is open · push " + pushes[0] + " of " + PUSH_CAP, false); Audio.Play("chime"); }
        else { game.Toast("Defend · they are on our battleship · their push " + pushes[1] + " of " + PUSH_CAP, true); Audio.EnemyAlert(); }
        if (game.hud != null) game.hud.Dirty();
    }

    void EndAttack(bool pushedBack)
    {
        int was = attacking;
        attacking = -1;
        morale = 0f;
        attackT = 0f;
        phaseState = "neutral";
        if (was == 0) game.Toast(pushedBack ? "Pushed back · the fight returns to the middle" : "The attack is over · the fight returns to the middle", true);
        else game.Toast(pushedBack ? "They are pushed back · the fight returns to the middle" : "Their attack is over · the fight returns to the middle", false);
        if (game.hud != null) game.hud.Dirty();
    }

    /// A kill on the field moves the meter: theirs killed by us +1 (+2 by you), ours killed by them -1.
    public void OnKill(Raiders.Raider r, string by)
    {
        if (!Active) return;
        if (r.team == 1) morale += by == "You" ? 2f : 1f; else morale -= 1f;
        morale = Mathf.Clamp(morale, -FILL, FILL);
    }

    /// Your ship going down costs three.
    public void OnPlayerDown()
    {
        if (!Active) return;
        morale = Mathf.Clamp(morale - 3f, -FILL, FILL);
    }

    /// A bolt's segment this frame against the other side's base (`team` is the bolt's): the exposed subsystems, and the
    /// shield bubble, which soaks a hit on the hangar or the core and flares. True when the bolt is spent; `killed` when
    /// the hit finished a subsystem.
    public bool HitSegment(int team, Vector3 a, Vector3 b, float dmg, out bool killed, string by = null)
    {
        killed = false;
        if (!Active) return false;
        var target = team == 0 ? enemy : ally;
        if (target == null || target.destroyed) return false;
        var ab = b - a;
        float ab2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
        foreach (var s in target.subs)
        {
            if (s.dead) continue;
            var c = target.ToTrue(s.HeadLocal);
            float u = Mathf.Clamp01(Vector3.Dot(c - a, ab) / ab2);
            var p = a + ab * u;
            if ((p - c).magnitude >= s.hitR) continue;
            if (!target.Exposed(s))
            {
                // covered: the part's own bubble soaks the bolt and flares; nothing else on the hull soaks anything, so a bolt
                // for a turret is never stopped short of it (2026-09-22: the old bubble over the aft hull did just that)
                s.flash = 1f;
                if (game.sparks != null) game.sparks.Burst(p, 10, 120f, target.team == 1 ? Data.Hex("#ff6a9a") : Data.Hex("#7fd8ff"), 1.2f);
                return true;
            }
            Damage(target, s, dmg, p, by);
            killed = s.dead;
            return true;
        }
        return false;
    }

    void Damage(Base b, Subsystem s, float dmg, Vector3 at, string by)
    {
        s.hp -= dmg;
        if (by == "You") s.hitT = Time.time;
        if (game.sparks != null) game.sparks.Burst(at, 12, 180f, Data.Hex("#ff8a3a"), 1f);
        if (s.hp > 0f) return;
        s.hp = 0f;
        s.dead = true;
        if (game.hud != null) game.hud.Feed((by ?? "Someone") + " destroyed " + (b.team == 1 ? "the enemy " : "our ") + s.name, b.team == 0);   // the kill feed (2026-09-22)
        s.flameT = s.kind == CORE ? 14f : 8f;
        var p = b.ToTrue(s.HeadLocal);
        if (game.explosions != null) game.explosions.Raider(p, b.team == 0 ? game.carrier.vel : Vector3.zero);
        float bd = (p - game.ship.TruePos).magnitude;
        Audio.Play("boom", Mathf.Max(-24f, bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f)));
        if (s.headMr != null) s.headMr.sharedMaterial = _dark;
        if (s.barrelMr != null) s.barrelMr.gameObject.SetActive(false);
        if (s.eyeMr != null) s.eyeMr.enabled = false;
        string whose = b.team == 1 ? "Enemy" : "Our";
        if (s.kind == TURRET) { int up = b.Up(TURRET); game.Toast(whose + " " + s.name + " destroyed · " + (up > 0 ? up + " standing" : "the shield generators are exposed"), b.team == 0); }
        else if (s.kind == GENERATOR) { int up = b.Up(GENERATOR); game.Toast(whose + " " + s.name + " destroyed · " + (up > 0 ? up + " standing" : "the shield has failed · the hangar and the core are exposed"), b.team == 0); }
        else if (s.kind == HANGAR)
        {
            game.Toast(whose + " hangar control destroyed · no more fighters from it", b.team == 0);
            if (b.bayLamp != null) b.bayLamp.enabled = false;   // the mouth goes dark with it
        }
        else
        {
            b.destroyed = true;
            if (game.explosions != null)
            {
                game.explosions.Raider(p + new Vector3(260f, 180f, -120f), Vector3.zero);
                game.explosions.Raider(p + new Vector3(-200f, -260f, 220f), Vector3.zero);
            }
            Audio.Play("boom_big", Mathf.Max(-24f, bd <= 900f ? 0f : -20f * Mathf.Log10(bd / 900f)));
            foreach (var o in b.subs) o.flameT = Mathf.Max(o.flameT, 10f);
            foreach (var mr in b.lit) if (mr != null) mr.enabled = false;   // every lamp and glow on the hull goes out
            if (b.shield != null) b.shield.gameObject.SetActive(false);
        }
        if (game.hud != null) game.hud.Dirty();
    }

    void Won()
    {
        state = "won";
        attacking = -1;
        float pay = Reward();
        State.credits += pay;
        State.earned += pay;
        State.battlesWon++;
        State.Save();
        Audio.Play("cash");
        foreach (var r in enemy.fighters) if (!r.dead) { r.state = "idle"; r.wp = enemy.pos + Vector3.up * 4000f; }
        game.Toast("The enemy battleship's core is gone · the field is ours · +" + Data.Fmt(pay) + " cr", false);
        if (game.hud != null) game.hud.Dirty();
    }

    void Lost()
    {
        state = "lost";
        attacking = -1;
        State.battlesLost++;
        State.Save();
        foreach (var r in enemy.fighters) if (!r.dead) { r.state = "idle"; r.wp = enemy.pos + Vector3.up * 4000f; }
        foreach (var r in ally.fighters) if (!r.dead) { r.state = "idle"; r.wp = ally.pos + Vector3.up * 4000f; }
        game.Toast("Our battleship's core is gone · the field is lost · warp home and come back for another go", true);
        if (game.hud != null) game.hud.Dirty();
    }

    /// The ship against the enemy hull: three spheres along its length and the shield bubble while it is up.
    public bool Collide(Vector3 p, float m, out Vector3 n, out float push)
    {
        n = Vector3.zero; push = 0f;
        if (!active || enemy == null) return false;
        for (int i = -1; i <= 1; i++)
        {
            var c = enemy.ToTrue(new Vector3(i * 2400f * enemy.scale, 0f, 0f));
            var to = p - c;
            float d = to.magnitude;
            float cr = 1000f * enemy.scale + m;
            if (d < cr && d > 0.01f) { n = to / d; push = cr - d; return true; }
        }
        return false;
    }

    /// The nearest exposed enemy subsystem to the ship (for the test's aim), else the enemy core.
    public Vector3 AimPoint()
    {
        var sp = game.ship.TruePos;
        Subsystem best = null; float bd = float.PositiveInfinity;
        foreach (var s in enemy.subs) { if (!enemy.Exposed(s)) continue; float d = (enemy.ToTrue(s.HeadLocal) - sp).magnitude; if (d < bd) { bd = d; best = s; } }
        return best != null ? enemy.ToTrue(best.HeadLocal) : enemy.pos;
    }
}
