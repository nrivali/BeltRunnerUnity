using UnityEngine;

/// A small deterministic generator (xorshift128+), so a belt built from a seed is the same on every machine.
public class Rng
{
    ulong s0, s1;

    public Rng(ulong seed)
    {
        Seed(seed);
    }

    public void Seed(ulong seed)
    {
        // splitmix64 spreads the seed over both words
        ulong z = seed;
        s0 = Mix(ref z);
        s1 = Mix(ref z);
        if (s0 == 0 && s1 == 0) s1 = 1;
    }

    static ulong Mix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong x = z;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public ulong NextU64()
    {
        ulong x = s0, y = s1;
        s0 = y;
        x ^= x << 23;
        s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
        return s1 + y;
    }

    /// [0, 1)
    public float Value()
    {
        return (float)((NextU64() >> 11) * (1.0 / 9007199254740992.0));
    }

    public float Range(float a, float b)
    {
        return a + (b - a) * Value();
    }

    public int Int(int n)
    {
        return n <= 0 ? 0 : (int)(NextU64() % (ulong)n);
    }

    public Vector3 Dir()
    {
        var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
        return v.sqrMagnitude < 1e-8f ? Vector3.up : v.normalized;
    }
}
