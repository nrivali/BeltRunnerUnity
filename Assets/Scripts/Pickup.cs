using UnityEngine;

/// A loose lump of ore from a broken rock. It drifts, and once the ship is close it is pulled aboard (the browser
/// game's spawnDrop / updateDrops). Positions here are scene-local (the floating origin shifts them with everything).
public class Pickup : MonoBehaviour
{
    public const float PULL_RANGE = 900f;
    public const float GRAB_RANGE = Data.SHIP_R * 2.2f;
    public const float LIFE = 240f;

    public string ore;
    public float units;
    public Vector3 vel;
    public float age;
    public float noPick;   // seconds before the ship can pull it in
    public object claimed; // the collector drone heading for this lump, so two never chase the same one

    static Mesh _mesh;

    public static Pickup Make(Transform parent, string oreKey, float oreUnits, Vector3 at, Vector3 drift)
    {
        var go = new GameObject("Pickup " + oreKey);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = Vector3.one * 12f;
        var mf = go.AddComponent<MeshFilter>();
        if (_mesh == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
        }
        mf.sharedMesh = _mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var c = Data.ORES[Data.OreIndex(oreKey)].color;
        var mat = new Material(Game.Sh("Standard"));
        mat.color = c;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", c * 2f);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var p = go.AddComponent<Pickup>();
        p.ore = oreKey;
        p.units = oreUnits;
        p.vel = drift;
        return p;
    }

    /// Returns true when the pickup has been taken aboard (or has expired) and should go.
    public bool Tick(float dt, Vector3 shipPos)
    {
        age += dt;
        var pos = transform.localPosition;
        var toShip = shipPos - pos;
        float d = toShip.magnitude;
        if (age < noPick)
        {
            vel *= Mathf.Exp(-0.4f * dt);
            transform.localPosition = pos + vel * dt;
            transform.Rotate(Vector3.up, 90f * dt, Space.Self);
            return false;
        }
        if (d < GRAB_RANGE)
        {
            float took = State.AddCargo(ore, units);
            if (took > 0f) { Audio.Play("pickup"); return true; }
            vel = -toShip.normalized * 60f;   // hold full: the lump bounces off and waits
        }
        else if (d < PULL_RANGE)
        {
            vel = Vector3.Lerp(vel, toShip / d * 320f, 1f - Mathf.Exp(-4f * dt));
        }
        else
        {
            vel *= Mathf.Exp(-0.4f * dt);
        }
        transform.localPosition = pos + vel * dt;
        transform.Rotate(Vector3.up, 90f * dt, Space.Self);
        return age > LIFE;
    }
}
