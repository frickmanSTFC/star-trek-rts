using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Mining crawler breakdowns (minor to explosion) and repair on the Colony Hub's pad.</summary>
public class BreakdownTests
{
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    /// <summary>
    /// The real data with the crawler's breakdown chance set (per minute of digging) and, optionally, only one
    /// severity left in the draw. A chance of 900 breaks it on its first tick of digging.
    /// </summary>
    private static GameData Patched(string chance, string? onlySeverity = null)
    {
        var dir = TestData.CopyData("ff-breakdown-");
        var path = Path.Combine(dir, "surfaces/units/mining_crawler.json");
        var text = File.ReadAllText(path);
        Assert.Contains("\"chance_per_min\": 0.05", text);
        text = text.Replace("\"chance_per_min\": 0.05", $"\"chance_per_min\": {chance}");
        if (onlySeverity != null)
            foreach (var (id, name, weight) in new[] { ("minor", "Minor fault", 70), ("major", "Major breakdown", 22), ("explosion", "Explosion", 8) })
            {
                if (id == onlySeverity) continue;
                var from = $"\"name\": \"{name}\", \"weight\": {weight}";
                Assert.Contains(from, text);
                text = text.Replace(from, $"\"name\": \"{name}\", \"weight\": 0");
            }
        File.WriteAllText(path, text);
        return GameData.Load(dir);
    }

    private static Simulation Landed(GameData data, out int hub, out int deposit)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var map = sim.Surface!.Map;
        foreach (var site in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(site, data.Landing.RevealRadius).FirstOrDefault(t =>
                t != site && map.DepositResource[t] >= 0 && map.Terrain(t).Buildable && map.Terrain(t).Passable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
            hub = site;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a buildable deposit.");
    }

    private static SurfaceUnit Crawler(Simulation sim) => sim.Surface!.Units.Single(u => u.DefId == "mining_crawler");

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

    /// <summary>Sends the crawler to mine and runs until it breaks down.</summary>
    private static SurfaceUnit MineUntilBroken(Simulation sim, int deposit)
    {
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Broken, Sec(240));
        return crawler;
    }

    [Fact]
    public void RealDataMakesBreakdownsRareAndRepairable()
    {
        var def = TestData.Real.SurfaceUnit("mining_crawler");
        Assert.NotNull(def.Breakdown);
        Assert.NotNull(def.Repair);
        Assert.InRange(def.Breakdown!.ChancePerMinute.ToDouble(), 0.001, 0.2);
        var severities = def.Breakdown.Severities.Select(v => v.Id).ToList();
        Assert.Equal(new[] { "minor", "major", "explosion" }, severities);
        // Weighted so most breakdowns are small and explosions are rare.
        Assert.True(def.Breakdown.Severities[0].Weight > def.Breakdown.Severities[2].Weight);
        Assert.Empty(TestData.Real.Validate());
    }

    [Fact]
    public void ABreakdownStopsMiningAndFoldsTheCrawlerUp()
    {
        var data = Patched("900", "minor");
        var sim = Landed(data, out _, out var deposit);
        var crawler = MineUntilBroken(sim, deposit);
        var minor = data.SurfaceUnit("mining_crawler").Breakdown!.Severities[0];

        Assert.Equal(0, crawler.Breakdown);
        Assert.Equal(sim.Tick - 1, crawler.BrokeAtTick);
        Assert.InRange(crawler.Integrity.ToDouble(), 100 - minor.DamageMax, 100 - minor.DamageMin);
        Assert.Equal(UnitMode.Packing, crawler.Mode);
        Assert.NotNull(sim.Surface!.MineProblem(0, crawler.Id, deposit));

        // It won't unfurl again until repaired.
        StepUntil(sim, () => crawler.Mode == UnitMode.Mobile, Sec(10));
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        Assert.Equal(UnitMode.Mobile, crawler.Mode);
    }

    [Fact]
    public void AnExplosionLosesTheLoadButAMinorFaultKeepsIt()
    {
        // About one breakdown every two seconds of digging, so the hopper has something in it first.
        var boom = Patched("30", "explosion");
        var sim = Landed(boom, out _, out var deposit);
        var crawler = Crawler(sim);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        var load = Fix64.Zero;
        StepUntil(sim, () =>
        {
            if (!crawler.Broken) load = crawler.Hopper;
            return crawler.Broken;
        }, Sec(400));
        var explosion = boom.SurfaceUnit("mining_crawler").Breakdown!.Severities[2];
        Assert.InRange(crawler.Integrity.ToDouble(), 100 - explosion.DamageMax, 100 - explosion.DamageMin);
        Assert.Equal(Fix64.Zero, crawler.Hopper);

        var fault = Patched("30", "minor");
        var sim2 = Landed(fault, out _, out var deposit2);
        var crawler2 = Crawler(sim2);
        Do(sim2, new MineCommand(0, crawler2.Id, deposit2));
        var load2 = Fix64.Zero;
        StepUntil(sim2, () =>
        {
            if (!crawler2.Broken) load2 = crawler2.Hopper;
            return crawler2.Broken;
        }, Sec(400));
        Assert.Equal(load2, crawler2.Hopper);
    }

    [Fact]
    public void BrokenCrawlersDriveSlowerTheWorseTheDamage()
    {
        var data = Patched("900", "explosion");
        var sim = Landed(data, out _, out var deposit);
        var crawler = MineUntilBroken(sim, deposit);
        var s = sim.Surface!;
        var rules = data.SurfaceUnit("mining_crawler").Breakdown!;
        var expected = crawler.SpeedPerTick * (rules.SlowestDrive + (Fix64.One - rules.SlowestDrive) * crawler.Integrity / Fix64.FromInt(100));
        Assert.Equal(expected, s.SpeedPerTick(crawler));
        Assert.True(s.SpeedPerTick(crawler) < crawler.SpeedPerTick);
    }

    [Fact]
    public void TheCrewRepairsItOnTheHubPadForSpareKits()
    {
        var data = Patched("900", "major");
        var sim = Landed(data, out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = MineUntilBroken(sim, deposit);
        var repair = data.SurfaceUnit("mining_crawler").Repair!;
        var kit = data.Resource("mechanical_spares").Index;
        sim.Players[0].Stock.Add(kit, Fix64.FromInt(20));

        Do(sim, new RepairCommand(0, new[] { crawler.Id }));
        StepUntil(sim, () => s.IsRepairing(crawler), Sec(240));
        Assert.Equal(hub, crawler.Tile);

        // The crew walk out first: nothing is fixed yet.
        var integrity = crawler.Integrity;
        for (var i = 0; i < Sec(repair.CrewSeconds) - 1; i++) sim.Step();
        Assert.Equal(integrity, crawler.Integrity);
        Assert.True(s.CrewOut(crawler) > Fix64.Zero);

        // Then the crew take the kits (damage / kit_points rounded up, + 2 for a major breakdown) and it heals per_second.
        var stock = sim.Players[0].Stock[kit];
        var kits = (int)System.Math.Ceiling((100 - integrity.ToDouble()) / data.Fabrication!.KitPoints.ToDouble()) + 2;
        Assert.Equal(kits, s.KitsNeeded(crawler));
        Assert.Equal(Fix64.FromInt(kits), s.RepairCost(crawler).Single(c => c.Resource == kit).Amount);
        for (var i = 0; i < Sec(2); i++) sim.Step();
        Assert.Equal(stock - Fix64.FromInt(kits), sim.Players[0].Stock[kit]);
        Assert.Equal(0, s.KitsNeeded(crawler)); // taken: the rest of the repair needs no more
        var healed = (crawler.Integrity - integrity).ToDouble();
        Assert.InRange(healed, repair.PerSecond.ToDouble() * 2 - 0.5, repair.PerSecond.ToDouble() * 2 + 0.5);

        StepUntil(sim, () => !crawler.Broken, Sec(60));
        Assert.Equal(Fix64.FromInt(100), crawler.Integrity);
        Assert.Equal(0, crawler.RepairTicks);
        Assert.Equal(stock - Fix64.FromInt(kits), sim.Players[0].Stock[kit]);
        Assert.Null(s.MineProblem(0, crawler.Id, deposit));
    }

    [Fact]
    public void RepairWaitsForSpareKits()
    {
        var data = Patched("900", "minor");
        var sim = Landed(data, out _, out var deposit);
        var s = sim.Surface!;
        var crawler = MineUntilBroken(sim, deposit);
        Do(sim, new RepairCommand(0, new[] { crawler.Id }));
        StepUntil(sim, () => s.IsRepairing(crawler), Sec(240));

        var kit = data.Resource("mechanical_spares").Index;
        var stock = sim.Players[0].Stock;
        stock.TrySpend(new[] { new ResourceAmount(kit, stock[kit]) });
        // The hub workshop may finish a kit; keep taking them away.
        StepUntil(sim, () =>
        {
            stock.TrySpend(new[] { new ResourceAmount(kit, stock[kit]) });
            return crawler.RepairWaiting;
        }, Sec(10));
        var integrity = crawler.Integrity;
        for (var i = 0; i < Sec(3); i++)
        {
            stock.TrySpend(new[] { new ResourceAmount(kit, stock[kit]) });
            sim.Step();
        }
        Assert.True(crawler.Integrity - integrity < Fix64.FromInt(1));
        Assert.True(crawler.Broken);
        Assert.StartsWith("needs ", s.RepairWaitReason(crawler));
        // The missing kits are queued at the front of the colony core's workshop.
        var core = s.Bases(0).Single();
        Assert.True(s.Queued(0, "mechanical_spares") >= s.KitsNeeded(crawler));
        Assert.Contains(core.Fabrication, o => o.RecipeId == "mechanical_spares" && o.Auto);
    }

    [Fact]
    public void DrivingOffThePadSendsTheCrewBackIn()
    {
        var data = Patched("900", "minor");
        var sim = Landed(data, out var hub, out var deposit);
        var s = sim.Surface!;
        var crawler = MineUntilBroken(sim, deposit);
        Do(sim, new RepairCommand(0, new[] { crawler.Id }));
        StepUntil(sim, () => s.IsRepairing(crawler), Sec(240));
        for (var i = 0; i < Sec(1); i++) sim.Step();
        Assert.True(crawler.RepairTicks > 0);

        Do(sim, new MoveUnitsCommand(0, new[] { crawler.Id }, deposit));
        sim.Step();
        Assert.Equal(0, crawler.RepairTicks);
        Assert.False(s.IsRepairing(crawler));
    }

    [Fact]
    public void RoversAndHealthyCrawlersIgnoreRepairOrders()
    {
        var sim = Landed(TestData.Real, out var hub, out _);
        var ids = sim.Surface!.Units.Select(u => u.Id).ToArray();
        Do(sim, new RepairCommand(0, ids));
        Assert.All(sim.Surface.Units, u => Assert.Empty(u.Path));
    }

    [Fact]
    public void BreakdownsAreDeterministic()
    {
        ulong Run()
        {
            var data = Patched("20");
            var sim = Landed(data, out _, out var deposit);
            var crawler = Crawler(sim);
            sim.Schedule(sim.Tick + 1, new MineCommand(0, crawler.Id, deposit));
            for (var i = 0; i < Sec(60); i++)
            {
                if (crawler.Broken && crawler.Mode == UnitMode.Mobile && crawler.Path.Count == 0 && !sim.Surface!.IsRepairing(crawler))
                    sim.Schedule(sim.Tick, new RepairCommand(0, new[] { crawler.Id }));
                if (!crawler.Broken && crawler.Mode == UnitMode.Mobile && crawler.Path.Count == 0 && crawler.Tile != deposit)
                    sim.Schedule(sim.Tick, new MineCommand(0, crawler.Id, deposit));
                sim.Step();
            }
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
