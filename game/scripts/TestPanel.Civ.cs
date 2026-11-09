using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// The CIV tab (civilizations phase 2, docs/civilizations-phase2-contract.md "CIV test tab"): pick a body of the star
// system, then spawn a living civilization (level 1–6) or ruins there, or remove its site; set its level, awareness and
// temperament; finish the local player's study; set the local player's Starfleet Standing; force their observation
// post to be found; set relations 0–4. A RUINS row adds or removes ruins on a picked tile of the colony's own map. Every
// edit goes through DebugCivCommand / DebugRuinCommand with Simulation.DebugProblem first (Apply), undo where the old
// value is known, and counts for TEST MODE (Simulation.DebugEdits). Without a star system only the ruins row works.
public partial class TestPanel
{
    private int _civBody;
    private int _civSpawnLevel = 3;

    private StarSystemState? Civ => Sim.System;

    /// <summary>The bodies the picker lists (the system's, in order), or none.</summary>
    private IReadOnlyList<BodyDef> CivBodies() => Civ?.Bodies ?? (IReadOnlyList<BodyDef>)Array.Empty<BodyDef>();

    private BodyDef? CivPicked()
    {
        var bodies = CivBodies();
        return bodies.Count == 0 ? null : bodies[Math.Clamp(_civBody, 0, bodies.Count - 1)];
    }

    private string CivShape()
    {
        var b = CivPicked();
        var site = b == null ? null : Civ?.CivAt(b.Id);
        return $"civ|{Civ != null}|{b?.Id}|{site != null}|{site?.Living}|{CivBodies().Count}";
    }

    private string ClassShort(string classId)
    {
        var name = Data.PlanetClasses.FirstOrDefault(c => c.Id == classId)?.Name ?? classId;
        var dash = name.IndexOf(" —", StringComparison.Ordinal);
        return dash > 0 ? name[..dash] : name.Replace('_', ' ');
    }

    private string BodyLabel(BodyDef b)
    {
        var sys = Civ!;
        var site = sys.CivAt(b.Id);
        var tag = site == null ? "" : site.Living ? $" · L{(int)site.Level} {site.Species}" : $" · ruins ({site.Species})";
        var home = b.Id == sys.SurfaceBody ? " · colony" : "";
        return $"{b.Name} · {ClassShort(b.ClassId)} · {sys.KnowledgeOf(Me, b.Id).ToString().ToLowerInvariant()}{home}{tag}";
    }

    private DebugCivCommand CivCommand(string bodyId, DebugCivField field, double value = 0) => new(Me, Me, bodyId, field, Fx(value));

    private void BuildCiv()
    {
        var sys = Civ;
        if (sys == null)
        {
            var note = Wrapped("No star system in this game (a planet-only landing): civilizations live in the system. The ruins row below still works on this map.", 13, Dim);
            _content.AddChild(note);
            BuildRuinsRow();
            return;
        }
        var bodies = CivBodies();
        _content.AddChild(Caption("BODY"));
        var pick = MakeOption(bodies.Select(BodyLabel).ToArray(), _civBody, i =>
            _civBody = i); // the tab's shape follows the picked body: it rebuilds next frame
        _content.AddChild(pick);
        var b = CivPicked();
        if (b == null) return;
        var id = b.Id;
        var state = Wrapped("", 13, Soft);
        _content.AddChild(state);
        _refreshers.Add(() =>
        {
            var site = sys.CivAt(id);
            if (site == null)
            {
                state.Text = "No civilization site here.";
                return;
            }
            var view = sys.CivView(Me, id);
            var study = view == null ? "unknown to you" : $"study {Mathf.RoundToInt(Mathf.Clamp(view.Study.ToFloat(), 0f, 1f) * 100)} %";
            state.Text = site.Living
                ? $"{site.Species}: living, {CivText.LevelName(site.Level)}, {CivText.TemperamentText(site.Temperament)}, awareness {sys.AwarenessOf(id)}, {study}, relations {CivText.StageName(sys.RelationsWith(Me, id)).ToLowerInvariant()}"
                  + (sys.RefusesContactSeconds(Me, id) is var refuse and > 0 ? $", refuses contact {refuse} s" : "")
                : $"Ruins of {site.Species} (reached {CivText.LevelName(site.Level)}), {study}";
        });

        // ---- spawn / remove
        _content.AddChild(Section("SITE"));
        var spawnRow = Row();
        spawnRow.AddChild(Text("Level", 14, Ink));
        var levelLabel = Text($"L{_civSpawnLevel}", 14, Accent, _caps);
        levelLabel.CustomMinimumSize = new Vector2(28, 0);
        var spawnSlider = MakeSlider(1, 6, _ => { });
        spawnSlider.Value = _civSpawnLevel;
        spawnSlider.ValueChanged += v =>
        {
            _civSpawnLevel = (int)v;
            levelLabel.Text = $"L{_civSpawnLevel}";
        };
        spawnRow.AddChild(spawnSlider);
        spawnRow.AddChild(levelLabel);
        _content.AddChild(spawnRow);
        var siteGrid = Grid(3);
        siteGrid.AddChild(MakeButton("Spawn living", Green, () =>
            Apply($"civ: living L{_civSpawnLevel} on {b.Name}", new DebugCommand[] { CivCommand(id, DebugCivField.SpawnLiving, _civSpawnLevel) },
                sys.CivAt(id) == null ? () => new DebugCommand[] { CivCommand(id, DebugCivField.Remove) } : null), 0, 34));
        siteGrid.AddChild(MakeButton("Spawn ruins", Yellow, () =>
            Apply($"civ: ruins on {b.Name}", new DebugCommand[] { CivCommand(id, DebugCivField.SpawnRuins) },
                sys.CivAt(id) == null ? () => new DebugCommand[] { CivCommand(id, DebugCivField.Remove) } : null), 0, 34));
        siteGrid.AddChild(MakeButton("Remove", Red, () =>
            Apply($"civ: removed the site on {b.Name}", new DebugCommand[] { CivCommand(id, DebugCivField.Remove) }, null), 0, 34));
        _content.AddChild(siteGrid);
        _content.AddChild(Wrapped("Spawning replaces whatever site is there. The colony's own body can't have one.", 12, Dim));

        var site0 = sys.CivAt(id);
        if (site0 == null)
        {
            BuildStandingRow(sys);
            BuildRuinsRow();
            return;
        }

        if (site0.Living)
        {
            // ---- level
            _content.AddChild(Section("DEVELOPMENT LEVEL"));
            _content.AddChild(Segmented(new[] { "L1", "L2", "L3", "L4", "L5", "L6" }, () => Math.Clamp((int)(sys.CivAt(id)?.Level ?? CivLevel.StoneBronze) - 1, 0, 5), i =>
            {
                var before = (int)(sys.CivAt(id)?.Level ?? CivLevel.StoneBronze);
                Apply($"civ: {b.Name} level L{i + 1}", CivCommand(id, DebugCivField.Level, i + 1), CivCommand(id, DebugCivField.Level, before));
            }));

            // ---- awareness
            _content.AddChild(Section("AWARENESS OF ALIENS"));
            var awareRow = Row();
            var awareValue = Text("", 14, Ink);
            awareValue.CustomMinimumSize = new Vector2(80, 0);
            var aware = MakeSlider(0, 100, v => SetAwareness(id, b.Name, Mathf.RoundToInt(v)));
            awareRow.AddChild(aware);
            awareRow.AddChild(awareValue);
            _content.AddChild(awareRow);
            _refreshers.Add(() =>
            {
                var a = sys.AwarenessOf(id);
                awareValue.Text = $"{a} · {CivText.Bands[CivText.Band(a)].Name.ToLowerInvariant()}";
                if (!aware.HasMeta("dragging")) aware.SetValueNoSignal(a);
            });
            var awarePresets = Grid(5);
            foreach (var v in new[] { 0, 20, 50, 80, 100 })
                awarePresets.AddChild(MakeButton(v.ToString(), v >= 80 ? Red : v >= 50 ? Yellow : Plain, () => SetAwareness(id, b.Name, v), 0, 30));
            _content.AddChild(awarePresets);

            // ---- temperament
            _content.AddChild(Section("TEMPERAMENT"));
            _content.AddChild(Segmented(new[] { "Peaceful", "Wary", "Aggressive" }, () => Math.Clamp((int)(sys.CivAt(id)?.Temperament ?? Temperament.Peaceful) - 1, 0, 2), i =>
            {
                var before = Math.Clamp((int)(sys.CivAt(id)?.Temperament ?? Temperament.Peaceful), 1, 3);
                Apply($"civ: {b.Name} {(Temperament)(i + 1)}".ToLowerInvariant(), CivCommand(id, DebugCivField.Temperament, i + 1), CivCommand(id, DebugCivField.Temperament, before));
            }));

            // ---- relations (local player)
            _content.AddChild(Section("RELATIONS (YOU)"));
            _content.AddChild(Segmented(new[] { "None", "Contact", "Trade", "Candidate", "Allied" }, () => Math.Clamp((int)sys.RelationsWith(Me, id), 0, 4), i =>
            {
                var before = (int)sys.RelationsWith(Me, id);
                Apply($"civ: relations with {b.Name} → {CivText.StageName((ContactStage)i).ToLowerInvariant()}", CivCommand(id, DebugCivField.Relations, i), CivCommand(id, DebugCivField.Relations, before));
            }));
        }

        // ---- study and post (local player)
        _content.AddChild(Section("STUDY AND POST (YOU)"));
        var studyGrid = Grid(2);
        studyGrid.AddChild(MakeButton("Finish study", Blue, () =>
            Apply($"civ: study of {b.Name} finished", new DebugCommand[] { CivCommand(id, DebugCivField.FinishStudy) }, null), 0, 34));
        studyGrid.AddChild(MakeButton("Post is found now", Red, () =>
            Apply($"civ: post on {b.Name} found", new DebugCommand[] { CivCommand(id, DebugCivField.FindPost) }, null), 0, 34));
        _content.AddChild(studyGrid);

        BuildStandingRow(sys);
        BuildRuinsRow();
    }

    private void SetAwareness(string bodyId, string name, int value)
    {
        var before = Civ?.AwarenessOf(bodyId) ?? 0;
        Apply($"civ: {name} awareness {value}", CivCommand(bodyId, DebugCivField.Awareness, value), CivCommand(bodyId, DebugCivField.Awareness, before));
    }

    /// <summary>The local player's Starfleet Standing (body ignored by the sim; the picked body's id is sent).</summary>
    private void BuildStandingRow(StarSystemState sys)
    {
        _content.AddChild(Section("STARFLEET STANDING (YOU)"));
        var bodyId = CivPicked()?.Id ?? sys.SurfaceBody ?? "";
        var row = Row();
        var value = Text("", 14, Ink);
        value.CustomMinimumSize = new Vector2(110, 0);
        var slider = MakeSlider(0, 100, v => SetStanding(bodyId, Mathf.RoundToInt(v)));
        row.AddChild(slider);
        row.AddChild(value);
        _content.AddChild(row);
        _refreshers.Add(() =>
        {
            var st = sys.StandingOf(Me);
            value.Text = st is { } s ? $"{s} · {CivText.Rungs[Mathf.Clamp(sys.StandingRung(Me), 0, 5)].Name.ToLowerInvariant()}" : "none (not tracked)";
            if (st is { } s2 && !slider.HasMeta("dragging")) slider.SetValueNoSignal(s2);
        });
        var presets = Grid(5);
        foreach (var v in new[] { 100, 70, 50, 30, 10 })
            presets.AddChild(MakeButton(v.ToString(), v >= 70 ? Green : v >= 50 ? Yellow : Red, () => SetStanding(bodyId, v), 0, 30));
        _content.AddChild(presets);
        _content.AddChild(Wrapped("Standing exists for the Federation in a Prime Directive era; elsewhere the sim refuses it.", 12, Dim));
    }

    private void SetStanding(string bodyId, int value)
    {
        var before = Civ?.StandingOf(Me);
        Apply($"civ: Standing {value}", new DebugCommand[] { CivCommand(bodyId, DebugCivField.Standing, value) },
            before is { } b ? () => new DebugCommand[] { CivCommand(bodyId, DebugCivField.Standing, b) } : null);
    }

    /// <summary>Ruins on the colony's own map: add at a picked tile (undo removes them) or remove the picked tile's.</summary>
    private void BuildRuinsRow()
    {
        _content.AddChild(Section("RUINS ON THE COLONY MAP"));
        var info = Wrapped("", 13, Soft);
        _content.AddChild(info);
        _refreshers.Add(() =>
        {
            var ruins = Surface.Ruins;
            var tile = _host.CursorTile;
            var here = tile >= 0 ? Surface.RuinAt(tile) : null;
            info.Text = $"{ruins.Count} ruin site{(ruins.Count == 1 ? "" : "s")} on this map" +
                        (here != null ? $" · tile {tile}: ruins of {here.Species}, {here.LayersDug} of {here.Layers} layers dug" : tile >= 0 ? $" · tile {tile}: none" : "");
        });
        var grid = Grid(2);
        grid.AddChild(MakeButton("Add ruins at a tile…", Yellow, () =>
            _host.BeginPick("Test panel: click a tile to put ruins there", tile =>
                Apply($"ruins added on tile {tile}", new DebugRuinCommand(Me, tile, true), new DebugRuinCommand(Me, tile, false))), 0, 34));
        grid.AddChild(MakeButton("Remove ruins at a tile…", Red, () =>
            _host.BeginPick("Test panel: click a ruin tile to clear it", tile =>
                Apply($"ruins removed on tile {tile}", new DebugCommand[] { new DebugRuinCommand(Me, tile, false) }, null)), 0, 34));
        _content.AddChild(grid);
        _content.AddChild(Wrapped("New ruins get a species and layers from the sim. Removing can't be undone (they'd come back different).", 12, Dim));
    }
}
