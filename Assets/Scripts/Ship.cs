using UnityEngine;

/// The player's ship: the browser game's flight model (mouse yaw and pitch, W/S throttle, A/D roll, Shift afterburner,
/// the planet's pull, drag and the speed cap, the zone edge), the mining laser, the radar pulse and the chase camera.
/// The ship is a Unity transform in scene space; true world coordinates are its position plus the floating origin.
public class Ship : MonoBehaviour
{
    public const float TURN = 30f * Mathf.Deg2Rad;   // yaw and pitch: 30 degrees a second at full deflection

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

    public Game game;
    public Belt belt;
    public Camera cam;
    Quaternion _camQ = Quaternion.identity;
    LineRenderer _laser;
    Vector3 _prevPos;
    float _nearT;
    public System.Collections.Generic.List<int> nearRocks = new System.Collections.Generic.List<int>();

    public Vector3 TruePos { get { return transform.position + game.worldOffset; } }
    public Vector3 Forward { get { return transform.forward; } }
    public float Speed { get { return vel.magnitude; } }

    public void Build()
    {
        // a placeholder hull until the model comes across: a stretched body, two wings, an engine block
        var s = Data.SHIP_SCALE;
        var hull = new Material(Game.Sh("Standard"));
        hull.color = new Color(0.78f, 0.8f, 0.84f);
        hull.SetFloat("_Metallic", 0.6f);
        hull.SetFloat("_Glossiness", 0.55f);
        Part(PrimitiveType.Capsule, new Vector3(0f, 0f, 2f) * s, new Vector3(6f, 4f, 30f) * s, Quaternion.Euler(90f, 0f, 0f), hull);
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

    public void Tick(float dt)
    {
        _nearT += dt;
        if (_nearT > 0.5f)
        {
            _nearT = 0f;
            nearRocks = belt.RocksWithin(TruePos, 12000f);
        }
        hitCd = Mathf.Max(0f, hitCd - dt);
        _prevPos = transform.position;
        Fly(dt);
        RockContact();
        TickLaser(dt);
        radarCd = Mathf.Max(0f, radarCd - dt);
        if (Input.GetKeyDown(KeyCode.R)) Radar();
        if (Input.GetKeyDown(KeyCode.G)) ToggleOvercharge();
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
        _laser.SetPosition(0, transform.position + fwd * 20f * Data.SHIP_SCALE / 3f + transform.up * -1.5f * Data.SHIP_SCALE);
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

    /// The chase camera: behind and above the ship, easing onto its heading.
    public void UpdateCamera(float dt)
    {
        float s = Data.SHIP_SCALE;
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

    /// The floating origin moved: the camera keeps its place relative to the ship.
    public void OnShift(Vector3 delta)
    {
        cam.transform.position -= delta;
        _prevPos -= delta;
    }
}
