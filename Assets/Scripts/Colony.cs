using System.Collections.Generic;
using UnityEngine;

/// Meridian Colony, at the Hub's origin: two boxy habitat rings joined by spokes round a central hub sphere and core
/// shaft, pads at both ends of the core, four solar wings, and the cargo terminals above and below the ring plane.
/// A simplified port of buildColonyAt in belt-runner-3d.html (via the Godot port): the same proportions with plain
/// meshes in place of the merged detail. The ring assembly turns slowly; the hub and terminals never move.
public class Colony : MonoBehaviour
{
    Transform _ring;
    readonly List<MeshRenderer> _beacons = new List<MeshRenderer>();
    float _t;

    public void Tick(float dt)
    {
        _t += dt;
        _ring.Rotate(Vector3.up, 0.012f * dt * Mathf.Rad2Deg, Space.Self);
        int phase = (int)(_t * 1.6f) % 3;
        for (int i = 0; i < _beacons.Count; i++)
        {
            _beacons[i].enabled = (i % 3) == phase || (i % 3) == (phase + 1) % 3;
        }
    }

    static Material Mat(string hex, float smooth = 0.2f, float metal = 0.2f)
    {
        var m = new Material(Game.Sh("Standard"));
        m.color = Data.Hex(hex);
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", metal);
        return m;
    }

    static Material Glow(string hex, float energy = 3f)
    {
        var m = new Material(Game.Sh("Standard"));
        var c = Data.Hex(hex);
        m.color = c;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * energy);
        return m;
    }

    MeshRenderer Place(Transform parent, Mesh mesh, Material mat, Vector3 at, Vector3 rotDeg, Vector3 scale)
    {
        var go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localEulerAngles = rotDeg;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    MeshRenderer Box(Transform parent, Vector3 size, Material mat, Vector3 at, float yawDeg = 0f, float pitchDeg = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localEulerAngles = new Vector3(pitchDeg, yawDeg, 0f);
        go.transform.localScale = size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    /// A cylinder standing on Y: `rTop` at +Y, `rBottom` at -Y.
    MeshRenderer CylY(Transform parent, float rTop, float rBottom, float height, Material mat, Vector3 at, Vector3 rotDeg)
    {
        // ConeX lies along +X; rotating -90 about Z stands it up (its +X becomes +Y)
        var go = new GameObject("CylY");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localEulerAngles = rotDeg;
        var inner = new GameObject("Mesh");
        inner.transform.SetParent(go.transform, false);
        inner.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        inner.AddComponent<MeshFilter>().sharedMesh = MeshUtil.ConeX(rTop, rBottom, height, 24);
        var mr = inner.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    /// A boxy ring approximated by a flattened torus: the same footprint and thickness.
    void RingMesh(Transform parent, float rMid, float halfW, float height, Material mat)
    {
        Place(parent, MeshUtil.Torus(rMid, halfW, 96, 12), mat, Vector3.zero, Vector3.zero, new Vector3(1f, height / (2f * halfW), 1f));
    }

    void Beacon(Transform parent, Material mat, Vector3 at, float r)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = Vector3.one * r * 2f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        _beacons.Add(mr);
    }

    void PointLight(Vector3 at, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Data.Hex("#dce8ff");
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    public void Build()
    {
        var hull = Mat("#b8c0d4");
        var dark = Mat("#5a6488");
        var plate = Mat("#8a94b4");
        var warm = Glow("#ffd9a0", 2f);
        var green = Glow("#6bd69a");
        var red = Glow("#ff5a5a");
        var cyan = Glow("#8fe8ff");
        var solar = Mat("#16234a", 0.6f, 0.6f);
        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(transform, false);
        _ring = ringGo.transform;
        // the outer habitat ring with 24 modules on its outer wall and a beacon pylon at every third one
        float R = Data.COL_R, ringW = Data.COL_RING_W, ringH = Data.COL_RING_H;
        RingMesh(_ring, R, ringW, ringH, hull);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Box(_ring, new Vector3(1400, 2000, 3000), plate, n * (R + ringW + 700f), -a * Mathf.Rad2Deg);
            Box(_ring, new Vector3(60, 260, 2200), warm, n * (R + ringW + 1430f) + new Vector3(0, -500, 0), -a * Mathf.Rad2Deg);
            if (i % 3 == 0)
            {
                CylY(_ring, 160f, 220f, 2400f, dark, n * (R + 1200f) + new Vector3(0, ringH / 2f + 1200f, 0), Vector3.zero);
                Beacon(_ring, red, n * (R + 1200f) + new Vector3(0, ringH / 2f + 2600f, 0), 240f);
            }
            Beacon(_ring, i % 2 == 1 ? green : warm, n * (R + ringW + 60f), 200f);
        }
        // the inner ring, smaller, with twelve modules and a cyan band
        float R2 = Data.COL_R2, ring2W = Data.COL_RING2_W, ring2H = Data.COL_RING2_H;
        RingMesh(_ring, R2, ring2W, ring2H, plate);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f + Mathf.PI / 12f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Box(_ring, new Vector3(900, 1300, 1800), hull, n * (R2 + ring2W + 450f), -a * Mathf.Rad2Deg);
        }
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Box(_ring, new Vector3(60, 220, 2400), cyan, n * (R2 + ring2W + 30f), -a * Mathf.Rad2Deg);
        }
        // six spokes: hub to the inner ring, inner ring to the outer ring, with a lit lift car on each outer spoke
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            foreach (var seg in new[] { new[] { Data.COL_HUB - 500f, R2 - ring2W + 300f }, new[] { R2 + ring2W - 300f, R - ringW + 300f } })
            {
                float r0 = seg[0], r1 = seg[1];
                // a cylinder along the spoke: ConeX runs along +X, yawed to point along n
                var go = new GameObject("Spoke");
                go.transform.SetParent(_ring, false);
                go.transform.localPosition = n * (r0 + r1) * 0.5f;
                go.transform.localRotation = Quaternion.LookRotation(n, Vector3.up) * Quaternion.AngleAxis(-90f, Vector3.up);
                go.AddComponent<MeshFilter>().sharedMesh = MeshUtil.ConeX(520f, 520f, r1 - r0, 16);
                go.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }
            Box(_ring, new Vector3(1100, 1100, 1100), warm, n * (R2 + ring2W + (R - ringW - R2 - ring2W) * 0.45f));
        }
        // the hub sphere with an equatorial walkway, the core shaft through it, pads at both ends of the core
        float hubR = Data.COL_HUB;
        var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(sph.GetComponent<Collider>());
        sph.transform.SetParent(transform, false);
        sph.transform.localScale = Vector3.one * hubR * 2f;
        sph.GetComponent<MeshRenderer>().sharedMaterial = hull;
        Place(transform, MeshUtil.Torus(hubR + 180f, 260f, 96, 12), dark, Vector3.zero, Vector3.zero, Vector3.one);
        CylY(transform, Data.COL_CORE_R, Data.COL_CORE_R, Data.COL_CORE_H * 2f + 12000f, plate, Vector3.zero, Vector3.zero);
        foreach (var y in new[] { -19500f, -7500f, 7500f, 19500f })
        {
            Place(transform, MeshUtil.Torus(Data.COL_CORE_R + 175f, 225f, 48, 10), dark, new Vector3(0, y, 0), Vector3.zero, Vector3.one);
        }
        foreach (var s in new[] { 1f, -1f })
        {
            float y = s * Data.COL_PAD_Y;
            CylY(transform, Data.COL_PAD_R, Data.COL_PAD_R, 500f, hull, new Vector3(0, y, 0), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Beacon(transform, i % 2 == 1 ? red : green, new Vector3(Mathf.Cos(a) * (Data.COL_PAD_R - 500f), y + s * 380f, Mathf.Sin(a) * (Data.COL_PAD_R - 500f)), 160f);
            }
            // the docking arm beyond the pad, ending in a plate
            CylY(transform, 300f, 300f, 6000f, dark, new Vector3(0, s * (Data.COL_CORE_H + 3000f), 0), Vector3.zero);
            Box(transform, new Vector3(2600, 200, 2600), plate, new Vector3(0, s * (Data.COL_CORE_H + 6100f), 0));
        }
        // the comms dish on a stalk above the top plate
        var dishBase = new Vector3(0, Data.COL_CORE_H + 6150f, 0);
        CylY(transform, 140f, 140f, 5600f, dark, dishBase + new Vector3(0, 2800, 0), new Vector3(0, 0, -20f));
        CylY(transform, 2600f, 1400f, 400f, hull, dishBase + new Vector3(-1900, 5300, 0), new Vector3(0, 0, -20f));
        Beacon(transform, cyan, dishBase + new Vector3(-1950, 5700, 0), 360f);
        _beacons.RemoveAt(_beacons.Count - 1);   // the feed stays lit
        // four solar wings on booms from the core
        foreach (var y in new[] { Data.COL_PAD_Y - 3000f, -(Data.COL_PAD_Y - 3000f) })
        {
            foreach (var dz in new[] { 1f, -1f })
            {
                CylY(transform, 180f, 180f, 9000f, dark, new Vector3(0, y, dz * 4500f), new Vector3(90f, 0, 0));
                Box(transform, new Vector3(14000, 90, 9000), solar, new Vector3(0, y, dz * 13500f));
                Box(transform, new Vector3(14000, 60, 60), cyan, new Vector3(0, y + 60f, dz * 9000f));
                Box(transform, new Vector3(14000, 60, 60), cyan, new Vector3(0, y + 60f, dz * 18000f));
            }
        }
        // the cargo terminals above and below the ring plane: a warehouse block round the core, a lit strip, a deck plate
        foreach (var s in new[] { 1f, -1f })
        {
            float y = s * Data.COL_BERTH_Y;
            float T = Data.COL_TERM_Z;
            Box(transform, new Vector3(Data.COL_TERM_X * 2f, Data.COL_TERM_Y * 2f, T * 2f), plate, new Vector3(0, y, 0));
            Box(transform, new Vector3(Data.COL_TERM_X * 2f - 800f, 400, 60), warm, new Vector3(0, y + s * 700f, T + 30f));
            foreach (var x in new[] { -2800f, -1400f, 0f, 1400f, 2800f })
            {
                Box(transform, new Vector3(700, 500, 60), warm, new Vector3(x, y - s * 300f, T + 30f));
                Box(transform, new Vector3(700, 500, 60), warm, new Vector3(x, y, -T - 30f));
            }
            Box(transform, new Vector3(Data.COL_TERM_X * 2f + 1600f, 160, Data.COL_BERTH_Z + 900f - T), hull, new Vector3(0, y - s * 700f, (T + Data.COL_BERTH_Z + 900f) * 0.5f));
            for (int i = 0; i < 7; i++)
            {
                Beacon(transform, i % 2 == 1 ? green : warm, new Vector3(-1800f + i * 600f, y - s * 560f, T + 1400f), 120f);
            }
        }
        // a few lights so the structure reads on its night side
        foreach (var p in new[] { new Vector3(0, 9000, 0), new Vector3(0, -9000, 0), new Vector3(R * 0.7f, 0, 0), new Vector3(-R * 0.7f, 0, 0), new Vector3(0, 0, R * 0.7f), new Vector3(0, 0, -R * 0.7f) })
        {
            PointLight(p, 1.5f, 26000f);
        }
    }
}
