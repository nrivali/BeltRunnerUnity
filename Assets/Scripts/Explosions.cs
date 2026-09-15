using System.Collections.Generic;
using UnityEngine;

/// Explosions: a destroyed raider goes up in a flash, a fireball of overlapping puffs, an expanding shock ring, a
/// spray of embers, a cloud of dark smoke that drifts on with the wreck, and a few secondary pops a beat later. Every
/// piece is a camera-facing quad with a material of its own (colour and alpha animate per piece), positioned in true
/// coordinates against the floating origin like everything else.
public class Explosions
{
    public Game game;
    Transform _root;
    Texture2D _soft, _ring;
    Mesh _quad;

    class Fx
    {
        public Transform t;
        public Material m;
        public FaceCamera face;
        public Vector3 pos, vel;
        public float age, life, delay, size0, size1, drag, spin;
        public Color c0, c1;
        public int ease;   // 0 linear, 1 fast out (alpha falls quickly), 2 slow out (alpha holds then falls)
        public bool started;
    }
    readonly List<Fx> _fx = new List<Fx>();
    const int MAX = 400;

    public Explosions(Game g)
    {
        game = g;
        _root = new GameObject("Explosions").transform;
        _soft = Ship.SoftTexture();
        _ring = RingTexture();
        _quad = MeshUtil.Quad(1f, 1f, 1f, 1f);
    }

    /// A raider's death at `p` moving at `v` (true coordinates).
    public void Raider(Vector3 p, Vector3 v)
    {
        // the flash: white-hot, huge for an instant
        Add(p, v, 0f, 0.22f, 40f, 190f, new Color(1f, 0.97f, 0.9f, 1f), new Color(1f, 0.55f, 0.2f, 0f), true, _soft, 0f, 1);
        // the fireball: puffs of fire that swell and cool from yellow-white to deep orange to nothing
        for (int i = 0; i < 7; i++)
        {
            var off = Random.insideUnitSphere * 14f;
            var pv = v + off.normalized * Random.Range(25f, 70f) + Random.insideUnitSphere * 15f;
            float life = Random.Range(0.55f, 0.95f);
            Add(p + off, pv, Random.Range(0f, 0.06f), life, Random.Range(18f, 30f), Random.Range(70f, 120f),
                new Color(1f, 0.85f, 0.55f, 1f), new Color(0.6f, 0.12f, 0.02f, 0f), true, _soft, Random.Range(-90f, 90f), 2);
        }
        // the shock ring: a thin bright ring that races out and fades
        Add(p, v, 0f, 0.5f, 14f, 320f, new Color(1f, 0.8f, 0.55f, 0.9f), new Color(1f, 0.5f, 0.2f, 0f), true, _ring, 0f, 1);
        // the smoke: dark puffs that carry on with the wreck, slowing, swelling and thinning over seconds
        for (int i = 0; i < 16; i++)
        {
            var off = Random.insideUnitSphere * 18f;
            var pv = v + off.normalized * Random.Range(15f, 55f) + Random.insideUnitSphere * 10f;
            float life = Random.Range(2.6f, 4.2f);
            var grey = Random.Range(0.16f, 0.3f);
            Add(p + off, pv, Random.Range(0.05f, 0.25f), life, Random.Range(16f, 28f), Random.Range(80f, 150f),
                new Color(grey * 1.4f, grey * 1.1f, grey, 0.75f), new Color(grey, grey, grey, 0f), false, _soft, Random.Range(-40f, 40f), 2);
        }
        // secondary pops: a few smaller flashes a beat later, where the pieces are heading
        for (int i = 0; i < 3; i++)
        {
            var off = Random.insideUnitSphere * 30f;
            Add(p + off, v + off.normalized * 40f, Random.Range(0.15f, 0.55f), 0.18f, 10f, Random.Range(45f, 80f),
                new Color(1f, 0.9f, 0.7f, 1f), new Color(1f, 0.45f, 0.15f, 0f), true, _soft, 0f, 1);
        }
        // embers: hot sparks thrown out fast, and a slower cloud of glowing grit, both carrying the wreck's momentum
        if (game.sparks != null)
        {
            game.sparks.Burst(p, 220, 480f, Data.Hex("#ffb060"), 1.6f, v);
            game.sparks.Burst(p, 120, 160f, Data.Hex("#ff6a2a"), 2.6f, v);
            game.sparks.Burst(p, 60, 90f, Data.Hex("#fff0c0"), 1.2f, v);
        }
    }

    /// A lick of flame off a burning hull: a small bright puff that swells a little and dies fast.
    public void Flame(Vector3 p, Vector3 v)
    {
        Add(p, v, 0f, Random.Range(0.25f, 0.45f), Random.Range(6f, 10f), Random.Range(16f, 26f),
            new Color(1f, 0.8f, 0.45f, 0.9f), new Color(0.7f, 0.15f, 0.02f, 0f), true, _soft, Random.Range(-90f, 90f), 2);
    }

    /// A puff of smoke off a burning hull: dark, slow, swelling and thinning over a couple of seconds.
    public void Smoke(Vector3 p, Vector3 v)
    {
        float g = Random.Range(0.14f, 0.26f);
        Add(p, v, 0f, Random.Range(1.4f, 2.4f), Random.Range(8f, 14f), Random.Range(36f, 60f),
            new Color(g * 1.3f, g * 1.1f, g, 0.6f), new Color(g, g, g, 0f), false, _soft, Random.Range(-40f, 40f), 2);
    }

    void Add(Vector3 pos, Vector3 vel, float delay, float life, float s0, float s1, Color c0, Color c1, bool additive, Texture2D tex, float roll, int ease)
    {
        if (_fx.Count >= MAX) Kill(0);
        var go = new GameObject(additive ? "Fire" : "Smoke");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var mr = go.AddComponent<MeshRenderer>();
        var m = new Material(Game.Sh(additive ? "BeltRunner/Field" : "Sprites/Default"));
        m.mainTexture = tex;
        m.SetColor("_Color", c0);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var face = go.AddComponent<FaceCamera>();
        face.roll = roll;
        go.SetActive(false);
        _fx.Add(new Fx { t = go.transform, m = m, face = face, pos = pos, vel = vel, delay = delay, life = life, size0 = s0, size1 = s1, c0 = c0, c1 = c1, drag = additive ? 1.2f : 0.7f, spin = Random.Range(-25f, 25f), ease = ease });
    }

    void Kill(int i)
    {
        var f = _fx[i];
        if (f.m != null) Object.Destroy(f.m);
        if (f.t != null) Object.Destroy(f.t.gameObject);
        _fx.RemoveAt(i);
    }

    public void Tick(float dt, Vector3 off)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var f = _fx[i];
            if (f.delay > 0f) { f.delay -= dt; f.pos += f.vel * dt; continue; }
            if (!f.started) { f.started = true; f.t.gameObject.SetActive(true); }
            f.age += dt;
            if (f.age >= f.life) { Kill(i); continue; }
            float u = f.age / f.life;
            // the swell eases out: fast at first, then coasting
            float sw = 1f - (1f - u) * (1f - u);
            float size = Mathf.Lerp(f.size0, f.size1, sw);
            float a = f.ease == 1 ? (1f - u) * (1f - u) : f.ease == 2 ? (u < 0.25f ? 1f : 1f - (u - 0.25f) / 0.75f) : 1f - u;
            var c = Color.Lerp(f.c0, f.c1, f.ease == 2 ? Mathf.Clamp01(u * 1.3f) : u);
            c.a = Mathf.Lerp(f.c0.a, f.c1.a, u) * a;
            f.m.SetColor("_Color", c);
            // the pieces of the blast slow against nothing in space, but the fireball's own expansion does: a little drag
            f.vel *= Mathf.Exp(-f.drag * dt);
            f.pos += f.vel * dt;
            f.t.position = f.pos - off;
            f.t.localScale = Vector3.one * size;
            f.face.roll += f.spin * dt;
        }
    }

    public int Count { get { return _fx.Count; } }

    /// A soft ring: bright at radius 0.42, feathered either side, for the shock wave.
    static Texture2D RingTexture()
    {
        const int N = 128;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float band = Mathf.Clamp01(1f - Mathf.Abs(r - 0.42f) / 0.06f);
                float a = band * band;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }
}
