namespace FinalFrontier.Sim.Math;

/// <summary>
/// FNV-1a over 64-bit words. Peers compare state hashes every few ticks to catch desyncs early.
/// </summary>
public struct StateHasher
{
    private const ulong Prime = 1099511628211UL;
    private ulong _hash;

    public static StateHasher Create() => new() { _hash = 14695981039346656037UL };

    public void Add(ulong value)
    {
        for (var i = 0; i < 8; i++)
        {
            _hash ^= (value >> (i * 8)) & 0xFF;
            _hash *= Prime;
        }
    }

    public void Add(long value) => Add((ulong)value);
    public void Add(int value) => Add((ulong)(long)value);
    public void Add(bool value) => Add(value ? 1UL : 0UL);
    public void Add(Fix64 value) => Add(value.Raw);
    public void Add(FixVec2 value)
    {
        Add(value.X);
        Add(value.Y);
    }

    public void Add(string value)
    {
        Add(value.Length);
        foreach (var c in value) Add((ulong)c);
    }

    public readonly ulong Value => _hash;
}
