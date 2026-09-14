using System;
using System.Collections.Generic;
using UnityEngine;

/// The browser's procedural rocks (rockGeometry in belt-runner-3d.html): a seed polyhedron (icosahedron, dodecahedron
/// or octahedron) subdivided, then pushed in and out by two octaves of value noise, per-vertex grit, an optional waist,
/// and bowl craters; stretched along the axes per shape family. Built once per (shape, variant, detail) at unit radius;
/// every rock is an instance scaled by its own radius and a little random squash.
public static class RockMeshes
{
    public class Shape
    {
        public string key, baseKind = "icosa";
        public float w, amp, grit, freq, pinch;
        public int detail, craters;
        public Vector3 stretch;
        public bool smooth, cave;
    }

    static Shape S(string key, float w, int detail, float amp, float grit, float freq, float sx, float sy, float sz, int craters, bool smooth, string baseKind = "icosa", float pinch = 0f, bool cave = false)
    {
        return new Shape { key = key, w = w, detail = detail, amp = amp, grit = grit, freq = freq, stretch = new Vector3(sx, sy, sz), craters = craters, smooth = smooth, baseKind = baseKind, pinch = pinch, cave = cave };
    }

    public static readonly Shape[] SHAPES =
    {
        S("lumpy",    0.11f, 1, 0.30f, 0.22f, 1.3f, 1f, 1f, 1f, 0, false),
        S("chunk",    0.10f, 1, 0.10f, 0.30f, 1.0f, 1.1f, 0.9f, 1f, 0, false, "dodeca"),
        S("potato",   0.10f, 2, 0.16f, 0.03f, 1.1f, 1.45f, 0.85f, 1f, 0, true),
        S("shard",    0.09f, 1, 0.22f, 0.25f, 1.6f, 0.55f, 0.6f, 2.1f, 0, false),
        S("pancake",  0.06f, 2, 0.20f, 0.05f, 1.4f, 1.35f, 0.42f, 1.2f, 0, true),
        S("cratered", 0.09f, 2, 0.10f, 0.02f, 1.0f, 1f, 1f, 1f, 7, true),
        S("cluster",  0.06f, 1, 0.28f, 0.20f, 1.3f, 1f, 1f, 1f, 0, false),
        S("slab",     0.07f, 1, 0.08f, 0.18f, 1.2f, 1.6f, 0.38f, 1.15f, 0, false, "dodeca"),
        S("spindle",  0.06f, 2, 0.12f, 0.04f, 1.5f, 0.5f, 0.5f, 2.7f, 0, true),
        S("bean",     0.07f, 2, 0.10f, 0.04f, 1.2f, 0.9f, 0.85f, 1.9f, 0, true, "icosa", 0.38f),
        S("boulder",  0.07f, 2, 0.08f, 0.08f, 1.0f, 1.15f, 0.95f, 1f, 4, false, "dodeca"),
        S("jagged",   0.07f, 1, 0.34f, 0.40f, 2.1f, 1f, 1.2f, 0.9f, 0, false),
        S("wedge",    0.05f, 1, 0.06f, 0.14f, 1.0f, 1.4f, 0.75f, 0.9f, 0, false, "octa"),
        S("hollow",   0f,    3, 0.12f, 0.03f, 1.0f, 1f, 1f, 1f, 3, true, "icosa", 0f, true),
    };

    public static int ShapeIndex(string key)
    {
        for (int i = 0; i < SHAPES.Length; i++) if (SHAPES[i].key == key) return i;
        return 0;
    }

    // ---- the browser's hash3 / noise3 (value noise), in double precision as the JS ran it
    static double Hash3(double x, double y, double z)
    {
        double h = Math.Sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453;
        return h - Math.Floor(h);
    }

    static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

    static double Noise3(double x, double y, double z)
    {
        double xi = Math.Floor(x), yi = Math.Floor(y), zi = Math.Floor(z);
        double xf = x - xi, yf = y - yi, zf = z - zi;
        double u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
        return Lerp(
            Lerp(Lerp(Hash3(xi, yi, zi), Hash3(xi + 1, yi, zi), u), Lerp(Hash3(xi, yi + 1, zi), Hash3(xi + 1, yi + 1, zi), u), v),
            Lerp(Lerp(Hash3(xi, yi, zi + 1), Hash3(xi + 1, yi, zi + 1), u), Lerp(Hash3(xi, yi + 1, zi + 1), Hash3(xi + 1, yi + 1, zi + 1), u), v), w);
    }

    // ---- seed polyhedra (three.js's vertex tables; winding is fixed up afterwards from the face normals)
    static readonly float T = (1f + Mathf.Sqrt(5f)) / 2f;
    static readonly Vector3[] ICO_V =
    {
        new Vector3(-1, T, 0), new Vector3(1, T, 0), new Vector3(-1, -T, 0), new Vector3(1, -T, 0),
        new Vector3(0, -1, T), new Vector3(0, 1, T), new Vector3(0, -1, -T), new Vector3(0, 1, -T),
        new Vector3(T, 0, -1), new Vector3(T, 0, 1), new Vector3(-T, 0, -1), new Vector3(-T, 0, 1),
    };
    static readonly int[] ICO_F =
    {
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
        1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
        4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    };
    static readonly float R = 1f / T;
    static readonly Vector3[] DOD_V =
    {
        new Vector3(-1, -1, -1), new Vector3(-1, -1, 1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 1),
        new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(1, 1, -1), new Vector3(1, 1, 1),
        new Vector3(0, -R, -T), new Vector3(0, -R, T), new Vector3(0, R, -T), new Vector3(0, R, T),
        new Vector3(-R, -T, 0), new Vector3(-R, T, 0), new Vector3(R, -T, 0), new Vector3(R, T, 0),
        new Vector3(-T, 0, -R), new Vector3(T, 0, -R), new Vector3(-T, 0, R), new Vector3(T, 0, R),
    };
    static readonly int[] DOD_F =
    {
        3, 11, 7, 3, 7, 15, 3, 15, 13,
        7, 19, 17, 7, 17, 6, 7, 6, 15,
        17, 4, 8, 17, 8, 10, 17, 10, 6,
        8, 0, 16, 8, 16, 2, 8, 2, 10,
        0, 12, 1, 0, 1, 18, 0, 18, 16,
        6, 10, 2, 6, 2, 13, 6, 13, 15,
        2, 16, 18, 2, 18, 3, 2, 3, 13,
        18, 1, 9, 18, 9, 11, 18, 11, 3,
        4, 14, 12, 4, 12, 0, 4, 0, 8,
        11, 9, 5, 11, 5, 19, 11, 19, 7,
        19, 5, 14, 19, 14, 4, 19, 4, 17,
        1, 12, 14, 1, 14, 5, 1, 5, 9,
    };
    static readonly Vector3[] OCT_V =
    {
        new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
    };
    static readonly int[] OCT_F =
    {
        0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 2, 5, 1, 5, 3, 1, 3, 4, 1, 4, 2,
    };

    struct Crater { public Vector3 d; public float w, depth; }

    /// The unit sphere subdivided the three.js way (every face into (detail+1)^2 triangles), welded by position.
    static void Polyhedron(string kind, int detail, List<Vector3> verts, List<int> tris)
    {
        Vector3[] V; int[] F;
        if (kind == "dodeca") { V = DOD_V; F = DOD_F; detail = Mathf.Max(0, detail - 1); }
        else if (kind == "octa") { V = OCT_V; F = OCT_F; detail = detail + 1; }
        else { V = ICO_V; F = ICO_F; }
        var index = new Dictionary<long, int>();
        int cols = detail + 1;
        for (int f = 0; f < F.Length; f += 3)
        {
            var a = V[F[f]].normalized;
            var b = V[F[f + 1]].normalized;
            var c = V[F[f + 2]].normalized;
            // rows of points from a (top) toward the edge b-c
            var rows = new List<int[]>();
            for (int i = 0; i <= cols; i++)
            {
                var aj = Vector3.Lerp(a, c, (float)i / cols);
                var bj = Vector3.Lerp(a, b, (float)i / cols);
                var row = new int[i + 1];
                for (int j = 0; j <= i; j++)
                {
                    var p = (i == 0 ? aj : Vector3.Lerp(aj, bj, (float)j / i)).normalized;
                    row[j] = Weld(p, verts, index);
                }
                rows.Add(row);
            }
            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < 2 * i + 1; j++)
                {
                    int k = j / 2;
                    if (j % 2 == 0)
                    {
                        tris.Add(rows[i + 1][k + 1]); tris.Add(rows[i + 1][k]); tris.Add(rows[i][k]);
                    }
                    else
                    {
                        tris.Add(rows[i][k + 1]); tris.Add(rows[i + 1][k + 1]); tris.Add(rows[i][k]);
                    }
                }
            }
        }
    }

    static int Weld(Vector3 p, List<Vector3> verts, Dictionary<long, int> index)
    {
        long kx = Mathf.RoundToInt(p.x * 20000f) + 100000;
        long ky = Mathf.RoundToInt(p.y * 20000f) + 100000;
        long kz = Mathf.RoundToInt(p.z * 20000f) + 100000;
        long key = (kx * 1000003L + ky) * 1000003L + kz;
        int i;
        if (index.TryGetValue(key, out i)) return i;
        i = verts.Count;
        verts.Add(p);
        index[key] = i;
        return i;
    }

    /// A rock at unit radius: `shape` family, `detailBoost` added to the family's detail (LOD), `seed` picks its noise.
    public static Mesh Build(Shape S, int detailBoost, int seed)
    {
        var rng = new Rng((ulong)seed * 7919UL + 17UL);
        int detail = Mathf.Min(3, S.detail + detailBoost);
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Polyhedron(S.baseKind, detail, verts, tris);
        double sx = seed * 13.7, sy = seed * 7.3, sz = seed * 3.1;
        var craters = new List<Crater>();
        for (int i = 0; i < S.craters; i++) craters.Add(new Crater { d = rng.Dir(), w = rng.Range(0.25f, 0.55f), depth = rng.Range(0.06f, 0.16f) });
        if (S.cave)
        {
            var c = new Crater { d = rng.Dir(), w = 0.95f, depth = 1f };   // one crater so deep it becomes a cave mouth
            if (craters.Count > 0) craters[0] = c; else craters.Add(c);
        }
        for (int i = 0; i < verts.Count; i++)
        {
            var p = verts[i];
            double x = p.x, y = p.y, z = p.z;
            double n = Noise3(x * S.freq + sx, y * S.freq + sy, z * S.freq + sz) * 2 - 1;
            double n2 = Noise3(x * S.freq * 2.7 + sy, y * S.freq * 2.7 + sz, z * S.freq * 2.7 + sx) * 2 - 1;
            double s = 1 + S.amp * n + S.amp * 0.35 * n2 + S.grit * (Hash3(x, y, z) - 0.5);
            var t = p.normalized;
            if (S.pinch > 0f) s *= 1 - S.pinch * (1 - t.z * t.z);   // waist: full radius at the z poles, narrowed round the equator
            foreach (var c in craters)
            {
                float dot = Vector3.Dot(t, c.d);
                float edge = Mathf.Cos(c.w);
                if (dot > edge)
                {
                    float tt = (dot - edge) / (1f - edge);
                    float sn = Mathf.Sin(tt * Mathf.PI * 0.5f);
                    s -= c.depth * sn * sn * (1.4f - tt * 0.6f);
                }
            }
            verts[i] = new Vector3((float)(x * s) * S.stretch.x, (float)(y * s) * S.stretch.y, (float)(z * s) * S.stretch.z);
        }
        // winding: every face outward (the seed tables came from three.js, whose handedness differs from Unity's)
        for (int f = 0; f < tris.Count; f += 3)
        {
            var a = verts[tris[f]]; var b = verts[tris[f + 1]]; var c = verts[tris[f + 2]];
            var nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, a + b + c) < 0f)
            {
                int tmp = tris[f + 1]; tris[f + 1] = tris[f + 2]; tris[f + 2] = tmp;
            }
        }
        var mesh = new Mesh();
        mesh.name = S.key + "_" + seed + "_d" + detail;
        if (S.smooth)
        {
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
        }
        else
        {
            // flat shading: every triangle its own three vertices
            var fv = new Vector3[tris.Count];
            var ft = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++) { fv[i] = verts[tris[i]]; ft[i] = i; }
            mesh.vertices = fv;
            mesh.triangles = ft;
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        var bb = mesh.bounds;
        // the displaced shape pokes past the unit sphere; instancing bounds use the true extent
        mesh.bounds = new Bounds(bb.center, bb.size * 1.05f);
        return mesh;
    }
}
