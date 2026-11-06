using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Vehicles drawn where the sim's lanes put them (sim/Surface/Lanes.cs): a position inside the hex in world units, so they
// drive round buildings through the free ring and park at access points instead of driving centre to centre.
public partial class PlanetView
{
    /// <summary>A sim point (world units on the map plane) on the ground.</summary>
    public Vector3 SimPoint(FixVec2 p)
    {
        var v = new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());
        v.Y = GroundAt(v);
        return v;
    }

    /// <summary>Where a unit is drawn this frame: between its position at the start of the last tick and now (alpha = how
    /// far into the next tick we are), on the ground.</summary>
    public Vector3 UnitPos(SurfaceUnit u, float alpha = 1f)
    {
        var a = new Vector2(u.PrevPos.X.ToFloat(), u.PrevPos.Y.ToFloat());
        var b = new Vector2(u.Pos.X.ToFloat(), u.Pos.Y.ToFloat());
        var p = a.Lerp(b, Mathf.Clamp(alpha, 0f, 1f));
        var v = new Vector3(p.X, 0f, p.Y);
        v.Y = GroundAt(v);
        return v;
    }

    /// <summary>It moved in the last tick (not parked, not waiting for the vehicle in front).</summary>
    private static bool Rolling(SurfaceUnit u) => u.Mode == UnitMode.Mobile && u.Path.Count > 0 && u.Pos != u.PrevPos;

    /// <summary>The way it is heading on the ground plane: its last tick's motion, else toward its next route point
    /// (zero when it has nowhere to go).</summary>
    private static Vector3 Heading(SurfaceUnit u)
    {
        var d = new Vector3((u.Pos.X - u.PrevPos.X).ToFloat(), 0f, (u.Pos.Y - u.PrevPos.Y).ToFloat());
        if (d.LengthSquared() > 0.0004f) return d.Normalized();
        if (u.Route.Count == 0) return Vector3.Zero;
        var n = new Vector3((u.Route[0].X - u.Pos.X).ToFloat(), 0f, (u.Route[0].Y - u.Pos.Y).ToFloat());
        return n.LengthSquared() > 0.0004f ? n.Normalized() : Vector3.Zero;
    }

    /// <summary>Parked on its own colony core's repair pad (the core's access point), not elsewhere on the tile.</summary>
    private bool OnPad(SurfaceUnit u, SurfaceStructure core) =>
        _surface.AccessPoint(core.Tile) is { } pad && (u.Pos - pad).Length() < Fix64.FromInt(2);

    private Vector3 _lastPickPoint;

    /// <summary>The player's vehicle on this tile nearest the point the mouse last picked (several can park on one tile),
    /// or null.</summary>
    public SurfaceUnit? UnitOnTile(int tile, int owner)
    {
        SurfaceUnit? best = null;
        var bestDist = float.MaxValue;
        foreach (var u in _surface.Units)
        {
            if (u.Owner != owner || u.Tile != tile) continue;
            var p = _rovers.TryGetValue(u.Id, out var model) ? model.Position : UnitPos(u);
            var d = new Vector2(p.X - _lastPickPoint.X, p.Z - _lastPickPoint.Z).LengthSquared();
            if (d >= bestDist) continue;
            best = u;
            bestDist = d;
        }
        return best;
    }

    /// <summary>The sim and the view must agree on the size of a hex (the sim's lanes are in world units).</summary>
    private static void CheckTileSize()
    {
        if (Mathf.Abs(PlanetSurface.TileSize.ToFloat() - HexWidth) > 0.001f)
            GD.PushError($"PlanetView.HexWidth {HexWidth} differs from the sim's PlanetSurface.TileSize {PlanetSurface.TileSize}: vehicles will drive off their lanes.");
    }
}
