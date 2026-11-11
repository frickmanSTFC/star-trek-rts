using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>A ground vehicle cycling through the edge of a dome / containment field (docs/missions-contract.md A):
/// it stops at the hex edge where the field's shell stands for domes.transit_seconds while its own field forms and
/// meshes with the big one. StructureId = the dome or core whose field it is crossing; Entering = going under cover;
/// Fraction 0 → 1 over the pause; Contact = where it stands (the edge crossing point, world units like
/// <see cref="SurfaceUnit.Pos"/>); FromTile → ToTile is the edge being crossed; ticks from the sim clock.</summary>
public readonly record struct DomeTransit(int StructureId, bool Entering, Fix64 Fraction, FixVec2 Contact,
    int FromTile, int ToTile, long StartTick, long EndTick);

// Dome transits (worlds.json habitability.domes transit_seconds). Only where the domes kit applies (Domes / Tech worlds):
// a ground vehicle about to cross a hex edge between a tile its owner has Covered and one they haven't (either way) stops
// at the edge's crossing point (where the field's shell stands: shells reach r + ½ hexes) for transit_seconds, then drives
// over. The hook is in Lanes.cs MoveUnits (BeginTransit / TransitTick). A new order whose path still starts over the same
// edge keeps the transit; any other order, a halt (disabled, tyre change, teleport, mine order) or losing its path cancels
// it. At the end it drives over whatever the cover is by then. Aerial units never transit. On other worlds nothing here
// runs and nothing is hashed (SurfaceUnit.AddToHash adds the transit only once a unit has had one).
public sealed partial class PlanetSurface
{
    /// <summary>The vehicle's dome transit under way, or null (always null on worlds without the domes kit, for aerial
    /// units and for vehicles not at a field edge).</summary>
    public DomeTransit? DomeTransitOf(SurfaceUnit unit) => unit.Transit;

    /// <summary>The most recent finished transit of this vehicle (for the view's after-glow: the field lingers ~1.2 s
    /// after it drives through), or null.</summary>
    public DomeTransit? LastDomeTransitOf(SurfaceUnit unit) => unit.LastTransit;

    /// <summary>Ticks a transit lasts here (0 = no transits: no domes kit, or transit_seconds 0).</summary>
    private int TransitTicks => Domes is { } d ? System.Math.Max(0, (d.TransitSeconds * Simulation.TickRate).FloorToInt()) : 0;

    /// <summary>The unit stands at the crossing into its next tile: if that edge is a field boundary for its owner, start
    /// the transit there (true = it waits).</summary>
    private bool BeginTransit(Simulation sim, SurfaceUnit u)
    {
        if (u.Drone != null || u._path.Count == 0) return false;
        var total = TransitTicks;
        if (total == 0) return false;
        var from = u.Tile;
        var to = u._path[0];
        var coveredFrom = Covered(u.Owner, from);
        if (coveredFrom == Covered(u.Owner, to)) return false;
        var inside = coveredFrom ? from : to;
        u.Transit = new DomeTransit(FieldAt(u.Owner, inside, u.Pos), !coveredFrom, Fix64.Zero, u.Pos, from, to, sim.Tick, sim.Tick + total);
        u.TransitLeft = total;
        u.WaitTicks = 0;
        u.BlockedBy = 0;
        return true;
    }

    /// <summary>The covering dome or core over this tile whose centre is nearest the point (lowest id on a tie), 0 if none.</summary>
    private int FieldAt(int owner, int tile, FixVec2 at)
    {
        var best = 0;
        var bestDist = Fix64.MaxValue;
        foreach (var s in _structures)
        {
            if (s.Owner != owner) continue;
            var r = DomeRadiusOf(s);
            if (r <= 0 || Map.Grid.Distance(s.Tile, tile) > r) continue;
            var dist = (CentreOf(s.Tile) - at).Length();
            if (dist >= bestDist) continue;
            best = s.Id;
            bestDist = dist;
        }
        return best;
    }

    /// <summary>A unit with a transit under way, at the top of its move: false = still waiting (or the transit was just
    /// cancelled and it moves normally: then <paramref name="cross"/> is false); when it ends, cross = true and the
    /// caller drives it over the edge.</summary>
    private bool TransitTick(Simulation sim, SurfaceUnit u, out bool cross)
    {
        cross = false;
        if (u.Transit is not { } t) return true;
        if (u._path.Count == 0 || u.Tile != t.FromTile || u._path[0] != t.ToTile || u.Mode != UnitMode.Mobile)
        {
            CancelTransit(u); // a new order that doesn't cross this edge, or it stopped
            return true;
        }
        u.WaitTicks = 0;
        u.BlockedBy = 0;
        var total = (int)(t.EndTick - t.StartTick);
        u.TransitLeft--;
        if (u.TransitLeft > 0)
        {
            u.Transit = t with { Fraction = Fix64.FromInt(total - u.TransitLeft) / Fix64.FromInt(System.Math.Max(1, total)) };
            return false;
        }
        u.LastTransit = t with { Fraction = Fix64.One, EndTick = sim.Tick };
        u.Transit = null;
        u.TransitLeft = 0;
        u.Pos = t.Contact;
        u._route.Clear();
        cross = true;
        return true;
    }

    private static void CancelTransit(SurfaceUnit u)
    {
        u.Transit = null;
        u.TransitLeft = 0;
    }
}
