using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>Space sites (docs/space-jobs-contract.md): rolled from the system's seed, found by long-range scans, flown to
/// (holding position: not in orbit, not in flight), scanned, worked; refusals change nothing.</summary>
public class SpaceSiteTests
{
    [Fact]
    public void SitesAreRolledFromTheSeedBetweenTheBodies()
    {
        var a = Game();
        var b = Game();
        var rules = Campaign.SpaceSites!;
        var sites = a.System!.Sites;
        Assert.Equal(sites.Select(s => (s.Id, s.Kind, s.Variant, s.Name, s.Position, s.Size)), b.System!.Sites.Select(s => (s.Id, s.Kind, s.Variant, s.Name, s.Position, s.Size)));
        Assert.Single(sites, s => s.Kind == SiteKind.Station);
        Assert.InRange(sites.Count(s => s.Kind is not (SiteKind.Station or SiteKind.Asteroid or SiteKind.Anomaly)), 0, rules.CountMax); // asteroids: phase 2, anomalies: phase 3, their own counts
        foreach (var s in sites)
        {
            Assert.All(a.System.Bodies, body => Assert.True(FixVec2.Distance(body.Position, s.Position) >= rules.ClearOfBody, $"{s.Id} too near {body.Id}"));
            Assert.InRange(s.Size, 1, 5);
            Assert.Equal(s.Kind == SiteKind.Station ? SiteKnowledge.Identified : SiteKnowledge.Unknown, a.System.SiteKnowledgeOf(0, s.Id));
        }
        // Over many seeds every phase 1 kind turns up, and the counts stay in the data's range.
        var kinds = new HashSet<SiteKind>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var g = Game(null, seed);
            Assert.InRange(g.System!.Sites.Count(s => s.Kind is not (SiteKind.Station or SiteKind.Asteroid or SiteKind.Anomaly)), 0, rules.CountMax);
            foreach (var s in g.System.Sites) kinds.Add(s.Kind);
        }
        Assert.Superset(new HashSet<SiteKind> { SiteKind.Station, SiteKind.Derelict, SiteKind.KnownDerelict, SiteKind.Debris }, kinds);
    }

    [Fact]
    public void PlanetOnlyGamesHaveNoStarSystemSoNoSites()
    {
        var sim = MatchSetup.CreatePlanetLanding(TestData.Real, 1701);
        Assert.Null(sim.System);
        Assert.EndsWith("no star system", Refused(sim, new SiteCourseCommand(0, 1, "s1")));
    }

    [Fact]
    public void LongRangeScansDetectSitesInRange()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        Do(sim, new LongRangeScanCommand(0, ship.Id));
        var at = sys.ShipPosition(ship, sim.Tick);
        foreach (var s in sys.Sites.Where(s => s.Kind != SiteKind.Station))
            Assert.Equal(FixVec2.Distance(s.Position, at) <= sys.ScanRangeFor(0) ? SiteKnowledge.Detected : SiteKnowledge.Unknown, sys.SiteKnowledgeOf(0, s.Id));
        var known = sys.Sites.FirstOrDefault(s => s.Kind != SiteKind.Station && sys.SiteKnowledgeOf(0, s.Id) == SiteKnowledge.Detected);
        if (known != null)
        {
            var report = sys.SiteReportFor(0, known.Id)!;
            Assert.Equal("Unknown contact", report.Title);
            Assert.Contains(report.Actions, x => x.Id == "course" && x.Problem == null);
        }
        if (sys.Sites.FirstOrDefault(s => sys.SiteKnowledgeOf(0, s.Id) == SiteKnowledge.Unknown) is { } unknown) Assert.Null(sys.SiteReportFor(0, unknown.Id));
        Assert.Null(sys.SiteReportFor(0, "nope"));
    }

    [Fact]
    public void AShipAtASiteHoldsPositionNotInOrbitAndNotInFlight()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        var station = First(sim, SiteKind.Station)!;
        var expected = sys.SiteTravelTicksFor(ship, station.Id);
        Do(sim, new SiteCourseCommand(0, ship.Id, station.Id));
        Assert.True(ship.InFlight);
        Assert.Equal(station.Id, sys.SiteBound(ship));
        Assert.Equal(expected, ship.ArriveTick - ship.DepartTick);
        Assert.True(sys.EtaSeconds(ship, sim.Tick) > 0);
        Until(sim, () => ship.AtSite == station.Id, 300, "arrives");
        Assert.Null(ship.AtBody);
        Assert.False(ship.InFlight);
        Assert.Equal(station.Id, sys.ShipSite(ship));
        Assert.Equal(station.Position, sys.ShipPosition(ship, sim.Tick));
        Assert.NotNull(sys.OrbitProblem(0)); // not in orbit of the colony's body
        Assert.StartsWith("already at", sys.SiteCourseProblem(0, ship.Id, station.Id));
        Assert.Null(sys.ScanProblem(0, ship.Id)); // a long-range scan runs from a site too (not in flight)
        // And back to a body from there.
        var home = sys.Bodies.First().Id;
        Do(sim, new SetCourseCommand(0, ship.Id, home));
        Assert.True(ship.InFlight);
        Assert.Equal(station.Id, ship.FromSite);
        Until(sim, () => ship.AtBody == home, 300, "back at a body");
        Assert.Null(ship.AtSite);
        Assert.Null(ship.FromSite);
    }

    [Fact]
    public void ScanningIdentifiesAndLeavingRangeCallsItOff()
    {
        var seed = SeedWithEveryKind(Campaign);
        var sim = Game(seed: seed);
        var sys = sim.System!;
        var hulk = First(sim, SiteKind.Derelict)!;
        GoTo(sim, hulk.Id);
        Assert.Equal(SiteKnowledge.Detected, sys.SiteKnowledgeOf(0, hulk.Id));
        Assert.Equal("scan it first", sys.SiteWorkProblem(0, Ship(sim).Id, hulk.Id, "alongside"));
        Do(sim, new SiteScanCommand(0, Ship(sim).Id, hulk.Id));
        Assert.NotNull(sys.SiteScanShare(0, hulk.Id));
        Assert.StartsWith("scanning it already", sys.SiteScanProblem(0, Ship(sim).Id, hulk.Id));
        // Fly away before it is done: called off.
        var station = First(sim, SiteKind.Station)!;
        Do(sim, new SiteCourseCommand(0, Ship(sim).Id, station.Id));
        Until(sim, () => sys.SiteScanShare(0, hulk.Id) == null, 200, "called off");
        Assert.Equal(SiteKnowledge.Detected, sys.SiteKnowledgeOf(0, hulk.Id));
        Identify(sim, hulk.Id);
        var card = sys.SiteReportFor(0, hulk.Id)!;
        Assert.Equal(hulk.Name, card.Title);
        Assert.NotEmpty(card.Hazards);
        Assert.Contains(card.Actions, x => x.Id == "alongside" && x.Problem == null);
        Assert.Contains(card.Actions, x => x.Id == "away_team" && x.Problem == "come alongside first");
    }

    [Fact]
    public void DebrisSweepFindsTheBlackBoxAndWearsTheHull()
    {
        var seed = SeedWithEveryKind(Campaign);
        var sim = Game(seed: seed);
        var sys = sim.System!;
        var field = First(sim, SiteKind.Debris)!;
        Identify(sim, field.Id);
        var before = sys.StatusOf(0)!.Hull;
        Work(sim, field.Id, "sweep", () => sys.SiteWork(0, field.Id)!.Swept);
        var status = sys.StatusOf(0)!;
        Assert.True(status.Hull < before);
        Assert.Contains(status.Hold, l => l.Item == "salvage:black_box");
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, field.Id));
        Assert.True(field.ExpireTick > sim.Tick);
        // It drifts off once spent_seconds have passed and no ship is there.
        Do(sim, new SiteCourseCommand(0, Ship(sim).Id, First(sim, SiteKind.Station)!.Id));
        Seconds(sim, Campaign.SpaceSites!.SpentSeconds + 5);
        Assert.Null(sys.Site(field.Id));
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, field.Id)); // the record stays
    }

    [Fact]
    public void KnownDerelictPowerLogsAndReactor()
    {
        var seed = SeedWithEveryKind(Campaign);
        var sim = Game(seed: seed);
        var sys = sim.System!;
        var wreck = First(sim, SiteKind.KnownDerelict)!;
        Identify(sim, wreck.Id);
        Assert.Equal("come alongside first", sys.SiteWorkProblem(0, Ship(sim).Id, wreck.Id, "restore_power"));
        Work(sim, wreck.Id, "alongside", () => sys.SiteWork(0, wreck.Id)!.Alongside);
        Assert.Contains(sys.SiteReportFor(0, wreck.Id)!.Rows, r => r.Label == "REACTOR" && r.Tone == "bad");
        Assert.Equal("restore power first", sys.SiteWorkProblem(0, Ship(sim).Id, wreck.Id, "recover_logs"));
        Do(sim, new SiteWorkCommand(0, Ship(sim).Id, wreck.Id, "restore_power_kit")); // the ship's one patch kit
        Assert.True(sys.SiteWork(0, wreck.Id)!.Powered);
        var latinum = sim.Players[0].Stock[Campaign.Resource("latinum").Index];
        Work(sim, wreck.Id, "recover_logs", () => sys.SiteWork(0, wreck.Id)!.Logs);
        Assert.True(sim.Players[0].Stock[Campaign.Resource("latinum").Index] > latinum);
        // Stay alongside until the reactor fails: the hull takes the blast and the wreck is gone.
        var hull = sys.StatusOf(0)!.Hull;
        Until(sim, () => sys.SiteKnowledgeOf(0, wreck.Id) == SiteKnowledge.Spent, Campaign.SpaceSites!.Kind(SiteKind.KnownDerelict)!.Reactor!.Seconds + 5, "the reactor fails");
        Assert.True(sys.StatusOf(0)!.Hull < hull);
    }

    [Fact]
    public void RefusedSpaceCommandsChangeNothing()
    {
        var a = Game();
        var b = Game();
        var bad = new Command[]
        {
            new SiteCourseCommand(0, 999, "s1"), new SiteCourseCommand(1, Ship(a).Id, "s1"), new SiteCourseCommand(0, Ship(a).Id, "nope"),
            new SiteScanCommand(0, Ship(a).Id, "s2"), new SiteWorkCommand(0, Ship(a).Id, "s1", "alongside"), new SiteWorkCommand(0, Ship(a).Id, "s1", "fly"),
            new AwayTeamCommand(0, Ship(a).Id, "s2", new[] { 1, 1, 0, 0 }), new AwayTeamChoiceCommand(0, "s2", 0),
            new ShipRepairCommand(0, Ship(a).Id, "patch"), new ShipRepairCommand(0, Ship(a).Id, "fix:sensors"), new ShipRepairCommand(0, Ship(a).Id, "dock"),
            new ShipHoldCommand(0, Ship(a).Id, "sell", "salvage:cargo_pod", 1), new ShipHoldCommand(0, Ship(a).Id, "hire", "science", 1),
            new ShipHoldCommand(0, Ship(a).Id, "jettison", "survivors", 1), new ShipHoldCommand(0, Ship(a).Id, "drop", "patch_kit", 1),
            new DebugSpaceCommand(0, 0, DebugSpaceField.Hull, null, 200), new DebugSpaceCommand(0, 5, DebugSpaceField.Hull, null, 50),
        };
        foreach (var c in bad) Refused(a, c);
        Seconds(b, 0);
        for (var i = 0; i < bad.Length; i++) b.Step();
        Assert.Equal(b.StateHash(), a.StateHash());
    }

    [Fact]
    public void SiteGamesAreDeterministic()
    {
        ulong Run()
        {
            var seed = SeedWithEveryKind(Campaign);
            var sim = Game(seed: seed);
            var field = First(sim, SiteKind.Debris)!;
            Identify(sim, field.Id);
            Work(sim, field.Id, "sweep", () => sim.System!.SiteWork(0, field.Id)!.Swept);
            Seconds(sim, 10);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
