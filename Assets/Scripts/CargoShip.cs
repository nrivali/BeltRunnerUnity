using UnityEngine;

/// The pilot's cargo ship: the carrier with the through-hangar. Ported from DEPOT / STATION / placeDepot /
/// depotCollide in belt-runner-3d.html (via the Godot port). Its local frame is the HTML's: the keel runs along X
/// with the nose at +X, decks stack in Y, and the hangar runs straight through the hull along Z, open on both
/// flanks. Each "bay" is one mouth of that hangar; a ship docks on the pad just inside the FAR mouth, parked facing
/// that mouth, so it leaves straight ahead. The carrier orbits the planet at DEPOT_ORBIT; `truePos` is its world
/// position and the transform sits at truePos minus the floating origin. The geometry is the placeholder hull until
/// the Blender carrier comes across.
public class CargoShip : MonoBehaviour
{
    public static readonly Vector3 HALF = new Vector3(3700f, 540f, 900f);   // the hull's collision box
    public const float PROW_X0 = 3700f;
    public const float PROW_X1 = 4600f;
    public const float PROW_R0 = 800f;
    public const float BAY_X0 = -420f;
    public const float BAY_X1 = 420f;
    public const float BAY_Y0 = -150f;   // the deck is the collision floor
    public const float BAY_Y1 = 176f;
    public const float BAY_Z_OUT = 900f;

    public Vector3 truePos;
    public Vector3 vel;
    public Quaternion basisQ = Quaternion.identity;
    public float ang = Mathf.PI / 2f;
    public float orbit = Data.DEPOT_ORBIT;
    public float speed = Data.STATION_SPEED;
    public bool hold;   // at the Hub the carrier does not orbit: it is flown to its holding point and parked there
    public Game game;

    /// The carrier heading whose nose (+X) points along a world direction, deck level.
    public static Quaternion HeadingAlong(Vector3 d)
    {
        var f = new Vector3(d.x, 0f, d.z);
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        return Quaternion.LookRotation(f.normalized, Vector3.up) * Quaternion.AngleAxis(-90f, Vector3.up);
    }

    public Vector3 Nose { get { return basisQ * Vector3.right; } }

    /// A bay by its side (+1 or -1): where its pad and mouth are, in the carrier's frame. The pad sits just inside its
    /// own mouth, facing that mouth.
    public static Vector3 ParkLocal(int side) { return new Vector3(0f, -100f, side * 525f); }
    public static Vector3 OpeningLocal(int side) { return new Vector3(0f, 0f, side * 1000f); }
    /// The direction a ship parked in this bay faces: out of its own mouth.
    public static Vector3 FaceLocal(int side) { return new Vector3(0f, 0f, side); }
    public static string BayName(int side) { return side > 0 ? "Dock 1" : "Dock 2"; }

    public Vector3 ToLocalTrue(Vector3 pTrue) { return Quaternion.Inverse(basisQ) * (pTrue - truePos); }
    public Vector3 ToTrue(Vector3 local) { return basisQ * local + truePos; }
    public Vector3 Dir(Vector3 local) { return basisQ * local; }

    /// Put the carrier somewhere by hand.
    public void SetPose(Vector3 pTrue, Quaternion q)
    {
        truePos = pTrue;
        basisQ = q;
        transform.position = truePos - game.worldOffset;
        transform.rotation = basisQ;
    }

    /// Advance the orbit and refresh the transform. Returns how far the carrier moved this frame (a docked ship rides along).
    public Vector3 Tick(float dt)
    {
        if (hold)
        {
            vel = Vector3.zero;
            Place();
            return Vector3.zero;
        }
        var prev = truePos;
        ang -= (speed / orbit) * dt;
        Place();
        vel = (truePos - prev) / Mathf.Max(dt, 1e-4f);
        return truePos - prev;
    }

    public void Place()
    {
        if (!hold)
        {
            truePos = new Vector3(Mathf.Cos(ang) * orbit, 0f, Mathf.Sin(ang) * orbit);
            basisQ = Quaternion.AngleAxis((Mathf.PI / 2f - ang) * Mathf.Rad2Deg, Vector3.up);   // nose (+X) along the direction of travel
        }
        transform.position = truePos - game.worldOffset;
        transform.rotation = basisQ;
    }

    /// Inside the through-hangar corridor?
    public bool InCorridor(Vector3 local)
    {
        return local.x > BAY_X0 - 16f && local.x < BAY_X1 + 16f && local.y > BAY_Y0 - 16f && local.y < BAY_Y1 + 16f && Mathf.Abs(local.z) < BAY_Z_OUT + 16f;
    }

    /// The mouth a ship came in by, from its travel direction in the carrier's frame (falls back to which half it is in).
    public int EntrySide(Vector3 local, Vector3 relVelLocal)
    {
        if (Mathf.Abs(relVelLocal.z) > 5f) return relVelLocal.z > 0f ? -1 : 1;
        return local.z < 0f ? -1 : 1;
    }

    /// The mouth nearer to a world point.
    public int NearestSide(Vector3 pTrue)
    {
        float d1 = (ToTrue(OpeningLocal(1)) - pTrue).sqrMagnitude;
        float d2 = (ToTrue(OpeningLocal(-1)) - pTrue).sqrMagnitude;
        return d1 <= d2 ? 1 : -1;
    }

    /// The bay whose mouth faces the planet (the origin): every start is on that pad, so the first departure heads for the belt.
    public int PlanetSide()
    {
        var toPlanet = (-truePos).normalized;
        return Vector3.Dot(Dir(FaceLocal(1)), toPlanet) >= Vector3.Dot(Dir(FaceLocal(-1)), toPlanet) ? 1 : -1;
    }

    /// Collision with the hull for a ship of radius m at carrier-local position p. Inside the hangar corridor the ship
    /// is clamped to the walls; anywhere else inside the hull it is pushed out through the nearest face; the prow is a
    /// cone. Returns false for no contact, else the corrected local position and the local surface normal.
    public bool Collide(Vector3 p, float m, out Vector3 pos, out Vector3 n)
    {
        pos = p;
        n = Vector3.up;
        if (p.x > PROW_X0 && p.x < PROW_X1)
        {
            float rr = new Vector2(p.y, p.z).magnitude;
            float allow = PROW_R0 * (PROW_X1 - p.x) / (PROW_X1 - PROW_X0) + m;
            if (rr < allow)
            {
                n = rr < 1e-3f ? Vector3.up : new Vector3(0f, p.y / rr, p.z / rr);
                pos = new Vector3(p.x, n.y * allow, n.z * allow);
                return true;
            }
            return false;
        }
        if (Mathf.Abs(p.x) > HALF.x + m || Mathf.Abs(p.y) > HALF.y + m || Mathf.Abs(p.z) > HALF.z + m) return false;
        if (InCorridor(p))
        {
            float lx = Mathf.Clamp(p.x, BAY_X0 + m, BAY_X1 - m);
            float ly = Mathf.Clamp(p.y, BAY_Y0 + m, BAY_Y1 - m);
            var nn = new Vector3(lx - p.x, ly - p.y, 0f);
            float len = nn.magnitude;
            if (len < 1e-6f) return false;
            pos = new Vector3(lx, ly, p.z);
            n = nn / len;
            return true;
        }
        float dxs = HALF.x + m - Mathf.Abs(p.x);
        float dys = HALF.y + m - Mathf.Abs(p.y);
        float dzs = HALF.z + m - Mathf.Abs(p.z);
        var q = p;
        if (dys <= dxs && dys <= dzs)
        {
            n = new Vector3(0f, p.y < 0f ? -1f : 1f, 0f);
            q.y = n.y * (HALF.y + m);
        }
        else if (dzs <= dxs)
        {
            n = new Vector3(0f, 0f, p.z < 0f ? -1f : 1f);
            q.z = n.z * (HALF.z + m);
        }
        else
        {
            n = new Vector3(p.x < 0f ? -1f : 1f, 0f, 0f);
            q.x = n.x * (HALF.x + m);
        }
        pos = q;
        return true;
    }

    // ---- the placeholder hull: decks, mid-band slabs either side of the hangar, tapered bow and stern, engine bells, bridge
    static Material Mat(string hex, float smooth = 0.3f, float metal = 0.3f)
    {
        var m = new Material(Game.Sh("Standard"));
        m.color = Data.Hex(hex);
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", metal);
        return m;
    }

    static Material Glow(string hex)
    {
        var m = new Material(Game.Sh("Standard"));
        var c = Data.Hex(hex);
        m.color = c;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 3f);
        return m;
    }

    void Box(Vector3 size, Material mat, Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// A cylinder or cone laid along the keel (+X), `rTop` being the +X end radius; `flat` squashes it in Z.
    void CylX(float rTop, float rBottom, float length, Material mat, Vector3 at, float flat = 1f)
    {
        var go = new GameObject("Cyl");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        go.transform.localScale = new Vector3(1f, 1f, flat);
        go.AddComponent<MeshFilter>().sharedMesh = MeshUtil.ConeX(rTop, rBottom, length, 16);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    void PointLight(Vector3 at, string hex, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Data.Hex(hex);
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    public Transform model;
    public readonly System.Collections.Generic.Dictionary<string, Vector3> anchors = new System.Collections.Generic.Dictionary<string, Vector3>();

    /// Astra's carrier, or the placeholder hull. glTFast mirrors X on import, which puts the model's nose at -X; a
    /// half turn about Y brings the nose to +X (the hull is symmetric in Z, so the mouths and pads land where the
    /// frame expects them). The warm hangar lamps and the engine glows go in either way.
    public void Build()
    {
        var prefab = Resources.Load<GameObject>("Models/cargo_carrier");
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab, transform);
            go.name = "Model";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
            go.transform.localScale = Vector3.one;
            model = go.transform;
            foreach (var n in new[] { "hangar_mouth_pos", "hangar_mouth_neg", "pad_pos", "pad_neg", "drop_pad", "dish_mount", "engine_0", "engine_1", "engine_2", "drone_dock_0", "drone_dock_1", "drone_dock_2", "bridge_windows" })
            {
                var a = Ship.FindDeep(model, n);
                if (a != null) anchors[n] = transform.InverseTransformPoint(a.position);
            }
            foreach (var z in new[] { -525f, 525f }) PointLight(new Vector3(0, 130, z), "#ffc98c", 1.4f, 1150f);   // the HTML's warm hangar lamps
            for (int i = 0; i < 3; i++)
            {
                Vector3 e;
                if (!anchors.TryGetValue("engine_" + i, out e)) e = new Vector3(-3480, 0, 0);
                PointLight(e + new Vector3(-100, 0, 0), "#5ed3f0", 1.2f, 900f);
            }
            Debug.Log("carrier: model loaded, " + anchors.Count + " anchors");
            BuildDish();
            return;
        }
        BuildHull();
        BuildDish();
    }

    // ---- the mining dish on the mast: the model's own yaw and pitch rig, slewed onto rocks by the cargo ship laser upgrade
    Transform _dishYaw, _dishPitch, _dishFocus;
    Vector3 _pivot = new Vector3(-40f, 995f, 0f);      // dish_mount, carrier frame
    Vector3 _pitchOff = new Vector3(-30f, 355f, 0f);   // the pitch group's offset from the yaw pivot
    Vector3 _focusLocal = new Vector3(158f, 105f, 0f); // the beam's origin within the pitch group
    public int dishRock = -1;
    public float dishYaw, dishPitch = 0.15f;
    public bool dishFiring;
    public Vector3 dishHit;
    float _retarget;
    LineRenderer _beam;
    Transform _hitGlow;

    void BuildDish()
    {
        if (model != null)
        {
            _dishYaw = Ship.FindDeep(model, "dish_yaw");
            _dishPitch = Ship.FindDeep(model, "dish_pitch");
            _dishFocus = Ship.FindDeep(model, "focus");
            if (_dishYaw != null) _pivot = transform.InverseTransformPoint(_dishYaw.position);
            if (_dishPitch != null) _pitchOff = transform.InverseTransformPoint(_dishPitch.position) - _pivot;
            if (_dishFocus != null && _dishPitch != null) _focusLocal = transform.InverseTransformPoint(_dishFocus.position) - _pivot - _pitchOff;
        }
        var bg = new GameObject("DishBeam");
        _beam = bg.AddComponent<LineRenderer>();
        _beam.useWorldSpace = true;
        _beam.positionCount = 2;
        _beam.startWidth = 8f;
        _beam.endWidth = 5f;
        var bm = new Material(Game.Sh("Sprites/Default"));
        bm.color = new Color(1f, 0.77f, 0.4f, 0.85f);
        _beam.material = bm;
        _beam.startColor = new Color(1f, 0.8f, 0.45f);
        _beam.endColor = new Color(1f, 0.65f, 0.25f);
        _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beam.enabled = false;
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "DishHitGlow";
        var gm = new Material(Game.Sh("Standard"));
        gm.color = new Color(1f, 0.77f, 0.4f);
        gm.EnableKeyword("_EMISSION");
        gm.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f) * 3f);
        g.GetComponent<MeshRenderer>().sharedMaterial = gm;
        g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _hitGlow = g.transform;
        _hitGlow.gameObject.SetActive(false);
    }

    /// The yaw and pitch that point the dish at a carrier-local point.
    public Vector2 TurretAngles(Vector3 local)
    {
        var L = local - _pivot;
        return new Vector2(Mathf.Atan2(-L.z, L.x), Mathf.Atan2(L.y - _pitchOff.y, new Vector2(L.x, L.z).magnitude - _pitchOff.x));
    }

    /// Where the beam starts, in the carrier's frame, for a given yaw and pitch.
    public Vector3 MuzzleLocal(float yaw, float pitch)
    {
        var f = Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.forward) * _focusLocal + _pitchOff;
        return Quaternion.AngleAxis(-yaw * Mathf.Rad2Deg, Vector3.up) * f + _pivot;
    }

    static float Wrap(float a)
    {
        return Mathf.Repeat(a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
    }

    /// Reachable: within the pitch limits, and the beam from the mast top must not pass through the carrier's own hull box.
    bool InArc(Vector2 ang, Vector3 local)
    {
        if (ang.y < Data.TURRET_PITCH_MIN || ang.y > Data.TURRET_PITCH_MAX) return false;
        var origin = MuzzleLocal(ang.x, ang.y);
        var d = local - origin;
        float len = d.magnitude;
        if (len < 1e-3f) return false;
        d /= len;
        for (float s = 0f; s < Mathf.Min(len, 7000f); s += 150f)
        {
            var p = origin + d * s;
            if (Mathf.Abs(p.x) < HALF.x && Mathf.Abs(p.y) < HALF.y && Mathf.Abs(p.z) < HALF.z) return false;
        }
        return true;
    }

    /// Slew onto the nearest ore rock the dish's level can open, fire once both axes are within a degree or so, cut
    /// it, and leave its ore adrift for the collector drones (or the ship) to gather.
    public void TickDish(float dt, Belt belt)
    {
        var L = Data.DEPOT_UPGRADES["laser"].levels[State.depot["laser"]];
        _beam.enabled = false;
        _hitGlow.gameObject.SetActive(false);
        float step = Data.TURRET_SLEW * dt;
        if (L == null)
        {
            dishRock = -1;
            dishFiring = false;
            dishYaw = Wrap(dishYaw + Mathf.Clamp(Wrap(0f - dishYaw), -step, step));
            dishPitch += Mathf.Clamp(0.15f - dishPitch, -step, step);
        }
        else
        {
            float range = L.range;
            _retarget -= dt;
            int rock = dishRock;
            if (rock >= belt.count) rock = -1;   // the belt was rebuilt under it (a zone change)
            if (rock >= 0 && (!belt.alive[rock] || (belt.RockPos(rock) - truePos).magnitude > range * 1.15f)) rock = -1;
            if (rock < 0 && _retarget <= 0f)
            {
                _retarget = 0.6f;
                int best = -1;
                float bd = range * range;
                foreach (var i in belt.RocksNear(truePos, range))
                {
                    if (!belt.alive[i] || belt.ore[i] < 0 || belt.amount[i] <= 0.05f) continue;
                    if (Data.ORES[belt.ore[i]].unlock > State.depot["laser"] + 1) continue;   // the dish only works ores its own level has opened
                    float d2 = (belt.RockPos(i) - truePos).sqrMagnitude;
                    if (d2 >= bd) continue;
                    var local = ToLocalTrue(belt.RockPos(i));
                    if (!InArc(TurretAngles(local), local)) continue;
                    bd = d2;
                    best = i;
                }
                rock = best;
                dishFiring = false;
            }
            dishRock = rock;
            if (rock >= 0)
            {
                var local = ToLocalTrue(belt.RockPos(rock));
                var want = TurretAngles(local);
                if (!InArc(want, local))
                {
                    dishRock = -1;
                    dishFiring = false;
                }
                else
                {
                    dishYaw = Wrap(dishYaw + Mathf.Clamp(Wrap(want.x - dishYaw), -step, step));
                    dishPitch += Mathf.Clamp(want.y - dishPitch, -step, step);
                    float err = Mathf.Max(Mathf.Abs(Wrap(want.x - dishYaw)), Mathf.Abs(want.y - dishPitch));
                    dishFiring = !(err > (dishFiring ? 0.06f : 0.02f));
                    if (dishFiring)
                    {
                        var muzzle = ToTrue(MuzzleLocal(dishYaw, dishPitch));
                        var rp = belt.RockPos(rock);
                        dishHit = rp + (muzzle - rp).normalized * belt.radius[rock] * 0.85f;
                        belt.Damage(rock, L.rate * 5f * dt);
                        if (belt.hp[rock] <= 0f)
                        {
                            game.BreakRock(rock, true);
                            dishRock = -1;
                            dishFiring = false;
                        }
                        else
                        {
                            float flick = 1f + Mathf.Sin(State.time * 23f) * 0.12f;
                            _beam.enabled = true;
                            _beam.SetPosition(0, muzzle - game.worldOffset);
                            _beam.SetPosition(1, dishHit - game.worldOffset);
                            _hitGlow.gameObject.SetActive(true);
                            _hitGlow.position = dishHit - game.worldOffset;
                            _hitGlow.localScale = Vector3.one * 60f * flick;
                        }
                    }
                }
            }
            else
            {
                dishFiring = false;
                dishYaw = Wrap(dishYaw + Mathf.Clamp(Wrap(0f - dishYaw), -step, step));
                dishPitch += Mathf.Clamp(0.15f - dishPitch, -step, step);
            }
        }
        // the model's rig: yaw about Y and pitch about Z in the model's frame, which is the carrier frame with Z mirrored
        // (glTFast's X mirror and the half turn), so the yaw runs the other way
        if (_dishYaw != null) _dishYaw.localRotation = Quaternion.AngleAxis(-dishYaw * Mathf.Rad2Deg, Vector3.up);
        if (_dishPitch != null) _dishPitch.localRotation = Quaternion.AngleAxis(-dishPitch * Mathf.Rad2Deg, Vector3.forward);
    }

    /// How far the model's focus node sits from where the turret maths says the muzzle is, for the smoke run.
    public float DishRigError()
    {
        if (_dishFocus == null) return -1f;
        return (transform.InverseTransformPoint(_dishFocus.position) - MuzzleLocal(dishYaw, dishPitch)).magnitude;
    }

    public string DishStats()
    {
        return "rock " + dishRock + " firing " + dishFiring + " yaw " + dishYaw.ToString("0.00") + " pitch " + dishPitch.ToString("0.00") + " rig error " + DishRigError().ToString("0");
    }

    public void BuildHull()
    {
        var hull = Mat("#9aa4bf");
        var dark = Mat("#56608a");
        var metal = Mat("#b4bccf", 0.5f, 0.5f);
        var metalDark = Mat("#66709a", 0.4f, 0.4f);
        var window = Glow("#ffd9a0");
        var cyan = Glow("#5ed3f0");
        Box(new Vector3(4200, 360, 1800), hull, new Vector3(0, 360, 0));     // upper deck
        Box(new Vector3(4200, 360, 1800), hull, new Vector3(0, -360, 0));    // lower deck
        Box(new Vector3(1680, 360, 1800), hull, new Vector3(1260, 0, 0));    // mid-band slabs: the hangar is the 840-wide gap between them
        Box(new Vector3(1680, 360, 1800), hull, new Vector3(-1260, 0, 0));
        CylX(560, 900, 1500, hull, new Vector3(2850, 0, 0), 0.62f);          // bow
        CylX(70, 560, 1000, hull, new Vector3(4100, 0, 0), 0.62f);
        CylX(900, 700, 1200, hull, new Vector3(-2700, 0, 0), 0.62f);         // stern
        Box(new Vector3(80, 880, 1480), dark, new Vector3(-3320, 0, 0));
        foreach (var z in new[] { -540f, 0f, 540f })
        {
            CylX(240, 210, 420, metalDark, new Vector3(-3480, 0, z));       // engine bells
            CylX(120, 180, 60, cyan, new Vector3(-3700, 0, z));
            PointLight(new Vector3(-3800, 0, z), "#5ed3f0", 1.2f, 900f);
        }
        Box(new Vector3(700, 300, 560), metal, new Vector3(900, 690, 0));    // bridge
        Box(new Vector3(660, 26, 4), window, new Vector3(900, 760, 281));
        Box(new Vector3(660, 26, 4), window, new Vector3(900, 760, -281));
        foreach (var side in new[] { 1f, -1f })
        {
            foreach (var y in new[] { 470f, -470f }) Box(new Vector3(3900, 10, 4), window, new Vector3(0, y, side * 902f));   // window rows
            // hangar mouths: corner lights and a light inside the deck
            foreach (var x in new[] { BAY_X0 - 6f, BAY_X1 + 6f })
            {
                foreach (var y in new[] { BAY_Y0 + 14f, BAY_Y1 - 14f })
                {
                    var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(l.GetComponent<Collider>());
                    l.transform.SetParent(transform, false);
                    l.transform.localPosition = new Vector3(x, y, side * (BAY_Z_OUT + 4f));
                    l.transform.localScale = Vector3.one * 14f;
                    l.GetComponent<MeshRenderer>().sharedMaterial = Glow(y < 0f ? "#ff4a4a" : "#4aff7a");
                }
            }
            PointLight(new Vector3(0, 60, side * 450f), "#dce8ff", 0.9f, 1600f);
        }
        // the deck floor and ceiling inside the hangar, so the pad reads as a room
        Box(new Vector3(840, 12, 1800), dark, new Vector3(0, BAY_Y0 - 6f, 0));
        Box(new Vector3(840, 12, 1800), dark, new Vector3(0, BAY_Y1 + 6f, 0));
        foreach (var side in new[] { 1, -1 })
        {
            Box(new Vector3(300, 6, 300), Mat("#c8912e", 0.2f, 0.1f), ParkLocal(side) + new Vector3(0, -30, 0));
        }
    }
}

/// Small procedural meshes.
public static class MeshUtil
{
    /// A torus round Y: `rMid` to the tube centre, `tube` the tube radius.
    public static Mesh Torus(float rMid, float tube, int rings, int segs)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float a = (float)i / rings * Mathf.PI * 2f;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int j = 0; j <= segs; j++)
            {
                float b = (float)j / segs * Mathf.PI * 2f;
                float r = rMid + tube * Mathf.Cos(b);
                verts.Add(new Vector3(ca * r, tube * Mathf.Sin(b), sa * r));
            }
        }
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < segs; j++)
            {
                int a = i * (segs + 1) + j, b = a + segs + 1;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
            }
        }
        var m = new Mesh();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// A cone or cylinder along +X, centred at the origin: `rTop` at the +X end, `rBottom` at -X, capped.
    public static Mesh ConeX(float rTop, float rBottom, float length, int segments)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        float hx = length * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float cy = Mathf.Cos(a), sz = Mathf.Sin(a);
            verts.Add(new Vector3(hx, cy * rTop, sz * rTop));       // 2i
            verts.Add(new Vector3(-hx, cy * rBottom, sz * rBottom)); // 2i + 1
        }
        for (int i = 0; i < segments; i++)
        {
            int a = 2 * i, b = 2 * i + 1, c = 2 * i + 2, d = 2 * i + 3;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        int topC = verts.Count; verts.Add(new Vector3(hx, 0f, 0f));
        int botC = verts.Count; verts.Add(new Vector3(-hx, 0f, 0f));
        for (int i = 0; i < segments; i++)
        {
            tris.Add(topC); tris.Add(2 * i); tris.Add(2 * i + 2);
            tris.Add(botC); tris.Add(2 * i + 3); tris.Add(2 * i + 1);
        }
        // outward winding: flip any face whose normal points at the axis
        for (int f = 0; f < tris.Count; f += 3)
        {
            var p0 = verts[tris[f]]; var p1 = verts[tris[f + 1]]; var p2 = verts[tris[f + 2]];
            var nrm = Vector3.Cross(p1 - p0, p2 - p0);
            var centre = (p0 + p1 + p2) / 3f;
            var outward = new Vector3(Mathf.Abs(centre.x) > hx - 1e-3f ? Mathf.Sign(centre.x) : 0f, centre.y, centre.z);
            if (Vector3.Dot(nrm, outward) < 0f) { int t = tris[f + 1]; tris[f + 1] = tris[f + 2]; tris[f + 2] = t; }
        }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
