using FinalFrontier.Sim.Data;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// "Can every mission be done?" (GameData.MissionReach.cs, part of Validate()): owner 2026-11-17, after Wings asked for
/// scout drones whose Power Coupling needs Parsteel while the Alloy Foundry was only Wings' reward. The check walks each
/// chain with what is unlocked and granted by then; these tests move the data back to the broken states and expect the
/// plain sentence that names the mission, the objective and the missing thing.
/// </summary>
public class MissionReachTests
{
    private readonly ITestOutputHelper _out;

    public MissionReachTests(ITestOutputHelper output) => _out = output;

    private IReadOnlyList<string> Problems(GameData data)
    {
        var problems = data.MissionReachProblems();
        foreach (var p in problems) _out.WriteLine(p);
        return problems;
    }

    private const string FirstLight = "missions/first_light.json";

    // The data as it was when the owner got stuck: the foundry only as Wings' reward (and no objective to build one).
    private static readonly (string, string, string)[] FoundryAsWingsReward =
    {
        (FirstLight, "\"unlock_buildings\": [\"machine_shop\", \"drone_pad\", \"foundry\"]", "\"unlock_buildings\": [\"machine_shop\", \"drone_pad\"]"),
        (FirstLight, "{ \"id\": \"foundry\", \"text\": \"Build an Alloy Foundry\", \"cond\": { \"building\": { \"id\": \"foundry\" } },",
            "{ \"id\": \"foundry\", \"optional\": true, \"text\": \"Build an Alloy Foundry\", \"cond\": { \"building\": { \"id\": \"foundry\" } },"),
        (FirstLight, "\"rewards\": [ { \"grant\": { \"power_coupling\": 2 } }, { \"research\": { \"points\": 20 } } ],",
            "\"rewards\": [ { \"grant\": { \"power_coupling\": 2 } }, { \"research\": { \"points\": 20 } }, { \"unlock_buildings\": [\"foundry\"] } ],"),
    };

    [Fact]
    public void EveryMissionOfTheRealDataCanBeDone()
    {
        Assert.Empty(Problems(TestData.Real));
        // The campaign's own data set (era layer, Federation) too.
        var campaign = GameData.Load(TestData.Source, new DataSetup(Era: "dawn_of_warp", Factions: new[] { "federation" }));
        Assert.Empty(campaign.Validate());
        Assert.Empty(Problems(campaign));
    }

    [Fact]
    public void TheFoundryAsWingsRewardIsCaught()
    {
        var data = TestData.Patched(FoundryAsWingsReward);
        var problem = Assert.Single(Problems(data));
        Assert.Equal("Mission Wings can't be done (objective \"Fabricate 2 scout drones\"): Scout Drone needs Power Coupling, which needs Parsteel; " +
                     "nothing unlocked by then makes Parsteel: the Alloy Foundry does, but it is not unlocked yet (Wings only opens it as its reward).", problem);
        // It is part of Validate(), so RealDataPassesChecks (and Promote, and the release packer) would stop it.
        Assert.Equal(new[] { problem }, data.Validate());
        Assert.Contains(problem, DataChecks.All(data));
    }

    [Fact]
    public void AnObjectiveToBuildALockedBuildingIsCaught()
    {
        // The same broken data, with the "Build an Alloy Foundry" objective still required.
        var data = TestData.Patched(FoundryAsWingsReward[0], FoundryAsWingsReward[2]);
        var problems = Problems(data);
        Assert.Equal(2, problems.Count);
        Assert.Contains("Mission Wings can't be done (objective \"Build an Alloy Foundry\"): no Alloy Foundry can be built by then: " +
                        "it is not unlocked yet (Wings only opens it as its reward).", problems);
    }

    [Fact]
    public void OneOffGrantsCountOnlyAsFarAsTheyGo()
    {
        // Two power couplings handed over by an earlier reward are enough for the two drones Wings asks for…
        var two = FoundryAsWingsReward.Append((FirstLight, "{ \"grant\": { \"control_module\": 2 } }", "{ \"grant\": { \"control_module\": 2, \"power_coupling\": 2 } }")).ToArray();
        Assert.Empty(Problems(TestData.Patched(two)));
        // …one is not: the second drone's coupling still needs parsteel.
        var one = FoundryAsWingsReward.Append((FirstLight, "{ \"grant\": { \"control_module\": 2 } }", "{ \"grant\": { \"control_module\": 2, \"power_coupling\": 1 } }")).ToArray();
        var problem = Assert.Single(Problems(TestData.Patched(one)));
        Assert.StartsWith("Mission Wings can't be done (objective \"Fabricate 2 scout drones\"): Scout Drone needs Power Coupling, which needs Parsteel;", problem);
        // And a reward only counts once its mission is over: Wings' own two couplings come too late for Wings.
        Assert.Single(Problems(TestData.Patched(FoundryAsWingsReward)));
    }

    [Fact]
    public void ASideJobOfferedBeforeItCanBeDoneIsCaught()
    {
        // Lost Signal needs a drone; offered on the orbital hint alone it could come up in the first minute.
        var data = TestData.Patched(("missions/side_jobs.json",
            "\"trigger\": { \"all\": [ { \"ruin_hint\": { \"bind\": \"ruin\" } }, { \"mission\": { \"id\": \"wings\", \"state\": \"completed\" } } ] },",
            "\"trigger\": { \"ruin_hint\": { \"bind\": \"ruin\" } },"));
        var problem = Assert.Single(Problems(data));
        Assert.Equal("Side job Lost Signal can't be done when it is first offered (from the start, before Ground Truth) (objective \"Fly a drone over the marked area\"): " +
                     "it needs a drone, and no Drone Pad can be built by then: it is not unlocked yet (Wings opens it when it starts).", problem);
    }

    [Theory]
    // A building the mission itself only gives as its reward.
    [InlineData("{ \"building\": { \"id\": \"science_lab\" } }", "no Research Lab can be built by then: it is not unlocked yet (Ground Truth only opens it as its reward).")]
    // A tech with no lab to research it.
    [InlineData("{ \"tech\": { \"id\": \"deep_core_mining\" } }", "Deep-Core Mining can't be researched, no Research Lab can be built by then: it is not unlocked yet (Ground Truth only opens it as its reward).")]
    // More of a resource than the landing brings, with nothing to make or mine it.
    [InlineData("{ \"resource\": { \"res\": \"dilithium\", \"amount\": 60 } }", "60 Dilithium can't be had; nothing unlocked by then makes Dilithium, and nothing in the build menu does. The landing and earlier rewards give only 50 in all.")]
    // A unit whose bay is still locked.
    [InlineData("{ \"unit\": { \"id\": \"construction_rover\" } }", "no Construction Rover: the Vehicle Bay builds it, but it is not unlocked yet (Ground Truth only opens it as its reward).")]
    // An item no open workshop makes.
    [InlineData("{ \"fabricated\": { \"item\": \"structural_frame\", \"count\": 1 } }", "Structural Frame is made at a Machine Shop, but it is not unlocked yet (Wings opens it when it starts).")]
    // More kinds of deposit than the starting worlds have.
    [InlineData("{ \"deposits_found\": { \"count\": 4 } }", "it asks for 4 different resources found, and the starting worlds all have only 3 kinds of deposit.")]
    public void AnObjectiveOutOfReachIsCaught(string cond, string expected)
    {
        var data = TestData.Patched((FirstLight, "\"cond\": { \"deposits_found\": { \"count\": 2 } }", "\"cond\": " + cond));
        var problem = Assert.Single(Problems(data));
        Assert.Equal("Mission Ground Truth can't be done (objective \"Find 2 different resources\"): " + expected, problem);
    }

    [Theory]
    // What the landing brings, the start menu makes or the hub's workshop builds is fine from the first mission on.
    [InlineData("{ \"resource\": { \"res\": \"dilithium\", \"amount\": 50 } }")]
    [InlineData("{ \"resource\": { \"res\": \"deuterium\", \"amount\": 5000 } }")]
    [InlineData("{ \"building\": { \"ids\": [\"science_lab\", \"refinery\"] } }")]
    [InlineData("{ \"unit\": { \"id\": \"mining_crawler\", \"state\": \"deployed\" } }")]
    [InlineData("{ \"fabricated\": { \"item\": \"mechanical_spares\", \"count\": 3 } }")]
    [InlineData("{ \"any\": [ { \"tech\": { \"id\": \"deep_core_mining\" } }, { \"surveyed\": { \"count\": 10 } } ] }")]
    public void AnObjectiveWithinReachPasses(string cond)
    {
        var data = TestData.Patched((FirstLight, "\"cond\": { \"deposits_found\": { \"count\": 2 } }", "\"cond\": " + cond));
        Assert.Empty(Problems(data));
        Assert.Empty(data.Validate());
    }

    [Fact]
    public void ASpaceChainMustAffordItsSatellitesFromTheStartingStock()
    {
        // A ship that never lands has no income: its body satellites come out of the stock it starts with.
        Assert.NotNull(TestData.Real.SystemRules);
        var data = TestData.Patched(("missions/long_survey.json", "\"system_satellites\": { \"count\": 2 }", "\"system_satellites\": { \"count\": 4 }"),
            ("maps", "\"satellite\": { \"cost\": { \"duranium\": 60, \"tritanium\": 30 }, \"max\": 4 }", "\"satellite\": { \"cost\": { \"duranium\": 160, \"tritanium\": 30 }, \"max\": 4 }"));
        var problem = Assert.Single(Problems(data));
        Assert.Contains("Mission chain The Long Survey can't be done", problem);
        Assert.Contains("cost 640 Duranium", problem);
        Assert.Contains("starts with 500", problem);
        // And never more satellites than a player may have.
        var many = TestData.Patched(("missions/long_survey.json", "\"system_satellites\": { \"count\": 2 }", "\"system_satellites\": { \"count\": 5 }"));
        Assert.Contains(Problems(many), p => p.Contains("asks for 5 body satellites, and a player may have 4"));
    }
}
