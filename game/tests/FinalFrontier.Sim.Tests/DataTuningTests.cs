using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

/// <summary>The test panel's DATA tab: finding numbers in the data files, editing them, and handing the new data to a running game.</summary>
public class DataTuningTests
{
    private static GameData Data => TestData.Real;

    private static DataNumber Number(string file, string label) => DataTuning.Numbers(Data).Single(n => n.File == file && n.Label == label);

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    [Fact]
    public void NumbersAreListedWithReadablePaths()
    {
        var all = DataTuning.Numbers(Data);
        Assert.All(GameData.FileNames.Where(f => f != "factions.json"), f => Assert.Contains(all, n => n.File == f));
        var chance = Number("surfaces.json", "units › mining_crawler › breakdown › chance_per_min");
        Assert.Equal(Data.SurfaceUnit("mining_crawler").Breakdown!.ChancePerMinute, Fix64.Parse(chance.Value));
        Assert.Contains("/", chance.Path);
        Assert.False(chance.IsInteger);
        Assert.True(Number("buildings.json", "buildings › refinery › wear_per_min").IsInteger);
        Assert.Equal(chance, DataTuning.Find(Data, chance.File, chance.Path));
    }

    [Fact]
    public void AnEditChangesOnlyThatNumber()
    {
        var n = Number("surfaces.json", "units › mining_crawler › breakdown › chance_per_min");
        var text = Data.Sources["surfaces.json"];
        Assert.Null(DataTuning.SetNumber(text, n.Path, "0.75", out var patched));
        Assert.Equal(text.Length + "0.75".Length - n.Value.Length, patched.Length);
        var at = Enumerable.Range(0, text.Length).First(i => text[i] != patched[i]);
        Assert.Equal(text[..at], patched[..at]); // everything before is untouched
        Assert.Equal(text[(at + n.Value.Length)..], patched[(at + 4)..]); // and everything after
        Assert.Equal("0.75", DataTuning.Numbers("surfaces.json", patched).Single(x => x.Path == n.Path).Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.")]
    [InlineData("--1")]
    [InlineData("")]
    public void BadNumbersAreRefused(string value) =>
        Assert.NotNull(DataTuning.ValueProblem("0.05", value));

    [Fact]
    public void WholeNumbersStayWhole()
    {
        Assert.NotNull(DataTuning.ValueProblem("3", "2.5"));
        Assert.NotNull(DataTuning.ValueProblem("3", "99999999999"));
        Assert.Null(DataTuning.ValueProblem("3", "-12"));
        Assert.Null(DataTuning.ValueProblem("0.05", "7"));
    }

    [Fact]
    public void ARunningGameSwitchesToTheTunedData()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var wear = Number("buildings.json", "buildings › refinery › wear_per_min");
        Assert.Null(sim.DebugProblem(new DebugDataCommand(0, new[] { new DataEdit(wear.File, wear.Path, "45") })));
        Do(sim, new DebugDataCommand(0, new[] { new DataEdit(wear.File, wear.Path, "45") }));
        Assert.Equal(Fix64.FromInt(45), sim.Data.Building("refinery").WearPerMinute);
        Assert.Equal(1, sim.DebugEdits);
        Assert.NotSame(Data, sim.Data);
        Assert.Equal(Fix64.FromInt(3), Data.Building("refinery").WearPerMinute); // the loaded data itself is untouched
        Assert.Empty(sim.Data.Validate());
    }

    [Fact]
    public void BadDataEditsAreRefused()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var wear = Number("buildings.json", "buildings › refinery › wear_per_min");
        var refund = Number("surfaces.json", "salvage_refund");
        var buildSeconds = Number("buildings.json", "buildings › refinery › build_seconds");
        DebugCommand[] bad =
        {
            new DebugDataCommand(0, new[] { new DataEdit(wear.File, wear.Path, "4.5") }), // a whole number
            new DebugDataCommand(0, new[] { new DataEdit(wear.File, "buildings/nope", "1") }), // no such number
            new DebugDataCommand(0, new[] { new DataEdit("hazards.json", "x", "1") }), // not read by the game
            new DebugDataCommand(0, new[] { new DataEdit(refund.File, refund.Path, "2") }), // fails a data check
            new DebugDataCommand(0, new[] { new DataEdit(buildSeconds.File, buildSeconds.Path, "0") }), // fails a data check
            new DebugDataCommand(0, System.Array.Empty<DataEdit>()),
            new DebugReloadDataCommand(0, new[] { new DataFile("surfaces.json", "{ not json") }),
        };
        foreach (var c in bad)
        {
            Assert.NotNull(sim.DebugProblem(c));
            Do(sim, c);
        }
        Assert.Same(Data, sim.Data);
        Assert.Equal(0, sim.DebugEdits);
    }

    [Fact]
    public void ReloadingAFileRefreshesVehicleStats()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= Data.Landing.DescentSeconds * Simulation.TickRate; i++) sim.Step();
        var rover = sim.Surface.Units.First(u => u.DefId == "survey_rover");
        var speed = Number("surfaces.json", "units › survey_rover › speed");
        Assert.Null(DataTuning.SetNumber(Data.Sources["surfaces.json"], speed.Path, "3", out var text));
        var before = rover.SpeedPerTick;
        Do(sim, new DebugReloadDataCommand(0, new[] { new DataFile("surfaces.json", text) }));
        Assert.Equal(Fix64.Parse("3"), sim.Data.SurfaceUnit("survey_rover").Speed);
        Assert.Equal(Fix64.Parse("3") * Simulation.SecondsPerTick, rover.SpeedPerTick);
        Assert.NotEqual(before, rover.SpeedPerTick);

        // Changing the shape of the game (a resource renamed) is refused.
        var renamed = Data.Sources["resources.json"].Replace("\"id\": \"duranium\"", "\"id\": \"duranium_x\"");
        Assert.NotNull(sim.DebugProblem(new DebugReloadDataCommand(0, new[] { new DataFile("resources.json", renamed) })));
    }
}
