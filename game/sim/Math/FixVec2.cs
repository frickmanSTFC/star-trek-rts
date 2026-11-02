using System;

namespace FinalFrontier.Sim.Math;

/// <summary>
/// 2D fixed-point vector. Combat inside a star system plays on a plane (rendered in 3D),
/// so the simulation only needs X/Y.
/// </summary>
public readonly struct FixVec2 : IEquatable<FixVec2>
{
    public readonly Fix64 X;
    public readonly Fix64 Y;

    public FixVec2(Fix64 x, Fix64 y)
    {
        X = x;
        Y = y;
    }

    public static readonly FixVec2 Zero = new(Fix64.Zero, Fix64.Zero);

    public static FixVec2 FromInts(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));

    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec2 operator -(FixVec2 a) => new(-a.X, -a.Y);
    public static FixVec2 operator *(FixVec2 a, Fix64 s) => new(a.X * s, a.Y * s);
    public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

    public Fix64 LengthSquared() => X * X + Y * Y;

    /// <summary>
    /// Exact-as-possible length without overflow: the squares are summed in 128 bits
    /// (a Q64.64 value), whose integer square root is already the Q32.32 result.
    /// </summary>
    public Fix64 Length()
    {
        Int128 sum = (Int128)X.Raw * X.Raw + (Int128)Y.Raw * Y.Raw;
        return Fix64.FromRaw((long)Fix64.ISqrt((UInt128)sum));
    }

    /// <summary>Unit vector in the same direction, or zero for the zero vector.</summary>
    public FixVec2 Normalized()
    {
        var len = Length();
        return len == Fix64.Zero ? Zero : new FixVec2(X / len, Y / len);
    }

    public static Fix64 Distance(FixVec2 a, FixVec2 b) => (a - b).Length();

    public bool Equals(FixVec2 other) => this == other;
    public override bool Equals(object? obj) => obj is FixVec2 v && this == v;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
