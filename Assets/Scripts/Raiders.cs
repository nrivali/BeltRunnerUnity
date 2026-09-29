using System.Collections.Generic;
using UnityEngine;

/// Combat: pirate raiders holding station off the rich pockets, brought back from the browser's scrapped hazards code
/// (makePirate / updateHazards) with its numbers. A hold of one to three raiders wanders round each rich pocket; when
/// the ship comes within 9,000 u (flying, and outside the cargo ship's gun cover) they attack, closing to 900 u and
/// orbiting, firing bolts that lead the ship whenever their nose is on it (the guns are fixed forward). They give up beyond 11,000 u or when the ship is disabled or docked, and
/// they die under the cargo ship's guns inside SAFE_R. The player's autocannon (a refit) fires bolts from the dish
/// focus; a kill pays a bounty and sometimes drops salvage. The player's seeker rockets (a refit, fitted from the
/// start) chase a raider and destroy it outright on contact. Positions are true world coordinates.
public class Raiders
{
    public const float SAFE_R = 9000f;      // cargo ship gun cover: raiders die here and never engage inside it
    public const float ENGAGE = 16000f;     // aggressive: they come for a ship 2,000 m out (9,000 u until 2026-09-21, the 500 m/s rebalance)
    public const float GIVE_UP = 26000f;
    public const float BOLT_SPEED = 32000f;  // a raider bolt, as fast as the player one (2026-09-23, doubled; 16,000 from 2026-09-22 (9,000 until 2026-09-22: a turning ship out-ran the lead; 2,600 before the speeds)
    public static readonly float RAIDER_DMG = Data.UPGRADES["gun"].levels[0].mult;   // the player's own base autocannon damage, 8 (2026-09-23, one for one; 6 from 2026-09-22, 4 before)
    public const float RAIDER_REACH = 8000f;   // 1,000 m, the player's own reach (2026-09-23; 10,000 for two days, 6,000 before the speeds) // raiders open fire from 750 m; their bolt lives long enough to get there
    public const float RAIDER_BOLT_LIFE = 0.4f;   // 12,800 u of flight (0.8 s at the old speed)
    public const float WHOOSH_R = 500f, WHOOSH_R_HEAVY = 900f;   // a bolt passing inside this of the ship sounds its pass (2026-09-23); a turret shell further out
    public const float RAIDER_R = 80f;   // a raider body against the rocks and the hulls (2026-09-23; 40 until the raider grew twice that day, 160 for an hour at four times)
    public const float HEAVY_BOLT_LIFE = 0.8f;    // a battleship turret bolt: 22,400 u of flight at 28,000 u/s (doubled 2026-09-23), past the 14,000 u reach (2026-09-23, the staged battle; 6.4 s for the 80,000 u reach earlier that day)
    public const float PLAYER_BOLT_SPEED = 32000f;   // the player bolt (doubled 2026-09-23): PLAYER_BOLT_LIFE of flight covers 9,600 u, past the 8,000 u gun reach (5,000 until 2026-09-21; the reach 10,000, then 20,000, until 2026-09-23)
    public const float PLAYER_BOLT_LIFE = 0.3f;   // 0.6 at the old speed
    public const float RADIUS = 28f;    // the hull, for the reticle, ranges and the pick (the model is drawn at twice its original size)
    public const float HIT_R = 120f;    // the hit box a bolt has to pass through, for the raider at twice its old size (2026-09-23; 240 for an hour, 60 before): generous, the raider is fast; it covers the wing tips
    public const float SHIP_HIT_R = Data.SHIP_R * 1.9f;   // the ship's box for enemy fire, 91 u (1.5, 72 u, for a day) (2026-09-22, the user's: "I don't seem to be getting hit at all"): it was SHIP_R × 0.8, 38 u, a third of what the raiders get for their size
    // a raider flies like a ship: it turns no faster than this, and speeds up and slows down no harder than this
    public const float TURN_RATE = Ship.TURN;   // the player's own turn, whatever it is set to (2026-09-23, one for one; 30 degrees a second before)
    public static readonly float ACCEL = Data.UPGRADES["engine"].levels[0].thrust;   // the player's own thrust, 1,050 (2026-09-23, one for one; 1,100 before)
    public const float DECEL = 1600f;

    public class Raider
    {
        public Vector3 pos, vel, home, wp;
        public List<int> nearRocks;      // the rocks about it, looked up twice a second, for the flying (2026-09-23)
        public float rocksT = -100f;
        public Vector3 rocksAt;
        public Transform node;
        public Material exhaust;
        public Transform exhaustNode;
        public bool boosting;   // the throttle wide open: the exhaust flares, a jet trail streams, a quarter more speed
        public float jetT;
        public float hp, maxHp, dmg, speed, bounty, a, fireCd;
        public float shield, maxShield, sinceHit = 99f;
        public float gunDmg, gunRate, gunReach;   // the same weapon as the player's base autocannon   // the shield soaks damage first and recharges after ten quiet seconds
        public string state = "idle";
        public bool dead;
        public bool frozen;   // the combat test: holds its place (still turns to face the ship and fires)
        public bool ambush;   // a wandering raider (2026-09-19): it came for the ship out in the belt and goes for good once it gives up
        public int team = 1;  // the battlefield (2026-09-21): 1 a raider (theirs), 0 a wingman (ours); everything is a raider but on The Front
        public float idleT;   // how long an ambush raider has stood idle, for its removal
        // the attack: a run in (weaving), a strafing pass at a radius and direction of its own, a breakaway, then again
        public string move = "run";
        public float moveT, orbitR = 1400f, orbitDir = 1f, weave;
        // the jink: a burst sideways when a bolt is coming, with a cooldown so it is not perfect
        public float dodgeT, dodgeCd;
        public Vector3 dodgeDir;
        // the frustration: time spent close in without the nose on the ship; past its limit (3 to 5 s) the raider gives up
        // the tangle and extends out for room (the user's call, 2026-09-15)
        public float noShotT, noShotLimit = 4f;
        public Vector3 longTo;   // the far point of a long run
        public Vector3 heading = Vector3.forward;   // the way the nose points; the raider only ever moves along it
        public float spd;                           // along the heading
    }

    public class Bolt
    {
        public Vector3 pos, dir;
        public Transform node;
        public float life, dmg;
        public bool player;
        public float speed;   // a raider's BOLT_SPEED, the player's PLAYER_BOLT_SPEED, or the outpost turrets' faster one (2026-09-19)
        public int team = 1;  // whose bolt (2026-09-21): 0 ours (the player's, a wingman's, our battleship's), 1 theirs
        public bool heavy;    // a battleship turret's (2026-09-22): drawn three times the size, in orange-white, so the fire reads at range
        public string by;     // who fired it, for the kill feed (2026-09-22): You, Wingman, Raider, Our turret, Enemy turret
        public Raider shooter;   // the raider that fired it, for the attacker's waypoint (2026-09-23)
        public Vector3 origin;   // where it was fired from, for the same when a turret fired it
        public float lastD;      // its distance from the ship last frame, and whether its pass has sounded (2026-09-23)
        public bool whooshed;
    }

    public Game game;
    public readonly List<Raider> raiders = new List<Raider>();
    public readonly List<Bolt> bolts = new List<Bolt>();
    public int threat;              // raiders attacking right now
    public float nearest = 1e9f;    // the nearest living raider's distance from the ship (for its engine sound)
    public bool nearestBoosting;
    public float nearestBoost = 1e9f;   // the nearest boosting raider's distance (for the afterburner roar)
    public int kills, shotsFired, hitsTaken, passes;   // for the smoke run (passes: bolts heard going past, 2026-09-23)
    public int hitsLanded;
    public int rocketsFired, rocketKills;
    public float hitFlash;    // the hit marker: 1 the frame a player bolt lands, fading over HIT_FLASH seconds
    public bool hitKill;      // ... and whether that hit was the kill
    public const float HIT_FLASH = 0.28f;
    public float danger;
    public bool frozen;   // the combat test: raiders made while this is set hold their place
    public bool respawn;  // the combat test: ten seconds after the last raider dies, a fresh wave is spawned
    public const float WAVE_WAIT = 10f;
    float _waveT;
    public bool holdFire; // testing (F10): raiders fly and chase but never fire
    class Pending { public Vector3 pos, home; public float t; public bool frozen; }
    readonly List<Pending> _pending = new List<Pending>();
    Transform _root;
    Material _hull, _trim, _boltRed, _boltCyan, _boltGlowPlayer, _boltGlowRaider;
    Mesh _boltMesh;
    readonly List<Transform> _boltPool = new List<Transform>();

    // ---- seeker rockets: the player's fire-and-forget weapon. One leaves the dish focus carrying the ship's speed plus a
    // kick, burns up to ROCKET_SPEED and turns onto its raider at ROCKET_TURN a second, far tighter than a raider can
    // fly (TURN_RATE), leading it by its velocity, so it closes on anything within its life; contact (ROCKET_HIT_R, any
    // raider, not only its own) destroys the raider outright, on the spot, with the blast. A rocket that outlives
    // ROCKET_LIFE, or whose raider dies first, flies straight on and pops harmlessly.
    public class Rocket
    {
        public Vector3 pos, heading;
        public float spd, life, smokeT;
        public Raider target;
        public Transform node;
        public bool hostile;   // the outpost's (2026-09-19): it chases the ship, or a flare that has drawn it off
        public Vector3 origin;  // where it was launched from, for the attacker's waypoint (2026-09-23)
        public Flare flare;
    }
    public const float ROCKET_SPEED = 8000f, ROCKET_ACCEL = 6000f, ROCKET_KICK = 1000f, ROCKET_TURN = 70f * Mathf.Deg2Rad, ROCKET_LIFE = 12f, ROCKET_HIT_R = 140f;   // the hit sphere on a raider twice its old size (90 until 2026-09-23, 200 for an hour)
    // ---- hostile rockets and flares (2026-09-19, the user's: the outpost fires seekers, the ship gets a countermeasure)
    public const float HOSTILE_ROCKET_DMG = 22f;     // a hit on the ship: most of a base shield, or nearly half a base hull
    public const float HOSTILE_ROCKET_TURN = 55f * Mathf.Deg2Rad;   // a little slower round than the player's, so a hard turn at the last moment can throw one
    public const float FLARE_LIFE = 6f, FLARE_LURE = 8000f;   // a flare burns 6 s and draws every hostile rocket within 8,000 u (1,000 m) of the ship (3,500 until the 2026-09-21 speeds)
    public const float FLARE_TAKE = 0.85f;           // a rocket falls for a flare this often
    public class Flare { public Vector3 pos, vel; public float life; public Transform node; }
    public readonly List<Flare> flares = new List<Flare>();
    public int rocketsLaunchedAtShip, rocketsDecoyed, rocketsShotDown, rocketHits;   // for the log
    public int HostileRockets { get { int n = 0; foreach (var k in rockets) if (k.hostile) n++; return n; } }
    public readonly List<Rocket> rockets = new List<Rocket>();
    Material _rocketBody, _rocketFlame, _rocketNose;
    readonly List<Transform> _rocketPool = new List<Transform>();

    // the wreckage: a destroyed raider's hull pieces, each flying on with the momentum the raider had plus a shove from
    // the blast, tumbling, kept for DEBRIS_LIFE seconds (true coordinates, like everything here)
    class Debris { public Transform node; public Vector3 pos, vel, axis; public float rate, life; }
    readonly List<Debris> _debris = new List<Debris>();
    public const float DEBRIS_LIFE = 60f;
    const int DEBRIS_MAX = 80;
    bool _warned;
    // ---- ambushes (2026-09-19, the user's: "random raiders that attack you in the belt"): out in any belt but the Hub's,
    // with no fight on and nothing else near, one to three raiders drop in 5,000 to 7,000 u out, behind or beside the
    // ship, already on the attack. The wait between them is AMBUSH_MIN to AMBUSH_MAX seconds of flight, shortened by the
    // zone's danger; only time flying clear of the cargo ship's cover counts
    public const float AMBUSH_MIN = 150f, AMBUSH_MAX = 300f;
    public const float AMBUSH_NEAR = 12000f, AMBUSH_FAR = 16000f;
    float _ambushT = -1f;
    bool _hub, _battle;   // the zone: the Hub has no raiders; a battlefield (2026-09-21) has no holds, no ambushes and no gun cover, its fighters come from the hangars
    Material _trimAlly;
    Material _boltHeavy, _boltGlowHeavy;   // a battleship turret's bolt (2026-09-22)
    public int ambushes;   // sprung so far, for the log

    public Raiders(Game g)
    {
        game = g;
        _root = new GameObject("Raiders").transform;
        _hull = new Material(Game.Sh("Standard"));
        _hull.color = new Color(0.23f, 0.19f, 0.25f);
        _hull.SetFloat("_Metallic", 0.25f);
        _hull.SetFloat("_Glossiness", 0.45f);
        _trim = new Material(Game.Sh("Standard"));
        _trim.color = Data.Hex("#ff2e63");
        _trim.EnableKeyword("_EMISSION");
        _trim.SetColor("_EmissionColor", Data.Hex("#ff2e63") * 1.5f);
        _trimAlly = new Material(Game.Sh("Standard"));   // a wingman's trim (2026-09-21)
        _trimAlly.color = Ui.CYAN;
        _trimAlly.EnableKeyword("_EMISSION");
        _trimAlly.SetColor("_EmissionColor", Ui.CYAN * 1.5f);
        // the raiders' bolts are amber; the player's are a hot red with a glow round each, so they read at range
        _boltRed = new Material(Game.Sh("BeltRunner/Spark"));
        _boltRed.SetColor("_Color", new Color(1f, 0.62f, 0.22f, 0.9f));
        _boltCyan = new Material(Game.Sh("BeltRunner/Spark"));
        _boltCyan.SetColor("_Color", new Color(1f, 0.22f, 0.16f, 1f));
        _boltGlowPlayer = Ship.SoftMaterial(new Color(1f, 0.18f, 0.12f, 0.6f));
        _boltGlowRaider = Ship.SoftMaterial(new Color(1f, 0.6f, 0.2f, 0.4f));
        _boltHeavy = new Material(Game.Sh("BeltRunner/Spark"));   // a battleship turret's bolt (2026-09-22): orange-white, with a big glow
        _boltHeavy.SetColor("_Color", new Color(1f, 0.85f, 0.55f, 1f));
        _boltGlowHeavy = Ship.SoftMaterial(new Color(1f, 0.65f, 0.25f, 0.75f));
        // a rocket: a pale metal body, a hot amber exhaust and a small cyan seeker eye at the nose
        _rocketBody = new Material(Game.Sh("Standard"));
        _rocketBody.color = new Color(0.82f, 0.84f, 0.88f);
        _rocketBody.SetFloat("_Metallic", 0.6f);
        _rocketBody.SetFloat("_Glossiness", 0.5f);
        _rocketFlame = Ship.SoftMaterial(new Color(1f, 0.72f, 0.3f, 0.9f));
        _rocketNose = Ship.SoftMaterial(new Color(0.56f, 0.91f, 1f, 0.9f));
    }

    public void Clear()
    {
        _pending.Clear();
        foreach (var r in raiders) if (r.node != null) Object.Destroy(r.node.gameObject);
        raiders.Clear();
        foreach (var b in bolts) if (b.node != null) b.node.gameObject.SetActive(false);
        bolts.Clear();
        foreach (var k in rockets) if (k.node != null) k.node.gameObject.SetActive(false);
        rockets.Clear();
        foreach (var f in flares) if (f.node != null) Object.Destroy(f.node.gameObject);
        flares.Clear();
        foreach (var h in _hulks) if (h.node != null) Object.Destroy(h.node.gameObject);
        _hulks.Clear();
        foreach (var d in _debris) if (d.node != null) Object.Destroy(d.node.gameObject);
        _debris.Clear();
        threat = 0;
        _warned = false;
        _ambushT = -1f;
    }

    /// Pirate holds by the rich pockets: one hold each, one to three raiders (more in a more dangerous zone).
    public void Build(Data.Zone zone, Belt belt)
    {
        Clear();
        danger = zone.danger;
        _hub = zone.hub;
        _battle = zone.battle;
        if (zone.hub || zone.battle || danger <= 0f) return;
        // a hold at some of the rich pockets (three, plus six per point of danger), never at all of them
        var pockets = new List<Belt.Field>();
        foreach (var f in belt.fields) if (f.pocket) pockets.Add(f);
        for (int i = pockets.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = pockets[i]; pockets[i] = pockets[j]; pockets[j] = t; }
        int holds = Mathf.Min(pockets.Count, Mathf.RoundToInt(3f + danger * 6f));
        for (int h = 0; h < holds; h++)
        {
            var f = pockets[h];
            var dir = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.3f), Random.Range(-1f, 1f)).normalized;
            var home = belt.FieldCentre(f) + dir * f.radius * 1.6f;
            int n = 1 + Random.Range(0, Mathf.Min(3, 1 + Mathf.FloorToInt(danger)));
            for (int i = 0; i < n; i++) Make(home + new Vector3(Random.Range(-300f, 300f), Random.Range(-100f, 100f), Random.Range(-300f, 300f)), home);
        }
    }

    /// makePirate: a cone hull with swept wings, red trim tips, an eye and an exhaust glow (primitives, as the browser's).
    public Raider Make(Vector3 pos, Vector3 home, int team = 1)
    {
        var trim = team == 0 ? _trimAlly : _trim;   // a wingman (2026-09-21) wears cyan where a raider wears red
        var go = new GameObject(team == 0 ? "Wingman" : "Raider");
        go.transform.SetParent(_root, false);
        go.transform.localScale = Vector3.one * 4f;   // four times the size it was drawn at: twice the raider of this morning (2026-09-23, the user asked, after an hour at eight; twice until then)
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(8f, 13f, 8f);
        body.GetComponent<MeshRenderer>().sharedMaterial = _hull;
        var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(wing.GetComponent<Collider>());
        wing.transform.SetParent(go.transform, false);
        wing.transform.localPosition = new Vector3(0f, -0.5f, -4f);
        wing.transform.localScale = new Vector3(30f, 1f, 10f);
        wing.GetComponent<MeshRenderer>().sharedMaterial = _hull;
        foreach (var sx in new[] { -1f, 1f })
        {
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(sx * 15f, 0f, -10f);
            tip.transform.localScale = new Vector3(0.6f, 0.6f, 5f);
            tip.GetComponent<MeshRenderer>().sharedMaterial = trim;
        }
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        Object.Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(go.transform, false);
        eye.transform.localPosition = new Vector3(0f, 2.5f, 4f);
        eye.transform.localScale = Vector3.one * 2.8f;
        eye.GetComponent<MeshRenderer>().sharedMaterial = trim;
        var ex = Ship.SoftMaterial(new Color(1f, 0.18f, 0.39f, 0.6f));
        var exNode = Ship.GlowQuad(go.transform, new Vector3(0f, 0f, -16f), 9f, ex, "Exhaust");
        float hp = Data.UPGRADES["hull"].levels[0].hp;   // the player's base hull, 50 (2026-09-23, one for one; scaled with the zone's danger before)
        float shield = Data.SHIELD_MAX;                // and the player's base shield, 50
        raiders.Add(new Raider
        {
            pos = pos, home = home, wp = home, node = go.transform, exhaust = ex, exhaustNode = exNode, hp = hp, maxHp = hp, shield = shield, maxShield = shield, dmg = Mathf.Round(3f + 4f * danger),
            heading = Random.onUnitSphere, spd = 500f,
            gunDmg = RAIDER_DMG, gunRate = Data.UPGRADES["gun"].levels[0].rate, gunReach = Data.UPGRADES["gun"].levels[0].reach,   // the player's own base autocannon
            a = Random.value * 6f, fireCd = Random.Range(1f, 2f), speed = Data.UPGRADES["engine"].levels[0].max, noShotLimit = Random.Range(3f, 5f), bounty = team == 0 ? 0f : Mathf.Round(120f * danger + 80f), frozen = frozen, team = team,
        });
        return raiders[raiders.Count - 1];
    }

    public bool AnyAttacking
    {
        get { foreach (var r in raiders) if (r.state == "attack") return true; return false; }
    }

    /// The raiders stand down (a breach: they have what they came for).
    public void StandDown()
    {
        foreach (var r in raiders) { r.state = "idle"; r.wp = r.home; }
    }

    /// The ambush clock runs while the ship flies clear of the cargo ship's cover with no fight on. When it runs out and
    /// nothing hostile is near (no raider within a give-up radius, the outpost not engaged), one to three raiders drop in
    /// behind or beside the ship, on the attack from the first frame, with the alarm.
    void TickAmbush(float dt, Vector3 sp, Ship ship)
    {
        if (_hub || _battle) return;
        float scale = 1f / (1f + danger);
        if (_ambushT < 0f) _ambushT = Random.Range(AMBUSH_MIN, AMBUSH_MAX) * scale;
        if (AnyAttacking || (game.outpost != null && game.outpost.engaged)) return;
        _ambushT -= dt;
        if (_ambushT > 0f) return;
        foreach (var r in raiders) if (!r.dead && (r.pos - sp).magnitude < GIVE_UP) { _ambushT = 20f; return; }   // a hold is close: try again shortly
        _ambushT = Random.Range(AMBUSH_MIN, AMBUSH_MAX) * scale;
        int n = 1 + Random.Range(0, 2 + Mathf.FloorToInt(danger * 2f));   // 1 or 2 in a quiet zone, 1 to 3 at Kessler
        var fwd = ship.Forward;
        var dir = Random.onUnitSphere;
        if (Vector3.Dot(dir, fwd) > 0.3f) dir = -dir;   // from behind or the side, never from straight ahead
        var home = sp + dir * Random.Range(AMBUSH_NEAR, AMBUSH_FAR);
        for (int i = 0; i < n; i++)
        {
            var r = Make(home + Random.insideUnitSphere * 300f, home);
            r.ambush = true;
            r.state = "attack";
            r.heading = -dir;
            r.spd = 400f;
            r.fireCd = Random.Range(1.5f, 2.5f);
        }
        ambushes++;
        game.Toast(n == 1 ? "A raider is on you · it came out of the belt" : n + " raiders are on you · they came out of the belt", true);
        Audio.EnemyAlert();
        _warned = true;
    }

    /// An ambush raider that has given up (the ship got away, docked or was disabled) is not left drifting in the belt: it
    /// goes once it has stood idle for a while or the ship is well away.
    void DropAmbusher(int i)
    {
        var r = raiders[i];
        r.dead = true;   // a lock on it lapses
        if (r.node != null) Object.Destroy(r.node.gameObject);
        raiders.RemoveAt(i);
    }

    /// The nearest live raider within `maxDist` of `origin` whose bearing is within the cone (cosine) of `dir`.
    public Raider NearestInCone(Vector3 origin, Vector3 dir, float maxDist, float cosAng)
    {
        Raider best = null;
        float bd = float.PositiveInfinity;
        foreach (var r in raiders)
        {
            if (r.team == 0) continue;   // a wingman is never the player's mark (2026-09-21)
            var to = r.pos - origin;
            float d = to.magnitude;
            if (d < 1f || d > maxDist || d >= bd) continue;
            float c = Vector3.Dot(to / d, dir);
            float allow = Mathf.Max(cosAng, Mathf.Cos(Mathf.Atan2(RADIUS * 1.5f, d)));   // the disc itself counts up close
            if (c < Mathf.Min(cosAng, allow)) continue;
            bd = d;
            best = r;
        }
        return best;
    }

    /// A raider hit by the player: bounty and salvage on a kill.
    public void Damage(Raider r, float dmg, Vector3 at, string by = "You")
    {
        if (r.dead) return;
        r.sinceHit = 0f;
        float toShield = Mathf.Min(r.shield, dmg);
        r.shield -= toShield;
        r.hp -= dmg - toShield;
        if (game.sparks != null) game.sparks.Burst(at, 12, 180f, Data.Hex("#ff8a3a"), 1f);
        if (r.hp <= 0f) Kill(r, by == "You", false, by);   // only the player's own kill pays (2026-09-22: a wingman's, or a raider's on a wingman, used to)
    }

    void Kill(Raider r, bool byPlayer, bool blast = false, string by = null)
    {
        r.dead = true;
        raiders.Remove(r);
        if (game.battle != null) game.battle.OnKill(r, by ?? (byPlayer ? "You" : null));   // the field's meter (2026-09-23)
        if (game.hud != null) game.hud.Feed((by ?? (byPlayer ? "You" : "The cargo ship's guns")) + " destroyed " + (r.team == 0 ? "a wingman" : "a raider") + (byPlayer && r.bounty > 0f ? " · +" + Data.Fmt(r.bounty) + " cr" : ""), r.team == 0);   // the kill feed (2026-09-22)
        if (r.node != null)
        {
            if (blast || Random.value < BLAST_CHANCE) Explode(r.node, r.pos, r.vel);
            else StartWreck(r);   // one of five slower ends: burn, chain, runaway, shed or dead
        }
        if (byPlayer)
        {
            kills++;
            State.credits += r.bounty;
            State.earned += r.bounty;
            // the loot: one to three lumps of outer-belt ore thrown from the wreck, drifting on with its way, to be flown
            // through and collected like any ore (a quarter of the time one of them is a big one)
            int lumps = 0;
            var keys = new List<string>(game.zone.belts[2].Keys);
            if (keys.Count > 0)
            {
                int n = Random.Range(1, 4);
                for (int i = 0; i < n; i++)
                {
                    string k = keys[Random.Range(0, keys.Count)];
                    float u = i == 0 && Random.value < 0.25f ? Random.Range(40f, 60f) : Random.Range(10f, 30f);
                    var at = r.pos - game.worldOffset + Random.insideUnitSphere * 16f;   // pickups live in scene coordinates
                    var drift = r.vel * 0.5f + Random.onUnitSphere * Random.Range(20f, 60f);
                    game.SpawnPickup(k, Mathf.Round(u), at, drift);
                    lumps++;
                }
            }
            game.Toast("Raider destroyed · +" + Data.Fmt(r.bounty) + " cr bounty" + (lumps > 0 ? " · " + lumps + " lump" + (lumps > 1 ? "s" : "") + " of salvage adrift" : ""), false);
        }
        else game.Toast("Cargo ship guns downed a raider", false);
    }

    /// A bolt: a root (pointed along the flight), a core cylinder under it and a glow quad that faces the camera.
    /// 26 long, 1.6 across, with a 14 u glow; the player's and the raiders' look the same.
    Transform BoltNode(bool player)
    {
        Transform t = null;
        foreach (var p in _boltPool) if (!p.gameObject.activeSelf) { t = p; break; }
        if (t == null)
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(_root, false);
            var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(core.GetComponent<Collider>());
            core.name = "Core";
            core.transform.SetParent(go.transform, false);
            core.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Ship.GlowQuad(go.transform, Vector3.zero, 1f, _boltGlowPlayer, "Glow");
            t = go.transform;
            _boltPool.Add(t);
        }
        t.gameObject.SetActive(true);
        // the same bolt for everyone: a hot red core with a glow
        var c = t.GetChild(0);
        c.localScale = new Vector3(3.2f, 13f, 3.2f);
        c.GetComponent<MeshRenderer>().sharedMaterial = _boltCyan;
        var g = t.GetChild(1);
        g.localScale = Vector3.one * 14f;
        g.GetComponent<MeshRenderer>().sharedMaterial = _boltGlowPlayer;
        return t;
    }

    public void Fire(Vector3 from, Vector3 dir, float dmg, bool player, float speed = 0f, int team = -1, bool heavy = false)
    {
        if (speed <= 0f) speed = player ? PLAYER_BOLT_SPEED : BOLT_SPEED;
        if (team < 0) team = player ? 0 : 1;
        string by = player ? "You" : heavy ? (team == 0 ? "Our turret" : "Enemy turret") : (team == 0 ? "Wingman" : "Raider");
        var b = new Bolt { pos = from, dir = dir.normalized, life = player ? PLAYER_BOLT_LIFE : RAIDER_BOLT_LIFE, dmg = dmg, player = player, speed = speed, team = team, by = by, origin = from, node = BoltNode(player || team == 0) };
        b.node.rotation = Quaternion.FromToRotation(Vector3.up, b.dir);
        bolts.Add(b);
        if (heavy)
        {
            // a battleship turret's bolt (2026-09-22, the user's: "make battleship turret fire more visible"): a thicker, longer core in orange-white with a bigger glow; BoltNode resets the node when it is reused
            b.heavy = true;
            b.life = HEAVY_BOLT_LIFE;
            var hc = b.node.GetChild(0); hc.localScale = new Vector3(16f, 90f, 16f); hc.GetComponent<MeshRenderer>().sharedMaterial = _boltHeavy;   // 9 × 34 until 2026-09-23: "bigger and longer"
            var hg = b.node.GetChild(1); hg.localScale = Vector3.one * 84f; hg.GetComponent<MeshRenderer>().sharedMaterial = _boltGlowHeavy;
        }
        if (player)
        {
            shotsFired++;
            // a raider that sees the bolt coming its way jinks aside, most of the time, once its cooldown allows
            foreach (var r in raiders)
            {
                if (r.frozen || r.dodgeCd > 0f) continue;
                var to = r.pos - from;
                float along = Vector3.Dot(to, b.dir);
                if (along < 0f || along > 8000f) continue;
                float miss = (to - b.dir * along).magnitude;
                if (miss > Raiders.RADIUS + 110f) continue;
                if (Random.value > 0.7f) { r.dodgeCd = 0.8f; continue; }   // caught flat-footed this time
                var perp = Vector3.Cross(b.dir, Vector3.up);
                if (perp.sqrMagnitude < 1e-4f) perp = Vector3.Cross(b.dir, Vector3.right);
                perp.Normalize();
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var up = Vector3.Cross(b.dir, perp).normalized;
                r.dodgeDir = (perp * Mathf.Cos(ang) + up * Mathf.Sin(ang)).normalized;
                r.dodgeT = 0.8f;
                r.dodgeCd = 1.6f;
            }
        }
    }

    /// The raider a seeker goes after: the locked one if it is within reach, else the nearest within 30 degrees of the
    /// nose, else the nearest in reach at all. Null when nothing is in reach.
    public Raider Acquire(Vector3 origin, Vector3 dir, float reach, Raider locked)
    {
        if (locked != null && !locked.dead && (locked.pos - origin).magnitude <= reach) return locked;
        var best = NearestInCone(origin, dir, reach, Mathf.Cos(30f * Mathf.Deg2Rad));
        if (best != null) return best;
        float bd = reach;
        foreach (var r in raiders) { float d = (r.pos - origin).magnitude; if (d < bd) { bd = d; best = r; } }
        return best;
    }

    /// A rocket: a root pointed along the flight, the body cylinder (14 u long), the exhaust glow behind and the seeker eye at the nose.
    Transform RocketNode()
    {
        Transform t = null;
        foreach (var p in _rocketPool) if (!p.gameObject.activeSelf) { t = p; break; }
        if (t == null)
        {
            var go = new GameObject("Rocket");
            go.transform.SetParent(_root, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(2.2f, 7f, 2.2f);
            var mr = body.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _rocketBody;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Ship.GlowQuad(go.transform, new Vector3(0f, -9f, 0f), 12f, _rocketFlame, "Flame");
            Ship.GlowQuad(go.transform, new Vector3(0f, 7.5f, 0f), 4f, _rocketNose, "Eye");
            t = go.transform;
            _rocketPool.Add(t);
        }
        t.gameObject.SetActive(true);
        return t;
    }

    /// A seeker away from a point along a direction, carrying the ship's velocity, after a raider (null: it flies straight and pops).
    public Rocket Launch(Vector3 from, Vector3 dir, Vector3 vel, Raider target)
    {
        var d = dir.normalized;
        var k = new Rocket { pos = from, heading = d, spd = Mathf.Max(0f, Vector3.Dot(vel, d)) + ROCKET_KICK, life = ROCKET_LIFE, target = target, node = RocketNode() };
        k.node.position = from - game.worldOffset;
        k.node.rotation = Quaternion.FromToRotation(Vector3.up, d);
        rockets.Add(k);
        rocketsFired++;
        if (game.sparks != null) game.sparks.Burst(from, 10, 120f, new Color(1f, 0.75f, 0.35f), 0.6f);
        return k;
    }

    /// The outpost's seeker (2026-09-19): launched at the ship, it chases the ship (or a flare that draws it off) and
    /// hits for HOSTILE_ROCKET_DMG. A toast and the launch sound warn the pilot; the hint bar reads ROCKET INBOUND.
    public Rocket LaunchHostile(Vector3 from, Vector3 dir)
    {
        var d = dir.normalized;
        var k = new Rocket { pos = from, heading = d, spd = 1000f, life = ROCKET_LIFE, hostile = true, origin = from, node = RocketNode() };
        k.node.position = from - game.worldOffset;
        k.node.rotation = Quaternion.FromToRotation(Vector3.up, d);
        rockets.Add(k);
        rocketsLaunchedAtShip++;
        if (game.sparks != null) game.sparks.Burst(from, 12, 140f, new Color(1f, 0.55f, 0.3f), 0.7f);
        float bd = (from - game.ship.TruePos).magnitude;
        Audio.Play("rocket_launch", Mathf.Max(-24f, (bd <= 800f ? 0f : -16f * Mathf.Log10(bd / 800f))));
        game.Toast("Seeker rocket launched at you · B drops a flare", true);
        return k;
    }

    /// A flare (the ship's B): a burning decoy that drifts on with the way it was dropped at, for FLARE_LIFE. Every hostile
    /// rocket within FLARE_LURE of the ship goes for it instead, most of the time. Returns how many were drawn off.
    public int LaunchFlare(Vector3 from, Vector3 vel)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "Flare";
        go.transform.SetParent(_root, false);
        var m = new Material(Game.Sh("BeltRunner/Spark"));
        m.SetColor("_Color", new Color(1f, 0.85f, 0.55f, 1f));
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = Vector3.one * 26f;
        var f = new Flare { pos = from, vel = vel, life = FLARE_LIFE, node = go.transform };
        go.transform.position = from - game.worldOffset;
        flares.Add(f);
        if (game.sparks != null) game.sparks.Burst(from, 60, 160f, new Color(1f, 0.85f, 0.5f), 1.2f, vel);
        int lured = 0;
        var sp = game.ship.TruePos;
        foreach (var k in rockets)
        {
            if (!k.hostile || k.flare != null) continue;
            if ((k.pos - sp).magnitude > FLARE_LURE) continue;
            if (Random.value > FLARE_TAKE) continue;
            k.flare = f;
            lured++;
            rocketsDecoyed++;
        }
        return lured;
    }

    void TickFlares(float dt, Vector3 off)
    {
        for (int i = flares.Count - 1; i >= 0; i--)
        {
            var f = flares[i];
            f.life -= dt;
            if (f.life <= 0f)
            {
                if (f.node != null) Object.Destroy(f.node.gameObject);
                flares.RemoveAt(i);
                continue;
            }
            f.vel *= Mathf.Exp(-0.25f * dt);
            f.pos += f.vel * dt;
            f.node.position = f.pos - off;
            float k = Mathf.Clamp01(f.life / 1.5f);   // it gutters out over its last second and a half
            f.node.localScale = Vector3.one * (18f + 12f * Random.value) * k;
            if (game.sparks != null && Random.value < dt * 30f) game.sparks.Burst(f.pos, 3, 60f, new Color(1f, 0.8f, 0.45f), 0.8f, f.vel);
        }
    }

    void TickRockets(float dt, Vector3 off)
    {
        for (int i = rockets.Count - 1; i >= 0; i--)
        {
            var k = rockets[i];
            k.life -= dt;
            if (k.target != null && (k.target.dead || !raiders.Contains(k.target))) k.target = null;
            if (k.flare != null && k.flare.life <= 0f) k.flare = null;
            if (k.hostile)
            {
                // the outpost's seeker: onto the flare that drew it off, else onto the ship with a lead; it turns a little
                // slower than the player's, so a hard turn at the last moment can still throw one
                var ship = game.ship;
                var goal = k.flare != null ? k.flare.pos : ship.TruePos;
                var gv = k.flare != null ? k.flare.vel : ship.vel;
                var to = goal - k.pos;
                float eta = Mathf.Min(2f, to.magnitude / Mathf.Max(200f, k.spd));
                k.heading = RotateTowards(k.heading, to + gv * eta, HOSTILE_ROCKET_TURN * dt);
            }
            else if (k.target != null)
            {
                // the lead: aim where the raider will be when the rocket gets there (capped at two seconds out), then turn onto it
                var to = k.target.pos - k.pos;
                float eta = Mathf.Min(2f, to.magnitude / Mathf.Max(200f, k.spd));
                k.heading = RotateTowards(k.heading, to + k.target.vel * eta, ROCKET_TURN * dt);
            }
            k.spd = Mathf.Min(ROCKET_SPEED, k.spd + ROCKET_ACCEL * dt);
            var prev = k.pos;
            k.pos += k.heading * k.spd * dt;
            k.node.position = k.pos - off;
            k.node.rotation = Quaternion.FromToRotation(Vector3.up, k.heading);
            // nothing is shot through (2026-09-23): a rocket that crossed a rock or a hull this frame goes off there
            Vector3 blockAt;
            if (Solids.Blocked(prev, k.pos, out blockAt))
            {
                k.pos = blockAt;
                if (game.explosions != null) game.explosions.Pop(k.pos, Vector3.zero);
                if (game.sparks != null) game.sparks.Burst(k.pos, 40, 200f, new Color(1f, 0.7f, 0.35f), 1.2f);
                float bd0 = (k.pos - game.ship.TruePos).magnitude;
                Audio.Play("hit", Mathf.Max(-24f, -2f + (bd0 <= 300f ? 0f : -20f * Mathf.Log10(bd0 / 300f))));
                k.node.gameObject.SetActive(false);
                rockets.RemoveAt(i);
                continue;
            }
            // the exhaust trail: a puff of smoke every few metres of flight, left behind and drifting
            k.smokeT -= dt;
            if (k.smokeT <= 0f && game.explosions != null) { k.smokeT = 0.07f; game.explosions.Smoke(k.pos - k.heading * 9f, k.heading * -30f + Random.insideUnitSphere * 8f); }
            bool hit = false;
            if (k.hostile)
            {
                // contact: the flare it is chasing (a harmless pop), else the ship (the hit, the blast, the damage)
                var ab = k.pos - prev;
                if (k.flare != null)
                {
                    float tf = Mathf.Clamp01(Vector3.Dot(k.flare.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if ((prev + ab * tf - k.flare.pos).magnitude < ROCKET_HIT_R * 1.5f)
                    {
                        if (game.explosions != null) game.explosions.Pop(k.pos, k.heading * k.spd * 0.2f);
                        if (game.sparks != null) game.sparks.Burst(k.pos, 40, 200f, new Color(1f, 0.7f, 0.35f), 1.2f);
                        float fd = (k.pos - game.ship.TruePos).magnitude;
                        Audio.Play("hit", Mathf.Max(-24f, -2f + (fd <= 300f ? 0f : -20f * Mathf.Log10(fd / 300f))));
                        hit = true;
                    }
                }
                else
                {
                    var sp2 = game.ship.TruePos;
                    float ts = Mathf.Clamp01(Vector3.Dot(sp2 - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if (game.ship.CanFly && !game.ship.docked && (prev + ab * ts - sp2).magnitude < SHIP_HIT_R)
                    {
                        rocketHits++;
                        hitsTaken++;
                        if (game.sparks != null) game.sparks.Burst(k.pos, 120, 320f, new Color(1f, 0.6f, 0.25f), 1.6f, game.ship.vel);
                        if (game.explosions != null) game.explosions.Pop(k.pos, game.ship.vel);
                        Audio.Play("hit", 4f);
                        game.ship.Hurt(HOSTILE_ROCKET_DMG, k.pos, "Rocket hit", null, k.origin, "Rocket battery");
                        hit = true;
                    }
                }
                if (hit || k.life <= 0f)
                {
                    if (!hit && game.explosions != null) game.explosions.Pop(k.pos, k.heading * k.spd * 0.2f);
                    k.node.gameObject.SetActive(false);
                    rockets.RemoveAt(i);
                }
                continue;
            }
            // contact: the segment flown this frame against every raider's hit sphere
            foreach (var r in raiders)
            {
                var ab = k.pos - prev;
                float t = Mathf.Clamp01(Vector3.Dot(r.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((prev + ab * t - r.pos).magnitude < ROCKET_HIT_R)
                {
                    rocketKills++;
                    hitsLanded++;
                    hitFlash = 1f;
                    hitKill = true;
                    Audio.Sure("kill_marker");
                    r.sinceHit = 0f;
                    r.shield = 0f;
                    r.hp = 0f;
                    Kill(r, true, true, "Your rocket");   // destroyed outright, on the spot
                    hit = true;
                    break;
                }
            }
            if (hit || k.life <= 0f)
            {
                if (!hit)
                {
                    // spent: a small pop where it dies, heard with distance
                    if (game.explosions != null) game.explosions.Pop(k.pos, k.heading * k.spd * 0.2f);
                    float bd = (k.pos - game.ship.TruePos).magnitude;
                    Audio.Play("hit", Mathf.Max(-30f, -8f + (bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f))));
                }
                k.node.gameObject.SetActive(false);
                rockets.RemoveAt(i);
            }
        }
    }

    public void Tick(float dt)
    {
        var ship = game.ship;
        // the combat test: ten seconds after the last raider dies, a fresh wave
        if (respawn && raiders.Count == 0)
        {
            _waveT += dt;
            if (_waveT >= WAVE_WAIT) { _waveT = 0f; game.JumpToHold(); }
        }
        else _waveT = 0f;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            p.t -= dt;
            if (p.t > 0f) continue;
            _pending.RemoveAt(i);
            Make(p.pos, p.home).frozen = p.frozen;
            game.Toast("Test · raider respawned", false);
        }
        var carrier = game.carrier;
        var off = game.worldOffset;
        TickDebris(dt, off);
        TickHulks(dt, off);
        TickRockets(dt, off);
        TickFlares(dt, off);
        var sp = ship.TruePos;
        bool flying = game.started && !ship.docked && ship.warp == null && ship.cut == null;
        bool nearDepot = !_battle && carrier != null && !carrier.hold && (sp - carrier.truePos).magnitude < SAFE_R;   // no gun cover on the battlefield: our turrets are real there
        bool canAttack = flying && ship.CanFly && !nearDepot;
        if (canAttack) TickAmbush(dt, sp, ship);
        threat = 0;
        nearest = 1e9f;
        nearestBoosting = false;
        nearestBoost = 1e9f;
        for (int i = raiders.Count - 1; i >= 0; i--)
        {
            var r = raiders[i];
            float d = (r.pos - sp).magnitude;
            float dShip = d;   // the raider to you, for what is heard: on the battlefield d becomes the raider to its mark below (2026-09-23)
            if (d < nearest) { nearest = d; nearestBoosting = r.boosting; }
            if (r.boosting && d < nearestBoost) nearestBoost = d;
            r.sinceHit += dt;
            if (r.sinceHit >= Data.SHIELD_WAIT && r.shield < r.maxShield) r.shield = Mathf.Min(r.maxShield, r.shield + r.maxShield / 3f * dt);   // the same wait as the ship, 5 s (2026-09-23; its own 10 s before)
            if (r.state == "idle" && canAttack && d < ENGAGE)
            {
                r.state = "attack";
                if (threat == 0 && !_warned) { game.Toast("Pirate raiders inbound", true); Audio.EnemyAlert(); _warned = true; }
            }
            if (r.state == "attack" && (!canAttack || d > GIVE_UP)) { r.state = "idle"; r.wp = r.home; }
            if (r.ambush && !r.dead)
            {
                // an ambusher never idles for long: gone once the ship is well away or it has waited half a minute
                r.idleT = r.state == "idle" ? r.idleT + dt : 0f;
                if (r.state == "idle" && (d > GIVE_UP * 1.5f || r.idleT > 30f)) { DropAmbusher(i); continue; }
            }
            // the mark (2026-09-21): the ship, unless this is the battlefield, where the field names it (the player, a fighter
            // of the other side or an exposed subsystem of the other battleship); a wingman outside a battle only idles
            Vector3 tp = sp, tv = ship.vel;
            bool battle = game.battle != null && game.battle.Active;
            if (battle)
            {
                bool any;
                tp = game.battle.PickTarget(r, out tv, out any);
                r.state = any ? "attack" : "idle";
                if (!any) r.wp = r.home;
                d = (r.pos - tp).magnitude;
            }
            else if (r.team == 0) r.state = "idle";
            Vector3 desired;
            if (r.state == "attack")
            {
                if (r.team == 1) threat++;
                var toShip = d > 1f ? (tp - r.pos) / d : Vector3.forward;
                var side = Vector3.Cross(toShip, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(toShip, Vector3.right);
                side.Normalize();
                r.moveT -= dt;
                r.weave += dt * 1.7f;
                if (r.move == "run")
                {
                    // the run in: toward the ship, weaving side to side (less as it closes, so the guns line up), until close
                    float wv = Mathf.Clamp01(d / 5000f);
                    desired = tp + side * Mathf.Sin(r.weave) * 260f * wv + Vector3.up * Mathf.Sin(r.weave * 0.6f) * 90f * wv;
                    if (d < 2000f)
                    {
                        // the pass (2026-09-15, the user's call: no circling, no following): straight on past the ship, offset
                        // to one side, guns going while the nose is on it, then out to get room for the next run
                        r.move = "strafe";
                        r.orbitDir = Random.value < 0.5f ? -1f : 1f;
                        r.longTo = tp + toShip * 6000f + side * r.orbitDir * 800f + Vector3.up * Random.Range(-400f, 400f);
                        r.moveT = 1.8f;
                    }
                }
                else if (r.move == "strafe")
                {
                    // the pass: through and past, no turning back toward the ship
                    desired = r.longTo;
                    bool past = Vector3.Dot(tp - r.pos, r.heading) < 0f;   // the ship is behind the nose now
                    if (r.moveT <= 0f || (past && d > 350f))
                    {
                        if (Random.value < 0.85f)
                        {
                            // the extension: full thrust out to a point 313 to 625 m from the ship to get room, then a fresh
                            // run back in, guns going as soon as the nose is on you
                            r.move = "long";
                            var away = -toShip + side * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(-0.3f, 0.3f);
                            float reach = Random.Range(5000f, 9000f);
                            r.longTo = tp + away.normalized * reach;
                            r.moveT = reach / r.speed * 1.6f;
                        }
                        else
                        {
                            r.move = "break";   // now and then a short breakaway instead, for a quicker second pass
                            r.moveT = Random.Range(1.2f, 2.5f);
                        }
                    }
                }
                else if (r.move == "long")
                {
                    // the long run, weaving a little; over when the point is reached or the time is up
                    desired = r.longTo + side * Mathf.Sin(r.weave) * 500f;
                    if ((r.longTo - r.pos).magnitude < 1200f || r.moveT <= 0f) { r.move = "run"; r.moveT = 0f; }
                }
                else
                {
                    // the breakaway: out to 1,600 u off to one side, then a fresh run
                    desired = tp - toShip * 3000f + side * r.orbitDir * 1500f + Vector3.up * Mathf.Sin(r.weave * 0.5f) * 600f;
                    if (r.moveT <= 0f || d > 4000f) { r.move = "run"; r.moveT = 0f; }
                }
                // the jink: a hard turn aside, as hard as the ship can turn
                r.dodgeCd -= dt;
                if (r.dodgeT > 0f) { r.dodgeT -= dt; desired = r.pos + r.dodgeDir * 3000f; }
                r.fireCd -= dt;
                // the guns are fixed forward: a raider only fires when its nose is on the ship (within 20 degrees)
                bool facing = Vector3.Dot(r.node.forward, (tp - r.pos).normalized) > Mathf.Cos(40f * Mathf.Deg2Rad);   // 25 degrees until 2026-09-22: at 2,000 u/s the passes were too brief to shoot
                // close in and unable to bring the guns to bear for 3 to 5 s: stop tangling and go out for room
                if (facing || d > 5000f || r.move == "long") r.noShotT = 0f;
                else
                {
                    r.noShotT += dt;
                    if (r.noShotT > r.noShotLimit)
                    {
                        r.move = "long";
                        var away = -toShip + side * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(-0.3f, 0.3f);
                        float reach = Random.Range(5000f, 9000f);
                        r.longTo = tp + away.normalized * reach;
                        r.moveT = reach / r.speed * 1.6f;
                        r.noShotT = 0f;
                        r.noShotLimit = Random.Range(3f, 5f);
                    }
                }
                if (r.fireCd <= 0f && d < r.gunReach && facing && !holdFire && !Solids.Blocked(r.pos, tp, out _))   // the player's own gun: its range, its rate, its damage; never through a rock or a hull (2026-09-23)
                {
                    r.fireCd = 1f / r.gunRate;
                    // a rough gunner: the lead is over- or under-estimated shot by shot, and the spread is wide (about 5 degrees)
                    float spread = 0.02f;   // about 1.1 degrees (0.09, about 5, until 2026-09-22, then 0.04 for a day: at the new ranges nothing landed)
                    float leadErr = Random.Range(0.9f, 1.1f);   // a good gunner (0.55 to 1.15 until 2026-09-22)
                    var dir = (tp + tv * (d / BOLT_SPEED) * leadErr - r.pos).normalized + new Vector3(Random.Range(-spread, spread), Random.Range(-spread, spread), Random.Range(-spread, spread));
                    Fire(r.pos, dir.normalized, r.gunDmg, false, 0f, r.team);   // its own side's bolt: a wingman's was tagged as a raider's and hit the player (fixed 2026-09-22)
                    bolts[bolts.Count - 1].shooter = r;
                    // the same blaster as the player's, quieter with distance: 6 dB a doubling beyond 300 u, silent past 30 dB down
                    float att = Audio.Falloff(dShip, 300f);   // by its distance from you, not from its mark (2026-09-23: a wingman's shot at a raider far from you played at full); 8 dB a doubling past 300 u, gone past 2,850
                    if (att > Audio.SILENT) Audio.Shot("blaster", att);
                }
            }
            else
            {
                if ((r.pos - r.wp).magnitude < 80f) r.wp = r.home + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.3f), Random.Range(-1f, 1f)) * 1200f;
                desired = r.wp;
            }
            var want = desired - r.pos;
            float dist = want.magnitude;
            if (dist > 1f) want /= dist;
            if (r.frozen)
            {
                SetBoost(r, false, dShip);
                r.vel = Vector3.zero;
                r.spd = 0f;
                if (r.state == "attack" && d > 1f) r.heading = RotateTowards(r.heading, (tp - r.pos) / d, TURN_RATE * dt);
                r.node.rotation = Ship.LevelHeading(r.heading);
            }
            else
            {
                // the flight model: the nose turns toward where it wants to go no faster than a ship could, the throttle
                // works against thrust and braking limits, and the raider only ever moves along its nose
                // nothing is flown through (2026-09-23, the user's): the rocks about it, looked up twice a second, and the
                // steer off whatever lies ahead along the nose, harder the nearer; the throttle back when it is right there
                if (Time.time - r.rocksT > 0.5f || (r.pos - r.rocksAt).sqrMagnitude > 1500f * 1500f) { r.rocksT = Time.time; r.rocksAt = r.pos; r.nearRocks = game.belt != null ? game.belt.RocksNear(r.pos, 4500f) : null; }
                var avoid = Solids.Avoid(r.pos, r.heading, RAIDER_R, Mathf.Clamp(r.spd * 1.5f, 600f, 4000f), r.nearRocks);
                float avoidK = avoid.magnitude;
                if (avoidK > 0.01f) want = (want + avoid * 2f).normalized;
                r.heading = RotateTowards(r.heading, want, TURN_RATE * dt);
                float wantSpd = r.state == "attack" ? r.speed : 1000f;
                if (r.state == "attack" && r.move == "strafe") wantSpd = Mathf.Min(wantSpd, r.orbitR * TURN_RATE * 0.9f);   // slow enough to hold the circle
                if (r.state != "attack") wantSpd = Mathf.Min(wantSpd, Mathf.Max(60f, dist / 3f));   // ease up on the point (a long run is flown at full thrust)
                float off2 = Vector3.Dot(r.heading, want);
                if (off2 < 0.3f) wantSpd = Mathf.Min(wantSpd, 900f);   // pointing the wrong way: throttle back through the turn
                if (avoidK > 0.6f) wantSpd = Mathf.Min(wantSpd, 700f);   // something close ahead: room to turn off it
                // the boost: on a run in from far out or a long run out, nose on the mark, the throttle goes wide open
                bool boost = r.state == "attack" && off2 > 0.8f && avoidK < 0.3f && (r.move == "long" || (r.move == "run" && d > 3000f));
                if (boost) wantSpd *= BOOST_MULT;
                SetBoost(r, boost, dShip);
                r.spd = r.spd < wantSpd ? Mathf.Min(wantSpd, r.spd + ACCEL * (boost ? 1.6f : 1f) * dt) : Mathf.Max(wantSpd, r.spd - DECEL * dt);
                if (boost && game.explosions != null)
                {
                    // the jet trail: pale puffs streaming off the exhaust
                    r.jetT -= dt;
                    while (r.jetT <= 0f) { r.jetT += 0.035f; game.explosions.Jet(r.pos - r.heading * Random.Range(32f, 52f) + Random.insideUnitSphere * 6f, r.vel - r.heading * 120f); }   // off the stern of the twice-size raider
                }
                r.vel = r.heading * r.spd;
                r.pos += r.vel * dt;
                // ... and out of any rock or hull the move put it in, sliding along it (2026-09-23)
                Vector3 cn;
                if (Solids.Push(ref r.pos, RAIDER_R, ref r.vel, r.nearRocks, out cn))
                {
                    r.spd = r.vel.magnitude;
                    if (r.spd > 1f) r.heading = r.vel / r.spd;
                }
                r.node.rotation = Ship.LevelHeading(r.heading);
            }
            r.node.position = r.pos - off;
            // a wingman's exhaust is blue, a raider's pink, so the sides read at a glance (2026-09-23, the user's)
            float exA = r.state == "attack" ? 0.6f + Random.value * 0.3f : 0.35f;
            if (!r.boosting) r.exhaust.SetColor("_Color", r.team == 0 ? new Color(0.36f, 0.61f, 1f, exA) : new Color(1f, 0.18f, 0.39f, exA));
            if (!_battle && carrier != null && !carrier.hold && (r.pos - carrier.truePos).magnitude < SAFE_R)
            {
                r.hp -= 30f * dt;
                if (r.hp <= 0f) Kill(r, false, false, "The cargo ship's guns");
            }
        }
        if (threat == 0) _warned = false;
        if (hitFlash > 0f) hitFlash = Mathf.Max(0f, hitFlash - dt / HIT_FLASH);
        // the bolts: a raider's hits the ship, the player's hit raiders
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            b.life -= dt;
            float speed = b.speed > 0f ? b.speed : (b.player ? PLAYER_BOLT_SPEED : BOLT_SPEED);
            var prev = b.pos;
            b.pos += b.dir * speed * dt;
            b.node.position = b.pos - off;
            // a bolt going past (2026-09-23, the user's: "the blaster shots need to make a noise as they go past you"): a
            // hostile bolt, or a turret's shell of either side, that comes inside WHOOSH_R of the ship and then starts to
            // draw away sounds its pass once, louder the closer it came, panned to the side it passes on
            if (!b.player && (b.team == 1 || b.heavy) && !b.whooshed)
            {
                float bd = SegDist(prev, b.pos, sp);   // the closest of its flight this frame: at 530 u a frame the end point misses the pass (2026-09-23)
                float wr = b.heavy ? WHOOSH_R_HEAVY : WHOOSH_R;
                if (b.lastD > 0f && bd > b.lastD && b.lastD < wr && flying)
                {
                    b.whooshed = true;
                    passes++;
                    var side = game.cam != null ? game.cam.transform.right : Vector3.right;
                    Audio.Whoosh(1f - b.lastD / wr, Vector3.Dot((prev - sp).normalized, side), b.heavy);
                }
                b.lastD = bd;
            }
            if (b.player)
            {
                foreach (var r in raiders)
                {
                    if (r.team == 0) continue;   // a wingman is not hit by the player's own bolts (2026-09-21)
                    // the segment flown this frame against the raider's disc
                    var ab = b.pos - prev;
                    float t = Mathf.Clamp01(Vector3.Dot(r.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if ((prev + ab * t - r.pos).magnitude < HIT_R)
                    {
                        Damage(r, b.dmg, r.pos, "You");
                        hitsLanded++;
                        hitFlash = 1f;
                        hitKill = r.dead;
                        Audio.Sure(r.dead ? "kill_marker" : "hit_marker");   // the hit marker: a punchy tick, a sting on the kill   // the hit marker: a punchy tick, a sting on the kill
                        b.life = 0f;
                        break;
                    }
                }
                // the raider outpost (2026-09-19): its turrets and its core take the bolt too
                if (b.life > 0f && game.outpost != null)
                {
                    bool killed;
                    if (game.outpost.HitSegment(prev, b.pos, b.dmg, out killed))
                    {
                        hitsLanded++;
                        hitFlash = 1f;
                        hitKill = killed;
                        Audio.Sure(killed ? "kill_marker" : "hit_marker");
                        b.life = 0f;
                    }
                }
                // the enemy battleship (2026-09-21): its exposed subsystems and its shield take the bolt too
                if (b.life > 0f && game.battle != null)
                {
                    bool killed;
                    if (game.battle.HitSegment(0, prev, b.pos, b.dmg, out killed, "You"))
                    {
                        hitsLanded++;
                        hitFlash = 1f;
                        hitKill = killed;
                        Audio.Sure(killed ? "kill_marker" : "hit_marker");
                        b.life = 0f;
                    }
                }
                // a seeker coming at the ship can be shot down (2026-09-19): the segment against each hostile rocket
                if (b.life > 0f)
                {
                    for (int ki = rockets.Count - 1; ki >= 0; ki--)
                    {
                        var k = rockets[ki];
                        if (!k.hostile) continue;
                        var ab = b.pos - prev;
                        float t = Mathf.Clamp01(Vector3.Dot(k.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        if ((prev + ab * t - k.pos).magnitude > 45f) continue;
                        rocketsShotDown++;
                        hitsLanded++;
                        hitFlash = 1f;
                        hitKill = true;
                        Audio.Sure("kill_marker");
                        if (game.explosions != null) game.explosions.Pop(k.pos, k.heading * k.spd * 0.2f);
                        if (game.sparks != null) game.sparks.Burst(k.pos, 50, 220f, new Color(1f, 0.7f, 0.35f), 1.2f);
                        k.node.gameObject.SetActive(false);
                        rockets.RemoveAt(ki);
                        b.life = 0f;
                        break;
                    }
                }
            }
            else
            {
                // a raider's bolt hits the ship; on the battlefield (2026-09-21) a bolt of either side hits the other side's
                // fighters and the other side's battleship too
                if (b.team == 1 && flying && ship.CanFly && SegDist(prev, b.pos, sp) < SHIP_HIT_R)   // the segment flown this frame, not the end point: at 9,000 u/s and up a bolt crosses the hull between frames (2026-09-21)
                {
                    hitsTaken++;
                    ship.Hurt(b.dmg, b.pos, "Raider hit", b.shooter, b.shooter != null ? b.shooter.pos : b.origin, b.by);
                    b.life = 0f;
                }
                if (b.life > 0f && game.battle != null && game.battle.Active)
                {
                    foreach (var r in raiders)
                    {
                        if (r.dead || r.team == b.team) continue;
                        var ab = b.pos - prev;
                        float t = Mathf.Clamp01(Vector3.Dot(r.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        if ((prev + ab * t - r.pos).magnitude < HIT_R) { Damage(r, b.dmg, r.pos, b.by); b.life = 0f; break; }
                    }
                    bool killed;
                    if (b.life > 0f && game.battle.HitSegment(b.team, prev, b.pos, b.dmg, out killed, b.by)) b.life = 0f;
                }
            }
            // nothing is shot through (2026-09-23, the user's): a bolt still flying that crossed a rock or a hull this frame
            // dies there, with a spark of it
            Vector3 blockAt;
            if (b.life > 0f && Solids.Blocked(prev, b.pos, out blockAt))
            {
                if (game.sparks != null) game.sparks.Burst(blockAt, b.heavy ? 30 : 10, b.heavy ? 200f : 120f, b.heavy ? Data.Hex("#ffd090") : Data.Hex("#ff8a60"), b.heavy ? 1.6f : 0.8f);
                b.life = 0f;
            }
            if (b.life <= 0f)
            {
                b.node.gameObject.SetActive(false);
                bolts.RemoveAt(i);
            }
        }
    }

    /// The distance from p to the segment ab.
    static float SegDist(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (a + ab * t - p).magnitude;
    }

    public static readonly float BOOST_MULT = Data.UPGRADES["thrusters"].levels[0].mult;   // the player's own afterburner, ×2 (2026-09-23, one for one; 1.25 before)

    /// The boost on or off: the exhaust flares white-blue at twice the size while it is on (its normal pink glow
    /// otherwise), and a whoosh and roar plays with distance the moment it kicks in.
    void SetBoost(Raider r, bool on, float d)
    {
        if (on == r.boosting) return;
        r.boosting = on;
        if (r.exhaust != null) r.exhaust.SetColor("_Color", on ? new Color(0.75f, 0.88f, 1f, 0.95f) : r.team == 0 ? new Color(0.36f, 0.61f, 1f, 0.6f) : new Color(1f, 0.18f, 0.39f, 0.6f));   // a wingman's blue at rest (2026-09-23)
        if (r.exhaustNode != null) r.exhaustNode.localScale = Vector3.one * (on ? 18f : 9f);
        if (on)
        {
            float att = Audio.Falloff(d, 300f);   // the gunfire falloff (2026-09-23): gone past 2,850 u
            if (att > Audio.SILENT) Audio.Play("raider_boost", att);
        }
    }

    /// Turn a unit vector toward another by at most `maxRad`.
    static Vector3 RotateTowards(Vector3 from, Vector3 to, float maxRad)
    {
        if (to.sqrMagnitude < 1e-6f) return from;
        to.Normalize();
        float c = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
        float ang = Mathf.Acos(c);
        if (ang <= maxRad) return to;
        var axis = Vector3.Cross(from, to);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(from, Mathf.Abs(from.y) < 0.9f ? Vector3.up : Vector3.right);
        return (Quaternion.AngleAxis(maxRad * Mathf.Rad2Deg, axis.normalized) * from).normalized;
    }

    /// The raider comes apart: every hull piece becomes debris carrying the raider's velocity plus a shove outward from
    /// the blast (40 to 120 u/s) and a tumble; the exhaust glow goes out. The oldest debris is dropped past DEBRIS_MAX.
    /// The blast at `pos`: the sound with distance (full inside 600 u, 6 dB a doubling beyond, never below 24 dB down,
    /// so a kill is always heard), the explosion, and the hull coming apart.
    void Explode(Transform node, Vector3 pos, Vector3 vel)
    {
        float bd = (pos - game.ship.TruePos).magnitude;
        Audio.Play("boom", Mathf.Max(-24f, bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f)));
        if (game.explosions != null) game.explosions.Raider(pos, vel);   // the flash, the fireball, the ring, the smoke, the embers
        Shatter(node, pos, vel);
    }

    // ---- the wreck: a killed raider that did not blow up on the spot. Every kind flies on with the momentum it had,
    // out of control, and ends quietly, the hull left in one piece to drift as a dark wreck (only the kill on the spot
    // blows up: BLAST_CHANCE):
    //   burn     catches fire and tumbles, trailing flame, smoke and sparks, for 3.5 to 8 s, then the fire goes out
    //   chain    a run of small pops along the hull over about a second, then it goes dark
    //   runaway  the engine jams open: it flares and the hulk accelerates hard along its nose, corkscrewing, for 1.5 to 3 s, then the engine dies
    //   vent     jets of gas and plasma burst from the hull every 0.35 to 0.9 s, each shoving and spinning it, for 4 to 9 s
    //   dead     everything goes dark; it spins flat and silent for 10 to 18 s with arcs crawling over it, then the arcs stop
    public const float BLAST_CHANCE = 0.3f;   // the rest is split between the kinds below
    class Hulk
    {
        public string kind;
        public Transform node; public Vector3 pos, vel, axis; public float spin, fuse, flameT, smokeT, sparkT, popT, shedT, arcT;
        public Material exhaust; public Transform exhaustNode;
    }
    readonly List<Hulk> _hulks = new List<Hulk>();

    void StartWreck(Raider r)
    {
        float roll = Random.value;
        string kind = roll < 0.28f ? "burn" : roll < 0.5f ? "chain" : roll < 0.68f ? "runaway" : roll < 0.86f ? "vent" : "dead";
        var h = new Hulk
        {
            kind = kind, node = r.node, pos = r.pos, vel = r.vel + Random.insideUnitSphere * 20f, axis = Random.onUnitSphere,
            spin = Random.Range(10f, 30f), exhaust = r.exhaust
        };
        for (int i = 0; i < r.node.childCount; i++) if (r.node.GetChild(i).name == "Exhaust") h.exhaustNode = r.node.GetChild(i);
        switch (kind)
        {
            case "burn": h.fuse = Random.Range(3.5f, 8f); break;
            case "chain": h.fuse = Random.Range(0.8f, 1.3f); h.spin = Random.Range(5f, 15f); break;
            case "runaway": h.fuse = Random.Range(1.5f, 3f); h.spin = Random.Range(30f, 80f); h.axis = (r.node.forward * 0.6f + Random.insideUnitSphere).normalized; break;
            case "vent": h.fuse = Random.Range(4f, 9f); h.shedT = Random.Range(0.2f, 0.5f); h.spin = Random.Range(15f, 40f); break;
            case "dead":
                h.fuse = Random.Range(10f, 18f);
                h.spin = Random.Range(60f, 110f);
                if (h.exhaustNode != null) h.exhaustNode.gameObject.SetActive(false);   // the lights go out
                for (int i = 0; i < r.node.childCount; i++) if (r.node.GetChild(i).name == "Eye") r.node.GetChild(i).gameObject.SetActive(false);
                break;
        }
        _hulks.Add(h);
        float bd = (r.pos - game.ship.TruePos).magnitude;
        Audio.Play("hit", Mathf.Max(-24f, bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f)));   // the hit that did it: a metallic thud
        if (game.sparks != null) game.sparks.Burst(r.pos, 80, 220f, Data.Hex("#ffb060"), 1.4f, r.vel);
    }

    void TickHulks(float dt, Vector3 off)
    {
        for (int i = _hulks.Count - 1; i >= 0; i--)
        {
            var h = _hulks[i];
            h.fuse -= dt;
            if (h.node == null) { _hulks.RemoveAt(i); continue; }
            if (h.fuse <= 0f)
            {
                QuietEnd(h);   // a wreck never blows up and never comes apart: it goes dark and drifts on as one piece
                _hulks.RemoveAt(i);
                continue;
            }
            // the tumble and the drift: every kind is out of control, each in its own way
            switch (h.kind)
            {
                case "burn":
                    h.spin = Mathf.Min(220f, h.spin + 45f * dt);
                    h.axis = (h.axis + Random.insideUnitSphere * 0.4f * dt).normalized;
                    h.vel += Random.insideUnitSphere * 12f * dt;
                    break;
                case "chain":
                    h.spin = Mathf.Min(60f, h.spin + 20f * dt);
                    break;
                case "runaway":
                    // the engine jammed open: full thrust along the nose while the nose wanders, so it corkscrews away
                    h.vel += h.node.forward * 420f * dt;
                    h.spin = Mathf.Min(140f, h.spin + 30f * dt);
                    break;
                case "vent":
                    h.vel += Random.insideUnitSphere * 6f * dt;
                    break;
                case "dead":
                    break;   // a steady flat spin, nothing else
            }
            h.pos += h.vel * dt;
            h.node.position = h.pos - off;
            h.node.Rotate(h.axis, h.spin * dt, Space.World);
            // the exhaust: guttering on the burn, blazing white-blue on the runaway, out on the dead hull
            if (h.exhaust != null)
            {
                if (h.kind == "runaway") { h.exhaust.SetColor("_Color", new Color(0.8f, 0.9f, 1f, 0.95f)); if (h.exhaustNode != null) h.exhaustNode.localScale = Vector3.one * Random.Range(14f, 20f); }
                else if (h.kind != "dead") h.exhaust.SetColor("_Color", new Color(1f, 0.5f, 0.2f, Random.value < 0.5f ? 0.5f : 0.1f));
            }
            if (game.explosions == null) continue;
            switch (h.kind)
            {
                case "burn":
                    // the fire: flame puffs off the hull at a steady rate, smoke a little slower, sparks now and then
                    h.flameT -= dt;
                    while (h.flameT <= 0f) { h.flameT += 0.045f; game.explosions.Flame(h.pos + Random.insideUnitSphere * 12f, h.vel + Random.insideUnitSphere * 12f); }
                    h.smokeT -= dt;
                    while (h.smokeT <= 0f) { h.smokeT += 0.11f; game.explosions.Smoke(h.pos + Random.insideUnitSphere * 10f, h.vel + Random.insideUnitSphere * 8f); }
                    h.sparkT -= dt;
                    if (h.sparkT <= 0f && game.sparks != null) { h.sparkT = Random.Range(0.08f, 0.3f); game.sparks.Burst(h.pos + Random.insideUnitSphere * 10f, Random.Range(3, 9), 120f, Data.Hex("#ffc070"), 1.2f, h.vel); }
                    break;
                case "chain":
                    // pops running along the hull, each with a puff of smoke and a few sparks
                    h.popT -= dt;
                    if (h.popT <= 0f)
                    {
                        h.popT = Random.Range(0.08f, 0.16f);
                        var at = h.pos + h.node.right * Random.Range(-28f, 28f) + h.node.forward * Random.Range(-14f, 10f);
                        game.explosions.Pop(at, h.vel);
                        game.explosions.Smoke(at, h.vel + Random.insideUnitSphere * 10f);
                        if (game.sparks != null) game.sparks.Burst(at, 12, 160f, Data.Hex("#ffb060"), 1.3f, h.vel);
                        Pop(at, -4f);
                    }
                    break;
                case "runaway":
                    // a torch of flame and a thick smoke trail off the engine
                    h.flameT -= dt;
                    while (h.flameT <= 0f) { h.flameT += 0.03f; game.explosions.Flame(h.pos - h.node.forward * Random.Range(14f, 30f) + Random.insideUnitSphere * 5f, h.vel - h.node.forward * 60f); }
                    h.smokeT -= dt;
                    while (h.smokeT <= 0f) { h.smokeT += 0.07f; game.explosions.Smoke(h.pos - h.node.forward * 26f + Random.insideUnitSphere * 6f, h.vel - h.node.forward * 40f); }
                    break;
                case "vent":
                    // a jet of gas and plasma bursts from somewhere on the hull every so often, with sparks and a hiss,
                    // and shoves the hulk a little the other way; a thin haze leaks all the while
                    h.shedT -= dt;
                    if (h.shedT <= 0f)
                    {
                        h.shedT = Random.Range(0.35f, 0.9f);
                        var at = h.pos + h.node.right * Random.Range(-26f, 26f) + h.node.forward * Random.Range(-12f, 10f) + h.node.up * Random.Range(-4f, 4f);
                        var dirOut = (at - h.pos).sqrMagnitude > 1f ? (at - h.pos).normalized : Random.onUnitSphere;
                        for (int k = 0; k < 6; k++) game.explosions.Jet(at + dirOut * k * 5f, h.vel + dirOut * Random.Range(60f, 140f) + Random.insideUnitSphere * 15f);
                        game.explosions.Smoke(at, h.vel + dirOut * 40f);
                        if (game.sparks != null) game.sparks.Burst(at, 14, 160f, Data.Hex("#ffd090"), 1.2f, h.vel + dirOut * 60f);
                        h.vel -= dirOut * Random.Range(6f, 14f);
                        h.spin = Mathf.Min(160f, h.spin + Random.Range(10f, 30f));
                        h.axis = (h.axis + Random.insideUnitSphere * 0.5f).normalized;
                        float bd = (at - game.ship.TruePos).magnitude;
                        Audio.Play("laser_off", Mathf.Max(-30f, -4f + (bd <= 400f ? 0f : -20f * Mathf.Log10(bd / 400f))));   // the hiss
                    }
                    h.smokeT -= dt;
                    while (h.smokeT <= 0f) { h.smokeT += 0.2f; game.explosions.Smoke(h.pos + Random.insideUnitSphere * 8f, h.vel + Random.insideUnitSphere * 6f); }
                    break;
                case "dead":
                    // arcs crawl over the dark hull now and then: a blue-white flash, a spray of sparks, a crackle
                    h.arcT -= dt;
                    if (h.arcT <= 0f)
                    {
                        h.arcT = Random.Range(0.3f, 1.4f);
                        var at = h.pos + Random.insideUnitSphere * 22f;
                        game.explosions.Arc(at, h.vel);
                        if (game.sparks != null) game.sparks.Burst(at, Random.Range(6, 16), 90f, Data.Hex("#bfe8ff"), 1.1f, h.vel);
                        float bd = (h.pos - game.ship.TruePos).magnitude;
                        Audio.Play("zap", Mathf.Max(-30f, -6f + (bd <= 300f ? 0f : -20f * Mathf.Log10(bd / 300f))));
                    }
                    break;
            }
        }
    }

    /// A small bang with distance (the pops of the chain and the shed): the hit clip, a few dB under the blast.
    void Pop(Vector3 at, float db)
    {
        float bd = (at - game.ship.TruePos).magnitude;
        Audio.Play("hit", Mathf.Max(-30f, db + (bd <= 600f ? 0f : -20f * Mathf.Log10(bd / 600f))));
    }

    /// A wreck's end: no blast and nothing comes apart. A last crackle of arcs (the dead hull) or a last gout of flame
    /// and smoke (the rest) and a spray of sparks, the exhaust goes out, and the hull drifts on in one piece as debris,
    /// keeping its velocity and its tumble.
    void QuietEnd(Hulk h)
    {
        if (game.explosions != null)
        {
            if (h.kind == "dead") { game.explosions.Arc(h.pos, h.vel); game.explosions.Arc(h.pos + Random.insideUnitSphere * 15f, h.vel); }
            else for (int k = 0; k < 4; k++) { game.explosions.Flame(h.pos + Random.insideUnitSphere * 14f, h.vel + Random.insideUnitSphere * 25f); game.explosions.Smoke(h.pos + Random.insideUnitSphere * 12f, h.vel + Random.insideUnitSphere * 20f); }
        }
        if (game.sparks != null) game.sparks.Burst(h.pos, 40, 100f, h.kind == "dead" ? Data.Hex("#bfe8ff") : Data.Hex("#ffb060"), 1.2f, h.vel);
        Pop(h.pos, -10f);
        if (h.exhaustNode != null) h.exhaustNode.gameObject.SetActive(false);
        _debris.Add(new Debris { node = h.node, pos = h.pos, vel = h.vel, axis = h.axis, rate = Mathf.Max(20f, h.spin * 0.6f), life = DEBRIS_LIFE });
        while (_debris.Count > DEBRIS_MAX) { Object.Destroy(_debris[0].node.gameObject); _debris.RemoveAt(0); }
    }

    public int HulkCount { get { return _hulks.Count; } }

    void Shatter(Transform node, Vector3 pos, Vector3 vel)
    {
        var parts = new List<Transform>();
        for (int i = 0; i < node.childCount; i++) parts.Add(node.GetChild(i));
        foreach (var p in parts)
        {
            if (p.name == "Exhaust") { Object.Destroy(p.gameObject); continue; }
            p.SetParent(_root, true);   // keeps the world position, rotation and scale
            var outward = (p.position + game.worldOffset - pos);
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Random.onUnitSphere;
            var shove = outward * Random.Range(40f, 120f) + Random.insideUnitSphere * 30f;
            _debris.Add(new Debris
            {
                node = p, pos = p.position + game.worldOffset, vel = vel + shove,
                axis = Random.onUnitSphere, rate = Random.Range(40f, 220f), life = DEBRIS_LIFE
            });
        }
        Object.Destroy(node.gameObject);
        while (_debris.Count > DEBRIS_MAX) { Object.Destroy(_debris[0].node.gameObject); _debris.RemoveAt(0); }
    }

    void TickDebris(float dt, Vector3 off)
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.life -= dt;
            if (d.life <= 0f || d.node == null) { if (d.node != null) Object.Destroy(d.node.gameObject); _debris.RemoveAt(i); continue; }
            d.pos += d.vel * dt;
            d.node.position = d.pos - off;
            d.node.Rotate(d.axis, d.rate * dt, Space.World);
        }
    }

    public int DebrisCount { get { return _debris.Count; } }

    public string Stats()
    {
        int attacking = 0;
        foreach (var r in raiders) if (r.state == "attack") attacking++;
        return raiders.Count + " raiders, " + attacking + " attacking, " + bolts.Count + " bolts, kills " + kills + ", shots " + shotsFired + ", landed " + hitsLanded + ", hits taken " + hitsTaken + ", passes " + passes + ", rockets " + rocketsFired + " (" + rocketKills + " kills), blocked " + Solids.blocks + ", pushed " + Solids.pushes;
    }
}
