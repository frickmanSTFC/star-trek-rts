using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Civilizations phase 1, view side (docs/civilizations-contract.md "View", mockup docs/mockups/first-contact scan card +
// dossier in the helm's look; render-only, commands only): the CIVILIZATION block of the body card and the panel
// (CivRows), the HIDDEN POST button next to DEPLOY SATELLITE (sys:post:<body> -> ObservationPostCommand through
// SystemOrders, greyed by the sim's PostProblem), the DOSSIER (C, the card's DOSSIER button, the Standing chips;
// CivDossier.cs), the Starfleet Standing chip on the surface top bar and the helm (only while StandingOf is non-null:
// Federation in a Prime Directive era), and the event lines: the sim's CivEvents list when it has one (read by
// reflection, so this builds against the contract stubs), else a once-a-second diff of CivView / StandingOf /
// IncidentsOn (charted, study complete, post placed / found, incidents, awareness bands, Standing steps).
public partial class PlanetDriver
{
    private CivDossier _dossier = null!;
    private string? _dossierBody;
    private double _civPollAt = -1;
    private bool _civBaseline;
    private readonly Dictionary<string, CivSeen> _civSeen = new();
    private int? _standingSeen;
    private int _rungSeen;
    private int _civEventsSeen;

    private sealed record CivSeen(bool Signals, bool Charted, bool Living, bool Studied, bool PostHere, bool PostFound, int Incidents, int Band);

    /// <summary>Study seconds by source (contract placeholders: orbit 120, satellite 60, post 30), for the ETA only.</summary>
    private const float StudyOrbitSeconds = 120f;

    // The sim's event list, if it has one (the contract suggests "CivEvents with tick + text + body").
    private static readonly System.Reflection.PropertyInfo? CivEventsProperty = typeof(StarSystemState).GetProperty("CivEvents");

    private void SetupCiv()
    {
        var layer = new CanvasLayer { Name = "CivLayer", Layer = 4 };
        AddChild(layer);
        _dossier = new CivDossier { Name = "CivDossier" };
        layer.AddChild(_dossier);
        _dossier.ActionPicked += OnDossierAction;
        _hud.StandingPressed += () => OpenDossier(null);
        SetupContact(layer); // the first-contact event panel (PlanetDriver.Contact.cs)
    }

    /// <summary>What the local player knows about a body's civilization (null = none known).</summary>
    private CivReport? CivOf(string? bodyId) => bodyId == null || _sim.System is not { } sys ? null : sys.CivView(LocalPlayer, bodyId);

    /// <summary>Known civilization worlds (signals, living or ruins), in body order.</summary>
    private List<(string BodyId, string Name)> CivWorlds()
    {
        var list = new List<(string, string)>();
        if (_sim.System == null) return list;
        foreach (var b in _system.Bodies)
            if (CivOf(b.Id) != null) list.Add((b.Id, _system.KnownName(b)));
        return list;
    }

    /// <summary>A known civilization that could still be living (signals only, or charted alive): a post makes sense.</summary>
    private static bool MaybeLiving(CivReport r) => !r.Charted || r.Living;

    // ---------------------------------------------------------------- body card / panel rows

    /// <summary>The CIVILIZATION block by knowledge: signals, charted (species, level range or exact level, population,
    /// awareness bar, study bar), studied (temperament, unity, nations), ruins ("ruins of the …").</summary>
    private List<HelmRow> CivRows(BodyDef b)
    {
        var rows = new List<HelmRow>();
        if (CivOf(b.Id) is not { } r) return rows;
        if (!r.Charted)
        {
            rows.Add(new HelmRow("CIVILIZATION", "signals", HelmScreen.Peach, Heading: true));
            rows.Add(new HelmRow("", "Artificial signals: possible civilization", HelmScreen.Peach));
            rows.Add(new HelmRow("", "Orbit it for a sensor sweep to chart it.", DetectedGrey));
            return rows;
        }
        if (!r.Living)
        {
            rows.Add(new HelmRow("PAST CIVILIZATION", "ruins", CivText.Rust, Heading: true));
            rows.Add(new HelmRow("", $"Ruins of {CivText.The(r)}", CivText.Rust));
            return rows;
        }
        rows.Add(new HelmRow("CIVILIZATION", r.Live ? "live" : "at last scan", HelmScreen.Peach, Heading: true));
        rows.Add(new HelmRow("WHO", $"{CivText.The(r)} (living)"));
        rows.Add(new HelmRow("LEVEL", CivText.LevelText(r)));
        rows.Add(new HelmRow("PEOPLE", CivText.Population(r.Population)));
        rows.Add(new HelmRow("AWARE", $"{r.Awareness} / 100 · {CivText.Bands[CivText.Band(r.Awareness)].Name.ToLowerInvariant()}", CivText.AwarenessColour(r.Awareness), r.Awareness / 100f));
        if (CivText.Studied(r))
        {
            rows.Add(new HelmRow("TEMPER", CivText.TemperamentText(r.Temperament), CivText.TemperamentColour(r.Temperament)));
            rows.Add(new HelmRow("UNITY", CivText.UnityText(r)));
        }
        else
        {
            var (source, _) = StudySource(b.Id, r);
            var eta = StudyEta(b.Id, r);
            rows.Add(new HelmRow("STUDY", $"{Mathf.RoundToInt(r.Study.ToFloat() * 100)} % · {source}{(eta.Length > 0 ? $" · {eta}" : "")}", HelmScreen.Cyan, Mathf.Clamp(r.Study.ToFloat(), 0f, 1f)));
        }
        if (r.PostFound) rows.Add(new HelmRow("POST", "found by the natives", HelmScreen.Red));
        else if (r.PostHere) rows.Add(new HelmRow("POST", "hidden observation post", HelmScreen.Green));
        if (_sim.System?.StandingOf(LocalPlayer) != null && _sim.System.IncidentsOn(LocalPlayer, b.Id) is var n and > 0)
            rows.Add(new HelmRow("INCIDENTS", $"{n} of 3 on this world", HelmScreen.Red));
        AddContactRows(b, r, rows); // relations, talks, refusal, trade, their tech (PlanetDriver.Contact.cs)
        return rows;
    }

    /// <summary>What studies the body now and how fast: a post (×4), a satellite (×2), the ship in orbit (×1), or nothing.</summary>
    private (string Text, float Rate) StudySource(string bodyId, CivReport r)
    {
        var sys = _sim.System;
        if (r.PostHere && !r.PostFound) return ("hidden post (×4)", 4f);
        if (sys != null && sys.Satellites.Any(s => s.Owner == LocalPlayer && s.BodyId == bodyId)) return ("satellite (×2)", 2f);
        var f = _system.Flagship;
        if (f != null && f.AtBody == bodyId && !SystemSource.InFlight(f)) return ("ship in orbit (×1)", 1f);
        return ("paused: no ship, satellite or post here", 0f);
    }

    /// <summary>"~45 s left" at the current source's rate (placeholder seconds), or "".</summary>
    private string StudyEta(string bodyId, CivReport r)
    {
        var (_, rate) = StudySource(bodyId, r);
        if (rate <= 0f) return "";
        var left = (1f - Mathf.Clamp(r.Study.ToFloat(), 0f, 1f)) * StudyOrbitSeconds / rate;
        return left < 1f ? "" : $"~{Mathf.CeilToInt(left)} s left";
    }

    /// <summary>The card's HIDDEN POST button (flagship in orbit of a civilization world) and DOSSIER.</summary>
    private void AddCivButtons(BodyDef b, List<HelmAction> buttons)
    {
        if (CivOf(b.Id) is not { } r) return;
        var f = _system.Flagship;
        var here = f != null && f.AtBody == b.Id && !SystemSource.InFlight(f);
        // the post is for studying them unseen: not once they know us
        if (here && MaybeLiving(r) && StageOf(b.Id) == ContactStage.None) buttons.Add(PostButton(b.Id, r));
        AddContactButtons(b, r, buttons); // FIRST CONTACT / MEMBERSHIP (PlanetDriver.Contact.cs)
        buttons.Add(new HelmAction($"civ:dossier:{b.Id}", "", "DOSSIER", Colour: HelmScreen.Sky));
    }

    private HelmAction PostButton(string bodyId, CivReport r) =>
        r.PostHere && !r.PostFound
            ? new HelmAction($"sys:post:{bodyId}", "", "POST ✓", false, Colour: HelmScreen.Green)
            : new HelmAction($"sys:post:{bodyId}", "", r.PostFound ? "NEW POST" : "HIDDEN POST", PostProblem(bodyId) == null, Colour: HelmScreen.Peach);

    /// <summary>The action strip at another body: HIDDEN POST and DOSSIER [C] while it has a known civilization.</summary>
    private void AddCivStripActions(string bodyId, List<HelmAction> actions)
    {
        if (CivOf(bodyId) is not { } r) return;
        if (MaybeLiving(r) && StageOf(bodyId) == ContactStage.None) actions.Add(PostButton(bodyId, r));
        if (ContactAction(bodyId, r) is { } contact) actions.Add(contact); // FIRST CONTACT / MEMBERSHIP (PlanetDriver.Contact.cs)
        actions.Add(new HelmAction($"civ:dossier:{bodyId}", "C", "DOSSIER", Colour: HelmScreen.Sky));
    }

    /// <summary>Why a hidden post can't go down on this body now (view check, then the sim's PostProblem), or null.</summary>
    private string? PostProblem(string bodyId)
    {
        if (_system.State is not { } sys) return "no star-system game";
        var f = _system.Flagship;
        if (f == null || f.AtBody != bodyId || SystemSource.InFlight(f)) return "the flagship must be in orbit here";
        return sys.PostProblem(LocalPlayer, bodyId);
    }

    /// <summary>"sys:post:<body>" from a card, the strip or the dossier (OnSystemAction's path). True = handled.</summary>
    private bool OnPostAction(string id, long when)
    {
        if (!id.StartsWith("sys:post:")) return false;
        var bodyId = id[9..];
        var problem = PostProblem(bodyId) ?? SystemOrders.ObservationPost(_sim, LocalPlayer, bodyId, when);
        if (problem != null) _hud.PushEvent($"Can't place a hidden post: {problem}", new Color(1f, 0.7f, 0.42f));
        else _hud.PushEvent("Hidden observation post going down: it studies them ×4, but the natives may find it", HelmScreen.Cyan);
        return true;
    }

    // ---------------------------------------------------------------- dossier

    private void OnCivAction(string id)
    {
        if (OnContactAction(id)) return; // civ:contact:<body>, civ:member:<body> (PlanetDriver.Contact.cs)
        if (id == "civ:dossier") OpenDossier(null);
        else if (id.StartsWith("civ:dossier:")) OpenDossier(id[12..]);
    }

    private void OnDossierAction(string id)
    {
        if (id.StartsWith("civ:dossier:")) _dossierBody = id[12..];
        else OnHelmAction(id); // the card's ids (sys:post:<body>, sys:course:<body>)
    }

    /// <summary>The body the dossier opens on: the given one, the plot's selected body, the orbited one, the last one,
    /// else the first known civilization.</summary>
    private string? DossierTarget(string? wanted)
    {
        foreach (var id in new[] { wanted, _inOrbit && _orbit.Display == OrbitDisplay.System ? _selectedBody : null, _inOrbit ? _system.OrbitBodyId : null, _dossierBody })
            if (id != null && CivOf(id) != null) return id;
        return CivWorlds().Select(w => w.BodyId).FirstOrDefault();
    }

    private void OpenDossier(string? bodyId)
    {
        var target = DossierTarget(bodyId);
        if (target == null)
        {
            _hud.PushEvent(_sim.System == null ? "No civilizations: this game has no star system" : "No civilization known yet: long-range scans and visits find their signals", new Color(1f, 0.7f, 0.42f));
            return;
        }
        _dossierBody = target;
        _dossier.Open();
        RefreshDossier();
    }

    private void ToggleDossier()
    {
        if (_dossier.IsOpen) _dossier.Close();
        else OpenDossier(null);
    }

    /// <summary>C toggles the dossier (orbit and surface); while it is open Esc closes it and other keys wait. True = used.</summary>
    private bool CivInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C })
        {
            ToggleDossier();
            return true;
        }
        if (!_dossier.IsOpen || e is not InputEventKey key) return false;
        if (key is { Pressed: true, Keycode: Key.Escape }) _dossier.Close();
        else if (key is { Pressed: true, Keycode: Key.F1 }) _hud.ToggleHelp();
        return true;
    }

    private bool CivBlocksMouse(Vector2 mouse) => _dossier.IsOpen || ContactBlocksMouse(mouse);

    private void RefreshDossier()
    {
        if (!_dossier.IsOpen) return;
        if (_dossierBody == null || CivOf(_dossierBody) is not { } r || _system.Body(_dossierBody) is not { } b)
        {
            _dossier.Close();
            return;
        }
        var sys = _sim.System!;
        var (source, _) = StudySource(b.Id, r);
        var actions = new List<HelmAction>();
        var f = _system.Flagship;
        if (f != null && f.AtBody == b.Id && !SystemSource.InFlight(f))
        {
            if (MaybeLiving(r) && StageOf(b.Id) == ContactStage.None) actions.Add(PostButton(b.Id, r));
            if (ContactAction(b.Id, r) is { } contact) actions.Add(contact);
        }
        else
        {
            if (f != null) actions.Add(new HelmAction($"sys:course:{b.Id}", "", "SET COURSE", CourseProblem(b.Id) == null, Colour: HelmScreen.Peach));
            if (ContactAction(b.Id, r) is { } member) actions.Add(member); // MEMBERSHIP needs no ship there (the sim decides)
        }
        _dossier.Show(new DossierView(r, _system.KnownName(b), _system.Identified(b.Id) ? _system.ClassName(b.ClassId) : "unidentified", CivText.Studied(r) ? "study complete" : source,
            StudyEta(b.Id, r), sys.StandingOf(LocalPlayer), sys.StandingRung(LocalPlayer), sys.IncidentsOn(LocalPlayer, b.Id),
            sys.ObservationResearchPerMinute(LocalPlayer).ToFloat(), CivWorlds(), actions, ContactFor(b.Id, r)));
    }

    // ---------------------------------------------------------------- per frame: chips, events, dossier

    /// <summary>Every frame (after the sim stepped): the Standing chips, the dossier, the event lines once a second.</summary>
    private void UpdateCiv()
    {
        var sys = _sim.System;
        var standing = sys?.StandingOf(LocalPlayer);
        var rung = sys?.StandingRung(LocalPlayer) ?? 0;
        var tip = standing is { } st ? CivText.StandingTooltip(st, rung) : "";
        _hud.ShowStanding(standing, rung, tip);
        _helm.SetStanding(standing is { } s2 ? $"STANDING {s2}" : null, CivText.RungColour(rung), tip);
        RefreshDossier();
        UpdateContact(); // the first-contact panel (PlanetDriver.Contact.cs)
        if (_clock - _civPollAt < 1.0 && _civPollAt >= 0) return;
        _civPollAt = _clock;
        RefreshTrade();
        if (sys == null) return;
        if (CivEventsProperty != null) SimCivEvents(sys);
        else DiffCivEvents(sys, standing, rung);
    }

    /// <summary>The sim's own event list (when the sim branch has one): each new entry for us becomes a feed line.</summary>
    private void SimCivEvents(StarSystemState sys)
    {
        if (CivEventsProperty!.GetValue(sys) is not System.Collections.IEnumerable list) return;
        var items = list.Cast<object>().ToList();
        if (_civEventsSeen > items.Count) _civEventsSeen = 0;
        for (var i = _civEventsSeen; i < items.Count; i++)
        {
            var e = items[i];
            var type = e.GetType();
            var player = type.GetProperty("Player")?.GetValue(e) as int?;
            if (player is { } p && p >= 0 && p != LocalPlayer) continue;
            var text = (type.GetProperty("Text") ?? type.GetProperty("Message"))?.GetValue(e) as string;
            if (string.IsNullOrEmpty(text)) continue;
            var lower = text.ToLowerInvariant();
            var bad = lower.Contains("found") || lower.Contains("incident") || lower.Contains("fell") || lower.Contains("recall") || lower.Contains("sanction")
                || lower.Contains("refuse") || lower.Contains("turned our hail away") || lower.Contains("culture shock");
            var good = lower.Contains("complete") || lower.Contains("recover") || lower.Contains("settled down") || lower.Contains("trade route");
            _hud.PushEvent(text, bad ? HelmScreen.Red : good ? HelmScreen.Green : HelmScreen.Peach);
        }
        _civEventsSeen = items.Count;
    }

    /// <summary>No event list in the sim: diff what we know once a second (the first pass only takes a baseline).</summary>
    private void DiffCivEvents(StarSystemState sys, int? standing, int rung)
    {
        var lines = new List<(string Text, Color Colour)>();
        foreach (var b in _system.Bodies)
        {
            var r = CivOf(b.Id);
            if (r == null) continue;
            var now = new CivSeen(r.Signals, r.Charted, r.Living, CivText.Studied(r), r.PostHere, r.PostFound, sys.IncidentsOn(LocalPlayer, b.Id), CivText.Band(r.Awareness));
            _civSeen.TryGetValue(b.Id, out var was);
            _civSeen[b.Id] = now;
            if (!_civBaseline) continue;
            var name = _system.KnownName(b);
            if (was == null && !now.Charted) lines.Add(($"Artificial signals at {name}: a possible civilization", HelmScreen.Peach));
            if (now.Charted && (was == null || !was.Charted))
                lines.Add(now.Living
                    ? ($"{name} charted: {CivText.The(r)}, {CivText.LevelText(r)}, {CivText.Population(r.Population)}", HelmScreen.Cyan)
                    : ($"{name}: structures, no signals: ruins of {CivText.The(r)}", CivText.Rust));
            if (was == null) continue;
            if (now.Studied && !was.Studied && now.Living)
                lines.Add(($"Study of {CivText.The(r)} complete: {CivText.LevelName(r.LevelMax)}, {CivText.TemperamentText(r.Temperament)}, {CivText.UnityText(r)}", HelmScreen.Green));
            if (now.PostHere && !was.PostHere) lines.Add(($"Hidden observation post in place on {name}: studying ×4", HelmScreen.Green));
            if (now.PostFound && !was.PostFound) lines.Add(($"{CivText.The(r)} found our observation post on {name}!", HelmScreen.Red));
            if (now.Incidents > was.Incidents) lines.Add(($"Contamination incident on {name}: {now.Incidents} of 3 recorded by Starfleet", HelmScreen.Red));
            if (now.Band > was.Band && now.Living) lines.Add(($"{CivText.The(r)} on {name}: awareness of aliens now \"{CivText.Bands[now.Band].Name}\" ({r.Awareness})", HelmScreen.Peach));
        }
        if (_civBaseline && standing is { } s && _standingSeen is { } before)
        {
            if (rung > _rungSeen) lines.Add(($"Starfleet Standing fell to {s}: {CivText.Rungs[Mathf.Clamp(rung, 0, 5)].Name.ToLowerInvariant()}", rung >= 3 ? HelmScreen.Red : HelmScreen.Peach));
            else if (rung < _rungSeen) lines.Add(($"Starfleet Standing recovered to {s}: {CivText.Rungs[Mathf.Clamp(rung, 0, 5)].Name.ToLowerInvariant()}", HelmScreen.Green));
            else if (s <= before - 10) lines.Add(($"Starfleet Standing down to {s}", HelmScreen.Peach));
        }
        if (standing != null && (_standingSeen == null || rung != _rungSeen || standing <= _standingSeen - 10 || standing > _standingSeen)) _standingSeen = standing;
        _rungSeen = rung;
        _civBaseline = true;
        foreach (var (text, colour) in lines) _hud.PushEvent(text, colour);
    }
}
