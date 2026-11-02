namespace FinalFrontier.Sim.Math;

/// <summary>
/// Deterministic 2D value noise in fixed point: random values on an integer lattice (from a hash, not an RNG),
/// smoothly interpolated, summed over octaves. Same seed and coordinates give the same value everywhere.
/// </summary>
public sealed class ValueNoise
{
    private readonly ulong _seed;
    private static readonly Fix64 Three = Fix64.FromInt(3);
    private static readonly Fix64 Two = Fix64.FromInt(2);

    public ValueNoise(ulong seed) => _seed = seed;

    /// <summary>Lattice value in [0, 1).</summary>
    private Fix64 Lattice(int x, int y)
    {
        var h = _seed ^ ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL);
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return Fix64.FromRaw((long)(h >> 32));
    }

    private static Fix64 Smooth(Fix64 t) => t * t * (Three - Two * t);

    /// <summary>Single-octave noise at (x, y), in [0, 1).</summary>
    public Fix64 Sample(Fix64 x, Fix64 y)
    {
        var ix = x.FloorToInt();
        var iy = y.FloorToInt();
        var fx = Smooth(x - Fix64.FromInt(ix));
        var fy = Smooth(y - Fix64.FromInt(iy));
        var a = Lattice(ix, iy);
        var b = Lattice(ix + 1, iy);
        var c = Lattice(ix, iy + 1);
        var d = Lattice(ix + 1, iy + 1);
        var top = a + (b - a) * fx;
        var bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>Octaves with halving amplitude and doubling frequency, normalised to [0, 1).</summary>
    public Fix64 Fractal(Fix64 x, Fix64 y, int octaves)
    {
        var sum = Fix64.Zero;
        var amp = Fix64.One;
        var total = Fix64.Zero;
        for (var o = 0; o < octaves; o++)
        {
            sum += Sample(x, y) * amp;
            total += amp;
            amp = amp * Fix64.Half;
            x = x * 2 + Fix64.FromInt(17);
            y = y * 2 + Fix64.FromInt(31);
        }
        return sum / total;
    }
}
