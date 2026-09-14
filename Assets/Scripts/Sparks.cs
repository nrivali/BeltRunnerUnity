using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// The browser's SPARKS: short glowing streaks thrown out by a breaking rock or a hull hit, additive, each stretched
/// along its own velocity (the streak is the last 60 ms of its travel) and thinning as it dies. Drawn as instanced
/// boxes; positions are true world coordinates, placed against the floating origin each frame.
public class Sparks
{
    public const int MAX = 2000;
    const float TRAIL = 0.06f;
    const int BATCH = 1023;

    readonly List<Vector3> _pos = new List<Vector3>();
    readonly List<Vector3> _vel = new List<Vector3>();
    readonly List<Color> _col = new List<Color>();
    readonly List<float> _age = new List<float>();
    readonly List<float> _life = new List<float>();
    readonly List<float> _size = new List<float>();
    readonly Matrix4x4[][] _mats;
    readonly Vector4[][] _cols;
    readonly MaterialPropertyBlock[] _mpb;
    readonly int[] _count;
    Mesh _box;
    Material _mat;
    float _acc;

    public int Count { get { return _pos.Count; } }
    public Material SparkMaterial { get { return _mat; } }
    public Mesh BoxMesh { get { return _box; } }

    public Sparks()
    {
        int nb = (MAX + BATCH - 1) / BATCH;
        _mats = new Matrix4x4[nb][];
        _cols = new Vector4[nb][];
        _mpb = new MaterialPropertyBlock[nb];
        _count = new int[nb];
        for (int b = 0; b < nb; b++) { _mats[b] = new Matrix4x4[BATCH]; _cols[b] = new Vector4[BATCH]; _mpb[b] = new MaterialPropertyBlock(); }
        _box = Cube();
        _box.bounds = new Bounds(Vector3.zero, Vector3.one * 2000000f);
        var sh = Shader.Find("BeltRunner/Spark");
        _mat = new Material(sh != null ? sh : Game.Sh("Sprites/Default"));
        _mat.enableInstancing = true;
    }

    static Mesh Cube()
    {
        var m = new Mesh();
        var v = new List<Vector3>();
        var t = new List<int>();
        Vector3[] c = { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) };
        int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 1, 2, 6, 5 }, new[] { 0, 4, 7, 3 } };
        foreach (var f in faces)
        {
            int s = v.Count;
            foreach (var i in f) v.Add(c[i]);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        return m;
    }

    /// burst(p, count, spd, colour, sz): sparks fly out in every direction at a quarter to the whole of `spd`, live
    /// half a second to a second and a half, 1.4 to 3.2 units wide times `sz`.
    public void Burst(Vector3 p, int count, float spd, Color c, float sz)
    {
        for (int k = 0; k < count; k++)
        {
            if (_pos.Count >= MAX) Drop(0);
            var d = Random.onUnitSphere * Random.Range(spd * 0.25f, spd);
            _pos.Add(p + Random.insideUnitSphere * 3f);
            _vel.Add(d);
            _col.Add(c);
            _age.Add(0f);
            _life.Add(Random.Range(0.5f, 1.5f));
            _size.Add(Random.Range(1.4f, 3.2f) * sz);
        }
    }

    /// emit: a shower of streaks thrown off a surface point away from the rock while the beam cuts, more and hotter as
    /// the spot heats: dull orange when cold, yellow-white when hot, with the odd slower, longer-lived ember.
    public void Emit(Vector3 p, Vector3 nrm, float heat, float dt)
    {
        _acc += dt * (75f + 350f * heat);
        while (_acc >= 1f)
        {
            _acc -= 1f;
            if (_pos.Count >= MAX) Drop(0);
            bool ember = Random.value < 0.125f;
            var d = Random.onUnitSphere * Random.Range(0.3f, 1f) + nrm * Random.Range(0.2f, 1.1f);
            d = d.normalized * ((ember ? Random.Range(50f, 150f) : Random.Range(140f, 400f)) + heat * Random.Range(0f, 320f));
            float g = 0.32f + 0.48f * heat + Random.Range(-0.08f, 0.08f);
            float b = 0.06f + 0.34f * heat * heat + Random.Range(0f, 0.06f);
            _pos.Add(p + Random.insideUnitSphere * 1.5f);
            _vel.Add(d);
            _col.Add(new Color(1f, g, b));
            _age.Add(0f);
            _life.Add(ember ? Random.Range(1f, 1.9f) : Random.Range(0.45f, 1.1f));
            _size.Add(ember ? Random.Range(2.4f, 3.8f) : Random.Range(1.1f, 2.1f));
        }
    }

    void Drop(int s)
    {
        _pos.RemoveAt(s); _vel.RemoveAt(s); _col.RemoveAt(s); _age.RemoveAt(s); _life.RemoveAt(s); _size.RemoveAt(s);
    }

    public void Clear()
    {
        _pos.Clear(); _vel.Clear(); _col.Clear(); _age.Clear(); _life.Clear(); _size.Clear();
        for (int b = 0; b < _count.Length; b++) _count[b] = 0;
    }

    /// Every frame: age, coast with a little drag, and lay each streak's matrix out (side, up, along the velocity).
    public void Tick(float dt, Vector3 offset)
    {
        float damp = Mathf.Exp(-0.9f * dt);
        for (int b = 0; b < _count.Length; b++) _count[b] = 0;
        for (int s = _pos.Count - 1; s >= 0; s--)
        {
            float age = _age[s] + dt;
            if (age >= _life[s]) { Drop(s); continue; }
            _age[s] = age;
            var v = _vel[s] * damp;
            _vel[s] = v;
            var p = _pos[s] + v * dt;
            _pos[s] = p;
            float fade = age / _life[s];
            float w = _size[s] * (1f - 0.5f * fade);
            float len = Mathf.Max(v.magnitude * TRAIL, w);
            var dir = v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
            var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
            int bi = s / BATCH, si = s % BATCH;
            _mats[bi][si] = Matrix4x4.TRS(p - offset, Quaternion.LookRotation(dir, up), new Vector3(w, w, len));
            var c = _col[s];
            _cols[bi][si] = new Vector4(c.r, c.g, c.b, 1f - fade);
            if (si + 1 > _count[bi]) _count[bi] = si + 1;
        }
    }

    public void Draw()
    {
        for (int b = 0; b < _count.Length; b++)
        {
            if (_count[b] == 0) continue;
            _mpb[b].SetVectorArray("_Color", _cols[b]);
            Graphics.DrawMeshInstanced(_box, 0, _mat, _mats[b], _count[b], _mpb[b], ShadowCastingMode.Off, false, 0, null);
        }
    }
}
