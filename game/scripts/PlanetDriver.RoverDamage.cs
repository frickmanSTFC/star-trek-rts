using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Rover damage and flat tyres on the HUD (rules in sim/Surface/RoverDamage.cs): the selected rover's panel (integrity
// bar, tyres, what the ground is doing to it, REPAIR [R] and CHANGE TYRE [T]), events for hazards, flats, disabling
// and repairs, the R / T keys, and a tooltip line for our rovers on the hovered tile.
public partial class PlanetDriver
{
    /// <summary>What we last saw of each of our rovers, to turn changes into events.</summary>
    private readonly record struct RoverSeen(long Hazard, long Flat, int Flats, int Spares, bool Broken, bool Damaged, bool Sensors, bool Toxic, bool NeedsRepair);

    private readonly Dictionary<int, RoverSeen> _seenRovers = new();

    private static readonly Color RoverWarn = new(1f, 0.7f, 0.42f);
    private static readonly Color RoverBad = new(1f, 0.42f, 0.32f);
    private static readonly Color RoverGood = new(0.56f, 0.83f, 0.69f);

    private bool IsRover(SurfaceUnit u) => _data.SurfaceUnit(u.DefId).Damage != null;

    private IEnumerable<SurfaceUnit> SelectedRovers() =>
        _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null && u.Owner == LocalPlayer && IsRover(u)).Select(u => u!);

    private string WheelName(SurfaceUnit u, int wheel) => Wheels.Name(_data.SurfaceUnit(u.DefId).Tyres?.Wheels ?? 4, wheel);

    private string FlatList(SurfaceUnit u)
    {
        var wheels = _data.SurfaceUnit(u.DefId).Tyres?.Wheels ?? 0;
        return string.Join(", ", Enumerable.Range(0, wheels).Where(i => (u.FlatTyres & (1 << i)) != 0).Select(i => Wheels.Name(wheels, i)));
    }

    private static string Spares(int n) => n == 1 ? "1 spare" : $"{n} spares";

    /// <summary>Events for our rovers: hazards, flats, tyre changes, damage thresholds, toxic ground, disabled, repaired.</summary>
    private void RoverEvents()
    {
        foreach (var u in Surface.Units)
        {
            if (u.Owner != LocalPlayer || _data.SurfaceUnit(u.DefId).Damage is not { } d) continue;
            var integrity = u.Integrity;
            var now = new RoverSeen(u.HazardAtTick, u.FlatAtTick, Surface.FlatCount(u), u.Spares, u.Broken,
                integrity < d.SlowBelow, integrity < d.SensorsBelow, Surface.Map.Toxic[u.Tile], Surface.NeedsRepair(u));
            if (_seenRovers.TryGetValue(u.Id, out var was) && was != now)
            {
                var name = UnitName(u);
                var pct = Mathf.FloorToInt(integrity.ToFloat());
                if (now.Hazard != was.Hazard && u.HazardAtTick >= 0)
                    ColonyReport(u.LastHazard == PlanetSurface.WildlifeHazard
                        ? $"{name} attacked by wildlife (−{u.LastHazardDamage}, {pct}% integrity) — get it away from the herd"
                        : $"{name}: {Surface.HazardName(u).ToLowerInvariant()} (−{u.LastHazardDamage}, {pct}% integrity)", u.LastHazardDamage >= 15 ? RoverBad : RoverWarn);
                if (now.Flat != was.Flat && u.FlatAtTick >= 0 && now.Flats > 0)
                    ColonyReport($"{name}: flat tyre ({WheelName(u, u.LastFlat)})" + (u.Spares > 0
                        ? $" — T to change it ({Spares(u.Spares)} on board)"
                        : " — no spare left: R to the repair pad"), now.Flats >= 2 ? RoverBad : RoverWarn);
                if (now.Flats < was.Flats && now.Spares < was.Spares)
                    ColonyReport($"{name}: tyre changed ({Spares(u.Spares)} left)", RoverGood);
                if (now.Broken && !was.Broken)
                    ColonyReport($"{name} disabled ({pct}% integrity) — R to limp to the repair pad", RoverBad);
                else if (now.Sensors && !was.Sensors && !now.Broken)
                    ColonyReport($"{name}: sensors damaged ({pct}%), it surveys less ground — R to repair", RoverWarn);
                else if (now.Damaged && !was.Damaged && !now.Broken)
                    ColonyReport($"{name} damaged ({pct}%): it drives slower — R to repair", RoverWarn);
                if (now.Toxic && !was.Toxic && d.ToxicPerMinute > Fix64.Zero)
                    ColonyReport($"{name} is in a toxic patch: it corrodes while it stays", RoverWarn);
                if (was.NeedsRepair && !now.NeedsRepair && Surface.StructureAt(u.Tile) != null)
                    ColonyReport($"{name} repaired and ready", RoverGood);
            }
            _seenRovers[u.Id] = now;
        }
    }

    /// <summary>R: send the selected damaged vehicles (rovers, or broken crawlers) to the nearest repair pad.</summary>
    private void RepairSelected()
    {
        var damaged = _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit)
            .Where(u => u != null && u.Owner == LocalPlayer && Surface.NeedsRepair(u)).Select(u => u!).ToList();
        if (damaged.Count == 0)
        {
            _hud.PushEvent("Nothing to repair: the selected vehicles are in perfect shape", RoverGood);
            return;
        }
        if (Surface.NearestRepairPad(LocalPlayer, damaged[0].Tile) == null)
        {
            _hud.PushEvent("No finished colony core to repair at", RoverWarn);
            return;
        }
        _sim.Schedule(_sim.Tick + InputDelayTicks, new RepairCommand(LocalPlayer, damaged.Select(u => u.Id).ToList()));
        _hud.PushEvent(damaged.Count == 1 ? $"{UnitName(damaged[0])} heading for the repair pad" : $"{damaged.Count} vehicles heading for the repair pad", new Color(0.62f, 0.82f, 1f));
    }

    /// <summary>T: the crews of the selected rovers with a flat change a tyre where they stand.</summary>
    private void ChangeTyres()
    {
        var rovers = SelectedRovers().ToList();
        if (rovers.Count == 0) return;
        var ok = rovers.Where(u => Surface.ChangeTyreProblem(LocalPlayer, u.Id) == null).Select(u => u.Id).ToList();
        if (ok.Count > 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new ChangeTyreCommand(LocalPlayer, ok));
        else if (rovers.Count == 1) _hud.PushEvent($"{UnitName(rovers[0])} can't change a tyre: {Surface.ChangeTyreProblem(LocalPlayer, rovers[0].Id)}", RoverWarn);
        else _hud.PushEvent("None of the selected rovers has a flat tyre and a spare", RoverWarn);
    }

    /// <summary>Buttons on the rover panel; true if it was one of ours.</summary>
    private bool RoverAction(string id)
    {
        switch (id)
        {
            case "repair":
                RepairSelected();
                return true;
            case "tyre":
                ChangeTyres();
                return true;
            case "found":
                ArmFounding(); // a construction rover's FOUND OUTPOST (PlanetDriver.Founding.cs)
                return true;
        }
        return false;
    }

    /// <summary>One selected rover in the bottom panel: integrity bar, tyres, ground, speed and sensors, and its buttons.</summary>
    private void ShowRover(SurfaceUnit u)
    {
        var def = _data.SurfaceUnit(u.DefId);
        var d = def.Damage!;
        var t = def.Tyres;
        var integrity = u.Integrity.ToFloat();
        var flats = Surface.FlatCount(u);
        var repairing = Surface.IsRepairing(u);
        var changing = Surface.ChangingTyre(u);
        var share = Surface.DriveShare(u).ToFloat();

        var (status, colour) =
            repairing ? (u.RepairWaiting ? "Repair waiting: not enough resources" : Surface.CrewOut(u) < Fix64.One ? "Repair crew on the way" : "Repairing on the pad", RoverGood)
            : u.Broken ? (u.Path.Count > 0 ? "Disabled: limping to the repair pad" : "Disabled: press R to limp to the repair pad", RoverBad)
            : changing ? ("Changing a tyre", new Color(0.62f, 0.82f, 1f))
            : flats >= 2 ? ($"{flats} flat tyres: barely crawling", RoverBad)
            : flats == 1 ? ("Flat tyre: driving slower", RoverWarn)
            : FounderStatus(u) is { } founding ? founding
            : u.Path.Count > 0 ? ("Driving", RoverGood)
            : ("Parked", new Color(0.6f, 0.65f, 0.75f));

        var rows = new List<(string, string)>();
        var radius = Surface.SurveyRadiusOf(u);
        rows.Add(("Speed", $"{Mathf.RoundToInt(share * 100)}%" + (share < 0.999f
            ? $"  ({string.Join(" · ", new[] { u.Broken ? "disabled" : u.Integrity < d.SlowBelow ? "damage" : "", flats > 0 ? (flats == 1 ? "flat tyre" : "flat tyres") : "" }.Where(x => x.Length > 0))})"
            : "")));
        rows.Add(("Survey", radius == u.SurveyRadius ? $"{radius} tiles around it"
            : radius == 0 ? "sensors dark while disabled" : $"{radius} tiles (sensors damaged, normally {u.SurveyRadius})"));
        if (t != null)
            rows.Add(("Tyres", (flats == 0 ? $"all {t.Wheels} sound" : $"flat: {FlatList(u)}") + $"  ·  {Spares(u.Spares)} on board"));
        var terrain = Surface.Map.Terrain(u.Tile);
        if (d.On(terrain.Id) is { } ground)
        {
            var lost = (100f - integrity) / 100f;
            var flatChance = ground.Flat.ToFloat() * (1f + (t?.LowIntegrity.ToFloat() ?? 0f) * lost);
            rows.Add(("Ground", $"{terrain.Name}: wears {ground.Wear.ToFloat():0.##}/min driving · hazard ~{Mathf.RoundToInt(ground.Hazard.ToFloat() * 100)}% · flat ~{Mathf.RoundToInt(flatChance * 100)}% a minute"));
        }
        var dangers = new List<string>();
        if (Surface.Map.Toxic[u.Tile]) dangers.Add($"toxic ground: −{d.ToxicPerMinute.ToFloat():0.#}/min");
        if (Surface.Map.WildlifeAt[u.Tile] == Wildlife.Predators) dangers.Add($"predators: ~{Mathf.RoundToInt(d.WildlifeChancePerMinute.ToFloat() * 100)}% a minute of an attack");
        if (dangers.Count > 0) rows.Add(("Danger", string.Join(" · ", dangers)));
        if (rows.Count < 5) rows.Add(("Lights", _view.HeadlightText(u.Id) + "  [L]"));
        var cost = Surface.RepairCost(u);
        rows.Add(("Repair", Surface.NeedsRepair(u) ? $"on a colony core's pad: {(cost.Count == 0 ? "free" : Amounts(cost, false))}" : "not needed"));

        var conditionText = $"{Mathf.FloorToInt(integrity)}%  ·  " + (u.Broken ? "disabled"
            : integrity < d.SensorsBelow.ToFloat() ? "slower, sensors damaged"
            : integrity < d.SlowBelow.ToFloat() ? $"slower below {Mathf.RoundToInt(d.SlowBelow.ToFloat())}%"
            : "sound");
        var maintenance = repairing ? "the repair crew are on it" : u.Broken ? "at 0 it stops: R limps it home" : $"wear: rough ground, hazards, flats";

        var progress = -1f;
        var progressLabel = "";
        var progressText = "";
        if (changing && t != null)
        {
            progress = Surface.TyreChangeFraction(u).ToFloat();
            progressLabel = "Changing tyre";
            progressText = $"{Mathf.RoundToInt(progress * 100)}%  ·  {t.ChangeSeconds} s parked  ·  a move order stops it";
        }
        else if (repairing)
        {
            var crew = Surface.CrewOut(u).ToFloat();
            progress = crew < 1f ? crew : integrity / 100f;
            progressLabel = crew < 1f ? "Crew walking out" : "Repair";
            progressText = crew < 1f ? "then new tyres and welding" : $"{Mathf.FloorToInt(integrity)}%  ·  {Mathf.RoundToInt(def.Repair!.PerSecond.ToFloat())} points a second";
        }

        var actions = new List<ActionView>();
        var pad = Surface.NearestRepairPad(LocalPlayer, u.Tile);
        actions.Add(new ActionView("repair", u.Broken ? "LIMP TO THE PAD [R]" : "REPAIR [R]",
            !Surface.NeedsRepair(u) ? "not needed" : pad == null ? "no colony core" : repairing ? "on the pad" : Amounts(cost, false, shortNames: true),
            Surface.NeedsRepair(u) && pad != null && !repairing));
        if (t != null)
        {
            var why = Surface.ChangeTyreProblem(LocalPlayer, u.Id);
            actions.Add(new ActionView("tyre", "CHANGE TYRE [T]", why ?? $"{t.ChangeSeconds} s, uses a spare", why == null));
        }
        if (def.Found is { } found)
        {
            var stuck = Surface.Disabled(u) ? Surface.FoundText(FoundProblem.Disabled) : null;
            actions.Add(new ActionView("found", "FOUND OUTPOST [F]", stuck ?? Amounts(found.Cost, false, shortNames: true), stuck == null));
            if (Surface.Unfolding(u) && progress < 0f)
            {
                progress = Surface.DeployFraction(u).ToFloat();
                progressLabel = "Unfolding";
                progressText = $"{Mathf.RoundToInt(progress * 100)}%  ·  {def.DeploySeconds} s, then it becomes the new hub  ·  a move order folds it up (refunded)";
            }
        }

        _hud.ShowStructure(new StructureView(UnitName(u), $"{TypeName(u)}  ·  {Surface.Map.Terrain(u.Tile).Name.ToLowerInvariant()}", "rover", status, colour,
            rows.Take(6).ToList(), integrity, conditionText, maintenance, progress, progressLabel, progressText, actions));
    }

    /// <summary>A tooltip line for each of our rovers on the hovered tile that is hurt or has a flat.</summary>
    private string RoverTooltip(int tile)
    {
        var sb = new StringBuilder();
        foreach (var u in Surface.Units)
        {
            if (u.Tile != tile || u.Owner != LocalPlayer || !IsRover(u) || !Surface.NeedsRepair(u)) continue;
            sb.Append('\n').Append(CallSign(u)).Append(": ").Append(Mathf.FloorToInt(u.Integrity.ToFloat())).Append('%');
            var flats = Surface.FlatCount(u);
            if (flats > 0) sb.Append(", ").Append(flats == 1 ? "flat tyre" : $"{flats} flat tyres");
            if (u.Broken) sb.Append(", disabled");
        }
        return sb.ToString();
    }
}
