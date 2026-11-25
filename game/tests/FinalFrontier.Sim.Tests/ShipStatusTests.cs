using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>The flagship's hull, systems, crew and hold (docs/space-jobs-contract.md).</summary>
public class ShipStatusTests
{
    [Fact]
    public void StatusStartsFromTheFitOutOrTheSuggestedPreset()
    {
        var fit = FitOutRules.Preset(Campaign, "tereshkova", "explorer")!;
        var fitted = Game("tereshkova", fitOut: fit).System!.StatusOf(0)!;
        Assert.Equal(fit.Crew, fitted.Crew.Select(c => c.OnDuty));
        Assert.Equal(Campaign.FitOut!.Ship("tereshkova")!.Hold, fitted.HoldSize);
        Assert.Equal(FitOutRules.ContainersUsed(Campaign, fit), fitted.HoldUsed); // the preset fills the hold with its space cargo
        Assert.Equal(Fix64.FromInt(100), fitted.Hull);
        Assert.Equal(HullBand.Scuffed, fitted.Band);
        Assert.All(fitted.Crew, c => Assert.True(c.Floor >= 1 && c.OnDuty >= c.Floor));

        var plain = Game("tereshkova").System!.StatusOf(0)!;
        Assert.Equal(FitOutRules.Preset(Campaign, "tereshkova", "explorer")!.Crew, plain.Crew.Select(c => c.OnDuty)); // explorer is its suggestion
        Assert.Contains(plain.Hold, l => l.Item == StarSystemState.PatchKitItem && l.Count == Campaign.ShipStatus!.DefaultPatchKits);

        var sandbox = Game(null).System!.StatusOf(0)!; // no ship pick: default_ship
        Assert.Equal(Campaign.ShipStatus!.DefaultCrew, sandbox.Crew.Select(c => c.OnDuty));
        Assert.Equal(Campaign.ShipStatus.DefaultHold, sandbox.HoldSize);
    }

    [Fact]
    public void ADamagedHullSlowsFlightsAndShortensScanReach()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        var to = sys.Bodies.First(b => b.Id != ship.AtBody).Id;
        var sound = sys.TravelTicksFor(ship, ship.AtBody!, to);
        var reach = sys.ScanRangeFor(0);
        Debug(sim, DebugSpaceField.Hull, null, 40);
        Assert.Equal(HullBand.Damaged, sys.StatusOf(0)!.Band);
        Assert.Equal((Fix64.FromInt((int)sound) * Fix64.Parse("1.25")).FloorToInt(), sys.TravelTicksFor(ship, ship.AtBody!, to));
        Assert.Equal(reach * Fix64.Parse("0.8"), sys.ScanRangeFor(0));
        Debug(sim, DebugSpaceField.SystemDown, "engines", 60);
        Assert.True(sys.TravelTicksFor(ship, ship.AtBody!, to) > (Fix64.FromInt((int)sound) * Fix64.Parse("1.25")).FloorToInt());
        Debug(sim, DebugSpaceField.Hull, null, 100);
        Debug(sim, DebugSpaceField.SystemDown, "engines", 0);
        Assert.Equal(sound, sys.TravelTicksFor(ship, ship.AtBody!, to));
    }

    [Fact]
    public void ACrippledShipDriftsAndRecoversTo20()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        var to = sys.Bodies.First(b => b.Id != ship.AtBody).Id;
        Debug(sim, DebugSpaceField.Hull, null, 0);
        Assert.True(sys.StatusOf(0)!.Crippled);
        Assert.Equal(HullBand.Crippled, sys.StatusOf(0)!.Band);
        Assert.Contains("crippled", Refused(sim, new SetCourseCommand(0, ship.Id, to)));
        Assert.Contains("crippled", Refused(sim, new SiteCourseCommand(0, ship.Id, First(sim, SiteKind.Station)!.Id)));
        Seconds(sim, Campaign.ShipStatus!.RecoverSeconds + 2);
        var status = sys.StatusOf(0)!;
        Assert.False(status.Crippled);
        Assert.Equal(Fix64.FromInt(Campaign.ShipStatus.RecoverTo), status.Hull);
        Do(sim, new SetCourseCommand(0, ship.Id, to));
        Assert.True(ship.InFlight);
        Assert.Contains(status.Log, l => l.Text.Contains("crippled"));
    }

    [Fact]
    public void FieldRepairsStopAt60AndPatchKitsAndTheDockDoTheRest()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        Debug(sim, DebugSpaceField.Hull, null, 55);
        Seconds(sim, 120);
        Assert.Equal(Fix64.FromInt(60), sys.StatusOf(0)!.Hull);
        Do(sim, new ShipRepairCommand(0, ship.Id, "patch"));
        Assert.Equal("patch", sys.StatusOf(0)!.Repair);
        Assert.EndsWith("a patch kit is being fitted", Refused(sim, new ShipRepairCommand(0, ship.Id, "patch")));
        Seconds(sim, Campaign.ShipStatus!.PatchSeconds + 1);
        Assert.Equal(Fix64.FromInt(75), sys.StatusOf(0)!.Hull);
        Assert.DoesNotContain(sys.StatusOf(0)!.Hold, l => l.Item == StarSystemState.PatchKitItem);
        Debug(sim, DebugSpaceField.HoldItem, StarSystemState.PatchKitItem, 1);
        Debug(sim, DebugSpaceField.SystemDown, "sensors", 600);
        Do(sim, new ShipRepairCommand(0, ship.Id, "fix:sensors"));
        Assert.Empty(sys.StatusOf(0)!.Down);
        // The station's dockyard: everything back.
        Debug(sim, DebugSpaceField.SystemDown, "lifesupport", 600);
        Assert.EndsWith("dock at a station", Refused(sim, new ShipRepairCommand(0, ship.Id, "dock")));
        GoTo(sim, First(sim, SiteKind.Station)!.Id);
        Do(sim, new ShipRepairCommand(0, ship.Id, "dock"));
        Seconds(sim, Campaign.SpaceSites!.Station.DockSeconds + 1);
        Assert.Equal(Fix64.FromInt(100), sys.StatusOf(0)!.Hull);
        Assert.Empty(sys.StatusOf(0)!.Down);
    }

    [Fact]
    public void InjuredCrewComeBackAndLifeSupportDownHoldsThem()
    {
        var sim = Game();
        var sys = sim.System!;
        var start = sys.StatusOf(0)!.Crew.First(c => c.Division == "science").OnDuty;
        Debug(sim, DebugSpaceField.Injure, "science", 2);
        Assert.Equal((start - 2, 2), (sys.StatusOf(0)!.Crew.First(c => c.Division == "science").OnDuty, sys.StatusOf(0)!.Crew.First(c => c.Division == "science").Injured));
        Debug(sim, DebugSpaceField.SystemDown, "life_support", 60);
        Seconds(sim, Campaign.ShipStatus!.InjurySeconds + 5);
        Assert.Equal(2, sys.StatusOf(0)!.Crew.First(c => c.Division == "science").Injured); // held while life support was down
        Seconds(sim, 60);
        Assert.Equal((start, 0), (sys.StatusOf(0)!.Crew.First(c => c.Division == "science").OnDuty, sys.StatusOf(0)!.Crew.First(c => c.Division == "science").Injured));
    }

    [Fact]
    public void LostCrewMoveTheFitOutLeversAndHalfACrewSlowsFlights()
    {
        var fit = FitOutRules.Preset(Campaign, "tereshkova", "explorer")!;
        var sim = Game("tereshkova", fitOut: fit);
        var sys = sim.System!;
        var before = sim.FitOutFactor(0, FitOutRules.SweepTime);
        Debug(sim, DebugSpaceField.Crew, "exploration", -2);
        Assert.True(sim.FitOutFactor(0, FitOutRules.SweepTime) > before, "fewer explorers: sweeps slower");
        Assert.Equal(2, sys.StatusOf(0)!.Crew.First(c => c.Division == "exploration").Lost);
        Debug(sim, DebugSpaceField.Crew, "exploration", 2);
        Assert.Equal(before, sim.FitOutFactor(0, FitOutRules.SweepTime));

        // No fit-out: exactly 1 until the crew changes.
        var plain = Game("tereshkova");
        Assert.Equal(Fix64.One, plain.FitOutFactor(0, FitOutRules.SweepTime));
        Debug(plain, DebugSpaceField.Crew, "exploration", -3);
        Assert.True(plain.FitOutFactor(0, FitOutRules.SweepTime) > Fix64.One);

        // Below half the starting crew: flights slower.
        var ship = Ship(plain);
        var to = plain.System!.Bodies.First(b => b.Id != ship.AtBody).Id;
        var t = plain.System.TravelTicksFor(ship, ship.AtBody!, to);
        foreach (var c in plain.System.StatusOf(0)!.Crew.Where(c => c.OnDuty > 1)) Debug(plain, DebugSpaceField.Crew, c.Division, -(c.OnDuty - 1));
        Assert.True(plain.System.TravelTicksFor(ship, ship.AtBody!, to) > t);
    }

    [Fact]
    public void TheStationHiresBuysAndTakesSurvivors()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        GoTo(sim, First(sim, SiteKind.Station)!.Id);
        var latinum = Campaign.Resource("latinum").Index;
        Assert.EndsWith("the crew is complete", Refused(sim, new ShipHoldCommand(0, ship.Id, "hire", "science", 1)));
        Debug(sim, DebugSpaceField.Crew, "science", -3);
        sim.Players[0].Stock.Add(latinum, Fix64.FromInt(100) - sim.Players[0].Stock[latinum]);
        Do(sim, new ShipHoldCommand(0, ship.Id, "hire", "science", 2));
        Assert.Equal(Fix64.FromInt(100 - 2 * Campaign.SpaceSites!.Station.HireLatinum), sim.Players[0].Stock[latinum]);
        Assert.EndsWith("no one else to hire this visit", Refused(sim, new ShipHoldCommand(0, ship.Id, "hire", "science", 1)));
        Debug(sim, DebugSpaceField.HoldItem, "salvage:cargo_pod", 2);
        Do(sim, new ShipHoldCommand(0, ship.Id, "sell", "salvage:cargo_pod", 2));
        Assert.Equal(Fix64.FromInt(100 - 2 * Campaign.SpaceSites.Station.HireLatinum + 2 * Campaign.SpaceSites.SalvageItem("cargo_pod")!.Latinum), sim.Players[0].Stock[latinum]);
        Debug(sim, DebugSpaceField.HoldItem, "survivors:science", 2);
        Do(sim, new ShipHoldCommand(0, ship.Id, "drop", "survivors", 2));
        var status = sys.StatusOf(0)!;
        Assert.Equal(status.CrewStart, status.CrewTotal); // one rejoined to fill the last gap
        Assert.DoesNotContain(status.Hold, l => l.Item.StartsWith("survivors"));
        Assert.EndsWith("people don't go out of the airlock", Refused(sim, new ShipHoldCommand(0, ship.Id, "jettison", "survivors", 1)));
    }

    [Fact]
    public void AFullHoldCanBeEmptiedOfUsedUpFitOutCargo()
    {
        var fit = FitOutRules.Preset(Campaign, "tereshkova", "explorer")!;
        var sim = Game("tereshkova", fitOut: fit);
        var sys = sim.System!;
        var ship = Ship(sim);
        var probes = sim.FitOutProbes(0);
        Assert.True(probes > 0);
        var used = sys.StatusOf(0)!.HoldUsed;
        Do(sim, new ShipHoldCommand(0, ship.Id, "jettison", "probe", 1));
        Assert.Equal(probes - 1, sim.FitOutProbes(0));
        Assert.Equal(used - Campaign.FitOut!.Line("probe")!.Containers, sys.StatusOf(0)!.HoldUsed);
        Refused(sim, new ShipHoldCommand(0, ship.Id, "jettison", "mass", 1)); // reaction mass is not used up
    }
}
