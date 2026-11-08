using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Driving inside the hexes (surfaces.json "lanes", buildings.json footprint_radius / access).
//
// A unit keeps its tile path (Path = tiles still to enter, the last one its goal) and drives a polyline of points in world
// units (Pos). Inside a tile it heads for the edge it leaves by, crossing it lane_offset to the right of its direction of
// travel (so oncoming traffic passes), or, in its last tile (Path[0] == Tile), for its parking spot. If something stands in
// the middle of the tile (a building, a deployed crawler, an unfolding construction rover) the stretch bends round it on a
// ring footprint + clearance out; it also swerves round vehicles parked on the tile. Tile changes when it crosses the edge.
// Time per world unit on a tile = move_cost / TileSize effort, so a tile still costs about its move_cost.
// Spacing: it waits while another vehicle is close in front of it; after wait_seconds it drives on regardless for
// push_seconds, so nothing can deadlock.
public sealed partial class PlanetSurface
{
    /// <summary>World units across a hex (flat side to flat side). Must equal PlanetView.HexWidth.</summary>
    public static readonly Fix64 TileSize = Fix64.FromInt(25);

    private static readonly Fix64 ArcStep = Fix64.FromInt(30);   // degrees between ring points
    private static readonly Fix64 ArcGrow = Fix64.Parse("1.04"); // ring points this much out, so chords between them clear the disc
    private static readonly Fix64 InHex = Fix64.Parse("12.3");   // detours stay this close to the centre (the hex is 12.5 to an edge)
    private static readonly Fix64 CornerPark = Fix64.Parse("12.5"); // parking toward the corners of a tile with something in the middle
    private static readonly Fix64 Root3Half = Fix64.Parse("0.8660254038");
    private static readonly Fix64 Deg360 = Fix64.FromInt(360), Deg180 = Fix64.FromInt(180);

    // cos/sin of 60°·k (facings) and of 30° + 60°·k (the corner directions), in the view's frame.
    private static readonly Fix64[] Cos60 = { Fix64.One, Fix64.Half, -Fix64.Half, -Fix64.One, -Fix64.Half, Fix64.Half };
    private static readonly Fix64[] Sin60 = { Fix64.Zero, Root3Half, Root3Half, Fix64.Zero, -Root3Half, -Root3Half };

    private LaneRules Lanes => _data.Lanes;

    /// <summary>A tile's centre in world units (the view's TileCentre without the height).</summary>
    public FixVec2 CentreOf(int tile)
    {
        var c = Map.Grid.Centre(tile);
        return new FixVec2(c.X * TileSize, c.Y * TileSize);
    }

    /// <summary>What stands in the middle of a tile, as a radius round its centre (0 = nothing): a building's footprint,
    /// or a deployed / unfolding vehicle other than <paramref name="self"/>.</summary>
    public Fix64 BlockRadius(int tile, SurfaceUnit? self = null)
    {
        if (StructureAt(tile) is { } s) return _data.FootprintOf(s.DefId).Radius;
        foreach (var u in _units)
            if (u != self && u.Tile == tile && u.Mode != UnitMode.Mobile) return _data.DeployedFootprintOf(u.DefId);
        return Fix64.Zero;
    }

    /// <summary>Where vehicles park at the building on this tile (its access point, turned by its facing), or null.</summary>
    public FixVec2? AccessPoint(int tile)
    {
        if (StructureAt(tile) is not { } s || _data.FootprintOf(s.DefId).Access is not { } local) return null;
        return CentreOf(tile) + Turn(local, s.Facing);
    }

    /// <summary>A building-frame offset turned like the view's Basis(Up, −60°·facing).</summary>
    public static FixVec2 Turn(FixVec2 local, int facing)
    {
        var f = ((facing % 6) + 6) % 6;
        var c = Cos60[f];
        var s = Sin60[f];
        return new FixVec2(local.X * c - local.Y * s, local.X * s + local.Y * c);
    }

    /// <summary>Where a unit driving from one tile to its neighbour crosses their shared edge: the edge's middle,
    /// lane_offset to the right of the direction of travel.</summary>
    public FixVec2 Crossing(int from, int to)
    {
        var a = CentreOf(from);
        var b = CentreOf(to);
        var d = new FixVec2((b.X - a.X) / TileSize, (b.Y - a.Y) / TileSize);
        var mid = new FixVec2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        return mid + new FixVec2(-d.Y, d.X) * Lanes.LaneOffset;
    }

    /// <summary>Parking spots on a tile, best first: a building's access point (or the centre when nothing stands there),
    /// then six toward the corners.</summary>
    private List<FixVec2> Spots(int tile, SurfaceUnit? self)
    {
        var c = CentreOf(tile);
        var list = new List<FixVec2>(7);
        Fix64 ring;
        FixVec2? access = null;
        if (BlockRadius(tile, self) > Fix64.Zero)
        {
            access = AccessPoint(tile);
            if (access is { } a) list.Add(a);
            ring = CornerPark;
        }
        else
        {
            list.Add(c);
            ring = Lanes.ParkRadius;
        }
        for (var k = 0; k < 6; k++)
        {
            // 30° + 60°·k: between the edge middles, toward the corners.
            var spot = c + Turn(new FixVec2(Root3Half, Fix64.Half), k) * ring;
            if (access is { } a && (spot - a).Length() < Lanes.AvoidRadius + Fix64.Half) continue; // beside the access point
            list.Add(spot);
        }
        return list;
    }

    /// <summary>Where another unit means to stand on this tile: parked there, or driving its last stretch to a spot.</summary>
    private static FixVec2? Claim(SurfaceUnit w, int tile)
    {
        if (w.Tile != tile || w.Mode != UnitMode.Mobile) return null;
        if (w._path.Count == 0) return w.Pos;
        if (w._path[0] == tile && w._route.Count > 0) return w._route[^1];
        return null;
    }

    private bool Held(FixVec2 spot, int tile, SurfaceUnit? self)
    {
        foreach (var w in _units)
            if (w != self && Claim(w, tile) is { } p && (p - spot).Length() < Lanes.AvoidRadius) return true;
        return false;
    }

    /// <summary>The first free parking spot on a tile (for a unit that appears there), else the best one.</summary>
    private FixVec2 FreeSpot(int tile, SurfaceUnit? self)
    {
        var spots = Spots(tile, self);
        foreach (var s in spots)
            if (!Held(s, tile, self)) return s;
        return spots[0];
    }

    /// <summary>Where a unit stops in its last tile: the centre to unfurl or found, else the access point / centre when
    /// free, else the free corner spot nearest where it is (the way it came in).</summary>
    private FixVec2 StopSpot(SurfaceUnit u, int tile)
    {
        if (u.DeployOnArrival) return CentreOf(tile);
        var spots = Spots(tile, u);
        var first = BlockRadius(tile, u) == Fix64.Zero || AccessPoint(tile) != null;
        if (first && !Held(spots[0], tile, u)) return spots[0];
        FixVec2? best = null;
        var bestDist = Fix64.MaxValue;
        for (var pass = 0; pass < 2 && best == null; pass++)
            for (var i = first ? 1 : 0; i < spots.Count; i++)
            {
                if (pass == 0 && Held(spots[i], tile, u)) continue;
                var d = (spots[i] - u.Pos).Length();
                if (d >= bestDist) continue;
                best = spots[i];
                bestDist = d;
            }
        return best ?? spots[0];
    }

    /// <summary>Plans the unit's stretch across its current tile into its route.</summary>
    private void PlanLeg(SurfaceUnit u)
    {
        u._route.Clear();
        var tile = u.Tile;
        var end = u._path[0] == tile ? StopSpot(u, tile) : Crossing(tile, u._path[0]);
        var c = CentreOf(tile);
        var block = BlockRadius(tile, u);
        var ring = block > Fix64.Zero ? block + Lanes.Clearance : Fix64.Zero;
        var points = ring > Fix64.Zero ? AroundDisc(u.Pos, end, c, ring) : new List<FixVec2> { end };
        AvoidParked(u, tile, points, c, ring);
        u._route.AddRange(points);
        if (u._path[0] == tile && u.DeployOnArrival) MakeRoom(tile, u, _data.DeployedFootprintOf(u.DefId) + Lanes.Clearance + Fix64.Half);
    }

    /// <summary>The points from start to end that keep at least radius from centre (end last): straight if that clears it,
    /// else out to the ring, round it the shorter way and back in.</summary>
    private static List<FixVec2> AroundDisc(FixVec2 start, FixVec2 end, FixVec2 centre, Fix64 radius)
    {
        if (SegmentDistance(centre, start, end) >= radius) return new List<FixVec2> { end };
        var (a0, a1) = Angles(start, end, centre);
        return Arc(start, end, centre, radius * ArcGrow, a0, Wrap(a1 - a0), a1);
    }

    /// <summary>Swerves round vehicles parked on the tile (not ones that stand at the end of this stretch), if the detour
    /// fits inside the hex and outside the middle's ring.</summary>
    private void AvoidParked(SurfaceUnit u, int tile, List<FixVec2> points, FixVec2 centre, Fix64 ring)
    {
        var avoid = Lanes.AvoidRadius;
        foreach (var w in _units)
        {
            if (w == u || w.Tile != tile || w.Mode != UnitMode.Mobile || w._path.Count > 0) continue;
            var from = u.Pos;
            if ((from - w.Pos).Length() < avoid) continue; // starting beside it: just drive off
            for (var i = 0; i < points.Count; i++)
            {
                var to = points[i];
                if ((to - w.Pos).Length() < avoid) break; // stopping by it (a full tile): nothing to swerve round
                if (SegmentDistance(w.Pos, from, to) < avoid)
                {
                    if (Detour(from, to, w.Pos, avoid * ArcGrow, centre, ring) is { } detour)
                    {
                        points.RemoveAt(i);
                        points.InsertRange(i, detour);
                    }
                    break;
                }
                from = to;
            }
        }
    }

    private static List<FixVec2>? Detour(FixVec2 from, FixVec2 to, FixVec2 around, Fix64 radius, FixVec2 centre, Fix64 ring)
    {
        var (a0, a1) = Angles(from, to, around);
        var delta = Wrap(a1 - a0);
        for (var side = 0; side < 2; side++)
        {
            var d = side == 0 ? delta : delta > Fix64.Zero ? delta - Deg360 : delta + Deg360;
            var pts = Arc(from, to, around, radius, a0, d, a1);
            var ok = true;
            for (var i = 0; i < pts.Count - 1 && ok; i++)
            {
                var r = (pts[i] - centre).Length();
                ok = r <= InHex && (ring == Fix64.Zero || r >= ring);
            }
            if (ok) return pts;
        }
        return null;
    }

    /// <summary>Angles (degrees) of start and end seen from centre; a point on the centre takes the other's angle.</summary>
    private static (Fix64, Fix64) Angles(FixVec2 start, FixVec2 end, FixVec2 centre)
    {
        var s = start - centre;
        var e = end - centre;
        var tiny = Fix64.Parse("0.01");
        var sOn = s.LengthSquared() < tiny;
        var eOn = e.LengthSquared() < tiny;
        var a0 = Climate.Atan2Deg(sOn ? e.Y : s.Y, sOn ? e.X : s.X);
        var a1 = Climate.Atan2Deg(eOn ? s.Y : e.Y, eOn ? s.X : e.X);
        return (a0, a1);
    }

    /// <summary>Out from start to the ring (if inside it), round by delta degrees in steps of at most 30°, in to end.</summary>
    private static List<FixVec2> Arc(FixVec2 start, FixVec2 end, FixVec2 centre, Fix64 radius, Fix64 a0, Fix64 delta, Fix64 a1)
    {
        var pts = new List<FixVec2>();
        if ((start - centre).Length() < radius) pts.Add(centre + Dir(a0) * radius);
        var n = System.Math.Max(1, (Fix64.Abs(delta) / ArcStep).FloorToInt() + 1);
        for (var k = 1; k < n; k++) pts.Add(centre + Dir(a0 + delta * k / n) * radius);
        if ((end - centre).Length() < radius) pts.Add(centre + Dir(a1) * radius);
        pts.Add(end);
        return pts;
    }

    private static FixVec2 Dir(Fix64 deg) => new(Climate.CosDeg(deg), Climate.SinDeg(deg));

    private static Fix64 Wrap(Fix64 deg)
    {
        while (deg > Deg180) deg -= Deg360;
        while (deg <= -Deg180) deg += Deg360;
        return deg;
    }

    /// <summary>Distance from p to the segment a–b.</summary>
    public static Fix64 SegmentDistance(FixVec2 p, FixVec2 a, FixVec2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        if (len2 == Fix64.Zero) return (p - a).Length();
        var ap = p - a;
        var t = Fix64.Clamp((ap.X * ab.X + ap.Y * ab.Y) / len2, Fix64.Zero, Fix64.One);
        return (a + ab * t - p).Length();
    }

    /// <summary>Parked vehicles in the way of something new in the middle of a tile drive to another spot on it.</summary>
    private void MakeRoom(int tile, SurfaceUnit? except, Fix64 radius)
    {
        var c = CentreOf(tile);
        foreach (var w in _units)
        {
            if (w == except || w.Tile != tile || w.Mode != UnitMode.Mobile || w._path.Count > 0) continue;
            if (w.TyreChangeTicks > 0 || DriveProblem(w) != null || (w.Pos - c).Length() >= radius) continue;
            w._path.Add(tile);
            w._route.Clear();
        }
    }

    /// <summary>Stops where it is (no path, no route).</summary>
    private static void Halt(SurfaceUnit u)
    {
        u._path.Clear();
        u._route.Clear();
        u.WaitTicks = 0;
        u.PushTicks = 0;
        u.BlockedBy = 0;
    }

    /// <summary>Puts a unit on a tile at a free spot (appearing there: spawned, teleported, rolled back out).</summary>
    private void PlaceUnit(SurfaceUnit u, int tile, FixVec2? at = null)
    {
        u.Tile = tile;
        Halt(u);
        u.Pos = u.PrevPos = at ?? FreeSpot(tile, u);
    }

    /// <summary>A path for a unit: tiles with a building or a deployed vehicle cost 1 more to drive through (not the goal),
    /// so traffic goes round bases when that is about as quick.</summary>
    private List<int>? FindPath(int from, int to)
    {
        var busy = new bool[Map.Grid.Count];
        foreach (var s in _structures) busy[s.Tile] = true;
        foreach (var u in _units)
            if (u.Mode != UnitMode.Mobile) busy[u.Tile] = true;
        return Pathfinder.Find(Map, from, to, t => t != to && busy[t] ? 1 : 0);
    }

    /// <summary>
    /// Where each unit of a group order goes: the goal tile while it has parking spots left (counting vehicles already
    /// parked there that aren't in the group), the overflow on the nearest passable tiles round it. Units nearer the goal
    /// (then lower ids) pick first. Looked up by unit id, never iterated.
    /// </summary>
    private Dictionary<int, int> GroupGoals(List<SurfaceUnit> group, int goal)
    {
        var goals = new Dictionary<int, int>();
        if (group.Count <= 1) return goals;
        var order = new List<SurfaceUnit>(group);
        order.Sort((a, b) =>
        {
            var c = Map.Grid.Distance(a.Tile, goal).CompareTo(Map.Grid.Distance(b.Tile, goal));
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });
        var next = 0;
        for (var ring = 0; ring <= 3 && next < order.Count; ring++)
            foreach (var tile in Map.Grid.Within(goal, ring))
            {
                if (Map.Grid.Distance(goal, tile) != ring || !Map.Terrain(tile).Passable) continue;
                var room = Spots(tile, null).Count;
                foreach (var w in _units)
                    if (!group.Contains(w) && Claim(w, tile) != null) room--;
                for (; room > 0 && next < order.Count; room--) goals[order[next++].Id] = tile;
                if (next >= order.Count) break;
            }
        return goals;
    }

    private int Ticks(Fix64 seconds) => System.Math.Max(1, (seconds * Simulation.TickRate).FloorToInt());

    /// <summary>Waits for a vehicle close in front (true = don't move this tick); after wait_seconds drives on regardless.</summary>
    private bool Blocked(SurfaceUnit u)
    {
        if (u.PushTicks > 0)
        {
            u.PushTicks--;
            u.WaitTicks = 0;
            u.BlockedBy = 0;
            return false;
        }
        if (InTheWay(u) is { } w)
        {
            u.BlockedBy = w.Id;
            if (++u.WaitTicks < Ticks(Lanes.WaitSeconds)) return true;
            u.PushTicks = Ticks(Lanes.PushSeconds);
        }
        u.WaitTicks = 0;
        u.BlockedBy = 0;
        return false;
    }

    /// <summary>The first vehicle less than gap_side from the next gap_ahead of the unit's route, in front of it (not one
    /// that is itself waiting for this unit), or null.</summary>
    private SurfaceUnit? InTheWay(SurfaceUnit u)
    {
        var reach = Lanes.GapAhead + Lanes.GapSide;
        foreach (var w in _units)
        {
            if (w == u || w.Mode != UnitMode.Mobile || w.BlockedBy == u.Id) continue;
            var v = w.Pos - u.Pos;
            if (Fix64.Abs(v.X) > reach || Fix64.Abs(v.Y) > reach) continue;
            // A parked vehicle it couldn't swerve round (no room on a crowded tile) only stops it when it would hit it
            // nearly head on: brushing past is better than queueing behind something that never moves.
            var gap = w._path.Count == 0 ? Lanes.GapSide * 2 / 3 : Lanes.GapSide;
            var a = u.Pos;
            var left = Lanes.GapAhead;
            for (var i = 0; i < u._route.Count && left > Fix64.Zero; i++)
            {
                var b = u._route[i];
                var seg = b - a;
                var len = seg.Length();
                if (len == Fix64.Zero) continue;
                if (len > left)
                {
                    b = a + seg * (left / len);
                    len = left;
                }
                // Only what is in front: on the first stretch, not beside or behind it.
                var front = i > 0 || v.X * seg.X + v.Y * seg.Y > Fix64.Zero;
                if (front && SegmentDistance(w.Pos, a, b) < gap) return w;
                left -= len;
                a = b;
            }
        }
        return null;
    }

    private void MoveUnits(Simulation sim)
    {
        foreach (var unit in _units) unit.PrevPos = unit.Pos;
        foreach (var unit in _units)
        {
            if (unit._path.Count == 0 || unit.Mode != UnitMode.Mobile)
            {
                unit.WaitTicks = 0;
                unit.BlockedBy = 0;
                continue;
            }
            if (unit._route.Count == 0) PlanLeg(unit);
            if (Blocked(unit)) continue;
            var budget = SpeedPerTick(unit);
            for (var guard = 0; guard < 32 && budget > Fix64.Zero; guard++)
            {
                if (unit._route.Count == 0) PlanLeg(unit);
                var target = unit._route[0];
                var cost = Fix64.FromInt(System.Math.Max(1, Map.Terrain(unit.Tile).MoveCost));
                if (Gravity.HillCost != Fix64.One && Gravity.IsHill(Map.TerrainOf[unit.Tile])) cost *= Gravity.HillCost; // heavy worlds: hills harder
                var dist = (target - unit.Pos).Length();
                var need = dist * cost / TileSize;
                if (need > budget)
                {
                    unit.Pos += (target - unit.Pos) * (budget * TileSize / cost / dist);
                    break;
                }
                unit.Pos = target;
                budget -= need;
                unit._route.RemoveAt(0);
                if (unit._route.Count > 0) continue;
                if (unit._path[0] != unit.Tile)
                {
                    // Over the edge into the next tile (the last one stays in Path until it parks).
                    unit.Tile = unit._path[0];
                    if (unit._path.Count > 1) unit._path.RemoveAt(0);
                    var surveyRadius = SurveyRadiusOf(unit);
                    Reveal(unit.Owner, unit.Tile, surveyRadius);
                    MarkSightAt(unit, surveyRadius); // Visibility.cs: fog of war memory
                    continue;
                }
                unit._path.Clear();
                Arrive(sim, unit);
                break;
            }
        }
    }

    /// <summary>Parked at its spot in its last tile: unfurl (a mine order) or start founding (a construction rover).</summary>
    private void Arrive(Simulation sim, SurfaceUnit unit)
    {
        unit.WaitTicks = 0;
        unit.PushTicks = 0;
        unit.BlockedBy = 0;
        if (!unit.DeployOnArrival) return;
        unit.DeployOnArrival = false;
        if (unit.FoundTile >= 0) // a construction rover at its site (Founding.cs)
        {
            if (unit.FoundTile == unit.Tile) BeginFounding(sim, unit);
            else unit.FoundTile = -1;
            return;
        }
        if (DeployProblem(unit, unit.Tile) == null) StartDeploying(unit);
    }

    /// <summary>True when the unit stands on its tile's centre (where it unfurls or founds).</summary>
    private bool AtCentre(SurfaceUnit u) => (u.Pos - CentreOf(u.Tile)).Length() < Fix64.Half;
}
