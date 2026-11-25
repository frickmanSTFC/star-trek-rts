using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 3 on the helm (docs/space-jobs-contract.md "Phase 3"; render-only, commands only): an alert for each
// event as it arrives (StarSystemState.EventNotes; the ship log already puts the text in the event feed): a distress call
// (lifepods, a stranded ship) plays the alarm twice, a sensor alert (a rogue asteroid, a comet, an ion storm) the tutorial
// chime; and the event cards' bars: a rogue's time to impact, a comet's tail window, each waiting lifepod's air, a
// stranded ship's air, the storm's recording. Tips for the new site buttons.
public partial class PlanetDriver
{
    private int _eventNotesSeen = -1;

    /// <summary>From SpaceEvents (4×/s): a sound for each new announcement (none for the ones there at the start).</summary>
    private void SpaceEventAlerts(StarSystemState sys, bool events)
    {
        var count = sys.EventNoteCount;
        if (_eventNotesSeen < 0 || !events)
        {
            _eventNotesSeen = count;
            return;
        }
        if (count == _eventNotesSeen) return;
        var fresh = System.Math.Min(count - _eventNotesSeen, sys.EventNotes.Count);
        _eventNotesSeen = count;
        var distress = false;
        var alert = false;
        for (var i = sys.EventNotes.Count - fresh; i < sys.EventNotes.Count; i++)
        {
            var n = sys.EventNotes[i];
            if (n.Tick < _sim.Tick - 2 * Simulation.TickRate) continue;
            if (sys.Site(n.SiteId) is { } s && s.SpawnTick != n.Tick) continue; // an ending, not an arrival
            if (n.Distress) distress = true;
            else alert = true;
        }
        if (distress)
        {
            Sfx.Ui("error", -2f, 0.8f);
            Sfx.UiLater("error", 0.35f);
        }
        else if (alert) Sfx.Ui("tutorial", -4f, 0.85f);
    }

    /// <summary>Bars on an event site's card (after the report's own rows).</summary>
    private void EventSiteRows(SiteReport report, List<HelmRow> rows)
    {
        if (_sim.System is not { } sys || sys.Site(report.SiteId) is not { } s || s.Outcome.Length > 0) return;
        var now = _sim.Tick;
        float Left(long start, long end) => end <= start ? 0f : Mathf.Clamp((end - now) / (float)(end - start), 0f, 1f);
        string Clock(long tick)
        {
            var sec = SecondsTo(tick);
            return $"{sec / 60}:{sec % 60:00}";
        }
        switch (s.Kind)
        {
            case SiteKind.RogueAsteroid when s.PathEnd >= 0:
                rows.Add(new HelmRow("TO IMPACT", $"{Clock(s.PathEnd)} · {_system.Body(s.TargetBody)?.Name ?? "a body"}", HelmScreen.Red, Left(s.PathStart, s.PathEnd)));
                break;
            case SiteKind.Comet when s.PathStart >= 0:
            {
                var (ws, we) = sys.TailWindow(s);
                if (now < ws) rows.Add(new HelmRow("TAIL", $"active in {Clock(ws)}", SystemPlot.SiteAmber, 1f - Left(s.PathStart, ws)));
                else if (now <= we) rows.Add(new HelmRow("TAIL", $"active · {Clock(we)} left to sample", HelmScreen.Cyan, Left(ws, we)));
                break;
            }
            case SiteKind.Lifepods:
            {
                // Each waiting pod (least air first, at most six) with its air as a bar.
                var shown = 0;
                var waiting = 0;
                var maxAir = 1L;
                for (var k = 0; k < s.PodCount && k < 31; k++)
                    maxAir = System.Math.Max(maxAir, sys.PodAirTick(s, k) - s.SpawnTick);
                var pods = sys.PodsOf(s.Id);
                var order = new List<PodView>();
                foreach (var p in pods)
                    if (p.State == "waiting") order.Add(p);
                order.Sort((a, b) => a.AirTick != b.AirTick ? a.AirTick.CompareTo(b.AirTick) : a.Index.CompareTo(b.Index));
                waiting = order.Count;
                foreach (var p in order)
                {
                    if (shown++ >= 6) break;
                    var share = Mathf.Clamp((p.AirTick - now) / (float)maxAir, 0f, 1f);
                    rows.Add(new HelmRow($"POD {p.Index + 1}", $"{p.Survivors} aboard · air {Clock(p.AirTick)}", share < 0.25f ? HelmScreen.Red : share < 0.5f ? Warn : HelmScreen.Cyan, share));
                }
                if (waiting > 6) rows.Add(new HelmRow("", $"+{waiting - 6} more pods waiting", DetectedGrey));
                break;
            }
            case SiteKind.StrandedShip when s.AirTick >= 0:
                rows.Add(new HelmRow("AIR", $"{Clock(s.AirTick)} left aboard", HelmScreen.Red, Left(s.SpawnTick, s.AirTick)));
                break;
            case SiteKind.Anomaly when sys.SiteKindOf(s)?.Anomaly is { } an && sys.SiteWork(LocalPlayer, s.Id) is { } w:
                rows.Add(new HelmRow("RECORDED", $"{w.Recorded} of {an.ResearchCap} research", HelmScreen.Lavender, Mathf.Clamp(w.Recorded / (float)System.Math.Max(1, an.ResearchCap), 0f, 1f)));
                break;
        }
    }

    /// <summary>Tips for the phase 3 site buttons ("" for any other).</summary>
    private static string EventActionTip(string id) => id switch
    {
        "push" => "Hold alongside and lean on the rock with the tractor until it is turned aside (Engineering makes it quicker); the tractor must work",
        "sample" => "Fly through the comet's active tail and sample it (30 s): research, and ice for deuterium; the dust scuffs the hull",
        "pickup" => "Bring the lifepods in with the tractor, least air first, one every few seconds; survivors join the crew while it is short",
        "escort" => "It follows the ship: set course for its destination (flights a little slower); reaching orbit there rescues it",
        "record_outer" => "Hold at the storm's outer ring and record: slow research, no harm",
        "record_middle" => "Hold at the middle ring and record: more research, a little hull wear",
        "record_inner" => "Hold at the inner ring: the most research, hull wear, and discharges can knock out sensors or engines",
        "restore_power" => "Engineering crew restore its power (60 s); a stranded ship's air stops running out",
        "restore_power_kit" => "Fit a hull patch kit to restore its power at once",
        _ => "",
    };
}
