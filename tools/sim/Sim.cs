using System;
using System.Diagnostics;
using UnityEngine;

/// The engine-free checks: does the belt build, do the meshes come out sane, does the nose ray find a rock, does a
/// scan count ore, do rails drift, do kills respawn.
public static class Sim
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    public static int Main()
    {
        State.Init();
        var sw = Stopwatch.StartNew();
        var belt = new Belt();
        Console.WriteLine("meshes built in " + sw.ElapsedMilliseconds + " ms");
        for (int s = 0; s < RockMeshes.SHAPES.Length; s++)
        {
            var S = RockMeshes.SHAPES[s];
            var near = RockMeshes.Build(S, 1, 1);
            var far = RockMeshes.Build(S, 0, 1);
            bool sane = true;
            float maxR = 0f;
            foreach (var vert in near.vertices)
            {
                if (float.IsNaN(vert.x) || float.IsNaN(vert.y) || float.IsNaN(vert.z)) sane = false;
                maxR = Math.Max(maxR, vert.magnitude);
            }
            Check(sane && maxR > 0.3f && maxR < 4f && near.vertices.Length >= far.vertices.Length,   // hollow is capped at detail 3 on both
                S.key.PadRight(9) + " near " + near.vertices.Length.ToString().PadLeft(5) + " verts / " + (near.triangles.Length / 3).ToString().PadLeft(5) + " tris · far " + far.vertices.Length.ToString().PadLeft(5) + " verts · reach " + maxR.ToString("0.00"));
        }
        sw.Restart();
        belt.Build(Data.ZONE_KESSLER, Game.SEED);
        Console.WriteLine("belt built in " + sw.ElapsedMilliseconds + " ms: " + belt.count + " rocks, " + belt.ChunkCount + " chunks, " + belt.fields.Count + " fields, planet r " + belt.planetR + ", edge " + belt.worldR);
        Check(belt.count > 40000 && belt.count < 70000, "rock count in the browser's range");
        int ore = 0, barren = 0;
        var cls = new int[4];
        for (int i = 0; i < belt.count; i++) { if (belt.ore[i] >= 0) ore++; else barren++; cls[belt.cls[i]]++; }
        Console.WriteLine("  ore " + ore + " · barren " + barren + " · small " + cls[0] + " large " + cls[1] + " giant " + cls[2] + " colossal " + cls[3]);
        Check(ore > belt.count * 0.25f && ore < belt.count * 0.45f, "about a third of the rocks carry ore");
        // the ring belt start: copper within scanner range
        var start = new Vector3(0f, 0f, 925000f);
        int nearest; float dist;
        int n = belt.Scan(start, 200000f, Data.OreIndex("copper"), 0f, out nearest, out dist);
        Check(n > 0 && nearest >= 0, "copper within 200,000 u of the ring-belt start: " + n + " · nearest at " + dist.ToString("0"));
        // the nose ray: park 560 u off the rock, aim at it
        var rp = belt.RockPos(nearest);
        var dir = (rp - start).normalized;
        var origin = rp - dir * (belt.radius[nearest] + 560f);
        int hit = belt.RayHit(origin, dir, 2500f);
        Check(hit == nearest, "nose ray from 560 u finds that rock (hit " + hit + ", wanted " + nearest + ", r " + belt.radius[nearest].ToString("0") + ")");
        Check(belt.RayHit(origin, -dir, 2500f) != nearest, "the ray behind does not");
        var within = belt.RocksWithin(origin, 1000f);
        Check(within.Contains(nearest), "RocksWithin 1,000 u lists it (" + within.Count + " rocks)");
        // rails: a rail rock moves 28 u/s
        var p0 = belt.RockPos(nearest);
        belt.Tick(10f, new Vector3(1e7f, 0, 0));
        var moved = (belt.RockPos(nearest) - p0).magnitude;
        Check(Math.Abs(moved - 280f) < 2f, "rail drift over 10 s: " + moved.ToString("0.0") + " u (want 280)");
        var v = belt.RockVel(nearest);
        Check(Math.Abs(v.magnitude - 28f) < 0.01f && Math.Abs(Vector3.Dot(v.normalized, (belt.RockPos(nearest) - p0).normalized) - 1f) < 0.01f, "rail velocity 28 u/s along the drift");
        // damage, kill, respawn
        belt.Damage(nearest, belt.hp[nearest] * 0.5f);
        Check(belt.hp[nearest] > 0f && belt.hp[nearest] < belt.hpMax[nearest], "half damage leaves half the hp");
        float loose = belt.Kill(nearest);
        Check(!belt.alive[nearest] && loose == belt.amountMax[nearest], "kill frees its ore: " + loose);
        State.time += Belt.RESPAWN_AFTER + 1f;
        belt.Tick(1.5f, new Vector3(1e7f, 0, 0));
        Check(belt.alive[nearest] && belt.hp[nearest] == belt.hpMax[nearest], "grows back after " + Belt.RESPAWN_AFTER + " s with full hp");
        // a fragment and a free rock coasting
        int frag = belt.AddFragment(0, 20f, Data.OreIndex("copper"), false, start + new Vector3(500f, 0f, 0f), 30f, new Vector3(0f, 0f, 10f), 0);
        var fp = belt.RockPos(frag);
        belt.Tick(2f, new Vector3(1e7f, 0, 0));
        Check((belt.RockPos(frag) - fp).magnitude > 19f, "a fragment coasts on its own velocity");
        belt.SetFree(hit >= 0 ? hit : 0, new Vector3(5f, 0f, 0f));
        // the floating origin and the draw path
        belt.ApplyOffset(start);
        belt.Cull(start);
        Graphics.calls = 0;
        belt.Draw();
        Check(belt.VisibleChunks() > 0 && Graphics.calls > 0, belt.VisibleChunks() + " chunks visible from the start, " + Graphics.calls + " instanced draws");
        // names and readouts
        Console.WriteLine("  e.g. " + belt.RockName(0) + ", " + belt.RockName(1) + ", " + belt.RockName(2) + " · " + Data.Fm(12345f) + " m · " + Data.Describe("engine", 0));
        Console.WriteLine(_fails == 0 ? "all checks passed" : _fails + " check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
