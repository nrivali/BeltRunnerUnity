using System.Collections.Generic;
using UnityEngine;

/// The raider outpost (2026-09-19, the user's "enemy base raid mission"; new in the Unity port, there is no browser
/// original): a pirate station off one rich pocket of the Kessler Belt, riding along with the pocket's drift. A core
/// sphere under a shield bubble, a slow ring round it, three arms out to gun turrets, a hangar block under the core that
/// launches defenders, red trim and beacons like the raiders' own. The turrets fire the raiders' bolts at a ship inside
/// TURRET_REACH (rough gunners, like the raiders, with a lead and a spread); the core is shielded while any turret stands,
/// and only then can it be shot. Its destruction pays the contract (State.raid, the hangar window's Contracts tab) and
/// throws salvage; the wreck stays dark until a rebuilt outpost posts a fresh contract REPOST_AFTER seconds of play
/// later. Positions are true world coordinates, like the raiders'; Game ticks it right after the raiders, so its turrets
/// add to their threat (the combat music, the HUD's THREAT, the lock rules follow).
public class Outpost
{
    public const int TURRETS = 3;
    public const float TURRET_HP = 150f, CORE_HP = 500f;
    public const float TURRET_REACH = 7000f;                     // 875 m: the turrets open fire from here (the raiders from 750 m)
    public const float TURRET_DMG = 6f, TURRET_RATE = 2.5f;      // a bolt and a half of the raiders' each, 2.5 a second per turret
    public const float TURRET_SPREAD = 0.045f;                   // about 2.5 degrees: half the raiders' spread, since a turret has a steady mount
    public const float TURRET_SLEW = 4f;                         // how fast a turret swings onto the ship (an exponential ease)
    public const float ENGAGE = 12000f;                          // 1,500 m: the outpost wakes (the turrets track, the hangar launches)
    public const float RING_R = 1400f, CORE_R = 600f, TURRET_R = 150f, SHIELD_R = 820f;
    public const float TURRET_HIT_R = 190f, CORE_HIT_R = 640f;   // the discs a bolt has to cross (generous, like the raiders' HIT_R)
    public const int DEFENDERS_ALIVE = 2, DEFENDERS_TOTAL = 6;   // two in the air at a time, six over a raid, while the core stands
    public const float LAUNCH_EVERY = 9f;
    public const float REWARD_BASE = 2500f, REWARD_STEP = 750f;  // the contract: more for every outpost already put down under one
    public const float REPOST_AFTER = 600f;                      // seconds of play after a raid before a rebuilt outpost and a fresh contract
    public const float BUMP_RANGE = 3200f;                       // rocks drifting into the structure are shoved off it
    public const float STATION_OFF = 22000f;                     // 2,750 m: where the cargo ship holds station off the outpost, well outside its own gun cover (Raiders.SAFE_R), so the defenders still come

    public class Turret
    {
        public Vector3 local;                  // in the outpost's frame (the frame never turns: the ring turns on its own)
        public Transform node, head;
        public MeshRenderer headMr, barrelMr, eyeMr;
        public float hp = TURRET_HP, fireCd, flameT;
        public bool dead;
        public Vector3 aim = Vector3.up, rest = Vector3.up;
        public Vector3 HeadLocal { get { return local + new Vector3(0f, TURRET_R + 40f, 0f); } }
    }

    public Game game;
    public bool built, destroyed;
    public Belt.Field field;                   // the rich pocket it sits off
    public Vector3 pos;                        // true world coordinates, this frame
    public float coreHp = CORE_HP;
    public readonly List<Turret> turrets = new List<Turret>();
    public bool engaged;                       // the ship is inside ENGAGE, flying, and the core stands
    public int turretsFiring;                  // turrets with the ship in reach and on the mount this frame (added to the raiders' threat)
    public int launched;                       // defenders launched this raid
    readonly List<Raiders.Raider> _defenders = new List<Raiders.Raider>();
    float _launchT = 2f, _smokeT, _blink, _shieldFlash;
    Vector3 _dir;
    Transform _root, _ring, _shield;
    Material _hull, _dark, _plate, _trim, _lamp, _shieldMat;
    readonly List<MeshRenderer> _beacons = new List<MeshRenderer>();
    readonly List<MeshRenderer> _lit = new List<MeshRenderer>();   // everything that glows: gone dark on the wreck
    int _bumpFrame;
    List<int> _bumpIds = new List<int>();
    bool _woke;

    public Outpost(Game g)
    {
        game = g;
        _hull = new Material(Game.Sh("Standard"));
        _hull.color = new Color(0.23f, 0.19f, 0.25f);   // the raiders' hull
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
        _lamp = new Material(Game.Sh("Standard"));
        _lamp.color = Data.Hex("#ffb070");
        _lamp.EnableKeyword("_EMISSION");
        _lamp.SetColor("_EmissionColor", Data.Hex("#ffb070") * 2f);
        _shieldMat = new Material(Game.Sh("BeltRunner/Pulse"));   // the radar pulse's rim shell, in the raiders' pink
        _shieldMat.SetColor("_Color", new Color(1f, 0.28f, 0.45f, 0.2f));
        _shieldMat.SetFloat("_Rim", 2.6f);
    }

    /// Where the cargo ship holds station off the outpost (2026-09-19, the user's: the outpost was too far to fly to): STATION_OFF
    /// further out along the line from the pocket, with the Hub berth's gentle drift, riding with the pocket like the outpost.
    public Vector3 StationPos()
    {
        float t = State.time;
        var drift = new Vector3(Mathf.Sin(t * 0.21f) * 320f, Mathf.Sin(t * 0.17f) * 240f, Mathf.Cos(t * 0.13f) * 320f);
        return pos + _dir * STATION_OFF + drift;
    }
    /// What the run out costs the cargo ship's fuel supply: 150 to 600 by the distance (about 320 from its Kessler orbit).
    public static float TransitCost(float dist) { return Mathf.Round(Mathf.Clamp(120f + dist / 6000f, 150f, 600f)); }

    /// The contract's pay: more for every outpost already put down under one.
    public static float Reward() { return REWARD_BASE + REWARD_STEP * State.raids; }
    public int TurretsUp { get { int n = 0; foreach (var t in turrets) if (!t.dead) n++; return n; } }
    /// The core is shielded while any turret stands.
    public bool Shielded { get { return !destroyed && TurretsUp > 0; } }
    /// What the window and the HUD say of it.
    public string Status
    {
        get
        {
            if (!built) return "";
            if (destroyed) return "outpost destroyed";
            int up = TurretsUp;
            return up > 0 ? up + " turret" + (up > 1 ? "s" : "") + " up · core shielded" : "core exposed · " + Mathf.CeilToInt(coreHp) + " / " + Mathf.RoundToInt(CORE_HP);
        }
    }
    /// The hangar window's signature: the window rebuilds its Contracts tab when this changes.
    public string Sig { get { return built + "|" + destroyed + "|" + TurretsUp + "|" + Mathf.RoundToInt(coreHp); } }
    /// Where the defenders leave from: the hangar block under the core, out its mouth (true coordinates).
    public Vector3 Hangar { get { return pos + new Vector3(560f, -CORE_R - 140f, 0f); } }
    /// The raid test's aim: the nearest standing turret's head, else the core.
    public Vector3 AimPoint()
    {
        var sp = game.ship.TruePos;
        Turret best = null; float bd = float.PositiveInfinity;
        foreach (var t in turrets) { if (t.dead) continue; float d = (pos + t.HeadLocal - sp).magnitude; if (d < bd) { bd = d; best = t; } }
        return best != null ? pos + best.HeadLocal : pos;
    }

    public void Clear()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null; _ring = null; _shield = null;
        turrets.Clear(); _beacons.Clear(); _lit.Clear(); _defenders.Clear(); _bumpIds.Clear();
        built = false; destroyed = false; engaged = false; turretsFiring = 0; launched = 0; coreHp = CORE_HP; _woke = false; field = null; _launchT = 2f;
    }

    /// The Kessler Belt's outpost, off the last of its rich pockets (the belt is seeded, so it is always the same one).
    /// A destroyed outpost stands as a dark wreck until REPOST_AFTER seconds of play after the raid; then it is rebuilt
    /// and the contract is open again.
    public void Build(Data.Zone zone, Belt belt)
    {
        Clear();
        if (zone.id != "kessler") return;
        var pockets = new List<Belt.Field>();
        foreach (var f in belt.fields) if (f.pocket) pockets.Add(f);
        if (pockets.Count == 0) return;
        field = pockets[pockets.Count - 1];
        _dir = new Vector3(0.62f, 0.25f, 0.74f).normalized;
        if (State.raid == 2 && State.time >= State.raidAt) State.raid = 0;   // rebuilt: the contract is open again
        destroyed = State.raid == 2;
        pos = belt.FieldCentre(field) + _dir * (field.radius * 2.2f);
        BuildMesh();
        if (destroyed) GoDark();
        built = true;
        _root.position = pos - game.worldOffset;
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

    static MeshRenderer Place(Transform parent, Mesh mesh, Material mat, Vector3 at, Quaternion rot, Vector3 scale, string name = "Part")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    void BuildMesh()
    {
        _root = new GameObject("Outpost").transform;
        // the core, a red band round its equator, the mast above it and the hangar block below
        Prim(PrimitiveType.Sphere, _root, Vector3.zero, Vector3.one * CORE_R * 2f, _hull, "Core");
        _lit.Add(Place(_root, MeshUtil.Torus(CORE_R + 30f, 50f, 64, 10), _trim, Vector3.zero, Quaternion.identity, Vector3.one, "Band"));
        Place(_root, MeshUtil.ConeX(26f, 40f, 900f, 12), _dark, new Vector3(0f, CORE_R + 440f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one, "Mast");
        Beacon(new Vector3(0f, CORE_R + 920f, 0f), 46f);
        Prim(PrimitiveType.Cube, _root, new Vector3(0f, -CORE_R - 140f, 0f), new Vector3(1100f, 280f, 640f), _plate, "Hangar");
        _lit.Add(Prim(PrimitiveType.Cube, _root, new Vector3(552f, -CORE_R - 140f, 0f), new Vector3(8f, 180f, 480f), _lamp, "Mouth"));
        _lit.Add(Prim(PrimitiveType.Cube, _root, new Vector3(-552f, -CORE_R - 140f, 0f), new Vector3(8f, 180f, 480f), _lamp, "Mouth"));
        // the ring, turning on its own
        _ring = new GameObject("Ring").transform;
        _ring.SetParent(_root, false);
        Place(_ring, MeshUtil.Torus(RING_R, 90f, 96, 12), _plate, Vector3.zero, Quaternion.identity, Vector3.one, "Ring");
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f + Mathf.PI / 6f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Prim(PrimitiveType.Cube, _ring, n * RING_R, new Vector3(260f, 220f, 260f), _hull, "Pod").transform.localRotation = Quaternion.LookRotation(n, Vector3.up);
            _lit.Add(Prim(PrimitiveType.Sphere, _ring, n * (RING_R + 150f), Vector3.one * 60f, _trim, "Pip"));
            _beacons.Add(_lit[_lit.Count - 1]);
        }
        // three arms out to the turrets, a pad at each end, the turret's head on a short pivot with its barrel and an eye
        for (int i = 0; i < TURRETS; i++)
        {
            float a = i * Mathf.PI * 2f / TURRETS;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float len = RING_R - CORE_R + 140f;
            Place(_root, MeshUtil.ConeX(70f, 90f, len, 12), _dark, n * (CORE_R + len * 0.5f - 60f), Quaternion.LookRotation(n, Vector3.up) * Quaternion.AngleAxis(-90f, Vector3.up), Vector3.one, "Arm");
            var t = new Turret { local = n * (RING_R + 40f), rest = n, aim = n };
            var pad = Prim(PrimitiveType.Cube, _root, t.local, new Vector3(320f, 70f, 320f), _plate, "Pad");
            t.node = pad.transform;
            _lit.Add(Prim(PrimitiveType.Cube, _root, t.local + new Vector3(0f, 40f, 0f), new Vector3(330f, 6f, 330f), _trim, "PadLight"));
            var head = new GameObject("Head").transform;
            head.SetParent(_root, false);
            head.localPosition = t.HeadLocal;
            t.head = head;
            t.headMr = Prim(PrimitiveType.Sphere, head, Vector3.zero, Vector3.one * TURRET_R * 2f, _plate, "Turret");   // the plate grey, so the heads read against the dark hull
            t.barrelMr = Prim(PrimitiveType.Cylinder, head, new Vector3(0f, 0f, TURRET_R + 120f), new Vector3(44f, 130f, 44f), _dark, "Barrel");
            t.barrelMr.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.eyeMr = Prim(PrimitiveType.Sphere, head, new Vector3(0f, TURRET_R * 0.55f, TURRET_R * 0.6f), Vector3.one * 44f, _trim, "Eye");
            _lit.Add(t.eyeMr);
            head.localRotation = Quaternion.LookRotation(n, Vector3.up);
            turrets.Add(t);
        }
        // the shield bubble over the core
        var sh = Prim(PrimitiveType.Sphere, _root, Vector3.zero, Vector3.one * SHIELD_R * 2f, _shieldMat, "Shield");
        sh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shield = sh.transform;
        // a light, so the structure reads on its night side
        var lgo = new GameObject("Light");
        lgo.transform.SetParent(_root, false);
        lgo.transform.localPosition = new Vector3(0f, CORE_R + 300f, 0f);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Data.Hex("#ff8aa0");
        l.intensity = 1.4f;
        l.range = 5000f;
        l.shadows = LightShadows.None;
    }

    void Beacon(Vector3 at, float r)
    {
        var mr = Prim(PrimitiveType.Sphere, _root, at, Vector3.one * r * 2f, _trim, "Beacon");
        _beacons.Add(mr);
        _lit.Add(mr);
    }

    /// The wreck: everything lit goes dark, the shield is gone, the ring stops.
    void GoDark()
    {
        foreach (var mr in _lit) if (mr != null) { mr.enabled = true; mr.sharedMaterial = _dark; }
        foreach (var t in turrets) { t.dead = true; if (t.barrelMr != null) t.barrelMr.sharedMaterial = _dark; if (t.headMr != null) t.headMr.sharedMaterial = _dark; }
        if (_shield != null) _shield.gameObject.SetActive(false);
        var l = _root != null ? _root.GetComponentInChildren<Light>() : null;
        if (l != null) l.enabled = false;
    }

    public void Tick(float dt)
    {
        if (!built) return;
        var off = game.worldOffset;
        var belt = game.belt;
        var ship = game.ship;
        pos = belt.FieldCentre(field) + _dir * (field.radius * 2.2f);   // rides with the pocket's drift
        _root.position = pos - off;
        var sp = ship.TruePos;
        float d = (sp - pos).magnitude;
        _blink += dt;
        turretsFiring = 0;
        BumpRocks(belt);
        if (destroyed)
        {
            engaged = false;
            // a dark wreck: smoke rising off the core while the ship is near enough to see it
            _smokeT -= dt;
            if (_smokeT <= 0f && d < 30000f && game.explosions != null)
            {
                _smokeT = Random.Range(0.35f, 0.8f);
                game.explosions.Smoke(pos + Random.insideUnitSphere * CORE_R * 0.7f, Random.onUnitSphere * 10f + Vector3.up * 8f);
            }
            TickFlames(dt);
            return;
        }
        _ring.Rotate(Vector3.up, 2.2f * dt, Space.Self);
        int phase = (int)(_blink * 1.5f) % 3;
        for (int i = 0; i < _beacons.Count; i++) _beacons[i].enabled = (i % 3) == phase || (i % 3) == (phase + 1) % 3;
        if (_shield != null)
        {
            bool up = Shielded;
            if (_shield.gameObject.activeSelf != up) _shield.gameObject.SetActive(up);
            if (up)
            {
                _shieldFlash = Mathf.Max(0f, _shieldFlash - dt * 3f);
                _shieldMat.SetColor("_Color", new Color(1f, 0.28f + 0.5f * _shieldFlash, 0.45f + 0.4f * _shieldFlash, 0.2f + 0.45f * _shieldFlash + 0.03f * Mathf.Sin(_blink * 5f)));
            }
        }
        bool flying = game.started && !ship.docked && ship.warp == null && ship.cut == null && ship.CanFly;
        engaged = flying && d < ENGAGE;
        if (engaged && !_woke) { _woke = true; game.Toast("Raider outpost · its turrets are tracking you", true); Audio.Play("alarm"); }
        if (!engaged && d > ENGAGE * 1.3f) _woke = false;
        // the turrets: each swings onto the ship and fires while it is in reach and on the mount; at rest they point outward
        foreach (var t in turrets)
        {
            if (t.dead) continue;
            var hp = pos + t.HeadLocal;
            var to = sp - hp;
            float td = to.magnitude;
            var want = engaged && td < TURRET_REACH * 1.15f && td > 1f ? to / td : t.rest;
            t.aim = Vector3.Slerp(t.aim, want, 1f - Mathf.Exp(-dt * TURRET_SLEW)).normalized;
            t.head.localRotation = Quaternion.LookRotation(t.aim, Vector3.up);
            t.fireCd -= dt;
            if (!engaged || td >= TURRET_REACH || Vector3.Dot(t.aim, to / Mathf.Max(1f, td)) < 0.995f || game.raiders.holdFire) continue;
            turretsFiring++;
            if (t.fireCd > 0f) continue;
            t.fireCd = 1f / TURRET_RATE;
            // a steadier gunner than a raider: the lead a little off shot by shot, the spread half theirs
            float leadErr = Random.Range(0.75f, 1.1f);
            var muzzle = hp + t.aim * (TURRET_R + 260f);
            var dir = (sp + ship.vel * (td / Raiders.BOLT_SPEED) * leadErr - muzzle).normalized + new Vector3(Random.Range(-TURRET_SPREAD, TURRET_SPREAD), Random.Range(-TURRET_SPREAD, TURRET_SPREAD), Random.Range(-TURRET_SPREAD, TURRET_SPREAD));
            game.raiders.Fire(muzzle, dir.normalized, TURRET_DMG, false);
            if (game.sparks != null) game.sparks.Burst(muzzle, 5, 70f, Data.Hex("#ffb060"), 0.9f);
            float att = td <= 300f ? 0f : -20f * Mathf.Log10(td / 300f);   // the same blaster, quieter with distance
            if (att > -30f) Audio.Shot("blaster", att);
        }
        if (turretsFiring > 0) game.raiders.threat += turretsFiring;   // the combat music, the THREAT readout and the lock rules follow
        // the hangar: while the core stands and the ship is close, raiders launch, two in the air at a time, six a raid
        _defenders.RemoveAll(r => r.dead);
        if (engaged && launched < DEFENDERS_TOTAL && _defenders.Count < DEFENDERS_ALIVE)
        {
            _launchT -= dt;
            if (_launchT <= 0f)
            {
                _launchT = LAUNCH_EVERY;
                var at = Hangar + Random.insideUnitSphere * 30f;
                var r = game.raiders.Make(at, pos);
                r.state = "attack";
                r.heading = new Vector3(1f, 0f, 0f);   // out of the mouth
                r.spd = 220f;
                r.fireCd = 1.5f;
                _defenders.Add(r);
                launched++;
                game.Toast("The outpost launched a raider", true);
            }
        }
        else if (!engaged) _launchT = Mathf.Min(_launchT, 2f);   // the first comes quickly on the next approach
        TickFlames(dt);
    }

    /// Fire licking off a downed turret for a while.
    void TickFlames(float dt)
    {
        if (game.explosions == null) return;
        foreach (var t in turrets)
        {
            if (t.flameT <= 0f) continue;
            t.flameT -= dt;
            if (Random.value < dt * 6f) game.explosions.Flame(pos + t.HeadLocal + Random.insideUnitSphere * TURRET_R * 0.6f, Random.onUnitSphere * 15f);
            if (Random.value < dt * 3f) game.explosions.Smoke(pos + t.HeadLocal + Random.insideUnitSphere * TURRET_R * 0.6f, Random.onUnitSphere * 8f);
        }
    }

    /// Rocks that drift into the structure are shoved off it (as the cargo ship does).
    void BumpRocks(Belt belt)
    {
        _bumpFrame++;
        if (_bumpFrame % 30 == 1) _bumpIds = belt.RocksWithin(pos, BUMP_RANGE);
        foreach (int i in _bumpIds)
        {
            if (i >= belt.count || !belt.alive[i]) continue;
            float r = belt.radius[i];
            var to = belt.RockPos(i) - pos;
            float d = to.magnitude;
            float min = RING_R + 200f + r;
            if (d >= min || d < 1f) continue;
            var n = to / d;
            belt.PlaceFree(i, pos + n * min, n * 40f);
        }
    }

    /// A player bolt's segment this frame against the turrets, then the core (its shield, while it is up, soaks the hit
    /// and flares). True when the bolt is spent; `killed` when that hit finished a turret or the core.
    public bool HitSegment(Vector3 a, Vector3 b, float dmg, out bool killed)
    {
        killed = false;
        if (!built || destroyed) return false;
        var ab = b - a;
        float ab2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
        foreach (var t in turrets)
        {
            if (t.dead) continue;
            var c = pos + t.HeadLocal;
            float u = Mathf.Clamp01(Vector3.Dot(c - a, ab) / ab2);
            var p = a + ab * u;
            if ((p - c).magnitude >= TURRET_HIT_R) continue;
            DamageTurret(t, dmg, p);
            killed = t.dead;
            return true;
        }
        {
            float u = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab2);
            var p = a + ab * u;
            float r = (p - pos).magnitude;
            if (Shielded && r < SHIELD_R)
            {
                _shieldFlash = 1f;
                if (game.sparks != null) game.sparks.Burst(p, 10, 120f, Data.Hex("#ff6a9a"), 1.2f);
                return true;
            }
            if (!Shielded && r < CORE_HIT_R)
            {
                DamageCore(dmg, p);
                killed = destroyed;
                return true;
            }
        }
        return false;
    }

    void DamageTurret(Turret t, float dmg, Vector3 at)
    {
        t.hp -= dmg;
        if (game.sparks != null) game.sparks.Burst(at, 12, 180f, Data.Hex("#ff8a3a"), 1f);
        if (t.hp > 0f) return;
        t.dead = true;
        t.hp = 0f;
        t.flameT = 7f;
        var hp = pos + t.HeadLocal;
        if (game.explosions != null) game.explosions.Raider(hp, Vector3.zero);
        float bd = (hp - game.ship.TruePos).magnitude;
        Audio.Play("boom", Mathf.Max(-24f, bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f)));
        t.headMr.sharedMaterial = _dark;
        t.barrelMr.gameObject.SetActive(false);
        t.eyeMr.enabled = false;
        int up = TurretsUp;
        if (up > 0) game.Toast("Outpost turret destroyed · " + up + " standing", false);
        else game.Toast("Last turret down · the outpost's shield has failed · the core is exposed", false);
        if (game.hud != null) game.hud.Dirty();
    }

    void DamageCore(float dmg, Vector3 at)
    {
        coreHp -= dmg;
        if (game.sparks != null) game.sparks.Burst(at, 14, 200f, Data.Hex("#ff8a3a"), 1.1f);
        if (coreHp > 0f) return;
        coreHp = 0f;
        Destroy();
    }

    /// The core goes: three blasts, the big boom, everything dark, salvage thrown, the contract paid if it was taken, and
    /// a wreck that stands until the outpost is rebuilt.
    void Destroy()
    {
        destroyed = true;
        engaged = false;
        if (game.explosions != null)
        {
            game.explosions.Raider(pos, Vector3.zero);
            game.explosions.Raider(pos + new Vector3(260f, 180f, -120f), Vector3.zero);
            game.explosions.Raider(pos + new Vector3(-200f, -260f, 220f), Vector3.zero);
        }
        float bd = (pos - game.ship.TruePos).magnitude;
        Audio.Play("boom_big", Mathf.Max(-24f, bd <= 900f ? 0f : -20f * Mathf.Log10(bd / 900f)));
        GoDark();
        foreach (var t in turrets) t.flameT = Mathf.Max(t.flameT, 10f);
        // the salvage: five to eight lumps of outer-belt ore thrown from the core, to be flown through like any ore
        int lumps = 0;
        var keys = new List<string>(game.zone.belts[2].Keys);
        if (keys.Count > 0)
        {
            int n = Random.Range(5, 9);
            for (int i = 0; i < n; i++)
            {
                string k = keys[Random.Range(0, keys.Count)];
                var at = pos - game.worldOffset + Random.onUnitSphere * (CORE_R + 80f);
                var drift = (at - (pos - game.worldOffset)).normalized * Random.Range(25f, 60f);
                game.SpawnPickup(k, Mathf.Round(Random.Range(20f, 60f)), at, drift);
                lumps++;
            }
        }
        // the defenders still up lose heart
        foreach (var r in _defenders) if (!r.dead) { r.state = "idle"; r.wp = pos + Vector3.up * 3000f; }
        bool paid = State.raid == 1;
        if (paid)
        {
            float pay = Reward();
            State.credits += pay;
            State.earned += pay;
            State.raids++;
            Audio.Play("cash");
            game.Toast("Raider outpost destroyed · contract paid · +" + Data.Fmt(pay) + " cr · " + lumps + " lumps of salvage adrift", false);
        }
        else game.Toast("Raider outpost destroyed · no contract on it, nothing paid · " + lumps + " lumps of salvage adrift", false);
        State.raid = 2;
        State.raidAt = State.time + REPOST_AFTER;
        State.Save();
        if (game.hud != null) game.hud.Dirty();
    }

    /// The ship against the structure: the core (its shield bubble while that is up) and the turret heads are spheres
    /// it is pushed out of. `n` the surface normal, `push` how far along it.
    public bool Collide(Vector3 p, float m, out Vector3 n, out float push)
    {
        n = Vector3.zero; push = 0f;
        if (!built) return false;
        float cr = (Shielded ? SHIELD_R : CORE_R) + m;
        var to = p - pos;
        float d = to.magnitude;
        if (d < cr && d > 0.01f) { n = to / d; push = cr - d; return true; }
        foreach (var t in turrets)
        {
            var c = pos + t.HeadLocal;
            to = p - c;
            d = to.magnitude;
            float tr = TURRET_R + m;
            if (d < tr && d > 0.01f) { n = to / d; push = tr - d; return true; }
        }
        return false;
    }
}
