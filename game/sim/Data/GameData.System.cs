using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// The star system around a colony (maps document: maps/generator.json "system_generator", maps/system.json "system").
// Read here, used by Space/SystemGenerator.cs and Space/StarSystem.cs.

/// <summary>A weighted pick from data: an id and its weight (0 = never).</summary>
public readonly record struct Weighted(string Id, int Weight);

/// <summary>An orbit zone: the band of orbit distances it covers (map units), the au that band stands for round a Sol-bright
/// star (scaled by √luminosity) and the class weights rolled there.</summary>
public sealed record OrbitZone(string Id, Fix64 MinDistance, Fix64 MaxDistance, Fix64 MinAu, Fix64 MaxAu, IReadOnlyList<Weighted> Weights)
{
    public bool Contains(Fix64 distance) => distance >= MinDistance && distance <= MaxDistance;

    /// <summary>The au at this distance (linear across the zone, clamped), before the star's brightness.</summary>
    public Fix64 AuAt(Fix64 distance)
    {
        var span = MaxDistance - MinDistance;
        var share = span > Fix64.Zero ? Fix64.Clamp((distance - MinDistance) / span, Fix64.Zero, Fix64.One) : Fix64.Half;
        return MinAu + (MaxAu - MinAu) * share;
    }

    public Fix64 Middle => (MinDistance + MaxDistance) / 2;
}

/// <summary>A [min, max] range of whole numbers per class (moon counts, sizes, day lengths); Class "default" = the rest.</summary>
public readonly record struct ClassRange(string Class, int Min, int Max);

/// <summary>Climate numbers a generated body of this class gets on top of its class's climate template.</summary>
public sealed record GeneratedClimateClass(string Class, int DayMin, int DayMax, Fix64 TiltMin, Fix64 TiltMax, Fix64 EccentricityMin,
    Fix64 EccentricityMax, Fix64 Albedo, Fix64 GreenhouseC, Fix64 Prograde);

/// <summary>maps/generator.json "system_generator": how a star system is generated from a seed (see its _notes).</summary>
public sealed record SystemGeneratorRules(
    IReadOnlyList<Weighted> StarClasses,
    int StarRadiusMin,
    int StarRadiusMax,
    IReadOnlyList<string> StarNames,
    int PlanetsMin,
    int PlanetsMax,
    Fix64 OrbitMin,
    Fix64 OrbitMax,
    Fix64 SlotJitter,
    IReadOnlyList<OrbitZone> Zones,
    string HabitableZone,
    IReadOnlyList<string> Belts,
    string BeltSuffix,
    IReadOnlyList<ClassRange> MoonCounts,
    IReadOnlyList<(string Parent, IReadOnlyList<Weighted> Weights)> MoonWeights,
    Fix64 MoonDistanceMin,
    Fix64 MoonDistanceMax,
    IReadOnlyList<string> MoonSuffixes,
    IReadOnlyList<ClassRange> Sizes,
    IReadOnlyList<string> Names,
    string LandableFallback,
    int YearSecondsAt1Au,
    IReadOnlyList<GeneratedClimateClass> Climate,
    int MoonDayMin,
    int MoonDayMax)
{
    public const string Default = "default";

    public bool IsBelt(string classId) => Belts.Contains(classId);

    public OrbitZone? Zone(string id) => Zones.FirstOrDefault(z => z.Id == id);

    /// <summary>The zone an orbit distance falls in (the nearest one when it is outside them all).</summary>
    public OrbitZone ZoneAt(Fix64 distance)
    {
        foreach (var z in Zones)
            if (z.Contains(distance)) return z;
        return distance < Zones[0].MinDistance ? Zones[0] : Zones[^1];
    }

    /// <summary>[min, max] moons for a parent class (0, 0 when it isn't listed).</summary>
    public (int Min, int Max) MoonsFor(string parentClass)
    {
        foreach (var r in MoonCounts)
            if (r.Class == parentClass) return (r.Min, r.Max);
        return (0, 0);
    }

    public IReadOnlyList<Weighted> MoonWeightsFor(string parentClass)
    {
        foreach (var (p, w) in MoonWeights)
            if (p == parentClass) return w;
        foreach (var (p, w) in MoonWeights)
            if (p == Default) return w;
        return Array.Empty<Weighted>();
    }

    public (int Min, int Max) SizeOf(string classId) => RangeOf(Sizes, classId, (1, 2));

    public GeneratedClimateClass? ClimateOf(string classId) =>
        Climate.FirstOrDefault(c => c.Class == classId) ?? Climate.FirstOrDefault(c => c.Class == Default);

    private static (int, int) RangeOf(IReadOnlyList<ClassRange> list, string classId, (int, int) fallback)
    {
        foreach (var r in list)
            if (r.Class == classId) return (r.Min, r.Max);
        foreach (var r in list)
            if (r.Class == Default) return (r.Min, r.Max);
        return fallback;
    }
}

/// <summary>maps/system.json "system": flight times, long-range scans, body satellites and neutral traffic (see its _notes).</summary>
public sealed record StarSystemRules(
    int TravelMinSeconds,
    int TravelMaxSeconds,
    Fix64 TravelNear,
    Fix64 TravelFar,
    Fix64 NeutralScale,
    Fix64 ScanRange,
    int ScanCooldownSeconds,
    IReadOnlyList<ResourceAmount> SatelliteCost,
    int SatelliteMax,
    int TrafficMin,
    int TrafficMax,
    IReadOnlyList<string> TrafficFactions,
    IReadOnlyList<(string Role, IReadOnlyList<string> Cargo)> Cargo,
    int DwellMinSeconds,
    int DwellMaxSeconds,
    IReadOnlyList<string> ShipNames,
    IReadOnlyList<(string Faction, string Prefix)> Registry,
    int RegistryMin,
    int RegistryMax)
{
    public IReadOnlyList<string> CargoFor(string role)
    {
        foreach (var (r, c) in Cargo)
            if (r == role) return c;
        return Array.Empty<string>();
    }

    public string RegistryPrefix(string faction)
    {
        foreach (var (f, p) in Registry)
            if (f == faction) return p;
        return "";
    }
}

public sealed partial class GameData
{
    /// <summary>How star systems are generated (maps/generator.json), or null when the data has none.</summary>
    public SystemGeneratorRules? SystemGenerator { get; private init; }

    /// <summary>Flight, scan, satellite and traffic rules of a star system (maps/system.json), or null when the data has none.</summary>
    public StarSystemRules? SystemRules { get; private init; }

    /// <summary>Whether this planet class has a playable surface (surfaces.json planets): today class_m and class_l.</summary>
    public bool IsLandableClass(string classId) => _surfaces.ContainsKey(classId);

    /// <summary>The civilian classes neutral traffic flies (system.traffic: those factions, a role with cargo), in data order.</summary>
    public IReadOnlyList<ShipClassDef> TrafficClasses() =>
        SystemRules == null ? Array.Empty<ShipClassDef>()
            : ShipClasses.Where(c => SystemRules.TrafficFactions.Contains(c.Faction) && SystemRules.CargoFor(c.Role).Count > 0).ToList();

    private static List<Weighted> ReadWeights(JsonElement obj) =>
        obj.EnumerateObject().Select(p => new Weighted(p.Name, p.Value.GetInt32())).ToList();

    private static List<ClassRange> ReadRanges(JsonElement obj) =>
        obj.EnumerateObject().Select(p => new ClassRange(p.Name, p.Value[0].GetInt32(), p.Value[1].GetInt32())).ToList();

    private static List<string> ReadStrings(JsonElement arr) => arr.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

    private static SystemGeneratorRules? ReadSystemGenerator(JsonElement maps)
    {
        if (!maps.TryGetProperty("system_generator", out var g)) return null;
        var star = g.GetProperty("star");
        var planets = g.GetProperty("planets");
        var orbit = g.GetProperty("orbit");
        var moons = g.GetProperty("moons");
        var climate = g.GetProperty("climate");
        var moonDistance = moons.GetProperty("distance");
        var moonDay = climate.GetProperty("moon_day_seconds");
        return new SystemGeneratorRules(
            ReadWeights(star.GetProperty("classes")),
            star.GetProperty("radius")[0].GetInt32(), star.GetProperty("radius")[1].GetInt32(),
            ReadStrings(star.GetProperty("names")),
            planets[0].GetInt32(), planets[1].GetInt32(),
            Num(orbit.GetProperty("min")), Num(orbit.GetProperty("max")), Num(orbit.GetProperty("slot_jitter")),
            g.GetProperty("zones").EnumerateArray().Select(z =>
            {
                var d = z.GetProperty("distance");
                var au = z.GetProperty("au");
                return new OrbitZone(Str(z, "id"), Num(d[0]), Num(d[1]), Num(au[0]), Num(au[1]), ReadWeights(z.GetProperty("weights")));
            }).ToList(),
            Str(g, "habitable_zone"),
            ReadStrings(g.GetProperty("belts")),
            Str(g, "belt_suffix"),
            ReadRanges(moons.GetProperty("counts")),
            moons.GetProperty("weights").EnumerateObject().Select(p => (p.Name, (IReadOnlyList<Weighted>)ReadWeights(p.Value))).ToList(),
            Num(moonDistance[0]), Num(moonDistance[1]),
            ReadStrings(moons.GetProperty("suffixes")),
            ReadRanges(g.GetProperty("sizes")),
            ReadStrings(g.GetProperty("names")),
            Str(g, "landable_fallback"),
            climate.GetProperty("year_seconds_at_1_au").GetInt32(),
            climate.GetProperty("classes").EnumerateObject().Select(p =>
            {
                var c = p.Value;
                var day = c.GetProperty("day_seconds");
                var tilt = c.GetProperty("axial_tilt_deg");
                var ecc = c.GetProperty("eccentricity");
                return new GeneratedClimateClass(p.Name, day[0].GetInt32(), day[1].GetInt32(), Num(tilt[0]), Num(tilt[1]), Num(ecc[0]), Num(ecc[1]),
                    Num(c.GetProperty("albedo")), Num(c.GetProperty("greenhouse_c")), Num(c.GetProperty("prograde")));
            }).ToList(),
            moonDay[0].GetInt32(), moonDay[1].GetInt32());
    }

    private static StarSystemRules? ReadSystemRules(JsonElement maps, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        if (!maps.TryGetProperty("system", out var s)) return null;
        var travel = s.GetProperty("travel");
        var scan = s.GetProperty("scan");
        var sat = s.GetProperty("satellite");
        var t = s.GetProperty("traffic");
        var seconds = travel.GetProperty("seconds");
        var distance = travel.GetProperty("distance");
        var ships = t.GetProperty("ships");
        var dwell = t.GetProperty("dwell_seconds");
        var numbers = t.GetProperty("registry_numbers");
        return new StarSystemRules(
            seconds[0].GetInt32(), seconds[1].GetInt32(), Num(distance[0]), Num(distance[1]), Num(travel.GetProperty("neutral_scale")),
            Num(scan.GetProperty("range")), scan.GetProperty("cooldown_seconds").GetInt32(),
            amounts(sat, "cost", "System satellite"), sat.GetProperty("max").GetInt32(),
            ships[0].GetInt32(), ships[1].GetInt32(),
            ReadStrings(t.GetProperty("factions")),
            t.GetProperty("cargo").EnumerateObject().Select(p => (p.Name, (IReadOnlyList<string>)ReadStrings(p.Value))).ToList(),
            dwell[0].GetInt32(), dwell[1].GetInt32(),
            ReadStrings(t.GetProperty("names")),
            t.GetProperty("registry").EnumerateObject().Select(p => (p.Name, p.Value.GetString() ?? "")).ToList(),
            numbers[0].GetInt32(), numbers[1].GetInt32());
    }

    /// <summary>System generator and system rule checks: every id resolves, weights and ranges are sane, the fallback class
    /// can be landed on, every class the generator can roll has a climate template, neutral traffic has classes to fly.</summary>
    private void ValidateSystem(List<string> problems)
    {
        var zero = Fix64.Zero;
        if (SystemGenerator is not { } g)
        {
            problems.Add("maps: no system_generator (maps/generator.json).");
        }
        else
        {
            const string w = "System generator";
            void Weights(IReadOnlyList<Weighted> list, string where, bool planetClasses)
            {
                if (list.Count == 0 || list.Sum(x => x.Weight > 0 ? x.Weight : 0) <= 0) problems.Add($"{where}: needs weights with a positive sum.");
                foreach (var x in list)
                {
                    if (x.Weight < 0) problems.Add($"{where}: weight for {x.Id} can't be negative.");
                    if (planetClasses) CheckClass(x.Id, where);
                }
            }
            void CheckClass(string id, string where)
            {
                if (!_planetClasses.ContainsKey(id)) problems.Add($"{where}: unknown planet class '{id}'.");
                else if (Climate != null && Climate.ClassClimate(id) == null) problems.Add($"{where}: class {id} has no climate template (climate/classes.json).");
            }

            Weights(g.StarClasses, $"{w} star classes", false);
            foreach (var s in g.StarClasses)
                if (Climate != null && Climate.Star(s.Id) == null) problems.Add($"{w} star classes: '{s.Id}' is not in climate/stars.json.");
            if (g.StarRadiusMin < 1 || g.StarRadiusMax < g.StarRadiusMin) problems.Add($"{w}: star radius needs 1 <= min <= max.");
            if (g.StarNames.Count == 0 || g.StarNames.Any(string.IsNullOrWhiteSpace)) problems.Add($"{w}: star names can't be empty.");
            if (g.PlanetsMin < 1 || g.PlanetsMax < g.PlanetsMin || g.PlanetsMax > 20) problems.Add($"{w}: planets needs 1 <= min <= max <= 20.");
            if (g.Names.Count < g.PlanetsMax) problems.Add($"{w}: needs at least {g.PlanetsMax} names (one per planet), has {g.Names.Count}.");
            if (g.Names.Distinct().Count() != g.Names.Count || g.Names.Any(string.IsNullOrWhiteSpace)) problems.Add($"{w}: names must be distinct and not empty.");
            if (g.OrbitMin <= zero || g.OrbitMax <= g.OrbitMin) problems.Add($"{w}: orbit needs 0 < min < max.");
            if (g.SlotJitter < zero || g.SlotJitter > Fix64.One) problems.Add($"{w}: slot_jitter must be within 0..1.");
            if (g.Zones.Count == 0) problems.Add($"{w}: needs zones.");
            var last = g.OrbitMin;
            foreach (var z in g.Zones)
            {
                var where = $"{w} zone {z.Id}";
                if (z.MinDistance != last) problems.Add($"{where}: must start where the previous zone (or orbit.min) ends ({last}).");
                if (z.MaxDistance <= z.MinDistance) problems.Add($"{where}: distance needs min < max.");
                if (z.MinAu <= zero || z.MaxAu < z.MinAu || z.MaxAu > Fix64.FromInt(100)) problems.Add($"{where}: au needs 0 < min <= max <= 100.");
                Weights(z.Weights, where, true);
                last = z.MaxDistance;
            }
            if (g.Zones.Count > 0 && last != g.OrbitMax) problems.Add($"{w}: the last zone must end at orbit.max ({g.OrbitMax}).");
            if (g.Zone(g.HabitableZone) == null) problems.Add($"{w}: habitable_zone '{g.HabitableZone}' is not a zone.");
            foreach (var b in g.Belts) CheckClass(b, $"{w} belts");
            foreach (var mc in g.MoonCounts)
            {
                CheckClass(mc.Class, $"{w} moons counts");
                if (mc.Min < 0 || mc.Max < mc.Min || mc.Max > g.MoonSuffixes.Count) problems.Add($"{w} moons counts {mc.Class}: needs 0 <= min <= max <= {g.MoonSuffixes.Count} (the suffixes).");
            }
            if (g.MoonWeights.All(m => m.Parent != SystemGeneratorRules.Default)) problems.Add($"{w} moons weights: needs a default.");
            foreach (var (parent, weights) in g.MoonWeights)
            {
                if (parent != SystemGeneratorRules.Default) CheckClass(parent, $"{w} moons weights");
                Weights(weights, $"{w} moons weights {parent}", true);
            }
            if (g.MoonDistanceMin <= zero || g.MoonDistanceMax < g.MoonDistanceMin) problems.Add($"{w} moons: distance needs 0 < min <= max.");
            if (g.Sizes.All(s => s.Class != SystemGeneratorRules.Default)) problems.Add($"{w} sizes: needs a default.");
            foreach (var s in g.Sizes)
            {
                if (s.Class != SystemGeneratorRules.Default) CheckClass(s.Class, $"{w} sizes");
                if (s.Min < 1 || s.Max < s.Min || s.Max > 7) problems.Add($"{w} sizes {s.Class}: needs 1 <= min <= max <= 7.");
            }
            if (!IsLandableClass(g.LandableFallback)) problems.Add($"{w}: landable_fallback '{g.LandableFallback}' has no surface (surfaces.json planets).");
            if (g.YearSecondsAt1Au < 60) problems.Add($"{w} climate: year_seconds_at_1_au must be at least 60.");
            if (g.MoonDayMin < 30 || g.MoonDayMax < g.MoonDayMin) problems.Add($"{w} climate: moon_day_seconds needs 30 <= min <= max.");
            if (g.Climate.All(c => c.Class != SystemGeneratorRules.Default)) problems.Add($"{w} climate classes: needs a default.");
            foreach (var c in g.Climate)
            {
                var where = $"{w} climate {c.Class}";
                if (c.Class != SystemGeneratorRules.Default) CheckClass(c.Class, where);
                if (c.DayMin < 30 || c.DayMax < c.DayMin) problems.Add($"{where}: day_seconds needs 30 <= min <= max.");
                if (c.TiltMin < zero || c.TiltMax < c.TiltMin || c.TiltMax > Fix64.FromInt(90)) problems.Add($"{where}: axial_tilt_deg needs 0 <= min <= max <= 90.");
                if (c.EccentricityMin < zero || c.EccentricityMax < c.EccentricityMin || c.EccentricityMax >= Fix64.Parse("0.9")) problems.Add($"{where}: eccentricity needs 0 <= min <= max < 0.9.");
                if (c.Albedo < zero || c.Albedo >= Fix64.One) problems.Add($"{where}: albedo must be within [0, 1).");
                if (c.GreenhouseC < Fix64.FromInt(-100) || c.GreenhouseC > Fix64.FromInt(800)) problems.Add($"{where}: greenhouse_c must be within -100..800.");
                if (c.Prograde < zero || c.Prograde > Fix64.One) problems.Add($"{where}: prograde must be within 0..1.");
            }
            if (!g.Zones.Any(z => z.Weights.Any(x => x.Weight > 0 && IsLandableClass(x.Id))) && !IsLandableClass(g.LandableFallback))
                problems.Add($"{w}: no zone can roll a landable class.");
        }

        if (SystemRules is not { } r)
        {
            problems.Add("maps: no system rules (maps/system.json).");
            return;
        }
        const string sw = "System rules";
        if (r.TravelMinSeconds < 1 || r.TravelMaxSeconds < r.TravelMinSeconds) problems.Add($"{sw}: travel seconds needs 1 <= min <= max.");
        if (r.TravelNear < zero || r.TravelFar <= r.TravelNear) problems.Add($"{sw}: travel distance needs 0 <= near < far.");
        if (r.NeutralScale <= zero || r.NeutralScale > Fix64.FromInt(10)) problems.Add($"{sw}: neutral_scale must be in (0, 10].");
        if (r.ScanRange <= zero) problems.Add($"{sw}: scan range must be positive.");
        if (r.ScanCooldownSeconds < 0) problems.Add($"{sw}: scan cooldown_seconds can't be negative.");
        if (r.SatelliteMax < 1) problems.Add($"{sw}: satellite max must be at least 1.");
        if (r.SatelliteCost.Any(a => a.Amount < zero)) problems.Add($"{sw}: satellite cost can't be negative.");
        if (r.TrafficMin < 0 || r.TrafficMax < r.TrafficMin || r.TrafficMax > 32) problems.Add($"{sw}: traffic ships needs 0 <= min <= max <= 32.");
        foreach (var f in r.TrafficFactions)
            if (!_factions.ContainsKey(f)) problems.Add($"{sw}: traffic faction '{f}' is unknown.");
        foreach (var (role, cargo) in r.Cargo)
        {
            if (!ShipRoles.ContainsKey(role)) problems.Add($"{sw}: traffic cargo role '{role}' is not a ships.json role.");
            if (cargo.Count == 0 || cargo.Any(string.IsNullOrWhiteSpace)) problems.Add($"{sw}: traffic cargo for {role} needs items.");
        }
        if (r.TrafficMax > 0 && TrafficClasses().Count == 0) problems.Add($"{sw}: no civilian class of the traffic factions has a role with cargo.");
        if (r.DwellMinSeconds < 0 || r.DwellMaxSeconds < r.DwellMinSeconds) problems.Add($"{sw}: dwell_seconds needs 0 <= min <= max.");
        if (r.ShipNames.Count < r.TrafficMax || r.ShipNames.Distinct().Count() != r.ShipNames.Count)
            problems.Add($"{sw}: needs at least {r.TrafficMax} distinct traffic names.");
        foreach (var (f, _) in r.Registry)
            if (!_factions.ContainsKey(f)) problems.Add($"{sw}: registry faction '{f}' is unknown.");
        if (r.RegistryMin < 0 || r.RegistryMax < r.RegistryMin) problems.Add($"{sw}: registry_numbers needs 0 <= min <= max.");
    }
}
