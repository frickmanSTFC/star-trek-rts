using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// Wheel order for <see cref="SurfaceUnit.FlatTyres"/> (bit i = wheel i). Four wheels: front left, front right, rear
/// left, rear right. Six: front left, front right, middle left, middle right, rear left, rear right. The codes match
/// the rover models' wheel nodes ("WheelFL", "WheelMR", ...).
/// </summary>
public static class Wheels
{
    private static readonly string[] Four = { "FL", "FR", "RL", "RR" };
    private static readonly string[] Six = { "FL", "FR", "ML", "MR", "RL", "RR" };

    /// <summary>The wheel's code, e.g. "FL" (its model node is "Wheel" + code).</summary>
    public static string Code(int wheels, int index)
    {
        var codes = wheels == 6 ? Six : Four;
        return index >= 0 && index < codes.Length ? codes[index] : "";
    }

    /// <summary>"front left", "middle right", ...</summary>
    public static string Name(int wheels, int index)
    {
        var code = Code(wheels, index);
        if (code.Length != 2) return $"wheel {index + 1}";
        var axle = code[0] switch { 'F' => "front", 'M' => "middle", _ => "rear" };
        return $"{axle} {(code[1] == 'L' ? "left" : "right")}";
    }

    /// <summary>How many bits are set among the first <paramref name="wheels"/>.</summary>
    public static int Count(int mask, int wheels)
    {
        var n = 0;
        for (var i = 0; i < wheels; i++)
            if ((mask & (1 << i)) != 0) n++;
        return n;
    }

    /// <summary>The lowest set wheel, or -1.</summary>
    public static int First(int mask, int wheels)
    {
        for (var i = 0; i < wheels; i++)
            if ((mask & (1 << i)) != 0) return i;
        return -1;
    }
}

// Rover damage and flat tyres (units with a "damage" block in surfaces.json: the survey rovers). Driving wears
// integrity by terrain and rolls hazards and flat tyres on sim.Rng; toxic ground and predators hurt it wherever it is.
// Low integrity slows it and dims its sensors; at 0 it is disabled (Broken) and only a repair order moves it, at a crawl.
// Flats slow it more; the crew change one in the field with a spare, or a repair pad fixes everything.
public sealed partial class PlanetSurface
{
    /// <summary><see cref="SurfaceUnit.LastHazard"/> for a predator attack.</summary>
    public const string WildlifeHazard = "wildlife";

    private TyreRules? TyresOf(SurfaceUnit u) => _data.SurfaceUnit(u.DefId).Tyres;

    /// <summary>A rover at 0 integrity: stopped, sensors dark, it only drives (at a crawl) to a repair pad.</summary>
    public bool Disabled(SurfaceUnit u) => u.Broken && _data.SurfaceUnit(u.DefId).Damage != null;

    /// <summary>How many of its tyres are flat.</summary>
    public int FlatCount(SurfaceUnit u) => TyresOf(u) is { } t ? Wheels.Count(u.FlatTyres, t.Wheels) : 0;

    /// <summary>Anything a repair pad would fix: broken / disabled, integrity below 100, a flat, or spares to restock.</summary>
    public bool NeedsRepair(SurfaceUnit u) =>
        u.Broken || u.Integrity < Hundred || FlatCount(u) > 0 || (TyresOf(u) is { } t && u.Spares < t.Spares);

    /// <summary>Share of its full speed a damaged rover drives at (1 for units without damage rules).</summary>
    public Fix64 DriveShare(SurfaceUnit u)
    {
        var def = _data.SurfaceUnit(u.DefId);
        if (def.Damage is not { } d) return Fix64.One;
        var share = Fix64.One;
        if (u.Broken) share = d.Crawl;
        else if (u.Integrity < d.SlowBelow) share = d.SlowestDrive + (Fix64.One - d.SlowestDrive) * u.Integrity / d.SlowBelow;
        var flats = FlatCount(u);
        if (def.Tyres is { } t && flats > 0) share *= flats >= 2 ? t.TwoFlatSpeed : t.FlatSpeed;
        return Fix64.Max(d.Crawl, share);
    }

    /// <summary>How far it surveys now: its radius, less when its sensors are damaged, nothing while disabled.</summary>
    public int SurveyRadiusOf(SurfaceUnit u)
    {
        if (_data.SurfaceUnit(u.DefId).Damage is not { } d) return u.SurveyRadius;
        if (u.Broken) return 0;
        if (u.Integrity < d.SensorsBelow) return System.Math.Max(System.Math.Min(1, u.SurveyRadius), u.SurveyRadius - d.SensorLoss);
        return u.SurveyRadius;
    }

    /// <summary>The crew are changing a tyre (parked).</summary>
    public bool ChangingTyre(SurfaceUnit u) => u.TyreChangeTicks > 0;

    /// <summary>How far a tyre change has got, 0..1.</summary>
    public Fix64 TyreChangeFraction(SurfaceUnit u)
    {
        var total = (TyresOf(u)?.ChangeSeconds ?? 0) * Simulation.TickRate;
        return total <= 0 || u.TyreChangeTicks <= 0 ? Fix64.Zero : Fix64.Min(Fix64.One, Fix64.Ratio(u.TyreChangeTicks, total));
    }

    /// <summary>Why the crew can't change a tyre on this unit now (null = they can).</summary>
    public string? ChangeTyreProblem(int player, int unitId)
    {
        var u = FindUnit(unitId);
        if (u == null || u.Owner != player) return "not yours";
        if (TyresOf(u) == null) return "it has no tyres to change";
        if (FlatCount(u) == 0) return "no flat tyre";
        if (ChangingTyre(u)) return "already changing a tyre";
        if (u.Spares <= 0) return "no spare left: drive to a repair pad";
        return null;
    }

    /// <summary>Why this unit can't be driven by a move order (null = it can).</summary>
    public string? DriveProblem(SurfaceUnit u) => Disabled(u) ? "disabled: only a repair order (R) moves it, at a crawl" : null;

    private void StartTyreChange(SurfaceUnit u)
    {
        u._path.Clear();
        u.Progress = Fix64.Zero;
        u.DeployOnArrival = false;
        u.TyreChangeTicks = 1;
    }

    /// <summary>One flat, on a random wheel that isn't flat yet.</summary>
    private void Puncture(Simulation sim, SurfaceUnit u, TyreRules t)
    {
        var whole = t.Wheels - Wheels.Count(u.FlatTyres, t.Wheels);
        if (whole <= 0) return;
        var pick = sim.Rng.NextInt(whole);
        for (var i = 0; i < t.Wheels; i++)
        {
            if ((u.FlatTyres & (1 << i)) != 0) continue;
            if (pick-- > 0) continue;
            u.FlatTyres |= 1 << i;
            u.LastFlat = i;
            u.FlatAtTick = sim.Tick;
            return;
        }
    }

    private static void Hit(Simulation sim, SurfaceUnit u, string what, int damage)
    {
        u.Integrity = Fix64.Max(Fix64.Zero, u.Integrity - Fix64.FromInt(damage));
        u.HazardAtTick = sim.Tick;
        u.LastHazard = what;
        u.LastHazardDamage = damage;
    }

    private void Disable(Simulation sim, SurfaceUnit u)
    {
        u.Broken = true;
        u.BrokeAtTick = sim.Tick;
        u._path.Clear();
        u.Progress = Fix64.Zero;
        u.DeployOnArrival = false;
        u.TyreChangeTicks = 0;
        u.RepairTicks = 0;
        u.RepairWaiting = false;
    }

    /// <summary>At the pad: every flat fixed and the spares restocked, paying pad_cost a tyre. False if the player can't pay.</summary>
    private bool PadTyres(Simulation sim, SurfaceUnit u)
    {
        if (TyresOf(u) is not { } t) return true;
        var tyres = FlatCount(u) + System.Math.Max(0, t.Spares - u.Spares);
        if (tyres == 0) return true;
        var cost = t.PadCost.Select(c => new ResourceAmount(c.Resource, c.Amount * tyres)).ToList();
        if (!sim.Players[u.Owner].Stock.TrySpend(cost)) return false;
        u.FlatTyres = 0;
        u.Spares = t.Spares;
        u.TyreChangeTicks = 0;
        return true;
    }

    /// <summary>Tyres the pad would fit (flats + spares to restock).</summary>
    private int PadTyreCount(SurfaceUnit u) => TyresOf(u) is { } t ? FlatCount(u) + System.Math.Max(0, t.Spares - u.Spares) : 0;

    /// <summary>
    /// Every rover with damage rules, each tick: a tyre change in progress (a move order cancels it), wear and the hazard
    /// and flat rolls while driving, toxic ground and predators wherever it is, and disabling at 0 integrity.
    /// </summary>
    private void RoverWear(Simulation sim)
    {
        foreach (var u in _units)
        {
            var def = _data.SurfaceUnit(u.DefId);
            if (def.Damage is not { } d) continue;
            var t = def.Tyres;

            if (u.TyreChangeTicks > 0)
            {
                if (u._path.Count > 0 || t == null) u.TyreChangeTicks = 0; // driven off: the crew pack the jack away
                else if (++u.TyreChangeTicks >= t.ChangeSeconds * Simulation.TickRate)
                {
                    u.TyreChangeTicks = 0;
                    var wheel = Wheels.First(u.FlatTyres, t.Wheels);
                    if (wheel >= 0 && u.Spares > 0)
                    {
                        u.FlatTyres &= ~(1 << wheel);
                        u.Spares--;
                    }
                }
            }

            var chances = Fix64.Max(Fix64.Zero, Fix64.One + UnitResearchBonus(u.Owner, u.DefId, Modifier.Breakdown));
            var driving = u.Mode == UnitMode.Mobile && u._path.Count > 0 && u.TyreChangeTicks == 0;
            var terrain = Map.Terrain(u.Tile);
            if (driving && d.On(terrain.Id) is { } ground)
            {
                var flats = t == null ? 0 : Wheels.Count(u.FlatTyres, t.Wheels);
                var wear = ground.Wear * (Fix64.One + (t?.FlatWear ?? Fix64.Zero) * flats);
                u.Integrity = Fix64.Max(Fix64.Zero, u.Integrity - wear / Simulation.TicksPerMinute);

                if (ground.Hazard > Fix64.Zero && sim.Rng.NextFix() < ground.Hazard * chances / Simulation.TicksPerMinute)
                {
                    var eligible = d.Hazards.Where(h => h.Weight > 0 && (h.Terrain.Count == 0 || h.Terrain.Contains(terrain.Id))).ToList();
                    var total = eligible.Sum(h => h.Weight);
                    if (total > 0)
                    {
                        var roll = sim.Rng.NextInt(total);
                        var index = 0;
                        while (roll >= eligible[index].Weight)
                        {
                            roll -= eligible[index].Weight;
                            index++;
                        }
                        var hazard = eligible[index];
                        Hit(sim, u, hazard.Id, sim.Rng.NextInt(hazard.DamageMin, hazard.DamageMax + 1));
                        if (hazard.Flat && t != null) Puncture(sim, u, t);
                    }
                }

                if (t != null && ground.Flat > Fix64.Zero && Wheels.Count(u.FlatTyres, t.Wheels) < t.Wheels)
                {
                    var lost = (Hundred - u.Integrity) / Hundred;
                    var chance = ground.Flat * (Fix64.One + t.LowIntegrity * lost) * chances;
                    if (sim.Rng.NextFix() < chance / Simulation.TicksPerMinute) Puncture(sim, u, t);
                }
            }

            if (Map.Toxic[u.Tile] && d.ToxicPerMinute > Fix64.Zero)
                u.Integrity = Fix64.Max(Fix64.Zero, u.Integrity - d.ToxicPerMinute / Simulation.TicksPerMinute);
            if (Map.WildlifeAt[u.Tile] == Wildlife.Predators && d.WildlifeChancePerMinute > Fix64.Zero &&
                sim.Rng.NextFix() < d.WildlifeChancePerMinute / Simulation.TicksPerMinute)
                Hit(sim, u, WildlifeHazard, sim.Rng.NextInt(d.WildlifeMin, d.WildlifeMax + 1));

            if (!u.Broken && u.Integrity <= Fix64.Zero) Disable(sim, u);
        }
    }

    /// <summary>The hazard's display name ("Rock strike", "Wildlife attack"), from the unit's data.</summary>
    public string HazardName(SurfaceUnit u)
    {
        if (u.LastHazard == WildlifeHazard) return "Wildlife attack";
        var d = _data.SurfaceUnit(u.DefId).Damage;
        return d?.Hazards.FirstOrDefault(h => h.Id == u.LastHazard)?.Name ?? u.LastHazard;
    }
}
