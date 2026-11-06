using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Rover damage and flat tyres (surfaces.json units' "damage" and "tyres"): read and checked here so the loader and
// the checks stay together.
public sealed partial class GameData
{
    private static DamageRules? ReadDamage(JsonElement unit)
    {
        if (!unit.TryGetProperty("damage", out var d)) return null;
        var terrain = new List<TerrainWear>();
        if (d.TryGetProperty("terrain", out var t))
            foreach (var p in t.EnumerateObject())
                terrain.Add(new TerrainWear(p.Name,
                    p.Value.TryGetProperty("wear", out var w) ? Num(w) : Fix64.Zero,
                    p.Value.TryGetProperty("hazard", out var h) ? Num(h) : Fix64.Zero,
                    p.Value.TryGetProperty("flat", out var f) ? Num(f) : Fix64.Zero));
        var hazards = new List<RoverHazard>();
        if (d.TryGetProperty("hazards", out var hz))
            foreach (var v in hz.EnumerateArray())
            {
                var dmg = v.GetProperty("damage");
                hazards.Add(new RoverHazard(Str(v, "id"), Str(v, "name"), v.GetProperty("weight").GetInt32(), dmg[0].GetInt32(), dmg[1].GetInt32(),
                    v.TryGetProperty("terrain", out var on) ? on.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>(),
                    v.TryGetProperty("flat", out var fl) && fl.GetBoolean()));
            }
        var (wildChance, wildMin, wildMax) = (Fix64.Zero, 0, 0);
        if (d.TryGetProperty("wildlife", out var wild))
        {
            wildChance = Num(wild.GetProperty("chance_per_min"));
            var wd = wild.GetProperty("damage");
            (wildMin, wildMax) = (wd[0].GetInt32(), wd[1].GetInt32());
        }
        Fix64 Or(string name, Fix64 fallback) => d.TryGetProperty(name, out var x) ? Num(x) : fallback;
        return new DamageRules(terrain, hazards, Or("toxic_per_min", Fix64.Zero), wildChance, wildMin, wildMax,
            Or("slow_below", Fix64.FromInt(50)), Or("slowest_drive", Fix64.Half), Or("sensors_below", Fix64.Zero),
            d.TryGetProperty("sensor_loss", out var sl) ? sl.GetInt32() : 0, Or("crawl", Fix64.Ratio(15, 100)));
    }

    private static TyreRules? ReadTyres(JsonElement unit, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        if (!unit.TryGetProperty("tyres", out var t)) return null;
        return new TyreRules(t.GetProperty("wheels").GetInt32(), t.TryGetProperty("spares", out var sp) ? sp.GetInt32() : 0,
            t.TryGetProperty("low_integrity", out var li) ? Num(li) : Fix64.Zero, Num(t.GetProperty("flat_speed")), Num(t.GetProperty("two_flat_speed")),
            t.TryGetProperty("flat_wear", out var fw) ? Num(fw) : Fix64.Zero, t.GetProperty("change_seconds").GetInt32(),
            amounts(t, "pad_cost", $"Surface unit {Str(unit, "id")} tyres"));
    }

    private void ValidateRoverDamage(List<string> problems)
    {
        static bool Share(Fix64 v) => v > Fix64.Zero && v <= Fix64.One;
        var hundred = Fix64.FromInt(100);
        foreach (var u in SurfaceUnits)
        {
            var where = $"Surface unit {u.Id}";
            if (u.Damage is { } d)
            {
                if (u.Repair == null) problems.Add($"{where}: it takes damage, so it needs repair rules.");
                if (u.CanMine) problems.Add($"{where}: a miner uses breakdown, not damage.");
                foreach (var t in d.Terrain)
                {
                    if (!_terrain.TryGetValue(t.Terrain, out var td)) problems.Add($"{where} damage: unknown terrain '{t.Terrain}'.");
                    else if (!td.Passable) problems.Add($"{where} damage: {t.Terrain} isn't passable, so it can't wear a rover.");
                    if (t.Wear < Fix64.Zero || t.Hazard < Fix64.Zero || t.Flat < Fix64.Zero) problems.Add($"{where} damage {t.Terrain}: wear, hazard and flat can't be negative.");
                    if (t.Hazard > Fix64.Zero && !d.Hazards.Any(h => h.Weight > 0 && (h.Terrain.Count == 0 || h.Terrain.Contains(t.Terrain))))
                        problems.Add($"{where} damage {t.Terrain}: has a hazard chance but no hazard is listed for it.");
                }
                foreach (var t in Terrain.Where(t => t.Passable))
                    if (d.On(t.Id) == null) problems.Add($"{where} damage: no entry for passable terrain '{t.Id}'.");
                foreach (var h in d.Hazards)
                {
                    if (h.Weight < 0) problems.Add($"{where} hazard {h.Id}: weight can't be negative.");
                    if (h.DamageMin < 0 || h.DamageMax < h.DamageMin || h.DamageMax > 100) problems.Add($"{where} hazard {h.Id}: damage must be a range within 0..100.");
                    foreach (var on in h.Terrain)
                        if (!_terrain.ContainsKey(on)) problems.Add($"{where} hazard {h.Id}: unknown terrain '{on}'.");
                }
                if (d.ToxicPerMinute < Fix64.Zero || d.WildlifeChancePerMinute < Fix64.Zero) problems.Add($"{where} damage: toxic_per_min and wildlife chance can't be negative.");
                if (d.WildlifeMin < 0 || d.WildlifeMax < d.WildlifeMin || d.WildlifeMax > 100) problems.Add($"{where} damage: wildlife damage must be a range within 0..100.");
                if (d.SlowBelow <= Fix64.Zero || d.SlowBelow > hundred) problems.Add($"{where} damage: slow_below must be in (0, 100].");
                if (!Share(d.SlowestDrive) || !Share(d.Crawl)) problems.Add($"{where} damage: slowest_drive and crawl must be in (0, 1].");
                if (d.SensorsBelow < Fix64.Zero || d.SensorsBelow > hundred || d.SensorLoss < 0) problems.Add($"{where} damage: sensors_below must be in 0..100 and sensor_loss 0 or more.");
            }
            if (u.Tyres is { } y)
            {
                if (u.Damage == null) problems.Add($"{where}: tyres need a damage block (the flat chances are per terrain there).");
                if (y.Wheels != 4 && y.Wheels != 6) problems.Add($"{where} tyres: wheels must be 4 or 6 (the rover models' layouts).");
                if (y.Spares < 0 || y.ChangeSeconds < 1) problems.Add($"{where} tyres: spares must be 0 or more and change_seconds at least 1.");
                if (!Share(y.FlatSpeed) || !Share(y.TwoFlatSpeed) || y.TwoFlatSpeed > y.FlatSpeed)
                    problems.Add($"{where} tyres: flat_speed and two_flat_speed must be in (0, 1], two flats no faster than one.");
                if (y.LowIntegrity < Fix64.Zero || y.FlatWear < Fix64.Zero || y.PadCost.Any(a => a.Amount < Fix64.Zero))
                    problems.Add($"{where} tyres: low_integrity, flat_wear and pad_cost can't be negative.");
            }
        }
    }
}
