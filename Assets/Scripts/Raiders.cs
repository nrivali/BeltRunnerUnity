using System.Collections.Generic;
using UnityEngine;

/// Combat: pirate raiders holding station off the rich pockets, brought back from the browser's scrapped hazards code
/// (makePirate / updateHazards) with its numbers. A hold of one to three raiders wanders round each rich pocket; when
/// the ship comes within 6,500 u (flying, and outside the cargo ship's gun cover) they attack, closing to 900 u and
/// orbiting, firing bolts that lead the ship whenever their nose is on it (the guns are fixed forward). They give up beyond 11,000 u or when the ship is disabled or docked, and
/// they die under the cargo ship's guns inside SAFE_R. The player's autocannon (a refit) fires bolts from the dish
/// focus; a kill pays a bounty and sometimes drops salvage. Positions are true world coordinates.
public class Raiders
{
    public const float SAFE_R = 9000f;      // cargo ship gun cover: raiders die here and never engage inside it
    public const float ENGAGE = 6500f;
    public const float GIVE_UP = 11000f;
    public const float BOLT_SPEED = 2200f;
    public const float PLAYER_BOLT_SPEED = 2600f;
    public const float RADIUS = 14f;

    public class Raider
    {
        public Vector3 pos, vel, home, wp;
        public Transform node;
        public Material exhaust;
        public float hp, maxHp, dmg, speed, bounty, a, fireCd;
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
    public int kills, shotsFired, hitsTaken;   // for the smoke run
    public float danger;
    public bool frozen;   // the combat test: raiders made while this is set hold their place
    public bool respawn;  // the combat test: a raider killed comes back where it stood, three seconds on
    class Pending { public Vector3 pos, home; public float t; public bool frozen; }
    readonly List<Pending> _pending = new List<Pending>();
    Transform _root;
    Material _hull, _trim, _boltRed, _boltCyan;
    Mesh _boltMesh;
    readonly List<Transform> _boltPool = new List<Transform>();
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
        _boltRed = new Material(Game.Sh("BeltRunner/Spark"));
        _boltRed.SetColor("_Color", new Color(1f, 0.29f, 0.29f, 0.9f));
        _boltCyan = new Material(Game.Sh("BeltRunner/Spark"));
        _boltCyan.SetColor("_Color", new Color(0.37f, 0.83f, 0.94f, 0.9f));
    }

    public void Clear()
    {
        _pending.Clear();
        foreach (var r in raiders) if (r.node != null) Object.Destroy(r.node.gameObject);
        raiders.Clear();
        foreach (var b in bolts) if (b.node != null) b.node.gameObject.SetActive(false);
        bolts.Clear();
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
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
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
        Object.Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(go.transform, false);
        eye.transform.localPosition = new Vector3(0f, 2.5f, 4f);
        eye.transform.localScale = Vector3.one * 2.8f;
        eye.GetComponent<MeshRenderer>().sharedMaterial = _trim;
        var ex = Ship.SoftMaterial(new Color(1f, 0.18f, 0.39f, 0.6f));
        Ship.GlowQuad(go.transform, new Vector3(0f, 0f, -16f), 9f, ex, "Exhaust");
        float hp = Mathf.Round(60f * (0.6f + danger * 0.6f));
        raiders.Add(new Raider
        {
            pos = pos, home = home, wp = home, node = go.transform, exhaust = ex, hp = hp, maxHp = hp, dmg = Mathf.Round(3f + 4f * danger),
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
        r.hp -= dmg;
        if (game.sparks != null) game.sparks.Burst(at, 12, 180f, Data.Hex("#ff8a3a"), 1f);
        if (r.hp <= 0f) Kill(r, true);
    }

    void Kill(Raider r, bool byPlayer)
    {
        r.dead = true;
        raiders.Remove(r);
        if (respawn) _pending.Add(new Pending { pos = r.pos, home = r.home, t = 3f, frozen = r.frozen });
        if (r.node != null) Object.Destroy(r.node.gameObject);
        Audio.Play("boom");
        if (game.sparks != null)
        {
            game.sparks.Burst(r.pos, 260, 420f, Data.Hex("#ff8a3a"), 1.6f);
            game.sparks.Burst(r.pos, 90, 180f, Data.Hex("#ffe0a0"), 2.4f);
        }
        if (byPlayer)
        {
            kills++;
            State.credits += r.bounty;
            State.earned += r.bounty;
            string drop = "";
            if (Random.value < 0.35f)
            {
                var keys = new List<string>(game.zone.belts[2].Keys);
                if (keys.Count > 0)
                {
                    string k = keys[Random.Range(0, keys.Count)];
                    float u = Mathf.Min(Random.Range(8f, 28f), State.CargoRoom(k));
                    if (u > 0.5f) { State.AddCargo(k, u); drop = " · salvaged " + Mathf.FloorToInt(u) + " " + Data.ORES[Data.OreIndex(k)].name; }
                }
            }
            game.Toast("Raider destroyed · +" + Data.Fmt(r.bounty) + " cr bounty" + drop, false);
        }
        else game.Toast("Cargo ship guns downed a raider", false);
    }

    Transform BoltNode(bool player)
    {
        foreach (var t in _boltPool) if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); t.GetComponent<MeshRenderer>().sharedMaterial = player ? _boltCyan : _boltRed; return t; }
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "Bolt";
        go.transform.SetParent(_root, false);
        go.transform.localScale = new Vector3(1.2f, 7f, 1.2f);   // 14 long, 0.6 across
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = player ? _boltCyan : _boltRed;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _boltPool.Add(go.transform);
        return go.transform;
    }

    public void Fire(Vector3 from, Vector3 dir, float dmg, bool player)
    {
        var b = new Bolt { pos = from, dir = dir.normalized, life = 1.6f, dmg = dmg, player = player, node = BoltNode(player) };
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
                r.dodgeT = 0.5f;
                r.dodgeCd = 1.4f;
            }
        }
    }

    public void Tick(float dt)
    {
        var ship = game.ship;
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
        var sp = ship.TruePos;
        bool flying = game.started && !ship.docked && ship.warp == null && ship.cut == null;
        bool nearDepot = carrier != null && !carrier.hold && (sp - carrier.truePos).magnitude < SAFE_R;
        bool canAttack = flying && ship.CanFly && !nearDepot;
        threat = 0;
        for (int i = raiders.Count - 1; i >= 0; i--)
        {
            var r = raiders[i];
            float d = (r.pos - sp).magnitude;
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
                    if (d < 750f) { r.move = "strafe"; r.moveT = Random.Range(2.5f, 6f); r.orbitR = Random.Range(300f, 700f); r.orbitDir = Random.value < 0.5f ? -1f : 1f; }
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
                        if (roll < 0.25f)
                        {
                            // a long run: out to a point 2,000 to 5,000 m from the ship, then back in from wherever that leaves it
                            r.move = "long";
                            var away = -toShip + side * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(-0.3f, 0.3f);
                            float reach = Random.Range(4000f, 10000f);
                            r.longTo = sp + away.normalized * reach;
                            r.moveT = reach / r.speed * 1.6f;
                        }
                        else
                        {
                            r.move = roll < 0.55f ? "strafe" : "break";
                            r.moveT = r.move == "break" ? Random.Range(1.5f, 3.5f) : Random.Range(2.5f, 6f);
                            r.orbitR = Random.Range(300f, 700f);
                            if (Random.value < 0.5f) r.orbitDir = -r.orbitDir;
                        }
                    }
                }
                else if (r.move == "long")
                {
                    // the long run, weaving a little; over when the point is reached or the time is up
                    desired = r.longTo + side * Mathf.Sin(r.weave) * 150f;
                    if ((r.longTo - r.pos).magnitude < 300f || r.moveT <= 0f) { r.move = "run"; r.moveT = 0f; }
                }
                else
                {
                    // the breakaway: out to 1,600 u off to one side, then a fresh run
                    desired = sp - toShip * 1600f + side * r.orbitDir * 700f + Vector3.up * Mathf.Sin(r.weave * 0.5f) * 200f;
                    if (r.moveT <= 0f || d > 1900f) { r.move = "run"; r.moveT = 0f; }
                }
                // the jink
                r.dodgeCd -= dt;
                if (r.dodgeT > 0f) { r.dodgeT -= dt; desired = r.pos + r.dodgeDir * 900f; }
                r.fireCd -= dt;
                // the guns are fixed forward: a raider only fires when its nose is on the ship (within 20 degrees)
                bool facing = Vector3.Dot(r.node.forward, (sp - r.pos).normalized) > Mathf.Cos(20f * Mathf.Deg2Rad);
                if (r.fireCd <= 0f && d < 900f && facing)
                {
                    r.fireCd = 1.2f / Mathf.Max(0.6f, danger * 0.8f);
                    float spread = 0.09f / Mathf.Max(0.7f, danger);   // raiders in quiet zones are poor shots
                    var dir = (sp + ship.vel * (d / BOLT_SPEED) - r.pos).normalized + new Vector3(Random.Range(-spread, spread), Random.Range(-spread, spread), Random.Range(-spread, spread));
                    Fire(r.pos, dir.normalized, r.dmg, false);
                    Audio.Play("zap", -6f * Mathf.Clamp01(d / 1200f));
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
                r.vel = Vector3.zero;
                if (r.state == "attack" && d > 1f) r.node.rotation = Quaternion.Slerp(r.node.rotation, Ship.LevelHeading((sp - r.pos) / d), 1f - Mathf.Exp(-3f * dt));
            }
            else
            {
                float agility = r.dodgeT > 0f ? 6f : 2.2f;   // a jink is a snap, the rest a lean
                r.vel = Vector3.Lerp(r.vel, want * (r.state == "attack" ? r.speed : 300f), 1f - Mathf.Exp(-agility * dt));
                r.pos += r.vel * dt;
                if (r.vel.sqrMagnitude > 1f) r.node.rotation = Quaternion.Slerp(r.node.rotation, Ship.LevelHeading(r.vel.normalized), 1f - Mathf.Exp(-6f * dt));
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
                    if ((prev + ab * t - r.pos).magnitude < RADIUS + 6f)
                    {
                        Damage(r, b.dmg, r.pos);
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

    public string Stats()
    {
        int attacking = 0;
        foreach (var r in raiders) if (r.state == "attack") attacking++;
        return raiders.Count + " raiders, " + attacking + " attacking, " + bolts.Count + " bolts, kills " + kills + ", shots " + shotsFired + ", hits taken " + hitsTaken;
    }
}
