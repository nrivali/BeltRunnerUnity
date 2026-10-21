using System.Collections.Generic;
using UnityEngine;

/// On foot aboard the cargo ship (2026-10-21, the user's: "the ability to get out of your ship when you are in the
/// hangar and walk around", with rooms to come where different things are done, like crafting). G steps out of the
/// docked ship onto the hangar deck: a first-person walk (WASD, Shift runs, Space jumps, the mouse looks) under the
/// carrier's own gravity. The pilot lives in the carrier's frame (`_body` is a child of the carrier, so the orbit and the
/// floating origin carry it), and a CharacterController walks it against invisible walls round the hangar (the deck,
/// the bulkheads, the force fields across the mouths), the parked ship's hull and, where the model's meshes can be
/// read, the carrier's own geometry.
///
/// Things to do on board are STATIONS: a point in the carrier's frame, how near you must stand, the prompt and what E
/// does there. The ship itself is one (climb aboard), the workshop terminal in each bay another (the workshop tab of the
/// hangar window). A new room is new walls in `BuildBounds` and new entries in `BuildStations`.
public class Walker
{
    public Ship ship;
    public Game game;
    CargoShip Carrier { get { return ship.carrier; } }

    // the pilot, in metres times the ship scale (3 u to the metre, the scale the cockpit is modelled at)
    const float M = Data.SHIP_SCALE;
    public const float EYE = 1.68f * M, HEIGHT = 1.8f * M, RADIUS = 0.35f * M;
    public const float WALK = 3.2f * M, RUN = 6.5f * M, JUMP_H = 1.0f * M, GRAVITY = 9.8f * M;
    public const float LOOK = 2.0f;           // degrees per unit of the mouse axis
    public const float FADE_IN = 0.2f, FADE_OUT = 0.35f;

    public bool Active { get { return _active; } }
    bool _active;
    GameObject _body;
    CharacterController _cc;
    float _yaw, _pitch, _vy, _stepAcc, _bob, _runK;
    bool _grounded = true;
    // the fade across stepping out and climbing back in: up to the swap, then back down
    float _fadeT = -1f;
    bool _fadeOut;   // true: on the way out of the ship; false: on the way back in
    public float Fade
    {
        get
        {
            if (_fadeT < 0f) return 0f;
            return _fadeT < FADE_IN ? _fadeT / FADE_IN : 1f - Mathf.Clamp01((_fadeT - FADE_IN) / FADE_OUT);
        }
    }
    public bool Busy { get { return _fadeT >= 0f; } }
    public int boardedFrame = -1;   // the frame the pilot climbed back in: the docked keys skip it, so E does not also deposit

    // ---- the stations
    public class Station
    {
        public string id, name, verb;
        public Vector3 local;    // carrier frame, at deck height
        public float reach;      // how near the pilot must stand (on the deck, u)
        public System.Action use;
        public Transform model;
        public Material screen;
    }
    public readonly List<Station> stations = new List<Station>();
    public Station near;         // the station in reach and in front of the pilot, if any
    readonly List<Transform> _props = new List<Transform>();
    readonly List<Collider> _propCols = new List<Collider>();   // the ramps onto the pads: shown and solid only while walking
    public string propsState = "";
    public float shipHalf;   // how far the hull reaches across the flank the pilot steps out on (u)

    // ---- the walls
    Transform _bounds;
    readonly List<Collider> _carrierCols = new List<Collider>();
    readonly List<Collider> _shipCols = new List<Collider>();
    bool _carrierColsBuilt;
    public string boundsState = "";
    public float FloorY { get { return CargoShip.BAY_Y0; } }   // the collision box's floor; the model's own deck is found by GroundAt

    /// The walkable surface under a point of the hangar (carrier frame): a ray down from under the ceiling, past the ship
    /// and anything overhead, to the first floor below the pilot's head room. The model's deck is at -146 with the pads
    /// raised to -138 and a gutter at -150 along the bulkheads (2026-10-21's probe); the box floor is the fallback.
    public float GroundAt(float x, float z)
    {
        var c = Carrier;
        var top = c.transform.TransformPoint(new Vector3(x, CargoShip.BAY_Y1 - 30f, z));
        var hits = Physics.RaycastAll(top, -c.transform.up, CargoShip.BAY_Y1 - CargoShip.BAY_Y0 + 60f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider == null || _shipCols.Contains(h.collider) || _propCols.Contains(h.collider) || h.collider == _cc) continue;
            var p = c.transform.InverseTransformPoint(h.point);
            if (p.y > CargoShip.BAY_Y0 + 60f) continue;   // a light or a shelf overhead
            if (Vector3.Dot(h.normal, c.transform.up) < 0.6f) continue;   // a wall, not a floor
            return p.y;
        }
        return FloorY;
    }

    // ---- stepping out and back in
    /// G in the hangar: the fade comes down, the pilot is set on the deck beside the ship, the fade lifts.
    public void StepOut()
    {
        if (_active || Busy || Carrier == null) return;
        _fadeT = 0f;
        _fadeOut = true;
        Audio.Play("ui_open", -6f);
    }

    /// E (or G) by the ship: back into the seat behind the same fade.
    public void Board()
    {
        if (!_active || Busy) return;
        _fadeT = 0f;
        _fadeOut = false;
        Audio.Play("ui_close", -6f);
    }

    void Swap()
    {
        if (_fadeOut) Enter();
        else Leave();
    }

    void Enter()
    {
        var c = Carrier;
        if (_body == null)
        {
            _body = new GameObject("Pilot");
            _cc = _body.AddComponent<CharacterController>();
            _cc.height = HEIGHT;
            _cc.radius = RADIUS;
            _cc.center = new Vector3(0f, HEIGHT * 0.5f, 0f);
            _cc.stepOffset = 0.85f * M;   // the step onto a pad's rim is 2 u
            _cc.slopeLimit = 45f;
            _cc.skinWidth = 0.05f * M;
            _cc.minMoveDistance = 0f;
        }
        _body.transform.SetParent(c.transform, false);
        _body.transform.localRotation = Quaternion.identity;
        if (_bounds == null) BuildBounds();
        _bounds.gameObject.SetActive(true);
        BuildCarrierColliders();
        foreach (var col in _carrierCols) if (col != null) col.enabled = true;
        Physics.SyncTransforms();
        if (stations.Count == 0) BuildStations();
        foreach (var s in stations) if (s.model != null) s.model.gameObject.SetActive(true);
        foreach (var p in _props) if (p != null) p.gameObject.SetActive(true);
        Physics.SyncTransforms();
        _active = true;   // before the ship's pose, so at the colony the ship is set on the pad
        ship.PlaceForWalk();
        Physics.SyncTransforms();
        BuildShipColliders();
        UpdateShipStation();
        foreach (var s in stations) if (s.id == "ship") s.reach = Mathf.Max(Data.SHIP_R, ShipHalfAll()) + 10f * M;
        // beside the ship: off its right flank by its radius and a little more, a few metres ahead, facing it
        var park = c.ToLocalTrue(ship.TruePos);
        var face = Quaternion.Inverse(c.basisQ) * ship.Forward;
        face.y = 0f;
        if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
        face.Normalize();
        var right = Vector3.Cross(Vector3.up, face);
        // the hull's own extent across that flank (the model is wider than its 48 u collision sphere): out past it by 5 m
        float half = Data.SHIP_R;
        foreach (var col in _shipCols)
        {
            if (col == null) continue;
            var b = col.bounds; var bc = b.center; var be = b.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var p = c.transform.InverseTransformPoint(bc + new Vector3((i & 1) != 0 ? be.x : -be.x, (i & 2) != 0 ? be.y : -be.y, (i & 4) != 0 ? be.z : -be.z)) - park;
                half = Mathf.Max(half, Vector3.Dot(p, right));
            }
        }
        shipHalf = half;
        var at = park + right * (half + 5f * M) + face * (4f * M);
        at.x = Mathf.Clamp(at.x, CargoShip.BAY_X0 + 10f, CargoShip.BAY_X1 - 10f);
        at.z = Mathf.Clamp(at.z, -CargoShip.BAY_Z_OUT + 30f, CargoShip.BAY_Z_OUT - 30f);
        Physics.SyncTransforms();
        at.y = GroundAt(at.x, at.z) + 0.3f;
        SetFeet(at);
        var look = park - at;
        _yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        _pitch = 6f;
        _vy = 0f;
        _grounded = true;
        _stepAcc = 0f;
        _bob = 0f;
        _runK = 0f;
        if (game.hud != null && game.hud.InvOpen) game.hud.CloseWindow();
        game.Toast("On the hangar deck · WASD walk · Shift run · E by your ship climbs back aboard", false);
        Debug.Log("walk: out on the deck at " + at.ToString("0") + " · ship at " + park.ToString("0") + " · " + boundsState + " · ship colliders " + _shipCols.Count + " reaching " + shipHalf.ToString("0") + " u across" + " · stations " + stations.Count + " ·" + propsState);
    }

    void Leave()
    {
        _active = false;
        boardedFrame = Time.frameCount;
        foreach (var col in _shipCols) if (col != null) Object.Destroy(col);
        _shipCols.Clear();
        foreach (var col in _carrierCols) if (col != null) col.enabled = false;
        if (_bounds != null) _bounds.gameObject.SetActive(false);
        foreach (var s in stations) if (s.model != null) s.model.gameObject.SetActive(false);
        foreach (var p in _props) if (p != null) p.gameObject.SetActive(false);
        if (game.hud != null && game.hud.InvOpen) game.hud.CloseWindow();
        near = null;
        Audio.Play("dock", -8f);
        game.Toast("Aboard · G steps out onto the hangar deck", false);
    }

    void SetFeet(Vector3 local)
    {
        _cc.enabled = false;
        _body.transform.localPosition = local;
        _cc.enabled = true;
        Physics.SyncTransforms();
    }

    public Vector3 FeetLocal { get { return _body != null ? _body.transform.localPosition : Vector3.zero; } }
    public Vector3 EyeLocal { get { return FeetLocal + new Vector3(0f, EYE + _bob, 0f); } }
    public Quaternion LookLocal { get { return Quaternion.Euler(_pitch, _yaw, 0f); } }

    /// For a test: turn the head to a yaw and pitch (degrees, carrier frame).
    public void LookAt(float yaw, float pitch) { _yaw = yaw; _pitch = pitch; }
    public float Yaw { get { return _yaw; } }
    public float testSpeed = 1f;   // a test: the walk sped up so it covers the deck inside the run
    public void FaceStation(string id)
    {
        Station best = null; float bd = float.MaxValue;
        foreach (var s in stations) { if (s.id != id) continue; float d = (s.local - FeetLocal).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
        if (best == null) return;
        var to = best.local - FeetLocal;
        _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        _pitch = 8f;
    }

    /// Called every frame while docked, before the docked keys: the fade, the walk and the stations.
    public void Tick(float dt, Vector3 testWish)
    {
        if (_fadeT >= 0f)
        {
            float before = _fadeT;
            _fadeT += dt;
            if (before < FADE_IN && _fadeT >= FADE_IN) Swap();
            if (_fadeT >= FADE_IN + FADE_OUT) _fadeT = -1f;
        }
        if (!_active) return;
        var hud = game.hud;
        bool ui = hud != null && (hud.InvOpen || hud.MapOpen || hud.MenuVisible);
        bool free = !ui && !Busy;
        // the look: the mouse turns the head (yaw about the deck's up, pitch held short of straight up and down)
        if (free)
        {
            _yaw += Input.GetAxisRaw("Mouse X") * LOOK;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * LOOK, -85f, 85f);
        }
        // the walk: WASD (or the arrows) along the deck, Shift runs, Space jumps
        var wish = testWish;
        if (free)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) wish.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) wish.z -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) wish.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) wish.x -= 1f;
        }
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        bool run = free && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        _runK = Mathf.MoveTowards(_runK, run && wish.sqrMagnitude > 0.01f ? 1f : 0f, dt * 3f);
        float speed = Mathf.Lerp(WALK, RUN, _runK) * testSpeed;
        var flat = Quaternion.Euler(0f, _yaw, 0f) * wish * speed;
        if (_grounded)
        {
            _vy = -2f * M;   // pressed to the deck so the controller stays grounded over a step down
            if (free && Input.GetKeyDown(KeyCode.Space)) { _vy = Mathf.Sqrt(2f * GRAVITY * JUMP_H); _grounded = false; }
        }
        else _vy -= GRAVITY * dt;
        // the move happens in the carrier's frame: the body is its child, so the frame's directions carry it
        var moveLocal = (flat + Vector3.up * _vy) * dt;
        var moveWorld = Carrier.transform.TransformDirection(moveLocal);
        Physics.SyncTransforms();   // the carrier moved this frame (its orbit, the floating origin): the walls go with it
        var before2 = FeetLocal;
        var flags = _cc.Move(moveWorld);
        bool wasGrounded = _grounded;
        _grounded = (flags & CollisionFlags.Below) != 0;
        if ((flags & CollisionFlags.Above) != 0 && _vy > 0f) _vy = 0f;
        // a fall through everything (a gap in the walls) puts the pilot back on the deck by the ship
        if (FeetLocal.y < FloorY - 40f) { var z0 = Carrier.ToLocalTrue(ship.TruePos).z; SetFeet(new Vector3(-180f, GroundAt(-180f, z0) + 0.3f, z0)); _vy = 0f; }
        // the footfalls: one per stride of deck covered, a heavier one on landing; the head bobs with them
        var moved = FeetLocal - before2;
        moved.y = 0f;
        float stride = Mathf.Lerp(1.5f, 2.2f, _runK) * M;
        if (_grounded)
        {
            _stepAcc += moved.magnitude;
            if (_stepAcc >= stride) { _stepAcc -= stride; Audio.Step(Mathf.Lerp(-3f, 0f, _runK)); }
            if (!wasGrounded) { Audio.Step(2f); _stepAcc = 0f; }
            bool moving = moved.sqrMagnitude > 1e-4f;
            float want = moving ? -Mathf.Abs(Mathf.Sin(_stepAcc / stride * Mathf.PI)) * Mathf.Lerp(0.04f, 0.08f, _runK) * M : 0f;
            _bob = Mathf.Lerp(_bob, want, 1f - Mathf.Exp(-14f * dt));
        }
        else _bob = Mathf.Lerp(_bob, 0f, 1f - Mathf.Exp(-8f * dt));
        // the stations: the nearest in reach that is roughly in front of the pilot
        UpdateShipStation();
        near = null;
        float best = float.MaxValue;
        var feet = FeetLocal;
        var fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        foreach (var s in stations)
        {
            var to = s.local - feet;
            to.y = 0f;
            float d = to.magnitude;
            if (d > s.reach || d >= best) continue;
            if (d > 2f * M && Vector3.Dot(to / d, fwd) < 0.2f) continue;   // behind the pilot: walked past without the prompt
            best = d;
            near = s;
        }
        if (free && near != null && (Input.GetKeyDown(KeyCode.E) || (near.id == "ship" && Input.GetKeyDown(KeyCode.G))))
        {
            Audio.Play("ui_click", -4f);
            near.use();
        }
        else if (free && Input.GetKeyDown(KeyCode.G)) game.Toast("Walk back to your ship to climb aboard", false);
        // the stations' screens breathe, brighter for the one in reach
        float t = Time.time;
        foreach (var s in stations)
            if (s.screen != null) s.screen.SetColor("_EmissionColor", Data.Hex("#5ed3f0") * (1.4f + 0.4f * Mathf.Sin(t * 2.3f) + (s == near ? 1.4f : 0f)));
    }

    /// The camera at the pilot's eye.
    public void Camera(Camera cam, float dt)
    {
        var c = Carrier;
        cam.transform.position = c.transform.TransformPoint(EyeLocal);
        cam.transform.rotation = c.transform.rotation * LookLocal;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 72f + 4f * _runK, 1f - Mathf.Exp(-6f * dt));
    }

    /// A test's look round: what the carrier's and the ship's meshes are (in the carrier's frame), and what a ray from
    /// the eye meets in each direction.
    public void Probe()
    {
        var c = Carrier;
        foreach (var mf in c.model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh; if (mesh == null) continue;
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = -lo;
            var bc = mesh.bounds.center; var be = mesh.bounds.size * 0.5f;
            for (int i = 0; i < 8; i++) { var p = c.transform.InverseTransformPoint(mf.transform.TransformPoint(bc + new Vector3((i & 1) != 0 ? be.x : -be.x, (i & 2) != 0 ? be.y : -be.y, (i & 4) != 0 ? be.z : -be.z))); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            var mr = mf.GetComponent<MeshRenderer>();
            Debug.Log("probe carrier mesh " + mf.name + " · " + mesh.vertexCount + " verts · local " + lo.ToString("0") + " .. " + hi.ToString("0") + " · active " + mf.gameObject.activeInHierarchy + " · renderer " + (mr != null && mr.enabled) + " · mat " + (mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "-"));
        }
        foreach (var kv in c.anchors) Debug.Log("probe anchor " + kv.Key + " " + kv.Value.ToString("0"));
        Debug.Log("probe ship model " + (ship.model != null ? ship.model.gameObject.activeInHierarchy + " layer " + ship.model.gameObject.layer : "none") + " · ship local " + c.ToLocalTrue(ship.TruePos).ToString("0") + " · cam mask " + ship.cam.cullingMask + " · cam local " + c.transform.InverseTransformPoint(ship.cam.transform.position).ToString("0.0"));
        foreach (var px in new[] { -400f, -250f, -140f, -100f, -60f, 0f, 60f, 140f, 250f, 400f })
            foreach (var pz in new[] { -850f, -700f, -525f, -350f, 0f, 350f, 525f })
            {
                var top = c.transform.TransformPoint(new Vector3(px, 170f, pz));
                var sb = new System.Text.StringBuilder();
                var hits = Physics.RaycastAll(top, -c.transform.up, 400f);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits) sb.Append(" ").Append(h.collider.name).Append("@").Append(c.transform.InverseTransformPoint(h.point).y.ToString("0"));
                Debug.Log("probe column x " + px + " z " + pz + ":" + sb);
            }
        var eye = c.transform.TransformPoint(EyeLocal);
        var knee = c.transform.TransformPoint(FeetLocal + Vector3.up * 1.5f);
        for (int k = 0; k < 8; k++)
        {
            var dw = c.transform.TransformDirection(Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward);
            foreach (var h in Physics.RaycastAll(knee, dw, 400f)) if (h.collider.name != "Pilot") Debug.Log("probe knee ray " + (k * 45) + " deg hits " + h.collider.name + " at " + h.distance.ToString("0.0") + " u · local " + c.transform.InverseTransformPoint(h.point).ToString("0.0"));
        }
        foreach (var h in Physics.RaycastAll(eye, -c.transform.up, 400f)) Debug.Log("probe down hits " + h.collider.name + " at " + h.distance.ToString("0.0") + " · local y " + c.transform.InverseTransformPoint(h.point).y.ToString("0.0"));
        for (int k = 0; k < 10; k++)
        {
            Vector3 d = k < 8 ? Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward : (k == 8 ? Vector3.up : Vector3.down);
            RaycastHit h;
            var dw = c.transform.TransformDirection(d);
            if (Physics.Raycast(eye, dw, out h, 5000f)) Debug.Log("probe ray " + d.ToString("0.0") + " hits " + h.collider.name + " at " + h.distance.ToString("0.0") + " u");
            else Debug.Log("probe ray " + d.ToString("0.0") + " hits nothing");
        }
    }

    // ---- the walls: the deck, the bulkheads either side, the ceiling and the force fields across the mouths
    void BuildBounds()
    {
        var root = new GameObject("WalkBounds");
        root.transform.SetParent(Carrier.transform, false);
        _bounds = root.transform;
        float x1 = CargoShip.BAY_X1, y0 = CargoShip.BAY_Y0, y1 = CargoShip.BAY_Y1, z1 = CargoShip.BAY_Z_OUT;
        float t = 40f;
        Wall("Deck", new Vector3(0f, y0 - 20f - t * 0.5f, 0f), new Vector3(2f * x1 + 2f * t, t, 2f * z1 + 2f * t));
        Wall("Ceiling", new Vector3(0f, y1 + t * 0.5f, 0f), new Vector3(2f * x1 + 2f * t, t, 2f * z1 + 2f * t));
        Wall("Bulkhead +x", new Vector3(x1 + t * 0.5f, (y0 + y1) * 0.5f, 0f), new Vector3(t, y1 - y0 + 2f * t, 2f * z1 + 2f * t));
        Wall("Bulkhead -x", new Vector3(-x1 - t * 0.5f, (y0 + y1) * 0.5f, 0f), new Vector3(t, y1 - y0 + 2f * t, 2f * z1 + 2f * t));
        // the force fields hold the air in, and the pilot: a few metres short of each mouth
        float zf = z1 - 6f * M;
        Wall("Field +z", new Vector3(0f, (y0 + y1) * 0.5f, zf + t * 0.5f), new Vector3(2f * x1 + 2f * t, y1 - y0 + 2f * t, t));
        Wall("Field -z", new Vector3(0f, (y0 + y1) * 0.5f, -zf - t * 0.5f), new Vector3(2f * x1 + 2f * t, y1 - y0 + 2f * t, t));
        root.SetActive(false);
    }

    void Wall(string name, Vector3 at, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_bounds, false);
        go.transform.localPosition = at;
        var b = go.AddComponent<BoxCollider>();
        b.size = size;
    }

    /// The carrier's own meshes in and round the hangar, as mesh colliders where the imported meshes can be read; built
    /// once and switched on only while walking.
    void BuildCarrierColliders()
    {
        if (_carrierColsBuilt) return;
        _carrierColsBuilt = true;
        var c = Carrier;
        if (c.model == null) { boundsState = "walls: boxes only (placeholder hull)"; return; }
        int used = 0, unreadable = 0, skipped = 0;
        float t0 = Time.realtimeSinceStartup;
        foreach (var mf in c.model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            if (!NearHangar(mf.transform, mesh.bounds)) { skipped++; continue; }
            if (!mesh.isReadable) { unreadable++; continue; }
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.enabled = false;
            _carrierCols.Add(mc);
            used++;
        }
        boundsState = "walls: boxes + " + used + " carrier meshes (" + unreadable + " unreadable, " + skipped + " away from the hangar) in " + Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " ms";
    }

    bool NearHangar(Transform t, Bounds b)
    {
        var c = Carrier;
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = -lo;
        var bc = b.center; var be = b.size * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            var corner = bc + new Vector3((i & 1) != 0 ? be.x : -be.x, (i & 2) != 0 ? be.y : -be.y, (i & 4) != 0 ? be.z : -be.z);
            var p = c.transform.InverseTransformPoint(t.TransformPoint(corner));
            lo = Vector3.Min(lo, p);
            hi = Vector3.Max(hi, p);
        }
        float m = 60f;
        return hi.x > CargoShip.BAY_X0 - m && lo.x < CargoShip.BAY_X1 + m && hi.y > CargoShip.BAY_Y0 - m && lo.y < CargoShip.BAY_Y1 + m && hi.z > -CargoShip.BAY_Z_OUT - m && lo.z < CargoShip.BAY_Z_OUT + m;
    }

    /// The hull's widest reach from its centre on the deck, any direction (u).
    float ShipHalfAll()
    {
        var c = Carrier;
        var park = c.ToLocalTrue(ship.TruePos);
        float r = 0f;
        foreach (var col in _shipCols)
        {
            if (col == null) continue;
            var b = col.bounds; var bc = b.center; var be = b.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var p = c.transform.InverseTransformPoint(bc + new Vector3((i & 1) != 0 ? be.x : -be.x, (i & 2) != 0 ? be.y : -be.y, (i & 4) != 0 ? be.z : -be.z)) - park;
                p.y = 0f;
                r = Mathf.Max(r, p.magnitude);
            }
        }
        return r;
    }

    /// The parked ship's hull: a collider on each visible part (its mesh when it can be read, its box otherwise).
    void BuildShipColliders()
    {
        foreach (var col in _shipCols) if (col != null) Object.Destroy(col);
        _shipCols.Clear();
        var root = ship.model != null ? ship.model : ship.transform;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || mf.gameObject.layer == Ship.COCKPIT_LAYER) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) continue;
            string n = mf.gameObject.name;
            if (n.StartsWith("Exhaust") || n.StartsWith("NavLight") || n.StartsWith("Glow") || n.StartsWith("Beam")) continue;
            if (mesh.isReadable)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                _shipCols.Add(mc);
            }
            else
            {
                var bc = mf.gameObject.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center;
                bc.size = mesh.bounds.size;
                _shipCols.Add(bc);
            }
        }
        Physics.SyncTransforms();
    }

    // ---- the stations
    void BuildStations()
    {
        stations.Add(new Station { id = "ship", name = "your ship", verb = "Climb aboard", reach = Data.SHIP_R + 16f * M, use = Board });   // widened to the hull's reach once it is measured (Enter)
        // the workshop terminal: against the bulkhead across from the pad, in each bay
        foreach (int side in new[] { 1, -1 })
        {
            float tx = -250f, tz = CargoShip.ParkLocal(side).z;   // on the deck, a short walk off the pad's edge (x 140) toward the -x bulkhead
            var at = new Vector3(tx, GroundAt(tx, tz), tz);
            var st = new Station { id = "workshop", name = "Workshop terminal", verb = "Use the workshop · make things from your ore", local = at + new Vector3(1.5f * M, 0f, 0f), reach = 3.5f * M, use = () => { if (game.hud != null) game.hud.OpenTab("fab"); } };
            st.model = BuildTerminal(at, "Workshop terminal " + side, out st.screen);
            stations.Add(st);
            // the pad stands 8 u (2.7 m) over the deck, more than a step: a ramp down each side
            BuildRamp(side, -1);
            BuildRamp(side, 1);
        }
    }

    /// A way off a pad's edge on the side `dir` (-1 toward -x, +1 toward +x). The pad is ringed by a glowing rim a little
    /// over a metre high (the model's "emissive" strips, 2026-10-21's probe: pad -138, rim -134 from x 142 to 150, deck
    /// -146 beyond): a step up onto the rim from the pad, and a ramp from the rim's outer edge down to the deck. Found by
    /// walking the floor out from the pad's middle, so a remodelled pad still gets its way down.
    void BuildRamp(int side, int dir)
    {
        float z = CargoShip.ParkLocal(side).z;
        float padY = GroundAt(0f, z);
        // out to where the floor drops to the deck, then back in across the rim to the pad
        float rimIn = -1f, rimOut = -1f, rimY = padY, deckY = padY;
        for (float x = 60f; x < CargoShip.BAY_X1 - 40f; x += 1f)
        {
            float y = GroundAt(dir * x, z);
            if (y < padY - 3f) { rimOut = x - 0.5f; deckY = y; break; }
        }
        if (rimOut < 0f) { propsState += " no pad edge at z " + z + " dir " + dir + ";"; return; }
        rimIn = rimOut;
        for (float x = rimOut - 0.5f; x > rimOut - 30f; x -= 1f)
        {
            float y = GroundAt(dir * x, z);
            if (y <= padY + 1f) break;
            rimIn = x - 0.5f;
            rimY = Mathf.Max(rimY, y);
        }
        float width = 12f * M;
        var body = new Material(Game.Sh("Standard")); body.color = Data.Hex("#5a6385"); body.SetFloat("_Glossiness", 0.35f); body.SetFloat("_Metallic", 0.55f);
        var trim = new Material(Game.Sh("Standard")); trim.color = Data.Hex("#c8912e");
        // the step: from the pad up toward the rim, half its height, 2 m deep
        if (rimY > padY + 1f)
        {
            float sh = (rimY - padY) * 0.5f, sd = 2f * M;
            var step = Block("Step " + side + " " + dir, new Vector3(dir * (rimIn - sd * 0.5f), padY + sh * 0.5f, z), new Vector3(sd, sh, width), Quaternion.identity, body);
            Stripe(step, trim);
            _props.Add(step);
        }
        // the ramp: from the rim's top at its outer edge down to the deck at about 22 degrees
        float rise = rimY - deckY, run = rise * 2.5f, thick = 1.5f;
        float len = Mathf.Sqrt(run * run + rise * rise);
        float ang = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        var q = Quaternion.AngleAxis(dir * -ang, Vector3.forward);
        var mid = new Vector3(dir * (rimOut + run * 0.5f), (rimY + deckY) * 0.5f, z);
        var ramp = Block("Ramp " + side + " " + dir, mid - q * Vector3.up * (thick * 0.5f), new Vector3(len + 1f, thick, width), q, body);
        Stripe(ramp, trim);
        _props.Add(ramp);
        // and a deck plate over the rim between them, so the rim is walked across, not climbed
        var plate = Block("Plate " + side + " " + dir, new Vector3(dir * ((rimIn + rimOut) * 0.5f), rimY + 0.05f, z), new Vector3(rimOut - rimIn + 0.5f, 0.1f, width), Quaternion.identity, body);
        _props.Add(plate);
        propsState += " ramp z " + z + " dir " + dir + " pad " + padY.ToString("0") + " rim " + rimY.ToString("0") + " " + rimIn.ToString("0") + ".." + rimOut.ToString("0") + " deck " + deckY.ToString("0") + ";";
    }

    Transform Block(string name, Vector3 at, Vector3 size, Quaternion q, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // its BoxCollider is the surface
        go.name = name;
        go.transform.SetParent(Carrier.transform, false);
        go.transform.localPosition = at;
        go.transform.localRotation = q;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        _propCols.Add(go.GetComponent<Collider>());
        go.SetActive(false);
        return go.transform;
    }

    /// Amber edge stripes along both long sides of a block.
    static void Stripe(Transform block, Material trim)
    {
        foreach (float s2 in new[] { -1f, 1f })
        {
            var st = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(st.GetComponent<Collider>());
            st.transform.SetParent(block, false);
            st.transform.localPosition = new Vector3(0f, 0.52f, s2 * 0.47f);
            st.transform.localScale = new Vector3(1f, 0.06f, 0.04f);
            st.GetComponent<MeshRenderer>().sharedMaterial = trim;
        }
    }

    /// The ship's station is wherever the ship is parked.
    void UpdateShipStation()
    {
        foreach (var s in stations) if (s.id == "ship") { var p = Carrier.ToLocalTrue(ship.TruePos); s.local = new Vector3(p.x, FloorY, p.z); }
    }

    /// A console on the deck: a pedestal, a sloped desk, a mast and a glowing screen, its front facing +x (into the hangar).
    Transform BuildTerminal(Vector3 at, string name, out Material screen)
    {
        var root = new GameObject(name).transform;
        root.SetParent(Carrier.transform, false);
        root.localPosition = at;
        root.localRotation = Quaternion.AngleAxis(90f, Vector3.up);   // its front (+z) turned to face +x
        var body = new Material(Game.Sh("Standard")); body.color = Data.Hex("#4a5378"); body.SetFloat("_Glossiness", 0.45f); body.SetFloat("_Metallic", 0.6f);
        var trim = new Material(Game.Sh("Standard")); trim.color = Data.Hex("#c8912e"); trim.SetFloat("_Glossiness", 0.3f); trim.SetFloat("_Metallic", 0.2f);
        screen = new Material(Game.Sh("Standard")); screen.color = Data.Hex("#0b2733"); screen.EnableKeyword("_EMISSION"); screen.SetColor("_EmissionColor", Data.Hex("#5ed3f0") * 2f);
        Part(root, "Pedestal", new Vector3(0f, 0.5f * M, 0f), new Vector3(0.9f * M, 1.0f * M, 0.6f * M), Quaternion.identity, body);
        Part(root, "Desk", new Vector3(0f, 1.05f * M, 0.12f * M), new Vector3(1.3f * M, 0.1f * M, 0.8f * M), Quaternion.AngleAxis(20f, Vector3.right), body);
        Part(root, "Trim", new Vector3(0f, 0.04f * M, 0f), new Vector3(1.1f * M, 0.08f * M, 0.8f * M), Quaternion.identity, trim);
        Part(root, "Mast", new Vector3(0f, 1.45f * M, -0.25f * M), new Vector3(0.12f * M, 0.8f * M, 0.12f * M), Quaternion.identity, body);
        Part(root, "Screen", new Vector3(0f, 1.8f * M, -0.2f * M), new Vector3(1.2f * M, 0.7f * M, 0.05f * M), Quaternion.AngleAxis(-8f, Vector3.right), screen);
        var lg = new GameObject("Light");
        lg.transform.SetParent(root, false);
        lg.transform.localPosition = new Vector3(0f, 1.8f * M, 0.8f * M);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = Data.Hex("#5ed3f0"); l.intensity = 1.2f; l.range = 9f * M; l.shadows = LightShadows.None;
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f * M, 0f);
        box.size = new Vector3(1.3f * M, 1.2f * M, 0.9f * M);
        root.gameObject.SetActive(false);
        return root;
    }

    static void Part(Transform root, string name, Vector3 at, Vector3 size, Quaternion q, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localPosition = at;
        go.transform.localRotation = q;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }
}
