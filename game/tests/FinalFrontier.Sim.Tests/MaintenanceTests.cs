using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Building wear, breakdowns, crew and drone maintenance, building levels (Mk I–III) and surface research.</summary>
public class MaintenanceTests
{
    private static int Sec(int seconds) => seconds * Simulation.TickRate;
    private static readonly Fix64 Hundred = Fix64.FromInt(100);

    /// <summary>Refineries, mines and foundries wear 120 a minute (worn out in under a minute) and, unless allowed, never break.</summary>
    private static GameData FastWear(bool breaks = false, int rate = 120) => TestData.Patched(
        ("buildings.json", "\"wear_per_min\": 3,", $"\"wear_per_min\": {rate},"),
        ("surfaces.json", "\"break_chance_per_min_at_zero\": 0.6", breaks ? "\"break_chance_per_min_at_zero\": 900" : "\"break_chance_per_min_at_zero\": 0"));

    private static Simulation Landed(GameData data, out int hub)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = site;
        var stock = sim.Players[0].Stock;
        stock.Add(data.Resource("duranium").Index, Fix64.FromInt(1500));
        stock.Add(data.Resource("tritanium").Index, Fix64.FromInt(500));
        stock.Add(data.Resource("parasteel").Index, Fix64.FromInt(100));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    /// <summary>Builds and finishes a building at the n-th free tile two hexes from the hub.</summary>
    private static SurfaceStructure Build(Simulation sim, int hub, string building, int skip = 0)
    {
        var map = sim.Surface!.Map;
        var tile = map.Grid.Within(hub, 2).Where(t => map.Grid.Distance(t, hub) >= 1 &&
            sim.Surface.CheckPlacement(sim, 0, building, t) == PlacementProblem.None).Skip(skip).First();
        Do(sim, new PlaceBuildingCommand(0, building, tile, 0));
        var s = sim.Surface.StructureAt(tile)!;
        while (s.State != StructureState.Complete) sim.Step();
        sim.Step();
        return s;
    }

    /// <summary>A refinery kept busy with plenty of ore.</summary>
    private static SurfaceStructure BusyRefinery(Simulation sim, int hub, GameData data)
    {
        var r = Build(sim, hub, "refinery");
        sim.Players[0].Stock.Add(data.Resource("duranium_ore").Index, Fix64.FromInt(800));
        sim.Step();
        return r;
    }

    // ---------------------------------------------------------------- wear and breakdowns

    [Fact]
    public void BuildingsWearOnlyWhileTheyWork()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub);
        var refinery = Build(sim, hub, "refinery");
        Assert.Equal(StructureStatus.NoInput, refinery.Status);
        for (var i = 0; i < Sec(30); i++) sim.Step();
        Assert.Equal(Hundred, refinery.Condition); // idle: no wear

        sim.Players[0].Stock.Add(data.Resource("duranium_ore").Index, Fix64.FromInt(800));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        var lost = (Hundred - refinery.Condition).ToDouble();
        Assert.InRange(lost, data.Building("refinery").WearPerMinute.ToDouble() - 0.1, data.Building("refinery").WearPerMinute.ToDouble() + 0.1);

        // Colony cores and storage don't wear.
        Assert.Equal(Hundred, sim.Surface!.StructureAt(hub)!.Condition);
        Assert.False(data.Building("storage_container").Wears);
    }

    [Fact]
    public void OutputFallsAsConditionDropsBelowHalf()
    {
        var data = FastWear();
        var sim = Landed(data, out var hub);
        var refinery = BusyRefinery(sim, hub, data);
        var rules = data.Maintenance!;
        Assert.Equal(Fix64.One, sim.Surface!.ConditionFactor(refinery));
        StepUntil(sim, () => refinery.Condition <= Fix64.FromInt(25), Sec(60));
        var factor = sim.Surface.ConditionFactor(refinery).ToDouble();
        var expected = rules.SlowestOutput.ToDouble() + (1 - rules.SlowestOutput.ToDouble()) * refinery.Condition.ToDouble() / rules.DeclineBelow.ToDouble();
        Assert.InRange(factor, expected - 0.001, expected + 0.001);
        Assert.True(factor < 0.9);
        StepUntil(sim, () => refinery.Condition == Fix64.Zero, Sec(60));
        Assert.Equal(rules.SlowestOutput, sim.Surface.ConditionFactor(refinery));
        Assert.False(refinery.Broken); // breakdowns switched off in this test
    }

    [Fact]
    public void WornOutBuildingsBreakDownAndStop()
    {
        var data = FastWear(breaks: true);
        var sim = Landed(data, out var hub);
        var refinery = BusyRefinery(sim, hub, data);
        StepUntil(sim, () => refinery.Broken, Sec(60));
        sim.Step();
        Assert.Equal(StructureStatus.Broken, refinery.Status);
        Assert.Equal(Fix64.Zero, sim.Surface!.PowerOf(refinery.BaseId).Use);
        var ore = sim.Players[0].Stock[data.Resource("duranium_ore").Index];
        var condition = refinery.Condition;
        for (var i = 0; i < Sec(5); i++) sim.Step();
        Assert.Equal(ore, sim.Players[0].Stock[data.Resource("duranium_ore").Index]); // stopped refining
        Assert.Equal(condition, refinery.Condition); // and stopped wearing
    }

    // ---------------------------------------------------------------- crew maintenance

    [Fact]
    public void TheCrewWalksOutFixesItForAPriceAndWalksBack()
    {
        var data = FastWear(breaks: true);
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var refinery = BusyRefinery(sim, hub, data);
        StepUntil(sim, () => refinery.Broken, Sec(60));
        var core = s.StructureAt(hub)!;
        var rules = data.Maintenance!;

        Do(sim, new MaintainCommand(0, refinery.Id));
        Assert.True(refinery.MaintenanceOrdered);
        sim.Step();
        Assert.Equal(CrewPhase.Out, core.CrewPhase);
        Assert.Equal(refinery.Id, core.CrewJob);
        var walk = (rules.CrewWalkSeconds + rules.CrewSecondsPerTile * Fix64.FromInt(s.Map.Grid.Distance(hub, refinery.Tile))) * Simulation.TickRate;
        Assert.Equal(walk.FloorToInt(), core.CrewWalkTicks);

        var condition = refinery.Condition;
        StepUntil(sim, () => core.CrewPhase == CrewPhase.Working, core.CrewWalkTicks + 2);
        Assert.Equal(condition, refinery.Condition); // nothing fixed while walking

        var duranium = data.Resource("duranium").Index;
        var before = sim.Players[0].Stock[duranium];
        StepUntil(sim, () => !refinery.Broken, Sec(30));
        Assert.Equal(Hundred, refinery.Condition);
        Assert.False(refinery.MaintenanceOrdered);
        var paid = (before - sim.Players[0].Stock[duranium]).ToDouble();
        var expected = (Hundred - condition).ToDouble() * rules.CostPerPoint.Single(c => c.Resource == duranium).Amount.ToDouble();
        Assert.InRange(paid, expected - 1, expected + 1); // the hub trickle adds a little meanwhile

        Assert.Equal(CrewPhase.Back, core.CrewPhase);
        StepUntil(sim, () => core.CrewPhase == CrewPhase.None, core.CrewWalkTicks + 2);
        Assert.Equal(0, core.CrewJob);
    }

    [Fact]
    public void OneCrewPerBaseWorksTheOldestOrderFirst()
    {
        var data = FastWear();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var a = BusyRefinery(sim, hub, data);
        var b = Build(sim, hub, "refinery");
        for (var i = 0; i < Sec(10); i++) sim.Step();
        Do(sim, new MaintainCommand(0, b.Id));
        Do(sim, new MaintainCommand(0, a.Id));
        var core = s.StructureAt(hub)!;
        Assert.Equal(b.Id, core.CrewJob);
        StepUntil(sim, () => core.CrewJob == a.Id, Sec(90));
        Assert.False(b.MaintenanceOrdered);
        Assert.True(a.MaintenanceOrdered);
    }

    [Fact]
    public void AnOrderCanBeCancelledUntilTheCrewIsOnIt()
    {
        var data = FastWear();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var a = BusyRefinery(sim, hub, data);
        var b = Build(sim, hub, "refinery");
        for (var i = 0; i < Sec(10); i++) sim.Step();
        Do(sim, new MaintainCommand(0, a.Id)); // the crew takes this one
        Do(sim, new MaintainCommand(0, b.Id)); // this one waits
        Do(sim, new MaintainCommand(0, b.Id)); // cancelled
        Assert.False(b.MaintenanceOrdered);
        Do(sim, new MaintainCommand(0, a.Id)); // too late: the crew is on it
        Assert.True(a.MaintenanceOrdered);
        Assert.NotNull(s.MaintenanceProblem(0, s.StructureAt(hub)!.Id)); // cores don't need it
    }

    [Fact]
    public void MaintenanceWaitsWhenThePlayerCanNotPay()
    {
        var data = FastWear();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var refinery = BusyRefinery(sim, hub, data);
        StepUntil(sim, () => refinery.Condition < Fix64.FromInt(40), Sec(60));
        Do(sim, new MaintainCommand(0, refinery.Id));
        var core = s.StructureAt(hub)!;
        StepUntil(sim, () => core.CrewPhase == CrewPhase.Working, Sec(30));
        var duranium = data.Resource("duranium").Index;
        var stock = sim.Players[0].Stock;
        StepUntil(sim, () =>
        {
            stock.TrySpend(new[] { new ResourceAmount(duranium, stock[duranium]) });
            return refinery.MaintenanceWaiting;
        }, Sec(5));
        Assert.Equal(CrewPhase.Working, core.CrewPhase);
    }

    // ---------------------------------------------------------------- levels

    [Fact]
    public void UpgradingARefineryMakesItFasterAndRefitsIt()
    {
        var data = FastWear();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var refinery = BusyRefinery(sim, hub, data);
        StepUntil(sim, () => refinery.Condition < Fix64.FromInt(60), Sec(60));
        Assert.Equal("Mk I", s.LevelName(refinery));
        Assert.Equal(Fix64.One, s.SpeedMultiplier(refinery));

        var mk2 = s.NextLevel(refinery)!;
        Assert.Equal("Mk II", mk2.Name);
        Assert.Null(s.UpgradeProblem(sim, 0, refinery.Id));
        Do(sim, new UpgradeCommand(0, refinery.Id));
        Assert.Equal(2, refinery.UpgradingLevel);
        StepUntil(sim, () => refinery.UpgradingLevel == 0, Sec(mk2.BuildSeconds) + 2);
        Assert.Equal(2, refinery.Level);
        Assert.Equal("Mk II", s.LevelName(refinery));
        Assert.InRange(refinery.Condition.ToDouble(), 99, 100); // refitted (less a tick of wear)
        Assert.Equal(Fix64.One + mk2.Modifiers.Single(m => m.Stat == Modifier.Speed).Add, s.SpeedMultiplier(refinery));
        Assert.True(s.WearMultiplier(refinery) < Fix64.One);
    }

    [Fact]
    public void MarkThreeNeedsColonialAdministrationAndMaintainsItself()
    {
        var data = FastWear(rate: 30); // slow enough for drones to win
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var refinery = BusyRefinery(sim, hub, data);
        Do(sim, new UpgradeCommand(0, refinery.Id));
        StepUntil(sim, () => refinery.Level == 2, Sec(60));
        Assert.Contains("level 2", s.UpgradeProblem(sim, 0, refinery.Id));

        var core = s.StructureAt(hub)!;
        Do(sim, new UpgradeCommand(0, core.Id));
        StepUntil(sim, () => core.UpgradingTo == null, Sec(120));
        Do(sim, new UpgradeCommand(0, refinery.Id));
        StepUntil(sim, () => refinery.Level == 3, Sec(60));
        Assert.True(s.AutoMaintained(refinery));

        // Drones take over below auto_below; no crew needed.
        StepUntil(sim, () => refinery.AutoRepairing, Sec(200));
        Assert.True(refinery.Condition < data.Maintenance!.AutoBelow + Fix64.One);
        Assert.Equal(CrewPhase.None, core.CrewPhase);
        StepUntil(sim, () => !refinery.AutoRepairing, Sec(60));
        Assert.InRange(refinery.Condition.ToDouble(), 99.9, 100);
    }

    [Fact]
    public void CancellingALevelUpgradeRefundsIt()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub);
        var refinery = Build(sim, hub, "refinery");
        var tritanium = data.Resource("tritanium").Index;
        var before = sim.Players[0].Stock[tritanium];
        Do(sim, new UpgradeCommand(0, refinery.Id));
        Do(sim, new CancelBuildingCommand(0, refinery.Id));
        Assert.Equal(0, refinery.UpgradingLevel);
        Assert.InRange((sim.Players[0].Stock[tritanium] - before).ToDouble(), 0, 0.2);
        Assert.Equal(1, refinery.Level);
    }

    // ---------------------------------------------------------------- research

    [Fact]
    public void LabsResearchTheChosenTechAndItsBonusApplies()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var lab = Build(sim, hub, "science_lab");
        var refinery = Build(sim, hub, "refinery");
        Assert.Equal(StructureStatus.NoInput, lab.Status); // nothing chosen yet

        var tech = data.Tech("improved_smelting");
        Do(sim, new ResearchCommand(0, tech.Id));
        sim.Step();
        Assert.Equal(StructureStatus.Working, lab.Status);
        Assert.Equal(data.Building("science_lab").ResearchPerMinute, s.ResearchPerMinute(0));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.InRange(s.Players[0].ResearchProgress[tech.Index].ToDouble(), 9.5, 10.5);

        StepUntil(sim, () => s.Players[0].Known[tech.Index], Simulation.TicksPerMinute * 10);
        Assert.Equal(-1, s.Players[0].ActiveTech);
        Assert.Equal(Fix64.One + Fix64.Ratio(2, 10), s.YieldMultiplier(refinery));
        Assert.Equal("already researched", s.ResearchProblem(0, tech.Id));
    }

    [Fact]
    public void TechsNeedTheirPrerequisitesAndKeepProgressWhenSwitching()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        Build(sim, hub, "science_lab");
        Assert.Contains("Preventive Maintenance", s.ResearchProblem(0, "automated_maintenance"));
        Do(sim, new ResearchCommand(0, "automated_maintenance"));
        Assert.Equal(-1, s.Players[0].ActiveTech);

        Do(sim, new ResearchCommand(0, "ore_conveyors"));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        var kept = s.Players[0].ResearchProgress[data.Tech("ore_conveyors").Index];
        Do(sim, new ResearchCommand(0, "reinforced_drills"));
        for (var i = 0; i < Sec(30); i++) sim.Step();
        Assert.Equal(kept, s.Players[0].ResearchProgress[data.Tech("ore_conveyors").Index]);
        Assert.True(s.Players[0].ResearchProgress[data.Tech("reinforced_drills").Index] > Fix64.Zero);
    }

    [Fact]
    public void AutomatedMaintenanceResearchPutsDronesOnEveryBuilding()
    {
        var data = TestData.Patched(
            ("surfaces.json", "\"id\": \"automated_maintenance\", \"name\": \"Automated Maintenance\", \"cost\": 150,",
                              "\"id\": \"automated_maintenance\", \"name\": \"Automated Maintenance\", \"cost\": 5,"),
            ("surfaces.json", "\"id\": \"preventive_maintenance\", \"name\": \"Preventive Maintenance\", \"cost\": 60,",
                              "\"id\": \"preventive_maintenance\", \"name\": \"Preventive Maintenance\", \"cost\": 5,"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var lab = Build(sim, hub, "science_lab");
        var reactor = Build(sim, hub, "fusion_reactor");
        Assert.False(s.AutoMaintained(reactor));
        Do(sim, new ResearchCommand(0, "preventive_maintenance"));
        StepUntil(sim, () => s.Players[0].Known[data.Tech("preventive_maintenance").Index], Sec(60));
        Assert.True(s.WearMultiplier(reactor) < Fix64.One);
        Do(sim, new ResearchCommand(0, "automated_maintenance"));
        StepUntil(sim, () => s.Players[0].Known[data.Tech("automated_maintenance").Index], Sec(60));
        Assert.True(s.AutoMaintained(reactor));
        Assert.True(s.AutoMaintained(lab));
    }

    [Fact]
    public void ReinforcedDrillsHalveCrawlerBreakdowns()
    {
        var data = TestData.Real;
        var sim = Landed(data, out _);
        Assert.Equal(Fix64.Zero, sim.Surface!.UnitResearchBonus(0, "mining_crawler", Modifier.Breakdown));
        var tech = data.Tech("reinforced_drills");
        Assert.Equal(-Fix64.Ratio(1, 2), tech.Modifiers.Single().Add);
    }

    [Fact]
    public void WornReactorsSupplyLessPower()
    {
        var data = TestData.Patched(
            ("buildings.json", "\"upkeep_per_min\": { \"deuterium\": 3 }, \"wear_per_min\": 2,", "\"upkeep_per_min\": { \"deuterium\": 3 }, \"wear_per_min\": 200,"),
            ("surfaces.json", "\"break_chance_per_min_at_zero\": 0.6", "\"break_chance_per_min_at_zero\": 0"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var reactor = Build(sim, hub, "fusion_reactor");
        var full = s.PowerOf(reactor.BaseId).Supply;
        StepUntil(sim, () => reactor.Condition == Fix64.Zero, Sec(60));
        Assert.True(s.PowerOf(reactor.BaseId).Supply < full);
    }

    [Fact]
    public void MaintenanceAndResearchAreDeterministic()
    {
        ulong Run()
        {
            var data = FastWear(breaks: true);
            var sim = Landed(data, out var hub);
            var refinery = BusyRefinery(sim, hub, data);
            Build(sim, hub, "science_lab");
            sim.Schedule(sim.Tick + 1, new ResearchCommand(0, "improved_smelting"));
            for (var i = 0; i < Sec(90); i++)
            {
                if (refinery.Broken && !refinery.MaintenanceOrdered) sim.Schedule(sim.Tick, new MaintainCommand(0, refinery.Id));
                sim.Step();
            }
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
