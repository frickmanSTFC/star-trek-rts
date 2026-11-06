using FinalFrontier.Balance;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// The headless balance runner (tools/BalanceRunner): its scripted player plays the surface economy through commands
/// only, runs are deterministic, and an A/B run picks up a data change.
/// </summary>
public class BalanceRunnerTests
{
    private static string OrderFile(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "balance", "orders", name + ".json");
            if (File.Exists(f)) return f;
        }
        throw new FileNotFoundException(name);
    }

    private static BuildOrder Order(string name) => BuildOrder.Load(OrderFile(name));

    [Fact]
    public void EveryShippedOrderParsesAndNamesRealThings()
    {
        var data = TestData.Real;
        foreach (var name in new[] { "standard_opener", "fast_expand", "tech_rush" })
        {
            var o = Order(name);
            Assert.Equal(name, o.Name);
            Assert.NotEmpty(o.Steps);
            Assert.Contains(data.Factions, f => f.Id == o.Faction);
            foreach (var t in o.Research) Assert.True(data.HasTech(t), $"{name}: unknown tech {t}");
            foreach (var s in o.Steps)
                switch (s.Kind)
                {
                    case StepKind.Build: Assert.Contains(s.Target, data.BuildMenu); break;
                    case StepKind.Upgrade: Assert.Contains(data.Buildings, b => b.Id == s.Target); break;
                    case StepKind.Unit: Assert.Contains(data.SurfaceUnits, u => u.Id == s.Target); break;
                }
        }
    }

    [Fact]
    public void ConditionsReadNamesOperatorsAndNumbers()
    {
        var c = Condition.Parse("duranium >= 200 and minute < 5");
        Assert.Equal(2, c.Terms.Count);
        Assert.True(c.Holds(n => n == "duranium" ? 250 : 3));
        Assert.False(c.Holds(n => n == "duranium" ? 150 : 3));
        Assert.False(c.Holds(n => n == "duranium" ? 250 : 5));
        Assert.Throws<FormatException>(() => Condition.Parse("duranium is lots"));
        Assert.Throws<FormatException>(() => BuildOrder.Parse("""{ "steps": [ { "dance": "now" } ] }"""));
    }

    [Fact]
    public void TheBotLandsSurveysMinesAndBuildsARefineryWithinThreeMinutesOnSeed1701()
    {
        var r = Runner.Run(TestData.Real, Order("standard_opener"), 1701, 4);
        Assert.True(r.LandingTile >= 0);
        Assert.True(r.Milestones["hub_ready"] <= 40);
        Assert.True(r.Milestones.ContainsKey("crawler_mining"), "the crawler never dug");
        Assert.True(r.Milestones["built:storage_container"] <= 120);
        Assert.True(r.Milestones["built:refinery"] <= 180, $"refinery at {r.Milestones.GetValueOrDefault("built:refinery")} s");
        Assert.True(r.Milestones.ContainsKey("first_metal"));
        Assert.True(r.Milestones.ContainsKey("first_pod"));
        Assert.True(r.Series["surveyed_tiles"][^1] > r.Series["surveyed_tiles"][3], "the rovers surveyed nothing");
        Assert.Equal(r.Times.Count, r.Series["stock:duranium"].Count);
        Assert.All(r.Series.Values, s => Assert.Equal(r.Times.Count, s.Count));
    }

    [Fact]
    public void SameDataOrderAndSeedGiveTheSameMetricsAndStateHash()
    {
        var a = Runner.Run(TestData.Real, Order("fast_expand"), 99999, 8);
        var b = Runner.Run(TestData.Real, Order("fast_expand"), 99999, 8);
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(Aggregate.Fingerprint(a), Aggregate.Fingerprint(b));
        var c = Runner.Run(TestData.Real, Order("fast_expand"), 1, 8);
        Assert.NotEqual(a.StateHash, c.StateHash); // another planet
    }

    [Fact]
    public void TheBotOnlyActsThroughCommandsAndTheSimAgreesWithItsLog()
    {
        // Driving the same sim by hand with the bot gives the same hash as the runner: the bot has no back door.
        var data = TestData.Real;
        var order = Order("standard_opener");
        var sim = MatchSetup.CreatePlanetLanding(data, 1701, order.Faction, order.Planet);
        var bot = new Bot(sim, 0, order);
        while (sim.Tick < 3 * Simulation.TicksPerMinute)
        {
            bot.Think();
            sim.Step();
        }
        var r = Runner.Run(data, order, 1701, 3);
        Assert.Equal(r.StateHash, "0x" + sim.StateHash().ToString("x16"));
        Assert.Equal(0, sim.DebugEdits);
        Assert.Contains(sim.Surface!.Structures, s => s.DefId == "refinery" && s.Owner == 0);
        Assert.Equal(LandingPhase.Landed, sim.Surface.Players[0].Phase);
    }

    [Fact]
    public void ABRunsPickUpASlowerRefinery()
    {
        var slow = TestData.Patched(("buildings.json", "\"cycles_per_min\": 20", "\"cycles_per_min\": 5"));
        var order = Order("standard_opener");
        var a = new Variant { Label = "A", Runs = { Runner.Run(TestData.Real, order, 1701, 10) } };
        var b = new Variant { Label = "B", Runs = { Runner.Run(slow, order, 1701, 10) } };
        var keys = Aggregate.MilestoneKeys(new[] { a, b });
        Aggregate.Measure(a, keys);
        Aggregate.Measure(b, keys);
        double Median(Variant v, string key) => v.Measures.Single(m => m.Key == key).Spread.Median ?? 0;
        // A quarter of the refining: less duranium made, and ore piles up waiting for the refinery.
        Assert.True(Median(b, "made:duranium") < Median(a, "made:duranium"), $"duranium income A {Median(a, "made:duranium")} B {Median(b, "made:duranium")}");
        Assert.True(Median(b, "end:duranium_ore") > Median(a, "end:duranium_ore") + 50, $"ore left A {Median(a, "end:duranium_ore")} B {Median(b, "end:duranium_ore")}");
        Assert.NotEqual(a.Runs[0].StateHash, b.Runs[0].StateHash);
    }

    [Fact]
    public void ReportFolderHasHtmlSummaryAndPerRunFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ff-balance-" + Guid.NewGuid().ToString("N"));
        var v = new Variant { Label = "A", Data = "data", Order = "standard_opener", Faction = "federation", Runs = { Runner.Run(TestData.Real, Order("standard_opener"), 1, 2) } };
        Report.Write(dir, new[] { v }, "test", 2, 10);
        var html = File.ReadAllText(Path.Combine(dir, "report.html"));
        Assert.Contains("const R = {", html);
        Assert.DoesNotContain("__DATA__", html);
        Assert.True(File.Exists(Path.Combine(dir, "summary.json")));
        var csv = File.ReadAllLines(Path.Combine(dir, "runs", "A-seed1.csv"));
        Assert.StartsWith("time_s,", csv[0]);
        Assert.Equal(v.Runs[0].Times.Count + 1, csv.Length);
        Directory.Delete(dir, true);
    }
}
