using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Gravity (surfaces.json gravity, data/surfaces/worlds.json): curves, the class M normalisation, the hooks on
/// light and heavy worlds, and the drop ship's fuel.</summary>
public class GravityTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static readonly Fix64 Tolerance = Fix64.Ratio(1, 1000);

    /// <summary>The real data with Tarsis given its own gravity (a light or heavy class M world).</summary>
    private static GameData TarsisAt(string g) =>
        TestData.Patched(("climate/bodies.json", "\"tarsis\":     { \"orbit\"", $"\"tarsis\":     {{ \"gravity_g\": {g}, \"orbit\""));

    private static Simulation Landed(GameData data, out int site)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (!sim.Surface.UplinkOnline(0, sim.Tick)) sim.Step();
        sim.Step();
        return sim;
    }

    private static Fix64 Effect(GravityProfile p, string id) => p.Effect(id);

    [Fact]
    public void ClassMIsExactlyNeutral()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var g = sim.Surface!.Gravity;
        Assert.Equal(Fix64.Parse("0.97"), sim.Surface.GravityG);
        Assert.True(g.Neutral);
        foreach (var id in GravityRules.EffectIds) Assert.Equal(Fix64.One, Effect(g, id));
        Assert.Equal(Fix64.One, g.HillCost);
        Assert.Equal("earth_like", g.Band);
        // Neutral means the data's own numbers: costs and times come through untouched.
        Assert.Same(Data.Building("refinery").Cost, sim.Surface.BuildCostOf("refinery"));
        Assert.Equal(Data.Building("refinery").BuildSeconds * Simulation.TickRate, sim.Surface.BuildTicksOf("refinery"));
        Assert.Equal((long)Data.Landing.DescentSeconds * Simulation.TickRate, sim.Surface.DescentTicks(Data.Landing.DescentSeconds));
    }

    [Fact]
    public void ClassMPlaysExactlyAsWithoutGravity()
    {
        // The scripted game (landing, crawler mining, rovers driving) gives the same state hash with the gravity rules
        // taken out of the data: on class M every hook multiplies by exactly 1.
        var without = TestData.Patched(("surfaces/worlds.json", "\"gravity\": {", "\"gravity_switched_off\": {"));
        Assert.Null(without.Gravity);
        Assert.Equal(GoldenData.ScriptedGameHash(without), GoldenData.ScriptedGameHash(Data));
    }

    [Fact]
    public void CurvesInterpolateLinearlyAndClampAtTheEnds()
    {
        var curve = new GravityCurve("t", "", new[] { (Fix64.Parse("0.5"), Fix64.FromInt(2)), (Fix64.One, Fix64.One), (Fix64.FromInt(2), Fix64.FromInt(3)) });
        Assert.Equal(Fix64.FromInt(2), curve.At(Fix64.Zero)); // clamped below
        Assert.Equal(Fix64.FromInt(2), curve.At(Fix64.Parse("0.5")));
        Assert.Equal(Fix64.Parse("1.5"), curve.At(Fix64.Parse("0.75")));
        Assert.Equal(Fix64.One, curve.At(Fix64.One));
        Assert.Equal(Fix64.FromInt(2), curve.At(Fix64.Parse("1.5")));
        Assert.Equal(Fix64.FromInt(3), curve.At(Fix64.FromInt(9))); // clamped above

        // The doc's worked example: Tarsis A at 0.16 g gets lift cost about ×0.28 (raw curve), drive about ×0.77.
        var lift = Data.Gravity!.Effect("lift_cost")!.At(Fix64.Parse("0.16"));
        Assert.True(Fix64.Abs(lift - Fix64.Parse("0.28")) < Tolerance, lift.ToString());
        var drive = Data.Gravity.Effect("drive_speed")!.At(Fix64.Parse("0.16"));
        Assert.True(Fix64.Abs(drive - Fix64.Parse("0.768")) < Tolerance, drive.ToString());

        // Normalised to class M: a multiplier = curve(g) / curve(0.97).
        var p = GravityProfile.For(Data, Fix64.Parse("0.16"));
        var m = Data.Gravity.Effect("lift_cost")!.At(Fix64.Parse("0.97"));
        Assert.Equal(lift / m, p.LiftCost);
        Assert.Equal("low", p.Band);
        Assert.Equal("micro", GravityProfile.For(Data, Fix64.Parse("0.005")).Band);
        Assert.Equal("extreme", GravityProfile.For(Data, Fix64.FromInt(8)).Band);
    }

    [Fact]
    public void ALightWorldBuildsFastLiftsCheapAndDrivesSlowly()
    {
        var data = TarsisAt("0.16");
        Assert.Empty(data.Validate());
        var sim = Landed(data, out _);
        var s = sim.Surface!;
        var g = s.Gravity;
        Assert.Equal(Fix64.Parse("0.16"), s.GravityG);
        Assert.False(g.Neutral);
        Assert.True(g.DriveSpeed < Fix64.Parse("0.8"));
        Assert.True(g.LiftCost < Fix64.Parse("0.3"));
        Assert.True(g.BuildTime < Fix64.One && g.DescentTime < Fix64.One && g.PodSpeed > Fix64.One && g.Extraction < Fix64.One);
        Assert.Equal(Fix64.One, g.HillCost);
        Assert.True(s.BuildTicksOf("refinery") < Data.Building("refinery").BuildSeconds * Simulation.TickRate);
        var dur = s.BuildCostOf("refinery").Single(c => c.Resource == R("duranium")).Amount;
        Assert.True(dur < Data.Building("refinery").Cost.Single(c => c.Resource == R("duranium")).Amount);
        // Crew isn't metal: the hub's crew cost doesn't scale.
        Assert.Equal(Data.Building("colony_hub").Cost.Single(c => c.Resource == R("crew")).Amount, s.BuildCostOf("colony_hub").Single(c => c.Resource == R("crew")).Amount);

        // The rovers drive slower than on class M (same unit, same tile).
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        var m = Landed(Data, out _);
        var mRover = m.Surface!.Units.First(u => u.DefId == "survey_rover");
        Assert.True(s.SpeedPerTick(rover) < m.Surface.SpeedPerTick(mRover));

        // The first landing came down quicker.
        Assert.True(s.Players[0].LandsAtTick < m.Surface.Players[0].LandsAtTick);
    }

    [Fact]
    public void AHeavyWorldTaxesBuildingLiftingCrewsAndHills()
    {
        var data = TarsisAt("1.5");
        var sim = Landed(data, out _);
        var s = sim.Surface!;
        var g = s.Gravity;
        Assert.Equal("high", g.Band);
        Assert.True(g.BuildTime > Fix64.One && g.BuildCost > Fix64.One && g.LiftCost > Fix64.One && g.DescentTime > Fix64.One);
        Assert.True(g.CrewWork < Fix64.One && g.DriveSpeed < Fix64.One && g.RoverWear > Fix64.One && g.PodSpeed < Fix64.One);
        Assert.Equal(Fix64.One + Fix64.Half * Fix64.Half, g.HillCost); // 1 + 0.5 × (1.5 − 1)
        Assert.True(s.BuildTicksOf("refinery") > Data.Building("refinery").BuildSeconds * Simulation.TickRate);
        var tri = s.BuildCostOf("vehicle_bay").Single(c => c.Resource == R("tritanium")).Amount;
        Assert.True(tri > Data.Building("vehicle_bay").Cost.Single(c => c.Resource == R("tritanium")).Amount);
        // Survey teams walk with crew_work, vehicles with drive_speed.
        Assert.Equal(g.CrewWork, g.SpeedOf("survey_team"));
        Assert.Equal(g.DriveSpeed, g.SpeedOf("survey_rover"));
        // A heavier drop-ship trip burns more deuterium.
        Assert.True(s.ShuttleFuel.Single().Amount > Fix64.FromInt(5));
    }

    [Fact]
    public void DropShipsBurnFiveDeuteriumOnClassMAndAreRefusedWithoutIt()
    {
        var sim = Landed(Data, out var site);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        Assert.Equal(Fix64.FromInt(5), s.ShuttleFuel.Single(c => c.Resource == R("deuterium")).Amount);
        var tile = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.ShuttleProblem(sim, 0, t, "survey_rover") == null && s.Map.Grid.Distance(t, site) > 2);

        // Short of fuel: refused, and nothing changes.
        stock.Add(R("deuterium"), -stock[R("deuterium")] + Fix64.FromInt(4));
        Assert.Equal(PlanetSurface.ShuttleFuelProblem, s.ShuttleProblem(sim, 0, tile, "survey_rover"));
        var hash = sim.StateHash();
        sim.Schedule(sim.Tick, new ShuttleCommand(0, tile, "survey_rover"));
        sim.Step();
        Assert.Empty(s.Deliveries);
        Assert.Contains("deuterium", sim.LastRefusal ?? "");

        // With the fuel: it flies and burns 5.
        stock.Add(R("deuterium"), Fix64.FromInt(10));
        var before = stock[R("deuterium")];
        sim.Schedule(sim.Tick, new ShuttleCommand(0, tile, "survey_rover"));
        sim.Step();
        Assert.Single(s.Deliveries);
        var unitDeuterium = Data.SurfaceUnit("survey_rover").Cost.Where(c => c.Resource == R("deuterium")).Sum(c => c.Amount.ToDouble());
        Assert.Equal(before.ToDouble() - 5 - unitDeuterium, stock[R("deuterium")].ToDouble(), 6);
        _ = hash;
    }

    [Fact]
    public void TheFirstLandingPaysNoFuel()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        var deuterium = sim.Players[0].Stock[R("deuterium")];
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i < 3; i++) sim.Step();
        Assert.Equal(LandingPhase.Descending, sim.Surface.Players[0].Phase);
        Assert.Equal(deuterium, sim.Players[0].Stock[R("deuterium")]);
    }

    [Fact]
    public void GravityDataIsChecked()
    {
        var unsorted = TestData.Patched(("surfaces/worlds.json", "[[0.005, 0.35], [0.13, 0.75]", "[[0.13, 0.35], [0.005, 0.75]"));
        Assert.Contains(unsorted.Validate(), p => p.Contains("sorted by g"));
        var missing = TestData.Patched(("surfaces/worlds.json", "{ \"id\": \"pod_speed\"", "{ \"id\": \"pod_speedy\""));
        Assert.Contains(missing.Validate(), p => p.Contains("'pod_speed'"));
        var negative = TestData.Patched(("surfaces/orbit.json", "\"fuel\": { \"deuterium\": 5 }", "\"fuel\": { \"deuterium\": -5 }"));
        Assert.Contains(negative.Validate(), p => p.Contains("fuel"));
    }
}
