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
