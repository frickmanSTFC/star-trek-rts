using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Storage, refining, the mining crawler and its cargo pods, salvage and deep-core mining.</summary>
public class MiningTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    /// <summary>Landed next to a buildable deposit; returns the hub tile and that deposit.</summary>
    private static Simulation Landed(out int hub, out int deposit, ulong seed = 1701)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, seed);
        var map = sim.Surface!.Map;
        foreach (var site in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(site, Data.Landing.RevealRadius).FirstOrDefault(t =>
                t != site && map.DepositResource[t] >= 0 && map.Terrain(t).Buildable && map.Terrain(t).Passable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= Data.Landing.DescentSeconds * Simulation.TickRate; i++) sim.Step();
            hub = site;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a buildable deposit.");
    }

    private static int FreeTile(Simulation sim, int hub, params int[] except)
    {
        var map = sim.Surface!.Map;
        return map.Grid.Within(hub, 2).First(t =>
            map.Terrain(t).Buildable && map.DepositResource[t] < 0 && sim.Surface.StructureAt(t) == null && !except.Contains(t)
            && sim.Surface.Units.All(u => u.Tile != t));
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static SurfaceStructure Build(Simulation sim, string building, int tile)
    {
        Do(sim, new PlaceBuildingCommand(0, building, tile, 0));
        var s = sim.Surface!.StructureAt(tile)!;
        Assert.NotNull(s);
        while (s.State != StructureState.Complete) sim.Step();
        sim.Step();
        return s;
    }

    private static SurfaceUnit Crawler(Simulation sim) => sim.Surface!.Units.Single(u => u.DefId == "mining_crawler");

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    /// <summary>Fills storage to the brim with duranium.</summary>
    private static void FillStorage(Simulation sim)
    {
        var free = sim.Surface!.FreeStorage(sim, 0);
        sim.Players[0].Stock.Add(R("duranium"), free);
    }

    [Fact]
    public void LandingBringsOneCrawlerAndASmallVault()
    {
        var sim = Landed(out _, out _);
        Assert.Single(sim.Surface!.Units, u => u.DefId == "mining_crawler");
        Assert.Equal(Data.Building("colony_hub").Storage, sim.Surface.StorageCapacity(0));
        Assert.True(sim.Surface.Stored(sim, 0) > Fix64.Zero);
        Assert.True(sim.Surface.FreeStorage(sim, 0) < Fix64.FromInt(1000), "the vault should fill up fast");
        // Crew and latinum take no space.
        Assert.False(Data.IsStored(R("crew")));
        Assert.False(Data.IsStored(R("latinum")));
        Assert.True(Data.IsStored(R("duranium_ore")));
    }

    [Fact]
    public void StorageContainersAddCapacity()
    {
        var sim = Landed(out var hub, out _);
        var before = sim.Surface!.StorageCapacity(0);
        Build(sim, "storage_container", FreeTile(sim, hub));
        Assert.Equal(before + Data.Building("storage_container").Storage, sim.Surface.StorageCapacity(0));
    }

    [Fact]
    public void FullStorageStopsMinesAndTheHubTrickle()
    {
        var sim = Landed(out _, out var deposit);
        var mine = Build(sim, "mine", deposit);
        FillStorage(sim);
        sim.Step();
        Assert.Equal(StructureStatus.StorageFull, mine.Status);
        var left = sim.Surface!.Map.DepositAmount[deposit];
        var duranium = sim.Players[0].Stock[R("duranium")];
        for (var i = 0; i < Sec(10); i++) sim.Step();
        Assert.Equal(left, sim.Surface.Map.DepositAmount[deposit]);
        Assert.Equal(duranium, sim.Players[0].Stock[R("duranium")]);
        // A full mine draws no power.
        Assert.Equal(Fix64.Zero, sim.Surface.PowerOf(mine.BaseId).Use);
    }

    [Fact]
    public void RefineryTurnsTwoOreIntoOneMetal()
    {
        var sim = Landed(out var hub, out _);
        var refinery = Build(sim, "refinery", FreeTile(sim, hub));
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium_ore"), Fix64.FromInt(200));
        var oreBefore = stock[R("duranium_ore")];
        var metalBefore = stock[R("duranium")];
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();

        var cycles = Data.Building("refinery").CyclesPerMinute.ToDouble();
        var used = (oreBefore - stock[R("duranium_ore")]).ToDouble();
        var made = (stock[R("duranium")] - metalBefore).ToDouble() - Data.Building("colony_hub").OutputPerMinute.Single(o => o.Resource == R("duranium")).Amount.ToDouble();
        Assert.InRange(used, cycles * 2 - 0.1, cycles * 2 + 0.1);
        Assert.InRange(made, cycles - 0.1, cycles + 0.1);
        Assert.Equal(StructureStatus.Working, refinery.Status);
    }

    [Fact]
    public void RefinerySplitsItsWorkBetweenOres()
    {
        var sim = Landed(out var hub, out _);
        Build(sim, "refinery", FreeTile(sim, hub));
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium_ore"), Fix64.FromInt(200));
        stock.Add(R("tritanium_ore"), Fix64.FromInt(200));
        var d = stock[R("duranium_ore")];
        var t = stock[R("tritanium_ore")];
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        var cycles = Data.Building("refinery").CyclesPerMinute.ToDouble();
        Assert.InRange((d - stock[R("duranium_ore")]).ToDouble(), cycles - 0.1, cycles + 0.1);
        Assert.InRange((t - stock[R("tritanium_ore")]).ToDouble(), cycles - 0.1, cycles + 0.1);
    }

    [Fact]
    public void RefineryIdlesWithoutOreAndDrawsNoPower()
    {
        var sim = Landed(out var hub, out _);
        var refinery = Build(sim, "refinery", FreeTile(sim, hub));
        Assert.Equal(StructureStatus.NoInput, refinery.Status);
        Assert.Equal(Fix64.Zero, sim.Surface!.PowerOf(refinery.BaseId).Use);
    }

    [Fact]
    public void FoundryMakesParasteel()
    {
        var sim = Landed(out var hub, out _);
        var a = FreeTile(sim, hub);
        Build(sim, "foundry", a);
        var stock = sim.Players[0].Stock;
        var before = stock[R("parasteel")];
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        Assert.InRange((stock[R("parasteel")] - before).ToDouble(), Data.Building("foundry").CyclesPerMinute.ToDouble() - 0.1, Data.Building("foundry").CyclesPerMinute.ToDouble() + 0.1);
    }

    [Fact]
    public void CrawlerDrivesToADepositUnfurlsAndSendsLoadsHome()
    {
        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        var def = Data.SurfaceUnit("mining_crawler");
        var resource = s.Map.DepositResource[deposit];
        Assert.Null(s.MineProblem(0, crawler.Id, deposit));

        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Mode == UnitMode.Deploying, Sec(120));
        Assert.Equal(deposit, crawler.Tile);
        Assert.True(s.DeployFraction(crawler) < Fix64.One);
        StepUntil(sim, () => crawler.Mode == UnitMode.Deployed, Sec(def.DeploySeconds) + 2);
        Assert.Equal(MiningStatus.Mining, crawler.MiningStatus);

        // The hopper fills, then a pod launches toward the nearest storage (the hub) and arrives later.
        var left = s.Map.DepositAmount[deposit];
        StepUntil(sim, () => s.Pods.Count > 0, Sec(60));
        var pod = s.Pods[0];
        Assert.Equal(hub, pod.ToTile);
        Assert.Equal(resource, pod.Resource);
        Assert.Equal(def.Hopper, pod.Amount);
        Assert.True(pod.ArriveTick - pod.LaunchTick >= Simulation.TickRate);
        Assert.True(left - s.Map.DepositAmount[deposit] >= def.Hopper - Fix64.FromInt(1));

        var before = sim.Players[0].Stock[resource];
        StepUntil(sim, () => !s.Pods.Contains(pod), (int)(pod.ArriveTick - sim.Tick) + 2);
        Assert.Equal(def.Hopper, sim.Players[0].Stock[resource] - before);
    }

    [Fact]
    public void FartherDepotsMeanLongerFlights()
    {
        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => s.Pods.Count > 0, Sec(180));
        var near = s.Pods[0];
        var d = s.Map.Grid.Distance(deposit, hub);
        var speed = Data.SurfaceUnit("mining_crawler").PodSpeed.ToDouble();
        Assert.InRange(near.ArriveTick - near.LaunchTick, (long)System.Math.Max(Simulation.TickRate, d * Simulation.TickRate / speed), (long)(d * Simulation.TickRate / speed) + Simulation.TickRate + 1);
    }

    [Fact]
    public void CrawlerPacksUpBeforeItDrivesOff()
    {
        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Mode == UnitMode.Deployed, Sec(180));

        var target = FreeTile(sim, hub, deposit);
        Do(sim, new MoveUnitsCommand(0, new[] { crawler.Id }, target));
        Assert.Equal(UnitMode.Packing, crawler.Mode);
        for (var i = 0; i < Sec(Data.SurfaceUnit("mining_crawler").DeploySeconds) - 2; i++) sim.Step();
        Assert.Equal(deposit, crawler.Tile); // still folding up
        StepUntil(sim, () => crawler.Tile == target, Sec(180));
        Assert.Equal(UnitMode.Mobile, crawler.Mode);
    }

    [Fact]
    public void CrawlerWaitsWhenStorageIsFull()
    {
        var sim = Landed(out _, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Mode == UnitMode.Deployed, Sec(180));
        FillStorage(sim);
        StepUntil(sim, () => crawler.MiningStatus == MiningStatus.StorageFull, Sec(60));
        var left = s.Map.DepositAmount[deposit];
        for (var i = 0; i < Sec(10); i++) sim.Step();
        Assert.Equal(left, s.Map.DepositAmount[deposit]);
        Assert.Empty(s.Pods);

        // Make room and it carries on.
        sim.Players[0].Stock.TrySpend(new[] { new ResourceAmount(R("duranium"), Fix64.FromInt(200)) });
        StepUntil(sim, () => s.Pods.Count > 0, Sec(5));
    }

    [Fact]
    public void CrawlerFoldsUpWhenTheDepositRunsOut()
    {
        var sim = Landed(out _, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        s.Map.DepositAmount[deposit] = Fix64.FromInt(4);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.MiningStatus == MiningStatus.Depleted, Sec(240));
        Assert.Single(s.Pods);
        Assert.Equal(Fix64.FromInt(4), s.Pods[0].Amount);
        StepUntil(sim, () => crawler.Mode == UnitMode.Mobile, Sec(10));
        Assert.True(s.Map.IsDepleted(deposit));
    }

    [Fact]
    public void CrawlersOnlyMineSurveyedDeposits()
    {
        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = Crawler(sim);
        var empty = FreeTile(sim, hub, deposit);
        Assert.NotNull(s.MineProblem(0, crawler.Id, empty));
        Do(sim, new MineCommand(0, crawler.Id, empty));
        Assert.Empty(crawler.Path);
        Assert.Equal(UnitMode.Mobile, crawler.Mode);

        // Survey rovers can't mine.
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        Assert.NotNull(s.MineProblem(0, rover.Id, deposit));

        // A deposit with a mine on it is taken.
        Build(sim, "mine", deposit);
        Assert.NotNull(s.MineProblem(0, crawler.Id, deposit));
    }

    [Fact]
    public void BuildingsCanNotGoUnderADeployedCrawler()
    {
        var sim = Landed(out _, out var deposit);
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Mode == UnitMode.Deployed, Sec(180));
        Assert.Equal(PlacementProblem.Occupied, sim.Surface!.CheckPlacement(sim, 0, "mine", deposit));
    }

    [Fact]
    public void SalvagingRefundsHalfAndFreesTheTile()
    {
        var sim = Landed(out var hub, out _);
        var tile = FreeTile(sim, hub);
        var container = Build(sim, "storage_container", tile);
        var stock = sim.Players[0].Stock;
        var before = stock[R("duranium")];
        Do(sim, new SalvageCommand(0, container.Id));
        Assert.Null(sim.Surface!.StructureAt(tile));
        var refund = Data.Building("storage_container").Cost.Single().Amount * Data.SalvageRefund;
        Assert.InRange((stock[R("duranium")] - before - refund).ToDouble(), 0, 0.1); // plus a tick of hub trickle

        // The colony core can't be salvaged.
        var core = sim.Surface.StructureAt(hub)!;
        Do(sim, new SalvageCommand(0, core.Id));
        Assert.NotNull(sim.Surface.StructureAt(hub));
    }

    [Fact]
    public void DeepCoreMinesReopenAUsedUpDepositOnceAtLevelTwo()
    {
        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium"), Fix64.FromInt(600));
        stock.Add(R("tritanium"), Fix64.FromInt(300));

        // Mine it out, then salvage the old mine.
        var mine = Build(sim, "mine", deposit);
        s.Map.DepositAmount[deposit] = Fix64.Zero;
        sim.Step();
        Assert.Equal(StructureStatus.Depleted, mine.Status);
        Do(sim, new SalvageCommand(0, mine.Id));

        Assert.Equal(PlacementProblem.NeedsLevel, s.CheckPlacement(sim, 0, "deep_core_mine", deposit));
        var core = s.StructureAt(hub)!;
        Do(sim, new UpgradeCommand(0, core.Id));
        StepUntil(sim, () => core.UpgradingTo == null, Sec(200));
        Assert.Equal(2, Data.Building(core.DefId).Level);
        Assert.Equal(PlacementProblem.NeedsDepletedDeposit, s.CheckPlacement(sim, 0, "deep_core_mine", FreeTile(sim, hub, deposit)));

        Build(sim, "deep_core_mine", deposit);
        Assert.True(s.Map.DepositReopened[deposit]);
        var reopened = s.Map.DepositStart[deposit] * Data.Building("deep_core_mine").ReopenShare;
        Assert.InRange((reopened - s.Map.DepositAmount[deposit]).ToDouble(), 0, 1);

        // Once it runs dry again, it can't be reopened a second time.
        s.Map.DepositAmount[deposit] = Fix64.Zero;
        sim.Step();
        Do(sim, new SalvageCommand(0, s.StructureAt(deposit)!.Id));
        Assert.Equal(PlacementProblem.NeedsDepletedDeposit, s.CheckPlacement(sim, 0, "deep_core_mine", deposit));
    }

    [Fact]
    public void MiningIsDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(out var hub, out var deposit);
            var crawler = Crawler(sim);
            sim.Schedule(sim.Tick + 1, new MineCommand(0, crawler.Id, deposit));
            sim.Schedule(sim.Tick + 2, new PlaceBuildingCommand(0, "refinery", FreeTile(sim, hub, deposit), 0));
            sim.Schedule(sim.Tick + Sec(90), new PackUpCommand(0, new[] { crawler.Id }));
            for (var i = 0; i < Sec(120); i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
