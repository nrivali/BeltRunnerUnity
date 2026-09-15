using UnityEngine;

/// Speed lines for the afterburner: faint pale streaks scattered in a tube ahead of the ship, fixed in space, so the
/// ship streams past them; each is a soft quad stretched along the line of flight and turned to face the camera. They
/// fade in over a third of a second when the burner lights and out over half a second when it stops, and a streak
/// that falls behind the ship is respawned ahead. True coordinates, like everything else.
public class SpeedLines
{
    const int N = 56;
    readonly Transform[] _t = new Transform[N];
    readonly Vector3[] _pos = new Vector3[N];
    readonly float[] _len = new float[N], _w = new float[N], _a = new float[N];
    readonly bool[] _live = new bool[N];
    Material _mat;
    Transform _root;
    float _k;   // 0..1, eased

    public SpeedLines()
    {
        _root = new GameObject("SpeedLines").transform;
        _mat = Ship.SoftMaterial(new Color(0.75f, 0.85f, 1f, 0.3f));
        var quad = MeshUtil.Quad(1f, 1f, 1f, 1f);
        for (int i = 0; i < N; i++)
        {
            var go = new GameObject("Streak");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            _t[i] = go.transform;
        }
    }

    void Spawn(int i, Vector3 ship, Vector3 fwd, float ahead)
    {
        // a point in a tube round the line of flight, `ahead` units on, clear of the ship itself
        var side = Vector3.Cross(fwd, Mathf.Abs(fwd.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var up = Vector3.Cross(side, fwd).normalized;
        float ang = Random.Range(0f, Mathf.PI * 2f), rad = Random.Range(45f, 190f);
        _pos[i] = ship + fwd * ahead + side * Mathf.Cos(ang) * rad + up * Mathf.Sin(ang) * rad;
        _len[i] = Random.Range(0.7f, 1.3f);
        _w[i] = Random.Range(0.5f, 1.1f);
        _a[i] = Random.Range(0.5f, 1f);
        _live[i] = true;
    }

    public void Tick(float dt, Vector3 ship, Vector3 fwd, Vector3 vel, bool on, Vector3 off, Camera cam)
    {
        _k = Mathf.Lerp(_k, on ? 1f : 0f, 1f - Mathf.Exp(-(on ? 3f : 2f) * dt));
        if (_k < 0.02f)
        {
            for (int i = 0; i < N; i++) if (_live[i]) { _live[i] = false; _t[i].gameObject.SetActive(false); }
            return;
        }
        float speed = vel.magnitude;
        var dir = speed > 1f ? vel / speed : fwd;
        float len = Mathf.Clamp(speed * 0.16f, 30f, 160f);
        var camPos = cam != null ? cam.transform.position + off : ship;
        _mat.SetColor("_Color", new Color(0.75f, 0.85f, 1f, 0.32f * _k));
        for (int i = 0; i < N; i++)
        {
            if (!_live[i]) Spawn(i, ship, fwd, Random.Range(150f, 1400f));
            float along = Vector3.Dot(_pos[i] - ship, dir);
            if (along < -220f || along > 1800f) Spawn(i, ship, fwd, Random.Range(900f, 1400f));
            var t = _t[i];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            var scene = _pos[i] - off;
            t.position = scene;
            var toCam = (camPos - _pos[i]).normalized;
            t.rotation = Quaternion.LookRotation(toCam, dir);
            // thinner and fainter far ahead, full as they pass
            float near = Mathf.Clamp01(1f - along / 1800f);
            t.localScale = new Vector3(_w[i] * (0.6f + 0.8f * near), len * _len[i] * (0.5f + 0.5f * near), 1f);
        }
    }
}
