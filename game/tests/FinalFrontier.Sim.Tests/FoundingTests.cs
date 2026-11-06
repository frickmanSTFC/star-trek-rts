using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Founding a second outpost on the ground: the construction rover is built at the Vehicle Bay, drives to a site, pays
/// and unfolds there, and is used up as a new Colony Hub under construction that the exocomps build into a working base.
/// </summary>
public class FoundingTests
{
    private const string Rover = "construction_rover";
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;
    private static FoundRules Rules(GameData d) => d.SurfaceUnit(Rover).Found!;

    /// <summary>The real data without driving hazards, flats or wildlife, so a drive across the map always gets there.</summary>
    private static GameData SafeRoads() => TestData.Regexed("surfaces.json",
        (@"""hazard"": 0\.\d+", @"""hazard"": 0"), (@"""flat"": 0\.\d+", @"""flat"": 0"),
        (@"""wildlife"": \{ ""chance_per_min"": [0-9.]+", @"""wildlife"": { ""chance_per_min"": 0"));

    /// <summary>Landed and assembled, with plenty in stock.</summary>
    private static Simulation Landed(GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        Rich(sim, data);
        return sim;
    }

    private static void Rich(Simulation sim, GameData data)
    {
        sim.Players[0].Stock.Add(data.Resource("duranium").Index, Fix64.FromInt(3000));
        sim.Players[0].Stock.Add(data.Resource("tritanium").Index, Fix64.FromInt(1000));
        sim.Players[0].Stock.Add(data.Resource("crew").Index, Fix64.FromInt(300));
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static int Hub(Simulation sim) => sim.Surface!.Players[0].LandingTile;

    private static SurfaceUnit Spawn(Simulation sim, int near)
    {
        var before = sim.Surface!.Units.Select(u => u.Id).ToHashSet();
        Do(sim, new DebugSpawnUnitCommand(0, 0, Rover, near));
        return sim.Surface.Units.Single(u => !before.Contains(u.Id));
    }

    /// <summary>A site the rover can drive to, between minDistance and minDistance + 4 tiles from the hub.</summary>
    private static int Site(Simulation sim, SurfaceUnit rover, int extra = 4, Func<int, bool>? also = null)
    {
        var s = sim.Surface!;
        var rules = Rules(sim.Data);
        var hub = Hub(sim);
        return Enumerable.Range(0, s.Map.Grid.Count).First(t =>
        {
            var d = s.Map.Grid.Distance(t, hub);
            return d >= rules.MinDistance && d <= rules.MinDistance + extra && s.SiteProblem(sim, 0, t, rules, rover) == FoundProblem.None
                && (also == null || also(t)) && Pathfinder.Find(s.Map, rover.Tile, t) != null;
        });
    }

    private static List<Fix64> Stock(Simulation sim) => sim.Data.Resources.Select(r => sim.Players[0].Stock[r.Index]).ToList();

    private static void StepUntil(Simulation sim, Func<bool> done, int limit)
    {
        for (var i = 0; i < limit && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    [Fact]
    public void TheConstructionRoverIsARoverThatFoundsCheaperAndCloserThanADrop()
    {
        var def = Data.SurfaceUnit(Rover);
        Assert.True(def.CanFound);
        Assert.False(def.CanMine);
        Assert.Contains(Rover, Data.Building("vehicle_bay").Produces);
        Assert.Equal("Construction Rover", Data.GroundVehicleNames[Rover]);
        Assert.NotNull(def.Damage);
        Assert.Equal(6, def.Tyres!.Wheels);
        Assert.True(def.Speed < Data.SurfaceUnit("survey_rover").Speed, "slower than the Mk I");
        Assert.True(def.SurveyRadius <= 1);
        Assert.True(def.DeploySeconds > 0);
        Assert.False(string.IsNullOrEmpty(def.Description));
        var rules = def.Found!;
        Assert.Equal(Data.Orbit!.OutpostBuilding, rules.Building);
        Assert.True(Data.Building(rules.Building).BuildSeconds > 0, "the exocomps need a build time");
        Assert.True(rules.MinDistance < Data.Orbit.OutpostMinDistance);
        Assert.True(rules.MinDistance >= 2 * Data.Building(rules.Building).BuildRadius, "build areas never overlap");
        Assert.True(rules.Cost.Sum(c => c.Amount.ToFloat()) < Data.Orbit.OutpostCost.Sum(c => c.Amount.ToFloat()));
        Assert.Empty(Data.Validate());
    }

    [Fact]
    public void BuiltAtTheBayItDrivesToTheSiteFoundsAndTheNewBaseWorks()
    {
        var data = SafeRoads();
        var sim = Landed(data);
        var s = sim.Surface!;
        var hub = Hub(sim);
        // A finished Vehicle Bay, then a construction rover from it.
        var bayTile = s.Map.Grid.Within(hub, 2).First(t => s.Map.Grid.Distance(t, hub) == 2 && s.CheckPlacement(sim, 0, "vehicle_bay", t) == PlacementProblem.None);
        Do(sim, new PlaceBuildingCommand(0, "vehicle_bay", bayTile, 0));
        var bay = s.StructureAt(bayTile)!;
        StepUntil(sim, () => bay.State == StructureState.Complete, Sec(60));
        sim.Step();
        var paid = Stock(sim);
        Do(sim, new BuildUnitCommand(0, bay.Id, Rover));
        var unitCost = data.SurfaceUnit(Rover).Cost;
        foreach (var c in unitCost) Assert.Equal(paid[c.Resource] - c.Amount, sim.Players[0].Stock[c.Resource]);
        StepUntil(sim, () => s.Units.Any(u => u.DefId == Rover), Sec(data.SurfaceUnit(Rover).BuildSeconds + 10));
        var rover = s.Units.Single(u => u.DefId == Rover);
        Assert.False(string.IsNullOrEmpty(rover.Name));
        Assert.False(string.IsNullOrEmpty(rover.Number));
        Assert.Equal(bayTile, rover.BuiltAtTile);

        // Drive and found: nothing is paid until it gets there.
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var site = Site(sim, rover);
        Assert.Equal(FoundProblem.None, s.FoundOutpostProblem(sim, 0, rover.Id, site));
        var before = Stock(sim);
        var cores = s.Bases(0).Count;
        Do(sim, new FoundOutpostCommand(0, rover.Id, site));
        Assert.Equal(site, rover.FoundTile);
        Assert.NotEmpty(rover.Path);
        Assert.True(rover.DeployOnArrival);
        StepUntil(sim, () => rover.Mode == UnitMode.Deploying, Sec(120));
        Assert.Equal(site, rover.Tile);
        Assert.True(s.Unfolding(rover));
        var cost = Rules(data).Cost;
        foreach (var c in cost) Assert.True(sim.Players[0].Stock[c.Resource] <= before[c.Resource] - c.Amount + Fix64.FromInt(2)); // paid (plus a little hub trickle)

        // Unfolded: the rover is used up and the new hub stands there under construction, as a base of its own.
        StepUntil(sim, () => s.FindUnit(rover.Id) == null, Sec(data.SurfaceUnit(Rover).DeploySeconds + 2));
        var core = s.StructureAt(site)!;
        Assert.Equal(Rules(data).Building, core.DefId);
        Assert.Same(rover, core.Founder);
        Assert.True(core.Founded);
        Assert.Equal(core.Id, core.BaseId);
        Assert.Equal(cores + 1, s.Bases(0).Count);
        Assert.NotEqual(StructureState.Complete, core.State);
        sim.Step();
        Assert.Equal(StructureState.Building, core.State); // its own build queue: the exocomps start at once
        Assert.True(core.BuildTicks > 0);
        // Nothing can join the base until it is finished.
        var near = s.Map.Grid.Within(site, 2).First(t => t != site && s.Map.Terrain(t).Buildable && s.StructureAt(t) == null && !s.Map.NearNatives(t) && s.Map.DepositResource[t] < 0);
        Assert.Equal(PlacementProblem.CoreUnderConstruction, s.CheckPlacement(sim, 0, "storage_container", near));
        Assert.Equal(0, s.PowerOf(core.Id).Supply.CompareTo(Fix64.Zero));

        // Built: a working base with its own queue, power, storage and build range.
        var queues = s.BuildQueues(0);
        var capacity = s.StorageCapacity(0);
        StepUntil(sim, () => core.State == StructureState.Complete, Sec(data.Building(core.DefId).BuildSeconds + 2));
        sim.Step();
        Assert.Equal(queues + data.Building(core.DefId).BuildQueues, s.BuildQueues(0));
        Assert.Equal(capacity + data.Building(core.DefId).Storage, s.StorageCapacity(0));
        Assert.Equal(data.Building(core.DefId).PowerSupply, s.PowerOf(core.Id).Supply);
        Assert.Equal(core.Id, s.BaseFor(0, near));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "storage_container", near));
        Do(sim, new PlaceBuildingCommand(0, "storage_container", near, 0));
        Assert.Equal(core.Id, s.StructureAt(near)!.BaseId);
        Assert.NotNull(s.NearestRepairPad(0, site));
        // The rover can't come back now: cancelling a finished core does nothing.
        Do(sim, new CancelBuildingCommand(0, core.Id));
        Assert.Same(core, s.StructureAt(site));
        Assert.Null(s.FindUnit(rover.Id));
    }

    [Fact]
    public void EachBadSiteIsRefusedWithAReason()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var map = s.Map;
        var hub = Hub(sim);
        var rover = Spawn(sim, hub);
        var rules = Rules(Data);
        FoundProblem P(int tile) => s.FoundOutpostProblem(sim, 0, rover.Id, tile);

        var unsurveyed = Enumerable.Range(0, map.Grid.Count).First(t => !s.Players[0].Surveyed[t] && map.Terrain(t).Buildable);
        Assert.Equal(FoundProblem.NotSurveyed, P(unsurveyed));
        Do(sim, new DebugSurveyAllCommand(0, 0));
        Assert.Equal(FoundProblem.OffMap, P(-1));
        Assert.Equal(FoundProblem.OffMap, P(map.Grid.Count));
        var survey = s.Units.First(u => u.DefId == "survey_rover");
        Assert.Equal(FoundProblem.CantFound, s.FoundOutpostProblem(sim, 0, survey.Id, unsurveyed));
        Assert.Equal(FoundProblem.NotYours, s.FoundOutpostProblem(sim, 1, rover.Id, unsurveyed));
        Assert.Equal(FoundProblem.NotYours, s.FoundOutpostProblem(sim, 0, 9999, unsurveyed));

        bool Far(int t) => map.Grid.Distance(t, hub) >= rules.MinDistance;
        var forest = Enumerable.Range(0, map.Grid.Count).First(t => Far(t) && !map.Terrain(t).Buildable && map.Terrain(t).Passable);
        Assert.Equal(FoundProblem.Unbuildable, P(forest));
        Assert.Contains(map.Terrain(forest).Name.ToLowerInvariant(), s.FoundText(FoundProblem.Unbuildable, forest));
        if (map.NativeTile >= 0)
        {
            var native = Enumerable.Range(0, map.Grid.Count).First(t => map.NearNatives(t) && map.Terrain(t).Buildable);
            Assert.Equal(FoundProblem.NativeLand, P(native));
        }
        var toxic = Enumerable.Range(0, map.Grid.Count).FirstOrDefault(t => Far(t) && map.Toxic[t] && map.Terrain(t).Buildable && !map.NearNatives(t), -1);
        if (toxic >= 0) Assert.Equal(FoundProblem.Toxic, P(toxic));
        var predators = Enumerable.Range(0, map.Grid.Count).FirstOrDefault(t => Far(t) && map.WildlifeAt[t] == Wildlife.Predators && !map.Toxic[t] && map.Terrain(t).Buildable && !map.NearNatives(t), -1);
        if (predators >= 0) Assert.Equal(FoundProblem.Predators, P(predators));
        Assert.True(toxic >= 0 || predators >= 0, "seed 1701 has toxic patches or predators");

        var nextToHub = map.Grid.Neighbours(hub).First(t => map.Terrain(t).Buildable && !map.Toxic[t] && map.WildlifeAt[t] != Wildlife.Predators);
        Assert.Equal(FoundProblem.BuildingInTheWay, P(nextToHub));
        var close = map.Grid.Within(hub, rules.MinDistance - 1).First(t => map.Grid.Distance(t, hub) >= 3 && s.SiteProblem(sim, 0, t, rules with { MinDistance = 1 }, rover) == FoundProblem.None);
        Assert.Equal(FoundProblem.TooCloseToBase, P(close));
        Assert.Contains($"{rules.MinDistance} tiles", s.FoundText(FoundProblem.TooCloseToBase, close, rules.MinDistance));

        var site = Site(sim, rover);
        Assert.Equal(FoundProblem.None, P(site));
        var stock = sim.Players[0].Stock;
        var crew = stock[R("crew")];
        stock.Add(R("crew"), -crew);
        Assert.Equal(FoundProblem.CannotAfford, P(site));
        stock.Add(R("crew"), crew);

        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Broken, Fix64.One));
        Assert.True(rover.Broken);
        Assert.Equal(FoundProblem.Disabled, P(site));

        // Before landing there is nowhere to found anything.
        var fresh = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Equal(FoundProblem.NotLanded, fresh.Surface!.SiteProblem(fresh, 0, site, rules, null));
        // Every problem has a text.
        foreach (var p in Enum.GetValues<FoundProblem>().Where(p => p != FoundProblem.None))
            Assert.False(string.IsNullOrEmpty(s.FoundText(p, site, rules.MinDistance)));
    }

    [Fact]
    public void ARefusedOrderDoesNothing()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var hub = Hub(sim);
        var rover = Spawn(sim, hub);
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var nextToHub = s.Map.Grid.Neighbours(hub).First(t => s.Map.Terrain(t).Buildable);
        var before = Stock(sim);
        Do(sim, new FoundOutpostCommand(0, rover.Id, nextToHub));
        Assert.Equal(-1, rover.FoundTile);
        Assert.Empty(rover.Path);
        Assert.False(rover.DeployOnArrival);
        Assert.Equal(UnitMode.Mobile, rover.Mode);
        Assert.True(Stock(sim).Zip(before).All(x => x.First >= x.Second)); // only the hub's trickle came in
    }

    [Fact]
    public void SpacingAppliesToOtherFoundersAndOutpostDrops()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var hub = Hub(sim);
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var a = Spawn(sim, hub);
        var site = Site(sim, a, 6, t => s.Map.Safety(t) != LandingSafety.Unsafe && s.Map.Grid.Distance(t, hub) >= Data.Orbit!.OutpostMinDistance); // a drop could land there too
        Do(sim, new DebugMoveUnitCommand(0, a.Id, site));
        Assert.Equal(site, a.Tile);
        var b = Spawn(sim, hub);
        Do(sim, new FoundOutpostCommand(0, a.Id, site));
        Assert.True(s.Unfolding(a));
        Assert.Equal(FoundProblem.AlreadyFounding, s.FoundOutpostProblem(sim, 0, a.Id, site));
        var rules = Rules(Data);
        var nearA = s.Map.Grid.Within(site, rules.MinDistance - 1).First(t => t != site && s.Map.Grid.Distance(t, site) >= 2 && s.Map.Grid.Distance(t, hub) >= Data.Orbit!.OutpostMinDistance
            && s.SiteProblem(sim, 0, t, rules, a) == FoundProblem.None && s.Map.Safety(t) != LandingSafety.Unsafe);
        Assert.Equal(FoundProblem.FounderNearby, s.FoundOutpostProblem(sim, 0, b.Id, nearA));
        Assert.Equal("a construction rover is founding an outpost near there", s.OutpostProblem(sim, 0, nearA));

        // And a drop on its way down keeps founders away from its site.
        Do(sim, new MoveUnitsCommand(0, new[] { a.Id }, hub)); // a folds up and goes home (refunded)
        Assert.False(s.Unfolding(a));
        Assert.Equal((string?)null, s.OutpostProblem(sim, 0, site));
        Do(sim, new OutpostDropCommand(0, site));
        Assert.Contains(s.Deliveries, d => d.Kind == DeliveryKind.Outpost && d.Tile == site);
        Assert.Equal(FoundProblem.OutpostIncoming, s.FoundOutpostProblem(sim, 0, b.Id, nearA));
    }

    [Fact]
    public void MovingOffWhileUnfoldingFoldsItUpAndRefunds()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var hub = Hub(sim);
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var rover = Spawn(sim, hub);
        var site = Site(sim, rover);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, site));
        var before = Stock(sim);
        Do(sim, new FoundOutpostCommand(0, rover.Id, site)); // on the tile: unfolds at once
        Assert.Equal(UnitMode.Deploying, rover.Mode);
        foreach (var c in Rules(Data).Cost) Assert.True(sim.Players[0].Stock[c.Resource] < before[c.Resource]);
        for (var i = 0; i < Sec(2); i++) sim.Step();
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, hub));
        Assert.Equal(UnitMode.Packing, rover.Mode);
        Assert.Equal(-1, rover.FoundTile);
        foreach (var c in Rules(Data).Cost) Assert.True(sim.Players[0].Stock[c.Resource] >= before[c.Resource]);
        Assert.Null(s.StructureAt(site));
        StepUntil(sim, () => rover.Mode == UnitMode.Mobile, Sec(10));
        Assert.Null(s.StructureAt(site));

        // Pack up (F) works the same way.
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, site));
        Do(sim, new FoundOutpostCommand(0, rover.Id, site));
        Assert.True(s.Unfolding(rover));
        Do(sim, new PackUpCommand(0, new[] { rover.Id }));
        Assert.False(s.Unfolding(rover));
        foreach (var c in Rules(Data).Cost) Assert.True(sim.Players[0].Stock[c.Resource] >= before[c.Resource]);
    }

    [Fact]
    public void CancellingTheUnfinishedHubRefundsAndTheRoverComesBack()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var hub = Hub(sim);
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var rover = Spawn(sim, hub);
        var site = Site(sim, rover);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, site));
        var (name, number, id) = (rover.Name, rover.Number, rover.Id);
        var cost = Rules(Data).Cost;
        var before = Stock(sim);
        Do(sim, new FoundOutpostCommand(0, rover.Id, site));
        StepUntil(sim, () => s.StructureAt(site) != null, Sec(Data.SurfaceUnit(Rover).DeploySeconds + 2));
        var core = s.StructureAt(site)!;
        for (var i = 0; i < Sec(10); i++) sim.Step();
        Assert.Equal(StructureState.Building, core.State);
        var spent = Stock(sim);
        foreach (var c in cost) Assert.True(spent[c.Resource] < before[c.Resource]);

        Do(sim, new CancelBuildingCommand(0, core.Id));
        Assert.Null(s.StructureAt(site));
        Assert.DoesNotContain(s.Bases(0), b => b.Id == core.Id);
        foreach (var c in cost) Assert.True(sim.Players[0].Stock[c.Resource] >= spent[c.Resource] + c.Amount);
        var back = s.FindUnit(id)!;
        Assert.Same(rover, back);
        Assert.Equal((name, number), (back.Name, back.Number));
        Assert.Equal(site, back.Tile);
        Assert.Equal(UnitMode.Packing, back.Mode); // the crane folds up
        StepUntil(sim, () => back.Mode == UnitMode.Mobile, Sec(10));
        Assert.Equal(FoundProblem.None, s.FoundOutpostProblem(sim, 0, back.Id, site)); // and it can try again
    }

    [Fact]
    public void ArrivingWhenItCanNoLongerPayDoesntFound()
    {
        var data = SafeRoads();
        var sim = Landed(data);
        var s = sim.Surface!;
        var hub = Hub(sim);
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var rover = Spawn(sim, hub);
        var site = Site(sim, rover);
        Do(sim, new FoundOutpostCommand(0, rover.Id, site));
        var crew = R("crew");
        var stock = sim.Players[0].Stock;
        stock.Add(crew, -stock[crew]); // spent on the way
        StepUntil(sim, () => rover.Path.Count == 0, Sec(120));
        sim.Step();
        Assert.Equal(site, rover.Tile);
        Assert.Equal(UnitMode.Mobile, rover.Mode);
        Assert.Equal(-1, rover.FoundTile);
        Assert.Equal(FoundProblem.CannotAfford, rover.FoundFailure);
        Assert.True(rover.FoundFailedTick > 0);
        Assert.Null(s.StructureAt(site));
    }

    [Fact]
    public void FoundingIsDeterministic()
    {
        ulong Run(List<ulong> trace)
        {
            var data = TestData.Real;
            var sim = Landed(data);
            Do(sim, new DebugSurveyAllCommand(0, 0));
            var rover = Spawn(sim, Hub(sim));
            var site = Site(sim, rover);
            Do(sim, new FoundOutpostCommand(0, rover.Id, site));
            for (var i = 0; i < Sec(150); i++)
            {
                sim.Step();
                if (i % 60 == 0) trace.Add(sim.StateHash());
            }
            return sim.StateHash();
        }
        var a = new List<ulong>();
        var b = new List<ulong>();
        Assert.Equal(Run(a), Run(b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void TheFounderIsInTheStateHash()
    {
        var sim = Landed();
        var s = sim.Surface!;
        Do(sim, new DebugSurveyAllCommand(0, 0));
        var rover = Spawn(sim, Hub(sim));
        var site = Site(sim, rover);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, site));
        Do(sim, new FoundOutpostCommand(0, rover.Id, site));
        StepUntil(sim, () => s.StructureAt(site) != null, Sec(Data.SurfaceUnit(Rover).DeploySeconds + 2));
        var core = s.StructureAt(site)!;
        Assert.NotNull(core.Founder);
        // The same game with the rover's spare tyre count changed before it founded: only the kept rover differs.
        var other = Landed();
        Do(other, new DebugSurveyAllCommand(0, 0));
        var twin = Spawn(other, Hub(other));
        Do(other, new DebugMoveUnitCommand(0, twin.Id, site));
        Do(other, new DebugUnitCommand(0, twin.Id, DebugUnitField.Spares, Fix64.Zero));
        Do(sim, new DebugUnitCommand(0, 999, DebugUnitField.Spares, Fix64.Zero)); // keeps the debug-edit count level (refused: no such unit)
        Do(other, new FoundOutpostCommand(0, twin.Id, site));
        while (other.Tick < sim.Tick) other.Step();
        Assert.Equal(sim.Tick, other.Tick);
        Assert.Equal(0, other.Surface!.StructureAt(site)!.Founder!.Spares);
        Assert.NotEqual(sim.StateHash(), other.StateHash());
    }
}
