using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Achievements (docs/achievements-contract.md): the data and its checks, every trigger kind the sim checks, the gate
/// (turned off / test mode), determinism and the state hash, and the sim-side cosmetics (rover names, hull numbers).
/// States are reached with patched copies of the data and direct setup, never test-panel commands (they shut the gate).
/// </summary>
public class AchievementTests
{
    private static GameData Real => TestData.Real;
    private static int Sec(int s) => s * Simulation.TickRate;

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks, string what = "timed out")
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), what);
    }

    /// <summary>A planet game on seed 1701 landed next to a passable deposit, the hub assembled.</summary>
    private static Simulation Landed(GameData data, out int site, out int deposit, Action<Simulation>? beforeLanding = null)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        beforeLanding?.Invoke(sim);
        var map = sim.Surface!.Map;
        foreach (var t in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(t, data.Landing.RevealRadius).FirstOrDefault(n =>
                n != t && map.DepositResource[n] >= 0 && map.Terrain(n).Buildable && map.Terrain(n).Passable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, t));
            Run(sim, TestData.ReadyTicks(data) + Simulation.TickRate + 1); // assembled, and one more check
            site = t;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a deposit.");
    }

    private static Simulation Landed(GameData data) => Landed(data, out _, out _);

    private static bool Earned(Simulation sim, string id) => sim.Achievements.Earned(0, id);

    /// <summary>The real data with text replaced in one achievements file.</summary>
    private static GameData Patch(string file, params (string From, string To)[] edits) =>
        TestData.Patched(edits.Select(e => ("achievements/" + file, e.From, e.To)).ToArray());

    // ---------------------------------------------------------------- data

    [Fact]
    public void RealDataHasTheThirtyAchievementsOfTheContract()
    {
        var a = Real.Achievements!;
        Assert.Equal(30, a.List.Count);
        Assert.Equal(8, a.Categories.Count);
        Assert.Equal(new[] { 10, 25, 50 }, a.Tiers.Select(t => t.Points));
        Assert.Equal(30, a.List.Select(x => x.Reward).Distinct().Count());
        Assert.Equal(new[] { "holding_breath", "never_left", "sos_answered", "unlucky" }, a.List.Where(x => x.Hidden).Select(x => x.Id).OrderBy(x => x));
        Assert.Equal("hull_number", a.Cosmetic(a.Find("fleet")!.Reward)!.Kind);
        Assert.Equal("1", a.Cosmetic("hull_01")!.Value);
        Assert.Equal("Kalty's Mileage", a.Find("kaltys_mileage")!.Name);
        Assert.Equal(3, a.Find("kaltys_mileage")!.Trigger.List.Count);
        Assert.Equal((decimal)1.3, a.Find("heavy_world")!.Worlds!.MinGravityG);
        Assert.Contains("achievements", GameData.Documents);
        Assert.Contains(Real.Files, f => f == "achievements/rules.json");
        Assert.Empty(Real.Validate());
    }

    [Theory]
    [InlineData("colony.json", "\"reward\": \"hull_10\"", "\"reward\": \"name_beacon\"", "already first_light's")]
    [InlineData("colony.json", "\"reward\": \"hull_10\"", "\"reward\": \"no_such_cosmetic\"", "unknown reward")]
    [InlineData("colony.json", "\"id\": \"refined_taste\"", "\"id\": \"Refined-Taste\"", "lower_snake")]
    [InlineData("colony.json", "\"id\": \"refined_taste\"", "\"id\": \"first_light\"", "listed twice")]
    [InlineData("colony.json", "\"milestone\": \"first_metal\"", "\"milestone\": \"first_latinum\"", "unknown milestone")]
    [InlineData("colony.json", "\"building\": \"planetary_capital\"", "\"building\": \"space_elevator\"", "unknown building")]
    [InlineData("colony.json", "\"seconds\": 1800", "\"seconds\": 20000", "at most 14400")]
    [InlineData("colony.json", "\"condition\": \"no_power_short\"", "\"condition\": \"always\"", "unknown condition")]
    [InlineData("colony.json", "\"category\": \"colony\", \"tier\": \"gold\"", "\"category\": \"colony\", \"tier\": \"platinum\"", "unknown tier")]
    [InlineData("colony.json", "\"icon\": \"bolt\"", "\"icon\": \"teapot\"", "unknown icon")]
    [InlineData("exploration.json", "\"at_least\": 4", "\"at_least\": 0", "positive")]
    [InlineData("exploration.json", "\"event\": \"orbit_farthest\"", "\"event\": \"orbit_nearest\"", "unknown event")]
    [InlineData("exploration.json", "\"class\": \"class_m\"", "\"class\": \"class_q\"", "unknown planet class")]
    [InlineData("exploration.json", "\"measure\": \"system_satellites\"", "\"measure\": \"moons\"", "unknown count measure")]
    [InlineData("surface.json", "\"at_least\": 0.5", "\"at_least\": 1.5", "at most 1")]
    [InlineData("vehicles.json", "\"survey_rover_mk3\"", "\"hover_tank\"", "unknown unit")]
    [InlineData("hostile.json", "\"dust_storm\"", "\"meteor_shower\"", "unknown weather kind")]
    [InlineData("comms.json", "\"delay_seconds\": 8", "\"delay\": 8", "has no field 'delay'")]
    [InlineData("challenge.json", "\"without\": \"storage_container\"", "\"without\": \"warehouse\"", "unknown building")]
    [InlineData("research.json", "\"kind\": \"count\", \"measure\": \"techs_known\", \"all\": true",
        "\"kind\": \"all_of\", \"rules\": [ { \"kind\": \"any_of\", \"rules\": [ { \"kind\": \"milestone\", \"milestone\": \"uplink\" } ] } ]", "nested")]
    [InlineData("research.json", "\"kind\": \"count\", \"measure\": \"techs_known\", \"all\": true", "\"kind\": \"teleport\"", "unknown trigger kind")]
    public void BadAchievementDataIsReported(string file, string from, string to, string problem)
    {
        var problems = Patch(file, (from, to)).Validate();
        Assert.Contains(problems, p => p.Contains(problem));
    }

    [Fact]
    public void BadCosmeticsAreReported()
    {
        var data = TestData.Patched(
            ("achievements/rules.json", "\"value\": \"#4f7fb8\"", "\"value\": \"blue\""),
            ("achievements/rules.json", "\"value\": \"chrome\"", "\"value\": \"plaid\""),
            ("achievements/rules.json", "\"value\": \"44\"", "\"value\": \"forty-four\""),
            ("achievements/rules.json", "\"kind\": \"dome_tint\"", "\"kind\": \"hat\""));
        var problems = data.Validate();
        Assert.Contains(problems, p => p.Contains("paint_steel_blue") && p.Contains("#rrggbb"));
        Assert.Contains(problems, p => p.Contains("unknown finish 'plaid'"));
        Assert.Contains(problems, p => p.Contains("hull_44") && p.Contains("whole number"));
        Assert.Contains(problems, p => p.Contains("unknown kind 'hat'"));
    }

    // ---------------------------------------------------------------- triggers

    [Fact]
    public void MilestonesUnlockFirstLightWhenTheUplinkComesOnline()
    {
        var sim = Landed(Real);
        Assert.True(Earned(sim, "first_light"));
        var u = sim.Achievements.Unlocks.Single(x => x.Id == "first_light");
        Assert.Equal(0, u.Player);
        Assert.True(u.Tick >= sim.Surface!.Players[0].UplinkTick && u.Tick < sim.Surface.Players[0].UplinkTick + Simulation.TickRate);
        Assert.Equal(u.Tick, sim.Achievements.MilestoneTick(0, "uplink"));
        Assert.NotNull(sim.Achievements.MilestoneTick(0, "built:colony_hub"));
        Assert.Null(sim.Achievements.Gate);
        Assert.Single(sim.Achievements.Unlocks, x => x.Id == "first_light"); // once only
        Run(sim, Sec(5));
        Assert.Single(sim.Achievements.Unlocks, x => x.Id == "first_light");
    }

    [Fact]
    public void MilestonesCanHaveADeadlineAndAWithout()
    {
        // bay_by_eight on the hub (well before 8:30); no_storage on the uplink "without" a storage container (none built) …
        var data = Patch("challenge.json", ("\"milestone\": \"built:vehicle_bay\"", "\"milestone\": \"built:colony_hub\""),
            ("\"milestone\": \"first_metal\", \"without\"", "\"milestone\": \"uplink\", \"without\""));
        var sim = Landed(data);
        Assert.True(Earned(sim, "bay_by_eight"));
        Assert.True(Earned(sim, "no_storage"));
        // … but not past the deadline, or without the very building that was finished.
        var late = Landed(Patch("challenge.json", ("\"milestone\": \"built:vehicle_bay\", \"before_seconds\": 510", "\"milestone\": \"built:colony_hub\", \"before_seconds\": 5"),
            ("\"milestone\": \"first_metal\", \"without\": \"storage_container\"", "\"milestone\": \"uplink\", \"without\": \"colony_hub\"")));
        Assert.False(Earned(late, "bay_by_eight"));
        Assert.False(Earned(late, "no_storage"));
    }

    [Fact]
    public void BuiltCountsFinishedBuildingsAndWorldsFilterTheSurface()
    {
        var data = TestData.Patched(
            ("achievements/colony.json", "\"building\": \"colonial_administration\"", "\"building\": \"colony_hub\""),
            ("achievements/hostile.json", "\"building\": \"colonial_administration\"", "\"building\": \"colony_hub\""));
        var sim = Landed(data);
        Assert.True(Earned(sim, "seat_of_government"));
        Assert.False(Earned(sim, "heavy_world")); // Tarsis is 1 g, not over 1.3
        Assert.False(Earned(sim, "capital_idea"));

        var light = TestData.Patched(
            ("achievements/hostile.json", "\"building\": \"colonial_administration\"", "\"building\": \"colony_hub\""),
            ("achievements/hostile.json", "\"min_gravity_g\": 1.3", "\"max_gravity_g\": 1.2, \"classes\": [\"class_m\"], \"tier\": \"open\""));
        Assert.True(Earned(Landed(light), "heavy_world"));
    }

    [Fact]
    public void CountsAndSharesHaveProgress()
    {
        var sim = Landed(Real);
        var fleet = sim.Achievements.Progress(0, "fleet")!;
        Assert.Equal(new AchievementProgress(3, 10, "vehicles"), fleet);
        var survey = sim.Achievements.Progress(0, "ground_truth")!;
        Assert.Equal("%", survey.Unit);
        Assert.Equal(50, survey.Target);
        Assert.InRange(survey.Current, 0, 5);
        Assert.Equal(new AchievementProgress(0, Real.Techs.Count, "techs"), sim.Achievements.Progress(0, "scholar")); // every surface tech (9 since the ruin techs)
        Assert.Null(sim.Achievements.Progress(0, "uncharted_no_more")); // no star system in a planet-only game
        Assert.Null(sim.Achievements.Progress(0, "first_light")); // no bar
        Assert.Null(sim.Achievements.Progress(0, "hailing_frequencies")); // the view counts it

        var easy = TestData.Patched(("achievements/vehicles.json", "\"measure\": \"units\", \"at_least\": 10", "\"measure\": \"units\", \"at_least\": 3"),
            ("achievements/surface.json", "\"at_least\": 0.5", "\"at_least\": 0.001"));
        var won = Landed(easy);
        Assert.True(Earned(won, "fleet"));
        Assert.True(Earned(won, "ground_truth"));
        Assert.Equal(new AchievementProgress(3, 3, "vehicles"), won.Achievements.Progress(0, "fleet"));
    }

    [Fact]
    public void UnitCountersCountTheHexesOneRoverDrove()
    {
        var data = Patch("vehicles.json", ("\"at_least\": 100", "\"at_least\": 3"));
        var sim = Landed(data, out var site, out _);
        var rover = sim.Surface!.Units.First(u => u.DefId == "survey_rover");
        var map = sim.Surface.Map;
        var goal = map.Grid.Within(site, 6).Where(t => map.Terrain(t).Passable && map.Grid.Distance(site, t) == 5).ToList();
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, goal.First(t => sim.Surface.StructureAt(t) == null)));
        StepUntil(sim, () => sim.Achievements.TilesDriven(rover.Id) >= 3, Sec(120));
        Run(sim, Simulation.TickRate);
        Assert.True(Earned(sim, "kaltys_mileage"));
        Assert.Equal("hexes", sim.Achievements.Progress(0, "kaltys_mileage")!.Unit);
        Assert.Equal(0, sim.Achievements.TilesDriven(sim.Surface.Units.First(u => u.DefId == "mining_crawler").Id)); // not a survey rover
    }

    [Fact]
    public void StreaksNeedTheConditionInARow()
    {
        var data = Patch("colony.json", ("\"seconds\": 1800", "\"seconds\": 6"));
        var sim = Landed(data);
        Assert.False(Earned(sim, "lights_stay_on"));
        var p = sim.Achievements.Progress(0, "lights_stay_on")!;
        Assert.Equal("s", p.Unit);
        Assert.Equal(6, p.Target);
        Run(sim, Sec(7));
        Assert.True(Earned(sim, "lights_stay_on"));
        // Never Left: a from_start streak never holds without a flagship (planet-only games have no star system).
        Assert.Equal(0, sim.Achievements.Progress(0, "never_left")!.Current);
    }

    [Fact]
    public void FromStartStreaksBreakForGoodWhenTheFlagshipLeaves()
    {
        var data = Patch("challenge.json", ("\"seconds\": 1800, \"from_start\": true", "\"seconds\": 4, \"from_start\": true"));
        var home = MatchSetup.CreateSystemGame(data, 1701);
        Run(home, Sec(5));
        Assert.True(home.Achievements.Earned(0, "never_left"));

        var away = MatchSetup.CreateSystemGame(data, 1701);
        var sys = away.System!;
        var other = sys.Bodies.First(b => b.Id != sys.SurfaceBody).Id;
        Do(away, new SetCourseCommand(0, sys.FlagshipOf(0)!.Id, other));
        StepUntil(away, () => sys.FlagshipOf(0)!.AtBody == other, Sec(200));
        Do(away, new SetCourseCommand(0, sys.FlagshipOf(0)!.Id, sys.SurfaceBody!));
        StepUntil(away, () => sys.FlagshipOf(0)!.AtBody == sys.SurfaceBody, Sec(400));
        Run(away, Sec(10));
        Assert.False(away.Achievements.Earned(0, "never_left"));
    }

    [Fact]
    public void SetsCollectTheWeatherSeen()
    {
        // The first two minutes are calm and clear everywhere (climate calm_start_seconds).
        var data = Patch("hostile.json", ("\"contains\": [\"storm\", \"blizzard\", \"dust_storm\"]", "\"contains\": [\"clear\"]"));
        var sim = Landed(data);
        Assert.Contains("clear", sim.Achievements.WeatherSeen(0));
        Assert.True(Earned(sim, "every_kind_of_weather"));
        Assert.Equal(new AchievementProgress(0, 3, "kinds"), Landed(Real).Achievements.Progress(0, "every_kind_of_weather"));
    }

    [Fact]
    public void AllOfAndAnyOfCombineRules()
    {
        var data = TestData.Patched(
            ("achievements/colony.json", "\"trigger\": { \"kind\": \"milestone\", \"milestone\": \"uplink\" }",
                "\"trigger\": { \"kind\": \"all_of\", \"rules\": [ { \"kind\": \"milestone\", \"milestone\": \"uplink\" }, { \"kind\": \"count\", \"measure\": \"units\", \"at_least\": 3 } ] }"),
            ("achievements/colony.json", "\"trigger\": { \"kind\": \"milestone\", \"milestone\": \"first_metal\" }",
                "\"trigger\": { \"kind\": \"any_of\", \"rules\": [ { \"kind\": \"milestone\", \"milestone\": \"first_metal\" }, { \"kind\": \"count\", \"measure\": \"units\", \"at_least\": 30 } ] }"),
            ("achievements/colony.json", "\"trigger\": { \"kind\": \"milestone\", \"milestone\": \"first_parasteel\" }",
                "\"trigger\": { \"kind\": \"any_of\", \"rules\": [ { \"kind\": \"milestone\", \"milestone\": \"landed\" }, { \"kind\": \"count\", \"measure\": \"units\", \"at_least\": 30 } ] }"));
        Assert.Empty(data.Validate());
        var sim = Landed(data);
        Assert.True(Earned(sim, "first_light")); // both hold
        Assert.False(Earned(sim, "refined_taste")); // neither
        Assert.True(Earned(sim, "parasteel_age")); // one does
    }

    [Fact]
    public void EventsDepositFoundAndCrawlerExplosion()
    {
        var real = Landed(Real, out _, out var deposit);
        var big = real.Surface!.Map.DepositStart[deposit] >= Fix64.FromInt(2500);
        var data = TestData.Patched(
            ("achievements/surface.json", "\"amount\": 2500", "\"amount\": 1"),
            ("surfaces/units/mining_crawler.json", "\"chance_per_min\": 0.05", "\"chance_per_min\": 900"),
            ("surfaces/units/mining_crawler.json", "\"name\": \"Minor fault\", \"weight\": 70", "\"name\": \"Minor fault\", \"weight\": 0"),
            ("surfaces/units/mining_crawler.json", "\"name\": \"Major breakdown\", \"weight\": 22", "\"name\": \"Major breakdown\", \"weight\": 0"));
        var sim = Landed(data, out _, out deposit);
        Assert.True(Earned(sim, "motherlode")); // the landing surveyed a deposit
        Assert.False(Earned(sim, "unlucky"));
        var crawler = sim.Surface!.Units.Single(u => u.DefId == "mining_crawler");
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        StepUntil(sim, () => crawler.Broken, Sec(240));
        Run(sim, Simulation.TickRate);
        Assert.True(Earned(sim, "unlucky"));
        Assert.Equal(big, real.Achievements.Earned(0, "motherlode")); // the real threshold: only a deposit of 2500 or more
    }

    [Fact]
    public void EventsTyreChangedInTheFieldAndLimpedHome()
    {
        // Flats on every metre, no other luck; a rover changes its tyre away from the pad.
        var flats = TestData.Regexed("surfaces.json", ("\"hazard\": [0-9.]+", "\"hazard\": 0"), ("\"flat\": [0-9.]+", "\"flat\": 900"),
            ("\"chance_per_min\": 0\\.4,", "\"chance_per_min\": 0,"));
        var sim = Landed(flats, out var site, out _);
        var s = sim.Surface!;
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        var away = s.Map.Grid.Within(site, 4).First(t => s.Map.Terrain(t).Passable && s.Map.Grid.Distance(site, t) == 3 && s.StructureAt(t) == null);
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, away));
        StepUntil(sim, () => rover.FlatTyres != 0, Sec(60));
        Do(sim, new ChangeTyreCommand(0, new[] { rover.Id }));
        StepUntil(sim, () => Earned(sim, "pit_crew"), Sec(80), "the tyre change in the field");

        // Worn out at once while driving: disabled, then limps to the pad.
        var wear = TestData.Regexed("surfaces.json", ("\"hazard\": [0-9.]+", "\"hazard\": 0"), ("\"flat\": [0-9.]+", "\"flat\": 0"),
            ("\"chance_per_min\": 0\\.4,", "\"chance_per_min\": 0,"), ("\"wear\": [0-9.]+", "\"wear\": 9000"));
        var w = Landed(wear, out site, out _);
        s = w.Surface!;
        rover = s.Units.First(u => u.DefId == "survey_rover");
        away = s.Map.Grid.Within(site, 3).First(t => s.Map.Terrain(t).Passable && s.Map.Grid.Distance(site, t) == 2 && s.StructureAt(t) == null);
        Do(w, new MoveUnitsCommand(0, new[] { rover.Id }, away));
        StepUntil(w, () => s.Disabled(rover), Sec(30));
        Assert.False(Earned(w, "limp_home"));
        Do(w, new RepairCommand(0, new[] { rover.Id }));
        StepUntil(w, () => Earned(w, "limp_home"), Sec(300), "limped home");
    }

    [Fact]
    public void SystemEventsFarthestBodyChartedClassRelayedOrdersAndSos()
    {
        // Which class the farthest body is on this seed (the same game is then played with patched data).
        var probe = MatchSetup.CreateSystemGame(Real, 1701);
        var psys = probe.System!;
        var homePos = psys.Body(psys.SurfaceBody)!.Position;
        var farthest = psys.Bodies.Where(b => b.Id != psys.SurfaceBody).OrderBy(b => FixVec2.Distance(b.Position, homePos)).Last();

        var data = TestData.Patched(
            ("achievements/exploration.json", "\"class\": \"class_m\"", $"\"class\": \"{farthest.ClassId}\""),
            ("achievements/comms.json", "\"delay_seconds\": 8", "\"delay_seconds\": 1"),
            ("surfaces/units/mining_crawler.json", "\"chance_per_min\": 0.05", "\"chance_per_min\": 900"));
        var sim = MatchSetup.CreateSystemGame(data, 1701);
        var sys = sim.System!;
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t)
            && s.Map.Grid.Within(t, data.Landing.RevealRadius).Any(n => n != t && s.Map.DepositResource[n] >= 0 && s.Map.Terrain(n).Passable && s.Map.Terrain(n).Buildable));
        var deposit = s.Map.Grid.Within(site, data.Landing.RevealRadius).First(n => n != site && s.Map.DepositResource[n] >= 0 && s.Map.Terrain(n).Passable && s.Map.Terrain(n).Buildable);
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        Run(sim, TestData.ReadyTicks(data) + 2);
        var ship = sys.FlagshipOf(0)!;

        Do(sim, new SetCourseCommand(0, ship.Id, farthest.Id));
        var crawler = s.Units.Single(u => u.DefId == "mining_crawler");
        Do(sim, new MineCommand(0, crawler.Id, deposit)); // relayed with the delay: an order_relayed event when it arrives
        StepUntil(sim, () => ship.AtBody == farthest.Id, Sec(200), "arrived");
        Run(sim, Simulation.TickRate);
        Assert.True(Earned(sim, "long_way_round"));
        Assert.True(Earned(sim, "long_distance"));
        Assert.True(crawler.Broken, "broke down while the flagship was away");
        StepUntil(sim, () => sys.KnowledgeOf(0, farthest.Id) == BodyKnowledge.Charted, Sec(30), "charted");
        Run(sim, Simulation.TickRate);
        Assert.True(Earned(sim, "rare_find"));
        // Back home: too late for the SOS (the trip out and back takes longer than 60 s), so not answered.
        Do(sim, new SetCourseCommand(0, ship.Id, sys.SurfaceBody!));
        StepUntil(sim, () => ship.AtBody == sys.SurfaceBody, Sec(200), "home");
        Run(sim, Simulation.TickRate);
        Assert.False(Earned(sim, "sos_answered"));
    }

    [Fact]
    public void AnSosAnsweredInTimeCounts()
    {
        var data = TestData.Patched(("achievements/comms.json", "\"within_seconds\": 60", "\"within_seconds\": 600"),
            ("surfaces/units/mining_crawler.json", "\"chance_per_min\": 0.05", "\"chance_per_min\": 900"));
        var sim = MatchSetup.CreateSystemGame(data, 1701);
        var sys = sim.System!;
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t)
            && s.Map.Grid.Within(t, data.Landing.RevealRadius).Any(n => n != t && s.Map.DepositResource[n] >= 0 && s.Map.Terrain(n).Passable && s.Map.Terrain(n).Buildable));
        var deposit = s.Map.Grid.Within(site, data.Landing.RevealRadius).First(n => n != site && s.Map.DepositResource[n] >= 0 && s.Map.Terrain(n).Passable && s.Map.Terrain(n).Buildable);
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        Run(sim, TestData.ReadyTicks(data) + 2);
        var ship = sys.FlagshipOf(0)!;
        var homePos = sys.Body(sys.SurfaceBody)!.Position;
        var near = sys.Bodies.Where(b => b.Id != sys.SurfaceBody).OrderBy(b => FixVec2.Distance(b.Position, homePos)).First();
        var crawler = s.Units.Single(u => u.DefId == "mining_crawler");
        Do(sim, new MineCommand(0, crawler.Id, deposit)); // at home: no delay
        Do(sim, new SetCourseCommand(0, ship.Id, near.Id));
        StepUntil(sim, () => crawler.Broken, Sec(200), "breakdown");
        StepUntil(sim, () => ship.AtBody != null, Sec(200), "arrived");
        Assert.NotEqual(sys.SurfaceBody, ship.AtBody);
        Do(sim, new SetCourseCommand(0, ship.Id, sys.SurfaceBody!));
        StepUntil(sim, () => ship.AtBody == sys.SurfaceBody, Sec(200), "home");
        Run(sim, Simulation.TickRate);
        Assert.True(Earned(sim, "sos_answered"));
    }

    [Fact]
    public void HostileWorldEventsUnderGlassAndHoldingBreath()
    {
        // Veyra with a hub that gives no air and a long grace: a Life Support Plant brings it back in time.
        var data = TestData.Patched(("buildings/surface/cores.json", "\"storage\": 1500, \"life_support\": 2,", "\"storage\": 1500, \"life_support\": 0,"),
            ("surfaces/worlds.json", "\"grace_seconds\": 30", "\"grace_seconds\": 600"));
        var sim = MatchSetup.CreatePlanetLanding(data, 1701, "federation", null, "veyra");
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.LandProblem(sim, 0, t) == null
            && s.Map.Grid.Within(t, 4).Count(n => s.Map.Terrain(n).Buildable && s.Map.Grid.Distance(t, n) >= 2) >= 12);
        sim.Schedule(0, new LandColonyCommand(0, site));
        StepUntil(sim, () => s.UplinkOnline(0, sim.Tick), Sec(120));
        sim.Step();
        var hub = s.Bases(0).Single();
        foreach (var r in new[] { "duranium", "tritanium" }) sim.Players[0].Stock.Add(data.Resource(r).Index, Fix64.FromInt(5000));
        foreach (var t in s.Map.Grid.Within(site, 6)) s.Players[0].Surveyed[t] = true;
        int Free(int distance, int from) => s.Map.Grid.Within(from, distance).First(t => s.Map.Grid.Distance(from, t) == distance && s.Map.Terrain(t).Buildable
            && s.StructureAt(t) == null && s.Map.DepositResource[t] < 0 && !s.Units.Any(u => u.Tile == t) && s.Map.Grid.Neighbours(t).All(n => s.StructureAt(n) == null));
        var domeTile = Free(3, site);
        Do(sim, new PlaceBuildingCommand(0, "life_support_plant", Free(2, site), 0));
        Do(sim, new PlaceBuildingCommand(0, "pressure_dome", domeTile, 0));
        Assert.True(hub.AirlessTicks > 0 || s.LifeSupportOf(hub.Id) == Fix64.Zero);
        StepUntil(sim, () => Earned(sim, "holding_breath"), Sec(400), "life support saved");
        StepUntil(sim, () => s.Structures.Any(x => x.DefId == "pressure_dome" && x.State == StructureState.Complete), Sec(400), "dome built");
        var inside = s.Map.Grid.Within(domeTile, 2).First(t => t != domeTile && s.Map.Terrain(t).Buildable && s.StructureAt(t) == null
            && s.Map.DepositResource[t] < 0 && s.CheckPlacement(sim, 0, "prefab_habitat", t) == PlacementProblem.None && s.Map.Grid.Distance(site, t) > 1);
        Do(sim, new PlaceBuildingCommand(0, "prefab_habitat", inside, 0));
        Assert.False(Earned(sim, "under_glass"));
        StepUntil(sim, () => Earned(sim, "under_glass"), Sec(600), "crewed building working under the dome");
    }

    // ---------------------------------------------------------------- gate, determinism, hash

    [Fact]
    public void NothingUnlocksWhenTurnedOffOrInTestMode()
    {
        var off = Landed(Real, out _, out _, sim => sim.AchievementsEnabled = false);
        Assert.Equal("turned off", off.Achievements.Gate);
        Assert.Empty(off.Achievements.Unlocks);
        Assert.Null(off.Achievements.MilestoneTick(0, "uplink"));
        Assert.Empty(off.Achievements.PendingEvents);

        var test = Landed(Real, out _, out _, sim => sim.Schedule(0, new DebugStockCommand(0, 0, new[] { new ResourceAmount(Real.Resource("duranium").Index, Fix64.FromInt(900)) })));
        Assert.True(test.DebugEdits > 0);
        Assert.Equal("test mode", test.Achievements.Gate);
        Assert.Empty(test.Achievements.Unlocks);

        // view_count rules are the view's: the sim never unlocks them.
        var on = Landed(Real);
        Assert.DoesNotContain(on.Achievements.Unlocks, u => u.Id == "hailing_frequencies");
    }

    [Fact]
    public void TheSameGameUnlocksTheSameAtTheSameTicksAndHashes()
    {
        (string, ulong) Play()
        {
            var sim = Landed(Real, out var site, out var deposit);
            var crawler = sim.Surface!.Units.Single(u => u.DefId == "mining_crawler");
            Do(sim, new MineCommand(0, crawler.Id, deposit));
            var rovers = sim.Surface.Units.Where(u => u != crawler).Select(u => u.Id).ToList();
            Do(sim, new MoveUnitsCommand(0, rovers, sim.Surface.Map.Grid.Within(site, 6).Last(t => sim.Surface.Map.Terrain(t).Passable)));
            Run(sim, Sec(90));
            return (string.Join(";", sim.Achievements.Unlocks.Select(u => $"{u.Player}:{u.Id}@{u.Tick}")), sim.StateHash());
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a, b);
        Assert.Contains("first_light", a.Item1);
    }

    [Fact]
    public void TrackerStateIsHashedOnlyOnceThereIsAny()
    {
        // In orbit nothing is tracked yet: on and off hash the same (planet-only games keep their old hashes).
        var on = MatchSetup.CreatePlanetLanding(Real, 1701);
        var off = MatchSetup.CreatePlanetLanding(Real, 1701);
        off.AchievementsEnabled = false;
        Run(on, Sec(10));
        Run(off, Sec(10));
        Assert.Equal(off.StateHash(), on.StateHash());

        // After landing the tracker has milestones and unlocks: hashed.
        var landedOn = Landed(Real);
        var landedOff = Landed(Real, out _, out _, sim => sim.AchievementsEnabled = false);
        Assert.NotEmpty(landedOn.Achievements.Unlocks);
        Assert.NotEqual(landedOff.StateHash(), landedOn.StateHash());
    }

    // ---------------------------------------------------------------- cosmetics

    private static List<SurfaceUnit> LandedUnits(PlayerCosmetics? cosmetics, out Simulation sim)
    {
        sim = Landed(Real, out _, out _, s =>
        {
            if (cosmetics != null) s.SetCosmetics(0, cosmetics);
        });
        return sim.Surface!.Units.Where(u => u.Owner == 0).OrderBy(u => u.Id).ToList();
    }

    [Fact]
    public void CosmeticNamesAndNumbersComeFirstThenThePool()
    {
        var plain = LandedUnits(null, out var plainSim);
        var c = new PlayerCosmetics(new[] { "beacon", " LIMPET ", "BEACON", "" }, new[] { 1, 44, 1 });
        var units = LandedUnits(c, out var sim);
        Assert.Equal(new[] { "BEACON", "LIMPET" }, sim.CosmeticsOf(0).RoverNames);
        Assert.Equal(new[] { 1, 44 }, sim.CosmeticsOf(0).HullNumbers);
        Assert.Equal("BEACON", units[0].Name);
        Assert.Equal("LIMPET", units[1].Name);
        Assert.Equal(plain[0].Name, units[2].Name); // then the pool, from its start
        Assert.Equal("01", units[0].Number);
        Assert.Equal("44", units[1].Number);
        Assert.Equal(plain[0].Number == "44" ? plain[1].Number : plain[0].Number, units[2].Number); // a pool "44" is skipped
        Assert.NotEqual(plainSim.StateHash(), sim.StateHash());

        // A pool name equal to an extra is skipped: give the pool's second name as the extra.
        var skip = LandedUnits(new PlayerCosmetics(new[] { plain[1].Name }, Array.Empty<int>()), out _);
        Assert.Equal(new[] { plain[1].Name, plain[0].Name, plain[2].Name }, skip.Select(u => u.Name));
        Assert.Equal(plain.Select(u => u.Number), skip.Select(u => u.Number)); // numbers untouched
    }

    [Fact]
    public void CosmeticsAreDeterministicHashedAndOnlyBeforeTheStart()
    {
        var c = new PlayerCosmetics(new[] { "SCORCH" }, new[] { 10 });
        LandedUnits(c, out var a);
        LandedUnits(c, out var b);
        Assert.Equal(a.StateHash(), b.StateHash());
        LandedUnits(PlayerCosmetics.None, out var none);
        LandedUnits(null, out var plain);
        Assert.Equal(plain.StateHash(), none.StateHash()); // none = not hashed at all
        LandedUnits(new PlayerCosmetics(new[] { "HOMEBODY" }, new[] { 10 }), out var other);
        Assert.NotEqual(a.StateHash(), other.StateHash());

        // After tick 0 (or for a player that doesn't exist) it is ignored.
        var late = MatchSetup.CreatePlanetLanding(Real, 1701);
        late.Step();
        late.SetCosmetics(0, c);
        late.SetCosmetics(5, c);
        Assert.Empty(late.CosmeticsOf(0).RoverNames);
    }
}
