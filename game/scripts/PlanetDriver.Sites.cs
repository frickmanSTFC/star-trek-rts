using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 1, view side (docs/space-jobs-contract.md "VIEW", design page docs/mockups/space-jobs; render-only,
// commands only through SpaceOrders after the sim's own problem check):
//  - site cards on the SYSTEM display ("site:<id>" in _systemCard): rows from StarSystemState.SiteReportFor, hazards,
//    scan / work progress bars, the report's actions as buttons (greyed with the sim's problem as tooltip), SET COURSE /
//    SCAN when the report doesn't list them; a SITES section in SYSTEM DATA; the cursor readout over a site;
//  - the globe displays at a site (the flagship holds there): title, SITE DATA panel and the site's actions on the strip
//    (the 3D scene is OrbitView.Sites.cs);
//  - the away team (AwayTeamPanel): the picker (per division − / +, the ship's maximum, who goes when the sim can say),
//    then the timed event and its result;
//  - the ship status screen (ShipStatusScreen: STATUS on the rail, the HULL chip, U in orbit), the HULL / CREW chip on
//    the helm's top bar (HelmScreen.Space.cs);
//  - event lines: contacts detected / identified / worked out, the ship log (damage, crew, repairs), hull band changes,
//    crippled, the away team going and coming back.
// Everything reads the stubs safely: no sites, no status (null), no away team = nothing shows and nothing is said.
public partial class PlanetDriver
{
    private AwayTeamPanel _awayPanel = null!;
    private ShipStatusScreen _statusScreen = null!;
    private string? _pickSite;
    private int[] _pickCrew = System.Array.Empty<int>();
    private string? _awayDismissed;
    private string? _awaySeen;
    private bool _awayDoneSeen;
    private double _spacePollAt = -1;
    private bool _spaceBaseline;
    private ShipStatusView? _status;
    private readonly Dictionary<string, SiteKnowledge> _siteSeen = new();
    private long _shipLogSeen = long.MinValue;
    private HullBand? _bandSeen;
    private bool _crippledSeen;
    private string? _siteArrivedSeen;
    private AwayPickerView? _pickerView;
    private string _pickerKey = "";
    private double _pickerAt;

    private static readonly Color SpaceAmber = Color.FromHtml("#f5c04a");
    private static readonly Color Warn = new(1f, 0.7f, 0.42f);


    private void SetupSpace()
    {
        var layer = new CanvasLayer { Name = "SpaceLayer", Layer = 4 };
        AddChild(layer);
        _awayPanel = new AwayTeamPanel { Name = "AwayTeamPanel" };
        layer.AddChild(_awayPanel);
        _awayPanel.CrewChanged += OnPickCrew;
        _awayPanel.Launch += LaunchAwayTeam;
        _awayPanel.Cancel += () => _pickSite = null;
        _awayPanel.ChoicePicked += OnAwayChoice;
        _awayPanel.Dismissed += () => _awayDismissed = AwayKey(_sim.System?.AwayTeamOf(LocalPlayer));
        _statusScreen = new ShipStatusScreen { Name = "ShipStatus" };
        layer.AddChild(_statusScreen);
        _statusScreen.ActionPicked += OnStatusAction;
    }

    private bool SpaceBlocksMouse(Vector2 mouse) => _awayPanel.IsOverUi(mouse) || _statusScreen.IsOverUi(mouse);

    private SystemShip? Flagship => _system.Flagship;

    /// <summary>The player's card for a site (null = unknown, or no system).</summary>
    private SiteReport? SiteReport(string? siteId) => siteId == null || _sim.System is not { } sys ? null : sys.SiteReportFor(LocalPlayer, siteId);

    private string SiteTitle(string? siteId) => SiteReport(siteId)?.Title is { Length: > 0 } t ? t : "Unknown contact";

    private static Color ToneColour(string tone) => tone switch
    {
        "good" => HelmScreen.Green,
        "warn" => SpaceAmber,
        "bad" => HelmScreen.Red,
        _ => HelmScreen.Ink,
    };

    /// <summary>Site-scan reach in map units: the sim's number when it has one, else the contract's placeholder (less below half hull).</summary>
    private float SiteReach()
    {
        if (_sim.System is { } sys) return sys.SiteScanRange(LocalPlayer).ToFloat();
        return SystemPlot.DefaultSiteScanRange * (_status != null && _status.Band >= HullBand.Damaged ? 0.8f : 1f);
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Every frame: the polls (4×/s), the away-team panel, the status screen.</summary>
    private void UpdateSpace(double delta)
    {
        var sys = _sim.System;
        if (sys != null && (_spacePollAt < 0 || _clock - _spacePollAt >= 0.25))
        {
            _spacePollAt = _clock;
            _status = sys.StatusOf(LocalPlayer);
            SpaceEvents(sys);
            _orbit.SiteScanRange = SiteReach();
            UpdateHullChip();
        }
        if (sys == null && _status != null)
        {
            _status = null;
            UpdateHullChip();
        }
        UpdateAwayPanel(sys);
        if (!_inOrbit && _statusScreen.IsOpen) _statusScreen.Close();
        if (_statusScreen.IsOpen && (_spacePollAt == _clock || _statusPage == null)) _statusPage = _status == null ? null : StatusPage(_status);
        _statusScreen.Show(_statusPage, _helm.OpenRect);
    }

    private ShipStatusPage? _statusPage;

    private void ToggleShipStatus()
    {
        if (_statusScreen.IsOpen) _statusScreen.Close();
        else
        {
            _statusPage = _status == null ? null : StatusPage(_status);
            _statusScreen.Open();
        }
    }

    private static Color BandColour(HullBand band) => band switch
    {
        HullBand.Scuffed => HelmScreen.Green,
        HullBand.Worn => SpaceAmber,
        HullBand.Damaged => HelmScreen.Peach,
        _ => HelmScreen.Red,
    };

    private static string BandName(HullBand band) => band switch
    {
        HullBand.Scuffed => "Scuffed",
        HullBand.Worn => "Worn",
        HullBand.Damaged => "Damaged",
        HullBand.Critical => "Critical",
        _ => "Crippled",
    };

    private void UpdateHullChip()
    {
        if (_status is not { } s)
        {
            _helm.SetHull(null, 1f, HelmScreen.Green, "", "");
            return;
        }
        var hull = Mathf.Clamp(s.Hull.ToFloat(), 0f, 100f);
        var tip = new System.Text.StringBuilder();
        tip.Append($"Hull {hull:0}% · {BandName(s.Band)}{(s.BandEffect.Length > 0 ? $": {s.BandEffect}" : "")}");
        foreach (var d in s.Down) tip.Append($"\n{SystemName(d.System)} knocked out · back in {SecondsTo(d.BackTick)} s");
        var injured = s.Crew.Sum(c => c.Injured);
        var lost = s.Crew.Sum(c => c.Lost);
        tip.Append($"\nCrew {s.CrewTotal} of {s.CrewStart}{(injured > 0 ? $" · {injured} injured" : "")}{(lost > 0 ? $" · {lost} lost" : "")}");
        tip.Append("\nClick (or U) for the ship status");
        _helm.SetHull(s.Crippled ? "ADRIFT" : $"{hull:0}%", hull / 100f, BandColour(s.Band), $"{s.CrewTotal}/{s.CrewStart}", tip.ToString());
    }

    private long SecondsTo(long tick) => System.Math.Max(0, (tick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate);

    private static string SystemName(ShipSystem s) => s switch
    {
        ShipSystem.LifeSupport => "Life support",
        _ => s.ToString(),
    };

    private static string SystemEffect(ShipSystem s) => s switch
    {
        ShipSystem.Sensors => "scans slower",
        ShipSystem.Engines => "flights slower",
        ShipSystem.Tractor => "no mining, towing or pick-ups",
        _ => "injured crew don't recover",
    };

    // ---------------------------------------------------------------- events

    private void SpaceEvents(StarSystemState sys)
    {
        var events = _spaceBaseline;
        _spaceBaseline = true;

        // Sites: detected / identified / worked out.
        var detected = 0;
        string? oneDetected = null;
        foreach (var s in sys.Sites)
        {
            var k = sys.SiteKnowledgeOf(LocalPlayer, s.Id);
            var was = _siteSeen.GetValueOrDefault(s.Id, SiteKnowledge.Unknown);
            if (k == was) continue;
            _siteSeen[s.Id] = k;
            if (!events) continue;
            if (k == SiteKnowledge.Detected && was == SiteKnowledge.Unknown)
            {
                detected++;
                oneDetected = $"Contact detected: unknown, size ~{s.Size} · scan it from within range to identify it";
            }
            else if (k == SiteKnowledge.Identified) _hud.PushEvent($"Identified: {SiteTitle(s.Id)} ({SystemPlot.KindWord(s.Kind).ToLowerInvariant()})", SystemPlot.SiteColour(s.Kind, k));
            else if (k == SiteKnowledge.Spent) _hud.PushEvent($"{SiteTitle(s.Id)} worked out", HelmScreen.Lavender);
        }
        if (detected == 1 && oneDetected != null) _hud.PushEvent(oneDetected, SystemPlot.SiteGrey);
        else if (detected > 1) _hud.PushEvent($"{detected} new contacts detected between the orbits (grey on the plot)", SystemPlot.SiteGrey);

        // Arrived at / left a site.
        var here = _system.FlagshipSite;
        if (here != _siteArrivedSeen)
        {
            if (events && here != null) _hud.PushEvent($"{Flagship?.Name ?? "The ship"} holding at {SiteTitle(here)}", HelmScreen.Peach);
            _siteArrivedSeen = here;
        }

        // The ship log (newest after the last seen), the hull band, crippled.
        if (_status is { } st)
        {
            long newest = _shipLogSeen;
            foreach (var line in st.Log.OrderBy(l => l.Tick))
            {
                if (line.Tick <= _shipLogSeen) continue;
                newest = System.Math.Max(newest, line.Tick);
                if (events) _hud.PushEvent(line.Text, line.Tone switch { "bad" => HelmScreen.Red, "warn" => Warn, "good" => HelmScreen.Green, _ => HelmScreen.Sky });
            }
            _shipLogSeen = newest;
            _shipLogUsed |= st.Log.Count > 0;
            if (!_shipLogUsed) StatusDiffEvents(st, events); // the sim keeps no log (yet): say what changed
            _statusSeen = st;
            if (_bandSeen is { } before && before != st.Band && events && !st.Crippled)
                _hud.PushEvent($"Hull {BandName(st.Band).ToLowerInvariant()} ({st.Hull.ToFloat():0}%){(st.BandEffect.Length > 0 ? $": {st.BandEffect}" : "")}", BandColour(st.Band));
            _bandSeen = st.Band;
            if (st.Crippled && !_crippledSeen && events)
                _hud.PushEvent($"{Flagship?.Name ?? "The ship"} crippled: adrift, every active job failed. Field repairs bring it back to {st.FieldRepairTo}%", HelmScreen.Red);
            _crippledSeen = st.Crippled;
        }

        // The away team: going, and back.
        var ev = sys.AwayTeamOf(LocalPlayer);
        var key = AwayKey(ev);
        if (ev != null && key != _awaySeen)
        {
            if (events && !ev.Done) _hud.PushEvent($"Away team ({ev.Team.Count}) across to {SiteTitle(ev.SiteId)}: keep the ship alongside", HelmScreen.Peach);
            _awaySeen = key;
            _awayDoneSeen = ev.Done;
        }
        if (ev is { Done: true } && !_awayDoneSeen)
        {
            _awayDoneSeen = true;
            if (events) _hud.PushEvent($"Away team back from {SiteTitle(ev.SiteId)}: {ev.Outcome ?? "done"}{(ev.Result is { Length: > 0 } r ? $". {r}" : "")}", OutcomeColour(ev.Outcome));
        }

        SpaceEventAlerts(sys, events); // phase 3: a distress call / sensor alert sound (PlanetDriver.SpaceEvents.cs)
    }

    private bool _shipLogUsed;
    private ShipStatusView? _statusSeen;

    /// <summary>Event lines from the difference between two status polls (only while the sim's ship log is empty, so
    /// nothing is said twice): hull down / repaired, a system knocked out / back, crew injured / lost / back.</summary>
    private void StatusDiffEvents(ShipStatusView now, bool events)
    {
        if (_statusSeen is not { } was || !events) return;
        var name = Flagship?.Name ?? "The ship";
        var before = Mathf.RoundToInt(was.Hull.ToFloat());
        var after = Mathf.RoundToInt(now.Hull.ToFloat());
        if (after < before) _hud.PushEvent($"{name}: hull −{before - after}% (now {after}%)", after < 25 ? HelmScreen.Red : Warn);
        else if (after > before && (after == 100 || after == now.FieldRepairTo || after / 10 > before / 10))
            _hud.PushEvent($"{name}: hull repaired to {after}%", HelmScreen.Green);
        foreach (var d in now.Down)
            if (was.Down.All(x => x.System != d.System)) _hud.PushEvent($"{SystemName(d.System)} knocked out: {SystemEffect(d.System)} (back in {SecondsTo(d.BackTick)} s)", HelmScreen.Red);
        foreach (var d in was.Down)
            if (now.Down.All(x => x.System != d.System)) _hud.PushEvent($"{SystemName(d.System)} back on line", HelmScreen.Green);
        foreach (var c in now.Crew)
        {
            var old = was.Crew.FirstOrDefault(x => x.Division == c.Division);
            if (old == null) continue;
            var division = Divisions.FirstOrDefault(d => string.Equals(d.Id, c.Division, System.StringComparison.OrdinalIgnoreCase))?.Name ?? c.Division;
            if (c.Lost > old.Lost) _hud.PushEvent($"{c.Lost - old.Lost} {division} crew lost", HelmScreen.Red);
            if (c.Injured > old.Injured) _hud.PushEvent($"{c.Injured - old.Injured} {division} crew injured (back in a while)", Warn);
            else if (c.Injured < old.Injured && c.Lost == old.Lost) _hud.PushEvent($"{old.Injured - c.Injured} {division} crew back on duty", HelmScreen.Green);
        }
    }

    private static Color OutcomeColour(string? outcome)
    {
        var o = (outcome ?? "").ToLowerInvariant();
        if (o.Contains("clean") || o.Contains("success")) return HelmScreen.Green;
        if (o.Contains("lost") || o.Contains("defence") || o.Contains("defense") || o.Contains("fail") || o.Contains("recall")) return HelmScreen.Red;
        return HelmScreen.Peach;
    }

    private static string? AwayKey(AwayTeamView? ev) => ev == null ? null : $"{ev.SiteId}|{ev.Team.Count}|{string.Join(",", ev.Team.Select(m => m.Name))}";

    // ---------------------------------------------------------------- the site card (SYSTEM display)

    /// <summary>The card beside a site on the plot; false when the card should close.</summary>
    private bool ShowSiteCard(string siteId)
    {
        var report = SiteReport(siteId);
        if (report == null) return false;
        var at = _orbit.ScreenPositionOf($"site:{siteId}");
        if (at == null)
        {
            _helm.ShowCard(null, System.Array.Empty<HelmRow>(), System.Array.Empty<HelmAction>(), Vector2.Zero, HelmScreen.Peach);
            return true;
        }
        _helm.ShowCard(report.Title, SiteRows(report, true), SiteButtons(report, true), at.Value, SystemPlot.SiteColour(report.Kind, report.Knowledge));
        return true;
    }

    /// <summary>A site's rows: the subtitle, the report's rows (tones as colours), hazards, distance and reach, scan /
    /// work progress bars.</summary>
    private List<HelmRow> SiteRows(SiteReport report, bool withDistance)
    {
        var rows = new List<HelmRow>();
        var sys = _sim.System!;
        if (report.Subtitle.Length > 0) rows.Add(new HelmRow("", report.Subtitle, report.Knowledge < SiteKnowledge.Identified ? DetectedGrey : HelmScreen.Lavender));
        foreach (var r in report.Rows) rows.Add(new HelmRow(r.Label, r.Value, ToneColour(r.Tone)));
        EventSiteRows(report, rows); // phase 3: impact / tail / air bars (PlanetDriver.SpaceEvents.cs)
        if (report.Hazards.Count > 0) rows.Add(new HelmRow("HAZARDS", string.Join(" · ", report.Hazards), SpaceAmber));
        if (sys.SiteScanShare(LocalPlayer, report.SiteId) is { } scan)
            rows.Add(new HelmRow("SCAN", $"scanning {Mathf.RoundToInt(Mathf.Clamp(scan.ToFloat(), 0f, 1f) * 100)} % · stay in range", HelmScreen.Cyan, Mathf.Clamp(scan.ToFloat(), 0f, 1f)));
        if (sys.SiteWorkProgress(LocalPlayer, report.SiteId) is { } work)
            rows.Add(new HelmRow("WORK", $"{work.Label} · {work.SecondsLeft} s left", HelmScreen.Cyan, Mathf.Clamp(work.Share.ToFloat(), 0f, 1f)));
        if (withDistance && Flagship is { } f && _system.FlagshipSite != report.SiteId)
        {
            var from = _system.Flying(f) ? _system.ShipPosition(f, 0f) : _system.Body(f.AtBody) is { } b ? SystemSource.PositionOf(b) : _system.SitePosition(_system.FlagshipSite) ?? _system.ShipPosition(f, 0f);
            var to = new Vector2(report.Position.X.ToFloat(), report.Position.Y.ToFloat());
            var d = from.DistanceTo(to);
            var reach = SiteReach();
            rows.Add(new HelmRow("DISTANCE", $"{DistanceText(d)} · {(d <= reach ? "in scan range" : $"scan range {DistanceText(reach)}")}", d <= reach ? HelmScreen.Green : DetectedGrey));
            if (_system.FlagshipSiteBound == report.SiteId) rows.Add(new HelmRow("ETA", $"{_system.SecondsToArrival(f)} s", HelmScreen.Peach));
        }
        else if (_system.FlagshipSite == report.SiteId) rows.Add(new HelmRow("SHIP", "holding here", HelmScreen.Peach));
        if (sys.AwayTeamOf(LocalPlayer) is { Done: false } ev && ev.SiteId == report.SiteId)
            rows.Add(new HelmRow("AWAY TEAM", $"{ev.Team.Count} across · step {ev.Step + 1} of {ev.Steps}", HelmScreen.Peach));
        return rows;
    }

    /// <summary>The report's actions as buttons ("site:act:&lt;site&gt;:&lt;action&gt;"), greyed with the sim's problem as
    /// tooltip; SET COURSE and SCAN added when the report doesn't list them. At most HelmScreen.MaxCardButtons on a card.</summary>
    private List<HelmAction> SiteButtons(SiteReport report, bool card)
    {
        var buttons = new List<HelmAction>();
        var sys = _sim.System!;
        var f = Flagship;
        var here = _system.FlagshipSite == report.SiteId;
        var hasCourse = false;
        var hasScan = false;
        foreach (var a in report.Actions)
        {
            hasCourse |= a.Id == "course";
            hasScan |= a.Id == "scan";
            buttons.Add(new HelmAction($"site:act:{report.SiteId}:{a.Id}", "", a.Label.ToUpperInvariant(), a.Problem == null,
                Colour: a.Id == "away_team" ? HelmScreen.Lavender : null, Tip: a.Problem ?? ActionTip(a.Id)));
        }
        if (f != null && !hasScan && report.Knowledge == SiteKnowledge.Detected)
        {
            var why = sys.SiteScanProblem(LocalPlayer, f.Id, report.SiteId);
            buttons.Insert(0, new HelmAction($"site:act:{report.SiteId}:scan", "", "SCAN", why == null, Colour: HelmScreen.Cyan, Tip: why ?? "Identify it: stay within scan range while it runs"));
        }
        if (f != null && !hasCourse && !here && card)
        {
            var why = sys.SiteCourseProblem(LocalPlayer, f.Id, report.SiteId);
            buttons.Insert(0, new HelmAction($"site:act:{report.SiteId}:course", "", "SET COURSE", why == null, Tip: why ?? "Fly there and hold position at the site"));
        }
        return buttons;
    }

    private static string ActionTip(string id) => id switch
    {
        "course" => "Fly there and hold position at the site",
        "scan" => "Identify it: stay within scan range while it runs",
        "away_team" => "Send an away team across: pick who goes, then three timed steps",
        "alongside" => "Come alongside: needed before boarding",
        "sweep" => "Fly through the field slowly and recover what is there",
        "mine" => "Cut ore out of the rock: a container into the hold every 30 s while the tractor works (rock falls scuff the hull)",
        "tow" => "Lock the tractor on and take it in tow: flights are slower; drop it at a station (latinum) or a colony ship's colony",
        "release" => "Let the tow go, or stop the escort: it stays where it is",
        "stop" => "Stop the work here",
        _ => EventActionTip(id), // phase 3 (PlanetDriver.SpaceEvents.cs)
    };

    /// <summary>A site card / strip button ("site:act:&lt;site&gt;:&lt;action&gt;"): the order after the sim's check, or the
    /// away-team picker. Greyed buttons land here too: the sim's problem goes to the event feed.</summary>
    private void OnSiteAction(string id)
    {
        var rest = id["site:act:".Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0) return;
        var siteId = rest[..colon];
        var action = rest[(colon + 1)..];
        var f = Flagship;
        if (_sim.System == null || f == null)
        {
            _hud.PushEvent("Can't do that: no ship under your command", Warn);
            return;
        }
        var when = _sim.Tick + InputDelayTicks;
        string? problem;
        string done;
        switch (action)
        {
            case "course":
                problem = SpaceOrders.Course(_sim, LocalPlayer, f.Id, siteId, when);
                done = $"Course laid in for {SiteTitle(siteId)}";
                if (problem == null) CloseSystemCard();
                break;
            case "scan":
                problem = SpaceOrders.Scan(_sim, LocalPlayer, f.Id, siteId, when);
                done = $"Scanning {SiteTitle(siteId)}: stay within range until it's done";
                break;
            case "away_team":
                if (_sim.System.AwayTeamOf(LocalPlayer) is { Done: false })
                {
                    _hud.PushEvent("Can't do that: an away team is already across", Warn);
                    return;
                }
                OpenPicker(siteId);
                return;
            default:
                problem = SpaceOrders.Work(_sim, LocalPlayer, f.Id, siteId, action, when);
                done = $"{SiteTitle(siteId)}: {ActionLabel(siteId, action)}";
                break;
        }
        if (problem != null)
        {
            _hud.PushEvent($"Can't do that: {problem}", Warn);
            return;
        }
        Sfx.Ui("confirm");
        _hud.PushEvent(done, HelmScreen.Cyan);
    }

    private string ActionLabel(string siteId, string action) =>
        SiteReport(siteId)?.Actions.FirstOrDefault(a => a.Id == action)?.Label.ToLowerInvariant() ?? action.Replace('_', ' ');

    /// <summary>A click on the plot over a site: open its card. False when no site is under the mouse.</summary>
    private bool SiteClick(Vector2 mouse)
    {
        if (_orbit.PickSite(mouse) is not { } siteId) return false;
        _selectedShip = null;
        _systemCard = $"site:{siteId}";
        _orbit.HighlightSite(siteId);
        return true;
    }

    /// <summary>The cursor readout over a site on the plot; false when none is under the mouse.</summary>
    private bool SiteReadout(Vector2 mouse)
    {
        if (_mouseOverUi || _orbit.PickSite(mouse) is not { } siteId) return false;
        var mark = _orbit.SystemSites().FirstOrDefault(s => s.Id == siteId);
        if (mark == null) return false;
        var identified = mark.Knowledge >= SiteKnowledge.Identified;
        _helm.ShowReadout(identified ? SiteTitle(siteId) : "UNKNOWN CONTACT", identified ? $"{SystemPlot.KindWord(mark.Kind).ToLowerInvariant()}{(mark.Knowledge == SiteKnowledge.Spent ? " · worked out" : "")} · click for details" : "fly within range and scan it · click for details",
            SystemPlot.SiteColour(mark.Kind, mark.Knowledge), mouse + new Vector2(18, 14));
        return true;
    }

    /// <summary>SITES in the SYSTEM DATA panel (only once any is known).</summary>
    private void AddSiteSections(List<HelmSection> sections)
    {
        var sites = _orbit.SystemSites();
        if (sites.Count == 0) return;
        var rows = new List<HelmRow>();
        var here = _system.FlagshipSite;
        foreach (var s in sites)
        {
            var identified = s.Knowledge >= SiteKnowledge.Identified;
            var name = identified ? s.Title.ToUpperInvariant() : "UNKNOWN";
            var what = identified ? SystemPlot.KindWord(s.Kind).ToLowerInvariant() : "contact · scan it";
            if (s.Knowledge == SiteKnowledge.Spent) what += " · worked out";
            if (s.Id == here) what += " · here";
            rows.Add(new HelmRow(name, what, s.Id == here ? HelmScreen.Peach : identified ? SystemPlot.SiteColour(s.Kind, s.Knowledge) : DetectedGrey));
        }
        sections.Add(new HelmSection($"SITES · {sites.Count}", SystemPlot.SiteRust, rows));
    }

    // ---------------------------------------------------------------- the globe displays at a site

    /// <summary>At a site the PLANET / ORBIT / ATMOS displays show it: title, SITE DATA panel. False = not at a site.</summary>
    private bool SiteDisplayHud(OrbitDisplay display, string? hint)
    {
        if (display == OrbitDisplay.System || _orbit.SiteShown is not { } siteId) return false;
        var report = SiteReport(siteId);
        var title = report?.Title is { Length: > 0 } t ? t : "Unknown contact";
        _helm.SetTitle(title, hint ?? $"HOLDING AT THE SITE · {ShipLine()}");
        var sections = new List<HelmSection>();
        if (report != null)
            sections.Add(new HelmSection(SystemPlot.KindWord(report.Kind), SystemPlot.SiteColour(report.Kind, report.Knowledge), SiteRows(report, false)));
        else sections.Add(new HelmSection("", DetectedGrey, new List<HelmRow> { new("", "No data on this contact yet.", DetectedGrey) }));
        if (_status is { } st)
            sections.Add(new HelmSection("SHIP", BandColour(st.Band), new List<HelmRow>
            {
                new("HULL", $"{st.Hull.ToFloat():0}% · {BandName(st.Band).ToLowerInvariant()}", BandColour(st.Band), Mathf.Clamp(st.Hull.ToFloat() / 100f, 0f, 1f)),
                new("CREW", $"{st.CrewTotal} of {st.CrewStart} on duty"),
                new("HOLD", $"{st.HoldUsed} of {st.HoldSize} containers"),
            }));
        _helm.SetPanel("SITE DATA", sections);
        return true;
    }

    /// <summary>The site's actions on the action strip while the flagship holds there.</summary>
    private void AddSiteStripActions(List<HelmAction> actions)
    {
        if (_system.FlagshipSite is not { } siteId || SiteReport(siteId) is not { } report) return;
        actions.AddRange(SiteButtons(report, false).Select(a => a with { Colour = a.Colour ?? HelmScreen.Peach }));
    }

    // ---------------------------------------------------------------- away team

    private IReadOnlyList<FinalFrontier.Sim.Data.FitOutDivision> Divisions =>
        _data.FitOut?.Divisions ?? (IReadOnlyList<FinalFrontier.Sim.Data.FitOutDivision>)System.Array.Empty<FinalFrontier.Sim.Data.FitOutDivision>();

    private int OnDuty(string divisionId) =>
        _status?.Crew.FirstOrDefault(c => string.Equals(c.Division, divisionId, System.StringComparison.OrdinalIgnoreCase))?.OnDuty ?? 0;

    /// <summary>Open the picker for a site: a first pick spread over the divisions up to the ship's maximum (4 at most).</summary>
    private void OpenPicker(string siteId)
    {
        var divisions = Divisions;
        _pickSite = siteId;
        _pickCrew = new int[divisions.Count];
        var max = _sim.System?.MaxAwayTeam(LocalPlayer) ?? 0;
        var want = System.Math.Min(max, 4);
        for (var round = 0; want > 0 && round < 6; round++)
            for (var i = 0; i < divisions.Count && want > 0; i++)
                if (_pickCrew[i] < OnDuty(divisions[i].Id))
                {
                    _pickCrew[i]++;
                    want--;
                }
        _statusScreen.Close();
    }

    private void OnPickCrew(int division, int delta)
    {
        if (division < 0 || division >= _pickCrew.Length) return;
        var max = _sim.System?.MaxAwayTeam(LocalPlayer) ?? 0;
        var total = _pickCrew.Sum();
        var next = _pickCrew[division] + delta;
        if (next < 0 || next > OnDuty(Divisions[division].Id) || (delta > 0 && total >= max)) return;
        _pickCrew[division] = next;
    }

    private void LaunchAwayTeam()
    {
        var f = Flagship;
        if (_pickSite == null || f == null) return;
        var crew = (int[])_pickCrew.Clone();
        var why = SpaceOrders.AwayTeam(_sim, LocalPlayer, f.Id, _pickSite, crew, _sim.Tick + InputDelayTicks);
        if (why != null)
        {
            _hud.PushEvent($"Can't send the away team: {why}", Warn);
            return;
        }
        _hud.PushEvent($"Away team of {crew.Sum()} ordered across to {SiteTitle(_pickSite)}", HelmScreen.Peach);
        _pickSite = null;
    }

    private void OnAwayChoice(string siteId, int choice)
    {
        var ev = _sim.System?.AwayTeamOf(LocalPlayer);
        var why = SpaceOrders.AwayChoice(_sim, LocalPlayer, siteId, choice, _sim.Tick + InputDelayTicks);
        if (why != null) _hud.PushEvent($"Can't do that: {why}", Warn);
        else if (ev != null && choice < ev.Choices.Count) _hud.PushEvent($"Away team: {ev.Choices[choice].Label}", HelmScreen.Peach);
    }

    private void UpdateAwayPanel(StarSystemState? sys)
    {
        AwayPickerView? picker = null;
        AwayEventView? view = null;
        var ev = sys?.AwayTeamOf(LocalPlayer);
        if (ev != null && (!ev.Done || AwayKey(ev) != _awayDismissed))
        {
            view = EventViewOf(sys!, ev);
            if (!ev.Done) _pickSite = null; // a team is across: the picker gives way
        }
        else if (_pickSite != null && sys != null)
        {
            if (_system.FlagshipSite != _pickSite) _pickSite = null; // left the site
            else
            {
                // the sim's problem check (and who would go) 4×/s, or at once when the picks change
                var key = $"{_pickSite}|{string.Join(",", _pickCrew)}";
                if (_pickerView == null || key != _pickerKey || _clock - _pickerAt >= 0.25)
                {
                    _pickerView = PickerView(sys, _pickSite);
                    _pickerKey = key;
                    _pickerAt = _clock;
                }
                picker = _pickerView;
            }
        }
        if (_dossier.IsOpen) picker = null;
        var screen = GetViewport().GetVisibleRect().Size;
        if (_inOrbit)
        {
            var rect = _helm.OpenRect;
            var top = rect.Size.Y > 32 ? rect.Position.Y + 10f : 80f;
            if (_contactPanel.Visible) top += 180f * HudKit.ScaleFor(screen); // first contact sits at the top too
            _awayPanel.Show(picker, view, rect.Size.X > 32 ? rect.Position.X + rect.Size.X / 2f : screen.X / 2f, top);
        }
        else _awayPanel.Show(picker, view, screen.X / 2f, PlanetHud.TopHeight + 96f);
    }

    private AwayPickerView PickerView(StarSystemState sys, string siteId)
    {
        var divisions = Divisions;
        if (_pickCrew.Length != divisions.Count) _pickCrew = new int[divisions.Count];
        var rows = new List<AwayDivisionRow>();
        for (var i = 0; i < divisions.Count; i++) rows.Add(new AwayDivisionRow(divisions[i].Id, divisions[i].Name, _pickCrew[i], OnDuty(divisions[i].Id)));
        var crew = (IReadOnlyList<int>)_pickCrew;
        var problem = _pickCrew.Sum() == 0 ? "pick at least one crew member" : sys.AwayTeamProblem(LocalPlayer, siteId, crew);
        var preview = new List<string>();
        preview.AddRange(sys.AwayTeamPreview(LocalPlayer, siteId, crew).Select(m => m.Role.Length > 0 ? $"{m.Name}, {m.Role}" : m.Name));
        const string note = "Each Defense member lowers the loss chance; a Science medic turns half of all losses into injuries. Injured crew are out for a while, lost crew are gone for the match. Leaving the site recalls the team: the job fails with no reward.";
        return new AwayPickerView(siteId, SiteTitle(siteId), rows, sys.MaxAwayTeam(LocalPlayer), problem, preview, note);
    }

    private AwayEventView EventViewOf(StarSystemState sys, AwayTeamView ev)
    {
        var rate = (float)Simulation.TickRate;
        var chosen = ev.Step >= 0 && ev.Step < ev.Picked.Count ? ev.Picked[ev.Step] : -1;
        var problems = new string?[ev.Choices.Count];
        if (!ev.Done && chosen < 0)
            for (var i = 0; i < problems.Length; i++) problems[i] = sys.AwayChoiceProblem(LocalPlayer, ev.SiteId, i);
        var probe = sys.Site(ev.SiteId) is { Kind: SiteKind.AlienProbe }; // phase 4: the answer to a probe runs on the same event, with no team
        var title = probe ? $"ANSWER TO {SiteTitle(ev.SiteId).ToUpperInvariant()}" : $"AWAY TEAM · {SiteTitle(ev.SiteId).ToUpperInvariant()}";
        var sub = probe ? ev.Done ? "ANSWER SENT" : "STAY WITH THE PROBE: LEAVING CALLS THE ANSWER OFF"
            : ev.Done ? $"{ev.Team.Count} CREW · BACK ABOARD" : "KEEP THE SHIP ALONGSIDE: LEAVING RECALLS THE TEAM (JOB FAILED, NO REWARD)";
        return new AwayEventView(ev.SiteId, title, sub, ev.Step, System.Math.Max(1, ev.Steps), ev.StepTitle,
            Mathf.Max(0f, (ev.StepEndTick - _sim.Tick) / rate), Mathf.Max(1f, (ev.StepEndTick - ev.StepStartTick) / rate),
            ev.Choices, problems, chosen, ev.Picked, ev.Team, Mathf.Clamp(ev.InjuryPct, 0, 100), Mathf.Clamp(ev.LossPct, 0, 100), Mathf.Clamp(ev.FindPct, 0, 100),
            ev.Outcome, ev.Done ? ev.Result ?? "The team is back aboard." : null, OutcomeColour(ev.Outcome));
    }

    // ---------------------------------------------------------------- ship status screen

    private ShipStatusPage StatusPage(ShipStatusView v)
    {
        var sys = _sim.System!;
        var f = Flagship;
        var hull = Mathf.Clamp(v.Hull.ToFloat(), 0f, 100f);
        var band = BandColour(v.Band);
        var title = $"SHIP STATUS · {(f?.Name ?? "FLAGSHIP").ToUpperInvariant()}{(f is { Registry.Length: > 0 } ? $" · {f.Registry}" : "")}";
        var subtitle = $"{(f != null ? _system.ShipClassText(f) : "")} · CREW {v.CrewTotal} OF {v.CrewStart} · HOLD {v.HoldUsed} OF {v.HoldSize}".ToUpperInvariant();
        var bandText = v.Crippled ? "Crippled: adrift, can't set course; every active job failed. Field repairs bring it back."
            : $"{BandName(v.Band)}{(v.BandEffect.Length > 0 ? $": {v.BandEffect}" : ": no effect yet")}";
        var bandLines = new List<string>
        {
            "Worn: flights a little slower · Damaged: flights slower, shorter scan reach",
            "Critical: no hazardous jobs, flights much slower · Crippled at 0: adrift, active jobs fail",
        };

        // Left: repairs, the log.
        var left = new List<StatusSection>();
        string? Problem(string action) => sys.RepairProblem(LocalPlayer, action);
        var kits = v.Hold.Where(h => h.Item.Contains("patch")).Sum(h => h.Count);
        var patch = Problem("patch");
        var dock = Problem("dock");
        left.Add(new StatusSection("REPAIRS", HelmScreen.Sky, new List<StatusLine>
        {
            new("FIELD", hull < v.FieldRepairTo ? $"Engineering crew patching the hull up to {v.FieldRepairTo}%" : $"Engineering crew patch the hull up to {v.FieldRepairTo}% (above it now)",
                hull < v.FieldRepairTo ? HelmScreen.Cyan : HelmScreen.Ink),
            new("PATCH KITS", $"{kits} in the hold", kits > 0 ? HelmScreen.Ink : DetectedGrey, Buttons: new[] { new StatusButton("repair:patch", "FIT PATCH KIT", patch == null, patch ?? "Fit a hull patch kit from the hold", HelmScreen.Peach) }),
            new("DOCKYARD", "a station, or a colony ship's colony: back to 100%", HelmScreen.Ink, Buttons: new[] { new StatusButton("repair:dock", "DOCK FOR REPAIR", dock == null, dock ?? "Full repair at the dockyard here", HelmScreen.Lavender) }),
        }));
        var log = v.Log.OrderByDescending(l => l.Tick).Take(8)
            .Select(l => new StatusLine(GameClock(l.Tick), l.Text, ToneColour(l.Tone))).ToList();
        left.Add(new StatusSection("SHIP LOG", HelmScreen.Lavender, log.Count > 0 ? log : new List<StatusLine> { new("", "Nothing logged yet.", DetectedGrey) }));

        // Right: systems, crew, hold, hiring.
        var right = new List<StatusSection>();
        var systems = new List<StatusLine>();
        foreach (var s in System.Enum.GetValues<ShipSystem>())
        {
            var down = v.Down.FirstOrDefault(d => d.System == s);
            if (down == null)
            {
                systems.Add(new StatusLine(SystemName(s), "OK", HelmScreen.Green));
                continue;
            }
            var action = $"fix:{s.ToString().ToLowerInvariant()}";
            var why = Problem(action);
            systems.Add(new StatusLine(SystemName(s), $"Knocked out · {SystemEffect(s)} · back in {Clock((int)SecondsTo(down.BackTick))}", HelmScreen.Red,
                Buttons: new[] { new StatusButton($"repair:{action}", "FIX", why == null, why ?? "A patch kit fixes it at once", HelmScreen.Peach) }));
        }
        right.Add(new StatusSection("SYSTEMS", HelmScreen.Sky, systems, "A hazard can knock one out; it comes back on its own, or a patch kit fixes it at once."));

        var crew = new List<StatusLine>();
        foreach (var c in v.Crew)
        {
            var total = System.Math.Max(1, c.OnDuty + c.Injured + c.Lost);
            var name = Divisions.FirstOrDefault(d => string.Equals(d.Id, c.Division, System.StringComparison.OrdinalIgnoreCase))?.Name ?? c.Division;
            var parts = new List<string> { $"{c.OnDuty} on duty" };
            if (c.Injured > 0) parts.Add($"{c.Injured} injured");
            if (c.Lost > 0) parts.Add($"{c.Lost} lost");
            var below = c.OnDuty < c.Floor;
            if (below) parts.Add("below the floor: its effects stop");
            crew.Add(new StatusLine(name, string.Join(" · ", parts), below ? HelmScreen.Red : AwayTeamPanel.DivisionColour(c.Division.ToLowerInvariant()),
                c.OnDuty / (float)total, c.Floor > 0 ? c.Floor / (float)total : null));
        }
        var crewNote = "Red tick = the division's floor. Injured crew come back after a while; lost crew are gone for the match.";
        if (v.CrewTotal * 2 < v.CrewStart) crewNote = "Below half the starting crew: flights are slower. " + crewNote;
        right.Add(new StatusSection($"CREW · {v.CrewTotal} OF {v.CrewStart}", HelmScreen.Lavender, crew.Count > 0 ? crew : new List<StatusLine> { new("", "No crew data.", DetectedGrey) }, crewNote));

        var hold = new List<StatusLine> { new("USED", $"{v.HoldUsed} of {v.HoldSize} containers", v.HoldUsed >= v.HoldSize ? SpaceAmber : HelmScreen.Ink, v.HoldSize > 0 ? v.HoldUsed / (float)v.HoldSize : 0f) };
        foreach (var h in v.Hold)
        {
            var buttons = new List<StatusButton>();
            foreach (var (action, label) in new[] { ("sell", "SELL"), ("drop", "DROP"), ("jettison", "JETTISON") })
                if (sys.HoldProblem(LocalPlayer, action, h.Item, h.Count) == null)
                {
                    var shown = h.Item == StarSystemState.TowItem && action == "jettison" ? "RELEASE" : h.Item == StarSystemState.EscortItem ? "STOP ESCORT" : label;
                    var tip = h.Item.StartsWith(StarSystemState.OrePrefix) && action == "sell"
                        ? $"sell {h.Count} {h.Label.ToLowerInvariant()} for about {sys.OreValue(h.Item, h.Count, _system.FlagshipSite == null)} latinum"
                        : h.Item == StarSystemState.TowItem ? (action == "drop" ? "hand the tow over for its salvage" : "let the tow go here")
                        : h.Item == StarSystemState.EscortItem ? "stop the escort: the ship stays adrift where we are"
                        : $"{label.ToLowerInvariant()} {h.Count} {h.Label}";
                    buttons.Add(new StatusButton($"hold:{action}:{h.Count}:{h.Item}", shown, true, tip, action == "jettison" ? HelmScreen.Red : HelmScreen.Peach));
                }
            hold.Add(new StatusLine(h.Label, $"{h.Count}{(h.Containers > 0 ? $" · {h.Containers} container{(h.Containers == 1 ? "" : "s")}" : "")}", HelmScreen.Ink, Buttons: buttons));
        }
        right.Add(new StatusSection("HOLD", SpaceAmber, hold, $"Ore ({_data.SpaceSites?.OrePerContainer ?? 30} to a container), salvage and survivors take hold space: a full hold stops mining and pick-ups. Ore sells at a station or to a freighter in orbit with you; a colony ship can drop it to its colony."));

        var hire = new List<StatusLine>();
        foreach (var d in Divisions)
            if (sys.HoldProblem(LocalPlayer, "hire", d.Id, 2) == null)
                hire.Add(new StatusLine(d.Name, "hire 2 at this station", AwayTeamPanel.DivisionColour(d.Id), Buttons: new[] { new StatusButton($"hold:hire:2:{d.Id}", "HIRE", true, $"Hire 2 {d.Name} crew", HelmScreen.Green) }));
        if (hire.Count > 0) right.Add(new StatusSection("HIRE CREW", HelmScreen.Green, hire));
        return new ShipStatusPage(title, subtitle, hull / 100f, band, v.Crippled ? "ADRIFT" : $"{hull:0}%", bandText, bandLines, left, right);
    }

    /// <summary>A sim tick as game time "14:22" (minutes:seconds since the start).</summary>
    private static string GameClock(long tick)
    {
        var s = System.Math.Max(0, tick / Simulation.TickRate);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The status screen's buttons: "repair:&lt;action&gt;" and "hold:&lt;action&gt;:&lt;count&gt;:&lt;item&gt;".</summary>
    private void OnStatusAction(string id)
    {
        var f = Flagship;
        if (_sim.System == null || f == null) return;
        var when = _sim.Tick + InputDelayTicks;
        string? why;
        string done;
        if (id.StartsWith("repair:"))
        {
            var action = id[7..];
            why = SpaceOrders.Repair(_sim, LocalPlayer, f.Id, action, when);
            done = action == "patch" ? "Fitting a hull patch kit" : action == "dock" ? "Docking for a full repair" : $"Patch kit on the {action[4..]}";
        }
        else if (id.StartsWith("hold:"))
        {
            var parts = id.Split(':', 4);
            if (parts.Length < 4 || !int.TryParse(parts[2], out var count)) return;
            why = SpaceOrders.Hold(_sim, LocalPlayer, f.Id, parts[1], parts[3], count, when);
            done = $"Hold: {parts[1]} {count} {parts[3].Replace('_', ' ')}";
        }
        else return;
        if (why != null)
        {
            _hud.PushEvent($"Can't do that: {why}", Warn);
            return;
        }
        Sfx.Ui("confirm");
        _hud.PushEvent(done, HelmScreen.Cyan);
        _spacePollAt = -1; // refresh the page at once
    }

    // ---------------------------------------------------------------- keys

    /// <summary>U in orbit: the ship status screen; Esc closes the picker, then the screen. True = taken.</summary>
    private bool SpaceKey(InputEventKey key)
    {
        if (key.Keycode == Key.U && _sim.System != null && Flagship != null)
        {
            ToggleShipStatus();
            return true;
        }
        if (key.Keycode != Key.Escape) return false;
        if (_pickSite != null)
        {
            _pickSite = null;
            return true;
        }
        if (_statusScreen.IsOpen)
        {
            _statusScreen.Close();
            return true;
        }
        return false;
    }
}
