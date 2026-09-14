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
    const float COLOSSAL_LOOSE = 0.146f;
    const float BARREN_SHARE = 2f / 3f;
    const float COLOSSAL_ORE_SHARE = 0.001f;
    const int BATCH_MAX = 1023;
    static readonly int[] FIELDS_PER_BELT = { 7, 7, 9, 0 };
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
    Mesh[,] _meshes;   // [key, lod] with lod 0 = near, 1 = far
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
        _meshes = new Mesh[RockMeshes.SHAPES.Length * 2, 2];
        for (int s = 0; s < RockMeshes.SHAPES.Length; s++)
        {
            for (int v = 0; v < 2; v++)
            {
                _meshes[s * 2 + v, 0] = RockMeshes.Build(RockMeshes.SHAPES[s], 1, s * 2 + v + 1);
                _meshes[s * 2 + v, 1] = RockMeshes.Build(RockMeshes.SHAPES[s], 0, s * 2 + v + 1);
                // the rail drift happens in the vertex shader, so a rock can sit up to a chunk away from its matrix;
                // bounds wide enough (in mesh units, scaled by the smallest radius) keep the renderer from culling it
                _meshes[s * 2 + v, 0].bounds = new Bounds(Vector3.zero, Vector3.one * 60000f);
                _meshes[s * 2 + v, 1].bounds = new Bounds(Vector3.zero, Vector3.one * 60000f);
            }
        }
    }

    public void Clear()
    {
        pos.Clear(); radius.Clear(); ore.Clear(); hp.Clear(); hpMax.Clear(); amount.Clear(); amountMax.Clear(); cls.Clear(); alive.Clear();
        meshKey.Clear(); chunkOf.Clear(); batchOf.Clear(); slotOf.Clear(); ang.Clear(); orbit.Clear(); free.Clear(); vel.Clear(); fragment.Clear();
        beltOf.Clear(); respawnAt.Clear(); markUntil.Clear(); markFrom.Clear(); rot.Clear(); scl.Clear();
        count = 0;
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
        ang.Add(ra); orbit.Add(Mathf.Max(1f, ro)); free.Add(isFree); vel.Add(v); fragment.Add(false); beltOf.Add(bi); respawnAt.Add(0f);
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

    Color RockColor(int i)
    {
        var stone = Color.Lerp(new Color(0.36f, 0.34f, 0.31f), new Color(0.26f, 0.25f, 0.24f), _rng.Value());
        if (ore[i] < 0) return stone;
        return Color.Lerp(stone, Data.ORES[ore[i]].color, 0.55f);
    }

    /// The instance's matrix, colour and rail (angle, radius, on-rail flag, body heat) for the shader.
    void WriteInstance(int i)
    {
        var b = _batches[batchOf[i]];
        int s = slotOf[i];
        b.mats[s] = alive[i] ? Matrix4x4.TRS(pos[i] - _offset, rot[i], scl[i]) : Matrix4x4.zero;
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
        var b = _batches[batchOf[i]];
        var m = b.mats[slotOf[i]];
        var p = pos[i] - _offset;
        m.m03 = p.x; m.m13 = p.y; m.m23 = p.z;
        b.mats[slotOf[i]] = m;
    }

    float BodyHeat(int i)
    {
        return Mathf.Pow(Mathf.Max(0f, 1f - hp[i] / Mathf.Max(1f, hpMax[i])), 1.3f);
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
        int ci = ChunkIndex(p);
        chunkOf[i] = ci;
        _chunks[ci].rocks.Add(i);
        Slot(i, ci);
        _freeIds.Add(i);
        return i;
    }

    /// Every frame: the drift time for the rock shader, free rocks coasting (bouncing off the zone edge, coming to rest
    /// on the planet), and broken belt rocks growing back once the ship is well away.
    public void Tick(float dt, Vector3 shipTrue)
    {
        _elapsed += dt;
        Shader.SetGlobalFloat("_BeltTime", _elapsed);
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
            ch.lod = d < near ? 0 : 1;
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
        return "shader " + _mat.shader.name + " supported=" + _mat.shader.isSupported + " instancing=" + _mat.enableInstancing + " · device " + SystemInfo.graphicsDeviceType + " supportsInstancing=" + SystemInfo.supportsInstancing + " · visible batches " + batches + " with " + insts + " instances";
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
                Graphics.DrawMeshInstanced(_meshes[b.key, ch.lod], 0, _mat, b.mats, b.count, b.mpb, ShadowCastingMode.On, true, 0, null);
            }
        }
    }
}
