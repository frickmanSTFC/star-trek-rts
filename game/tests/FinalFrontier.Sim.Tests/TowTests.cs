using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>Space jobs phase 2 (docs/space-jobs-contract.md "Phase 2"): taking a wreck in tow from alongside, the wreck
/// following the ship, flights 1.6× slower, dropping it at the station for latinum or releasing it where it is.</summary>
public class TowTests
{
    private static (Simulation Sim, SpaceSite Wreck) Towing(SiteKind kind = SiteKind.Derelict)
    {
        var sim = Game("tereshkova", SeedWithEveryKind(Campaign));
        var wreck = First(sim, kind)!;
        Identify(sim, wreck.Id);
        Assert.EndsWith("come alongside first", sim.System!.SiteWorkProblem(0, Ship(sim).Id, wreck.Id, "tow"));
        Work(sim, wreck.Id, "alongside", () => sim.System.SiteWork(0, wreck.Id)!.Alongside);
        return (sim, wreck);
    }

    [Fact]
    public void ATowedWreckFollowsTheShipFlightsAreSlowerAndTheStationPaysForIt()
    {
        var (sim, wreck) = Towing();
        var sys = sim.System!;
        var ship = Ship(sim);
        var station = First(sim, SiteKind.Station)!;
        var untowed = sys.SiteTravelTicksFor(ship, station.Id);
        Assert.Contains(sys.SiteReportFor(0, wreck.Id)!.Actions, a => a.Id == "tow" && a.Problem == null);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "tow"));
        Assert.Equal(wreck.Id, sys.TowOf(0));
        Assert.Equal(0, sys.TowerOf(wreck.Id));
        Assert.True(sys.SiteWork(0, wreck.Id)!.Towing);
        Assert.Contains(sys.StatusOf(0)!.Hold, l => l.Item == StarSystemState.TowItem && l.Containers == 0);
        var towed = sys.SiteTravelTicksFor(ship, station.Id);
        Assert.InRange(towed, untowed * 16 / 10 - 2, untowed * 16 / 10 + 2);
        Do(sim, new SiteCourseCommand(0, ship.Id, station.Id));
        Assert.Equal(towed, ship.ArriveTick - ship.DepartTick);
        for (var i = 0; i < 20; i++)
        {
            Seconds(sim, 1);
            Assert.Equal(sys.ShipPosition(ship, sim.Tick - 1), wreck.Position); // set during the last step
        }
        Until(sim, () => ship.AtSite == station.Id, 300, "reaches the station with the tow");
        Assert.EndsWith("in tow behind us", Refused(sim, new SiteCourseCommand(0, ship.Id, wreck.Id)));
        Assert.Equal(station.Position, wreck.Position);
        var latinum = sim.Players[0].Stock[sim.Data.Resource("latinum").Index];
        Assert.Null(sys.HoldProblem(0, "drop", StarSystemState.TowItem, 1));
        Do(sim, new ShipHoldCommand(0, ship.Id, "drop", StarSystemState.TowItem, 1));
        var paid = (sim.Players[0].Stock[sim.Data.Resource("latinum").Index] - latinum).FloorToInt();
        Assert.InRange(paid, Campaign.SpaceSites!.Kind(SiteKind.Derelict)!.Tow!.LatinumMin, Campaign.SpaceSites.Kind(SiteKind.Derelict)!.Tow!.LatinumMax);
        Assert.Null(sys.TowOf(0));
        Assert.True(sys.SiteWork(0, wreck.Id)!.Towed);
        Assert.Equal(1, sys.Tally(0, "tow:station"));
        Seconds(sim, 1);
        Assert.Null(sys.Site(wreck.Id)); // handed over: gone
        Assert.True(sys.SiteWork(0, wreck.Id)!.Towed); // the record stays (missions read it)
    }

    [Fact]
    public void ReleasingLeavesTheWreckWhereItIsAndTheTractorGoingDownLetsGo()
    {
        var (sim, wreck) = Towing(SiteKind.KnownDerelict);
        var sys = sim.System!;
        var ship = Ship(sim);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "tow"));
        var body = sys.Bodies.OrderBy(b => FixVec2.Distance(b.Position, wreck.Position)).First();
        Do(sim, new SetCourseCommand(0, ship.Id, body.Id));
        Seconds(sim, 5);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "release"));
        Assert.Null(sys.TowOf(0));
        var at = wreck.Position;
        Seconds(sim, 5);
        Assert.Equal(at, wreck.Position);
        Assert.False(sys.SiteWork(0, wreck.Id)!.Alongside);
        Assert.EndsWith("not towing it", Refused(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "release")));

        // Back to it, alongside, in tow again; the tractor knocked out lets go.
        Until(sim, () => !ship.InFlight, 300, "arrives");
        GoTo(sim, wreck.Id);
        if (sys.SiteWork(0, wreck.Id)!.Alongside == false && sys.SiteWorkProblem(0, ship.Id, wreck.Id, "alongside") == null)
            Work(sim, wreck.Id, "alongside", () => sys.SiteWork(0, wreck.Id)!.Alongside);
        if (sys.SiteWorkProblem(0, ship.Id, wreck.Id, "tow") != null) return; // its reactor failed meanwhile
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "tow"));
        Assert.Equal(wreck.Id, sys.TowOf(0));
        Debug(sim, DebugSpaceField.SystemDown, "tractor", 60);
        Assert.Null(sys.TowOf(0));
    }

    [Fact]
    public void AWreckWorkedOutCanStillBeTowedButNotOnceItsReactorBlew()
    {
        var (sim, wreck) = Towing();
        var sys = sim.System!;
        Board(sim, wreck.Id, 0);
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, wreck.Id));
        Assert.Contains(sys.SiteReportFor(0, wreck.Id)!.Actions, a => a.Id == "tow");
        if (sys.SiteWorkProblem(0, Ship(sim).Id, wreck.Id, "tow") is { } p) Assert.Contains("alongside", p);
        else Do(sim, new SiteWorkCommand(0, Ship(sim).Id, wreck.Id, "tow"));
        Assert.Equal(wreck.Id, sys.TowOf(0));
        Seconds(sim, Campaign.SpaceSites!.SpentSeconds + 10);
        Assert.NotNull(sys.Site(wreck.Id)); // in tow: it doesn't drift off
    }
}
