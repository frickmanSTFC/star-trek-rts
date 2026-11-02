using System;
using System.Globalization;

namespace FinalFrontier.Sim.Math;

/// <summary>
/// Q32.32 fixed-point number. All simulation math uses this instead of float/double so that
/// every machine computes bit-identical results (required for lockstep multiplayer and replays).
/// Range is about ±2.1 billion with a resolution of about 2.3e-10.
/// </summary>
public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    public const int FractionalBits = 32;
    private const long OneRaw = 1L << FractionalBits;

    /// <summary>The underlying integer; value = Raw / 2^32.</summary>
    public readonly long Raw;

    private Fix64(long raw) => Raw = raw;

    public static readonly Fix64 Zero = new(0);
    public static readonly Fix64 One = new(OneRaw);
    public static readonly Fix64 Half = new(OneRaw >> 1);
    public static readonly Fix64 MaxValue = new(long.MaxValue);
    public static readonly Fix64 MinValue = new(long.MinValue);

    public static Fix64 FromRaw(long raw) => new(raw);
    public static Fix64 FromInt(int value) => new((long)value << FractionalBits);

    /// <summary>numerator / denominator, e.g. Ratio(1, 15) for one tick at 15 Hz.</summary>
    public static Fix64 Ratio(int numerator, int denominator) => FromInt(numerator) / FromInt(denominator);

    /// <summary>
    /// Converts a double. Use ONLY at the input boundary (e.g. turning a mouse click into a command):
    /// the resulting fixed value is what gets sent to every machine, so the sim stays deterministic.
    /// Never call this inside simulation code.
    /// </summary>
    public static Fix64 FromDouble(double value) => new((long)System.Math.Round(value * OneRaw));

    /// <summary>For rendering and UI only. Never feed the result back into the simulation.</summary>
    public double ToDouble() => (double)Raw / OneRaw;

    /// <summary>For rendering and UI only.</summary>
    public float ToFloat() => (float)ToDouble();

    /// <summary>Rounds toward negative infinity.</summary>
    public int FloorToInt() => (int)(Raw >> FractionalBits);

    /// <summary>
    /// Parses a decimal string ("12", "-0.25", "3.125") exactly, without going through double,
    /// so data files load identically everywhere.
    /// </summary>
    public static Fix64 Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Empty number.");
        var s = text.Trim();
        var negative = s.StartsWith('-');
        if (negative || s.StartsWith('+')) s = s[1..];

        var dot = s.IndexOf('.');
        var intPart = dot < 0 ? s : s[..dot];
        var fracPart = dot < 0 ? "" : s[(dot + 1)..];
        if (intPart.Length == 0 && fracPart.Length == 0) throw new FormatException($"Not a number: '{text}'.");
        if (fracPart.Length > 18) fracPart = fracPart[..18];

        long whole = intPart.Length == 0 ? 0 : long.Parse(intPart, NumberStyles.None, CultureInfo.InvariantCulture);
        if (whole > int.MaxValue) throw new OverflowException($"Out of Fix64 range: '{text}'.");

        long fracRaw = 0;
        if (fracPart.Length > 0)
        {
            Int128 digits = long.Parse(fracPart, NumberStyles.None, CultureInfo.InvariantCulture);
            Int128 scale = 1;
            for (var i = 0; i < fracPart.Length; i++) scale *= 10;
            fracRaw = (long)(((digits << FractionalBits) + scale / 2) / scale); // round half up
        }

        var raw = (whole << FractionalBits) + fracRaw;
        return new Fix64(negative ? -raw : raw);
    }

    public static Fix64 operator +(Fix64 a, Fix64 b) => new(a.Raw + b.Raw);
    public static Fix64 operator -(Fix64 a, Fix64 b) => new(a.Raw - b.Raw);
    public static Fix64 operator -(Fix64 a) => new(-a.Raw);
    public static Fix64 operator *(Fix64 a, Fix64 b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionalBits));
    public static Fix64 operator *(Fix64 a, int b) => new(a.Raw * b);

    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        return new((long)(((Int128)a.Raw << FractionalBits) / b.Raw));
    }

    public static Fix64 operator /(Fix64 a, int b)
    {
        if (b == 0) throw new DivideByZeroException();
        return new(a.Raw / b);
    }

    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

    public static Fix64 Abs(Fix64 a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw <= b.Raw ? a : b;
    public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw >= b.Raw ? a : b;
    public static Fix64 Clamp(Fix64 v, Fix64 lo, Fix64 hi) => Max(lo, Min(hi, v));

    public static Fix64 Sqrt(Fix64 a)
    {
        if (a.Raw < 0) throw new ArgumentOutOfRangeException(nameof(a), "Square root of a negative number.");
        // sqrt(raw / 2^32) * 2^32 = sqrt(raw * 2^32)
        return new((long)ISqrt((UInt128)(ulong)a.Raw << FractionalBits));
    }

    /// <summary>Floor of the integer square root (Newton's method, starting above the root).</summary>
    internal static ulong ISqrt(UInt128 n)
    {
        if (n == 0) return 0;
        var bits = 128 - (int)UInt128.LeadingZeroCount(n);
        UInt128 x = (UInt128)1 << ((bits + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return (ulong)x;
            x = y;
        }
    }

    public bool Equals(Fix64 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix64 f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToDouble().ToString("0.######", CultureInfo.InvariantCulture);
}
