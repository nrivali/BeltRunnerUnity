using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// The asteroid belt, ported from belt-runner-3d.html: three base belts scaled out from the planet, the ring belt, the
/// charted fields and the rich pockets, every rock on an orbit rail that carries it round the planet at 28 units a
/// second. Rocks live in flat arrays and draw as GPU instances, one batch per (250 km chunk, mesh); the rail drift is
/// computed in the rock shader from the rail written into each instance, so the belt costs no CPU to move. Positions
/// are true world coordinates; the floating origin's offset is subtracted when the instance matrices are written.
public class Belt
{
    public const float CHUNK = 250000f;
    public const float DRAW_DIST = 180000f;
    public const float LOD1_DIST = 70000f;
    public const float ORBIT_SPEED = 28f;
    public const float RESPAWN_AFTER = 300f;
    public const float MARK_TIME = 25f;
    public const float LOD0_RADII = 10f;   // retain sculpted detail while a rock occupies roughly 1/6 of the view height at cruise FOV
    public const float LOD0_OUT = 12f;      // and drops back beyond this many (hysteresis)
    public const int SCRAP_MAX = 512;
    public const int BURN_MAX = 64;   // scorches kept per rock, the oldest going first
    const float COLOSSAL_LOOSE = 0.146f;
    const float BARREN_SHARE = 2f / 3f;
    const float COLOSSAL_ORE_SHARE = 0.001f;
    const int BATCH_MAX = 1023;
    public static readonly int[] FIELDS_PER_BELT = { 7, 7, 9, 0 };
    public static readonly string[] CLS_NAME = { "Small", "Large", "Giant", "Colossal" };

    // ---- the rocks
    public List<Vector3> pos = new List<Vector3>();      // build position, true world (a free rock: its position now)
    public List<float> radius = new List<float>();
    public List<int> ore = new List<int>();              // index into Data.ORE_KEYS, -1 = barren
    public List<float> hp = new List<float>();
    public List<float> hpMax = new List<float>();
    public List<float> amount = new List<float>();
    public List<float> amountMax = new List<float>();
    public List<int> cls = new List<int>();
    public List<bool> alive = new List<bool>();
    public List<int> meshKey = new List<int>();          // shape * 2 + variant
    public List<int> chunkOf = new List<int>();
    public List<int> batchOf = new List<int>();
    public List<int> slotOf = new List<int>();
    public List<float> ang = new List<float>();          // the rail's angle at build time
    public List<float> orbit = new List<float>();        // the rail's radius
    public List<bool> free = new List<bool>();           // off the rail, coasting with vel
    public List<Vector3> vel = new List<Vector3>();
    public List<bool> fragment = new List<bool>();
    public List<int> beltOf = new List<int>();
    public List<float> respawnAt = new List<float>();
    public List<float> markUntil = new List<float>();
    public List<float> markFrom = new List<float>();
    public List<Quaternion> rot = new List<Quaternion>();
    public List<Vector3> scl = new List<Vector3>();
    public List<float> glow = new List<float>();          // residual heat on a fresh fragment (1 at birth, gone in 30 s)
    public int count;

    public class Field
    {
        public string name;
        public Vector3 center;
        public float ang, orbit, radius;
        public int count;
        public Dictionary<string, float> ores;
        public bool pocket;
    }
    public List<Field> fields = new List<Field>();
    public List<Data.BeltDef> belts = new List<Data.BeltDef>();

    // ---- chunks and instance batches
    class Batch
    {
        public int key;
        public int chunk;
        public Matrix4x4[] mats = new Matrix4x4[BATCH_MAX];
        public Vector4[] colors = new Vector4[BATCH_MAX];
        public Vector4[] rails = new Vector4[BATCH_MAX];
        public int count;
        public bool dirty = true;
        public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    }

    class Chunk
    {
        public Vector3 centre;
        public List<int> rocks = new List<int>();
        public List<int> batches = new List<int>();
        public bool visible;
        public int lod = 1;
    }

    List<Chunk> _chunks = new List<Chunk>();
    List<Batch> _batches = new List<Batch>();
    Dictionary<long, int> _chunkKey = new Dictionary<long, int>();
    List<int> _freeIds = new List<int>();
    List<int> _dead = new List<int>();
    List<int> _marked = new List<int>();
    Mesh[,] _meshes;   // [key, lod] with lod 0 = the finest (rocks up close), 1 = near chunks, 2 = far chunks
    // LOD 0: the rocks close to the ship leave their chunk's batch and draw on their own with Astra's finest mesh
    class Lod0 { public Matrix4x4[] mats = new Matrix4x4[1]; public Vector4[] colors = new Vector4[1]; public Vector4[] rails = new Vector4[1]; public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
    readonly Dictionary<int, Lod0> _lod0 = new Dictionary<int, Lod0>();
    readonly HashSet<int> _lod0Keep = new HashSet<int>();
    readonly List<int> _lod0Gone = new List<int>();
    bool _haveLod0;
    // scrap: small hot chunks thrown off a broken rock; they coast, spin, cool over 30 s and fade out after half an hour
    readonly List<Vector3> _scrapPos = new List<Vector3>();
    readonly List<Vector3> _scrapVel = new List<Vector3>();
    readonly List<float> _scrapR = new List<float>();
    readonly List<Vector3> _scrapAxis = new List<Vector3>();
    readonly List<float> _scrapSpin = new List<float>();
    readonly List<float> _scrapT = new List<float>();
    readonly List<float> _scrapLife = new List<float>();
    readonly List<Quaternion> _scrapRot = new List<Quaternion>();
    readonly Matrix4x4[] _scrapMats = new Matrix4x4[SCRAP_MAX];
    readonly Vector4[] _scrapCols = new Vector4[SCRAP_MAX];
    readonly Vector4[] _scrapRails = new Vector4[SCRAP_MAX];
    readonly MaterialPropertyBlock _scrapMpb = new MaterialPropertyBlock();
    int _scrapKey;
    readonly List<int> _hot = new List<int>();
    // the burn trail (addBurn): every spot the laser cooks leaves a scorch on the rock for good, a flat dark decal laid
    // on the surface where the beam is; kept per rock relative to its centre (rocks never turn), drawn as instanced quads
    class Burn { public Vector3 local; public Quaternion rot; public float size; }
    readonly Dictionary<int, List<Burn>> _burns = new Dictionary<int, List<Burn>>();
    readonly Dictionary<int, Burn> _burnLast = new Dictionary<int, Burn>();
    readonly List<int> _burnGone = new List<int>();
    Matrix4x4[] _burnMats = new Matrix4x4[1023];
    Mesh _burnQuad;
    Material _burnMat;
    MaterialPropertyBlock _burnMpb = new MaterialPropertyBlock();
    static Texture2D _burnTex;
    // the laser heat points (the ship's beam 0, the dish's 1): true world, amount, radius
    readonly Vector3[] _heatSrc = new Vector3[2];
    readonly float[] _heatAmt = new float[2];
    readonly float[] _heatRad = { 1f, 1f };
    Material _mat;
    Rng _rng = new Rng(1);
    float _elapsed;
    Vector3 _offset;
    float _respawnT;
    public float planetR;
    public float worldR;
    public float Elapsed { get { return _elapsed; } }

    public Belt()
    {
        // the material asset (made by the build script) keeps the shader's instancing variants in a build; a fresh
        // material stands in when it is missing, which is fine in the editor
        var asset = Resources.Load<Material>("Materials/Rock");
        if (asset != null)
        {
            _mat = new Material(asset);
        }
        else
        {
            var sh = Shader.Find("BeltRunner/Rock");
            if (sh == null)
            {
                Debug.LogWarning("rocks: BeltRunner/Rock shader missing, the Standard shader stands in (no drift, no heat)");
                sh = Game.Sh("Standard");
            }
            _mat = new Material(sh);
        }
        _mat.enableInstancing = true;
        _meshes = new Mesh[RockMeshes.SHAPES.Length * 2, 3];
        _subMats = new Material[RockMeshes.SHAPES.Length * 2][];
        _tints = new bool[RockMeshes.SHAPES.Length * 2][];
        int fromLib = LoadLibrary();
        for (int s = 0; s < RockMeshes.SHAPES.Length; s++)
        {
            for (int v = 0; v < 2; v++)
            {
                int key = s * 2 + v;
                if (_meshes[key, 1] == null || _meshes[key, 2] == null)
                {
                    // the browser's own procedural shape stands in for a missing library entry
                    _meshes[key, 1] = RockMeshes.Build(RockMeshes.SHAPES[s], 1, key + 1);
                    _meshes[key, 2] = RockMeshes.Build(RockMeshes.SHAPES[s], 0, key + 1);
                    _subMats[key] = new[] { _mat };
                    _tints[key] = new[] { true };
                }
                if (_meshes[key, 0] == null) _meshes[key, 0] = _meshes[key, 1];
                // the rail drift happens in the vertex shader, so a rock can sit up to a chunk away from its matrix;
                // bounds wide enough (in mesh units, scaled by the smallest radius) keep the renderer from culling it
                for (int l = 0; l < 3; l++) _meshes[key, l].bounds = new Bounds(Vector3.zero, Vector3.one * 60000f);
                if (RockMeshes.SHAPES[s].key == "lumpy" && v == 0) _scrapKey = key;
            }
        }
        Debug.Log("rocks: library " + (fromLib > 0 ? fromLib + " shapes from Astra's asteroids" : "missing, the procedural shapes stand in"));
    }

    Material[][] _subMats;   // per mesh key: one rock material per submesh (regolith, ore vein)
    bool[][] _tints;         // per mesh key and submesh: the instance colour tints it (the ore veins)
    public int libraryShapes;

    /// Astra's asteroid library (asteroids_lod1 / lod2, imported by glTFast): one mesh per shape and variant at unit
    /// radius, two surfaces (regolith and the ore vein), with the PBR maps copied into rock materials. Returns how
    /// many shapes were found.
    int LoadLibrary()
    {
        var lod1 = Resources.Load<GameObject>("Models/asteroids_lod1");
        var lod2 = Resources.Load<GameObject>("Models/asteroids_lod2");
        var lod0 = Resources.Load<GameObject>("Models/asteroids_lod0");
        if (lod1 == null || lod2 == null) return 0;
        var near = new Dictionary<string, MeshFilter>();
        var far = new Dictionary<string, MeshFilter>();
        var finest = new Dictionary<string, MeshFilter>();
        foreach (var mf in lod1.GetComponentsInChildren<MeshFilter>(true)) near[KeyOf(mf.name)] = mf;
        foreach (var mf in lod2.GetComponentsInChildren<MeshFilter>(true)) far[KeyOf(mf.name)] = mf;
        if (lod0 != null) foreach (var mf in lod0.GetComponentsInChildren<MeshFilter>(true)) finest[KeyOf(mf.name)] = mf;
        // Barren instances use the same regolith maps on both submeshes, including the former vein faces.
        Material stoneSource = null;
        foreach (var mf in near.Values)
        {
            var renderer = mf.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.name == "Barren_Regolith") { stoneSource = material; break; }
            if (stoneSource != null) break;
        }
        int found = 0;
        for (int s = 0; s < RockMeshes.SHAPES.Length; s++)
        {
            for (int v = 0; v < 2; v++)
            {
                string k = RockMeshes.SHAPES[s].key + "_" + (v == 0 ? "A" : "B");
                MeshFilter a, b;
                if (!near.TryGetValue(k, out a) || !far.TryGetValue(k, out b) || a.sharedMesh == null || b.sharedMesh == null) continue;
                int key = s * 2 + v;
                _meshes[key, 1] = a.sharedMesh;
                _meshes[key, 2] = b.sharedMesh;
                MeshFilter f0;
                if (finest.TryGetValue(k, out f0) && f0.sharedMesh != null && f0.sharedMesh.subMeshCount == a.sharedMesh.subMeshCount) { _meshes[key, 0] = f0.sharedMesh; _haveLod0 = true; }
                var mr = a.GetComponent<MeshRenderer>();
                var src = mr != null ? mr.sharedMaterials : new Material[0];
                int n = Mathf.Max(1, a.sharedMesh.subMeshCount);
                _subMats[key] = new Material[n];
                _tints[key] = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    var sm = i < src.Length ? src[i] : null;
                    _subMats[key][i] = ConvertMaterial(sm, stoneSource);
                    _tints[key][i] = sm != null && sm.name.StartsWith("Ore_");
                }
                found++;
            }
        }
        libraryShapes = found;
        return found;
    }

    static string KeyOf(string nodeName)
    {
        var parts = nodeName.Split('_');
        return parts.Length >= 2 ? parts[0] + "_" + parts[1] : nodeName;
    }

    readonly Dictionary<Material, Material> _matCache = new Dictionary<Material, Material>();

    // The approved Blender finishes, in Data.ORE_KEYS order. Entry zero is barren. Instance alpha carries
    // the finish index; RGB remains the ore tint. This retains the existing instance batches and draw count.
    static readonly Vector4[] OreFinishes = BuildOreFinishes();
    static Vector4[] BuildOreFinishes()
    {
        var result = new Vector4[8];
        result[0] = new Vector4(1f, 0f, 0f, 0f);
        for (int i = 0; i < Data.ORE_KEYS.Length; i++)
        {
            string key = Data.ORE_KEYS[i];
            float roughness = key == "gold" || key == "platinum" ? 0.30f : key == "beryl" ? 0.36f : key == "crystal" || key == "cobalt" ? 0.34f : 0.38f;
            float metallic = key == "gold" ? 0.97f : key == "copper" ? 0.95f : key == "crystal" ? 0.82f : key == "beryl" ? 0.83f : key == "cobalt" ? 0.92f : 0.94f;
            result[i + 1] = new Vector4(roughness, metallic, 0f, 0f);
        }
        return result;
    }

    static Color OreSurfaceColor(int oreIndex)
    {
        if (oreIndex < 0) return new Color(1f, 1f, 1f, 0f);
        string key = Data.ORE_KEYS[oreIndex];
        // Surface reflectance uses the asset palette; HUD icon colours remain independent.
        Color c = Data.Hex(key == "iron" ? "#D3DCEC" : key == "copper" ? "#F39860" : key == "platinum" ? "#58DDD0" : key == "cobalt" ? "#578EF1" : key == "beryl" ? "#39DB85" : key == "gold" ? "#F2C94C" : "#B98CFF");
        c.a = oreIndex + 1;
        return c;
    }

    static void CopyRockMap(Material source, string from, Material target, string to)
    {
        if (source == null || !source.HasProperty(from)) return;
        var texture = source.GetTexture(from);
        if (texture == null) return;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;
        target.SetTexture(to, texture);
    }

    /// Keep the glTF surface maps, but select the actual ore finish per instance instead of tinting iron again.
    Material ConvertMaterial(Material sm, Material stoneSource)
    {
        if (sm == null) return _mat;
        Material m;
        if (_matCache.TryGetValue(sm, out m)) return m;
        m = new Material(_mat);
        m.name = sm.name;
        m.enableInstancing = true;
        bool vein = sm.name.StartsWith("Ore_");
        CopyRockMap(sm, "baseColorTexture", m, "_MainTex");
        CopyRockMap(sm, "normalTexture", m, "_BumpMap");
        CopyRockMap(sm, "metallicRoughnessTexture", m, "_MetalRough");
        CopyRockMap(stoneSource, "baseColorTexture", m, "_StoneTex");
        CopyRockMap(stoneSource, "normalTexture", m, "_StoneNormal");
        CopyRockMap(stoneSource, "metallicRoughnessTexture", m, "_StoneMetalRough");
        // Larger fractured plates replace the port's fine gravel surface. Unity imports these with mipmaps
        // and linear data sampling; the embedded maps remain available if an optional map is missing.
        foreach (string suffix in new[] { "albedo", "normal", "metalrough" })
        {
            var texture = Resources.Load<Texture2D>("Asteroids/regolith_" + suffix);
            if (texture == null) continue;
            string stoneSlot = suffix == "albedo" ? "_StoneTex" : suffix == "normal" ? "_StoneNormal" : "_StoneMetalRough";
            string bodySlot = suffix == "albedo" ? "_MainTex" : suffix == "normal" ? "_BumpMap" : "_MetalRough";
            m.SetTexture(stoneSlot, texture);
            if (!vein) m.SetTexture(bodySlot, texture);
            else
            {
                // Full-resolution concept metal maps override the compact GLB fallback textures.
                var metal = Resources.Load<Texture2D>("Asteroids/ore_" + suffix);
                if (metal != null) m.SetTexture(bodySlot, metal);
            }
        }
        m.SetColor("_BaseColor", vein ? Color.white : sm.GetColor("baseColorFactor") * 0.50f);
        m.SetColor("_StoneColor", (stoneSource != null ? stoneSource.GetColor("baseColorFactor") : Color.white) * 0.50f);
        // Larger surface grains and dark basalt reflectance for the approved asteroid concepts.
        m.SetTextureScale("_MainTex", Vector2.one * 0.60f);
        var detailNormal = Resources.Load<Texture2D>("Asteroids/rock_detail_normal");
        var detailSurface = Resources.Load<Texture2D>("Asteroids/rock_detail_surface");
        if (detailNormal != null && detailSurface != null)
        {
            m.SetTexture("_DetailNormalMap", detailNormal);
            m.SetTexture("_DetailSurface", detailSurface);
            m.SetFloat("_DetailStrength", 1f);
        }
        m.SetFloat("_Library", 1f);
        m.SetFloat("_Tint", vein ? 1f : 0f);
        m.SetFloat("_BumpScale", sm.HasProperty("normalTexture_scale") ? sm.GetFloat("normalTexture_scale") : 1f);
        m.SetVectorArray("_OreFinish", OreFinishes);
        _matCache[sm] = m;
        return m;
    }

    public void Clear()
    {
        pos.Clear(); radius.Clear(); ore.Clear(); hp.Clear(); hpMax.Clear(); amount.Clear(); amountMax.Clear(); cls.Clear(); alive.Clear();
        meshKey.Clear(); chunkOf.Clear(); batchOf.Clear(); slotOf.Clear(); ang.Clear(); orbit.Clear(); free.Clear(); vel.Clear(); fragment.Clear();
        beltOf.Clear(); respawnAt.Clear(); markUntil.Clear(); markFrom.Clear(); rot.Clear(); scl.Clear(); glow.Clear();
        count = 0;
        _lod0.Clear();
        _hot.Clear();
        _burns.Clear();
        _burnLast.Clear();
        ClearScrap();
        _heatAmt[0] = _heatAmt[1] = 0f;
        fields.Clear();
        belts.Clear();
        _chunks.Clear();
        _batches.Clear();
        _chunkKey.Clear();
        _freeIds.Clear();
        _dead.Clear();
        _marked.Clear();
        _elapsed = 0f;
    }

    // ---- building
    public void Build(Data.Zone zone, ulong seed)
    {
        _rng.Seed(seed);
        _elapsed = 0f;
        planetR = zone.central ? zone.planetR * Data.PLANET_SCALE : 0f;
        worldR = planetR + Data.WORLD_EDGE_BASE * Data.WORLD_SCALE;
        float density = zone.density;
        if (density <= 0f) return;
        // the three base belts, scaled to world units and given this zone's ore weights, then the ring belt at its fixed radius
        var list = new List<Data.BeltDef>();
        for (int i = 0; i < Data.BASE_BELTS.Length; i++)
        {
            var b = Data.BASE_BELTS[i].Clone();
            b.ores = zone.belts[i];
            b.count = Mathf.RoundToInt(b.count * density * 0.65f);
            b.rMin = planetR + b.rMin * Data.WORLD_SCALE;
            b.rMax = planetR + b.rMax * Data.WORLD_SCALE;
            b.spread = b.spread * Data.WORLD_SCALE;
            b.amountMult = zone.amountMult;
            list.Add(b);
        }
        if (Data.RING_BELT.rMin > planetR + 20000f)
        {
            var r = Data.RING_BELT.Clone();
            r.count = Mathf.RoundToInt(r.count * density);
            r.amountMult = zone.amountMult;
            list.Add(r);
        }
        belts.AddRange(list);
        for (int bi = 0; bi < list.Count; bi++)
        {
            var b = list[bi];
            for (int i = 0; i < b.count; i++) MakeRock(b, null, bi);
            int nf = bi < FIELDS_PER_BELT.Length ? FIELDS_PER_BELT[bi] : 0;
            for (int f = 0; f < nf; f++)
            {
                var fld = MakeField(b, density);
                fld.name = zone.name.Substring(0, 1) + (bi + 1) + "-" + (char)(65 + f);   // K1-A ...
                fld.pocket = false;
                fields.Add(fld);
                for (int i = 0; i < fld.count; i++) MakeRock(b, fld, bi);
            }
        }
        // rich pockets: tight clusters anywhere in the zone, every ore the zone offers, two and a half times the yield
        var all = new Dictionary<string, float>();
        for (int i = 0; i < Data.BASE_BELTS.Length; i++)
        {
            foreach (var kv in zone.belts[i])
            {
                float w;
                all.TryGetValue(kv.Key, out w);
                all[kv.Key] = w + kv.Value;
            }
        }
        var pocketBelt = new Data.BeltDef { name = "Pocket", rMin = planetR + 40000f, rMax = worldR * 0.9f, size0 = 30, size1 = 96, amount0 = 120, amount1 = 360, spread = 0f, ores = all, amountMult = zone.amountMult * 2.5f };
        belts.Add(pocketBelt);
        int pocketBi = belts.Count - 1;
        int np = Mathf.RoundToInt(30f * Mathf.Max(0.7f, density));
        for (int i = 0; i < np; i++)
        {
            float prad = _rng.Range(1500f, 3500f);
            float dist = _rng.Range(pocketBelt.rMin, pocketBelt.rMax);
            float pang = _rng.Value() * Mathf.PI * 2f;
            float lat = Mathf.Asin(_rng.Range(-1f, 1f)) * 0.85f;
            float porbit = Mathf.Max(planetR * 0.2f, Mathf.Cos(lat) * dist);
            var centre = new Vector3(Mathf.Cos(pang) * porbit, Mathf.Sin(lat) * dist, Mathf.Sin(pang) * porbit);
            var fld = new Field { radius = prad, center = centre, ores = all, count = Mathf.RoundToInt(_rng.Range(40f, 90f)), ang = pang, orbit = porbit, name = zone.name.Substring(0, 1) + "P-" + (i + 1), pocket = true };
            fields.Add(fld);
            for (int k = 0; k < fld.count; k++) MakeRock(pocketBelt, fld, pocketBi);
        }
        BuildChunks();
    }

    Field MakeField(Data.BeltDef b, float density)
    {
        float rad = _rng.Range(900f, 2200f);
        float orb = _rng.Range(b.rMin + rad, b.rMax - rad);
        float a = _rng.Value() * Mathf.PI * 2f;
        float y = _rng.Range(-0.5f, 0.5f) * b.spread;
        var keys = new List<string>(b.ores.Keys);
        string mainOre = keys[_rng.Int(keys.Count)];
        var w = new Dictionary<string, float>();
        float sum = 0f;
        foreach (var k in keys)
        {
            w[k] = b.ores[k] * (k == mainOre ? 3f : 1f);
            sum += w[k];
        }
        foreach (var k in keys) w[k] /= sum;
        return new Field { radius = rad, center = new Vector3(Mathf.Cos(a) * orb, y, Mathf.Sin(a) * orb), ores = w, count = Mathf.RoundToInt(_rng.Range(45f, 85f) * density), ang = a, orbit = orb };
    }

    string PickOre(Dictionary<string, float> weights)
    {
        float r = _rng.Value();
        float acc = 0f;
        string last = "";
        foreach (var kv in weights)
        {
            acc += kv.Value;
            last = kv.Key;
            if (r <= acc) return kv.Key;
        }
        return last;
    }

    /// Which mesh a rock uses: giants and colossals are big rounded, pocked or blocky bodies; other large rocks are
    /// sometimes hollows; the rest draw from the whole library. A or B variant at random.
    int PickShape(int c)
    {
        string fam;
        if (c >= 2)
        {
            float q = _rng.Value();
            fam = q < 0.45f ? "cratered" : (q < 0.7f ? "boulder" : (q < 0.85f ? "potato" : "bean"));
        }
        else if (c == 1 && _rng.Value() < 0.22f)
        {
            fam = "hollow";
        }
        else
        {
            fam = RockMeshes.SHAPES[_rng.Int(RockMeshes.SHAPES.Length - 1)].key;   // everything but hollow
        }
        return RockMeshes.ShapeIndex(fam) * 2 + (_rng.Value() < 0.5f ? 0 : 1);
    }

    void MakeRock(Data.BeltDef b, Field fld, int bi)
    {
        Vector3 p;
        if (fld != null)
        {
            p = fld.center + _rng.Dir() * fld.radius * Mathf.Pow(_rng.Value(), 0.6f);
        }
        else
        {
            int tries = 0;
            do
            {
                float a = _rng.Value() * Mathf.PI * 2f;
                float orb = _rng.Range(b.rMin, b.rMax);
                float y = (_rng.Value() + _rng.Value() - 1f) * b.spread;
                p = new Vector3(Mathf.Cos(a) * orb, y, Mathf.Sin(a) * orb);
                tries++;
            } while (tries < 30 && p.magnitude <= planetR * 1.02f);
        }
        // size mix: colossals come off the top of the roll for loose rocks only, then giant : large : small split 2 : 4 : 3
        float roll = _rng.Value();
        int c = 0;
        if (fld == null)
        {
            if (roll < COLOSSAL_LOOSE) c = 3;
            else roll = (roll - COLOSSAL_LOOSE) / (1f - COLOSSAL_LOOSE);
        }
        if (c != 3) c = roll < 2f / 9f ? 2 : (roll < 6f / 9f ? 1 : 0);
        float baseR;
        switch (c)
        {
            case 3: baseR = b.size1 * _rng.Range(14f, 24f); break;
            case 2: baseR = b.size1 * _rng.Range(5f, 8.5f); break;
            case 1: baseR = b.size1 * _rng.Range(1.8f, 3.2f); break;
            default: baseR = b.size0 + (b.size1 - b.size0) * Mathf.Pow(_rng.Value(), 1.7f); break;
        }
        string oreKey = PickOre(fld != null ? fld.ores : b.ores);
        bool barren = _rng.Value() < (c == 3 ? 1f - COLOSSAL_ORE_SHARE : BARREN_SHARE);
        float t = Mathf.Clamp01((baseR - b.size0) / (b.size1 - b.size0));
        float amt = Mathf.Round((b.amount0 + (b.amount1 - b.amount0) * t) * Mathf.Max(1f, baseR / b.size1) * b.amountMult * _rng.Range(0.85f, 1.15f));
        float hpm = Mathf.Round(40f + 2.2f * Mathf.Pow(baseR, 1.15f));
        // the rail: a field rock rides its field's rail (it keeps its offset from the centre); a loose rock has its own
        float ra = fld != null ? fld.ang : Mathf.Atan2(p.z, p.x);
        float ro = fld != null ? fld.orbit : Mathf.Sqrt(p.x * p.x + p.z * p.z);
        AppendRock(p, baseR, barren ? -1 : Data.OreIndex(oreKey), hpm, barren ? 0f : amt, c, PickShape(c), ra, ro, bi, false, Vector3.zero);
    }

    /// One row in every array. Rail rocks join a chunk batch in BuildChunks; a fragment is slotted in at once.
    int AppendRock(Vector3 p, float r, int oreI, float hpm, float amt, int c, int key, float ra, float ro, int bi, bool isFree, Vector3 v)
    {
        int i = count;
        pos.Add(p); radius.Add(r); ore.Add(oreI); hp.Add(hpm); hpMax.Add(hpm); amount.Add(amt); amountMax.Add(amt); cls.Add(c); alive.Add(true);
        meshKey.Add(key); chunkOf.Add(-1); batchOf.Add(-1); slotOf.Add(-1); markUntil.Add(0f); markFrom.Add(0f);
        ang.Add(ra); orbit.Add(Mathf.Max(1f, ro)); free.Add(isFree); vel.Add(v); fragment.Add(false); beltOf.Add(bi); respawnAt.Add(0f); glow.Add(0f);
        rot.Add(Quaternion.Euler(_rng.Value() * 360f, _rng.Value() * 360f, _rng.Value() * 360f));
        scl.Add(new Vector3(_rng.Range(0.85f, 1.2f), _rng.Range(0.8f, 1.15f), _rng.Range(0.85f, 1.2f)) * r);
        count++;
        return i;
    }

    int ChunkIndex(Vector3 p)
    {
        long cx = Mathf.FloorToInt(p.x / CHUNK), cy = Mathf.FloorToInt(p.y / CHUNK), cz = Mathf.FloorToInt(p.z / CHUNK);
        long key = ((cx + 5000) * 10007L + (cy + 5000)) * 10007L + (cz + 5000);
        int idx;
        if (_chunkKey.TryGetValue(key, out idx)) return idx;
        idx = _chunks.Count;
        _chunkKey[key] = idx;
        _chunks.Add(new Chunk { centre = new Vector3((cx + 0.5f) * CHUNK, (cy + 0.5f) * CHUNK, (cz + 0.5f) * CHUNK) });
        return idx;
    }

    void BuildChunks()
    {
        for (int i = 0; i < count; i++)
        {
            int ci = ChunkIndex(pos[i]);
            chunkOf[i] = ci;
            _chunks[ci].rocks.Add(i);
            Slot(i, ci);
        }
    }

    /// A slot in a batch of this chunk with this rock's mesh (a new batch when none has room).
    void Slot(int i, int ci)
    {
        var ch = _chunks[ci];
        Batch b = null;
        foreach (var bi in ch.batches)
        {
            var cand = _batches[bi];
            if (cand.key == meshKey[i] && cand.count < BATCH_MAX) { b = cand; batchOf[i] = bi; break; }
        }
        if (b == null)
        {
            b = new Batch { key = meshKey[i], chunk = ci };
            batchOf[i] = _batches.Count;
            _batches.Add(b);
            ch.batches.Add(batchOf[i]);
        }
        slotOf[i] = b.count;
        b.count++;
        WriteInstance(i);
    }

    /// The instance colour: the ore's colour for the vein surfaces (the library's regolith ignores it); a stone grey
    /// for the procedural stand-ins, tinted toward the ore.
    Color RockColor(int i)
    {
        var stone = Color.Lerp(new Color(0.36f, 0.34f, 0.31f), new Color(0.26f, 0.25f, 0.24f), _rng.Value());
        if (libraryShapes > 0) return OreSurfaceColor(ore[i]);
        if (ore[i] < 0) return stone;
        return Color.Lerp(stone, Data.ORES[ore[i]].color, 0.55f);
    }

    /// The instance's matrix, colour and rail (angle, radius, on-rail flag, body heat) for the shader.
    void WriteInstance(int i)
    {
        var b = _batches[batchOf[i]];
        int s = slotOf[i];
        b.mats[s] = alive[i] && !_lod0.ContainsKey(i) ? Matrix4x4.TRS(pos[i] - _offset, rot[i], scl[i]) : Matrix4x4.zero;
        if (b.colors[s] == Vector4.zero) b.colors[s] = RockColor(i);
        b.rails[s] = new Vector4(ang[i], orbit[i], free[i] ? 0f : 1f, BodyHeat(i));
        b.dirty = true;
    }

    void WriteRail(int i)
    {
        var b = _batches[batchOf[i]];
        b.rails[slotOf[i]] = new Vector4(ang[i], orbit[i], free[i] ? 0f : 1f, BodyHeat(i));
        b.dirty = true;
    }

    void WriteTranslation(int i)
    {
        if (_lod0.ContainsKey(i)) return;   // drawn on its own while it is close
        var b = _batches[batchOf[i]];
        var m = b.mats[slotOf[i]];
        var p = pos[i] - _offset;
        m.m03 = p.x; m.m13 = p.y; m.m23 = p.z;
        b.mats[slotOf[i]] = m;
    }

    float BodyHeat(int i)
    {
        float h = Mathf.Pow(Mathf.Max(0f, 1f - hp[i] / Mathf.Max(1f, hpMax[i])), 1.3f);
        return Mathf.Max(h, Mathf.Pow(glow[i], 1.6f));
    }

    /// The scorch texture (the browser's burn canvas): a dark core fading out, with a few lighter flecks.
    static Texture2D BurnTexture()
    {
        if (_burnTex != null) return _burnTex;
        _burnTex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var c0 = new Color(12f / 255f, 7f / 255f, 4f / 255f, 0.95f);
        var c1 = new Color(28f / 255f, 14f / 255f, 8f / 255f, 0.75f);
        var c2 = new Color(40f / 255f, 22f / 255f, 12f / 255f, 0.25f);
        var c3 = new Color(40f / 255f, 22f / 255f, 12f / 255f, 0f);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                Color c;
                if (d < 0.45f) c = Color.Lerp(c0, c1, d / 0.45f);
                else if (d < 0.8f) c = Color.Lerp(c1, c2, (d - 0.45f) / 0.35f);
                else c = Color.Lerp(c2, c3, Mathf.Clamp01((d - 0.8f) / 0.2f));
                _burnTex.SetPixel(x, y, c);
            }
        }
        var rng = new Rng(9);
        for (int k = 0; k < 40; k++)
        {
            int x = 8 + (int)(rng.Value() * 48f), y = 8 + (int)(rng.Value() * 48f);
            float a = rng.Value() * 0.35f;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
            {
                var px = _burnTex.GetPixel(x + dx, y + dy);
                _burnTex.SetPixel(x + dx, y + dy, Color.Lerp(px, new Color(60f / 255f, 30f / 255f, 14f / 255f, Mathf.Max(px.a, a)), a));
            }
        }
        _burnTex.wrapMode = TextureWrapMode.Clamp;
        _burnTex.Apply();
        return _burnTex;
    }

    /// addBurn: a scorch where the beam is, laid on the surface facing the rock's centre with a random turn; the same
    /// spot is not restamped; a rock keeps up to BURN_MAX.
    public void Scorch(int i, Vector3 worldHit, float size)
    {
        if (i < 0 || i >= count || !alive[i]) return;
        var centre = RockPos(i);
        var local = worldHit - centre;
        if (local.sqrMagnitude < 1e-6f) return;
        var n = local.normalized;
        Burn last;
        if (_burnLast.TryGetValue(i, out last) && (last.local - local).magnitude < size * 0.4f && Mathf.Abs(last.size - size) < size * 0.3f) return;
        List<Burn> list;
        if (!_burns.TryGetValue(i, out list)) { list = new List<Burn>(); _burns[i] = list; }
        var ax = Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up;
        var t1 = Vector3.Cross(n, ax).normalized;
        var t2 = Vector3.Cross(n, t1).normalized;
        float rot = Random.value * Mathf.PI * 2f;
        var e1 = (t1 * Mathf.Cos(rot) + t2 * Mathf.Sin(rot)).normalized;
        var e2 = Vector3.Cross(n, e1).normalized;
        var b = new Burn { local = local + n * 0.8f, rot = Quaternion.LookRotation(n, e2), size = size };
        list.Add(b);
        if (list.Count > BURN_MAX) list.RemoveAt(0);
        _burnLast[i] = b;
    }

    public int BurnCount
    {
        get { int n = 0; foreach (var kv in _burns) n += kv.Value.Count; return n; }
    }

    void DrawBurns()
    {
        if (_burns.Count == 0) return;
        if (_burnMat == null)
        {
            _burnQuad = MeshUtil.Quad(1f, 1f, 1f, 1f);
            _burnQuad.bounds = new Bounds(Vector3.zero, Vector3.one * 2000000f);
            _burnMat = new Material(Game.Sh("BeltRunner/Scorch"));
            _burnMat.mainTexture = BurnTexture();
            _burnMat.enableInstancing = true;
        }
        _burnGone.Clear();
        int k = 0;
        foreach (var kv in _burns)
        {
            int i = kv.Key;
            if (!alive[i]) { _burnGone.Add(i); continue; }
            var centre = RockPos(i) - _offset;
            foreach (var b in kv.Value)
            {
                _burnMats[k++] = Matrix4x4.TRS(centre + b.local, b.rot, new Vector3(b.size * 2f, b.size * 1.6f, 1f));
                if (k == 1023) { Graphics.DrawMeshInstanced(_burnQuad, 0, _burnMat, _burnMats, k, _burnMpb, ShadowCastingMode.Off, false, 0, null); k = 0; }
            }
        }
        if (k > 0) Graphics.DrawMeshInstanced(_burnQuad, 0, _burnMat, _burnMats, k, _burnMpb, ShadowCastingMode.Off, false, 0, null);
        foreach (int i in _burnGone) { _burns.Remove(i); _burnLast.Remove(i); }
    }

    /// The laser heat points: where the ship's beam (0) and the dish's beam (1) are cooking a rock this frame.
    public void SetSpotHeat(int idx, Vector3 atTrue, float amount, float radius)
    {
        _heatSrc[idx] = atTrue;
        _heatAmt[idx] = amount;
        _heatRad[idx] = Mathf.Max(1f, radius);
    }

    // ---- the rails: where a rock is now, and how fast it is going
    /// The drift since the belt was built for a rail with angle `a` and radius `o` (a rotation by 28 / o radians a
    /// second), written so nothing large is subtracted from anything large.
    public Vector3 Delta(float a, float o)
    {
        float th = ORBIT_SPEED * _elapsed / o;
        float c = Mathf.Cos(th) - 1f;
        float s = Mathf.Sin(th);
        float ca = Mathf.Cos(a);
        float sa = Mathf.Sin(a);
        return new Vector3(o * (ca * c + sa * s), 0f, o * (sa * c - ca * s));
    }

    public Vector3 RockPos(int i)
    {
        return free[i] ? pos[i] : pos[i] + Delta(ang[i], orbit[i]);
    }

    /// The HTML's orbitalVel: 28 u/s along the rail (a free rock's own velocity).
    public Vector3 RockVel(int i)
    {
        if (free[i]) return vel[i];
        // the rail angle falls with time (Delta rotates by -28 / radius a second), so the tangent runs the other way
        float phi = ang[i] - ORBIT_SPEED * _elapsed / orbit[i];
        return new Vector3(Mathf.Sin(phi), 0f, -Mathf.Cos(phi)) * ORBIT_SPEED;
    }

    public Vector3 FieldCentre(Field f)
    {
        return f.center + Delta(f.ang, f.orbit);
    }

    /// The charted field `p` (true world) is inside, if any.
    public Field FieldAt(Vector3 p)
    {
        foreach (var f in fields) if ((FieldCentre(f) - p).magnitude < f.radius) return f;
        return null;
    }

    /// The nearest field by its edge, with the distance to that edge; null in a zone without fields.
    public Field NearestField(Vector3 p, out float edge)
    {
        Field best = null;
        float bd = float.PositiveInfinity;
        foreach (var f in fields)
        {
            float e = (FieldCentre(f) - p).magnitude - f.radius;
            if (e < bd) { bd = e; best = f; }
        }
        edge = Mathf.Max(0f, bd);
        return best;
    }

    /// Every live rock in the chunks within `range` of `from` (true world coordinates).
    public List<int> RocksNear(Vector3 from, float range)
    {
        var outList = new List<int>();
        float span = range + CHUNK * 0.87f + Mathf.Min(ORBIT_SPEED * _elapsed, CHUNK);
        foreach (var ch in _chunks)
        {
            if ((ch.centre - from).sqrMagnitude > span * span) continue;
            foreach (var i in ch.rocks) if (alive[i]) outList.Add(i);
        }
        return outList;
    }

    /// The rocks within `range` of `from` right now (a cheap pass on the build positions with a drift allowance, then
    /// the exact check).
    public List<int> RocksWithin(Vector3 from, float range)
    {
        var outList = new List<int>();
        float allow = Mathf.Min(ORBIT_SPEED * _elapsed, CHUNK);
        foreach (var i in RocksNear(from, range))
        {
            float lim = range + radius[i] + allow;
            if ((pos[i] - from).sqrMagnitude > lim * lim) continue;
            if ((RockPos(i) - from).magnitude < range + radius[i]) outList.Add(i);
        }
        return outList;
    }

    /// The rock under the nose: a ray from `origin` along `dir` (true world), out to `reach`, with the browser game's
    /// aiming slack (about two degrees plus a few units). Returns the rock id, or -1.
    public int RayHit(Vector3 origin, Vector3 dir, float reach)
    {
        int best = -1;
        float bestT = float.PositiveInfinity;
        float span = reach + CHUNK * 0.87f + Mathf.Min(ORBIT_SPEED * _elapsed, CHUNK);
        foreach (var ch in _chunks)
        {
            if ((ch.centre - origin).sqrMagnitude > span * span) continue;
            foreach (var i in ch.rocks)
            {
                if (!alive[i]) continue;
                var to = RockPos(i) - origin;
                float t = Vector3.Dot(to, dir);
                if (t < 0f) continue;
                float r = radius[i];
                if (t - r > reach) continue;
                float d2 = to.sqrMagnitude - t * t;
                float tol = r + 8f + t * 0.035f;
                if (d2 < tol * tol && t - r < bestT)
                {
                    bestT = t - r;
                    best = i;
                }
            }
        }
        return best;
    }

    /// Ore-bearing live rocks within `range` of `from`: count and the nearest one's id. `onlyOre` narrows it to one
    /// ore index. A full scan (onlyOre < 0) marks the rocks for the radar readout, each reached as the pulse spreads.
    public int Scan(Vector3 from, float range, int onlyOre, float speed, out int nearest, out float dist)
    {
        int n = 0;
        nearest = -1;
        float nd = float.PositiveInfinity;
        float r2 = range * range;
        for (int i = 0; i < count; i++)
        {
            if (!alive[i] || ore[i] < 0 || (onlyOre >= 0 && ore[i] != onlyOre)) continue;
            float d2 = (RockPos(i) - from).sqrMagnitude;
            if (d2 < r2)
            {
                n++;
                if (d2 < nd) { nd = d2; nearest = i; }
                if (onlyOre < 0)
                {
                    if (markUntil[i] <= State.time) _marked.Add(i);
                    markUntil[i] = State.time + MARK_TIME;
                    markFrom[i] = State.time + (speed > 0f ? Mathf.Sqrt(d2) / speed : 0f);
                }
            }
        }
        dist = nearest >= 0 ? Mathf.Sqrt(nd) : 0f;
        return n;
    }

    public struct Mark { public int id; public float dist, left; }

    /// The rocks still carrying a radar mark, nearest first; expired ones drop off the list.
    public List<Mark> Marked(float now, Vector3 from, int maxN)
    {
        var keep = new List<int>();
        var outList = new List<Mark>();
        foreach (var i in _marked)
        {
            if (!alive[i] || markUntil[i] <= now) continue;
            keep.Add(i);
            if (markFrom[i] > now) continue;
            outList.Add(new Mark { id = i, dist = (RockPos(i) - from).magnitude, left = markUntil[i] - now });
        }
        _marked = keep;
        outList.Sort((a, b) => a.dist.CompareTo(b.dist));
        if (outList.Count > maxN) outList.RemoveRange(maxN, outList.Count - maxN);
        return outList;
    }

    /// Damage: a rock glows hotter the less health it has left (the HTML's body heat), red, then orange, then near-white.
    public void Damage(int i, float dmg)
    {
        hp[i] -= dmg;
        WriteRail(i);
    }

    /// The rock is gone; returns the ore that comes loose. A belt rock grows back after RESPAWN_AFTER; a fragment is
    /// simply gone.
    public float Kill(int i)
    {
        alive[i] = false;
        _lod0.Remove(i);
        _burns.Remove(i);
        _burnLast.Remove(i);
        if (batchOf[i] >= 0)
        {
            _batches[batchOf[i]].mats[slotOf[i]] = Matrix4x4.zero;
        }
        _freeIds.Remove(i);
        float loose = amount[i];
        amount[i] = 0f;
        hp[i] = 0f;
        if (!fragment[i])
        {
            respawnAt[i] = State.time + RESPAWN_AFTER;
            _dead.Add(i);
        }
        return loose;
    }

    public string RockName(int i)
    {
        string size = cls[i] > 0 ? CLS_NAME[cls[i]] + " " : "";
        string what = ore[i] < 0 ? "Barren" : Data.ORES[ore[i]].name;
        return size + what + " rock";
    }

    // ---- free rocks: knocked off the rail, coasting
    public void SetFree(int i, Vector3 v)
    {
        if (free[i]) { vel[i] = v; return; }
        pos[i] = RockPos(i);
        free[i] = true;
        vel[i] = v;
        _freeIds.Add(i);
        WriteInstance(i);
    }

    /// A free rock set down at a true position with a velocity (a rock shoved off the cargo ship's hull).
    public void PlaceFree(int i, Vector3 p, Vector3 v)
    {
        SetFree(i, v);
        pos[i] = p;
        if (batchOf[i] >= 0) WriteTranslation(i);
    }

    /// A shove from the ship: 80% of the closing speed, less the heavier the rock, and never faster than the ship.
    public void Bump(int i, Vector3 dir, float speed)
    {
        float push = speed * Mathf.Min(1f, 900f / (radius[i] * radius[i])) * 0.8f;
        if (push < 1.5f) return;
        if (!free[i]) SetFree(i, RockVel(i));
        var v = vel[i] + dir * push;
        float cap = speed + 40f;
        if (v.magnitude > cap) v = v.normalized * cap;
        vel[i] = v;
    }

    /// A piece of a broken rock (splitRock's fragments): a smaller rock of the next class down, adrift from the start.
    public int AddFragment(int c, float r, int oreI, bool barren, Vector3 p, float amt, Vector3 v, int bi)
    {
        float hpm = Mathf.Round(40f + 2.2f * Mathf.Pow(r, 1.15f));
        int i = AppendRock(p, r, barren ? -1 : oreI, hpm, barren ? 0f : amt, c, PickShape(c), 0f, 1f, bi, true, v);
        fragment[i] = true;
        glow[i] = 1f;   // fragments start hot and cool over 30 s
        _hot.Add(i);
        int ci = ChunkIndex(p);
        chunkOf[i] = ci;
        _chunks[ci].rocks.Add(i);
        Slot(i, ci);
        _freeIds.Add(i);
        return i;
    }

    /// Every frame: the drift time for the rock shader, free rocks coasting (bouncing off the zone edge, coming to rest
    /// on the planet), and broken belt rocks growing back once the ship is well away.
    public void Tick(float dt, Vector3 shipTrue, List<int> nearIds = null)
    {
        _elapsed += dt;
        Shader.SetGlobalFloat("_BeltTime", _elapsed);
        Shader.SetGlobalVector("_HeatPos0", new Vector4(_heatSrc[0].x - _offset.x, _heatSrc[0].y - _offset.y, _heatSrc[0].z - _offset.z, _heatRad[0]));
        Shader.SetGlobalVector("_HeatPos1", new Vector4(_heatSrc[1].x - _offset.x, _heatSrc[1].y - _offset.y, _heatSrc[1].z - _offset.z, _heatRad[1]));
        Shader.SetGlobalVector("_HeatAmt", new Vector4(_heatAmt[0], _heatAmt[1], 0f, 0f));
        // fresh fragments cool over 30 s
        for (int k = _hot.Count - 1; k >= 0; k--)
        {
            int i = _hot[k];
            if (!alive[i]) { _hot.RemoveAt(k); continue; }
            glow[i] = Mathf.Max(0f, glow[i] - dt / 30f);
            if (batchOf[i] >= 0) WriteRail(i);
            if (glow[i] <= 0f) _hot.RemoveAt(k);
        }
        TickScrap(dt, nearIds);
        for (int k = _freeIds.Count - 1; k >= 0; k--)
        {
            int i = _freeIds[k];
            if (!alive[i]) { _freeIds.RemoveAt(k); continue; }
            var p = pos[i] + vel[i] * dt;
            float d = p.magnitude;
            if (d > worldR)
            {
                vel[i] = Vector3.Reflect(vel[i], p / d);
                p -= p / d * (d - worldR);
            }
            else if (planetR > 0f && d < planetR + radius[i])
            {
                vel[i] = Vector3.zero;
                p += p / d * (planetR + radius[i] - d);
            }
            else
            {
                vel[i] *= Mathf.Exp(-0.02f * dt);
            }
            pos[i] = p;
            WriteTranslation(i);
        }
        TickPairs();
        _respawnT += dt;
        if (_respawnT > 1f)
        {
            _respawnT = 0f;
            for (int k = _dead.Count - 1; k >= 0; k--)
            {
                int i = _dead[k];
                if (State.time < respawnAt[i]) continue;
                if ((RockPos(i) - shipTrue).magnitude < 20000f) continue;   // never in front of the pilot
                Respawn(i);
                _dead.RemoveAt(k);
            }
        }
    }

    // ---- LOD 0: the rocks close to the ship draw on their own with the finest mesh, the rest ride their chunk's batch
    void Promote(int i)
    {
        _lod0[i] = new Lod0();
        if (batchOf[i] >= 0) { _batches[batchOf[i]].mats[slotOf[i]] = Matrix4x4.zero; }
    }

    void Demote(int i)
    {
        _lod0.Remove(i);
        if (alive[i] && batchOf[i] >= 0) WriteInstance(i);
    }

    /// Called with the rocks near the ship: the close ones draw LOD 0, the rest drop back to their chunk.
    public void UpdateLod0(Vector3 from, List<int> nearIds)
    {
        if (!_haveLod0) return;
        _lod0Keep.Clear();
        foreach (int i in nearIds)
        {
            if (i >= count || !alive[i]) continue;
            float d = (RockPos(i) - from).magnitude;
            bool has = _lod0.ContainsKey(i);
            if ((has && d < radius[i] * LOD0_OUT) || (!has && d < radius[i] * LOD0_RADII))
            {
                _lod0Keep.Add(i);
                if (!has) Promote(i);
            }
        }
        _lod0Gone.Clear();
        foreach (var kv in _lod0) if (!_lod0Keep.Contains(kv.Key)) _lod0Gone.Add(kv.Key);
        foreach (int i in _lod0Gone) Demote(i);
    }

    public int Lod0Count { get { return _lod0.Count; } }
    public bool IsLod0(int i) { return _lod0.ContainsKey(i); }

    // ---- scrap (spawnDebris / chunkRock): 5 to 16 chunks, 4 to 12 % of the parent's radius (never mistakable for a
    // rock you could cut), thrown out from the rock with the rock's own velocity plus a shove, spinning, white-hot at first
    public void SpawnScrap(int i, Vector3 v)
    {
        var p = RockPos(i);
        float r = radius[i];
        int n = new[] { 5, 8, 11, 16 }[Mathf.Clamp(cls[i], 0, 3)];
        for (int k = 0; k < n; k++)
        {
            if (_scrapPos.Count >= SCRAP_MAX) DropScrap(0);
            var dir = Random.onUnitSphere;
            float sr = r * Random.Range(0.04f, 0.12f);
            _scrapPos.Add(p + dir * r * Random.Range(0.2f, 0.7f));
            _scrapVel.Add(v + dir * Random.Range(14f, 55f));
            _scrapR.Add(sr);
            _scrapAxis.Add(Random.onUnitSphere);
            _scrapSpin.Add(Random.Range(0.3f, 1.4f));
            _scrapT.Add(0f);
            _scrapLife.Add(1800f + Random.Range(0f, 60f));
            _scrapRot.Add(Random.rotation);
        }
    }

    void DropScrap(int s)
    {
        _scrapPos.RemoveAt(s); _scrapVel.RemoveAt(s); _scrapR.RemoveAt(s); _scrapAxis.RemoveAt(s); _scrapSpin.RemoveAt(s); _scrapT.RemoveAt(s); _scrapLife.RemoveAt(s); _scrapRot.RemoveAt(s);
    }

    void ClearScrap()
    {
        _scrapPos.Clear(); _scrapVel.Clear(); _scrapR.Clear(); _scrapAxis.Clear(); _scrapSpin.Clear(); _scrapT.Clear(); _scrapLife.Clear(); _scrapRot.Clear();
    }

    public int ScrapCount { get { return _scrapPos.Count; } }
    public Vector3 ScrapPos(int s) { return _scrapPos[s]; }
    public float ScrapR(int s) { return _scrapR[s]; }
    public Vector3 ScrapVel(int s) { return _scrapVel[s]; }

    /// The ship hit a chunk: scrap is light, so the shove is the ship's share of the momentum (chunkRock / the solids loop).
    public void ScrapHit(int s, Vector3 n, float vn, float cv)
    {
        float mass = _scrapR[s] * _scrapR[s] * _scrapR[s] / 1000f;
        float share = 10f / (10f + mass);
        var v = _scrapVel[s];
        if (vn < 0f) v += n * vn * 1.5f * share;
        if (cv > 0f) v -= n * cv * 1.4f;
        _scrapVel[s] = v;
        _scrapSpin[s] = Mathf.Min(2.5f, _scrapSpin[s] + 0.5f);
    }

    static long CellKey(Vector3 q)
    {
        long x = Mathf.FloorToInt(q.x / 600f), y = Mathf.FloorToInt(q.y / 600f), z = Mathf.FloorToInt(q.z / 600f);
        return ((x + 1000000L) << 42) ^ ((y + 1000000L) << 21) ^ (z + 1000000L);
    }

    readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

    void TickScrap(float dt, List<int> nearIds)
    {
        if (_scrapPos.Count == 0) return;
        for (int s = _scrapPos.Count - 1; s >= 0; s--)
        {
            float t = _scrapT[s] + dt;
            _scrapT[s] = t;
            if (_scrapLife[s] - t <= 0f) { DropScrap(s); continue; }
            var p = _scrapPos[s] + _scrapVel[s] * dt;
            // a chunk bounces off the rocks near the ship (chunkRock)
            if (nearIds != null)
            {
                foreach (int i in nearIds)
                {
                    if (i >= count || !alive[i]) continue;
                    var rp = RockPos(i);
                    var to = p - rp;
                    float d = to.magnitude;
                    float minD = radius[i] * 0.92f + _scrapR[s];
                    if (d < 1e-3f || d >= minD) continue;
                    var nrm = to / d;
                    p = rp + nrm * minD;
                    float vn = Vector3.Dot(_scrapVel[s], nrm) - (free[i] ? Vector3.Dot(vel[i], nrm) : 0f);
                    if (vn < 0f)
                    {
                        _scrapVel[s] -= nrm * vn * 1.5f;
                        _scrapSpin[s] = Mathf.Min(2.5f, _scrapSpin[s] + 0.4f);
                    }
                }
            }
            _scrapPos[s] = p;
        }
        // chunks bounce off one another (collideDebris): pairs found through a coarse spatial hash, mass-weighted, a little inelastic
        int nsc = _scrapPos.Count;
        if (nsc > 1)
        {
            _cells.Clear();
            for (int k = 0; k < nsc; k++)
            {
                long key = CellKey(_scrapPos[k]);
                List<int> arr;
                if (!_cells.TryGetValue(key, out arr)) { arr = new List<int>(); _cells[key] = arr; }
                arr.Add(k);
            }
            for (int k = 0; k < nsc; k++)
            {
                var q = _scrapPos[k];
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                {
                    List<int> arr;
                    if (!_cells.TryGetValue(CellKey(q + new Vector3(dx * 600f, dy * 600f, dz * 600f)), out arr)) continue;
                    foreach (int c in arr)
                    {
                        if (c <= k) continue;
                        var n = _scrapPos[c] - _scrapPos[k];
                        float d = n.magnitude;
                        float minD = _scrapR[k] + _scrapR[c];
                        if (d >= minD || d < 1e-3f) continue;
                        n /= d;
                        float mk = Mathf.Pow(_scrapR[k], 3f) / 1000f, mc = Mathf.Pow(_scrapR[c], 3f) / 1000f;
                        float tot = mk + mc;
                        float overlap = minD - d;
                        _scrapPos[k] -= n * overlap * mc / tot;
                        _scrapPos[c] += n * overlap * mk / tot;
                        float vrel = Vector3.Dot(_scrapVel[c], n) - Vector3.Dot(_scrapVel[k], n);
                        if (vrel >= 0f) continue;
                        float jimp = -(1f + 0.55f) * vrel / (1f / mk + 1f / mc);
                        _scrapVel[k] -= n * jimp / mk;
                        _scrapVel[c] += n * jimp / mc;
                        _scrapSpin[k] = Mathf.Min(2.5f, _scrapSpin[k] + Mathf.Abs(jimp) / mk * 0.02f);
                        _scrapSpin[c] = Mathf.Min(2.5f, _scrapSpin[c] + Mathf.Abs(jimp) / mc * 0.02f);
                    }
                }
            }
        }
        for (int s = 0; s < _scrapPos.Count; s++)
        {
            float t = _scrapT[s];
            float left = _scrapLife[s] - t;
            var rq = Quaternion.AngleAxis(_scrapSpin[s] * dt * Mathf.Rad2Deg, _scrapAxis[s]) * _scrapRot[s];
            _scrapRot[s] = rq;
            float sc = Mathf.Clamp(left / 3f, 0.01f, 1f) * _scrapR[s];   // the last three seconds shrink it away
            _scrapMats[s] = Matrix4x4.TRS(_scrapPos[s] - _offset, rq, Vector3.one * sc);
            _scrapCols[s] = new Vector4(0.36f, 0.34f, 0.31f, 0f);   // ordinary stone, with heat carried separately in _Rail.w
            _scrapRails[s] = new Vector4(0f, 1f, 0f, Mathf.Pow(Mathf.Max(0f, 1f - t / 30f), 1.6f));
        }
    }

    // ---- rock-on-rock (rockPair): only rocks that are adrift need pair tests. Along the line between two rocks the
    // overlap pushes both out, mass-weighted, and knocks both off their rails with a soft bounce.
    readonly Dictionary<int, List<int>> _pairCache = new Dictionary<int, List<int>>();
    int _pairFrame;

    void TickPairs()
    {
        if (_freeIds.Count == 0) return;
        _pairFrame++;
        bool refresh = _pairFrame % 30 == 1;
        for (int k = 0; k < _freeIds.Count; k++)
        {
            int i = _freeIds[k];
            if (!alive[i]) continue;
            var pa = pos[i];
            float ra = radius[i];
            List<int> cands;
            if (refresh || !_pairCache.TryGetValue(i, out cands))
            {
                cands = RocksWithin(pa, ra * 3f + 1500f);
                _pairCache[i] = cands;
            }
            foreach (int j in cands)
            {
                if (j == i || j >= count || !alive[j]) continue;
                var pb = RockPos(j);
                var n = pb - pa;
                float d = n.magnitude;
                float minD = (ra + radius[j]) * 0.92f + 4f;
                if (d < 1e-3f || d >= minD) continue;
                n /= d;
                float ma = ra * ra * ra, mb = radius[j] * radius[j] * radius[j];
                float tot = ma + mb;
                float overlap = minD - d;
                if (!free[j]) SetFree(j, RockVel(j));
                pos[i] = pa - n * overlap * mb / tot;
                pos[j] = pb + n * overlap * ma / tot;
                pa = pos[i];
                WriteTranslation(j);
                float vrel = Vector3.Dot(vel[j], n) - Vector3.Dot(vel[i], n);
                if (vrel >= 0f) continue;
                float jimp = -(1f + 0.3f) * vrel / (1f / ma + 1f / mb);
                vel[i] -= n * jimp / ma;
                vel[j] += n * jimp / mb;
            }
            WriteTranslation(i);
        }
    }

    /// A broken belt rock grows back somewhere in its chunk, on a fresh rail, whole again.
    void Respawn(int i)
    {
        var b = beltOf[i] < belts.Count ? belts[beltOf[i]] : null;
        var centre = chunkOf[i] >= 0 ? _chunks[chunkOf[i]].centre : pos[i];
        var target = RockPos(i);
        if (b != null)
        {
            for (int t = 0; t < 30; t++)
            {
                var q = centre + new Vector3(_rng.Range(-0.5f, 0.5f), _rng.Range(-0.5f, 0.5f), _rng.Range(-0.5f, 0.5f)) * CHUNK;
                float o = Mathf.Sqrt(q.x * q.x + q.z * q.z);
                if (o >= b.rMin && o <= b.rMax && q.magnitude > planetR * 1.02f) { target = q; break; }
            }
        }
        if (free[i]) { free[i] = false; vel[i] = Vector3.zero; _freeIds.Remove(i); }
        ang[i] = Mathf.Atan2(target.z, target.x);
        orbit[i] = Mathf.Max(1f, Mathf.Sqrt(target.x * target.x + target.z * target.z));
        pos[i] = target - Delta(ang[i], orbit[i]);
        hp[i] = hpMax[i];
        amount[i] = amountMax[i];
        alive[i] = true;
        respawnAt[i] = 0f;
        markUntil[i] = 0f;
        WriteInstance(i);
    }

    // ---- drawing
    /// Move every instance so that true world coordinates minus `offset` land in scene space (floating origin).
    public void ApplyOffset(Vector3 offset)
    {
        _offset = offset;
        for (int i = 0; i < count; i++)
        {
            if (batchOf[i] >= 0 && alive[i]) WriteTranslation(i);
        }
    }

    /// Which chunks draw, and at which detail, for a viewer at `from` (true world).
    public void Cull(Vector3 from)
    {
        float lim = DRAW_DIST + CHUNK * 0.87f;
        float near = LOD1_DIST + CHUNK * 0.87f;
        foreach (var ch in _chunks)
        {
            float d = (ch.centre - from).magnitude;
            ch.visible = d < lim;
            ch.lod = d < near ? 1 : 2;
        }
    }

    public int VisibleChunks()
    {
        int n = 0;
        foreach (var ch in _chunks) if (ch.visible) n++;
        return n;
    }

    public int ChunkCount { get { return _chunks.Count; } }

    public Material RockMaterial { get { return _mat; } }
    public Mesh MeshFor(int key, int lod) { return _meshes[key, lod]; }

    /// What the renderer thinks of the rock material and the instancing path, for the smoke log.
    public string DrawReport()
    {
        int batches = 0, insts = 0;
        foreach (var ch in _chunks)
        {
            if (!ch.visible) continue;
            foreach (var bi in ch.batches) { batches++; insts += _batches[bi].count; }
        }
        return "shader " + _mat.shader.name + " supported=" + _mat.shader.isSupported + " instancing=" + _mat.enableInstancing + " · library shapes " + libraryShapes + " · device " + SystemInfo.graphicsDeviceType + " supportsInstancing=" + SystemInfo.supportsInstancing + " · visible batches " + batches + " with " + insts + " instances";
    }

    /// Submit every visible batch for this frame.
    public void Draw()
    {
        foreach (var ch in _chunks)
        {
            if (!ch.visible) continue;
            foreach (var bi in ch.batches)
            {
                var b = _batches[bi];
                if (b.count == 0) continue;
                if (b.dirty)
                {
                    b.mpb.SetVectorArray("_Color", b.colors);
                    b.mpb.SetVectorArray("_Rail", b.rails);
                    b.dirty = false;
                }
                var mesh = _meshes[b.key, ch.lod];
                var mats = _subMats[b.key];
                int subs = Mathf.Min(mesh.subMeshCount, mats.Length);
                for (int sub = 0; sub < subs; sub++)
                {
                    Graphics.DrawMeshInstanced(mesh, sub, mats[sub], b.mats, b.count, b.mpb, ShadowCastingMode.On, true, 0, null);
                }
            }
        }
        // the rocks up close, one draw each with the finest mesh
        foreach (var kv in _lod0)
        {
            int i = kv.Key;
            if (!alive[i] || batchOf[i] < 0) continue;
            var e = kv.Value;
            var bt = _batches[batchOf[i]];
            e.mats[0] = Matrix4x4.TRS(pos[i] - _offset, rot[i], scl[i]);
            e.colors[0] = bt.colors[slotOf[i]];
            e.rails[0] = new Vector4(ang[i], orbit[i], free[i] ? 0f : 1f, BodyHeat(i));
            e.mpb.SetVectorArray("_Color", e.colors);
            e.mpb.SetVectorArray("_Rail", e.rails);
            var mesh = _meshes[meshKey[i], 0];
            var mats = _subMats[meshKey[i]];
            int subs = Mathf.Min(mesh.subMeshCount, mats.Length);
            for (int sub = 0; sub < subs; sub++) Graphics.DrawMeshInstanced(mesh, sub, mats[sub], e.mats, 1, e.mpb, ShadowCastingMode.On, true, 0, null);
        }
        DrawBurns();
        // the scrap
        int ns = _scrapPos.Count;
        if (ns > 0)
        {
            _scrapMpb.SetVectorArray("_Color", _scrapCols);
            _scrapMpb.SetVectorArray("_Rail", _scrapRails);
            var mesh = _meshes[_scrapKey, 2];
            var mats = _subMats[_scrapKey];
            int subs = Mathf.Min(mesh.subMeshCount, mats.Length);
            for (int sub = 0; sub < subs; sub++) Graphics.DrawMeshInstanced(mesh, sub, mats[sub], _scrapMats, ns, _scrapMpb, ShadowCastingMode.On, true, 0, null);
        }
    }
}
