using UnityEngine;
using UnityEngine.Rendering;

/// Space dust (2026-09-23, the user's: "make it so the space dust moves past you at the speed you are traveling"): a
/// field of motes fixed in space round the camera, so flying through it shows the true speed and nothing else. A mote
/// is a dot at rest and a streak at speed, stretched along the way it passes (the last 12 ms of its travel: 19 u at
/// the 1,600 u/s cruise, twice that on the burner). The field is a box 900 u a side that wraps round the camera, so
/// it is endless in every direction; a mote fades in from 40 u out (past the cockpit's cabin) to 90 u and out again
/// from 340 u to the box's edge, and the whole field is gone inside the carrier's reach (the hangar), while docked and
/// while warping. Drawn like the sparks, with their mesh and shader: instanced boxes, additive, no depth write, so the
/// cabin's walls, the hull and any rock in front hide them; positions are true coordinates, placed against the
/// floating origin each frame.
public class Dust
{
    public const int COUNT = 1600;
    public const float HALF = 450f;                    // the box round the camera: 900 u a side
    public const float NEAR = 40f, NEAR_FULL = 90f;    // faded in over this range out from the eye
    public const float FAR_FADE = 340f;                // fading out from here to the box's edge
    public const float TRAIL = 0.012f;                 // seconds of travel a streak shows
    public const float WIDTH = 0.25f;                  // a mote's width, units: about 3 px at 90 u on the cockpit's lens
    public const float PIXEL = 0.0013f;                // the width grows with distance, so a far mote keeps about 1.5 px
    public const float BRIGHT = 0.2f;    // faint: the user asked for subtle (2026-09-23; it was 0.45)
    public const float CARRIER_CLEAR = 1500f, CARRIER_FULL = 3000f;   // the field's fade by distance from the carrier
    const int BATCH = 1023;

    readonly Vector3[] _pos = new Vector3[COUNT];      // true coordinates
    readonly float[] _tone = new float[COUNT];         // a mote's own brightness
    readonly Matrix4x4[][] _mats;
    readonly Vector4[][] _cols;
    readonly MaterialPropertyBlock[] _mpb;
    readonly int[] _count;
    readonly Mesh _box;
    readonly Material _mat;
    bool _seeded;
    public int drawn;      // motes laid out this frame
    public float streak;   // the streak length this frame, units
    public float gain;     // the field's fade this frame, 0..1

    public Dust(Sparks sparks)
    {
        int nb = (COUNT + BATCH - 1) / BATCH;
        _mats = new Matrix4x4[nb][];
        _cols = new Vector4[nb][];
        _mpb = new MaterialPropertyBlock[nb];
        _count = new int[nb];
        for (int b = 0; b < nb; b++) { _mats[b] = new Matrix4x4[BATCH]; _cols[b] = new Vector4[BATCH]; _mpb[b] = new MaterialPropertyBlock(); }
        _box = sparks.BoxMesh;
        _mat = sparks.SparkMaterial;
        for (int i = 0; i < COUNT; i++) _tone[i] = Random.Range(0.25f, 1f);
    }

    public string State { get { return drawn + " motes · streak " + streak.ToString("0.0") + " u · gain " + gain.ToString("0.00"); } }

    static Vector3 Somewhere(Vector3 c) { return c + new Vector3(Random.Range(-HALF, HALF), Random.Range(-HALF, HALF), Random.Range(-HALF, HALF)); }

    /// Every frame: the field kept round the camera (a mote that left the box comes back in on the far side; after a
    /// jump it is thrown afresh), and each mote laid out as a box along the way it passes, faded by its distance and
    /// the field's gain `k`. `vel` is the ship's, true units a second; `offset` the floating origin.
    public void Tick(Vector3 camTrue, Vector3 vel, float k, Vector3 offset)
    {
        if (!_seeded) { for (int i = 0; i < COUNT; i++) _pos[i] = Somewhere(camTrue); _seeded = true; }
        gain = k;
        for (int b = 0; b < _count.Length; b++) _count[b] = 0;
        drawn = 0;
        float speed = vel.magnitude;
        streak = speed * TRAIL;
        var dir = speed > 1e-3f ? vel / speed : Vector3.forward;
        var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
        var rot = Quaternion.LookRotation(dir, up);
        float size = 2f * HALF;
        for (int i = 0; i < COUNT; i++)
        {
            var p = _pos[i];
            var d = p - camTrue;
            if (Mathf.Abs(d.x) > 3f * HALF || Mathf.Abs(d.y) > 3f * HALF || Mathf.Abs(d.z) > 3f * HALF) { p = Somewhere(camTrue); d = p - camTrue; }
            if (d.x > HALF) { p.x -= size; d.x -= size; } else if (d.x < -HALF) { p.x += size; d.x += size; }
            if (d.y > HALF) { p.y -= size; d.y -= size; } else if (d.y < -HALF) { p.y += size; d.y += size; }
            if (d.z > HALF) { p.z -= size; d.z -= size; } else if (d.z < -HALF) { p.z += size; d.z += size; }
            _pos[i] = p;
            if (k <= 0.001f) continue;
            float dist = d.magnitude;
            if (dist < NEAR || dist > HALF) continue;
            float fade = Mathf.Clamp01((dist - NEAR) / (NEAR_FULL - NEAR)) * Mathf.Clamp01((HALF - dist) / (HALF - FAR_FADE));
            if (fade <= 0.002f) continue;
            float w = Mathf.Max(WIDTH, dist * PIXEL);
            float len = Mathf.Max(w, streak);
            // a streak spreads its light along its length, so it dims as it stretches, though never below 45%; and
            // a far mote, widened to keep its pixel, is dimmed by as much
            float bright = BRIGHT * k * fade * _tone[i] * (WIDTH / w) * Mathf.Clamp(8f * w / len, 0.45f, 1f);
            int bi = drawn / BATCH, si = drawn % BATCH;
            // the streak is the mote's trail over the last moment: relative to the ship it moves against the way of
            // travel, so the trail lies ahead of it, along the velocity
            _mats[bi][si] = Matrix4x4.TRS(p - offset + dir * (len * 0.5f), rot, new Vector3(w, w, len));
            _cols[bi][si] = new Vector4(0.75f, 0.85f, 1f, bright);
            _count[bi] = si + 1;
            drawn++;
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
