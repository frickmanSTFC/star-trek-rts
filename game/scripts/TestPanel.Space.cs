using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// The SPACE tab (space jobs phase 1, docs/space-jobs-contract.md "VIEW"): every DebugSpaceField for the local player's
// flagship and knowledge — spawn a site of any kind (and variant) next to the flagship, identify or remove a site,
// set the hull (slider + presets), knock out or fix a ship system, lose / add / injure crew per division, set a hold
// item's count, finish the running away team (rolled, or a set outcome). Every edit is a DebugSpaceCommand through
// Apply (Simulation.DebugProblem first); hull, crew and hold edits undo from the value before the edit. The panel
// opens on this tab in orbit for a ship that never lands (F9 works there; PlanetDriver.cs).
public partial class TestPanel
{
    private int _spaceKind = (int)SiteKind.Derelict;
    private int _spaceVariant = -1;
    private int _spaceSite;
    private string _spaceItem = "patch_kit";
    private int _spaceCount = 1;

    /// <summary>Switch to the SPACE tab (the driver opens the panel there in orbit).</summary>
    public void ShowSpaceTab() => ShowTab(Array.IndexOf(TabNames, "SPACE"));

    private DebugSpaceCommand SpaceCommand(DebugSpaceField field, string? id, int value) => new(Me, Me, field, id, value);

    private string SpaceShape()
    {
        var sys = Sim.System;
        var status = sys?.StatusOf(Me);
        return $"space|{sys != null}|{sys?.Sites.Count}|{_spaceSite}|{status != null}|{status?.Crew.Count}|{status?.Hold.Count}|{sys?.AwayTeamOf(Me) is { Done: false }}";
    }

    private void BuildSpace()
    {
        var sys = Sim.System;
        if (sys == null)
        {
            _content.AddChild(Wrapped("No star system in this game (a planet-only landing): space sites, hull and crew belong to the star system's flagship.", 13, Dim));
            return;
        }
        var state = Wrapped("", 13, Soft);
        _content.AddChild(state);
        _refreshers.Add(() =>
        {
            var s = sys.StatusOf(Me);
            var f = sys.FlagshipOf(Me);
            var at = f == null ? "no flagship" : sys.ShipSite(f) is { } site ? $"holding at {site}" : f.AtBody != null ? $"in orbit of {f.AtBody}" : sys.SiteBound(f) is { } bound ? $"flying to {bound}" : "under way";
            state.Text = s == null
                ? $"{f?.Name ?? "Flagship"}: {at} · no ship status yet (the sim's StatusOf is empty)"
                : $"{f?.Name ?? "Flagship"}: {at} · hull {s.Hull.ToFloat():0}% ({s.Band}) · crew {s.CrewTotal}/{s.CrewStart} · hold {s.HoldUsed}/{s.HoldSize} · {s.Down.Count} system(s) down"
                  + (sys.AwayTeamOf(Me) is { } ev ? $" · away team {(ev.Done ? "done" : $"step {ev.Step + 1}/{ev.Steps}")}" : "");
        });

        // ---- sites
        _content.AddChild(Section("SITES"));
        var kinds = Enum.GetValues<SiteKind>();
        var spawnRow = Row();
        var kindPick = MakeOption(kinds.Select(k => k.ToString()).ToArray(), _spaceKind, i => _spaceKind = i);
        kindPick.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spawnRow.AddChild(kindPick);
        var variantPick = MakeOption(new[] { "variant: random", "variant 0", "variant 1", "variant 2", "variant 3" }, _spaceVariant + 1, i => _spaceVariant = i - 1);
        variantPick.TooltipText = "In the kind's data order. Asteroid: 0 common, 1 metal, 2 ice, 3 hollow. Known derelict: 0 earth, 1 trader, 2 native.";
        spawnRow.AddChild(variantPick);
        _content.AddChild(spawnRow);
        _content.AddChild(MakeButton("Spawn next to the flagship", Green, () =>
        {
            var kind = kinds[Math.Clamp(_spaceKind, 0, kinds.Length - 1)].ToString().ToLowerInvariant();
            Apply($"space: spawned a {kind} site", new DebugCommand[] { SpaceCommand(DebugSpaceField.SpawnSite, kind, _spaceVariant) }, null);
        }, 0, 34));

        // ---- events (phase 3): each arrives now, as the system's clock would bring it (placed by its own hash, announced)
        _content.AddChild(Section("EVENTS"));
        var clock = Wrapped("", 12, Soft);
        _content.AddChild(clock);
        _refreshers.Add(() =>
        {
            var next = sys.NextEventTick;
            var seconds = next < 0 ? -1 : Math.Max(0, (next - Sim.Tick) / Simulation.TickRate);
            clock.Text = (next < 0 ? "No events in this data set." : $"Next event turn in {seconds / 60}:{seconds % 60:00}") + $" · {sys.LiveEvents()} event(s) running";
        });
        var events = Grid(3);
        foreach (var (id, label) in new[] { ("rogue_asteroid", "Rogue rock"), ("comet", "Comet"), ("lifepods", "Lifepods"), ("stranded_ship", "Stranded ship"), ("anomaly", "Ion storm") })
            events.AddChild(MakeButton(label, id is "lifepods" or "stranded_ship" ? Red : Yellow, () =>
                Apply($"space: a {label.ToLowerInvariant()} event now", new DebugCommand[] { SpaceCommand(DebugSpaceField.SpawnEvent, id, -1) }, null), 0, 30));
        _content.AddChild(events);

        // ---- chain-only sites (phase 4): a ship running dark (hunted with long-range scans: its search area shows; Identify
        // for me below skips the hunt) and an alien probe that follows our flagship
        _content.AddChild(Section("CHAIN SITES"));
        var chain = Grid(2);
        foreach (var (id, label) in new[] { ("hidden_ship", "Ship running dark"), ("alien_probe", "Alien probe") })
            chain.AddChild(MakeButton(label, Yellow, () =>
                Apply($"space: spawned a {label.ToLowerInvariant()}", new DebugCommand[] { SpaceCommand(DebugSpaceField.SpawnSite, id, -1) }, null), 0, 30));
        _content.AddChild(chain);

        var sites = sys.Sites;
        if (sites.Count == 0) _content.AddChild(Wrapped("No sites in this system yet.", 12, Dim));
        else
        {
            _spaceSite = Math.Clamp(_spaceSite, 0, sites.Count - 1);
            _content.AddChild(MakeOption(sites.Select(s => $"{s.Id} · {s.Kind}{(s.Variant.Length > 0 ? $" ({s.Variant})" : "")} · {sys.SiteKnowledgeOf(Me, s.Id).ToString().ToLowerInvariant()}{(s.Name.Length > 0 ? $" · {s.Name}" : "")}").ToArray(),
                _spaceSite, i => _spaceSite = i));
            var picked = sites[_spaceSite].Id;
            var siteGrid = Grid(2);
            siteGrid.AddChild(MakeButton("Identify for me", Blue, () =>
                Apply($"space: identified {picked}", new DebugCommand[] { SpaceCommand(DebugSpaceField.IdentifySite, picked, 0) }, null), 0, 34));
            siteGrid.AddChild(MakeButton("Remove", Red, () =>
                Apply($"space: removed {picked}", new DebugCommand[] { SpaceCommand(DebugSpaceField.RemoveSite, picked, 0) }, null), 0, 34));
            _content.AddChild(siteGrid);
        }

        // ---- hull
        _content.AddChild(Section("HULL"));
        var hullHead = Row();
        hullHead.AddChild(Caption("INTEGRITY"));
        var hullValue = Text("", 20, Ink);
        hullValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hullValue.HorizontalAlignment = HorizontalAlignment.Right;
        hullHead.AddChild(hullValue);
        _content.AddChild(hullHead);
        var hullSlider = MakeSlider(0, 100, v => SetHull((int)v));
        _content.AddChild(hullSlider);
        _refreshers.Add(() =>
        {
            var s = sys.StatusOf(Me);
            hullValue.Text = s == null ? "—" : $"{s.Hull.ToFloat():0}%";
            if (s != null && !hullSlider.HasMeta("dragging")) hullSlider.SetValueNoSignal(s.Hull.ToFloat());
        });
        var presets = Grid(5);
        foreach (var v in new[] { 100, 75, 50, 25, 0 })
            presets.AddChild(MakeButton($"{v}%", v >= 75 ? Green : v >= 50 ? Blue : v >= 25 ? Yellow : Red, () => SetHull(v), 0, 30));
        _content.AddChild(presets);

        // ---- systems
        _content.AddChild(Section("SYSTEMS"));
        var systems = Grid(4);
        foreach (var system in Enum.GetValues<ShipSystem>())
        {
            var id = system.ToString().ToLowerInvariant();
            systems.AddChild(Text(system == ShipSystem.LifeSupport ? "Life support" : system.ToString(), 14, Ink));
            systems.AddChild(MakeButton("Out 60 s", Yellow, () => Apply($"space: {id} out 60 s", new DebugCommand[] { SpaceCommand(DebugSpaceField.SystemDown, id, 60) }, null), 0, 30));
            systems.AddChild(MakeButton("Out 180 s", Red, () => Apply($"space: {id} out 180 s", new DebugCommand[] { SpaceCommand(DebugSpaceField.SystemDown, id, 180) }, null), 0, 30));
            systems.AddChild(MakeButton("Fix", Green, () => Apply($"space: {id} fixed", new DebugCommand[] { SpaceCommand(DebugSpaceField.SystemDown, id, 0) }, null), 0, 30));
        }
        _content.AddChild(systems);

        // ---- crew
        _content.AddChild(Section("CREW"));
        var divisions = Data.FitOut?.Divisions ?? (IReadOnlyList<FinalFrontier.Sim.Data.FitOutDivision>)Array.Empty<FinalFrontier.Sim.Data.FitOutDivision>();
        if (divisions.Count == 0) _content.AddChild(Wrapped("No crew divisions in the data (fit_out.json).", 12, Dim));
        var crewGrid = Grid(4);
        foreach (var d in divisions)
        {
            var id = d.Id;
            var label = Text(d.Name, 14, Ink);
            crewGrid.AddChild(label);
            _refreshers.Add(() =>
            {
                var line = sys.StatusOf(Me)?.Crew.FirstOrDefault(c => string.Equals(c.Division, id, StringComparison.OrdinalIgnoreCase));
                label.Text = line == null ? d.Name : $"{d.Name} {line.OnDuty} (+{line.Injured} inj, {line.Lost} lost)";
            });
            crewGrid.AddChild(MakeButton("Lose 1", Red, () =>
                Apply($"space: lost 1 {id}", SpaceCommand(DebugSpaceField.Crew, id, -1), SpaceCommand(DebugSpaceField.Crew, id, 1)), 0, 30));
            crewGrid.AddChild(MakeButton("Add 1", Green, () =>
                Apply($"space: added 1 {id}", SpaceCommand(DebugSpaceField.Crew, id, 1), SpaceCommand(DebugSpaceField.Crew, id, -1)), 0, 30));
            crewGrid.AddChild(MakeButton("Injure 1", Yellow, () =>
                Apply($"space: injured 1 {id}", new DebugCommand[] { SpaceCommand(DebugSpaceField.Injure, id, 1) }, null), 0, 30));
        }
        _content.AddChild(crewGrid);

        // ---- hold
        _content.AddChild(Section("HOLD"));
        var status = sys.StatusOf(Me);
        if (status != null)
            foreach (var h in status.Hold)
            {
                if (h.Item == StarSystemState.TowItem) continue; // a tow isn't a count (release or drop it from the status screen)
                var item = h.Item;
                var row = Row();
                var name = Text($"{h.Label} ({item})", 13, Ink);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                name.ClipText = true;
                row.AddChild(name);
                var count = Text(h.Count.ToString(), 14, Accent, _caps);
                row.AddChild(count);
                _refreshers.Add(() =>
                {
                    var now = sys.StatusOf(Me)?.Hold.FirstOrDefault(x => x.Item == item)?.Count ?? 0;
                    count.Text = now.ToString();
                });
                row.AddChild(MakeButton("−1", Plain, () => SetHold(item, -1, true), 40, 28));
                row.AddChild(MakeButton("+1", Plain, () => SetHold(item, 1, true), 40, 28));
                _content.AddChild(row);
            }
        var setRow = Row();
        var itemField = MakeField(text => _spaceItem = text.Trim());
        itemField.Text = _spaceItem;
        itemField.PlaceholderText = "item id";
        itemField.Alignment = HorizontalAlignment.Left;
        itemField.TextChanged += text => _spaceItem = text.Trim();
        itemField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        setRow.AddChild(itemField);
        var countField = MakeField(text => { if (int.TryParse(text, out var n)) _spaceCount = Math.Max(0, n); });
        countField.Text = _spaceCount.ToString();
        countField.TextChanged += text => { if (int.TryParse(text, out var n)) _spaceCount = Math.Max(0, n); };
        countField.CustomMinimumSize = new Vector2(60, 30);
        setRow.AddChild(countField);
        setRow.AddChild(MakeButton("Set", Blue, () => SetHold(_spaceItem, _spaceCount, false), 50, 30));
        _content.AddChild(setRow);
        _content.AddChild(Wrapped("Item ids as the hold lists them: patch_kit, probe, ore:duranium_ore, salvage:<part>, survivors…", 12, Dim));
        // Phase 2: ore by the container (30 a container).
        var oreRow = Grid(3);
        foreach (var (ore, label) in new[] { ("ore:duranium_ore", "+30 duranium ore"), ("ore:tritanium_ore", "+30 tritanium ore"), ("ore:deuterium", "+30 ice (deut.)") })
        {
            var item = ore;
            oreRow.AddChild(MakeButton(label, Plain, () => SetHold(item, Sim.Data.SpaceSites?.OrePerContainer ?? 30, true), 0, 28));
        }
        _content.AddChild(oreRow);

        // ---- away team
        _content.AddChild(Section("AWAY TEAM"));
        var away = Wrapped("", 13, Soft);
        _content.AddChild(away);
        _refreshers.Add(() =>
        {
            var ev = sys.AwayTeamOf(Me);
            away.Text = ev == null ? "No away team across." : ev.Done ? $"Back from {ev.SiteId}: {ev.Outcome}" : $"At {ev.SiteId}: step {ev.Step + 1} of {ev.Steps} · injury {ev.InjuryPct} % · loss {ev.LossPct} % · find {ev.FindPct} %";
        });
        var finish = Grid(5);
        finish.AddChild(MakeButton("Finish: roll", Blue, () => Apply("space: finished the away team (rolled)", new DebugCommand[] { SpaceCommand(DebugSpaceField.FinishAwayTeam, null, -1) }, null), 0, 30));
        for (var i = 0; i < 4; i++)
        {
            var outcome = i;
            finish.AddChild(MakeButton($"Outcome {i}", i == 0 ? Green : i == 1 ? Yellow : Red, () =>
                Apply($"space: finished the away team (outcome {outcome})", new DebugCommand[] { SpaceCommand(DebugSpaceField.FinishAwayTeam, null, outcome) }, null), 0, 30));
        }
        _content.AddChild(finish);
        _content.AddChild(Wrapped("Outcome indexes follow the site's table in ship_status.json (0 = the first, usually clean).", 12, Dim));
    }

    private void SetHull(int value)
    {
        var before = Sim.System?.StatusOf(Me)?.Hull.ToFloat();
        var command = SpaceCommand(DebugSpaceField.Hull, null, Math.Clamp(value, 0, 100));
        if (before is { } b) Apply($"space: hull {value}%", command, SpaceCommand(DebugSpaceField.Hull, null, Mathf.RoundToInt(b)));
        else Apply($"space: hull {value}%", new DebugCommand[] { command }, null);
    }

    /// <summary>Set (or with <paramref name="relative"/>, change by) a hold item's count; undo puts the old count back.</summary>
    private void SetHold(string item, int value, bool relative)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            _host.Notify("Test panel: type an item id first", true);
            return;
        }
        var before = Sim.System?.StatusOf(Me)?.Hold.FirstOrDefault(h => h.Item == item)?.Count ?? 0;
        var next = Math.Max(0, relative ? before + value : value);
        Apply($"space: hold {item} = {next}", SpaceCommand(DebugSpaceField.HoldItem, item, next), SpaceCommand(DebugSpaceField.HoldItem, item, before));
    }
}
