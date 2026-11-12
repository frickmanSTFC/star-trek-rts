using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Building unlocks (owner 2026-11-12: start the first outpost with the bare minimum, the rest through the missions):
/// surfaces.json start_menu, missions' unlock_buildings, PlacementProblem.NotUnlocked, missions off = everything open,
/// the test panel's DebugUnlockCommand, the hash.
/// </summary>
public class UnlockTests
{
    private static GameData Data => TestData.Real;

    private static readonly string[] StartSet = { "fusion_reactor", "mine", "refinery", "storage_container" };

    /// <summary>A landed one-player game on seed 1701 (Tarsis, or Veyra), missions on or off, outpost assembled.</summary>
    private static Simulation Game(bool missions = true, string world = "tarsis", GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701, "federation", null, world);
        if (missions) Assert.Null(sim.Missions.Enable("first_light"));
        TestData.NoStockTargets(sim);
        var map = sim.Surface!.Map;
        sim.Schedule(0, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t))));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static string Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        return sim.LastRefusal ?? "";
    }

    /// <summary>A tile in the hub's range where the building could go but for the unlock (or nothing at all).</summary>
    private static int Spot(Simulation sim, string building) =>
        sim.Surface!.Map.Grid.Within(sim.Surface.Bases(0)[0].Tile, 3)
            .First(t => sim.Surface.CheckPlacement(sim, 0, building, t) is PlacementProblem.None or PlacementProblem.NotUnlocked);

    [Fact]
    public void TheStartMenuIsTheFourBasicsAndPartOfTheBuildMenu()
    {
        Assert.Equal(new[] { "storage_container", "refinery", "mine", "fusion_reactor" }, Data.StartMenu);
        Assert.All(Data.StartMenu, id => Assert.Contains(id, Data.BuildMenu));
        Assert.Empty(Data.Validate());
    }

    [Fact]
    public void ACampaignStartsWithTheStartMenuAndRefusesTheRest()
    {
        var sim = Game();
        var s = sim.Surface!;
        Assert.Equal(StartSet, s.Players[0].UnlockedBuildings);
        Assert.Equal(new[] { "storage_container", "refinery", "mine", "fusion_reactor" }, s.UnlockedMenu(0));
        foreach (var id in Data.BuildMenu.Except(StartSet)) Assert.False(s.BuildingUnlocked(0, id), id);

        // The refusal names the mission that opens it, and changes nothing.
        var tile = Spot(sim, "science_lab");
        Assert.Equal(PlacementProblem.NotUnlocked, s.CheckPlacement(sim, 0, "science_lab", tile));
        Assert.Equal("unlocks with the mission Ground Truth", s.LockedText(sim, 0, "science_lab"));
        Assert.Equal("unlocks when the mission Wings starts", s.LockedText(sim, 0, "machine_shop"));
        Assert.Null(s.LockedText(sim, 0, "refinery"));
        var duranium = sim.Players[0].Stock[Data.Resource("duranium").Index];
        var count = s.Structures.Count;
        Assert.Contains("unlocks with the mission Ground Truth", Refused(sim, new PlaceBuildingCommand(0, "science_lab", tile, 0)));
        Assert.Equal(count, s.Structures.Count);
        Assert.True(sim.Players[0].Stock[Data.Resource("duranium").Index] >= duranium); // nothing paid (the hub's trickle goes on)
        // A start-menu building places as always.
        Do(sim, new PlaceBuildingCommand(0, "refinery", Spot(sim, "refinery"), 0));
        Assert.Equal(count + 1, s.Structures.Count);
    }

    [Fact]
    public void MissionsUnlockWhatTheyNeedAsTheyGo()
    {
        var sim = Game();
        var s = sim.Surface!;
        // Ground Truth's reward: the Research Lab and the Vehicle Bay (with the debrief saying so).
        Do(sim, new DebugMissionCommand(0, 0, "ground_truth", DebugMissionAction.Complete, "", "", -1));
        Assert.True(s.BuildingUnlocked(0, "science_lab"));
        Assert.True(s.BuildingUnlocked(0, "vehicle_bay"));
        Assert.False(s.BuildingUnlocked(0, "machine_shop"));
        Assert.Contains(sim.Missions.Messages(0), m => m.MissionId == "ground_truth" && m.Text.Contains("new buildings: Research Lab, Vehicle Bay"));
        Assert.Contains("unlocks Research Lab, Vehicle Bay", sim.Missions.View(0, "ground_truth")!.Rewards);
        Do(sim, new PlaceBuildingCommand(0, "science_lab", Spot(sim, "science_lab"), 0));

        // Wings opens the Machine Shop and the Drone Pad as it starts (its objectives build them), the Foundry as its reward.
        Do(sim, new DebugMissionCommand(0, 0, "wings", DebugMissionAction.Start, "", "", -1));
        Assert.True(s.BuildingUnlocked(0, "machine_shop"));
        Assert.True(s.BuildingUnlocked(0, "drone_pad"));
        Assert.False(s.BuildingUnlocked(0, "foundry"));
        Do(sim, new DebugMissionCommand(0, 0, "wings", DebugMissionAction.Complete, "", "", -1));
        Assert.True(s.BuildingUnlocked(0, "foundry"));

        // Unlocking twice (a reset mission paid again) adds nothing and says nothing new.
        var before = s.Players[0].UnlockedBuildings!.ToList();
        Do(sim, new DebugMissionCommand(0, 0, "ground_truth", DebugMissionAction.Reset, "", "", -1));
        Do(sim, new DebugMissionCommand(0, 0, "ground_truth", DebugMissionAction.Complete, "", "", -1));
        Assert.Equal(before, s.Players[0].UnlockedBuildings);
    }

    [Fact]
    public void EveryLockedBuildingHasAMissionOfFirstLightThatOpensIt()
    {
        foreach (var id in Data.BuildMenu.Except(Data.StartMenu))
            Assert.True(Data.UnlockMission(id, "first_light") != null, id);
        Assert.Equal("ground_truth", Data.UnlockMission("science_lab", "first_light")!.Id);
        Assert.Equal("beyond_the_ridge", Data.UnlockMission("excavation_site", "first_light")!.Id);
        Assert.Null(Data.UnlockMission("refinery", "first_light"));
    }

    [Fact]
    public void WithMissionsOffEverythingIsOpen()
    {
        var sim = Game(missions: false);
        var s = sim.Surface!;
        Assert.Null(s.Players[0].UnlockedBuildings);
        Assert.All(s.BuildMenu, id => Assert.True(s.BuildingUnlocked(0, id), id));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "science_lab", Spot(sim, "science_lab")));
        Do(sim, new PlaceBuildingCommand(0, "science_lab", Spot(sim, "science_lab"), 0));
    }

    [Fact]
    public void TheDomesKitIsOpenFromTheStartOnADomesWorld()
    {
        var sim = Game(world: "veyra");
        var s = sim.Surface!;
        Assert.True(s.NeedsDomes);
        Assert.True(s.BuildingUnlocked(0, "life_support_plant"));
        Assert.True(s.BuildingUnlocked(0, "pressure_dome"));
        Assert.False(s.BuildingUnlocked(0, "science_lab"));
        Assert.Equal(new[] { "storage_container", "refinery", "mine", "fusion_reactor", "life_support_plant", "pressure_dome" }, s.UnlockedMenu(0));
    }

    [Fact]
    public void TurningMissionsOnLaterKeepsWhatThePlayerHasBuilt()
    {
        var sim = Game(missions: false);
        var s = sim.Surface!;
        Do(sim, new PlaceBuildingCommand(0, "science_lab", Spot(sim, "science_lab"), 0));
        Do(sim, new DebugMissionCommand(0, 0, "first_light", DebugMissionAction.Enable, "", "", -1));
        Assert.Equal(StartSet.Append("science_lab").OrderBy(x => x, StringComparer.Ordinal), s.Players[0].UnlockedBuildings);
        Assert.False(s.BuildingUnlocked(0, "vehicle_bay"));
    }

    [Fact]
    public void ThePlayerAddedLaterIsLockedToo()
    {
        var sim = Game();
        sim.Schedule(sim.Tick, new DebugAddPlayerCommand(0, "cardassian"));
        for (var i = 0; i < 2 * Simulation.TickRate; i++) sim.Step();
        Assert.Equal(StartSet, sim.Surface!.Players[1].UnlockedBuildings);
    }

    [Fact]
    public void TheTestPanelUnlocksAndLocks()
    {
        var sim = Game();
        var s = sim.Surface!;
        var edits = sim.DebugEdits;
        Do(sim, new DebugUnlockCommand(0, 0, "", true));
        Assert.All(s.BuildMenu, id => Assert.True(s.BuildingUnlocked(0, id), id));
        Assert.Equal(edits + 1, sim.DebugEdits);
        Do(sim, new DebugUnlockCommand(0, 0, "", false));
        Assert.Equal(StartSet, s.Players[0].UnlockedBuildings);
        Do(sim, new DebugUnlockCommand(0, 0, "foundry", true));
        Assert.True(s.BuildingUnlocked(0, "foundry"));
        Do(sim, new DebugUnlockCommand(0, 0, "mine", false));
        Assert.False(s.BuildingUnlocked(0, "mine"));
        Assert.Contains("not in the build menu", Refused(sim, new DebugUnlockCommand(0, 0, "warp_core", true)));
        Assert.Contains("no such player", Refused(sim, new DebugUnlockCommand(0, 5, "", true)));
        // A malformed one (null id) is refused before any handler.
        Assert.Contains("no id", Refused(sim, new DebugUnlockCommand(0, 0, null!, true)));

        // Missions off: the panel can still lock the sandbox to the start menu (to try the campaign's menu).
        var off = Game(missions: false);
        Do(off, new DebugUnlockCommand(0, 0, "", false));
        Assert.False(off.Surface!.BuildingUnlocked(0, "science_lab"));
        Assert.Equal("not unlocked yet (a mission opens it)", off.Surface.LockedText(off, 0, "science_lab"));
    }

    [Fact]
    public void UnlocksAreInTheHashAndDeterministic()
    {
        var a = Game();
        var b = Game();
        Assert.Equal(a.StateHash(), b.StateHash());
        var before = a.StateHash();
        a.Schedule(a.Tick, new DebugUnlockCommand(0, 0, "foundry", true));
        b.Schedule(b.Tick, new DebugUnlockCommand(0, 0, "foundry", true));
        a.Step();
        b.Step();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(before, a.StateHash());

        // Only the unlock differs: same everything else, different hash.
        var c = Game();
        var d = Game();
        c.Schedule(c.Tick, new DebugUnlockCommand(0, 0, "foundry", true));
        d.Schedule(d.Tick, new DebugUnlockCommand(0, 0, "drone_pad", true));
        c.Step();
        d.Step();
        Assert.NotEqual(c.StateHash(), d.StateHash());
    }

    [Theory]
    [InlineData("\"start_menu\": [\"storage_container\", \"refinery\", \"mine\", \"fusion_reactor\"]",
        "\"start_menu\": [\"storage_container\", \"refinery\", \"mine\", \"fusion_reactor\", \"shipyard\"]", "shipyard is not in build_menu")]
    [InlineData("\"start_menu\": [\"storage_container\", \"refinery\", \"mine\", \"fusion_reactor\"]",
        "\"start_menu\": [\"storage_container\", \"refinery\", \"mine\", \"mine\", \"fusion_reactor\"]", "mine is listed twice")]
    [InlineData("\"start_menu\": [\"storage_container\", \"refinery\", \"mine\", \"fusion_reactor\"]", "\"start_menu\": []", "empty")]
    public void BadStartMenusAreReported(string from, string to, string problem)
    {
        var data = TestData.Patched(("surfaces/economy.json", from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }
}
