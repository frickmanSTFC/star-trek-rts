using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The parts economy (docs/parts-contract.md): Machine Shop and hub workshop queues, keep-in-stock targets, storage
/// volume, repairs that take spare kits (and wait for them), jury-rigging, refusals and determinism.</summary>
public class FabricationTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static Fix64 F(int v) => Fix64.FromInt(v);
    private static int Sec(int s) => s * Simulation.TickRate;

    /// <summary>A landed colony with plenty of metal; keep-in-stock targets off unless asked for.</summary>
    private static Simulation Landed(out SurfaceStructure hub, bool targets = false, ulong seed = 1701)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, seed);
        if (!targets) TestData.NoStockTargets(sim);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        hub = sim.Surface.Bases(0).Single();
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium"), F(600));
        stock.Add(R("tritanium"), F(300));
        stock.Add(R("parasteel"), F(100));
        // Room for all that (the hub's vault alone would be over full).
        var tile = FreeTile(sim, hub, "storage_container", 3);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "storage_container", tile, 0));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static void Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    private static int FreeTile(Simulation sim, SurfaceStructure hub, string building, int skip = 0) =>
        sim.Surface!.Map.Grid.Within(hub.Tile, 3).Where(t => sim.Surface.CheckPlacement(sim, 0, building, t) == PlacementProblem.None).Skip(skip).First();

    /// <summary>A finished Machine Shop beside the hub (placed by the test panel: free and at once).</summary>
    private static SurfaceStructure Shop(Simulation sim, SurfaceStructure hub, int skip = 0)
    {
        var tile = FreeTile(sim, hub, "machine_shop", skip);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "machine_shop", tile, 0));
        var shop = sim.Surface!.StructureAt(tile)!;
        Assert.Equal(StructureState.Complete, shop.State);
        return shop;
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void PartsAreResourcesWithRecipesAndVolumes()
    {
        var s = MatchSetup.CreatePlanetLanding(Data, 1).Surface!;
        Assert.Equal(new[] { "structural_frame", "hull_plating", "mechanism", "power_coupling", "control_module", "seal_kit",
            "mechanical_spares", "electrical_spares", "rover_spares", "environmental_spares", "prefab_module", "machinery_set" }, s.PartItems);
        Assert.Equal(Fix64.One, Data.Resource("mechanism").Volume);
        Assert.Equal(F(2), Data.Resource("mechanical_spares").Volume);
        Assert.Equal(F(2), Data.Resource("prefab_module").Volume);
        Assert.Equal(Fix64.One, Data.Resource("duranium").Volume);
        Assert.All(s.PartItems, id => Assert.NotNull(Data.Fabrication!.Recipe(id)));
        Assert.Contains("machine_shop", Data.BuildMenu);
        Assert.NotNull(Data.Building("machine_shop").Fabricator);
        Assert.True(Data.Building("machine_shop").Crewed);
        // Repairs no longer list metal.
        Assert.Empty(Data.Maintenance!.CostPerPoint);
        Assert.All(Data.SurfaceUnits.Where(u => u.Repair != null), u => Assert.Empty(u.Repair!.CostPerPoint));
        Assert.All(Data.SurfaceUnits.Where(u => u.Tyres != null), u => Assert.Empty(u.Tyres!.PadCost));
    }

    [Fact]
    public void TheHubFlattensAKitToMaterials()
    {
        // Mechanical Spares = 2 Mechanism (2 dur + 1 tri each) + 1 Structural Frame (4 dur + 1 tri) = 8 dur + 3 tri;
        // shop seconds 15 + 2 x 15 + 20 = 65, x time_factor 2 at the hub.
        var flat = Data.Flattened("mechanical_spares")!.Value;
        Assert.Equal(new[] { new ResourceAmount(R("tritanium"), F(3)), new ResourceAmount(R("duranium"), F(8)) }.OrderBy(a => a.Resource), flat.In);
        Assert.Equal(F(65), flat.Seconds);
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        Assert.True(s.IsFabricator(hub));
        Assert.True(s.IsHubWorkshop(hub));
        Assert.Equal(Data.Fabrication!.Hub.Recipes, s.RecipesAt(hub));
        Assert.Equal(flat.In, s.InputsAt(hub, "mechanical_spares"));
        Assert.Equal(F(130), s.SecondsAt(hub, "mechanical_spares"));
        Assert.Empty(s.InputsAt(hub, "mechanism")); // parts need a Machine Shop
        Assert.Contains("Machine Shop", s.FabricateProblem(0, hub.Id, "mechanism"));
    }

    // ---------------------------------------------------------------- the Machine Shop's queue

    [Fact]
    public void OrdersTakeTheirInputsWhenTheyStartAndOneRunsAtATime()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        Assert.Equal(FabStatus.Idle, s.FabricatorStatus(shop));
        Assert.Equal(StructureStatus.NoInput, shop.Status); // idle: no power drawn
        Assert.Equal(Fix64.Zero, s.PowerOf(hub.Id).Use);
        var stock = sim.Players[0].Stock;
        var duranium = stock[R("duranium")];
        sim.Schedule(sim.Tick, new FabricateCommand(0, shop.Id, "mechanism", 2));
        sim.Step(); // queued and the first started in the same tick (Economy runs after the command)
        Assert.Equal(2, shop.Fabrication.Count);
        Assert.True(shop.Fabrication[0].Started);
        Assert.False(shop.Fabrication[1].Started); // one line
        Assert.InRange((duranium - stock[R("duranium")]).ToDouble(), 1.9, 2.0); // only the first order's 2 duranium (+ a tick of hub trickle)
        Assert.Equal(2, s.Queued(0, "mechanism"));
        Assert.Equal(FabStatus.Working, s.FabricatorStatus(shop));
        Assert.Equal(StructureStatus.Working, shop.Status);
        Assert.Equal(Data.Building("machine_shop").PowerUse, s.PowerOf(hub.Id).Use);

        // 15 s at full speed for the first; the second starts as it finishes.
        StepUntil(sim, () => stock[R("mechanism")] >= Fix64.One, Sec(16));
        Assert.Equal("mechanism", shop.LastFabricated);
        Assert.Single(shop.Fabrication);
        sim.Step();
        Assert.True(shop.Fabrication[0].Started);
        StepUntil(sim, () => shop.Fabrication.Count == 0, Sec(16));
        Assert.Equal(F(2), stock[R("mechanism")]);
        sim.Step();
        Assert.Equal(FabStatus.Idle, s.FabricatorStatus(shop));
        Assert.True(shop.Condition < F(100)); // it wore while it worked
    }

    [Fact]
    public void AnOrderShortOfInputIsSkippedAndCancellingAStartedOneRefundsIt()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        var stock = sim.Players[0].Stock;
        Do(sim, new FabricateCommand(0, shop.Id, "mechanical_spares", 1)); // no mechanisms or frames: skipped
        Do(sim, new FabricateCommand(0, shop.Id, "structural_frame", 1));
        Assert.False(shop.Fabrication[0].Started);
        Assert.Equal("needs 1 Structural Frame (have 0)", s.OrderProblem(shop, shop.Fabrication[0]));
        Assert.True(shop.Fabrication[1].Started);
        Assert.Null(s.OrderProblem(shop, shop.Fabrication[1]));

        var duranium = stock[R("duranium")];
        var tritanium = stock[R("tritanium")];
        Do(sim, new CancelFabricateCommand(0, shop.Id, shop.Fabrication[1].Id));
        Assert.InRange((stock[R("duranium")] - duranium).ToDouble(), 4, 4.1); // the frame's 4 duranium + 1 tritanium back
        Assert.InRange((stock[R("tritanium")] - tritanium).ToDouble(), 1, 1.1);
        Assert.Single(shop.Fabrication);
        Assert.Equal(FabStatus.NoInput, s.FabricatorStatus(shop));
        Assert.Equal(StructureStatus.NoInput, shop.Status);
        Do(sim, new CancelFabricateCommand(0, shop.Id, shop.Fabrication[0].Id)); // unstarted: nothing to give back
        Assert.Empty(shop.Fabrication);
    }

    [Fact]
    public void ThePausedOrBrokenShopStops()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        Do(sim, new FabricateCommand(0, shop.Id, "control_module", 1));
        for (var i = 0; i < Sec(5); i++) sim.Step();
        Do(sim, new PauseCommand(0, shop.Id, true));
        var progress = shop.Fabrication[0].Progress;
        for (var i = 0; i < Sec(5); i++) sim.Step();
        Assert.Equal(progress, shop.Fabrication[0].Progress);
        Assert.Equal(FabStatus.Paused, s.FabricatorStatus(shop));
        Do(sim, new PauseCommand(0, shop.Id, false));
        Do(sim, new DebugStructureCommand(0, shop.Id, DebugStructureField.Broken, Fix64.One));
        progress = shop.Fabrication[0].Progress;
        for (var i = 0; i < Sec(5); i++) sim.Step();
        Assert.Equal(progress, shop.Fabrication[0].Progress);
        Assert.Equal(FabStatus.Broken, s.FabricatorStatus(shop));
    }

    [Fact]
    public void TheHubWorkshopMakesAKitFromMaterialsAtItsOwnSpeed()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        var duranium = stock[R("duranium")];
        var tritanium = stock[R("tritanium")];
        Do(sim, new FabricateCommand(0, hub.Id, "mechanical_spares", 1));
        Assert.True(hub.Fabrication[0].Started);
        Assert.InRange((duranium - stock[R("duranium")]).ToDouble(), 7.9, 8); // flattened inputs, less a tick of trickle
        Assert.InRange((tritanium - stock[R("tritanium")]).ToDouble(), 2.9, 3);
        Assert.Equal(FabStatus.Working, s.FabricatorStatus(hub));
        var start = sim.Tick - 1;
        StepUntil(sim, () => stock[R("mechanical_spares")] >= Fix64.One, Sec(140));
        Assert.InRange(sim.Tick - start, Sec(130) - 2, Sec(130) + 2);
        Assert.InRange(s.QueueSecondsLeft(hub).ToDouble(), 0, 0);
        Assert.Equal(FabStatus.Idle, s.FabricatorStatus(hub));
    }

    // ---------------------------------------------------------------- storage

    [Fact]
    public void PartsTakeStorageByVolumeAndADoneOrderWaitsForRoom()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        var stored = s.Stored(sim, 0);
        stock.Add(R("mechanical_spares"), F(3));
        stock.Add(R("mechanism"), F(4));
        Assert.Equal(stored + F(3 * 2 + 4), s.Stored(sim, 0));

        var shop = Shop(sim, hub);
        Do(sim, new FabricateCommand(0, shop.Id, "mechanical_spares", 1)); // 2 mechanisms + a frame we don't have
        Do(sim, new FabricateCommand(0, shop.Id, "structural_frame", 1));
        StepUntil(sim, () => shop.Fabrication.Count == 1, Sec(25)); // the frame is done...
        Assert.True(shop.Fabrication[0].Started); // ...and the kit started at once with it
        // Fill storage to the brim, then the finished kit (volume 2) has to wait.
        var free = s.FreeStorage(sim, 0);
        stock.Add(R("duranium"), free);
        StepUntil(sim, () => shop.Fabrication[0].Progress >= Fix64.One, Sec(20));
        stock.Add(R("duranium"), s.FreeStorage(sim, 0)); // the hub trickle may have been capped; top it up
        sim.Step();
        Assert.Single(shop.Fabrication);
        Assert.Equal(FabStatus.StorageFull, s.FabricatorStatus(shop));
        Assert.Equal(StructureStatus.StorageFull, shop.Status);
        stock.Add(R("duranium"), -F(5)); // room for one kit
        sim.Step();
        Assert.Empty(shop.Fabrication);
        Assert.Equal(F(4), stock[R("mechanical_spares")]);
    }

    // ---------------------------------------------------------------- keep-in-stock

    [Fact]
    public void DefaultTargetsKeepTwoOfEachKitInUseAtTheHub()
    {
        var sim = Landed(out var hub, targets: true);
        var s = sim.Surface!;
        var t = Data.Fabrication!.DefaultTarget;
        Assert.Equal(t, s.StockTarget(0, "mechanical_spares")); // the crawler uses them
        Assert.Equal(t, s.StockTarget(0, "rover_spares")); // the survey rovers
        Assert.Equal(0, s.StockTarget(0, "electrical_spares")); // nothing of ours uses them yet
        Assert.Equal(0, s.StockTarget(0, "mechanism")); // parts: no default
        for (var i = 0; i < Sec(2); i++) sim.Step();
        Assert.Equal(t, s.Queued(0, "mechanical_spares"));
        Assert.Equal(t, s.Queued(0, "rover_spares"));
        Assert.All(hub.Fabrication, o => Assert.True(o.Auto));
        // A target set by the player wins; lowering it drops the auto orders not yet started.
        Do(sim, new StockTargetCommand(0, "rover_spares", 0));
        Assert.Equal(0, s.StockTarget(0, "rover_spares"));
        Assert.Equal(0, s.Queued(0, "rover_spares") - hub.Fabrication.Count(o => o.Started && o.RecipeId == "rover_spares"));
        Do(sim, new StockTargetCommand(0, "electrical_spares", 3));
        for (var i = 0; i < Sec(2); i++) sim.Step();
        Assert.Equal(3, s.Queued(0, "electrical_spares"));
    }

    [Fact]
    public void AutoOrdersGoToTheMachineShopWithTheMissingPartsInFront()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        Do(sim, new StockTargetCommand(0, "mechanical_spares", 1));
        for (var i = 0; i < Sec(2); i++) sim.Step();
        Assert.Empty(hub.Fabrication); // the shop takes auto orders
        // Its parts in front of it: a frame and 2 mechanisms, then the kit.
        Assert.Equal(new[] { "structural_frame", "mechanism", "mechanism", "mechanical_spares" }, shop.Fabrication.Select(o => o.RecipeId));
        Assert.All(shop.Fabrication, o => Assert.True(o.Auto));
        var stock = sim.Players[0].Stock;
        StepUntil(sim, () => stock[R("mechanical_spares")] >= Fix64.One, Sec(80));
        for (var i = 0; i < Sec(3); i++) sim.Step();
        Assert.Empty(shop.Fabrication); // target met: nothing more
        Assert.Equal(Fix64.Zero, stock[R("mechanism")]);
        Assert.Equal(Fix64.Zero, stock[R("structural_frame")]);
        Assert.Equal(Fix64.Zero, s.KitEtaSeconds(0, "mechanical_spares") ?? Fix64.Zero);
    }

    // ---------------------------------------------------------------- repairs with kits

    [Fact]
    public void ARepairWaitsForKitsQueuesThemAtTheFrontAndFinishesWhenTheyArrive()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        Do(sim, new FabricateCommand(0, shop.Id, "seal_kit", 3)); // something already in the queue
        var tile = FreeTile(sim, hub, "refinery");
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "refinery", tile, 0));
        var refinery = s.StructureAt(tile)!;
        Do(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(40)));
        Assert.Equal(3, s.KitsNeeded(refinery)); // 60 missing / 25, rounded up
        Assert.Equal("mechanical_spares", s.KitOf(refinery));

        Do(sim, new MaintainCommand(0, refinery.Id));
        StepUntil(sim, () => refinery.MaintenanceWaiting, Sec(20));
        Assert.Equal("needs 3 Mechanical Spares (have 0)", s.RepairWaitReason(refinery));
        for (var i = 0; i < Sec(1); i++) sim.Step();
        // The 3 kits go in front of the seal kits that were waiting (after the one already running), parts first.
        var queue = shop.Fabrication.Select(o => o.RecipeId).ToList();
        Assert.Equal("seal_kit", queue[0]);
        Assert.True(shop.Fabrication[0].Started);
        var firstKit = queue.IndexOf("mechanical_spares");
        Assert.True(firstKit < queue.LastIndexOf("seal_kit"), string.Join(",", queue));
        Assert.Equal(3, s.Queued(0, "mechanical_spares"));
        Assert.NotNull(s.KitEtaSeconds(0, "mechanical_spares"));

        var condition = refinery.Condition;
        StepUntil(sim, () => !refinery.MaintenanceWaiting, Sec(400));
        Assert.True(refinery.RepairPaid);
        StepUntil(sim, () => !refinery.MaintenanceOrdered, Sec(20));
        Assert.Equal(F(100), refinery.Condition);
        Assert.True(condition <= F(40));
        Assert.Null(s.RepairWaitReason(refinery));
    }

    // ---------------------------------------------------------------- jury-rig

    [Fact]
    public void AJuryRigNeedsNoKitsButStopsAtTheCapIsSlowerAndWearsFaster()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var j = Data.Fabrication!.JuryRig;
        var tile = FreeTile(sim, hub, "refinery");
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "refinery", tile, 0));
        var refinery = s.StructureAt(tile)!;
        Do(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Broken, Fix64.One));
        Do(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(10)));
        Assert.Null(s.JuryRigProblem(0, refinery.Id));
        Do(sim, new JuryRigCommand(0, refinery.Id));
        Assert.True(refinery.JuryRigOrdered);
        Assert.True(refinery.MaintenanceOrdered);
        Assert.NotNull(s.JuryRigProblem(0, refinery.Id)); // already ordered
        var core = s.StructureAt(hub.Tile)!;
        StepUntil(sim, () => core.CrewPhase == CrewPhase.Working, Sec(20));
        var start = sim.Tick;
        var kits = sim.Players[0].Stock[R("mechanical_spares")];
        StepUntil(sim, () => refinery.JuryRigged, Sec(60));
        var seconds = (sim.Tick - start) / (double)Simulation.TickRate;
        var normal = (j.MaxCondition - F(10)).ToDouble() / Data.Maintenance!.RepairPerSecond.ToDouble();
        Assert.InRange(seconds, normal / j.Speed.ToDouble() - 1, normal / j.Speed.ToDouble() + 1);
        Assert.Equal(j.MaxCondition, refinery.Condition);
        Assert.False(refinery.Broken);
        Assert.False(refinery.MaintenanceOrdered);
        Assert.Equal(kits, sim.Players[0].Stock[R("mechanical_spares")]); // no kits
        Assert.Equal(Data.Building("refinery").WearPerMinute * j.WearMultiplier, s.WearPerMinute(refinery));
        Assert.Contains("only patches", s.JuryRigProblem(0, refinery.Id));

        // A proper repair with kits clears it.
        sim.Players[0].Stock.Add(R("mechanical_spares"), F(5));
        Do(sim, new MaintainCommand(0, refinery.Id));
        StepUntil(sim, () => !refinery.MaintenanceOrdered, Sec(30));
        Assert.False(refinery.JuryRigged);
        Assert.Equal(Data.Building("refinery").WearPerMinute, s.WearPerMinute(refinery));
    }

    [Fact]
    public void AJuryRigOrderCanBeCancelledBeforeTheCrewStart()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var tile = FreeTile(sim, hub, "refinery");
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "refinery", tile, 0));
        var refinery = s.StructureAt(tile)!;
        Do(sim, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(30)));
        sim.Schedule(sim.Tick, new JuryRigCommand(0, refinery.Id));
        sim.Schedule(sim.Tick, new JuryRigCommand(0, refinery.Id)); // again in the same tick: cancelled
        sim.Step();
        Assert.False(refinery.JuryRigOrdered);
        Assert.False(refinery.MaintenanceOrdered);
    }

    [Fact]
    public void AVehicleIsJuryRiggedOnThePadWithoutKits()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var crawler = s.Units.Single(u => u.DefId == "mining_crawler");
        Do(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Integrity, F(20)));
        Do(sim, new DebugUnitCommand(0, crawler.Id, DebugUnitField.Broken, Fix64.One));
        Assert.Equal("park it on a repair pad first (R)", s.JuryRigProblem(crawler));
        Do(sim, new RepairCommand(0, new[] { crawler.Id }));
        StepUntil(sim, () => crawler.RepairWaiting, Sec(120));
        Assert.StartsWith("needs ", s.RepairWaitReason(crawler));
        Assert.Null(s.JuryRigProblem(crawler));
        Do(sim, new JuryRigUnitsCommand(0, new[] { crawler.Id }));
        Assert.True(crawler.JuryRigOrdered);
        Assert.Null(s.RepairWaitReason(crawler));
        StepUntil(sim, () => crawler.JuryRigged, Sec(60));
        Assert.Equal(Data.Fabrication!.JuryRig.MaxCondition, crawler.Integrity);
        Assert.False(crawler.Broken);
        Assert.NotEqual("broken down: repair it at the Colony Hub first", s.MineProblem(0, crawler.Id, crawler.Tile));
        Refused(sim, new JuryRigUnitsCommand(0, new[] { crawler.Id })); // at the cap already (and parked: waiting for kits again)
    }

    // ---------------------------------------------------------------- refusals, determinism

    [Fact]
    public void RefusedCommandsChangeNothing()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var shop = Shop(sim, hub);
        sim.Step();
        var commands = new Command[]
        {
            new FabricateCommand(0, shop.Id, "no_such_item", 1),
            new FabricateCommand(0, shop.Id, "mechanism", 0),
            new FabricateCommand(0, shop.Id, "mechanism", PlanetSurface.MaxFabCount + 1),
            new FabricateCommand(1, shop.Id, "mechanism", 1), // no such player
            new FabricateCommand(0, hub.Id, "mechanism", 1), // the hub makes kits only
            new FabricateCommand(0, 999999, "mechanism", 1),
            new FabricateCommand(0, s.Units[0].Id, "mechanism", 1),
            new CancelFabricateCommand(0, shop.Id, 12345),
            new StockTargetCommand(0, "duranium", 3), // not a part
            new StockTargetCommand(0, "mechanism", -1),
            new StockTargetCommand(0, "mechanism", GameData.MaxStockTarget + 1),
            new JuryRigCommand(0, hub.Id), // a core doesn't wear
            new JuryRigCommand(0, shop.Id), // perfect condition
            new JuryRigUnitsCommand(0, s.Units.Select(u => u.Id).ToList()), // nobody on a pad
        };
        foreach (var c in commands) Refused(sim, c);
        Assert.Empty(shop.Fabrication);
        Assert.Empty(hub.Fabrication);
        Assert.All(s.PartItems, id => Assert.Equal(0, s.StockTarget(0, id))); // still the test's zeros
    }

    [Fact]
    public void ARefusedCommandLeavesTheHashAsItWas()
    {
        Simulation Game()
        {
            var sim = Landed(out var hub);
            Do(sim, new DebugPlaceBuildingCommand(0, 0, "machine_shop", FreeTile(sim, hub, "machine_shop"), 0));
            return sim;
        }
        var a = Game();
        var b = Game();
        var shop = a.Surface!.Structures.Single(x => x.DefId == "machine_shop");
        a.Schedule(a.Tick, new FabricateCommand(0, shop.Id, "no_such_item", 1));
        a.Schedule(a.Tick, new StockTargetCommand(0, "duranium", 3));
        a.Schedule(a.Tick, new CancelFabricateCommand(0, shop.Id, 777));
        for (var i = 0; i < Sec(3); i++)
        {
            a.Step();
            b.Step();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
    }

    [Fact]
    public void FabricationIsDeterministicAndHashed()
    {
        (ulong Hash, int Kits) Run()
        {
            var sim = Landed(out var hub, targets: true);
            var s = sim.Surface!;
            var shop = Shop(sim, hub);
            Do(sim, new FabricateCommand(0, shop.Id, "machinery_set", 1));
            Do(sim, new FabricateCommand(0, shop.Id, "power_coupling", 3));
            Do(sim, new StockTargetCommand(0, "prefab_module", 1));
            for (var i = 0; i < Sec(240); i++) sim.Step();
            return (sim.StateHash(), sim.Players[0].Stock[R("mechanical_spares")].FloorToInt());
        }
        var first = Run();
        Assert.Equal(first, Run());
        Assert.True(first.Kits >= 1);

        // Every piece of fabrication state is in the hash (flipped by reflection: the setters are internal).
        var sim = Landed(out var hub);
        var shop = Shop(sim, hub);
        Do(sim, new FabricateCommand(0, shop.Id, "mechanism", 2));
        var h = sim.StateHash();
        void Flip(object o, string property, object on, object off)
        {
            var p = o.GetType().GetProperty(property)!;
            p.SetValue(o, on);
            Assert.True(h != sim.StateHash(), $"{o.GetType().Name}.{property} is not in the state hash");
            p.SetValue(o, off);
            Assert.Equal(h, sim.StateHash());
        }
        Flip(shop.Fabrication[1], nameof(FabOrder.Started), true, false);
        Flip(shop.Fabrication[1], nameof(FabOrder.Progress), Fix64.Half, Fix64.Zero);
        Flip(shop, nameof(SurfaceStructure.JuryRigged), true, false);
        Flip(shop, nameof(SurfaceStructure.JuryRigOrdered), true, false);
        Flip(shop, nameof(SurfaceStructure.RepairPaid), true, false);
        var crawler = sim.Surface!.Units.Single(u => u.DefId == "mining_crawler");
        Flip(crawler, nameof(SurfaceUnit.JuryRigged), true, false);
        Flip(crawler, nameof(SurfaceUnit.JuryRigOrdered), true, false);
        Flip(crawler, nameof(SurfaceUnit.RepairPaid), true, false);
        var targets = (int[])typeof(SurfacePlayer).GetField("_stockTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sim.Surface.Players[0])!;
        var was = targets[R("mechanism")];
        targets[R("mechanism")] = 4;
        Assert.NotEqual(h, sim.StateHash());
        targets[R("mechanism")] = was;
        Assert.Equal(h, sim.StateHash());
    }
}
