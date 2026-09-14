using System.Collections.Generic;
using UnityEngine;

/// Collector drones, a cargo ship upgrade, ported from makeDrone / updateCollectors in belt-runner-3d.html (via the
/// Godot port). Stubby cargo drones wait at their docks off the starboard corner of mouth 1, fly out to loose ore
/// within range, gather it into their hold and bring it back: in through the nearest hangar mouth, down the starboard
/// side to the drop-off pad, a pause to unload into the cargo ship storage, and out the far mouth. Each drone claims
/// the lump it is heading for so two never chase the same one; if the ship scoops a claimed lump first the drone just
/// picks another. Drone positions are true world coordinates; the transforms are placed against the floating origin.
public class Drones
{
    const float LANE_X = 250f;     // the drop-off pad's x: the run home follows this lane through the hangar
    const float MOUTH_Z = 1400f;   // BAY_Z_OUT + 500: the point outside a mouth the drones aim for

    public class Drone
    {
        public Transform node;
        public Vector3 pos, vel;
        public List<Transform> eng = new List<Transform>();
        public Renderer strobe;
        public string phase = "idle";
        public Pickup target;
        public float load, t, wait;
        public Dictionary<string, float> cargo = new Dictionary<string, float>();
        public int idx, side;
    }

    public Game game;
    public CargoShip carrier;
    public readonly List<Drone> drones = new List<Drone>();
    Transform _root;

    Vector3 Dock(int i)
    {
        Vector3 a;
        if (!carrier.anchors.TryGetValue("drone_dock_" + Mathf.Min(i, 2), out a)) a = new Vector3(760f + i * 90f, -60f, 1500f);
        if (i > 2) a += new Vector3((i - 2) * 90f, 0f, 0f);
        return carrier.ToTrue(a);
    }

    Vector3 DropPad()
    {
        Vector3 a;
        return carrier.anchors.TryGetValue("drop_pad", out a) ? a : new Vector3(250f, -130f, 0f);
    }

    Drone Make(int i)
    {
        if (_root == null) _root = new GameObject("Drones").transform;
        var g = new GameObject("Drone" + i);
        g.transform.SetParent(_root, false);
        var metal = new Material(Game.Sh("Standard"));
        metal.color = Data.Hex("#b4bccf");
        metal.SetFloat("_Metallic", 0.5f);
        metal.SetFloat("_Glossiness", 0.5f);
        var dark = new Material(Game.Sh("Standard"));
        dark.color = Data.Hex("#66709a");
        var win = new Material(Game.Sh("Standard"));
        win.color = Data.Hex("#ffd9a0");
        win.EnableKeyword("_EMISSION");
        win.SetColor("_EmissionColor", Data.Hex("#ffd9a0") * 2f);
        Box(g.transform, new Vector3(10, 6, 18), metal, Vector3.zero);
        Box(g.transform, new Vector3(12, 7, 9), dark, new Vector3(0, -1, -2));
        Box(g.transform, new Vector3(6, 3, 5), win, new Vector3(0, 4, 5));
        var d = new Drone { node = g.transform, pos = Dock(i), vel = carrier.vel, idx = i, t = Random.value * 6f };
        var em = new Material(Game.Sh("Standard"));
        em.color = Data.Hex("#8fe8ff");
        em.EnableKeyword("_EMISSION");
        em.SetColor("_EmissionColor", Data.Hex("#8fe8ff") * 3f);
        foreach (var sx in new[] { -1f, 1f })
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(e.GetComponent<Collider>());
            e.transform.SetParent(g.transform, false);
            e.transform.localPosition = new Vector3(sx * 5f, 0f, -11f);
            e.transform.localScale = Vector3.one * 5f;
            e.GetComponent<MeshRenderer>().sharedMaterial = em;
            d.eng.Add(e.transform);
        }
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(g.transform, false);
        s.transform.localPosition = new Vector3(0, 5, -4);
        s.transform.localScale = Vector3.one * 2.4f;
        var sm = new Material(Game.Sh("Standard"));
        sm.color = Data.Hex("#5ed3f0");
        sm.EnableKeyword("_EMISSION");
        sm.SetColor("_EmissionColor", Data.Hex("#5ed3f0") * 3f);
        s.GetComponent<MeshRenderer>().sharedMaterial = sm;
        d.strobe = s.GetComponent<MeshRenderer>();
        return d;
    }

    static void Box(Transform parent, Vector3 size, Material mat, Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// A drone on the pad stows what fits in the cargo ship storage and keeps the rest aboard for the next visit.
    void Unload(Drone c)
    {
        float units = 0f, left = 0f;
        var keys = new List<string>(c.cargo.Keys);
        foreach (var k in keys)
        {
            float u = c.cargo[k];
            if (u <= 0.01f) { c.cargo.Remove(k); continue; }
            float fit = Mathf.Min(u, State.StoreRoom(k));
            if (fit > 0.01f)
            {
                State.store[k] += fit;
                c.cargo[k] -= fit;
                units += fit;
            }
            if (c.cargo[k] <= 0.01f) c.cargo.Remove(k); else left += c.cargo[k];
        }
        c.load = left;
        if (units > 0.5f)
        {
            State.droneUnits += units;
            Audio.Play("stow");
            game.Toast("Collector " + (c.idx + 1) + " stowed " + Mathf.RoundToInt(units) + " aboard the cargo ship" + (left > 0.5f ? " · storage full, " + Mathf.RoundToInt(left) + " kept aboard" : ""), false);
            if (game.ship.docked) game.OnDocked(true);
        }
        else if (left > 0.5f)
        {
            game.Toast("Collector " + (c.idx + 1) + " · cargo ship storage is full", true);
        }
    }

    /// Every drone back at its dock with an empty hold (a warp, a zone change).
    public void Reset()
    {
        foreach (var c in drones)
        {
            c.phase = "idle";
            c.side = 0;
            c.target = null;
            c.cargo.Clear();
            c.load = 0f;
            c.pos = Dock(c.idx);
            c.vel = carrier.vel;
        }
        foreach (var p in game.Drops) if (p != null) p.claimed = null;
    }

    public void Tick(float dt)
    {
        var L = Data.DEPOT_UPGRADES["collectors"].levels[State.depot["collectors"]];
        int want = L != null ? L.ships : 0;
        while (drones.Count < want) drones.Add(Make(drones.Count));
        while (drones.Count > want)
        {
            var last = drones[drones.Count - 1];
            drones.RemoveAt(drones.Count - 1);
            Object.Destroy(last.node.gameObject);
        }
        if (L == null) return;
        float range = L.range, cap = L.cap, speed = L.speed;
        var offset = game.worldOffset;
        var pickups = game.Drops;
        foreach (var c in drones)
        {
            c.t += dt;
            var pos = c.pos;
            if (c.phase == "idle" && (pos - Dock(c.idx)).magnitude > 60000f)   // left behind by a warp: it reappears at its dock
            {
                pos = Dock(c.idx);
                c.pos = pos;
                c.vel = carrier.vel;
            }
            var target = c.target;
            if (target != null && (target == null || !target || target.claimed != c || target.units <= 0.05f))
            {
                target = null;
                c.target = null;
            }
            if (c.phase == "idle" || (c.phase == "out" && target == null))
            {
                // pick the nearest unclaimed lump in range; with a decent load and nothing close, head home instead.
                // nothing goes out while the storage is full: a full drone would only come back to wait
                bool room = State.StoreAnyRoom();
                Pickup best = null;
                float bd = float.PositiveInfinity;
                if (room)
                {
                    foreach (var p in pickups)
                    {
                        if (p == null) continue;
                        if (p.claimed != null && p.claimed != c) continue;
                        var pt = p.transform.localPosition + offset;
                        if ((pt - carrier.truePos).sqrMagnitude >= range * range) continue;
                        float q = (pt - pos).sqrMagnitude;
                        if (q < bd) { bd = q; best = p; }
                    }
                }
                if (best != null && c.load < cap - 1f)
                {
                    best.claimed = c;
                    c.target = best;
                    target = best;
                    c.phase = "out";
                }
                else if (c.load > 0.5f && room) c.phase = "return";
                else c.phase = "idle";
            }
            // the run home goes in through the nearest mouth, down the starboard lane to the drop-off pad, and out the far mouth
            if (c.phase == "return" && c.side == 0)
            {
                float lz = carrier.ToLocalTrue(pos).z;
                c.side = lz < 0f ? -1 : 1;
            }
            float side = c.side;
            Vector3 goal;
            float arrive = 40f, vcap = speed;
            var pad = DropPad();
            switch (c.phase)
            {
                case "out": goal = target.transform.localPosition + offset; break;
                case "return": goal = carrier.ToTrue(new Vector3(LANE_X, pad.y, side * MOUTH_Z)); arrive = 80f; break;
                case "enter": goal = carrier.ToTrue(pad); vcap = 140f; break;
                case "unload": goal = carrier.ToTrue(pad); vcap = 60f; break;
                case "exit": goal = carrier.ToTrue(new Vector3(LANE_X, pad.y, -side * MOUTH_Z)); arrive = 80f; vcap = 160f; break;
                default: goal = Dock(c.idx); break;
            }
            // steering: accelerate toward the goal, brake to arrive, cap at the drone's speed; ride the carrier's motion near home
            var to = goal - pos;
            float dist = to.magnitude;
            var baseV = c.phase == "out" ? Vector3.zero : carrier.vel;
            float wantSpeed = Mathf.Min(vcap, Mathf.Sqrt(2f * 260f * Mathf.Max(0f, dist - arrive * 0.5f)) + 8f);
            var desired = dist > 1e-3f ? to / dist * wantSpeed + baseV : baseV;
            var vel = c.vel;
            var dv = desired - vel;
            float m = dv.magnitude;
            if (m > 1e-3f) vel += dv / m * Mathf.Min(m, 320f * dt);
            pos += vel * dt;
            c.vel = vel;
            c.pos = pos;
            if (c.phase == "unload")
            {
                c.wait -= dt;
                if (c.wait <= 0f)
                {
                    Unload(c);
                    c.phase = "exit";
                }
            }
            else if (dist < arrive)
            {
                switch (c.phase)
                {
                    case "out":
                        {
                            float take = Mathf.Min(target.units, cap - c.load);
                            if (take > 0.01f)
                            {
                                float have;
                                c.cargo.TryGetValue(target.ore, out have);
                                c.cargo[target.ore] = have + take;
                                c.load += take;
                                target.units -= take;
                            }
                            if (target.units <= 0.05f) game.RemoveDrop(target);
                            else target.claimed = null;
                            c.target = null;
                            if (c.load >= cap - 1f) c.phase = "return";
                            break;
                        }
                    case "return": c.phase = "enter"; break;
                    case "enter": c.phase = "unload"; c.wait = 2.5f; break;
                    case "exit": c.phase = "idle"; c.side = 0; break;
                }
            }
            // face the way it is going (relative to the carrier when hovering home), engines bright under thrust
            c.node.position = pos - offset;
            var rel = vel - baseV;
            if (rel.magnitude > 5f)
            {
                var q = Quaternion.LookRotation(rel.normalized, Vector3.up);
                c.node.rotation = Quaternion.Slerp(c.node.rotation, q, Mathf.Min(1f, 4f * dt));
            }
            float thr = Mathf.Min(1f, rel.magnitude / speed);
            foreach (var e in c.eng) e.localScale = Vector3.one * 5f * (0.6f + 0.9f * thr);
            c.strobe.enabled = (c.t % 1f) < 0.15f;
        }
    }

    public string Stats()
    {
        var parts = new List<string>();
        foreach (var c in drones) parts.Add("drone" + c.idx + " " + c.phase + " load=" + Mathf.RoundToInt(c.load));
        return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
    }
}
