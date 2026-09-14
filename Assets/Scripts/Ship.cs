using System.Collections.Generic;
using UnityEngine;

/// The player's ship: the browser game's flight model (mouse yaw and pitch, W/S throttle, A/D roll, Shift afterburner,
/// the planet's pull, drag and the speed cap, the zone edge), the mining laser, the radar pulse, the chase camera, and
/// the cargo ship's hangar: approach control (E), the pad, and departure. The ship is a Unity transform in scene
/// space; true world coordinates are its position plus the floating origin.
public class Ship : MonoBehaviour
{
    public const float TURN = 30f * Mathf.Deg2Rad;   // yaw and pitch: 30 degrees a second at full deflection
    public const float REPAIR_RATE = 6f;
    public const float WARP_DUR = 8.6f;
    public const float WARP_LOAD_AT = 4.3f;   // the screen is black from 4.2 s to 5.4 s; the zone swaps underneath

    public Vector3 vel;
    public float throttle;
    public bool overcharge, afterburning, thrusting, braking;
    public int target = -1;
    public bool laserOn, firing;
    public float radarCd;
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
    bool _fuelDryWarned, _partsWarned;

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

    public void Build()
    {
        // a placeholder hull until the model comes across: a stretched body, two wings, an engine block
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
        // the beam
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
        float m = Mathf.Abs(v);
        const float dz = 0.06f;
        if (m < dz) return 0f;
        float t = Mathf.Min(1f, (m - dz) / (1f - dz));
        return Mathf.Sign(v) * t * (0.4f + 0.6f * t);
    }

    /// A level heading (no roll) with the nose along `d`.
    public static Quaternion LevelHeading(Vector3 d)
    {
        if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
        return Quaternion.LookRotation(d.normalized, Vector3.up);
    }

    public void Tick(float dt)
    {
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
        TickLaser(dt);
        radarCd = Mathf.Max(0f, radarCd - dt);
        if (Input.GetKeyDown(KeyCode.R)) Radar();
        if (Input.GetKeyDown(KeyCode.G)) ToggleOvercharge();
        if (Input.GetKeyDown(KeyCode.E)) StartApproach();
    }

    void Fly(float dt)
    {
        var eng = State.Stat("engine");
        // steering: the cursor's offset from screen centre yaws and pitches; A/D roll; arrow keys pitch
        float yaw = 0f, pitchUp = 0f, roll = 0f;
        if (mouseSteer)
        {
            var m = Input.mousePosition;
            float sx = (m.x - Screen.width * 0.5f) / (Screen.width * 0.5f);
            float sy = (m.y - Screen.height * 0.5f) / (Screen.height * 0.5f);
            yaw = Shape(Mathf.Clamp(sx, -1f, 1f));
            pitchUp = Shape(Mathf.Clamp(sy, -1f, 1f));
        }
        if (Input.GetKey(KeyCode.A)) roll += 1f;
        if (Input.GetKey(KeyCode.D)) roll -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) pitchUp += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) pitchUp -= 1f;
        yaw = Mathf.Clamp(yaw, -1f, 1f);
        pitchUp = Mathf.Clamp(pitchUp, -1f, 1f);
        transform.Rotate(Vector3.up, yaw * TURN * dt * Mathf.Rad2Deg, Space.Self);
        transform.Rotate(Vector3.right, -pitchUp * TURN * dt * Mathf.Rad2Deg, Space.Self);
        transform.Rotate(Vector3.forward, roll * 0.6f * dt * Mathf.Rad2Deg, Space.Self);

        // throttle: W raises, S lowers, X cuts; holding S at zero fires the retros
        if (Input.GetKey(KeyCode.W)) throttle = Mathf.Min(1f, throttle + 0.7f * dt);
        if (Input.GetKey(KeyCode.S)) throttle = Mathf.Max(0f, throttle - 0.9f * dt);
        if (Input.GetKey(KeyCode.X)) throttle = 0f;
        float abMult = State.Stat("thrusters").mult;
        afterburning = throttle > 0f && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && abMult > 1f && State.fuel > 0f;
        float mult = afterburning ? abMult : 1f;
        thrusting = false;
        braking = false;
        var fwd = Forward;
        float spBefore = vel.magnitude;
        if (State.fuel > 0f)
        {
            if (throttle > 0f)
            {
                vel += fwd * eng.thrust * throttle * mult * dt;
                State.fuel = Mathf.Max(0f, State.fuel - Data.FUEL_BURN * throttle * Data.BurnMult(mult) * dt);
                thrusting = true;
            }
            else if (Input.GetKey(KeyCode.S))
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
        float dragK = throttle > 0f ? 0.32f : 1.28f;
        vel *= Mathf.Exp(-dragK * dt);
        float sp2 = vel.magnitude;
        float lim = Mathf.Max(eng.max * mult, spBefore * Mathf.Exp(-dragK * dt));
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
                        Impact(-vn);
                        vel -= n * vn * 1.4f;
                        belt.Bump(i, -n, -vn);
                    }
                    break;
                }
            }
        }
    }

    void Impact(float speed)
    {
        if (hitCd > 0f) return;
        hitCd = 0.5f;
        float dmg = Mathf.Max(0f, speed - 140f) * 0.09f;
        shake = Mathf.Min(1f, 0.25f + speed / 400f);
        if (dmg > 0.5f)
        {
            State.hull = Mathf.Max(0f, State.hull - dmg);
            game.Toast("Collision · hull -" + Mathf.RoundToInt(dmg), true);
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
                State.hull = Mathf.Max(0f, State.hull - (-vn - 120f) * 0.08f);   // a hard knock against the hull costs plating
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
        game.Toast("Approach control has the ship · " + CargoShip.BayName(far) + " · Space skips", false);
    }

    /// W on the pad or the Depart button: approach control taxis the ship off the pad and straight out of its own
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
        game.Toast("Docked in " + CargoShip.BayName(side) + " · stow cargo from the services panel", false);
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
        // W departs, once it has been released since docking; E deposits the hold into the storage
        if (!Input.GetKey(KeyCode.W)) depWait = false;
        else if (!depWait)
        {
            depWait = true;
            StartDeparture();
        }
        if (Input.GetKeyDown(KeyCode.E)) DepositAll();
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
        if (warp == null) return 0f;
        float t = warp.t;
        if (warp.loaded) return warp.skip ? 0f : 1f - SmoothStep(WARP_LOAD_AT + 1.1f, WARP_LOAD_AT + 2.3f, t);
        return SmoothStep(WARP_LOAD_AT - 1.2f, WARP_LOAD_AT, t);
    }

    void WarpUpdate(float dt)
    {
        var W = warp;
        W.t += dt;
        if (!W.loaded && (W.skip || W.t >= WARP_LOAD_AT))
        {
            W.loaded = true;
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
        firing = autoFire || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.L) || Input.GetMouseButton(0);
        float reach = State.Stat("range").reach;
        var fwd = Forward;
        var origin = TruePos + fwd * 20f;
        target = belt.RayHit(origin, fwd, reach);
        laserOn = false;
        _laser.enabled = false;
        if (!firing) return;
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
        _laser.SetPosition(0, transform.position + fwd * 20f + transform.up * -1.5f * Data.SHIP_SCALE);
        _laser.SetPosition(1, end - game.worldOffset);
    }

    void Radar()
    {
        if (radarCd > 0f) return;
        radarCd = Data.PULSE_CD;
        float range = State.Stat("scanner").range;
        scanCount = belt.Scan(TruePos, range, -1, range / Data.PULSE_TIME, out scanNearest, out scanDist);
        if (scanCount == 0) game.Toast("Radar: no ore within " + Data.Fm(range) + " m", true);
        else game.Toast("Radar: " + scanCount + " ore rocks within " + Data.Fm(range) + " m · nearest " + belt.RockName(scanNearest) + " at " + Data.Fm(scanDist) + " m", false);
    }

    void ToggleOvercharge()
    {
        float m = State.Stat("overcharge").mult;
        if (m <= 1f)
        {
            game.Toast("No laser overcharge fitted · it is a refit in the cargo ship services", true);
            return;
        }
        overcharge = !overcharge;
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
        var f = _camQ * Vector3.forward;
        var u = _camQ * Vector3.up;
        var camPos = transform.position - f * 88f * s + u * 30f * s;
        var look = transform.position + f * 140f * s + u * 10f * s;
        if (shake > 0f)
        {
            shake = Mathf.Max(0f, shake - dt * 1.8f);
            float sh = shake * shake * 6f;
            camPos += new Vector3(Random.Range(-sh, sh), Random.Range(-sh, sh), Random.Range(-sh, sh));
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
