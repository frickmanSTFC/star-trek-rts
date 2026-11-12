using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The Vehicle Bay and Research Lab in the Machine Shop's tabbed bottom panel (owner approved
// docs/mockups/bay-lab-panel, 2026-11-12: "go"). Render-only: orders go through the existing commands
// (BuildUnitCommand, CancelUnitCommand, ResearchCommand); nothing in the sim changed.
//  - A selected, finished Vehicle Bay: FabPanel kind "bay" (name, base, "Building Mk II Surveyor · 18 s left" and the
//    building's buttons on the left; Overview / Queue · n / Vehicles · n). Vehicle tiles: click = its card (the shared
//    ItemCard with a PanelCard), Ctrl+click / number key = order one, Shift = five, right-click = cancel one of it,
//    Backspace = cancel the last order. × on a queue row removes that order with a full refund: CancelUnitCommand only
//    takes the last order back, so the rows after it are cancelled too and ordered again in the same tick (their cost
//    comes back and is paid again, so nothing changes for them; the one being built keeps its progress unless it is the
//    one removed).
//  - A selected, finished Research Lab: kind "lab" (Researching X · 48/80 · 2:00 left + a bar; Overview / Tech tree ·
//    n / Done · n). The tree: start-here techs, techs that need an earlier one, situational (ruins) ones; locked ones
//    greyed out but clickable. Click = the tech's card (RESEARCH THIS [Enter] = ResearchCommand), Ctrl+click researches;
//    number keys open a tech's card, the same key again researches it (as before). Shared techs sit on the Done tab and
//    open their compact card.
public partial class PlanetDriver
{
    /// <summary>The bay vehicle ("unit:&lt;id&gt;") or lab tech ("tech:&lt;id&gt;") whose full card is open; null = none.</summary>
    private string? _panelCard;

    private static readonly Color BayColour = new(0.62f, 0.82f, 1f);
    private static readonly Color LabColour = new(0.56f, 0.84f, 0.48f);
    private static readonly Color RedText = new(1f, 0.48f, 0.42f);

    private string VehicleName(string unitId) => _data.GroundVehicleNames.GetValueOrDefault(unitId, unitId);

    // ---------------------------------------------------------------- the panels

    /// <summary>The tabbed panel for a finished Vehicle Bay or Research Lab of ours (null for anything else).</summary>
    private FabPanelView? BayLabPanelOf(SurfaceStructure s, string subtitle, string status, Color statusColour, List<(string Label, string Value)> rows,
        string conditionText, string maintenance, float condition, List<ActionView> actions)
    {
        if (s.State != StructureState.Complete || s.Owner != LocalPlayer) return null;
        var def = _data.Building(s.DefId);
        if (def.IsFactory) return BayPanel(s, def, subtitle, status, statusColour, rows, conditionText, maintenance, condition, actions);
        if (def.IsLab) return LabPanel(s, def, subtitle, status, statusColour, rows, conditionText, maintenance, condition, actions);
        return null;
    }

    /// <summary>How fast this building works right now (0 when it isn't working): power share × condition × bonuses.</summary>
    private float RateOf(SurfaceStructure s, BuildingDef def)
    {
        if (s.Status is not (StructureStatus.Working or StructureStatus.LowPower)) return 0f;
        var power = def.PowerUse > Fix64.Zero ? Surface.PowerOf(s.BaseId).Efficiency : Fix64.One;
        return (Surface.SpeedMultiplier(s) * Surface.ConditionFactor(s) * power).ToFloat();
    }

    /// <summary>"Crewed · 6 power · full speed" (or "runs at 60% (power 60%)").</summary>
    private string CrewPower(SurfaceStructure s, BuildingDef def)
    {
        var parts = new List<string>();
        if (def.Crewed) parts.Add("Crewed");
        if (def.PowerUse > Fix64.Zero) parts.Add($"{Hud.Whole(def.PowerUse)} power");
        var power = def.PowerUse > Fix64.Zero ? Surface.PowerOf(s.BaseId).Efficiency : Fix64.One;
        var pct = Mathf.RoundToInt((Surface.SpeedMultiplier(s) * Surface.ConditionFactor(s) * power).ToFloat() * 100);
        parts.Add((pct == 100 ? "full speed" : $"runs at {pct}%") + (power < Fix64.One ? $" (power {Mathf.RoundToInt(power.ToFloat() * 100)}%)" : ""));
        if (s.Paused) parts.Add("paused");
        return string.Join("  ·  ", parts);
    }

    private List<FabCost> UnitCost(string unitId, bool paid)
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        return _data.SurfaceUnit(unitId).Cost.Select(a =>
        {
            var r = _data.Resources[a.Resource];
            return new FabCost(r.Id, r.Name, Mathf.CeilToInt(a.Amount.ToFloat()), Mathf.FloorToInt(stock[a.Resource].ToFloat()), !paid && stock[a.Resource] < a.Amount);
        }).ToList();
    }

    private FabPanelView BayPanel(SurfaceStructure s, BuildingDef def, string subtitle, string status, Color statusColour, List<(string Label, string Value)> rows,
        string conditionText, string maintenance, float condition, List<ActionView> actions)
    {
        var queue = s.Production;
        var rate = RateOf(s, def);
        var fraction = Surface.ProductionFraction(s).ToFloat();
        string Left(string unitId, float done) => rate > 0 ? $"{Mathf.CeilToInt(_data.SurfaceUnit(unitId).BuildSeconds * (1f - done) / rate)} s left" : "stopped";
        if (queue.Count > 0 && !s.Broken && !s.Paused)
        {
            status = $"Building {VehicleName(queue[0])}  ·  {Left(queue[0], fraction)}";
            statusColour = rate > 0 ? RoverGood : PartsWarn;
        }

        // Queue rows: name, a bar on the one being built, the paid cost, time or "waiting", ×.
        var rowsQ = new List<FabQueueView>();
        for (var i = 0; i < queue.Count; i++)
        {
            var u = queue[i];
            var building = i == 0;
            rowsQ.Add(new FabQueueView(i + 1, u, VehicleName(u), BayColour, UnitCost(u, true), "",
                building ? $"{Mathf.FloorToInt(fraction * 100)}%  ·  {Left(u, fraction)}" : $"waiting  ·  {_data.SurfaceUnit(u).BuildSeconds} s",
                building ? fraction : -1f, building ? "BUILDING" : "PAID", $"vehicle:{u}"));
        }
        var total = queue.Select((u, i) => _data.SurfaceUnit(u).BuildSeconds * (i == 0 ? 1f - fraction : 1f)).Sum();
        var queueNote = queue.Count == 0
            ? "Queue empty. Click a vehicle on the Vehicles tab for its card, or press its number key to order one (Shift: 5)."
            : (rate > 0 ? $"{Clock(total / rate)} left in all  ·  " : "") + "× removes that order and refunds its cost in full  ·  Backspace removes the last one";

        // Overview.
        var facts = new List<FabFactView>
        {
            new("Making", queue.Count > 0 ? $"{VehicleName(queue[0])}  ·  {Mathf.FloorToInt(fraction * 100)}%  ·  {Left(queue[0], fraction)}" : "nothing: pick a vehicle on the Vehicles tab"),
            new("Crew · power", CrewPower(s, def)),
        };
        if (def.Wears && conditionText.Length > 0)
            facts.Add(new FabFactView("Condition", conditionText + (maintenance.Length > 0 ? $"  ·  {maintenance}" : ""), condition >= 70 ? HudKit.Good : condition >= 50 ? PlanetHud.Accent : RedText));
        facts.Add(new FabFactView("Queue", $"{queue.Count} of {def.ProductionQueue} slots used", queue.Count >= def.ProductionQueue ? PartsWarn : null));
        foreach (var (label, value) in rows)
            if (label is "Next" or "Kits" && value.Length > 0) facts.Add(new FabFactView(label == "Next" ? "Next level" : label, value));
        facts.Add(new FabFactView("Roll-out", "New vehicles drive out of the door onto the apron and wait for orders."));

        // Vehicles.
        var tiles = new List<FabTileView>();
        for (var i = 0; i < def.Produces.Count; i++)
        {
            var u = def.Produces[i];
            var ud = _data.SurfaceUnit(u);
            var cost = UnitCost(u, false);
            var problem = Surface.ProductionProblem(_sim, LocalPlayer, s.Id, u);
            var key = i < 9 ? $"{i + 1}" : "";
            var tip = new StringBuilder(VehicleName(u)).Append(" · ").Append(VehicleKind(ud));
            tip.Append("\nCosts ").Append(string.Join(" + ", cost.Select(c => $"{c.Amount} {c.Name} (have {c.Have})")));
            tip.Append($"\n{ud.BuildSeconds} s at full speed");
            if (problem != null) tip.Append($"\nCan't order: {problem}");
            tip.Append("\nClick: its card  ·  Ctrl+click: order 1  ·  Shift+click: order 5  ·  right-click: cancel one");
            if (key.Length > 0) tip.Append($"  ·  key {key} orders 1 (Shift 5)");
            tiles.Add(new FabTileView(u, VehicleName(u), key, BayColour, cost, queue.Count(q => q == u), problem == null, tip.ToString(),
                $"vehicle:{u}", $"{ud.BuildSeconds} s · speed {Num(ud.Speed.ToFloat())} · survey {ud.SurveyRadius}"));
        }

        return new FabPanelView(def.Name, $"{subtitle}  ·  {BaseName(s.BaseId)}", s.DefId, status, statusColour, actions, facts,
            System.Array.Empty<(string, string)>(), rowsQ, queueNote, tiles, def.Produces.Count == 0 ? "This bay builds nothing." : "", false,
            "bay", new[] { ("overview", "OVERVIEW"), ("queue", $"QUEUE · {queue.Count}"), ("recipes", $"VEHICLES · {def.Produces.Count}") });
    }

    private static string VehicleKind(SurfaceUnitDef ud) =>
        ud.CanMine ? "Mining vehicle" : ud.CanFound ? "Builder" : ud.Mark is { } mark ? $"Survey rover · Mk {Roman(mark)}" : ud.SurveyRadius > 0 ? "Survey rover" : "Ground vehicle";

    private FabPanelView LabPanel(SurfaceStructure s, BuildingDef def, string subtitle, string status, Color statusColour, List<(string Label, string Value)> rows,
        string conditionText, string maintenance, float condition, List<ActionView> actions)
    {
        var me = Surface.Players[LocalPlayer];
        var rate = Surface.ResearchPerMinute(LocalPlayer).ToFloat();
        var labs = Surface.Structures.Count(x => x.Owner == LocalPlayer && x.State == StructureState.Complete && _data.Building(x.DefId).IsLab);
        var progress = -1f;
        TechDef? active = me.ActiveTech >= 0 ? _data.Techs[me.ActiveTech] : null;
        string LeftOf(TechDef t) => rate > 0 ? $"{Clock((t.Cost - me.ResearchProgress[t.Index]).ToFloat() / rate * 60f)} left" : "stopped";
        if (active != null && !s.Broken && !s.Paused)
        {
            var done = me.ResearchProgress[active.Index];
            status = $"Researching {active.Name}  ·  {Hud.Whole(done)}/{Hud.Whole(active.Cost)}  ·  {LeftOf(active)}";
            statusColour = rate > 0 ? LabColour : PartsWarn;
            progress = (done / active.Cost).ToFloat();
        }

        var known = _data.Techs.Where(t => me.Known[t.Index]).ToList();
        var facts = new List<FabFactView>
        {
            new("Researching", active != null ? $"{active.Name}  ·  {Hud.Whole(me.ResearchProgress[active.Index])}/{Hud.Whole(active.Cost)} points" : "nothing: pick a tech on the Tech tree tab", active == null ? PartsWarn : null),
            new("Speed", $"{rate:0.#} points a minute" + (labs > 1 ? $" (all {labs} labs)" : "") + (active != null ? $"  ·  {LeftOf(active)}" : "")),
            new("Done", $"{known.Count} of {_data.Techs.Count}" + (known.Count > 0 ? "  ·  " + string.Join(", ", known.Select(t => t.Name)) : ""), known.Count > 0 ? HudKit.Good : null),
            new("Crew · power", CrewPower(s, def)),
        };
        if (def.Wears && conditionText.Length > 0)
            facts.Add(new FabFactView("Condition", conditionText + (maintenance.Length > 0 ? $"  ·  {maintenance}" : ""), condition >= 70 ? HudKit.Good : condition >= 50 ? PlanetHud.Accent : RedText));
        foreach (var (label, value) in rows)
            if (label is "Next" or "Kits" && value.Length > 0) facts.Add(new FabFactView(label == "Next" ? "Next level" : label, value));
        facts.Add(new FabFactView("On finishing", "The lab waits for you to pick the next tech: the Tech tree tab opens and the event feed says so. Progress on a tech you switch away from is kept."));

        // The tree: start here / needs an earlier tech / situational (ruins).
        var columns = new[]
        {
            new TechColumnView("Start here", _data.Techs.Where(t => t.Requires.Count == 0 && !t.Optional).Select(t => TechTile(t, false)).ToList()),
            new TechColumnView("Needs an earlier tech", _data.Techs.Where(t => t.Requires.Count > 0 && !t.Optional).Select(t => TechTile(t, true)).ToList()),
            new TechColumnView("Situational · ruins", _data.Techs.Where(t => t.Optional).Select(t => TechTile(t, t.Requires.Count > 0)).ToList()),
        };
        var doneTiles = known.Select(t => TechTile(t, false)).ToList();
        foreach (var shared in me.SharedTechs)
            doneTiles.Add(new TechTileView($"{SharedPrefix}{shared.Id}", shared.Name, $"tech:{shared.Id}", shared.Effect, false, $"SHARED BY {SharedBy(shared.Id).ToUpperInvariant()}",
                HudKit.Sky, true, false, false, false, "shared", "", $"{shared.Name}\n{shared.Effect}\nShared by {SharedBy(shared.Id)}: works like a researched tech\nClick: its card"));

        return new FabPanelView(def.Name, $"{subtitle}  ·  {BaseName(s.BaseId)}", s.DefId, status, statusColour, actions, facts,
            System.Array.Empty<(string, string)>(), System.Array.Empty<FabQueueView>(), "", System.Array.Empty<FabTileView>(), "", false,
            "lab", new[] { ("overview", "OVERVIEW"), ("tree", $"TECH TREE · {_data.Techs.Count}"), ("done", $"DONE · {doneTiles.Count}") },
            columns, doneTiles, progress);
    }

    /// <summary>A tech's tile: first effect (or red "needs …" while a prerequisite isn't done) and its status.</summary>
    private TechTileView TechTile(TechDef t, bool linked)
    {
        var me = Surface.Players[LocalPlayer];
        var done = me.Known[t.Index];
        var active = me.ActiveTech == t.Index;
        var missing = t.Requires.Where(r => !me.Known[_data.Tech(r).Index]).Select(r => _data.Tech(r).Name).ToList();
        var locked = !done && missing.Count > 0;
        var share = (me.ResearchProgress[t.Index] / t.Cost).ToFloat();
        var (statusText, colour) = done ? ("✓ DONE", HudKit.Good)
            : active ? ($"{Mathf.FloorToInt(share * 100)}%", PlanetHud.Accent)
            : locked ? ("LOCKED", RedText)
            : share > 0 ? ($"{Mathf.FloorToInt(share * 100)}% · {Hud.Whole(t.Cost)} PTS", HudKit.Dim)
            : ($"{Hud.Whole(t.Cost)} PTS", HudKit.Dim);
        var effects = TechEffects(t);
        var line = locked ? $"needs {JoinAnd(missing)}" : effects.Count > 0 ? effects[0] : t.Effect;
        var key = t.Index < 9 ? $"{t.Index + 1}" : "";
        var tip = $"{t.Name} · {Hud.Whole(t.Cost)} points\n{string.Join("\n", effects)}" + (locked ? $"\nLocked: research {JoinAnd(missing)} first" : "")
                  + "\nClick: its card  ·  Ctrl+click: research it" + (key.Length > 0 ? $"  ·  key {key}: its card (again: research)" : "");
        return new TechTileView(t.Id, t.Name, $"tech:{t.Id}", line, locked, statusText, colour, done, active, locked, linked, t.Optional ? "ruins" : "", key, tip);
    }

    // ---------------------------------------------------------------- what a tech does, in plain words

    private static string JoinAnd(IReadOnlyList<string> items) =>
        items.Count == 0 ? "" : items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    private static string Plural(string name)
    {
        if (name.EndsWith("y") && name.Length > 1 && "aeiou".IndexOf(name[^2]) < 0) return name[..^1] + "ies";
        if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh")) return name + "es";
        return name + "s";
    }

    /// <summary>"Refineries: +20% yield", "Every building wears 30% slower", "Mining Crawlers break down half as often",
    /// "Drones repair buildings below 70%", "Unlocks the Deep-Core Mine"; a tech with none of these gets its effect line.</summary>
    private List<string> TechEffects(TechDef t)
    {
        var lines = new List<string>();
        foreach (var m in t.Modifiers)
        {
            var add = m.Add.ToFloat();
            var pct = Mathf.RoundToInt(Mathf.Abs(add) * 100);
            var buildings = JoinAnd(m.Buildings.Select(b => Plural(BuildingName(b))).ToList());
            var units = JoinAnd(m.Units.Select(u => Plural(VehicleName(u))).ToList());
            var who = units.Length > 0 ? units : buildings;
            switch (m.Stat)
            {
                case Modifier.Speed:
                    lines.Add($"{(who.Length > 0 ? who : "Every building")}: {pct}% {(add >= 0 ? "faster" : "slower")}");
                    break;
                case Modifier.Yield:
                    lines.Add($"{(who.Length > 0 ? who : "Every building")}: {(add >= 0 ? "+" : "−")}{pct}% yield");
                    break;
                case Modifier.Wear:
                    lines.Add($"{(who.Length > 0 ? who : "Every building")} wear{(who.Length > 0 ? "" : "s")} {pct}% {(add <= 0 ? "slower" : "faster")}");
                    break;
                case Modifier.Breakdown:
                    var times = Mathf.IsEqualApprox(add, -0.5f) ? "half as often" : add < 0 ? $"{pct}% less often" : $"{pct}% more often";
                    lines.Add($"{(who.Length > 0 ? who : "Vehicles")} break down {times}");
                    break;
                case Modifier.AutoMaintain:
                    lines.Add($"Drones repair {(buildings.Length > 0 ? buildings : "buildings")} below {Hud.Whole(_data.Maintenance!.AutoBelow)}% (repairs still take spare kits)");
                    break;
                default:
                    lines.Add($"{(who.Length > 0 ? who + ": " : "")}{m.Stat} {Pct(m.Add)}");
                    break;
            }
        }
        foreach (var b in _data.Buildings.Where(b => b.RequiresTech == t.Id)) lines.Add($"Unlocks the {b.Name}");
        if (lines.Count == 0) lines.Add(t.Effect.Length > 0 ? t.Effect : t.Description);
        return lines;
    }

    // ---------------------------------------------------------------- cards

    /// <summary>The open vehicle / tech card (null closes it when its bay or lab is no longer selected).</summary>
    private PanelCard? BayLabCard()
    {
        if (_panelCard == null) return null;
        if (_inOrbit) return Drop();
        if (_panelCard.StartsWith("unit:") && SelectedBay() is { } bay && _data.Building(bay.DefId).Produces.Contains(_panelCard[5..]))
            return VehicleCard(bay, _panelCard[5..]);
        if (_panelCard.StartsWith("tech:") && SelectedLab() != null && _data.HasTech(_panelCard[5..])) return TechCard(_data.Tech(_panelCard[5..]));
        return Drop();

        PanelCard? Drop()
        {
            _panelCard = null;
            return null;
        }
    }

    private PanelCard VehicleCard(SurfaceStructure bay, string u)
    {
        var def = _data.Building(bay.DefId);
        var ud = _data.SurfaceUnit(u);
        var index = def.Produces.ToList().IndexOf(u);
        var key = index is >= 0 and < 9 ? $"{index + 1}" : "";
        var cost = UnitCost(u, false);
        var problem = Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, u);
        var missing = cost.Where(c => c.Short).ToList();
        var (tone, text) = problem == null ? (CardTone.Good, "Can build now. Paid when you order it; refunded in full if you cancel.")
            : missing.Count > 0 ? (CardTone.Bad, "Can't build yet. Missing " + JoinAnd(missing.Select(c => $"{c.Amount - c.Have} {c.Name}").ToList()) + " (refine more ore, or wait for the crawlers).")
            : problem == "queue full" ? (CardTone.Bad, $"Can't order it now: the queue is full ({bay.Production.Count} of {def.ProductionQueue}). Cancel an order or wait for one to roll out.")
            : (CardTone.Bad, $"Can't order it now: {problem}.");
        var stats = new List<(string, string)>
        {
            (Num(ud.Speed.ToFloat()), "Speed"),
            (ud.SurveyRadius.ToString(), "Survey radius"),
            ud.Tyres is { } tyres ? ($"{tyres.Wheels} · {tyres.Spares} spare{(tyres.Spares == 1 ? "" : "s")}", "Wheels") : ("tracks", "Drive"),
            ($"{ud.BuildSeconds} s", "Build time"),
        };
        var rows = cost.Select(c => new ItemNeed(c.ResourceId, c.Name, c.Amount, c.Have, c.Short, false)).ToList();
        var here = bay.Production.Count(q => q == u);
        var rate = RateOf(bay, def);
        var notes = new List<(string, Color)>();
        if (here > 0) notes.Add(($"{here} in this bay's queue", HudKit.Dim));
        var times = $"This bay: {Surface.LevelName(bay)}" + (rate > 0 ? (Mathf.IsEqualApprox(rate, 1f) ? "  ·  full speed" : $"  ·  {Mathf.CeilToInt(ud.BuildSeconds / rate)} s at its speed now") : "  ·  not working right now");
        var buttons = new List<CardButton>
        {
            new("BUILD ONE  [ENTER]", 1, problem == null, true, "Order one: paid now, refunded in full if you cancel"),
            new("BUILD 5  [SHIFT+ENTER]", 5, problem == null, false, "Order up to five (as many as the queue and your stock allow)"),
        };
        return new PanelCard($"unit:{u}", $"vehicle:{u}", true, VehicleName(u),
            $"{VehicleKind(ud)}  ·  " + (key.Length > 0 ? $"hotkey {key}" : "no hotkey") + $"  ·  assembles in {ud.BuildSeconds} s", times, BayColour,
            tone, text, "", System.Array.Empty<CardLink>(), stats, "COSTS", rows, "", System.Array.Empty<string>(), ud.Description, notes, buttons,
            "On the vehicle tiles: click = this card · Ctrl+click = order 1 · Shift+click = order 5 · right-click = cancel one · number keys order 1 (Shift 5) · Backspace cancels the last order");
    }

    private PanelCard TechCard(TechDef t)
    {
        var me = Surface.Players[LocalPlayer];
        var done = me.Known[t.Index];
        var active = me.ActiveTech == t.Index;
        var progress = me.ResearchProgress[t.Index];
        var missing = t.Requires.Where(r => !me.Known[_data.Tech(r).Index]).Select(r => _data.Tech(r)).ToList();
        var locked = !done && missing.Count > 0;
        var rate = Surface.ResearchPerMinute(LocalPlayer).ToFloat();
        var left = (t.Cost - progress).ToFloat();
        var time = rate > 0 ? Clock(left / rate * 60f) : "";
        CardTone tone;
        string text;
        var links = new List<CardLink>();
        if (done) (tone, text) = (CardTone.Good, "Researched. Its effects are on for everything it touches.");
        else if (active) (tone, text) = (CardTone.Busy, $"Researching now: {Hud.Whole(progress)} of {Hud.Whole(t.Cost)} points  ·  " + (rate > 0 ? $"{time} left." : "the labs aren't working right now."));
        else if (locked)
        {
            (tone, text) = (CardTone.Bad, "Locked. Research these first:");
            links.AddRange(missing.Select(m => new CardLink($"tech:{m.Id}", m.Name, true)));
        }
        else
        {
            tone = CardTone.Good;
            text = $"Can research now. {Hud.Whole(t.Cost)} points" + (progress > Fix64.Zero ? $" ({Hud.Whole(progress)} done already)" : "")
                   + (rate > 0 ? $"  ·  about {time} at your labs' speed ({rate:0.#} points a minute)." : "  ·  your labs aren't working right now.")
                   + (me.ActiveTech >= 0 ? $" Progress on {_data.Techs[me.ActiveTech].Name} is kept if you switch." : "");
        }
        var needs = t.Requires.Count > 0 ? "  ·  needs " + JoinAnd(t.Requires.Select(r => _data.Tech(r).Name).ToList()) : "";
        var key = t.Index < 9 ? $"  ·  key {t.Index + 1}" : "";
        var notes = new List<(string, Color)>();
        var leadsTo = _data.Techs.Where(o => o.Requires.Contains(t.Id)).Select(o => o.Name).ToList();
        if (leadsTo.Count > 0) notes.Add(($"Leads to {JoinAnd(leadsTo)}.", HudKit.Dim));
        var buttons = new List<CardButton>();
        if (!done)
            buttons.Add(new CardButton(active ? "RESEARCHING" : locked ? "LOCKED" : "RESEARCH THIS  [ENTER]", 1, !locked && !active, true,
                active ? "This lab is on it already" : locked ? "Research its prerequisites first" : "Switch your labs to this tech"));
        return new PanelCard($"tech:{t.Id}", $"tech:{t.Id}", true, t.Name,
            $"Surface research  ·  {Hud.Whole(t.Cost)} points{needs}{(t.Optional ? "  ·  situational (ruins)" : "")}{key}", "", LabColour,
            tone, text, "", links, System.Array.Empty<(string, string)>(), "", System.Array.Empty<ItemNeed>(),
            "What it does", TechEffects(t), t.Description, notes, buttons,
            "On the tech tree: click = this card · Ctrl+click = research it · number keys open a tech's card (the same key again researches it)");
    }

    // ---------------------------------------------------------------- clicks and keys

    /// <summary>A tile on the bay's Vehicles tab or the lab's tree: 0 = its card, 1 / 5 = order (research), -1 = cancel one.</summary>
    private void OnBayLabTile(string id, int mode)
    {
        if (SelectedBay() is { } bay)
        {
            if (mode == 0) _panelCard = $"unit:{id}";
            else if (mode > 0) OrderUnits(bay, id, mode);
            else CancelOneOf(bay, id);
            return;
        }
        if (SelectedLab() == null) return;
        if (id.StartsWith(SharedPrefix))
        {
            _info = _info == id ? null : id; // a shared tech keeps its compact card
            return;
        }
        if (mode == 0) _panelCard = $"tech:{id}";
        else if (mode > 0) Research(id);
    }

    /// <summary>Orders up to n of a vehicle (as many as the queue has room for); the sim refuses what can't be paid.</summary>
    private void OrderUnits(SurfaceStructure bay, string unitId, int n)
    {
        var problem = Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, unitId);
        if (problem != null)
        {
            _hud.PushEvent($"Can't order a {VehicleName(unitId)}: {problem}", PartsWarn);
            return;
        }
        var room = _data.Building(bay.DefId).ProductionQueue - bay.Production.Count;
        var cost = _data.SurfaceUnit(unitId).Cost;
        var stock = _sim.Players[LocalPlayer].Stock;
        var afford = 0;
        while (afford < n && afford < room && cost.All(c => stock[c.Resource] >= c.Amount * Fix64.FromInt(afford + 1))) afford++;
        for (var i = 0; i < afford; i++) _sim.Schedule(_sim.Tick + InputDelayTicks, new BuildUnitCommand(LocalPlayer, bay.Id, unitId));
        if (afford < n && n > 1) _hud.PushEvent($"Ordered {afford} of {n} {Plural(VehicleName(unitId))}: " + (afford >= room ? "the queue is full" : "not enough resources for more"), PartsWarn);
    }

    /// <summary>× on a queue row (1-based): removes that order with a full refund (see the file comment).</summary>
    private void CancelBayRow(SurfaceStructure bay, int row)
    {
        var queue = bay.Production.ToList();
        var index = row - 1;
        if (index < 0 || index >= queue.Count) return;
        var tick = _sim.Tick + InputDelayTicks;
        for (var k = queue.Count - 1; k >= index; k--) _sim.Schedule(tick, new CancelUnitCommand(LocalPlayer, bay.Id));
        for (var k = index + 1; k < queue.Count; k++) _sim.Schedule(tick, new BuildUnitCommand(LocalPlayer, bay.Id, queue[k]));
        Sfx.Ui("click");
    }

    /// <summary>Right-click on a vehicle tile: the last queued order of that vehicle.</summary>
    private void CancelOneOf(SurfaceStructure bay, string unitId)
    {
        var queue = bay.Production.ToList();
        var last = queue.LastIndexOf(unitId);
        if (last < 0) _hud.PushEvent($"No {VehicleName(unitId)} queued here", PartsWarn);
        else CancelBayRow(bay, last + 1);
    }

    /// <summary>A number key on a selected bay (order one, Shift five) or lab (its card; the same key again researches).</summary>
    private void BayLabKey(int index, bool shift)
    {
        if (SelectedBay() is { } bay)
        {
            var produces = _data.Building(bay.DefId).Produces;
            if (index < produces.Count) OrderUnits(bay, produces[index], shift ? 5 : 1);
            return;
        }
        if (SelectedLab() == null || index >= _data.Techs.Count) return;
        var id = _data.Techs[index].Id;
        if (_panelCard == $"tech:{id}") Research(id);
        else _panelCard = $"tech:{id}";
    }

    /// <summary>The card's button (or Enter / Shift+Enter): order 1 / 5 of the vehicle, or research the tech.</summary>
    private void BayLabCardAction(int n)
    {
        if (_panelCard == null) return;
        if (_panelCard.StartsWith("unit:") && SelectedBay() is { } bay) OrderUnits(bay, _panelCard[5..], n);
        else if (_panelCard.StartsWith("tech:")) Research(_panelCard[5..]);
    }
}
