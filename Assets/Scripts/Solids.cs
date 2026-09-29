using System.Collections.Generic;
using UnityEngine;

/// What nothing flies or shoots through (2026-09-23, the user's: "AI should not be able to shoot or fly through
/// anything"): the rocks, the cargo ship's hull, the two battleships' hulls and the outpost's structure, asked three
/// ways. `Blocked` says whether a segment passes through any of them (a bolt's or a rocket's flight this frame, a
/// gunner's line to its mark); `Push` sets a body that has ended up inside one back on its surface, sliding; `Avoid`
/// is the steer off whatever lies ahead along a heading. True coordinates throughout.
public static class Solids
{
    public static Game game;
    public static int blocks, pushes;   // segments blocked and bodies pushed out, for the tests
    const float SEG_STEP = 120f;                                            // the sampling along a segment against the carrier's curved hull
    const float CARRIER_NEAR = 5200f;                                       // the carrier only matters this close to its centre
    static readonly Vector3 HULL_HALF = new Vector3(3700f, 540f, 900f);     // a battleship's box (Battlefield.HULL_HALF)
    const float HULL_SPHERE_R = 1000f;                                      // its three spheres, for steering and pushing (Battlefield.Collide)

    /// The first solid the segment a→b passes through, if any, and where it enters it.
    public static bool Blocked(Vector3 a, Vector3 b, out Vector3 hit)
    {
        hit = b;
        if (game == null) return false;
        var d = b - a;
        float len = d.magnitude;
        if (len < 1e-3f) return false;
        var dir = d / len;
        float best = float.PositiveInfinity;
        // the rocks: the belt's own ray test, out to the segment's end, then the entry point on the rock it names
        var belt = game.belt;
        if (belt != null)
        {
            int rock = belt.RayHit(a, dir, len);
            if (rock >= 0)
            {
                float t;
                best = RaySphere(a, dir, belt.RockPos(rock), belt.radius[rock] + 4f, out t) && t <= len ? t : 0f;
            }
        }
        // the battleships' boxes
        var bf = game.battle;
        if (bf != null && bf.active)
        {
            if (bf.ally != null && !bf.ally.destroyed) { float t; if (SegBox(bf.ally, a, b, out t) && t * len < best) best = t * len; }
            if (bf.enemy != null && !bf.enemy.destroyed) { float t; if (SegBox(bf.enemy, a, b, out t) && t * len < best) best = t * len; }
        }
        // the cargo ship: its curved hull, sampled along the segment while either end is near it
        var carrier = game.carrier;
        if (carrier != null && ((a - carrier.truePos).sqrMagnitude < CARRIER_NEAR * CARRIER_NEAR || (b - carrier.truePos).sqrMagnitude < CARRIER_NEAR * CARRIER_NEAR))
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(len / SEG_STEP));
            for (int i = 1; i <= n; i++)
            {
                float t = len * i / n;
                if (t >= best) break;
                Vector3 cp, cn;
                if (carrier.Collide(carrier.ToLocalTrue(a + dir * t), 0f, out cp, out cn)) { best = t; break; }
            }
        }
        if (float.IsPositiveInfinity(best)) return false;
        blocks++;
        hit = a + dir * best;
        return true;
    }

    /// A body of radius `m` at `p` moving at `vel`, set back on the surface of any solid it is in, the way in taken out
    /// of its velocity so it slides along; `rocks` the belt ids about it (the caller keeps them), `n` the last surface.
    public static bool Push(ref Vector3 p, float m, ref Vector3 vel, List<int> rocks, out Vector3 n)
    {
        n = Vector3.up;
        bool any = false;
        if (game == null) return false;
        var belt = game.belt;
        if (belt != null && rocks != null)
        {
            foreach (var i in rocks)
            {
                if (i >= belt.count || !belt.alive[i]) continue;
                if (Sphere(ref p, ref vel, belt.RockPos(i), belt.radius[i] + m, ref n)) any = true;
            }
        }
        var bf = game.battle;
        if (bf != null && bf.active)
        {
            if (bf.ally != null) for (int i = -1; i <= 1; i++) if (Sphere(ref p, ref vel, bf.ally.ToTrue(new Vector3(i * 2400f * bf.ally.scale, 0f, 0f)), HULL_SPHERE_R * bf.ally.scale + m, ref n)) any = true;
            if (bf.enemy != null) for (int i = -1; i <= 1; i++) if (Sphere(ref p, ref vel, bf.enemy.ToTrue(new Vector3(i * 2400f * bf.enemy.scale, 0f, 0f)), HULL_SPHERE_R * bf.enemy.scale + m, ref n)) any = true;
        }
        var carrier = game.carrier;
        if (carrier != null && (p - carrier.truePos).sqrMagnitude < CARRIER_NEAR * CARRIER_NEAR)
        {
            Vector3 cp, cn;
            if (carrier.Collide(carrier.ToLocalTrue(p), m, out cp, out cn))
            {
                p = carrier.ToTrue(cp);
                n = carrier.Dir(cn);
                Slide(ref vel, n);
                any = true;
            }
        }
        var op = game.outpost;
        if (op != null && op.built)
        {
            Vector3 on; float push;
            if (op.Collide(p, m, out on, out push)) { p += on * push; n = on; Slide(ref vel, n); any = true; }
        }
        if (any) pushes++;
        return any;
    }

    /// The steer off whatever lies within `ahead` along `h` for a body of radius `m`: a lateral push, zero when the way
    /// is clear, up to 1.5 with something right under the nose.
    public static Vector3 Avoid(Vector3 p, Vector3 h, float m, float ahead, List<int> rocks)
    {
        var sum = Vector3.zero;
        if (game == null) return sum;
        var belt = game.belt;
        if (belt != null && rocks != null)
            foreach (var i in rocks) { if (i >= belt.count || !belt.alive[i]) continue; Steer(p, h, ahead, belt.RockPos(i), belt.radius[i] + m + 60f, ref sum); }
        var bf = game.battle;
        if (bf != null && bf.active)
        {
            if (bf.ally != null) for (int i = -1; i <= 1; i++) Steer(p, h, ahead, bf.ally.ToTrue(new Vector3(i * 2400f * bf.ally.scale, 0f, 0f)), HULL_SPHERE_R * bf.ally.scale + m + 120f, ref sum);
            if (bf.enemy != null) for (int i = -1; i <= 1; i++) Steer(p, h, ahead, bf.enemy.ToTrue(new Vector3(i * 2400f * bf.enemy.scale, 0f, 0f)), HULL_SPHERE_R * bf.enemy.scale + m + 120f, ref sum);
        }
        else if (game.carrier != null)
        {
            // off the battlefield the cargo ship is the same three spheres
            for (int i = -1; i <= 1; i++) Steer(p, h, ahead, game.carrier.ToTrue(new Vector3(i * 2400f, 0f, 0f)), HULL_SPHERE_R + m + 120f, ref sum);
        }
        float k = sum.magnitude;
        return k > 1.5f ? sum * (1.5f / k) : sum;
    }

    // ---- the pieces

    static bool Sphere(ref Vector3 p, ref Vector3 vel, Vector3 c, float r, ref Vector3 n)
    {
        var to = p - c;
        float d = to.magnitude;
        if (d >= r || d < 0.01f) return false;
        n = to / d;
        p = c + n * r;
        Slide(ref vel, n);
        return true;
    }

    static void Slide(ref Vector3 vel, Vector3 n)
    {
        float into = Vector3.Dot(vel, n);
        if (into < 0f) vel -= n * into;
    }

    /// The push off one obstacle sphere: sideways off it, harder the nearer along the path and the deeper the overlap.
    static void Steer(Vector3 p, Vector3 h, float ahead, Vector3 c, float r, ref Vector3 sum)
    {
        var to = c - p;
        float t = Vector3.Dot(to, h);
        if (t < -r || t > ahead + r) return;
        float tc = Mathf.Clamp(t, 0f, ahead);
        var off = p + h * tc - c;
        float d = off.magnitude;
        if (d >= r) return;
        var away = d > 1f ? off / d : Vector3.Cross(h, Vector3.up);
        away -= h * Vector3.Dot(away, h);   // lateral: off the path, not back along it
        if (away.sqrMagnitude < 1e-4f) away = Vector3.Cross(h, Vector3.right);
        if (away.sqrMagnitude < 1e-4f) away = Vector3.up;
        away.Normalize();
        float urgency = (1f - Mathf.Clamp01(tc / ahead)) * (1f - d / r) + 0.2f;
        sum += away * urgency;
    }

    static bool RaySphere(Vector3 o, Vector3 dir, Vector3 c, float r, out float t)
    {
        var to = c - o;
        float tc = Vector3.Dot(to, dir);
        float d2 = to.sqrMagnitude - tc * tc;
        t = 0f;
        if (d2 > r * r) return false;
        float h = Mathf.Sqrt(r * r - d2);
        if (tc + h < 0f) return false;
        t = Mathf.Max(0f, tc - h);
        return true;
    }

    static float Comp(Vector3 v, int k) { return k == 0 ? v.x : k == 1 ? v.y : v.z; }

    /// The segment a→b against a battleship's box, in its frame: `t` (0..1 along the segment) where it enters.
    static bool SegBox(Battlefield.Base bs, Vector3 a, Vector3 b, out float t)
    {
        var inv = Quaternion.Inverse(bs.q);
        var la = inv * (a - bs.pos);
        var d = inv * (b - bs.pos) - la;
        float t0 = 0f, t1 = 1f;
        t = 0f;
        for (int k = 0; k < 3; k++)
        {
            float o = Comp(la, k), dd = Comp(d, k), hh = Comp(HULL_HALF, k) * bs.scale;
            if (Mathf.Abs(dd) < 1e-6f) { if (Mathf.Abs(o) > hh) return false; continue; }
            float ta = (-hh - o) / dd, tb = (hh - o) / dd;
            if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            if (t0 > t1) return false;
        }
        t = t0;
        return true;
    }
}
