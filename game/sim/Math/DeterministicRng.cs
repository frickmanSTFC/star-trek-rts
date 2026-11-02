using System;

namespace FinalFrontier.Sim.Math;

/// <summary>
/// xoshiro256** seeded through SplitMix64. Same seed gives the same sequence on every machine.
/// The simulation owns one instance; never use System.Random in sim code.
/// </summary>
public sealed class DeterministicRng
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRng(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    private static ulong SplitMix64(ref ulong x)
    {
        var z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        var result = ulong.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = ulong.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [0, maxExclusive), without modulo bias.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        var bound = (ulong)maxExclusive;
        var limit = ulong.MaxValue - ulong.MaxValue % bound;
        ulong r;
        do r = NextULong(); while (r >= limit);
        return (int)(r % bound);
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

    /// <summary>Uniform value in [0, 1).</summary>
    public Fix64 NextFix() => Fix64.FromRaw((long)(NextULong() >> 32));

    /// <summary>Folds the generator state into a state hash (for desync detection).</summary>
    internal void AddToHash(ref StateHasher h)
    {
        h.Add(_s0);
        h.Add(_s1);
        h.Add(_s2);
        h.Add(_s3);
    }
}
