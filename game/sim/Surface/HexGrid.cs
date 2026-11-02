using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// Rectangular hex map, pointy-top hexes, odd rows shifted right by half a hex ("odd-r" layout).
/// Tiles are addressed by index = row * Width + column. Neighbour order is fixed (E, NE, NW, W, SW, SE),
/// which keeps pathfinding deterministic.
/// </summary>
public sealed class HexGrid
{
    public int Width { get; }
    public int Height { get; }
    public int Count => Width * Height;

    /// <summary>sqrt(3)/2: vertical spacing of hex rows relative to horizontal spacing.</summary>
    public static readonly Fix64 RowSpacing = Fix64.Parse("0.8660254038");

    public HexGrid(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
    }

    public int Index(int column, int row) => row * Width + column;
    public int Column(int index) => index % Width;
    public int Row(int index) => index / Width;
    public bool InBounds(int column, int row) => column >= 0 && column < Width && row >= 0 && row < Height;

    // Offsets (dColumn, dRow) for even and odd rows, in the order E, NE, NW, W, SW, SE (north = row - 1).
    private static readonly (int dc, int dr)[] EvenRow = { (1, 0), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1) };
    private static readonly (int dc, int dr)[] OddRow = { (1, 0), (1, -1), (0, -1), (-1, 0), (0, 1), (1, 1) };

    /// <summary>Neighbouring tile indices (fewer than 6 at the map edge).</summary>
    public IEnumerable<int> Neighbours(int index)
    {
        var c = Column(index);
        var r = Row(index);
        var offsets = (r & 1) == 0 ? EvenRow : OddRow;
        foreach (var (dc, dr) in offsets)
        {
            var nc = c + dc;
            var nr = r + dr;
            if (InBounds(nc, nr)) yield return Index(nc, nr);
        }
    }

    public int NeighbourCount(int index)
    {
        var n = 0;
        foreach (var _ in Neighbours(index)) n++;
        return n;
    }

    /// <summary>Steps between two tiles.</summary>
    public int Distance(int a, int b)
    {
        var (aq, ar) = Axial(a);
        var (bq, br) = Axial(b);
        var dq = aq - bq;
        var dr = ar - br;
        return (System.Math.Abs(dq) + System.Math.Abs(dr) + System.Math.Abs(dq + dr)) / 2;
    }

    private (int q, int r) Axial(int index)
    {
        var c = Column(index);
        var r = Row(index);
        return (c - (r - (r & 1)) / 2, r);
    }

    /// <summary>All tiles within <paramref name="radius"/> steps, in index order.</summary>
    public List<int> Within(int centre, int radius)
    {
        var result = new List<int>();
        var r0 = Row(centre);
        for (var r = System.Math.Max(0, r0 - radius); r <= System.Math.Min(Height - 1, r0 + radius); r++)
            for (var c = 0; c < Width; c++)
            {
                var i = Index(c, r);
                if (Distance(centre, i) <= radius) result.Add(i);
            }
        return result;
    }

    /// <summary>Tile centre on the plane, in hex widths (x) and the same units down the rows (y grows southward).</summary>
    public FixVec2 Centre(int index)
    {
        var c = Column(index);
        var r = Row(index);
        var x = Fix64.FromInt(c) + ((r & 1) == 1 ? Fix64.Half : Fix64.Zero);
        return new FixVec2(x, RowSpacing * r);
    }
}
