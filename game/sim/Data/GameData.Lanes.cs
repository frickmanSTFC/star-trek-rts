using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// How vehicles drive inside the hexes (surfaces.json "lanes"; world units, a hex is 25 across and 12.5 from its centre
/// to an edge). LaneOffset = how far right of its direction of travel a vehicle crosses a hex edge; Clearance = how far
/// it keeps from a footprint (about half a vehicle's width plus a margin); DefaultFootprint = footprint of a building
/// without footprint_radius; ParkRadius = the parking ring on a tile with nothing in the middle; AvoidRadius = how wide
/// it swerves round a parked vehicle; GapAhead / GapSide = it waits while another vehicle is that close in front of it;
/// WaitSeconds = how long it waits before it drives on regardless, for PushSeconds.
/// </summary>
public sealed record LaneRules(Fix64 LaneOffset, Fix64 Clearance, Fix64 DefaultFootprint, Fix64 ParkRadius, Fix64 AvoidRadius,
    Fix64 GapAhead, Fix64 GapSide, Fix64 WaitSeconds, Fix64 PushSeconds)
{
    /// <summary>Used when surfaces.json has no "lanes" (old data in tests): the same numbers as the shipped data.</summary>
    public static readonly LaneRules Default = new(Fix64.Parse("2.5"), Fix64.FromInt(2), Fix64.Parse("7.5"), Fix64.FromInt(8),
        Fix64.Parse("4.5"), Fix64.FromInt(6), Fix64.FromInt(3), Fix64.FromInt(2), Fix64.FromInt(3));
}

/// <summary>
/// The ground a building (or a deployed vehicle) takes up round its tile centre: Radius in world units, and Access = where a
/// vehicle ordered onto its tile parks, in the building's own frame (x right, y = its front, +Z in the view; turned by its
/// facing), or null = on the ring facing the way the vehicle came.
/// </summary>
public sealed record Footprint(Fix64 Radius, FixVec2? Access);

// Lanes and footprints (surfaces.json "lanes", buildings.json footprint_radius / access, surfaces.json units' footprint_radius).
public sealed partial class GameData
{
    /// <summary>How far a footprint plus the clearance may reach from a tile centre (the hex's inner radius is 12.5).</summary>
    public static readonly Fix64 MaxRing = Fix64.FromInt(12);

    public LaneRules Lanes { get; private init; } = LaneRules.Default;

    private IReadOnlyDictionary<string, Footprint> BuildingFootprints { get; init; } = new Dictionary<string, Footprint>();
    private IReadOnlyDictionary<string, Fix64> UnitFootprints { get; init; } = new Dictionary<string, Fix64>();

    /// <summary>A building's footprint (lanes.default_footprint and no access point when the data gives none).</summary>
    public Footprint FootprintOf(string buildingId) =>
        BuildingFootprints.TryGetValue(buildingId, out var f) ? f : new Footprint(Lanes.DefaultFootprint, null);

    /// <summary>What a deployed or unfolding vehicle takes up round its tile centre (its rig or its founding site).</summary>
    public Fix64 DeployedFootprintOf(string unitId) => UnitFootprints.TryGetValue(unitId, out var r) ? r : Lanes.DefaultFootprint;

    private static LaneRules ReadLanes(JsonElement surfaces)
    {
        if (!surfaces.TryGetProperty("lanes", out var l)) return LaneRules.Default;
        var d = LaneRules.Default;
        Fix64 N(string p, Fix64 fallback) => l.TryGetProperty(p, out var v) ? Num(v) : fallback;
        return new LaneRules(N("lane_offset", d.LaneOffset), N("clearance", d.Clearance), N("default_footprint", d.DefaultFootprint),
            N("park_radius", d.ParkRadius), N("avoid_radius", d.AvoidRadius), N("gap_ahead", d.GapAhead), N("gap_side", d.GapSide),
            N("wait_seconds", d.WaitSeconds), N("push_seconds", d.PushSeconds));
    }

    private static Dictionary<string, Footprint> ReadBuildingFootprints(JsonElement buildings, List<string> problems)
    {
        var result = new Dictionary<string, Footprint>(System.StringComparer.Ordinal);
        foreach (var b in buildings.GetProperty("buildings").EnumerateArray())
        {
            if (!b.TryGetProperty("footprint_radius", out var r)) continue;
            FixVec2? access = null;
            if (b.TryGetProperty("access", out var a))
            {
                if (a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 2) access = new FixVec2(Num(a[0]), Num(a[1]));
                else problems.Add($"Building {Str(b, "id")}: access must be [x, z].");
            }
            result.TryAdd(Str(b, "id"), new Footprint(Num(r), access));
        }
        return result;
    }

    private static Dictionary<string, Fix64> ReadUnitFootprints(JsonElement surfaces)
    {
        var result = new Dictionary<string, Fix64>(System.StringComparer.Ordinal);
        foreach (var u in surfaces.GetProperty("units").EnumerateArray())
            if (u.TryGetProperty("footprint_radius", out var r)) result.TryAdd(Str(u, "id"), Num(r));
        return result;
    }

    private void ValidateLanes(List<string> problems)
    {
        var l = Lanes;
        var half = Fix64.Parse("12.5");
        if (l.LaneOffset < Fix64.Zero || l.LaneOffset > Fix64.FromInt(5)) problems.Add("lanes: lane_offset must be 0 to 5 (an edge is 14.4 long).");
        if (l.Clearance <= Fix64.Zero) problems.Add("lanes: clearance must be positive.");
        if (l.DefaultFootprint <= Fix64.Zero || l.DefaultFootprint + l.Clearance > MaxRing) problems.Add("lanes: default_footprint + clearance must be positive and at most 12.");
        if (l.ParkRadius <= Fix64.Zero || l.ParkRadius > MaxRing) problems.Add("lanes: park_radius must be 0 to 12.");
        if (l.AvoidRadius <= Fix64.Zero || l.AvoidRadius > Fix64.FromInt(6)) problems.Add("lanes: avoid_radius must be 0 to 6.");
        if (l.GapAhead <= Fix64.Zero || l.GapSide <= Fix64.Zero) problems.Add("lanes: gap_ahead and gap_side must be positive.");
        if (l.GapSide >= l.LaneOffset * 2 + Fix64.One && l.LaneOffset > Fix64.Zero)
            problems.Add("lanes: gap_side must be under twice lane_offset (plus 1), or oncoming vehicles would stop for each other.");
        if (l.WaitSeconds <= Fix64.Zero || l.PushSeconds <= Fix64.Zero) problems.Add("lanes: wait_seconds and push_seconds must be positive.");

        var surface = new HashSet<string>(BuildMenu, System.StringComparer.Ordinal) { Landing.HubBuilding };
        foreach (var b in Buildings.Where(b => b.IsCore)) surface.Add(b.Id);
        foreach (var (id, f) in BuildingFootprints)
        {
            var where = $"Building {id}";
            if (!_buildings.ContainsKey(id)) continue;
            if (f.Radius <= Fix64.Zero || f.Radius + l.Clearance > MaxRing) problems.Add($"{where}: footprint_radius must be positive and footprint_radius + lanes.clearance at most 12.");
            if (f.Access is { } a)
            {
                var r = a.Length();
                if (r < f.Radius || r >= half - Fix64.Parse("0.1")) problems.Add($"{where}: access must be between footprint_radius and 12.4 from the centre (it is {r}).");
            }
        }
        foreach (var id in surface)
            if (_buildings.ContainsKey(id) && !BuildingFootprints.ContainsKey(id)) problems.Add($"Building {id}: a surface building needs footprint_radius (vehicles drive round it).");
        foreach (var (id, r) in UnitFootprints)
        {
            if (!_surfaceUnits.ContainsKey(id)) problems.Add($"Surface unit {id}: unknown unit with footprint_radius.");
            if (r <= Fix64.Zero || r + l.Clearance > MaxRing) problems.Add($"Surface unit {id}: footprint_radius must be positive and footprint_radius + lanes.clearance at most 12.");
        }
        foreach (var u in SurfaceUnits.Where(u => u.CanMine || u.CanFound))
            if (!UnitFootprints.ContainsKey(u.Id)) problems.Add($"Surface unit {u.Id}: deploys on its tile, so it needs footprint_radius.");
    }
}
