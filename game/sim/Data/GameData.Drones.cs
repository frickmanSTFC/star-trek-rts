using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// A scout drone's flight rules (surfaces units' "drone" block; docs/missions-contract.md B). BatterySeconds of flight on
/// a full charge; Reserve = share of the battery it keeps (it turns home when what is left only just covers the way back
/// and the landing plus this; it launches only with at least Reserve + <see cref="LaunchMargin"/>); RechargeSeconds empty
/// to full on a working pad; LaunchSeconds / LandSeconds off and onto its bay; LoiterSeconds circling its target;
/// LostSeconds = how long a drone that ran dry lies there before it is removed.
/// </summary>
public sealed record DroneDef(string UnitId, Fix64 BatterySeconds, Fix64 Reserve, Fix64 RechargeSeconds, Fix64 LaunchSeconds,
    Fix64 LandSeconds, Fix64 LoiterSeconds, Fix64 LostSeconds)
{
    /// <summary>A docked drone launches only with at least Reserve + this much battery.</summary>
    public static readonly Fix64 LaunchMargin = Fix64.Ratio(1, 10);
}

/// <summary>Comms relay satellites (surfaces/comms.json relay): cost (from stock, like a survey satellite), how many a player
/// may have up (survey satellites count separately), the coverage radius round the station in tiles, seconds drifting to
/// the station (first deploy and after a retask) and names for the call signs.</summary>
public sealed record RelayRules(IReadOnlyList<ResourceAmount> Cost, int Max, int Radius, int DeploySeconds, int RetaskSeconds,
    IReadOnlyList<string> CallSigns);

/// <summary>Comms coverage (surfaces/comms.json): a finished colony core's antenna reaches CoreRadius tiles; Relay = the
/// relay satellites (null = none in this game).</summary>
public sealed record CommsRules(int CoreRadius, RelayRules? Relay);

// Drones, Drone Pads and comms (surfaces units' "aerial" / "drone", buildings' "drone_bays", surfaces "comms").
public sealed partial class GameData
{
    /// <summary>Every drone type (units with a "drone" block), in file order.</summary>
    public IReadOnlyList<DroneDef> Drones { get; private init; } = Array.Empty<DroneDef>();

    /// <summary>Comms coverage rules, or null (then every tile counts as covered: old data).</summary>
    public CommsRules? Comms { get; private init; }

    private IReadOnlyList<string> AerialUnits { get; init; } = Array.Empty<string>();
    private IReadOnlyDictionary<string, int> DroneBayCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>The drone rules of a unit, or null if it isn't a drone.</summary>
    public DroneDef? DroneOf(string unitId)
    {
        foreach (var d in Drones)
            if (d.UnitId == unitId) return d;
        return null;
    }

    /// <summary>True for a unit that flies (surfaces units' "aerial": true).</summary>
    public bool IsAerialUnit(string unitId) => AerialUnits.Contains(unitId);

    /// <summary>How many drone bays a building has (buildings' "drone_bays"; 0 = not a drone pad).</summary>
    public int DroneBaysOf(string buildingId) => DroneBayCounts.TryGetValue(buildingId, out var n) ? n : 0;

    private static List<DroneDef> ReadDrones(JsonElement surfaces)
    {
        var list = new List<DroneDef>();
        foreach (var u in surfaces.GetProperty("units").EnumerateArray())
        {
            if (!u.TryGetProperty("drone", out var d)) continue;
            list.Add(new DroneDef(Str(u, "id"), Num(d.GetProperty("battery_seconds")), Num(d.GetProperty("reserve")),
                Num(d.GetProperty("recharge_seconds")), Num(d.GetProperty("launch_seconds")), Num(d.GetProperty("land_seconds")),
                Num(d.GetProperty("loiter_seconds")), Num(d.GetProperty("lost_seconds"))));
        }
        return list;
    }

    private static List<string> ReadAerialUnits(JsonElement surfaces)
    {
        var list = new List<string>();
        foreach (var u in surfaces.GetProperty("units").EnumerateArray())
            if (u.TryGetProperty("aerial", out var a) && a.ValueKind == JsonValueKind.True) list.Add(Str(u, "id"));
        return list;
    }

    private static Dictionary<string, int> ReadDroneBays(JsonElement buildings)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in buildings.GetProperty("buildings").EnumerateArray())
            if (b.TryGetProperty("drone_bays", out var n)) result.TryAdd(Str(b, "id"), n.GetInt32());
        return result;
    }

    private static CommsRules? ReadComms(JsonElement surfaces, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        if (!surfaces.TryGetProperty("comms", out var c)) return null;
        RelayRules? relay = null;
        if (c.TryGetProperty("relay", out var r))
            relay = new RelayRules(amounts(r, "cost", "Comms relay"), r.GetProperty("max").GetInt32(), r.GetProperty("radius").GetInt32(),
                r.GetProperty("deploy_seconds").GetInt32(), r.TryGetProperty("retask_seconds", out var rs) ? rs.GetInt32() : r.GetProperty("deploy_seconds").GetInt32(),
                r.TryGetProperty("call_signs", out var cs) ? cs.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>());
        return new CommsRules(c.GetProperty("core_radius").GetInt32(), relay);
    }

    private void ValidateDrones(List<string> problems)
    {
        var maxSeconds = Fix64.FromInt(3600);
        foreach (var d in Drones)
        {
            var where = $"Surface unit {d.UnitId} drone";
            if (!_surfaceUnits.TryGetValue(d.UnitId, out var u)) continue;
            if (!IsAerialUnit(d.UnitId)) problems.Add($"{where}: a drone must be aerial.");
            if (d.BatterySeconds < Fix64.One || d.BatterySeconds > maxSeconds) problems.Add($"{where}: battery_seconds must be 1 to 3600.");
            if (d.RechargeSeconds < Fix64.One || d.RechargeSeconds > maxSeconds) problems.Add($"{where}: recharge_seconds must be 1 to 3600.");
            if (d.Reserve < Fix64.Zero || d.Reserve + DroneDef.LaunchMargin >= Fix64.One) problems.Add($"{where}: reserve must be 0 to under 0.9.");
            foreach (var (name, v) in new[] { ("launch_seconds", d.LaunchSeconds), ("land_seconds", d.LandSeconds), ("loiter_seconds", d.LoiterSeconds), ("lost_seconds", d.LostSeconds) })
                if (v < Fix64.Zero || v > Fix64.FromInt(600)) problems.Add($"{where}: {name} must be 0 to 600.");
            if (d.LaunchSeconds + d.LandSeconds >= d.BatterySeconds * (Fix64.One - d.Reserve - DroneDef.LaunchMargin))
                problems.Add($"{where}: launch_seconds + land_seconds must leave battery to fly.");
            if (u.CanMine || u.CanFound || u.Damage != null || u.Tyres != null || u.Repair != null || u.Breakdown != null)
                problems.Add($"{where}: a drone can't mine, found, take damage, have tyres or be repaired.");
            if (!_resources.TryGetValue(d.UnitId, out var item)) problems.Add($"{where}: needs a resource '{d.UnitId}' (the packed drone a pad unpacks).");
            else if (Fabrication?.Recipe(d.UnitId) == null || !item.IsPartItem) problems.Add($"{where}: the resource '{d.UnitId}' needs a fabrication recipe (tier drone).");
            if (Buildings.Any(b => b.Produces.Contains(d.UnitId))) problems.Add($"{where}: drones come from a Drone Pad, not a Vehicle Bay.");
            if (Landing.Units.Contains(d.UnitId) || (Orbit != null && (Orbit.ShuttlePayloads.Contains(d.UnitId) || Orbit.TransporterLevels.Any(l => l.Payloads.Contains(d.UnitId)))))
                problems.Add($"{where}: drones can't land with the colony or come down from orbit.");
        }
        foreach (var id in AerialUnits)
        {
            if (!_surfaceUnits.ContainsKey(id)) problems.Add($"Surface unit {id}: unknown aerial unit.");
            else if (DroneOf(id) == null) problems.Add($"Surface unit {id}: aerial, but has no drone block (only drones fly for now).");
        }
        foreach (var (id, bays) in DroneBayCounts)
        {
            if (!_buildings.ContainsKey(id)) continue;
            if (bays < 1 || bays > 8) problems.Add($"Building {id}: drone_bays must be 1 to 8.");
            if (Drones.Count == 0) problems.Add($"Building {id}: has drone bays, but no unit is a drone.");
            if (_buildings[id].IsCore) problems.Add($"Building {id}: a colony core can't be a drone pad.");
        }
        if (Comms is { } c)
        {
            if (c.CoreRadius < 0 || c.CoreRadius > 40) problems.Add("Comms: core_radius must be 0 to 40.");
            if (c.Relay is { } r)
            {
                if (r.Max < 0 || r.Max > 20) problems.Add("Comms relay: max must be 0 to 20.");
                if (r.Radius < 1 || r.Radius > 100) problems.Add("Comms relay: radius must be 1 to 100.");
                if (r.DeploySeconds < 0 || r.RetaskSeconds < 0 || r.DeploySeconds > 600 || r.RetaskSeconds > 600) problems.Add("Comms relay: deploy_seconds and retask_seconds must be 0 to 600.");
                if (r.Cost.Any(a => a.Amount < Fix64.Zero)) problems.Add("Comms relay cost: amounts can't be negative.");
                if (r.CallSigns.Any(string.IsNullOrWhiteSpace)) problems.Add("Comms relay: call_signs can't be empty.");
            }
        }
    }
}
