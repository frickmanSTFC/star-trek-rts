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
        foreach (var name in new[] { "standard_opener", "fast_expand", "tech_rush", "storage_skip" })
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
            foreach (var s in o.Steps)
                foreach (var d in s.Deposit) Assert.True(data.Resources.Any(r => r.Id == d), $"{name}: unknown deposit {d}");
            foreach (var p in o.Pauses) Assert.Contains(data.Buildings, b => b.Id == p.Building);
            if (o.CrawlerRefuel is { } fuel) Assert.Contains(data.Resources, r => r.Id == fuel.Resource);
        }
    }

    [Fact]
    public void OrdersReadPauseRulesRefuelDepositListsAndOrConditions()
    {
        var o = BuildOrder.Parse("""
            { "crawlers": { "refuel": { "resource": "deuterium", "below": 50, "until": 150 } },
              "pause": [ { "building": "foundry", "when": "duranium < 50 or parasteel >= 40", "resume": "duranium >= 100" } ],
              "steps": [ { "build": "mine", "deposit": ["duranium_ore", "tritanium_ore"] }, { "build": "mine", "deposit": "tritanium_ore" } ] }
            """);
        Assert.Equal(new Refuel("deuterium", 50, 150), o.CrawlerRefuel);
        var rule = Assert.Single(o.Pauses);
        Assert.Equal("foundry", rule.Building);
        Assert.True(rule.When.Holds(n => n == "duranium" ? 20 : 0));
        Assert.True(rule.When.Holds(n => n == "duranium" ? 500 : 45));
        Assert.False(rule.When.Holds(n => n == "duranium" ? 500 : 10));
        Assert.True(rule.Resume!.Holds(_ => 120));
        Assert.Equal(new[] { "duranium_ore", "tritanium_ore" }, o.Steps[0].Deposit);
        Assert.Equal(new[] { "tritanium_ore" }, o.Steps[1].Deposit);
        Assert.Throws<FormatException>(() => BuildOrder.Parse("""{ "pause": [ { "when": "duranium < 5" } ] }"""));
    }

    [Fact]
    public void ThePauseRuleSwitchesTheFoundryOffAndOnThroughCommands()
    {
        // Stock the colony so the foundry comes early; the rule pauses it once parasteel piles up and resumes it after.
        var order = BuildOrder.Parse("""
            { "pause": [ { "building": "foundry", "when": "parasteel >= 3", "resume": "parasteel < 1" } ],
              "steps": [ { "build": "storage_container" }, { "build": "fusion_reactor" }, { "build": "foundry" } ] }
            """, "pause_test");
        var data = TestData.Real;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var bot = new Bot(sim, 0, order);
        var paused = false;
        for (var t = 0; t < Simulation.TicksPerMinute * 6 && !paused; t++)
        {
            bot.Think();
            sim.Step();
            paused = sim.Surface!.Structures.Any(s => s.DefId == "foundry" && s.Paused && s.Status == StructureStatus.Paused);
        }
        Assert.True(paused, string.Join("\n", bot.Log.Select(e => e.Text)));
        Assert.Contains(bot.Log, e => e.Text.StartsWith("pause foundry", StringComparison.Ordinal));
        Assert.Equal(0, sim.DebugEdits);
        // Spend the parasteel: the rule switches it back on.
        sim.Players[0].Stock.Add(data.Resource("parasteel").Index, -sim.Players[0].Stock[data.Resource("parasteel").Index]);
        for (var t = 0; t < 3 * Bot.ThinkTicks; t++)
        {
            bot.Think();
            sim.Step();
        }
        Assert.Contains(sim.Surface!.Structures, s => s.DefId == "foundry" && !s.Paused);
        Assert.Contains(bot.Log, e => e.Text.StartsWith("resume foundry", StringComparison.Ordinal));
    }

    [Fact]
    public void ACrawlerFetchesDeuteriumWhenTheReactorRunsLow()
    {
        // A thirsty reactor (60 deuterium a minute) runs the start stock down in a few minutes.
        var data = TestData.Patched(("buildings/surface/fusion_reactor.json", "\"upkeep_per_min\": { \"deuterium\": 3 }", "\"upkeep_per_min\": { \"deuterium\": 60 }"));
        var order = BuildOrder.Parse("""
            { "crawlers": { "refuel": { "resource": "deuterium", "below": 60, "until": 200 } }, "survey": { "max_distance": 14 },
              "steps": [ { "build": "storage_container" }, { "build": "refinery" }, { "build": "fusion_reactor" } ] }
            """, "refuel_test");
        var r = Runner.Run(data, order, 1, 12);
        Assert.Contains(r.Log, e => e.Text.StartsWith("deuterium low", StringComparison.Ordinal));
        Assert.Contains(r.Log, e => e.Text.Contains(" deuterium at ", StringComparison.Ordinal)); // drove to a deuterium deposit
        Assert.Equal("", r.Crash);
    }

    [Fact]
    public void TargetsJudgeMedianWindowsAndListMissedMilestones()
    {
        var targets = Targets.Load(TargetsFile());
        Assert.Contains("ms:built:refinery", targets.For("standard_opener").Keys);
        Assert.Contains("crashed", targets.For("standard_opener").Keys); // from "*"
        var w = new TargetWindow(1, 2, "");
        Measure M(double? median) => new("ms:x", "x", "Milestones", "time", false, new Spread(median, median, median, median == null ? 0 : 1, 1));
        Assert.Equal("in", Targets.Judge(w, M(90), new double?[] { 90 })!.Verdict); // seconds vs minutes
        Assert.Equal("early", Targets.Judge(w, M(30), new double?[] { 30 })!.Verdict);
        Assert.Equal("late", Targets.Judge(w, M(200), new double?[] { 200 })!.Verdict);
        Assert.Equal("missed", Targets.Judge(w, M(null), new double?[] { null })!.Verdict);
        Assert.Equal("after run", Targets.Judge(new TargetWindow(40, 45, ""), M(null), new double?[] { null }, 30)!.Verdict);
        var level = new Measure("made:duranium", "d", "Income", "", true, new Spread(20, 20, 20, 1, 1));
        Assert.Equal("low", Targets.Judge(new TargetWindow(35, null, ""), level, new double?[] { 20 })!.Verdict);

        // A milestone a target names shows up even when no run reached it: missed, or not judged when its window starts
        // after the run's end.
        var v = new Variant { Label = "A", Order = "standard_opener", Runs = { Runner.Run(TestData.Real, Order("standard_opener"), 1701, 3) } };
        var file = Path.Combine(Path.GetTempPath(), "ff-targets-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, """
            { "orders": { "*": { "crashed": { "max": 0 } }, "standard_opener": { "ms:built:refinery": { "min": 1, "max": 2 },
              "ms:core:colonial_administration": { "min": 20, "max": 30 }, "ms:second_base": { "max": 3 } } } }
            """);
        var mine = Targets.Load(file);
        File.Delete(file);
        var keys = Aggregate.MilestoneKeys(new[] { v }, mine);
        Assert.Contains("core:colonial_administration", keys);
        Assert.Contains("second_base", keys);
        Aggregate.Measure(v, keys, mine);
        string Verdict(string key) => v.Measures.Single(m => m.Key == key).Target!.Verdict;
        Assert.Equal("in", Verdict("ms:built:refinery"));
        Assert.Equal("after run", Verdict("ms:core:colonial_administration"));
        Assert.Equal("missed", Verdict("ms:second_base"));
        Assert.Equal("in", Verdict("crashed"));
        Assert.Null(v.Measures.Single(m => m.Key == "end:duranium").Target);
    }

    private static string TargetsFile()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "balance", "targets.json");
            if (File.Exists(f)) return f;
        }
        throw new FileNotFoundException("balance/targets.json");
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
        sim.AchievementsEnabled = false; // as the runner does
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
        var slow = TestData.Patched(("buildings/surface/refinery.json", "\"cycles_per_min\": 25", "\"cycles_per_min\": 5"));
        var order = Order("standard_opener");
        var a = new Variant { Label = "A", Runs = { Runner.Run(TestData.Real, order, 1701, 10) } };
        var b = new Variant { Label = "B", Runs = { Runner.Run(slow, order, 1701, 10) } };
        var keys = Aggregate.MilestoneKeys(new[] { a, b });
        Aggregate.Measure(a, keys);
        Aggregate.Measure(b, keys);
        double Median(Variant v, string key) => v.Measures.Single(m => m.Key == key).Spread.Median ?? 0;
        // A fifth of the refining: less duranium made, and ore piles up waiting for the refinery.
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
