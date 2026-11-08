using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Data;

/// <summary>A gravity band (surfaces.json gravity.bands): names a world; BelowG = upper edge (null = no upper edge).</summary>
public sealed record GravityBand(string Id, string Name, Fix64? BelowG);

/// <summary>
/// One gravity effect (surfaces.json gravity.effects): points (g, multiplier) sorted by g, interpolated linearly and
/// clamped at the ends.
/// </summary>
public sealed record GravityCurve(string Id, string Applies, IReadOnlyList<(Fix64 G, Fix64 Value)> Points)
{
    /// <summary>The curve's multiplier at this gravity (linear between points, flat beyond the ends).</summary>
    public Fix64 At(Fix64 g)
    {
        if (Points.Count == 0) return Fix64.One;
        if (g <= Points[0].G) return Points[0].Value;
        for (var i = 1; i < Points.Count; i++)
        {
            var (g1, v1) = Points[i];
            if (g > g1) continue;
            var (g0, v0) = Points[i - 1];
            if (g == g1 || g1 == g0) return v1;
            return v0 + (v1 - v0) * (g - g0) / (g1 - g0);
        }
        return Points[^1].Value;
    }
}

/// <summary>
/// Gravity rules (surfaces.json "gravity", the part file surfaces/worlds.json): bands, effect curves, which class's gravity
/// counts as x1 (NormaliseTo), which resources the build-cost curve scales, the hill rule above 1 g, and the units that
/// walk (their speed follows crew_work instead of drive_speed).
/// </summary>
public sealed record GravityRules(string? NormaliseTo, IReadOnlyList<GravityBand> Bands, IReadOnlyList<GravityCurve> Effects,
    IReadOnlyList<string> BuildCostResources, IReadOnlyList<string> HillTerrain, Fix64 HillCostPerG, IReadOnlyList<string> OnFoot)
{
    /// <summary>The effect ids the sim reads (GravityProfile).</summary>
    public static readonly IReadOnlyList<string> EffectIds = new[]
        { "drive_speed", "rover_wear", "descent_time", "lift_cost", "pod_speed", "build_time", "build_cost", "extraction", "crew_work" };

    public GravityCurve? Effect(string id) => Effects.FirstOrDefault(e => e.Id == id);
}

/// <summary>One tier's range in a habitability check: a value within [Min, Max] needs no more than Tier.</summary>
public sealed record TierRange(HabitabilityTier Tier, Fix64 Min, Fix64 Max);

/// <summary>A habitability check (surfaces.json habitability.checks): its tier ranges, cheapest tier first.</summary>
public sealed record HabitabilityCheck(string Id, IReadOnlyList<TierRange> Ranges);

/// <summary>
/// The domes kit on Domes-tier (and Tech-tier) worlds (habitability.domes): a core's containment field radius, the
/// buildings added to the build menu there, life support each crewed building needs, and the failure rule (after
/// GraceSeconds at no life support, CrewLossPerMinute crew a minute per crewed building).
/// </summary>
public sealed record DomeRules(int CoreDomeRadius, IReadOnlyList<string> BuildMenu, Fix64 LifeSupportPerCrewed, int GraceSeconds, Fix64 CrewLossPerMinute);

/// <summary>Habitability rules (surfaces.json "habitability"): tier names, the checks, the domes kit.</summary>
public sealed record HabitabilityRules(IReadOnlyList<(string Id, string Name)> Tiers, IReadOnlyList<HabitabilityCheck> Checks, DomeRules? Domes)
{
    /// <summary>The check ids the sim knows (Habitability.cs).</summary>
    public static readonly IReadOnlyList<string> CheckIds = new[] { "gravity_g", "pressure_kpa", "o2_pct", "gas_limit_x", "temperature_c", "radiation_usv_h" };

    public HabitabilityCheck? Check(string id) => Checks.FirstOrDefault(c => c.Id == id);

    /// <summary>A tier's display name ("Domes").</summary>
    public string NameOf(HabitabilityTier tier)
    {
        var id = TierId(tier);
        foreach (var (i, n) in Tiers)
            if (i == id) return n;
        return tier.ToString();
    }

    public static string TierId(HabitabilityTier tier) => tier.ToString().ToLowerInvariant();
}

/// <summary>What a building needs and gives on a hostile world (buildings.json crewed, dome_radius, life_support).</summary>
public sealed record BuildingWorldStats(bool Crewed, bool CrewedStated, int DomeRadius, Fix64 LifeSupport);

// Gravity and hostile worlds: surfaces.json "gravity" and "habitability" (both in data/surfaces/worlds.json) and the
// buildings' crewed / dome_radius / life_support fields. Read here, used by GravityProfile.cs and Habitability.cs.
public sealed partial class GameData
{
    public GravityRules? Gravity { get; private init; }

    public HabitabilityRules? Habitability { get; private init; }

    private IReadOnlyDictionary<string, BuildingWorldStats> BuildingWorlds { get; init; } = new Dictionary<string, BuildingWorldStats>();

    private static readonly BuildingWorldStats NoWorldStats = new(false, false, 0, Fix64.Zero);

    /// <summary>A building's crew / dome / life-support stats (all off when the data says nothing).</summary>
    public BuildingWorldStats WorldStatsOf(string buildingId) => BuildingWorlds.TryGetValue(buildingId, out var w) ? w : NoWorldStats;

    /// <summary>Crewed buildings need cover and life support on a Domes world (buildings.json crewed).</summary>
    public bool IsCrewed(string buildingId) => WorldStatsOf(buildingId).Crewed;

    private static GravityRules? ReadGravity(JsonElement surfaces, List<string> problems)
    {
        if (!surfaces.TryGetProperty("gravity", out var g)) return null;
        var bands = g.GetProperty("bands").EnumerateArray().Select(b =>
        {
            var below = b.GetProperty("below_g");
            return new GravityBand(Str(b, "id"), Str(b, "name"), below.ValueKind == JsonValueKind.Null ? null : Num(below));
        }).ToList();
        var effects = g.GetProperty("effects").EnumerateArray().Select(e =>
        {
            var points = new List<(Fix64, Fix64)>();
            foreach (var p in e.GetProperty("points").EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2) points.Add((Num(p[0]), Num(p[1])));
                else problems.Add($"Gravity effect {Str(e, "id")}: every point must be [g, multiplier].");
            }
            return new GravityCurve(Str(e, "id"), e.TryGetProperty("applies", out var a) ? a.GetString() ?? "" : "", points);
        }).ToList();
        List<string> Ids(JsonElement e, string p) => e.TryGetProperty(p, out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
        var hills = g.TryGetProperty("hills", out var h) ? h : default;
        var normalise = g.TryGetProperty("normalise_to", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new GravityRules(normalise, bands, effects, Ids(g, "build_cost_resources"),
            hills.ValueKind == JsonValueKind.Object ? Ids(hills, "terrain") : new List<string>(),
            hills.ValueKind == JsonValueKind.Object && hills.TryGetProperty("cost_per_g_above_1", out var c) ? Num(c) : Fix64.Zero,
            Ids(g, "on_foot"));
    }

    private static HabitabilityTier? ParseTier(string id)
    {
        foreach (var t in System.Enum.GetValues<HabitabilityTier>())
            if (HabitabilityRules.TierId(t) == id) return t;
        return null;
    }

    private static HabitabilityRules? ReadHabitability(JsonElement surfaces, List<string> problems)
    {
        if (!surfaces.TryGetProperty("habitability", out var h)) return null;
        var tiers = h.GetProperty("tiers").EnumerateArray().Select(t => (Str(t, "id"), Str(t, "name"))).ToList();
        var checks = new List<HabitabilityCheck>();
        foreach (var c in h.GetProperty("checks").EnumerateObject())
        {
            var ranges = new List<TierRange>();
            foreach (var r in c.Value.EnumerateObject())
            {
                if (ParseTier(r.Name) is not { } tier)
                {
                    problems.Add($"Habitability check {c.Name}: unknown tier '{r.Name}'.");
                    continue;
                }
                if (r.Value.ValueKind != JsonValueKind.Array || r.Value.GetArrayLength() != 2)
                {
                    problems.Add($"Habitability check {c.Name}: {r.Name} must be [min, max].");
                    continue;
                }
                ranges.Add(new TierRange(tier, Num(r.Value[0]), Num(r.Value[1])));
            }
            ranges.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            checks.Add(new HabitabilityCheck(c.Name, ranges));
        }
        DomeRules? domes = null;
        if (h.TryGetProperty("domes", out var d))
            domes = new DomeRules(d.GetProperty("core_dome_radius").GetInt32(),
                d.GetProperty("build_menu").EnumerateArray().Select(x => x.GetString()!).ToList(),
                Num(d.GetProperty("life_support_per_crewed")), d.GetProperty("grace_seconds").GetInt32(), Num(d.GetProperty("crew_loss_per_min")));
        return new HabitabilityRules(tiers, checks, domes);
    }

    private static Dictionary<string, BuildingWorldStats> ReadBuildingWorlds(JsonElement buildings)
    {
        var result = new Dictionary<string, BuildingWorldStats>(System.StringComparer.Ordinal);
        foreach (var b in buildings.GetProperty("buildings").EnumerateArray())
        {
            var stated = b.TryGetProperty("crewed", out var c);
            var dome = b.TryGetProperty("dome_radius", out var r) ? r.GetInt32() : 0;
            var ls = b.TryGetProperty("life_support", out var l) ? Num(l) : Fix64.Zero;
            if (!stated && dome == 0 && ls == Fix64.Zero) continue;
            result.TryAdd(Str(b, "id"), new BuildingWorldStats(stated && c.ValueKind == JsonValueKind.True, stated, dome, ls));
        }
        return result;
    }

    /// <summary>
    /// Gravity and habitability checks: every effect the sim reads has a curve, points sorted by g with positive
    /// multipliers, bands ascending, the class to normalise to has an environment; every tier named once, check ids known,
    /// ranges valid and nested; the domes kit's buildings exist with build data; every build-menu, domes-menu and core
    /// building says whether it is crewed; the drop ship's fuel isn't negative; a body's own gravity is in (0, 10].
    /// </summary>
    private void ValidateWorlds(List<string> problems)
    {
        if (Gravity is { } g)
        {
            foreach (var id in GravityRules.EffectIds)
                if (g.Effect(id) == null) problems.Add($"Gravity: no curve for the effect '{id}'.");
            foreach (var e in g.Effects)
            {
                if (!GravityRules.EffectIds.Contains(e.Id)) problems.Add($"Gravity: unknown effect '{e.Id}'.");
                if (g.Effects.Count(x => x.Id == e.Id) > 1) problems.Add($"Gravity: effect '{e.Id}' is listed twice.");
                if (e.Points.Count == 0) problems.Add($"Gravity effect {e.Id}: needs at least one point.");
                for (var i = 0; i < e.Points.Count; i++)
                {
                    if (e.Points[i].Value <= Fix64.Zero) problems.Add($"Gravity effect {e.Id}: multipliers must be positive.");
                    if (e.Points[i].G < Fix64.Zero) problems.Add($"Gravity effect {e.Id}: g can't be negative.");
                    if (i > 0 && e.Points[i].G <= e.Points[i - 1].G) problems.Add($"Gravity effect {e.Id}: points must be sorted by g, each g once.");
                }
            }
            for (var i = 0; i < g.Bands.Count; i++)
            {
                var b = g.Bands[i];
                var last = i == g.Bands.Count - 1;
                if (last != (b.BelowG == null)) problems.Add("Gravity bands: only the last band has below_g null.");
                if (i > 0 && b.BelowG is { } hi && g.Bands[i - 1].BelowG is { } lo && hi <= lo) problems.Add("Gravity bands: below_g must rise band by band.");
            }
            if (g.NormaliseTo != null && EnvironmentOf(g.NormaliseTo) == null) problems.Add($"Gravity: normalise_to '{g.NormaliseTo}' has no environment.");
            foreach (var r in g.BuildCostResources)
                if (!_resources.ContainsKey(r)) problems.Add($"Gravity: build_cost_resources has unknown resource '{r}'.");
            foreach (var t in g.HillTerrain)
                if (!_terrain.ContainsKey(t)) problems.Add($"Gravity: hills lists unknown terrain '{t}'.");
            if (g.HillCostPerG < Fix64.Zero) problems.Add("Gravity: hills cost_per_g_above_1 can't be negative.");
            foreach (var u in g.OnFoot)
                if (!_surfaceUnits.ContainsKey(u)) problems.Add($"Gravity: on_foot lists unknown unit '{u}'.");
        }

        if (Habitability is { } h)
        {
            foreach (var t in System.Enum.GetValues<HabitabilityTier>())
                if (h.Tiers.Count(x => x.Id == HabitabilityRules.TierId(t)) != 1) problems.Add($"Habitability: tier '{HabitabilityRules.TierId(t)}' must be listed once.");
            foreach (var (id, _) in h.Tiers)
                if (ParseTier(id) == null) problems.Add($"Habitability: unknown tier '{id}'.");
            foreach (var id in HabitabilityRules.CheckIds)
                if (h.Check(id) == null) problems.Add($"Habitability: no '{id}' check.");
            foreach (var c in h.Checks)
            {
                if (!HabitabilityRules.CheckIds.Contains(c.Id)) problems.Add($"Habitability: unknown check '{c.Id}'.");
                for (var i = 0; i < c.Ranges.Count; i++)
                {
                    var r = c.Ranges[i];
                    if (r.Tier == HabitabilityTier.Impossible) problems.Add($"Habitability check {c.Id}: impossible has no range (it is what lies outside them).");
                    if (r.Min > r.Max) problems.Add($"Habitability check {c.Id}: {HabitabilityRules.TierId(r.Tier)} has min above max.");
                    if (i > 0 && c.Ranges[i - 1] is var p && (r.Min > p.Min || r.Max < p.Max))
                        problems.Add($"Habitability check {c.Id}: {HabitabilityRules.TierId(r.Tier)} must hold {HabitabilityRules.TierId(p.Tier)}'s range.");
                    if (i > 0 && c.Ranges[i - 1].Tier == r.Tier) problems.Add($"Habitability check {c.Id}: {HabitabilityRules.TierId(r.Tier)} twice.");
                }
            }
            if (h.Domes is { } d)
            {
                if (d.CoreDomeRadius < 0 || d.GraceSeconds < 0 || d.LifeSupportPerCrewed < Fix64.Zero || d.CrewLossPerMinute < Fix64.Zero)
                    problems.Add("Habitability domes: radius, grace, life support and crew loss can't be negative.");
                foreach (var id in d.BuildMenu)
                {
                    if (!_buildings.TryGetValue(id, out var b))
                    {
                        problems.Add($"Habitability domes: build_menu has unknown building '{id}'.");
                        continue;
                    }
                    if (BuildMenu.Contains(id)) problems.Add($"Habitability domes: {id} is already in the build menu.");
                    if (b.BuildSeconds <= 0) problems.Add($"Habitability domes: {id} needs build_seconds.");
                    if (b.Layer != "surface") problems.Add($"Habitability domes: {id} must be a surface building.");
                    if (b.RequiresTech.Length > 0 && !_techs.ContainsKey(b.RequiresTech))
                        problems.Add($"Habitability domes: {id} requires tech '{b.RequiresTech}', which isn't in surfaces.json research.");
                }
            }
        }

        var mustSay = BuildMenu.Concat(Habitability?.Domes?.BuildMenu ?? new List<string>()).Concat(Buildings.Where(b => b.IsCore && b.Layer == "surface").Select(b => b.Id)).Distinct();
        foreach (var id in mustSay)
            if (_buildings.ContainsKey(id) && !WorldStatsOf(id).CrewedStated) problems.Add($"Building {id}: say whether it is crewed (crewed: true/false).");
        foreach (var (id, w) in BuildingWorlds.OrderBy(p => p.Key, System.StringComparer.Ordinal))
            if (w.DomeRadius < 0 || w.LifeSupport < Fix64.Zero) problems.Add($"Building {id}: dome_radius and life_support can't be negative.");

        if (Orbit is { } o)
            foreach (var f in o.ShuttleFuel)
                if (f.Amount < Fix64.Zero) problems.Add("Orbit: the drop ship's fuel can't be negative.");
        if (Climate is { } climate)
            foreach (var b in climate.Bodies)
                if (b.GravityG is { } bg && (bg <= Fix64.Zero || bg > Fix64.FromInt(10))) problems.Add($"Climate body {b.Id}: gravity_g must be in (0, 10].");
    }
}
