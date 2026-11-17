using System.Collections.Generic;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// Landing fields and drop ships on the ground (surfaces "landing_field"; Surface/LandingField.cs). Pads = hexes of a
/// base's field (0 = no fields: ships climb straight back); Distance = hexes from the core to the field's first pad;
/// FootprintRadius = what vehicles keep clear in the middle of a pad. Times in seconds: UnloadSeconds (a delivery's ship,
/// from its arrival), ConvoyUnloadSeconds (a convoy ship that carried a vehicle, from the colony's touchdown),
/// ModuleSeconds (a ship that only carried an outpost module, from its touchdown), StaggerSeconds (× the ship's place in
/// its convoy), HopSeconds + HopSecondsPerTile × hexes up to HopMaxSeconds (the hop to the pad), RefuelSeconds (on the
/// pad, × the world's lift-cost curve).
/// </summary>
public sealed record LandingFieldRules(
    int Pads, int Distance, Fix64 FootprintRadius,
    Fix64 UnloadSeconds, Fix64 ConvoyUnloadSeconds, Fix64 ModuleSeconds, Fix64 StaggerSeconds,
    Fix64 HopSeconds, Fix64 HopSecondsPerTile, Fix64 HopMaxSeconds, Fix64 RefuelSeconds);

public sealed partial class GameData
{
    /// <summary>The landing field rules, or null (old data: drop ships climb straight back to the ship, as they did).</summary>
    public LandingFieldRules? LandingField { get; private init; }

    private static LandingFieldRules? ReadLandingField(JsonElement surfaces)
    {
        if (!surfaces.TryGetProperty("landing_field", out var f)) return null;
        Fix64 N(string p, int fallback) => f.TryGetProperty(p, out var v) ? Num(v) : Fix64.FromInt(fallback);
        int I(string p, int fallback) => f.TryGetProperty(p, out var v) && v.TryGetInt32(out var i) ? i : fallback;
        return new LandingFieldRules(I("pads", 4), I("distance", 3), N("footprint_radius", 8),
            N("unload_seconds", 14), N("convoy_unload_seconds", 24), N("module_seconds", 2), N("stagger_seconds", 2),
            N("hop_seconds", 8), N("hop_seconds_per_tile", 0), N("hop_max_seconds", 40), N("refuel_seconds", 45));
    }

    private void ValidateLandingField(List<string> problems)
    {
        if (LandingField is not { } f) return;
        const string where = "landing_field";
        if (f.Pads < 0 || f.Pads > 7) problems.Add($"{where}: pads must be 0 to 7.");
        if (f.Distance < 2 || f.Distance > 8) problems.Add($"{where}: distance must be 2 to 8 (the landing party rolls out on the hexes next to the core).");
        if (f.FootprintRadius <= Fix64.Zero || f.FootprintRadius + Lanes.Clearance > MaxRing) problems.Add($"{where}: footprint_radius must be positive and footprint_radius + lanes.clearance at most 12.");
        var max = Fix64.FromInt(3600);
        foreach (var (name, v, min) in new[]
                 {
                     ("unload_seconds", f.UnloadSeconds, 0), ("convoy_unload_seconds", f.ConvoyUnloadSeconds, 0), ("module_seconds", f.ModuleSeconds, 0),
                     ("stagger_seconds", f.StaggerSeconds, 0), ("hop_seconds", f.HopSeconds, 3), ("hop_seconds_per_tile", f.HopSecondsPerTile, 0),
                     ("hop_max_seconds", f.HopMaxSeconds, 3), ("refuel_seconds", f.RefuelSeconds, 0),
                 })
            if (v < Fix64.FromInt(min) || v > max) problems.Add($"{where}: {name} must be {min} to 3600.");
        if (f.HopMaxSeconds < f.HopSeconds) problems.Add($"{where}: hop_max_seconds can't be less than hop_seconds.");
    }
}
