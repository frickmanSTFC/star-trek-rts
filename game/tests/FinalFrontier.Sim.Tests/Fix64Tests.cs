using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

public class Fix64Tests
{
    [Fact]
    public void IntegerRoundTrip()
    {
        Assert.Equal(42, Fix64.FromInt(42).FloorToInt());
        Assert.Equal(-7, Fix64.FromInt(-7).FloorToInt());
    }

    [Fact]
    public void Arithmetic()
    {
        var a = Fix64.Parse("2.5");
        var b = Fix64.Parse("-4");
        Assert.Equal(Fix64.Parse("-1.5"), a + b);
        Assert.Equal(Fix64.Parse("-10"), a * b);
        Assert.Equal(Fix64.Parse("-0.625"), a / b);
        Assert.Equal(Fix64.Parse("0.25"), Fix64.Half * Fix64.Half);
    }

    [Theory]
    [InlineData("0.5", 1L << 31)]
    [InlineData("-0.25", -(1L << 30))]
    [InlineData("3", 3L << 32)]
    [InlineData(".75", 3L << 30)]
    public void ParseIsExact(string text, long raw) => Assert.Equal(raw, Fix64.Parse(text).Raw);

    [Fact]
    public void SqrtIsAccurate()
    {
        Assert.Equal(Fix64.FromInt(3), Fix64.Sqrt(Fix64.FromInt(9)));
        Assert.Equal(Fix64.Zero, Fix64.Sqrt(Fix64.Zero));
        Assert.InRange(Fix64.Sqrt(Fix64.FromInt(2)).ToDouble(), 1.4142135619, 1.4142135628);
        Assert.InRange(Fix64.Sqrt(Fix64.FromInt(1_000_000_000)).ToDouble(), 31622.7766, 31622.7767);
    }

    [Fact]
    public void VectorLengthDoesNotOverflow()
    {
        // 100,000² would overflow Q32.32; Length() sums squares in 128 bits.
        var v = FixVec2.FromInts(60_000, 80_000);
        Assert.Equal(Fix64.FromInt(100_000), v.Length());
        Assert.Equal(FixVec2.Zero, FixVec2.Zero.Normalized());
    }

    [Fact]
    public void DivideByZeroThrows() =>
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
}
