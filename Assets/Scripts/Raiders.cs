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
    public const float ENGAGE = 9000f;      // aggressive: they come for a ship 4,500 m out
    public const float GIVE_UP = 14000f;
    public const float BOLT_SPEED = 2600f;  // a raider's bolt
    public const float RAIDER_DMG = 4f;      // a raider bolt: half the player's base gun
    public const float RAIDER_REACH = 6000f; // raiders open fire from 3,000 m; their bolt lives long enough to get there
    public const float RAIDER_BOLT_LIFE = 2.6f;
    public const float PLAYER_BOLT_SPEED = 5000f;   // the player's bolt is faster: PLAYER_BOLT_LIFE of flight covers 11,000 u, past the 10,000 u gun reach
    public const float PLAYER_BOLT_LIFE = 2.2f;
    public const float RADIUS = 28f;    // the hull, for the reticle, ranges and the pick (the model is drawn at twice its original size)
    public const float HIT_R = 60f;     // the hit box a bolt has to pass through: generous, the raider is fast; it covers the wing tips
    // a raider flies like a ship: it turns no faster than this, and speeds up and slows down no harder than this
    public const float TURN_RATE = 30f * Mathf.Deg2Rad;   // the same as the player's ship
    public const float ACCEL = 220f;
    public const float DECEL = 320f;

    public class Raider
    {
        public Vector3 pos, vel, home, wp;
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
        // the attack: a run in (weaving), a strafing pass at a radius and direction of its own, a breakaway, then again
        public string move = "run";
        public float moveT, orbitR = 400f, orbitDir = 1f, weave;
        // the jink: a burst sideways when a bolt is coming, with a cooldown so it is not perfect
        public float dodgeT, dodgeCd;
        public Vector3 dodgeDir;
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
    }

    public Game game;
    public readonly List<Raider> raiders = new List<Raider>();
    public readonly List<Bolt> bolts = new List<Bolt>();
    public int threat;              // raiders attacking right now
    public float nearest = 1e9f;    // the nearest living raider's distance from the ship (for its engine sound)
    public bool nearestBoosting;
    public float nearestBoost = 1e9f;   // the nearest boosting raider's distance (for the afterburner roar)
    public int kills, shotsFired, hitsTaken;   // for the smoke run
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
    }
    public const float ROCKET_SPEED = 2400f, ROCKET_ACCEL = 1800f, ROCKET_KICK = 300f, ROCKET_TURN = 70f * Mathf.Deg2Rad, ROCKET_LIFE = 12f, ROCKET_HIT_R = 70f;
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
        // the raiders' bolts are amber; the player's are a hot red with a glow round each, so they read at range
        _boltRed = new Material(Game.Sh("BeltRunner/Spark"));
        _boltRed.SetColor("_Color", new Color(1f, 0.62f, 0.22f, 0.9f));
        _boltCyan = new Material(Game.Sh("BeltRunner/Spark"));
        _boltCyan.SetColor("_Color", new Color(1f, 0.22f, 0.16f, 1f));
        _boltGlowPlayer = Ship.SoftMaterial(new Color(1f, 0.18f, 0.12f, 0.6f));
        _boltGlowRaider = Ship.SoftMaterial(new Color(1f, 0.6f, 0.2f, 0.4f));
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
        foreach (var h in _hulks) if (h.node != null) Object.Destroy(h.node.gameObject);
        _hulks.Clear();
        foreach (var d in _debris) if (d.node != null) Object.Destroy(d.node.gameObject);
        _debris.Clear();
        threat = 0;
        _warned = false;
    }

    /// Pirate holds by the rich pockets: one hold each, one to three raiders (more in a more dangerous zone).
    public void Build(Data.Zone zone, Belt belt)
    {
        Clear();
        danger = zone.danger;
        if (zone.hub || danger <= 0f) return;
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
    public Raider Make(Vector3 pos, Vector3 home)
    {
        var go = new GameObject("Raider");
        go.transform.SetParent(_root, false);
        go.transform.localScale = Vector3.one * 2f;   // twice the size it was drawn at
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
            tip.GetComponent<MeshRenderer>().sharedMaterial = _trim;
        }
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        Object.Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(go.transform, false);
        eye.transform.localPosition = new Vector3(0f, 2.5f, 4f);
        eye.transform.localScale = Vector3.one * 2.8f;
        eye.GetComponent<MeshRenderer>().sharedMaterial = _trim;
        var ex = Ship.SoftMaterial(new Color(1f, 0.18f, 0.39f, 0.6f));
        var exNode = Ship.GlowQuad(go.transform, new Vector3(0f, 0f, -16f), 9f, ex, "Exhaust");
        float hp = Mathf.Round(50f * Mathf.Max(1f, 0.5f + danger));       // 50 at Kessler's danger, more in a harder zone
        float shield = Mathf.Round(50f * Mathf.Max(1f, 0.5f + danger));
        raiders.Add(new Raider
        {
            pos = pos, home = home, wp = home, node = go.transform, exhaust = ex, exhaustNode = exNode, hp = hp, maxHp = hp, shield = shield, maxShield = shield, dmg = Mathf.Round(3f + 4f * danger),
            heading = Random.onUnitSphere, spd = 120f,
            gunDmg = RAIDER_DMG, gunRate = Data.UPGRADES["gun"].levels[0].rate, gunReach = RAIDER_REACH,   // the player's own base autocannon
            a = Random.value * 6f, fireCd = Random.Range(1f, 2f), speed = 820f + danger * 90f, bounty = Mathf.Round(120f * danger + 80f), frozen = frozen,
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

    /// The nearest live raider within `maxDist` of `origin` whose bearing is within the cone (cosine) of `dir`.
    public Raider NearestInCone(Vector3 origin, Vector3 dir, float maxDist, float cosAng)
    {
        Raider best = null;
        float bd = float.PositiveInfinity;
        foreach (var r in raiders)
        {
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
    public void Damage(Raider r, float dmg, Vector3 at)
    {
        if (r.dead) return;
        r.sinceHit = 0f;
        float toShield = Mathf.Min(r.shield, dmg);
        r.shield -= toShield;
        r.hp -= dmg - toShield;
        if (game.sparks != null) game.sparks.Burst(at, 12, 180f, Data.Hex("#ff8a3a"), 1f);
        if (r.hp <= 0f) Kill(r, true);
    }

    void Kill(Raider r, bool byPlayer, bool blast = false)
    {
        r.dead = true;
        raiders.Remove(r);
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

    public void Fire(Vector3 from, Vector3 dir, float dmg, bool player)
    {
        var b = new Bolt { pos = from, dir = dir.normalized, life = player ? PLAYER_BOLT_LIFE : RAIDER_BOLT_LIFE, dmg = dmg, player = player, node = BoltNode(player) };
        b.node.rotation = Quaternion.FromToRotation(Vector3.up, b.dir);
        bolts.Add(b);
        if (player)
        {
            shotsFired++;
            // a raider that sees the bolt coming its way jinks aside, most of the time, once its cooldown allows
            foreach (var r in raiders)
            {
                if (r.frozen || r.dodgeCd > 0f) continue;
                var to = r.pos - from;
                float along = Vector3.Dot(to, b.dir);
                if (along < 0f || along > 2500f) continue;
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

    void TickRockets(float dt, Vector3 off)
    {
        for (int i = rockets.Count - 1; i >= 0; i--)
        {
            var k = rockets[i];
            k.life -= dt;
            if (k.target != null && (k.target.dead || !raiders.Contains(k.target))) k.target = null;
            if (k.target != null)
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
            // the exhaust trail: a puff of smoke every few metres of flight, left behind and drifting
            k.smokeT -= dt;
            if (k.smokeT <= 0f && game.explosions != null) { k.smokeT = 0.07f; game.explosions.Smoke(k.pos - k.heading * 9f, k.heading * -30f + Random.insideUnitSphere * 8f); }
            // contact: the segment flown this frame against every raider's hit sphere
            bool hit = false;
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
                    Kill(r, true, true);   // destroyed outright, on the spot
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
        var sp = ship.TruePos;
        bool flying = game.started && !ship.docked && ship.warp == null && ship.cut == null;
        bool nearDepot = carrier != null && !carrier.hold && (sp - carrier.truePos).magnitude < SAFE_R;
        bool canAttack = flying && ship.CanFly && !nearDepot;
        threat = 0;
        nearest = 1e9f;
        nearestBoosting = false;
        nearestBoost = 1e9f;
        for (int i = raiders.Count - 1; i >= 0; i--)
        {
            var r = raiders[i];
            float d = (r.pos - sp).magnitude;
            if (d < nearest) { nearest = d; nearestBoosting = r.boosting; }
            if (r.boosting && d < nearestBoost) nearestBoost = d;
            r.sinceHit += dt;
            if (r.sinceHit >= 10f && r.shield < r.maxShield) r.shield = Mathf.Min(r.maxShield, r.shield + 10f * dt);
            if (r.state == "idle" && canAttack && d < ENGAGE)
            {
                r.state = "attack";
                if (threat == 0 && !_warned) { game.Toast("Pirate raiders inbound", true); Audio.Play("alarm"); _warned = true; }
            }
            if (r.state == "attack" && (!canAttack || d > GIVE_UP)) { r.state = "idle"; r.wp = r.home; }
            Vector3 desired;
            if (r.state == "attack")
            {
                threat++;
                var toShip = d > 1f ? (sp - r.pos) / d : Vector3.forward;
                var side = Vector3.Cross(toShip, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(toShip, Vector3.right);
                side.Normalize();
                r.moveT -= dt;
                r.weave += dt * 1.7f;
                if (r.move == "run")
                {
                    // the run in: toward the ship, weaving side to side, until close
                    desired = sp + side * Mathf.Sin(r.weave) * 260f + Vector3.up * Mathf.Sin(r.weave * 0.6f) * 90f;
                    if (d < 600f) { r.move = "strafe"; r.moveT = Random.Range(2f, 4f); r.orbitR = Random.Range(200f, 500f); r.orbitDir = Random.value < 0.5f ? -1f : 1f; }
                }
                else if (r.move == "strafe")
                {
                    // a strafing pass round the ship at its own radius and direction, weaving up and down
                    r.a += r.orbitDir * (r.speed * 0.6f / r.orbitR) * dt;
                    var orbit = new Vector3(Mathf.Cos(r.a) * r.orbitR, Mathf.Sin(r.a * 0.7f) * r.orbitR * 0.35f, Mathf.Sin(r.a) * r.orbitR);
                    desired = sp + orbit;
                    if (r.moveT <= 0f)
                    {
                        float roll = Random.value;
                        if (roll < 0.5f)
                        {
                            // a long run, half the time: full thrust out to a point 1,250 to 2,500 m from the ship to get range,
                            // then a fresh attack run back in, guns going as soon as the nose is on you
                            r.move = "long";
                            var away = -toShip + side * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(-0.3f, 0.3f);
                            float reach = Random.Range(2500f, 5000f);
                            r.longTo = sp + away.normalized * reach;
                            r.moveT = reach / r.speed * 1.6f;
                        }
                        else
                        {
                            r.move = roll < 0.9f ? "strafe" : "break";   // otherwise mostly another pass
                            r.moveT = r.move == "break" ? Random.Range(1.2f, 2.5f) : Random.Range(2f, 4f);
                            r.orbitR = Random.Range(200f, 500f);
                            if (Random.value < 0.5f) r.orbitDir = -r.orbitDir;
                        }
                    }
                }
                else if (r.move == "long")
                {
                    // the long run, weaving a little; over when the point is reached or the time is up
                    desired = r.longTo + side * Mathf.Sin(r.weave) * 150f;
                    if ((r.longTo - r.pos).magnitude < 400f || r.moveT <= 0f) { r.move = "run"; r.moveT = 0f; }
                }
                else
                {
                    // the breakaway: out to 1,600 u off to one side, then a fresh run
                    desired = sp - toShip * 1000f + side * r.orbitDir * 500f + Vector3.up * Mathf.Sin(r.weave * 0.5f) * 200f;
                    if (r.moveT <= 0f || d > 1300f) { r.move = "run"; r.moveT = 0f; }
                }
                // the jink: a hard turn aside, as hard as the ship can turn
                r.dodgeCd -= dt;
                if (r.dodgeT > 0f) { r.dodgeT -= dt; desired = r.pos + r.dodgeDir * 900f; }
                r.fireCd -= dt;
                // the guns are fixed forward: a raider only fires when its nose is on the ship (within 20 degrees)
                bool facing = Vector3.Dot(r.node.forward, (sp - r.pos).normalized) > Mathf.Cos(25f * Mathf.Deg2Rad);
                if (r.fireCd <= 0f && d < r.gunReach && facing && !holdFire)   // the player's own gun: its range, its rate, its damage
                {
                    r.fireCd = 1f / r.gunRate;
                    // a rough gunner: the lead is over- or under-estimated shot by shot, and the spread is wide (about 5 degrees)
                    float spread = 0.09f;
                    float leadErr = Random.Range(0.55f, 1.15f);
                    var dir = (sp + ship.vel * (d / BOLT_SPEED) * leadErr - r.pos).normalized + new Vector3(Random.Range(-spread, spread), Random.Range(-spread, spread), Random.Range(-spread, spread));
                    Fire(r.pos, dir.normalized, r.gunDmg, false);
                    // the same blaster as the player's, quieter with distance: 6 dB a doubling beyond 300 u, silent past 30 dB down
                    float att = d <= 300f ? 0f : -20f * Mathf.Log10(d / 300f);
                    if (att > -30f) Audio.Shot("blaster", att);
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
                SetBoost(r, false, d);
                r.vel = Vector3.zero;
                r.spd = 0f;
                if (r.state == "attack" && d > 1f) r.heading = RotateTowards(r.heading, (sp - r.pos) / d, TURN_RATE * dt);
                r.node.rotation = Ship.LevelHeading(r.heading);
            }
            else
            {
                // the flight model: the nose turns toward where it wants to go no faster than a ship could, the throttle
                // works against thrust and braking limits, and the raider only ever moves along its nose
                r.heading = RotateTowards(r.heading, want, TURN_RATE * dt);
                float wantSpd = r.state == "attack" ? r.speed : 300f;
                if (r.state == "attack" && r.move == "strafe") wantSpd = Mathf.Min(wantSpd, r.orbitR * TURN_RATE * 0.9f);   // slow enough to hold the circle
                if (r.state != "attack") wantSpd = Mathf.Min(wantSpd, Mathf.Max(60f, dist / 3f));   // ease up on the point (a long run is flown at full thrust)
                float off2 = Vector3.Dot(r.heading, want);
                if (off2 < 0.3f) wantSpd = Mathf.Min(wantSpd, 260f);   // pointing the wrong way: throttle back through the turn
                // the boost: on a run in from far out or a long run out, nose on the mark, the throttle goes wide open
                bool boost = r.state == "attack" && off2 > 0.8f && (r.move == "long" || (r.move == "run" && d > 900f));
                if (boost) wantSpd *= BOOST_MULT;
                SetBoost(r, boost, d);
                r.spd = r.spd < wantSpd ? Mathf.Min(wantSpd, r.spd + ACCEL * (boost ? 1.6f : 1f) * dt) : Mathf.Max(wantSpd, r.spd - DECEL * dt);
                if (boost && game.explosions != null)
                {
                    // the jet trail: pale puffs streaming off the exhaust
                    r.jetT -= dt;
                    while (r.jetT <= 0f) { r.jetT += 0.035f; game.explosions.Jet(r.pos - r.heading * Random.Range(16f, 26f) + Random.insideUnitSphere * 3f, r.vel - r.heading * 120f); }
                }
                r.vel = r.heading * r.spd;
                r.pos += r.vel * dt;
                r.node.rotation = Ship.LevelHeading(r.heading);
            }
            r.node.position = r.pos - off;
            r.exhaust.SetColor("_Color", new Color(1f, 0.18f, 0.39f, r.state == "attack" ? 0.6f + Random.value * 0.3f : 0.35f));
            if (carrier != null && !carrier.hold && (r.pos - carrier.truePos).magnitude < SAFE_R)
            {
                r.hp -= 30f * dt;
                if (r.hp <= 0f) Kill(r, false);
            }
        }
        if (threat == 0) _warned = false;
        if (hitFlash > 0f) hitFlash = Mathf.Max(0f, hitFlash - dt / HIT_FLASH);
        // the bolts: a raider's hits the ship, the player's hit raiders
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            b.life -= dt;
            float speed = b.player ? PLAYER_BOLT_SPEED : BOLT_SPEED;
            var prev = b.pos;
            b.pos += b.dir * speed * dt;
            b.node.position = b.pos - off;
            if (b.player)
            {
                foreach (var r in raiders)
                {
                    // the segment flown this frame against the raider's disc
                    var ab = b.pos - prev;
                    float t = Mathf.Clamp01(Vector3.Dot(r.pos - prev, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if ((prev + ab * t - r.pos).magnitude < HIT_R)
                    {
                        Damage(r, b.dmg, r.pos);
                        hitsLanded++;
                        hitFlash = 1f;
                        hitKill = r.dead;
                        Audio.Sure(r.dead ? "kill_marker" : "hit_marker");   // the hit marker: a punchy tick, a sting on the kill   // the hit marker: a punchy tick, a sting on the kill
                        b.life = 0f;
                        break;
                    }
                }
            }
            else if (flying && ship.CanFly && (b.pos - sp).magnitude < Data.SHIP_R * 0.8f)
            {
                hitsTaken++;
                ship.Hurt(b.dmg, b.pos, "Raider hit");
                b.life = 0f;
            }
            if (b.life <= 0f)
            {
                b.node.gameObject.SetActive(false);
                bolts.RemoveAt(i);
            }
        }
    }

    public const float BOOST_MULT = 1.25f;

    /// The boost on or off: the exhaust flares white-blue at twice the size while it is on (its normal pink glow
    /// otherwise), and a whoosh and roar plays with distance the moment it kicks in.
    void SetBoost(Raider r, bool on, float d)
    {
        if (on == r.boosting) return;
        r.boosting = on;
        if (r.exhaust != null) r.exhaust.SetColor("_Color", on ? new Color(0.75f, 0.88f, 1f, 0.95f) : new Color(1f, 0.18f, 0.39f, 0.6f));
        if (r.exhaustNode != null) r.exhaustNode.localScale = Vector3.one * (on ? 18f : 9f);
        if (on)
        {
            float att = d <= 300f ? 0f : -20f * Mathf.Log10(d / 300f);
            if (att > -30f) Audio.Play("raider_boost", att);
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
        return raiders.Count + " raiders, " + attacking + " attacking, " + bolts.Count + " bolts, kills " + kills + ", shots " + shotsFired + ", landed " + hitsLanded + ", hits taken " + hitsTaken + ", rockets " + rocketsFired + " (" + rocketKills + " kills)";
    }
}
