using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Founding a second outpost on the ground with a construction rover (rules in sim/Surface/Founding.cs). Selecting the
// rover swaps the command card to FOUND OUTPOST (like a Vehicle Bay swaps in its orders); picking it (or F / 1) arms the
// site pick: a ghost of the Level 1 outpost follows the mouse, green or red with the reason in the tooltip and on the
// info card, the would-be base's build area shows around a good site and a ring marks how close to your other colony
// cores you may not go. A click sends the rover (FoundOutpostCommand); events follow it there, through the unfolding,
// the founding and the finished base.
public partial class PlanetDriver
{
    /// <summary>The command-card button and info card id for founding.</summary>
    private const string FoundCard = "found:outpost";

    /// <summary>The construction rover whose site is being picked (0 = not picking).</summary>
    private int _founding;

    private int _foundHover = -2;

    private readonly record struct FounderSeen(long Failed, bool Unfolding, int Tile);

    private readonly Dictionary<int, FounderSeen> _seenFounders = new();
    private readonly Dictionary<int, (string Name, bool Built)> _foundedCores = new();

    private static readonly Color FoundBlue = new(0.62f, 0.82f, 1f);

    private bool IsFounder(SurfaceUnit u) => _data.SurfaceUnit(u.DefId).CanFound;

    /// <summary>Our construction rover when the selection is only construction rovers (the first by id), else null.</summary>
    private SurfaceUnit? SelectedFounder()
    {
        if (_view.SelectedUnits.Count == 0) return null;
        SurfaceUnit? first = null;
        foreach (var id in _view.SelectedUnits.OrderBy(i => i))
        {
            var u = Surface.FindUnit(id);
            if (u == null) continue;
            if (u.Owner != LocalPlayer || !IsFounder(u)) return null;
            first ??= u;
        }
        return first;
    }

    /// <summary>FOUND OUTPOST (the card, F or 1): start picking a site for the selected construction rover, or stop.</summary>
    private void ArmFounding()
    {
        var rover = SelectedFounder();
        if (rover == null || Me.Phase != LandingPhase.Landed) return;
        if (_founding == rover.Id)
        {
            StopFounding();
            return;
        }
        if (Surface.Disabled(rover))
        {
            _hud.PushEvent($"{UnitName(rover)} can't found an outpost: {Surface.FoundText(FoundProblem.Disabled)}", RoverWarn);
            return;
        }
        if (_placing != null) StopPlacing();
        _founding = rover.Id;
        _info = FoundCard;
        _foundHover = -2;
        var rules = Surface.FoundRulesOf(rover)!;
        _view.ShowSpacing(Surface.Bases(LocalPlayer).Select(b => b.Tile).ToList(), rules.MinDistance);
    }

    private void StopFounding()
    {
        if (_founding == 0) return;
        _founding = 0;
        if (_info == FoundCard) _info = null;
        _view.SetGhost(null, -1, 0, false);
        _view.HideSpacing();
        _view.HideBuildRange();
    }

    /// <summary>F: a selected construction rover picks a site (true if it handled the key).</summary>
    private bool FoundKey()
    {
        if (SelectedFounder() == null) return false;
        ArmFounding();
        return true;
    }

    private FoundProblem HoverProblem(SurfaceUnit rover, int tile) =>
        tile < 0 ? FoundProblem.OffMap : Surface.FoundOutpostProblem(_sim, LocalPlayer, rover.Id, tile);

    /// <summary>While picking: the outpost ghost on the hovered tile and, on a good site, the new base's build area.</summary>
    private void FoundingHover()
    {
        var rover = Surface.FindUnit(_founding);
        if (rover == null || !_view.SelectedUnits.Contains(_founding))
        {
            StopFounding();
            return;
        }
        var rules = Surface.FoundRulesOf(rover)!;
        var ok = _hoverTile >= 0 && HoverProblem(rover, _hoverTile) == FoundProblem.None;
        _view.SetGhost(rules.Building, _hoverTile, 0, ok);
        _view.SetHover(-1, Colors.White);
        var show = ok ? _hoverTile : -1;
        if (show == _foundHover) return; // the range mesh is rebuilt only when the site changes
        _foundHover = show;
        if (show >= 0) _view.ShowBuildRange(show, _data.Building(rules.Building).BuildRadius);
        else _view.HideBuildRange();
    }

    /// <summary>Click while picking: send the rover if the site is good, otherwise say why. Shift keeps picking.</summary>
    private void FoundClick(bool shift)
    {
        var rover = Surface.FindUnit(_founding);
        if (rover == null || _hoverTile < 0) return;
        var rules = Surface.FoundRulesOf(rover)!;
        var problem = HoverProblem(rover, _hoverTile);
        if (problem != FoundProblem.None)
        {
            _hud.PushEvent($"Can't found an outpost there: {Surface.FoundText(problem, _hoverTile, rules.MinDistance)}", RoverWarn);
            return;
        }
        if (rover.Tile != _hoverTile && Pathfinder.Find(Surface.Map, rover.Tile, _hoverTile) == null)
        {
            _hud.PushEvent("No route there: mountains or water are in the way", RoverWarn);
            return;
        }
        _sim.Schedule(_sim.Tick + InputDelayTicks, new FoundOutpostCommand(LocalPlayer, rover.Id, _hoverTile));
        var tiles = Surface.Map.Grid.Distance(rover.Tile, _hoverTile);
        _hud.PushEvent(tiles == 0 ? $"{UnitName(rover)} unfolding here to found an outpost"
            : $"{UnitName(rover)} heading out to found an outpost ({tiles} tiles): {Amounts(rules.Cost, false)} is paid when it gets there", FoundBlue);
        if (!shift) StopFounding();
    }

    /// <summary>The command card while a construction rover is selected: one FOUND OUTPOST button.</summary>
    private void ShowFoundCommands(SurfaceUnit rover)
    {
        var rules = Surface.FoundRulesOf(rover)!;
        var stock = _sim.Players[LocalPlayer].Stock;
        var tag = Surface.Unfolding(rover) ? $"{Mathf.RoundToInt(Surface.DeployFraction(rover).ToFloat() * 100)}%" : rover.FoundTile >= 0 ? "EN ROUTE" : "";
        _hud.ShowCommands($"{TypeName(rover).ToUpperInvariant()} · {CallSign(rover).ToUpperInvariant()}",
            new[] { new CommandView(FoundCard, "[F]", "Found Outpost", Amounts(rules.Cost, false, shortNames: true), stock.CanAfford(rules.Cost), _founding == rover.Id, tag, Surface.Disabled(rover)) },
            Me.Phase == LandingPhase.Landed);
    }

    /// <summary>The info card while picking a site: what founding does, costs and needs, and why the hovered tile won't do.</summary>
    private InfoView FoundInfo()
    {
        var rover = Surface.FindUnit(_founding);
        var unit = rover != null ? _data.SurfaceUnit(rover.DefId) : _data.SurfaceUnits.First(u => u.CanFound);
        var rules = unit.Found!;
        var hub = _data.Building(rules.Building);
        var stock = _sim.Players[LocalPlayer].Stock;
        var rows = new List<(string, string)>
        {
            ("Site", "surveyed, buildable ground (plains, desert, hills) with no building on it or next to it; no toxic ground, predators or native land"),
            ("Spacing", $"at least {rules.MinDistance} tiles from your other colony cores (the red ring); an outpost dropped from orbit needs {_data.Orbit?.OutpostMinDistance ?? rules.MinDistance}"),
            ("Founding", $"the rover drives there, pays {Amounts(rules.Cost, false)} and unfolds in {unit.DeploySeconds} s, then it is used up"),
            ("Builds", $"a {hub.Name}: its exocomps build it in {hub.BuildSeconds} s, then it is a new base with {hub.BuildQueues} build queue, radius {hub.BuildRadius}, +{Hud.Whole(hub.Storage)} storage and {Hud.Whole(hub.PowerSupply)} power"),
            ("Cancel", "X on the unfinished hub refunds the founding cost and the rover rolls back out; a move order while it unfolds folds it up (refunded)"),
        };
        var footer = "Click a site to send the rover  ·  Shift+click keep picking  ·  right-click / Esc stop";
        if (rover != null && _hoverTile >= 0 && HoverProblem(rover, _hoverTile) is var p and not FoundProblem.None)
            footer = $"Can't found an outpost on this tile: {Surface.FoundText(p, _hoverTile, rules.MinDistance)}\n" + footer;
        return new InfoView("colony_hub", "Found Outpost", rover != null ? $"{UnitName(rover)}  ·  a second base on the ground" : "Construction rover",
            $"Cost  {Amounts(rules.Cost, false)}   ·   unfolds {unit.DeploySeconds} s   ·   {hub.BuildSeconds} s to build",
            stock.CanAfford(rules.Cost),
            "Found a new base without the ship: the construction rover carries a folded Colony Hub and its own exocomps. " +
            "Pick a site well away from your other bases; the rover parks, unfolds its crane, sets the prefab panels down and its " +
            "exocomps build the hub around it. The new base gets its own build queue, power and storage, and shows up in the base tabs (Tab).",
            rows, -1f, "", "", false, footer);
    }

    /// <summary>Tooltip while picking a site: the tile, then the founding cost and whether it can go here.</summary>
    private string FoundTooltip(int tile)
    {
        var rover = Surface.FindUnit(_founding);
        if (rover == null) return DescribeTile(tile);
        var rules = Surface.FoundRulesOf(rover)!;
        var sb = new StringBuilder();
        sb.Append(Surface.Map.Terrain(tile).Name).Append("\nFound an outpost · ").Append(Amounts(rules.Cost, false));
        var nearest = Surface.Bases(LocalPlayer).Select(b => Surface.Map.Grid.Distance(b.Tile, tile)).DefaultIfEmpty(-1).Min();
        if (nearest >= 0) sb.Append("\n").Append(nearest).Append(" tiles from your nearest base (at least ").Append(rules.MinDistance).Append(')');
        var problem = HoverProblem(rover, tile);
        sb.Append(problem == FoundProblem.None ? "\nClick to found the outpost here" : "\nCan't found here: " + Surface.FoundText(problem, tile, rules.MinDistance));
        return sb.ToString();
    }

    /// <summary>The rover panel's status while founding, or null.</summary>
    private (string, Color)? FounderStatus(SurfaceUnit u)
    {
        if (!IsFounder(u)) return null;
        if (Surface.Unfolding(u)) return ($"Unfolding to found an outpost: {Mathf.RoundToInt(Surface.DeployFraction(u).ToFloat() * 100)}%", FoundBlue);
        if (u.Mode == UnitMode.Packing) return ("Folding up", new Color(0.6f, 0.65f, 0.75f));
        if (u.FoundTile >= 0) return ($"Driving to found an outpost ({Surface.Map.Grid.Distance(u.Tile, u.FoundTile)} tiles)", FoundBlue);
        return null;
    }

    /// <summary>Events for founding: a site refused on arrival, unfolding, called off, founded, built, cancelled.</summary>
    private void FounderEvents()
    {
        foreach (var u in Surface.Units)
        {
            if (u.Owner != LocalPlayer || !IsFounder(u)) continue;
            var now = new FounderSeen(u.FoundFailedTick, Surface.Unfolding(u), u.Tile);
            if (_seenFounders.TryGetValue(u.Id, out var was))
            {
                var rules = Surface.FoundRulesOf(u)!;
                if (now.Failed != was.Failed && u.FoundFailedTick >= 0)
                    _hud.PushEvent($"{UnitName(u)} couldn't found an outpost: {Surface.FoundText(u.FoundFailure, u.Tile, rules.MinDistance)}", RoverWarn);
                else if (now.Unfolding && !was.Unfolding)
                    _hud.PushEvent($"{UnitName(u)} unfolding at the site ({Amounts(rules.Cost, false)} paid)", FoundBlue);
                else if (!now.Unfolding && was.Unfolding)
                    _hud.PushEvent($"{UnitName(u)} folded up: founding called off (refunded)", RoverWarn);
            }
            _seenFounders[u.Id] = now;
        }

        var live = new HashSet<int>();
        foreach (var s in Surface.Structures)
        {
            if (s.Owner != LocalPlayer || s.Founder is not { } rover) continue;
            live.Add(s.Id);
            _seenFounders.Remove(rover.Id); // used up: if the hub is cancelled it comes back as a fresh sighting (no "folded up" line)
            var built = s.State == StructureState.Complete;
            if (!_foundedCores.TryGetValue(s.Id, out var seen))
            {
                if (!built)
                    _hud.PushEvent($"{UnitName(rover)} founded {BaseName(s.Id)}: its exocomps are building the {_data.Building(s.DefId).Name} ({_data.Building(s.DefId).BuildSeconds} s)", PlanetHud.Accent);
                _foundedCores[s.Id] = (UnitName(rover), built);
                continue;
            }
            if (built && !seen.Built)
            {
                _hud.PushEvent($"{BaseName(s.Id)} is up: the new {_data.Building(s.DefId).Name} is working (Tab to switch bases)", FoundBlue);
                _foundedCores[s.Id] = (seen.Name, true);
            }
        }
        foreach (var id in _foundedCores.Keys.Where(id => !live.Contains(id)).ToList())
        {
            if (!_foundedCores[id].Built) _hud.PushEvent($"Outpost cancelled: {_foundedCores[id].Name} folds up and rolls back out (refunded)", RoverWarn);
            _foundedCores.Remove(id);
        }
    }

    /// <summary>The base panel line for a core founded on the ground that the exocomps are still building, or null.</summary>
    private string? FoundedBaseLine(SurfaceStructure core)
    {
        if (!core.Founded || core.State == StructureState.Complete) return null;
        var pct = Mathf.RoundToInt(Surface.BuildFraction(core).ToFloat() * 100);
        return (core.State == StructureState.Queued ? "Exocomps launching" : $"Exocomps building the hub: {pct}%") +
            " · no power, storage, build range or repair pad yet\nX cancels it: the founding cost comes back and the construction rover rolls back out";
    }
}
