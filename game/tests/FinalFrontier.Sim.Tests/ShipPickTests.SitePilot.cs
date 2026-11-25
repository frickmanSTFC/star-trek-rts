using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Tests;

public partial class ShipPickTests
{
    /// <summary>The pilot's part for the chain missions built on space sites (docs/space-jobs-contract.md phase 4): from the
    /// markers, the site cards and their problems, commands only, once a second.</summary>
    private sealed partial class Pilot
    {
        /// <summary>Spots (rock ids) the pilot has scanned from while hunting a hidden ship.</summary>
        private readonly List<string> _fixedFrom = new();

        public bool SawSearchArea, SawHiddenFound, AnsweredProbe;

        private SiteKnowledge Know(string site) => Sys.SiteKnowledgeOf(0, site);

        private void SendIfOk(Command c, string? problem, string what)
        {
            if (problem != null) return;
            Log.Add($"{_sim.Tick / Simulation.TickRate} s: {what}");
            Send(c);
        }

        /// <summary>Flies to the site (once; nothing while in flight or already there).</summary>
        private void GoSite(string site)
        {
            if (Ship.InFlight || Ship.AtSite == site) return;
            SendIfOk(new SiteCourseCommand(0, Ship.Id, site), Sys.SiteCourseProblem(0, Ship.Id, site), $"course for site {site}");
        }

        private void Work(string site, string action) =>
            SendIfOk(new SiteWorkCommand(0, Ship.Id, site, action), Sys.SiteWorkProblem(0, Ship.Id, site, action), $"{action} at {site}");

        /// <summary>At the site, scanned, then the first of the actions that can run now. True while it is busy there.</summary>
        private void WorkSite(string site, params string[] actions)
        {
            if (Ship.AtSite != site)
            {
                GoSite(site);
                return;
            }
            if (Know(site) == SiteKnowledge.Detected)
            {
                if (Sys.SiteScanShare(0, site) == null) SendIfOk(new SiteScanCommand(0, Ship.Id, site), Sys.SiteScanProblem(0, Ship.Id, site), $"scan {site}");
                return;
            }
            if (Sys.WorkActionOf(0) != null || Sys.AwayTeamOf(0) is { Done: false }) return; // busy
            foreach (var a in actions)
                if (Sys.SiteWorkProblem(0, Ship.Id, site, a) == null)
                {
                    Work(site, a);
                    return;
                }
        }

        /// <summary>Throws a used-up fit-out line (probe, satellite, post) out of the hold to make room.</summary>
        private bool Jettison()
        {
            foreach (var line in Sys.StatusOf(0)!.Hold)
                if (Sys.HoldProblem(0, "jettison", line.Item, 1) == null && !line.Item.StartsWith(StarSystemState.SurvivorsPrefix, StringComparison.Ordinal)
                    && !line.Item.StartsWith(StarSystemState.SalvagePrefix, StringComparison.Ordinal) && line.Item != StarSystemState.PatchKitItem)
                {
                    SendIfOk(new ShipHoldCommand(0, Ship.Id, "jettison", line.Item, 1), null, $"jettison {line.Item}");
                    return true;
                }
            return false;
        }

        /// <summary>An away team (or the probe's reply): sends it when it can go, then picks choice `pick` at each step.</summary>
        private void Team(string site, int pick)
        {
            if (Sys.AwayTeamOf(0) is { Done: false } v)
            {
                if (v.SiteId == site && v.Step < v.Steps && v.Picked[v.Step] < 0)
                    SendIfOk(new AwayTeamChoiceCommand(0, site, System.Math.Min(pick, v.Choices.Count - 1)), Sys.AwayChoiceProblem(0, site, pick), $"choice {pick}");
                return;
            }
            var team = SpaceJobs.Team(_sim);
            SendIfOk(new AwayTeamCommand(0, Ship.Id, site, team), Sys.AwayTeamProblem(0, site, team), $"away team to {site}");
        }

        private int SurvivorsAboard() => Sys.StatusOf(0)!.Hold.Where(l => l.Item.StartsWith(StarSystemState.SurvivorsPrefix, StringComparison.Ordinal)).Sum(l => l.Count);

        private static string? SiteOf(IEnumerable<SpaceMarker> markers, string label) => markers.FirstOrDefault(m => m.Kind == "site" && m.Label == label)?.SiteId;

        /// <summary>The phase 4 missions; false for any other (the chain's own logic runs).</summary>
        private bool SiteMission(MissionView view, IReadOnlyList<SpaceMarker> markers)
        {
            var sites = markers.Where(m => m.SiteId != null).ToList();
            switch (view.Id)
            {
                case "wreck_at_the_edge":
                {
                    if (sites.FirstOrDefault()?.SiteId is not { } field) return true;
                    if (Ship.AtSite == field && Know(field) >= SiteKnowledge.Identified && Sys.WorkActionOf(0) == null
                        && Sys.SiteWorkProblem(0, Ship.Id, field, "sweep") is { } p && p.Contains("hold is full") && Jettison()) return true;
                    WorkSite(field, "sweep");
                    return true;
                }
                case "hollow_rock":
                {
                    if (view.StageIndex == 0)
                    {
                        var next = sites.Where(m => Know(m.SiteId!) == SiteKnowledge.Detected).Select(m => m.SiteId!)
                            .OrderBy(s => FixVec2.Distance(Sys.Site(s)!.Position, Sys.ShipPosition(Ship, _sim.Tick))).ThenBy(s => s, StringComparer.Ordinal).FirstOrDefault();
                        if (next != null) WorkSite(next);
                        return true;
                    }
                    if (SiteOf(sites, "Hollow rock") is not { } rock) return true;
                    if (Sys.SiteWork(0, rock) is { Alongside: true }) Team(rock, 0);
                    else WorkSite(rock, "alongside");
                    return true;
                }
                case "tow_line":
                {
                    if (SiteOf(sites, "Disabled freighter") is not { } hauler) return true;
                    if (Sys.TowOf(0) == hauler)
                    {
                        if (markers.FirstOrDefault(m => m.Kind == "target")?.BodyId is { } port) Go(port);
                        return true;
                    }
                    WorkSite(hauler, "tow", "alongside");
                    return true;
                }
                case "twelve_lifepods":
                {
                    var liner = SiteOf(sites, "Lifepods");
                    var station = SiteOf(sites, "Set them down here");
                    var picked = liner != null ? Sys.SiteWork(0, liner)?.Picked ?? 0 : 0;
                    var aboard = SurvivorsAboard();
                    var pickup = liner != null && Ship.AtSite == liner ? Sys.SiteWorkProblem(0, Ship.Id, liner, "pickup") : null;
                    var noRoom = pickup != null && pickup.Contains("no room");
                    var left = liner != null && Sys.Site(liner) is { } ls ? Sys.PodsOf(ls.Id).Count(p => p.State == "waiting") : 0;
                    if (station != null && aboard > 0 && (noRoom || picked >= 6 || left == 0 || (Ship.AtSite == station)))
                    {
                        if (Ship.AtSite != station) GoSite(station);
                        else SendIfOk(new ShipHoldCommand(0, Ship.Id, "drop", "survivors", aboard), Sys.HoldProblem(0, "drop", "survivors", aboard), $"drop {aboard} survivors");
                        return true;
                    }
                    if (noRoom && aboard == 0 && Sys.WorkActionOf(0) == null && Jettison()) return true;
                    if (liner != null && left > 0 && picked < 12) WorkSite(liner, "pickup");
                    return true;
                }
                case "watch_rogue_rock":
                {
                    if (SiteOf(sites, "Rogue rock") is { } rock) WorkSite(rock, "push", "alongside");
                    return true;
                }
                case "smugglers_belt":
                {
                    if (view.StageIndex == 0)
                    {
                        SawSearchArea |= markers.Any(m => m.Kind == "search");
                        var spots = sites.Where(m => m.Label == "Scan from here").Select(m => m.SiteId!).ToList();
                        if (Ship.AtSite is { } here && spots.Contains(here) && !_fixedFrom.Contains(here))
                        {
                            if (Sys.ScanProblem(0, Ship.Id) == null)
                            {
                                _fixedFrom.Add(here);
                                SendIfOk(new LongRangeScanCommand(0, Ship.Id), null, $"fix scan from {here}");
                            }
                            return true;
                        }
                        var next = spots.FirstOrDefault(s => !_fixedFrom.Contains(s));
                        if (next == null) // every rock tried: scan again from each in turn
                        {
                            _fixedFrom.Clear();
                            next = spots.FirstOrDefault(s => s != Ship.AtSite);
                        }
                        if (next != null) GoSite(next);
                        return true;
                    }
                    SawHiddenFound = true;
                    if (SiteOf(sites, "Smuggler") is { } ship) WorkSite(ship, "inspect", "alongside");
                    return true;
                }
                case "probe_that_talks":
                {
                    if (SiteOf(sites, "The probe") is not { } probe) return true;
                    if (Know(probe) == SiteKnowledge.Detected)
                    {
                        if (Sys.SiteScanShare(0, probe) == null && Sys.SiteScanProblem(0, Ship.Id, probe) == null)
                            SendIfOk(new SiteScanCommand(0, Ship.Id, probe), null, "scan the probe");
                        return true; // hold still: it comes to us
                    }
                    if (view.StageIndex == 1 && Ship.AtSite == probe)
                    {
                        if (Sys.AwayTeamOf(0) is { Done: false } v)
                        {
                            AnsweredProbe = true;
                            if (v.Step < v.Steps && v.Picked[v.Step] < 0) SendIfOk(new AwayTeamChoiceCommand(0, probe, 1), Sys.AwayChoiceProblem(0, probe, 1), "answer");
                            return true;
                        }
                    }
                    WorkSite(probe, "answer", "decode");
                    return true;
                }
                case "their_lost_ship":
                {
                    if (SiteOf(sites, "Their lost ship") is not { } lost) return true;
                    switch (view.StageIndex)
                    {
                        case 0:
                            WorkSite(lost, "alongside");
                            return true;
                        case 1:
                            SendIfOk(new MissionChoiceCommand(0, view.Id, "tow"), _sim.Missions.ChoiceProblem(new MissionChoiceCommand(0, view.Id, "tow")), "tow her home");
                            return true;
                        case 2:
                            if (Sys.TowOf(0) == lost)
                            {
                                if (markers.FirstOrDefault(m => m.Kind == "target")?.BodyId is { } world) Go(world);
                                else if (Sys.Bodies.FirstOrDefault(b => Sys.CivAt(b.Id) is { Living: true }) is { } civ) Go(civ.Id);
                            }
                            else WorkSite(lost, "tow", "alongside");
                            return true;
                    }
                    return true; // holding orbit
                }
            }
            return false;
        }
    }
}
