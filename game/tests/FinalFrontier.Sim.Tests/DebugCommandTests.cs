using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The test panel's commands (DebugCommand): each one, the limits they switch on, and that they stay deterministic.</summary>
public class DebugCommandTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static readonly Fix64 Hundred = Fix64.FromInt(100);
    private static Fix64 F(int v) => Fix64.FromInt(v);

    private static Simulation Landed(out int hub, GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = site;
        return sim;
    }

    /// <summary>Schedules the command for this tick and runs it.</summary>
    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>Runs a debug command that must be accepted: checks DebugProblem first, then that it was counted.</summary>
    private static void Ok(Simulation sim, DebugCommand c)
    {
        Assert.Null(sim.DebugProblem(c));
        var before = sim.DebugEdits;
        Do(sim, c);
        Assert.Equal(before + 1, sim.DebugEdits);
    }

    /// <summary>A debug command that must be refused: it has a problem, changes nothing and isn't counted.</summary>
    private static void Refused(Simulation sim, DebugCommand c)
    {
        Assert.NotNull(sim.DebugProblem(c));
        var before = sim.DebugEdits;
        Do(sim, c);
        Assert.Equal(before, sim.DebugEdits);
    }

    /// <summary>Equal give or take the hub's trickle of duranium over a few ticks.</summary>
    private static void About(Fix64 expected, Fix64 actual) => Assert.InRange(actual.ToDouble(), expected.ToDouble() - 0.5, expected.ToDouble() + 0.5);

    private static int FreeTile(Simulation sim, int hub, int skip = 0)
    {
        var map = sim.Surface!.Map;
        return map.Grid.Within(hub, 2).Where(t => t != hub && map.Terrain(t).Buildable && map.DepositResource[t] < 0 &&
            sim.Surface.StructureAt(t) == null && sim.Surface.Units.All(u => u.Tile != t)).Skip(skip).First();
    }

    private static SurfaceStructure Placed(Simulation sim, int hub, string building, int skip = 0)
    {
        var tile = FreeTile(sim, hub, skip);
        Ok(sim, new DebugPlaceBuildingCommand(0, 0, building, tile, 0));
        return sim.Surface!.StructureAt(tile)!;
    }

    private static SurfaceUnit Crawler(Simulation sim) => sim.Surface!.Units.First(u => u.DefId == "mining_crawler");

    // ---------------------------------------------------------------- vehicles

    [Fact]
    public void IntegrityAndBreakdownsAreSetOnAVehicle()
    {
        var sim = Landed(out _);
        var crawler = Crawler(sim);
        Ok(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Integrity, F(62)));
        Assert.Equal(F(62), crawler.Integrity);
        Refused(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Integrity, F(150)));
        Refused(sim, new DebugUnitCommand(0, 9999, DebugUnitField.Integrity, F(50)));
        Assert.Equal(F(62), crawler.Integrity);

        Ok(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Broken, Fix64.One));
        Assert.True(crawler.Broken);
        Assert.NotNull(sim.Surface!.MineProblem(0, crawler.Id, crawler.Tile)); // broken: can't mine
        Ok(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Broken, Fix64.Zero));
        Assert.False(crawler.Broken);
    }

    [Fact]
    public void VehiclesTeleportAreRemovedAndCloned()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        var map = s.Map;
        var far = Enumerable.Range(0, map.Grid.Count).First(t => map.Terrain(t).Passable && map.Grid.Distance(t, hub) > 20);
        Assert.False(s.Players[0].Surveyed[far]);
        Ok(sim, new DebugMoveUnitCommand(0, rover.Id, far));
        Assert.Equal(far, rover.Tile);
        Assert.Empty(rover.Path);
        Assert.True(s.Players[0].Surveyed[far]); // it surveys where it lands
        var water = Enumerable.Range(0, map.Grid.Count).First(t => !map.Terrain(t).Passable);
        Refused(sim, new DebugMoveUnitCommand(0, rover.Id, water));

        var count = s.Units.Count;
        Ok(sim, new DebugSpawnUnitCommand(0, 0, rover.DefId, rover.Tile));
        Assert.Equal(count + 1, s.Units.Count);
        var clone = s.Units[^1];
        Assert.Equal(rover.DefId, clone.DefId);
        Assert.NotEqual(rover.Name, clone.Name);
        Assert.NotEqual(rover.Tile, clone.Tile); // beside it, not on top
        Refused(sim, new DebugSpawnUnitCommand(0, 0, "warp_core", rover.Tile));
        Refused(sim, new DebugSpawnUnitCommand(0, 5, rover.DefId, rover.Tile));

        Ok(sim, new DebugRemoveUnitCommand(0, clone.Id));
        Assert.Null(s.FindUnit(clone.Id));
        Refused(sim, new DebugRemoveUnitCommand(0, clone.Id));
    }

    [Fact]
    public void AnotherPlayerCanBeAddedToOwnTestThings()
    {
        var sim = Landed(out var hub);
        Refused(sim, new DebugAddPlayerCommand(0, "nobody"));
        Ok(sim, new DebugAddPlayerCommand(0, "cardassian"));
        Assert.Equal(2, sim.Players.Count);
        Assert.Equal(2, sim.Surface!.Players.Count);
        var map = sim.Surface.Map;
        var site = Enumerable.Range(0, map.Grid.Count).First(t => map.Terrain(t).Buildable && map.Grid.Distance(t, hub) > 15 && sim.Surface.StructureAt(t) == null);
        Refused(sim, new DebugPlaceBuildingCommand(0, 1, "storage_container", site, 0)); // no base of theirs there
        Ok(sim, new DebugPlaceBuildingCommand(0, 1, "colony_hub", site, 0));
        var core = sim.Surface.StructureAt(site)!;
        Assert.Equal(1, core.Owner);
        Assert.Equal(StructureState.Complete, core.State);
        Assert.Single(sim.Surface.Bases(1));
        Ok(sim, new DebugSpawnUnitCommand(0, 1, "survey_rover", site));
        Assert.Contains(sim.Surface.Units, u => u.Owner == 1);
    }

    // ---------------------------------------------------------------- buildings

    [Fact]
    public void BuildingsArePlacedFinishedAndFree()
    {
        var sim = Landed(out var hub);
        var duranium = sim.Players[0].Stock[R("duranium")];
        var refinery = Placed(sim, hub, "refinery");
        Assert.Equal(StructureState.Complete, refinery.State);
        Assert.True(refinery.Free);
        About(duranium, sim.Players[0].Stock[R("duranium")]);
        Refused(sim, new DebugPlaceBuildingCommand(0, 0, "refinery", refinery.Tile, 0)); // occupied
        Refused(sim, new DebugPlaceBuildingCommand(0, 0, "starbase", FreeTile(sim, hub), 0)); // not a surface building

        Do(sim, new SalvageCommand(0, refinery.Id)); // placed free: nothing comes back
        Assert.Null(sim.Surface!.FindStructure(refinery.Id));
        About(duranium, sim.Players[0].Stock[R("duranium")]);
    }

    [Fact]
    public void ConditionBrokenAndStopWear()
    {
        var sim = Landed(out var hub);
        var refinery = Placed(sim, hub, "refinery");
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(34)));
        Assert.Equal(F(34), refinery.Condition);
        Assert.True(sim.Surface!.ConditionFactor(refinery) < Fix64.One);
        Refused(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(101)));
        var container = Placed(sim, hub, "storage_container", 1);
        Refused(sim, new DebugStructureCommand(0, container.Id, DebugStructureField.Condition, F(50))); // doesn't wear

        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Broken, Fix64.One));
        Assert.True(refinery.Broken);
        Assert.Equal(StructureStatus.Broken, refinery.Status);
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Broken, Fix64.Zero));
        Assert.False(refinery.Broken);
        Assert.Equal(F(34), refinery.Condition);

        // Busy with ore, it wears; with stop-wear it doesn't.
        sim.Players[0].Stock.Add(R("duranium_ore"), F(800));
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.NoWear, Fix64.One));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.Equal(F(34), refinery.Condition);
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.NoWear, Fix64.Zero));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.True(refinery.Condition < F(34));
    }

    [Fact]
    public void LevelAndWorkSpeed()
    {
        var sim = Landed(out var hub);
        var refinery = Placed(sim, hub, "refinery");
        var speed = sim.Surface!.SpeedMultiplier(refinery);
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Level, F(3)));
        Assert.Equal(3, refinery.Level);
        Assert.Equal("Mk III", sim.Surface.LevelName(refinery));
        Refused(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Level, F(4)));
        Refused(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Level, Fix64.Half));
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Level, Fix64.One));
        Assert.Equal(speed, sim.Surface.SpeedMultiplier(refinery));

        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.WorkSpeed, F(5)));
        Assert.Equal(speed * F(5), sim.Surface.SpeedMultiplier(refinery));
        Refused(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.WorkSpeed, Fix64.Zero));
        Refused(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.WorkSpeed, F(500)));
    }

    [Fact]
    public void ConstructionUpgradesAndOrdersFinishNow()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var tile = FreeTile(sim, hub);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", tile, 0));
        var container = s.StructureAt(tile)!;
        Assert.NotEqual(StructureState.Complete, container.State);
        Ok(sim, new DebugStructureCommand(0, container.Id, DebugStructureField.BuildProgress, Fix64.Half));
        Assert.InRange(s.BuildFraction(container).ToDouble(), 0.45, 0.55);
        Ok(sim, new DebugStructureCommand(0, container.Id, DebugStructureField.BuildProgress, Fix64.One));
        Assert.Equal(StructureState.Complete, container.State);
        Refused(sim, new DebugStructureCommand(0, container.Id, DebugStructureField.BuildProgress, Fix64.One)); // already built

        // The hub's upgrade to Colonial Administration.
        var core = s.StructureAt(hub)!;
        Refused(sim, new DebugStructureCommand(0, core.Id, DebugStructureField.UpgradeProgress, Fix64.One)); // not upgrading
        sim.Players[0].Stock.Add(R("duranium"), F(1000));
        sim.Players[0].Stock.Add(R("tritanium"), F(1000));
        Do(sim, new UpgradeCommand(0, core.Id));
        Assert.NotNull(core.UpgradingTo);
        Ok(sim, new DebugStructureCommand(0, core.Id, DebugStructureField.UpgradeProgress, Fix64.One));
        Assert.Equal("colonial_administration", core.DefId);

        // A Vehicle Bay order rolls out at once.
        var bay = Placed(sim, hub, "vehicle_bay", 1);
        sim.Players[0].Stock.Add(R("duranium"), F(1000));
        sim.Players[0].Stock.Add(R("crew"), F(100));
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Assert.Single(bay.Production);
        var units = s.Units.Count;
        Ok(sim, new DebugStructureCommand(0, bay.Id, DebugStructureField.ProductionProgress, Fix64.One));
        Assert.Empty(bay.Production);
        Assert.Equal(units + 1, s.Units.Count);
    }

    [Fact]
    public void RemovingBuildingsGivesNothingBackAndKeepsBasesWhole()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var tile = FreeTile(sim, hub);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", tile, 0));
        var container = s.StructureAt(tile)!;
        var duranium = sim.Players[0].Stock[R("duranium")];
        var core = s.StructureAt(hub)!;
        Refused(sim, new DebugRemoveStructureCommand(0, core.Id)); // its base still has a building
        Ok(sim, new DebugRemoveStructureCommand(0, container.Id));
        Assert.Null(s.StructureAt(tile));
        About(duranium, sim.Players[0].Stock[R("duranium")]);
        Ok(sim, new DebugRemoveStructureCommand(0, core.Id));
        Assert.Empty(s.Bases(0));
    }

    [Fact]
    public void WholeBaseActions()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var baseId = s.StructureAt(hub)!.Id;
        var refinery = Placed(sim, hub, "refinery");
        var reactor = Placed(sim, hub, "fusion_reactor", 1);

        Ok(sim, new DebugBaseCommand(0, baseId, DebugBaseAction.SetCondition, F(30)));
        About(F(30), refinery.Condition);
        About(F(30), reactor.Condition);
        Refused(sim, new DebugBaseCommand(0, refinery.Id, DebugBaseAction.RepairAll, Fix64.Zero)); // not a base
        Ok(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Broken, Fix64.One));
        Ok(sim, new DebugBaseCommand(0, baseId, DebugBaseAction.RepairAll, Fix64.Zero));
        About(Hundred, refinery.Condition);
        About(Hundred, reactor.Condition);
        Assert.False(refinery.Broken);

        var a = FreeTile(sim, hub);
        var b = FreeTile(sim, hub, 1);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", a, 0));
        Do(sim, new PlaceBuildingCommand(0, "storage_container", b, 0));
        Ok(sim, new DebugBaseCommand(0, baseId, DebugBaseAction.FinishConstruction, Fix64.Zero));
        Assert.Equal(StructureState.Complete, s.StructureAt(a)!.State);
        Assert.Equal(StructureState.Complete, s.StructureAt(b)!.State);

        var bay = Placed(sim, hub, "vehicle_bay", 2);
        sim.Players[0].Stock.Add(R("duranium"), F(2000));
        sim.Players[0].Stock.Add(R("crew"), F(200));
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Do(sim, new BuildUnitCommand(0, bay.Id, "mining_crawler"));
        var units = s.Units.Count;
        Ok(sim, new DebugBaseCommand(0, baseId, DebugBaseAction.FinishOrders, Fix64.Zero));
        Assert.Empty(bay.Production);
        Assert.Equal(units + 2, s.Units.Count);
    }

    // ---------------------------------------------------------------- resources and limits

    [Fact]
    public void StockIsSetExactly()
    {
        var sim = Landed(out _);
        Ok(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium"), F(505)), new ResourceAmount(R("latinum"), Fix64.Zero) }));
        About(F(505), sim.Players[0].Stock[R("duranium")]);
        Assert.Equal(Fix64.Zero, sim.Players[0].Stock[R("latinum")]);
        Refused(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium"), F(-1)) }));
        Refused(sim, new DebugStockCommand(0, 3, new[] { new ResourceAmount(R("duranium"), F(1)) }));
        Refused(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(999, F(1)) }));
        About(F(505), sim.Players[0].Stock[R("duranium")]);
    }

    [Fact]
    public void NoStorageCapLetsStockGrowPastCapacity()
    {
        var sim = Landed(out _);
        var s = sim.Surface!;
        var cap = s.StorageCapacity(0);
        // Fill storage to the brim with duranium: the hub's trickle then has nowhere to go.
        var full = cap - s.Stored(sim, 0) + sim.Players[0].Stock[R("duranium")];
        Ok(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium"), full) }));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.True(s.Stored(sim, 0) <= cap);
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.NoStorageCap, true));
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.True(s.Stored(sim, 0) > cap);
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.NoStorageCap, false));
        Assert.Equal(Fix64.Zero, s.FreeStorage(sim, 0));
    }

    [Fact]
    public void UnlimitedPowerCoversEveryBase()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var baseId = s.StructureAt(hub)!.Id;
        for (var i = 0; i < 5; i++) Placed(sim, hub, "refinery"); // 5 × 8 power against the hub's 30
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.NoStorageCap, true));
        Ok(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium_ore"), F(20000)) }));
        sim.Step();
        Assert.True(s.PowerOf(baseId).Efficiency < Fix64.One);
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.UnlimitedPower, true));
        sim.Step();
        Assert.Equal(Fix64.One, s.PowerOf(baseId).Efficiency);
        Assert.True(s.Structures.Where(x => x.DefId == "refinery").All(x => x.Status == StructureStatus.Working));
    }

    [Fact]
    public void FreeBuildingCostsNothingAndRefundsNothing()
    {
        var sim = Landed(out var hub);
        Ok(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium"), Fix64.Zero) }));
        var tile = FreeTile(sim, hub);
        Assert.Equal(PlacementProblem.CannotAfford, sim.Surface!.CheckPlacement(sim, 0, "storage_container", tile));
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.FreeBuilding, true));
        Assert.Equal(PlacementProblem.None, sim.Surface.CheckPlacement(sim, 0, "storage_container", tile));
        Do(sim, new PlaceBuildingCommand(0, "storage_container", tile, 0));
        var s = sim.Surface.StructureAt(tile)!;
        Assert.True(s.Free);
        Do(sim, new CancelBuildingCommand(0, s.Id));
        Assert.Null(sim.Surface.StructureAt(tile));
        About(Fix64.Zero, sim.Players[0].Stock[R("duranium")]);
    }

    [Fact]
    public void InstantConstructionFinishesBuildingsAndUpgradesAtOnce()
    {
        var sim = Landed(out var hub);
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.InstantBuild, true));
        var a = FreeTile(sim, hub);
        var b = FreeTile(sim, hub, 1);
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "storage_container", a, 0));
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "refinery", b, 0));
        sim.Step();
        Assert.Equal(StructureState.Complete, sim.Surface!.StructureAt(a)!.State); // both at once: no queue
        Assert.Equal(StructureState.Complete, sim.Surface.StructureAt(b)!.State);

        var refinery = sim.Surface.StructureAt(b)!;
        sim.Players[0].Stock.Add(R("tritanium"), F(1000));
        Do(sim, new UpgradeCommand(0, refinery.Id));
        Assert.Equal(2, refinery.Level);
    }

    // ---------------------------------------------------------------- research and map

    [Fact]
    public void TechsAreCompletedAndForgotten()
    {
        var sim = Landed(out _);
        var p = sim.Surface!.Players[0];
        var tech = Data.Techs[0];
        Do(sim, new ResearchCommand(0, tech.Id));
        Assert.Equal(tech.Index, p.ActiveTech);
        Ok(sim, new DebugTechCommand(0, 0, tech.Id, true, tech.Cost));
        Assert.True(p.Known[tech.Index]);
        Assert.Equal(-1, p.ActiveTech);
        Ok(sim, new DebugTechCommand(0, 0, tech.Id, false, Fix64.Zero));
        Assert.False(p.Known[tech.Index]);
        Assert.Equal(Fix64.Zero, p.ResearchProgress[tech.Index]);
        Refused(sim, new DebugTechCommand(0, 0, "warp_drive", true, Fix64.Zero));
        Refused(sim, new DebugTechCommand(0, 0, tech.Id, false, tech.Cost + Fix64.One));
    }

    [Fact]
    public void SurveyEverythingAndSetDeposits()
    {
        var sim = Landed(out _);
        var s = sim.Surface!;
        Assert.Contains(false, s.Players[0].Surveyed);
        Ok(sim, new DebugSurveyAllCommand(0, 0));
        Assert.DoesNotContain(false, s.Players[0].Surveyed);

        var map = s.Map;
        var deposit = Enumerable.Range(0, map.Grid.Count).First(t => map.DepositResource[t] >= 0);
        Ok(sim, new DebugDepositCommand(0, deposit, F(1234)));
        Assert.Equal(F(1234), map.DepositAmount[deposit]);
        Ok(sim, new DebugDepositCommand(0, deposit, Fix64.Zero));
        Assert.True(map.IsDepleted(deposit));
        var bare = Enumerable.Range(0, map.Grid.Count).First(t => map.DepositResource[t] < 0);
        Refused(sim, new DebugDepositCommand(0, bare, F(10)));
    }

    // ---------------------------------------------------------------- determinism

    /// <summary>A landing, some play and a stream of test-panel edits; returns the hash after every tick.</summary>
    private static List<ulong> Script(bool edits, bool refusedOnly = false)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        var landed = Data.Landing.DescentSeconds * Simulation.TickRate + 2;
        var hashes = new List<ulong>();
        for (var i = 0; i < landed + 400; i++)
        {
            if (i == landed && edits)
            {
                var crawler = sim.Surface.Units.First(u => u.DefId == "mining_crawler");
                var tile = sim.Surface.Map.Grid.Within(site, 2).First(t => t != site && sim.Surface.Map.Terrain(t).Buildable && sim.Surface.StructureAt(t) == null && sim.Surface.Units.All(u => u.Tile != t));
                if (refusedOnly)
                {
                    sim.Schedule(sim.Tick + 1, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Integrity, F(500)));
                    sim.Schedule(sim.Tick + 1, new DebugStockCommand(0, 7, new[] { new ResourceAmount(R("duranium"), F(1)) }));
                }
                else
                {
                    sim.Schedule(sim.Tick + 1, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Integrity, F(40)));
                    sim.Schedule(sim.Tick + 1, new DebugFlagCommand(0, 0, DebugFlag.InstantBuild, true));
                    sim.Schedule(sim.Tick + 2, new DebugPlaceBuildingCommand(0, 0, "refinery", tile, 2));
                    sim.Schedule(sim.Tick + 3, new DebugStockCommand(0, 0, new[] { new ResourceAmount(R("duranium_ore"), F(400)) }));
                    sim.Schedule(sim.Tick + 4, new DebugSpawnUnitCommand(0, 0, "survey_rover", site));
                    sim.Schedule(sim.Tick + 5, new DebugDataCommand(0, new[] { new DataEdit("buildings/surface/refinery.json", WearPath(), "30") }));
                    sim.Schedule(sim.Tick + 6, new DebugTechCommand(0, 0, Data.Techs[0].Id, true, Data.Techs[0].Cost));
                }
            }
            sim.Step();
            hashes.Add(sim.StateHash());
        }
        return hashes;
    }

    private static string WearPath() => DataTuning.Numbers(Data).First(n => n.File == "buildings/surface/refinery.json" && n.Label == "buildings › refinery › wear_per_min").Path;

    [Fact]
    public void DebugEditsAreDeterministic() => Assert.Equal(Script(true), Script(true));

    [Fact]
    public void DebugEditsChangeTheStateHash()
    {
        var clean = Script(false);
        var tested = Script(true);
        var at = Data.Landing.DescentSeconds * Simulation.TickRate + 2;
        Assert.Equal(clean[..at], tested[..at]);
        Assert.NotEqual(clean[^1], tested[^1]);
    }

    [Fact]
    public void RefusedEditsChangeNothing() => Assert.Equal(Script(false), Script(true, refusedOnly: true));
}
