using UnityEngine;

/// The cockpit glass and its damage (2026-09-23, the user's: "cockpit window damage when the hull takes damage", then
/// "the breaks need thresholds: 50%, 25%, 10%, repaired when the hull is repaired"; the panes and the marks are Astra's
/// cockpit_glass v1, `ArtSource/CockpitGlass/v1`): three panes in v2's window apertures, loaded beside the interior,
/// each drawn by BeltRunner/Glass with marks laid on it by UV. The glass breaks in three steps as the hull goes: the
/// first break at half the hull, the second at a quarter, the third at a tenth (a crack web, then a shatter, then a
/// bigger shatter with a scorch, each break larger than the last, and a crack heard as it comes). A break lands where
/// the line from the eye toward whatever hit you crosses a pane (the manifest's projection, pane by pane) when a hit
/// took the hull through the threshold, else on the front pane clear of the breaks already there; it flashes as it
/// lands. The breaks follow the hull back up: a patch or the pad that lifts the hull over a threshold repairs that
/// break, and the hull whole again leaves the glass clear.
public class Glass
{
    public const int SLOTS = 8;
    public const float ALPHA = 0.05f;                                     // the clear pane's haze (Astra's 0.06)
    public static readonly float[] THRESHOLD = { 0.5f, 0.25f, 0.1f };     // the hull shares the three breaks come at
    static readonly int[] BREAK_KIND = { 1, 2, 2 };                       // the web, the shatter, the shatter again (with a scorch)
    static readonly float[] BREAK_WIDTH = { 0.24f, 0.3f, 0.4f };          // each break larger than the last, as a share of the pane's UV width
    static readonly float[] BREAK_REST = { 1f, 0.85f, 0.8f };             // its opacity at rest
    public const float SCORCH_WIDTH = 0.24f, SCORCH_REST = 0.7f;
    public const float FLASH = 0.35f;                                     // seconds a new break flashes, 2.2× its rest at first
    public const float HIT_MEMORY = 1.5f;                                 // a hit places the break it caused within this many seconds

    /// A pane: its projection from the manifest (the unscaled ship frame, the eye at EYE), the view basis facing it,
    /// the projected span and the corner UVs (a trapezoid inside 0..1, counter-clockwise); then its node and material.
    class Pane
    {
        public string node;
        public Vector3 fwd, right, up;
        public Vector2 min, max;
        public Vector2[] corners;
        public Transform t;
        public Material mat;
        public readonly Vector4[] a = new Vector4[SLOTS], b = new Vector4[SLOTS];   // centre.xy, size.xy · cos, sin, opacity, kind
        public int count;
    }

    static readonly Vector3 EYE = new Vector3(0f, 4.5f, 5f);
    readonly Pane[] _panes =
    {
        new Pane { node = "canopy_glass_front", fwd = new Vector3(0f, 0f, 1f), right = new Vector3(1f, 0f, 0f), up = Vector3.up,
            min = new Vector2(-0.8928233f, -0.2460432f), max = new Vector2(0.8928233f, 0.6622563f),
            corners = new[] { new Vector2(0.0809734f, 0f), new Vector2(0.9190266f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) } },
        new Pane { node = "canopy_glass_left", fwd = new Vector3(-1f, 0f, 0f), right = new Vector3(0f, 0f, 1f), up = Vector3.up,
            min = new Vector2(-0.1129648f, -0.3345564f), max = new Vector2(1.2848265f, 0.7168213f),
            corners = new[] { new Vector2(0.0099786f, 0f), new Vector2(1f, 0f), new Vector2(0.8424261f, 1f), new Vector2(0f, 0.9661306f) } },
        new Pane { node = "canopy_glass_right", fwd = new Vector3(1f, 0f, 0f), right = new Vector3(0f, 0f, -1f), up = Vector3.up,
            min = new Vector2(-1.2848265f, -0.3345564f), max = new Vector2(0.1129648f, 0.7168213f),
            corners = new[] { new Vector2(0f, 0f), new Vector2(0.9900214f, 0f), new Vector2(1f, 0.9661306f), new Vector2(0.1575739f, 1f) } },
    };

    static readonly string[] KIND = { "crack_light", "crack_web", "crack_shatter", "scorch" };   // the textures, by kind

    /// One of the three breaks: on or not, where (a pane and a UV), its turn, when it landed, and its scorch if any.
    class Break
    {
        public bool on;
        public Pane pane;
        public Vector2 uv, scorchUv;
        public float rot, scorchRot, born;
        public bool scorch;
    }
    readonly Break[] _breaks = { new Break(), new Break(), new Break() };

    Texture2D[] _tex;
    Shader _shader;
    Pane _hitPane;        // the last hit that crossed a pane, and when: the break it causes lands there
    Vector2 _hitUv;
    float _hitT = -100f;
    public string state = "pending";

    public int Breaks { get { int n = 0; foreach (var b in _breaks) if (b.on) n++; return n; } }
    public int Marks { get { int n = 0; foreach (var p in _panes) n += p.count; return n; } }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var f = Find(t.GetChild(i), name); if (f != null) return f; }
        return null;
    }

    /// Binds the loaded glass holder: each pane node gets its own glass material, with the four mark textures.
    public void Bind(Transform holder)
    {
        _shader = Shader.Find("BeltRunner/Glass");
        _tex = new Texture2D[KIND.Length];
        for (int i = 0; i < KIND.Length; i++)
        {
            _tex[i] = Resources.Load<Texture2D>("Glass/" + KIND[i]);
            if (_tex[i] != null) _tex[i].wrapMode = TextureWrapMode.Clamp;
        }
        int bound = 0;
        foreach (var p in _panes)
        {
            p.t = Find(holder, p.node);
            var mr = p.t != null ? p.t.GetComponent<MeshRenderer>() : null;
            if (mr == null || _shader == null) continue;
            p.mat = new Material(_shader);
            for (int i = 0; i < KIND.Length; i++) p.mat.SetTexture("_Tex" + i, _tex[i]);
            p.mat.SetFloat("_Alpha", ALPHA);
            mr.sharedMaterial = p.mat;
            Push(p);
            bound++;
        }
        state = bound + " panes" + (_shader == null ? " · no shader" : "") + (_tex[1] == null ? " · no textures" : "");
    }

    /// A hit that cost `hullDmg` of hull from `fromLocal` (whatever hit you, in the ship's unscaled frame; zero when
    /// unknown), `hullShare` the hull left after it: remembered where its line crosses a pane, then the breaks follow
    /// the hull, so the break this hit caused lands where it came in.
    public void Hit(float hullDmg, Vector3 fromLocal, float hullShare)
    {
        if (hullDmg <= 0f) return;
        Pane p; Vector2 uv;
        if (fromLocal != Vector3.zero && Cross(fromLocal - EYE, out p, out uv)) { _hitPane = p; _hitUv = uv; _hitT = Time.time; }
        Follow(hullShare);
    }

    /// The breaks follow the hull: one on for each threshold the hull is at or under, off again above it. A break that
    /// comes on is heard.
    void Follow(float share)
    {
        int want = 0;
        for (int i = 0; i < THRESHOLD.Length; i++) if (share <= THRESHOLD[i]) want = i + 1;
        bool changed = false, heard = false;
        for (int i = 0; i < _breaks.Length; i++)
        {
            bool on = i < want;
            var b = _breaks[i];
            if (on == b.on) continue;
            b.on = on;
            changed = true;
            if (!on) continue;
            bool fromHit = _hitPane != null && Time.time - _hitT < HIT_MEMORY;
            b.pane = fromHit ? _hitPane : _panes[0];
            b.uv = fromHit ? _hitUv : Spot(b.pane);
            b.rot = Random.Range(0f, 360f);
            b.born = Time.time;
            b.scorch = i == 2;
            b.scorchUv = b.uv + new Vector2(Random.Range(-0.06f, 0.06f), Random.Range(-0.06f, 0.06f));
            b.scorchRot = Random.Range(0f, 360f);
            _hitPane = null;   // a hit places one break; a second in the same blow goes wherever
            heard = true;
        }
        if (heard) Audio.Play("glass_crack");
        if (changed) Rebuild();
    }

    /// A place on `p` for a break with no hit to put it: of eight throws, the one furthest from the breaks already on
    /// the pane, so they never pile up.
    Vector2 Spot(Pane p)
    {
        var best = Vector2.zero;
        float bestD = -1f;
        for (int k = 0; k < 8; k++)
        {
            var q = new Vector2(Random.Range(0.22f, 0.78f), Random.Range(0.3f, 0.75f));
            float d = 10f;
            foreach (var b in _breaks) if (b.on && b.pane == p) d = Mathf.Min(d, (b.uv - q).magnitude);
            if (d > bestD) { bestD = d; best = q; }
        }
        return best;
    }

    /// Where the line from the eye along `d` (the unscaled ship frame) crosses a pane, if it does: the manifest's
    /// projection, then the trapezoid test on the pane's corner UVs.
    bool Cross(Vector3 d, out Pane pane, out Vector2 uv)
    {
        foreach (var p in _panes)
        {
            float f = Vector3.Dot(d, p.fwd);
            if (f <= 1e-4f) continue;
            float s = Vector3.Dot(d, p.right) / f, t = Vector3.Dot(d, p.up) / f;
            var q = new Vector2((s - p.min.x) / (p.max.x - p.min.x), (t - p.min.y) / (p.max.y - p.min.y));
            if (Inside(p.corners, q)) { pane = p; uv = q; return true; }
        }
        pane = null;
        uv = Vector2.zero;
        return false;
    }

    static bool Inside(Vector2[] c, Vector2 q)
    {
        for (int i = 0; i < 4; i++)
        {
            var a = c[i]; var b = c[(i + 1) % 4];
            if ((b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x) < 0f) return false;
        }
        return true;
    }

    /// Every pane's marks laid out afresh from the breaks that are on: a mark is square on the pane (the manifest's
    /// heightUV = widthUV × spanX / spanY), and flashes for FLASH after it lands.
    void Rebuild()
    {
        foreach (var p in _panes) p.count = 0;
        for (int i = 0; i < _breaks.Length; i++)
        {
            var b = _breaks[i];
            if (!b.on || b.pane == null || b.pane.mat == null) continue;
            float flash = 1f + 1.2f * Mathf.Clamp01(1f - (Time.time - b.born) / FLASH);
            Mark(b.pane, BREAK_KIND[i], b.uv, b.rot, BREAK_WIDTH[i], BREAK_REST[i] * flash);
            if (b.scorch) Mark(b.pane, 3, b.scorchUv, b.scorchRot, SCORCH_WIDTH, SCORCH_REST * flash);
        }
        foreach (var p in _panes) if (p.mat != null) Push(p);
    }

    static void Mark(Pane p, int kind, Vector2 uv, float rot, float width, float opacity)
    {
        if (p.count >= SLOTS) return;
        int i = p.count++;
        float h = width * (p.max.x - p.min.x) / (p.max.y - p.min.y);
        p.a[i] = new Vector4(uv.x, uv.y, width, h);
        p.b[i] = new Vector4(Mathf.Cos(rot * Mathf.Deg2Rad), Mathf.Sin(rot * Mathf.Deg2Rad), opacity, kind);
    }

    static void Push(Pane p)
    {
        p.mat.SetVectorArray("_DecalA", p.a);
        p.mat.SetVectorArray("_DecalB", p.b);
        p.mat.SetFloat("_Count", p.count);
    }

    /// Every frame: the breaks follow the hull (a repair lifts them, the hull whole leaves the glass clear), and a
    /// fresh break's flash settles.
    public void Tick(float hull, float hullMax)
    {
        Follow(hullMax > 0f ? hull / hullMax : 1f);
        foreach (var b in _breaks) if (b.on && Time.time - b.born < FLASH + 0.1f) { Rebuild(); break; }
    }

    public void Clear()
    {
        foreach (var b in _breaks) b.on = false;
        Rebuild();
    }
}
