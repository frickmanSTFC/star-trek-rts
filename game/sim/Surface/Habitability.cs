using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Hostile worlds (2026-11-08, contract docs/hostile-worlds-contract.md): the surface's gravity and its effects
// (GravityProfile), its habitability tier, and on Domes worlds the domes kit: crewed buildings only under cover (a core's
// containment field or a Pressure Dome), life support per base (supply ÷ use, like power), crewed buildings slowing
// below 1 and the base losing crew after grace_seconds at 0. Gravity, tier, cover and the build menu are pure functions
// of the data, the map and the structures; the only new state is the failure clock on each core (hashed on Domes worlds).

/// <summary>A world's protection tier, cheapest first (gravity doc "Life and habitability limits").</summary>
public enum HabitabilityTier { Open, Masks, Suits, Domes, Tech, Impossible }

/// <summary>One habitability check's outcome: the check id ("o2_pct"), the reading(s) it judged (temperature: night low
/// and day high), and the tier it needs.</summary>
public sealed record HabitabilityReading(string Check, Fix64 Value, Fix64? Value2, HabitabilityTier Tier);

/// <summary>A world's habitability: the worst tier of its checks, and each check (for the landing scan's tier strip).</summary>
public sealed record HabitabilityAssessment(HabitabilityTier Tier, IReadOnlyList<HabitabilityReading> Checks);

/// <summary>Life support at a base: what its working cores and Life Support Plants supply, what its crewed buildings use.</summary>
public readonly record struct BaseLifeSupport(Fix64 Supply, Fix64 Use)
{
    /// <summary>1 when supply covers use (or nothing uses any); otherwise supply / use (crewed buildings work at this speed).</summary>
    public Fix64 Level => Use <= Supply || Use == Fix64.Zero ? Fix64.One : Supply / Use;
}

/// <summary>The habitability tier of a planet class (and gravity), from surfaces.json habitability + telemetry limits.</summary>
public static class WorldHabitability
{
    private static readonly Fix64 SeaLevelKpa = Fix64.Parse("101.325");
    private static readonly Fix64 PpmPerPercent = Fix64.FromInt(10000);

    /// <summary>The tier of a planet class's environment at this gravity (null = the environment's own gravity). Open when
    /// the data has no habitability rules or no environment for the class.</summary>
    public static HabitabilityAssessment Assess(GameData data, string planetClassId, Fix64? gravity = null)
    {
        var env = data.EnvironmentOf(planetClassId);
        var rules = data.Habitability;
        if (env == null || rules == null) return new HabitabilityAssessment(HabitabilityTier.Open, new List<HabitabilityReading>());
        var t = data.Telemetry;
        var g = gravity ?? env.GravityG;
        var list = new List<HabitabilityReading>();

        void Add(string id, Fix64 value, Fix64? value2, (Fix64 Min, Fix64 Max)? open)
        {
            var check = rules.Check(id);
            var ranges = new List<TierRange>();
            if (open is { } o && (check == null || check.Ranges.All(r => r.Tier != HabitabilityTier.Open))) ranges.Add(new TierRange(HabitabilityTier.Open, o.Min, o.Max));
            if (check != null) ranges.AddRange(check.Ranges);
            var tier = TierOf(ranges, value);
            if (value2 is { } v2 && TierOf(ranges, v2) > tier) tier = TierOf(ranges, v2);
            list.Add(new HabitabilityReading(id, value, value2, ranges.Count == 0 ? HabitabilityTier.Open : tier));
        }

        Add("gravity_g", g, null, null);
        Add("pressure_kpa", env.PressureKpa, null, t == null ? null : (t.BreathableKpaMin, t.BreathableKpaMax));
        Add("o2_pct", env.Percent("O2"), null, t == null ? null : (t.BreathableO2Min, t.BreathableO2Max));
        Add("gas_limit_x", GasLimitTimes(env, t), null, (Fix64.Zero, Fix64.One));
        Add("temperature_c", env.NightC, env.DayC, t == null ? null : (t.ColdWarnC, t.HeatWarnC));
        Add("radiation_usv_h", env.RadiationDay, null, t == null ? null : (Fix64.Zero, t.RadiationWarn));
        var worst = list.Count == 0 ? HabitabilityTier.Open : list.Max(r => r.Tier);
        return new HabitabilityAssessment(worst, list);
    }

    /// <summary>The cheapest tier whose range holds the value (ranges cheapest first); Impossible when none does.</summary>
    private static HabitabilityTier TierOf(IReadOnlyList<TierRange> ranges, Fix64 value)
    {
        if (ranges.Count == 0) return HabitabilityTier.Open;
        foreach (var r in ranges)
            if (value >= r.Min && value <= r.Max) return r.Tier;
        return HabitabilityTier.Impossible;
    }

    /// <summary>The worst main or trace gas against its exposure limit, at sea-level-equivalent ppm (ppm × pressure / 101.325
    /// kPa, as the telemetry compares them); 0 for a vacuum or without telemetry limits.</summary>
    public static Fix64 GasLimitTimes(EnvironmentDef env, TelemetryRules? t)
    {
        if (t == null || !env.HasAtmosphere) return Fix64.Zero;
        var toPressure = env.PressureKpa / SeaLevelKpa;
        var worst = Fix64.Zero;
        void Judge(string gas, Fix64 ppm)
        {
            if (t.LimitPpm(gas) is not { } limit || limit <= Fix64.Zero) return;
            var x = ppm * toPressure / limit;
            if (x > worst) worst = x;
        }
        foreach (var g in env.Atmosphere) Judge(g.Gas, g.Amount * PpmPerPercent);
        foreach (var g in env.Trace) Judge(g.Gas, g.Amount);
        return worst;
    }
}

public sealed partial class PlanetSurface
{
    /// <summary>The data the cached gravity, tier and build menu were worked out from (redone when the test panel swaps it).</summary>
    private GameData? _worldFor;

    private GravityProfile? _gravity;
    private HabitabilityAssessment? _habitability;
    private IReadOnlyList<string>? _buildMenu;

    private void RefreshWorld()
    {
        if (ReferenceEquals(_worldFor, _data)) return;
        _worldFor = _data;
        var g = GravityOf(_data, Map);
        _gravity = GravityProfile.For(_data, g);
        _habitability = WorldHabitability.Assess(_data, Map.Def.ClassId, g);
        var domes = _habitability.Tier is HabitabilityTier.Domes or HabitabilityTier.Tech ? _data.Habitability?.Domes : null;
        _buildMenu = domes == null ? _data.BuildMenu : _data.BuildMenu.Concat(domes.BuildMenu.Where(id => !_data.BuildMenu.Contains(id))).ToList();
    }

    /// <summary>A surface's gravity: its climate body's own gravity_g, else its class environment's, else 1 g.</summary>
    public static Fix64 GravityOf(GameData data, SurfaceMap map)
    {
        if (map.ClimateBodyId is { } body && (map.SystemClimate?.Body(body) ?? data.Climate?.Body(body)) is { GravityG: { } own }) return own;
        return data.EnvironmentOf(map.Def.ClassId)?.GravityG ?? Fix64.One;
    }

    /// <summary>This surface's gravity in g (body override, else its class environment).</summary>
    public Fix64 GravityG => Gravity.G;

    /// <summary>This surface's gravity multipliers (all exactly 1 on class M).</summary>
    public GravityProfile Gravity
    {
        get
        {
            RefreshWorld();
            return _gravity!;
        }
    }

    /// <summary>This surface's habitability: the tier and each check behind it (for the landing scan's tier strip).</summary>
    public HabitabilityAssessment Habitability
    {
        get
        {
            RefreshWorld();
            return _habitability!;
        }
    }

    /// <summary>This surface's protection tier (worst of gravity, air, temperature, radiation). Fixed per game.</summary>
    public HabitabilityTier Tier => Habitability.Tier;

    /// <summary>The domes kit applies (a Domes-tier or Tech-tier world): crewed buildings need cover and life support.</summary>
    public bool NeedsDomes => Tier is HabitabilityTier.Domes or HabitabilityTier.Tech;

    private DomeRules? Domes => NeedsDomes ? _data.Habitability?.Domes : null;

    /// <summary>What can be built on this surface: surfaces.json build_menu, plus the domes kit (Life Support Plant,
    /// Pressure Dome) on Domes worlds. The UI's build drawer should list these (on class M: exactly build_menu).</summary>
    public IReadOnlyList<string> BuildMenu
    {
        get
        {
            RefreshWorld();
            return _buildMenu!;
        }
    }

    /// <summary>What the player is told when a crewed building would stand outside every dome and containment field.</summary>
    public const string NeedsDomeProblem = "crewed buildings need cover here: inside a finished Pressure Dome or a colony core's containment field";

    /// <summary>True when the tile lies under a dome or containment field of the player's (any finished core's
    /// field, or a finished Pressure Dome's), so crewed buildings may stand there on a Domes world.</summary>
    public bool Covered(int player, int tile)
    {
        if (!NeedsDomes) return true;
        if (!OnMap(tile)) return false;
        foreach (var s in _structures)
        {
            if (s.Owner != player) continue;
            var r = DomeRadiusOf(s);
            if (r > 0 && Map.Grid.Distance(s.Tile, tile) <= r) return true;
        }
        return false;
    }

    /// <summary>How many tiles round its own tile a structure's dome / containment field reaches (0 = none).
    /// Cores: 1 on a Domes world; pressure_dome: 2. Only finished structures cover.</summary>
    public int DomeRadiusOf(SurfaceStructure s)
    {
        if (s.State != StructureState.Complete || Domes is not { } domes) return 0;
        var own = _data.WorldStatsOf(s.DefId).DomeRadius;
        if (own > 0) return own;
        return _data.Building(s.DefId).IsCore ? domes.CoreDomeRadius : 0;
    }

    /// <summary>Life support at a base now (from its buildings' state after the last tick): supply = life_support of its
    /// working cores and plants × condition × the base's power share for those that use power; use = life_support_per_crewed
    /// for each finished crewed building that isn't paused. Zero use and supply on worlds without the domes kit.</summary>
    public BaseLifeSupport LifeSupportBudget(int baseId)
    {
        if (Domes is not { } domes) return new BaseLifeSupport(Fix64.Zero, Fix64.Zero);
        var supply = Fix64.Zero;
        var use = Fix64.Zero;
        Fix64? power = null;
        foreach (var s in _structures)
        {
            if (s.BaseId != baseId || s.State != StructureState.Complete) continue;
            var w = _data.WorldStatsOf(s.DefId);
            if (w.Crewed && !s.Paused) use += domes.LifeSupportPerCrewed;
            if (w.LifeSupport <= Fix64.Zero || !DrawsPower(s.Status)) continue;
            var share = Fix64.One;
            if (_data.Building(s.DefId).PowerUse > Fix64.Zero) share = power ??= PowerOf(baseId).Efficiency;
            supply += w.LifeSupport * ConditionFactor(s) * share;
        }
        return new BaseLifeSupport(supply, use);
    }

    /// <summary>Life support of a base on a Domes world: 1 = fully supplied, 0 = no air (crew loss). 1 elsewhere.</summary>
    public Fix64 LifeSupportOf(int baseId) => Domes == null ? Fix64.One : LifeSupportBudget(baseId).Level;

    /// <summary>Power a crewed building draws on top of its own on a Domes world (life support's fans and heaters:
    /// habitability.domes crewed_extra_power; not cores). 0 elsewhere.</summary>
    public Fix64 CrewedExtraPower(SurfaceStructure s) =>
        Domes is { } d && _data.IsCrewed(s.DefId) && !_data.Building(s.DefId).IsCore ? d.CrewedExtraPower : Fix64.Zero;

    /// <summary>How fast a building works for want of air: its base's life support if it is crewed on a Domes world, else 1.</summary>
    private Fix64 CrewShare(SurfaceStructure s, Dictionary<int, Fix64>? cache)
    {
        if (Domes == null || !_data.IsCrewed(s.DefId)) return Fix64.One;
        if (cache == null) return LifeSupportOf(s.BaseId);
        if (!cache.TryGetValue(s.BaseId, out var level)) cache[s.BaseId] = level = LifeSupportOf(s.BaseId);
        return level;
    }

    /// <summary>Seconds (rounded up) until a base with no life support starts losing crew; 0 when it already is, or has air.</summary>
    public int AirlessSecondsLeft(SurfaceStructure core)
    {
        if (Domes is not { } domes || core.AirlessTicks == 0 || core.LifeSupportFailing) return 0;
        var left = (long)domes.GraceSeconds * Simulation.TickRate - core.AirlessTicks;
        return (int)System.Math.Max(0, (left + Simulation.TickRate - 1) / Simulation.TickRate);
    }

    /// <summary>
    /// Domes worlds, each tick after the economy: a base whose life support is 0 counts up; after grace_seconds it is failing
    /// (<see cref="SurfaceStructure.LifeSupportFailing"/>) and loses crew_loss_per_min crew a minute for each crewed building,
    /// from the owner's stock, until supply comes back (then the clock and the flag reset).
    /// </summary>
    private void LifeSupport(Simulation sim)
    {
        if (Domes is not { } domes) return;
        var crew = -1;
        foreach (var r in _data.Resources)
            if (r.Id == "crew") crew = r.Index;
        var grace = (long)domes.GraceSeconds * Simulation.TickRate;
        foreach (var core in _structures)
        {
            if (core.Id != core.BaseId || core.State != StructureState.Complete || !_data.Building(core.DefId).IsCore) continue;
            var budget = LifeSupportBudget(core.Id);
            if (budget.Use <= Fix64.Zero || budget.Supply > Fix64.Zero)
            {
                core.AirlessTicks = 0;
                core.LifeSupportFailing = false;
                continue;
            }
            if (core.AirlessTicks < int.MaxValue) core.AirlessTicks++;
            if (core.AirlessTicks < grace) continue;
            core.LifeSupportFailing = true;
            if (crew < 0) continue;
            var crewed = budget.Use / Fix64.Max(domes.LifeSupportPerCrewed, Fix64.Ratio(1, 1000));
            var stock = sim.Players[core.Owner].Stock;
            var loss = Fix64.Min(stock[crew], domes.CrewLossPerMinute * crewed / Simulation.TicksPerMinute);
            if (loss <= Fix64.Zero) continue;
            stock.Add(crew, -loss);
            core.CrewLost += loss;
        }
    }

    /// <summary>The domes kit's state (each core's failure clock, flag and crew lost), hashed only where the kit applies, so
    /// other worlds hash exactly as before; whether it applies follows from the hashed map and the data.</summary>
    private void AddLifeSupportToHash(ref StateHasher h)
    {
        if (Domes == null) return;
        foreach (var s in _structures)
        {
            if (s.Id != s.BaseId) continue;
            h.Add(s.Id);
            h.Add(s.AirlessTicks);
            h.Add(s.LifeSupportFailing);
            h.Add(s.CrewLost);
        }
    }
}
