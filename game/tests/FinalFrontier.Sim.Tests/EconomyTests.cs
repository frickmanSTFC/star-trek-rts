using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

public class EconomyTests
{
    private static int R(string id) => TestData.Real.Resource(id).Index;

    [Fact]
    public void PlayersStartWithMapStockAndAHubAtHome()
    {
        var data = TestData.Real;
        var map = data.Map(MatchSetup.MvpMap);
        var sim = MatchSetup.CreateMvpSkirmish(data, 1);

        Assert.Equal(2, sim.Players.Count);
        foreach (var start in map.Starts)
        {
            var player = sim.Players[start.Slot];
            foreach (var a in map.StartStock) Assert.Equal(a.Amount, player.Stock[a.Resource]);

            var home = sim.FindBody(start.HomeBodyId)!;
            var colony = sim.FindColony(home.ColonyId)!;
            Assert.Equal(start.Slot, colony.Owner);
            Assert.Equal(new[] { "colony_hub" }, colony.Buildings.Select(b => b.DefId));
        }
    }

    [Fact]
    public void HubProducesItsOutputPerMinute()
    {
        var data = TestData.Real;
        var sim = MatchSetup.CreateMvpSkirmish(data, 1);
        var hub = data.Building("colony_hub");
        Assert.NotEmpty(hub.OutputPerMinute);

        var before = Enumerable.Range(0, data.Resources.Count).Select(i => sim.Players[0].Stock[i]).ToArray();
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();

        foreach (var r in data.Resources)
        {
            var expected = hub.OutputPerMinute.Where(o => o.Resource == r.Index).Select(o => o.Amount.ToDouble()).Sum();
            var gained = (sim.Players[0].Stock[r.Index] - before[r.Index]).ToDouble();
            Assert.True(System.Math.Abs(gained - expected) < 1e-6, $"{r.Id}: gained {gained}, expected {expected}");
        }
        var hubDuranium = hub.OutputPerMinute.Single(o => o.Resource == R("duranium")).Amount;
        Assert.Equal(hubDuranium, sim.IncomePerMinute(0)[R("duranium")]);
    }

    [Fact]
    public void EconomyIsPartOfTheStateHash()
    {
        var a = MatchSetup.CreateMvpSkirmish(TestData.Real, 1);
        var hashBefore = a.StateHash();
        a.Step(); // nothing moves, but income arrives
        Assert.NotEqual(hashBefore, a.StateHash());
    }

    [Fact]
    public void MvpMapIsPointSymmetric()
    {
        var map = TestData.Real.Map(MatchSetup.MvpMap);
        foreach (var b in map.Bodies)
        {
            var mirror = map.Bodies.SingleOrDefault(o => o.Position == -b.Position);
            Assert.True(mirror != null, $"{b.Id} has no mirror body at {-b.Position}");
            Assert.Equal(b.ClassId, mirror!.ClassId);
            Assert.Equal(b.Size, mirror.Size);
            Assert.Equal(b.Deposits, mirror.Deposits);
            Assert.Equal(b.Parent == null, mirror.Parent == null);
        }
    }

    [Fact]
    public void SpendingIsAllOrNothing()
    {
        var data = TestData.Real;
        var bag = new ResourceBag(data.Resources.Count, new[] { new ResourceAmount(R("duranium"), Fix64.FromInt(100)) });
        var cost = new[] { new ResourceAmount(R("duranium"), Fix64.FromInt(80)), new ResourceAmount(R("crew"), Fix64.FromInt(1)) };
        Assert.False(bag.TrySpend(cost));
        Assert.Equal(Fix64.FromInt(100), bag[R("duranium")]);
        Assert.True(bag.TrySpend(new[] { new ResourceAmount(R("duranium"), Fix64.FromInt(80)) }));
        Assert.Equal(Fix64.FromInt(20), bag[R("duranium")]);
    }
}

public class MapValidationTests
{
    [Fact]
    public void BrokenMapsAreReported()
    {
        var src = GameData.FindDataDirectory(AppContext.BaseDirectory);
        var dir = Directory.CreateTempSubdirectory("ff-data-").FullName;
        try
        {
            foreach (var f in Directory.GetFiles(src, "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var maps = File.ReadAllText(Path.Combine(dir, "maps.json"))
                .Replace("\"deposits\": { \"tritanium\": 2000, \"duranium\": 3000 }", "\"deposits\": { \"deuterium\": 5, \"unobtainium\": 1 }")
                .Replace("{ \"slot\": 1, \"home\": \"morrow\" }", "{ \"slot\": 1, \"home\": \"west_belt\" }");
            File.WriteAllText(Path.Combine(dir, "maps.json"), maps);

            var problems = GameData.Load(dir).Validate();
            Assert.Contains(problems, p => p.Contains("unknown resource 'unobtainium'"));
            Assert.Contains(problems, p => p.Contains("deposit 'deuterium' is not a class_d yield"));
            Assert.Contains(problems, p => p.Contains("home 'west_belt' (asteroid) can't hold a full colony"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
