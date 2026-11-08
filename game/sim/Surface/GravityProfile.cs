using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// One surface's gravity and the multiplier for each effect (surfaces.json gravity, data/surfaces/worlds.json), built once
/// when the surface is first asked (again only when the test panel swaps the data). Each curve is interpolated at the
/// surface's g and divided by its value at the normalising class's g (class M), so class M gives exactly 1 everywhere and
/// plays as before; nothing here is state (a pure function of the data and the map's class and climate body).
/// </summary>
public sealed class GravityProfile
{
    /// <summary>Surface gravity in g.</summary>
    public Fix64 G { get; }

    /// <summary>The gravity band's id ("earth_like"), or "" when the data has no bands.</summary>
    public string Band { get; }

    /// <summary>The band's display name ("Earth-like").</summary>
    public string BandName { get; }

    /// <summary>× every ground vehicle's speed.</summary>
    public Fix64 DriveSpeed { get; }

    /// <summary>× rover wear and flat-tyre chances while driving.</summary>
    public Fix64 RoverWear { get; }

    /// <summary>× the first landing's descent, outpost drops and drop-ship flights.</summary>
    public Fix64 DescentTime { get; }

    /// <summary>× the drop ship's fuel a trip.</summary>
    public Fix64 LiftCost { get; }

    /// <summary>× cargo pod speed.</summary>
    public Fix64 PodSpeed { get; }

    /// <summary>× construction, level and core-upgrade times.</summary>
    public Fix64 BuildTime { get; }

    /// <summary>× the metal (build_cost_resources) in building, level and upgrade costs.</summary>
    public Fix64 BuildCost { get; }

    /// <summary>× mine, deep-core and crawler extraction.</summary>
    public Fix64 Extraction { get; }

    /// <summary>× crews on foot: survey teams' speed, the maintenance crew's walk and repair rate, the pad's repair rate.</summary>
    public Fix64 CrewWork { get; }

    /// <summary>× the move cost of hills terrain (1 at or below 1 g).</summary>
    public Fix64 HillCost { get; }

    /// <summary>True when every multiplier is exactly 1 (class M).</summary>
    public bool Neutral { get; }

    private readonly bool[] _costResource;
    private readonly bool[] _hillTerrain;
    private readonly IReadOnlyList<string> _onFoot;

    /// <summary>No gravity effects at all (data without gravity rules).</summary>
    public static GravityProfile None(GameData data) => new(data, Fix64.One, null);

    private GravityProfile(GameData data, Fix64 g, GravityRules? rules)
    {
        G = g;
        var band = rules?.Bands.FirstOrDefault(b => b.BelowG == null || g < b.BelowG.Value);
        Band = band?.Id ?? "";
        BandName = band?.Name ?? "";
        var norm = rules?.NormaliseTo is { } cls ? data.EnvironmentOf(cls)?.GravityG : null;
        Fix64 M(string id)
        {
            var curve = rules?.Effect(id);
            if (curve == null) return Fix64.One;
            if (norm is not { } n) return curve.At(g);
            if (n == g) return Fix64.One; // exactly 1 on the normalising class, whatever the division would round to
            var at = curve.At(n);
            return at <= Fix64.Zero ? curve.At(g) : curve.At(g) / at;
        }
        DriveSpeed = M("drive_speed");
        RoverWear = M("rover_wear");
        DescentTime = M("descent_time");
        LiftCost = M("lift_cost");
        PodSpeed = M("pod_speed");
        BuildTime = M("build_time");
        BuildCost = M("build_cost");
        Extraction = M("extraction");
        CrewWork = M("crew_work");
        HillCost = rules != null && g > Fix64.One ? Fix64.One + rules.HillCostPerG * (g - Fix64.One) : Fix64.One;
        Neutral = new[] { DriveSpeed, RoverWear, DescentTime, LiftCost, PodSpeed, BuildTime, BuildCost, Extraction, CrewWork, HillCost }.All(x => x == Fix64.One);

        _costResource = data.Resources.Select(r => rules?.BuildCostResources.Contains(r.Id) ?? false).ToArray();
        _hillTerrain = data.Terrain.Select(t => rules?.HillTerrain.Contains(t.Id) ?? false).ToArray();
        _onFoot = rules?.OnFoot ?? new List<string>();
    }

    /// <summary>The profile for a surface of this gravity (the data's curves; all 1 without gravity rules).</summary>
    public static GravityProfile For(GameData data, Fix64 g) => new(data, g, data.Gravity);

    /// <summary>The multiplier of an effect by id ("drive_speed", …; 1 for an unknown id).</summary>
    public Fix64 Effect(string id) => id switch
    {
        "drive_speed" => DriveSpeed,
        "rover_wear" => RoverWear,
        "descent_time" => DescentTime,
        "lift_cost" => LiftCost,
        "pod_speed" => PodSpeed,
        "build_time" => BuildTime,
        "build_cost" => BuildCost,
        "extraction" => Extraction,
        "crew_work" => CrewWork,
        _ => Fix64.One,
    };

    /// <summary>A duration in ticks × a multiplier, rounded to the nearest tick, at least 1 (unchanged when it is 1).</summary>
    public static long Ticks(long ticks, Fix64 multiplier)
    {
        if (multiplier == Fix64.One || ticks <= 0) return ticks;
        return System.Math.Max(1, (Fix64.FromRaw(ticks << Fix64.FractionalBits) * multiplier + Fix64.Half).FloorToInt());
    }

    /// <summary>A cost with its metal scaled by <see cref="BuildCost"/> (the same list when that is 1).</summary>
    public IReadOnlyList<ResourceAmount> ScaleCost(IReadOnlyList<ResourceAmount> cost)
    {
        if (BuildCost == Fix64.One || cost.Count == 0) return cost;
        var any = false;
        foreach (var c in cost)
            if (c.Resource < _costResource.Length && _costResource[c.Resource]) any = true;
        if (!any) return cost;
        return cost.Select(c => c.Resource < _costResource.Length && _costResource[c.Resource] ? new ResourceAmount(c.Resource, c.Amount * BuildCost) : c).ToList();
    }

    /// <summary>The drop ship's fuel a trip on this surface: orbit.shuttle.fuel × <see cref="LiftCost"/>.</summary>
    public IReadOnlyList<ResourceAmount> Fuel(OrbitRules? orbit)
    {
        if (orbit == null || orbit.ShuttleFuel.Count == 0) return System.Array.Empty<ResourceAmount>();
        if (LiftCost == Fix64.One) return orbit.ShuttleFuel;
        return orbit.ShuttleFuel.Select(c => new ResourceAmount(c.Resource, c.Amount * LiftCost)).ToList();
    }

    /// <summary>Whether a terrain (by index) takes the hill rule.</summary>
    public bool IsHill(int terrainIndex) => terrainIndex >= 0 && terrainIndex < _hillTerrain.Length && _hillTerrain[terrainIndex];

    /// <summary>A unit's speed multiplier: crew_work for units on foot, drive_speed for vehicles.</summary>
    public Fix64 SpeedOf(string unitId) => _onFoot.Contains(unitId) ? CrewWork : DriveSpeed;
}
