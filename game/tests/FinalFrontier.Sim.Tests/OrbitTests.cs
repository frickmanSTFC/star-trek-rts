using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Ways down from the ship: outpost drops, the shuttle, transporters (by faction and era) and survey satellites.</summary>
public class OrbitTests
{
    private static GameData Data => TestData.Real;
    private static OrbitRules Orbit => Data.Orbit!;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int s) => s * Simulation.TickRate;

    private static Simulation Landed(out int hub, string faction = "federation", GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701, faction);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = site;
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium"), Fix64.FromInt(1000));
        stock.Add(R("tritanium"), Fix64.FromInt(400));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    [Fact]
    public void EveryGreatPowerHasAShipAndTheHoldIsLoaded()
    {
        foreach (var f in new[] { "federation", "klingon", "romulan", "cardassian", "ferengi", "dominion", "borg" })
            Assert.NotNull(Orbit.ShipFor(f));
        var sim = Landed(out _);
        var hold = sim.Surface!.Players[0].Hold;
        Assert.Equal(Orbit.Hold.Single(a => a.Resource == R("duranium")).Amount, hold[R("duranium")]);
        Assert.Empty(Data.Validate());
    }

    [Fact]
    public void TransporterLevelComesFromTheEraOrTheFaction()
    {
        Assert.Equal(1, Orbit.TransporterLevelFor("federation", "ent"));
        Assert.Equal(2, Orbit.TransporterLevelFor("federation", "tos"));
        Assert.Equal(3, Orbit.TransporterLevelFor("federation", "tng"));
        Assert.Equal(3, Orbit.TransporterLevelFor("borg", "ent")); // the Borg always have them
        Assert.Equal(2, Orbit.TransporterLevelFor("ferengi", "tng"));
        Assert.Equal(3, Landed(out _).Surface!.Players[0].TransporterLevel); // the match is set in the Dominion War era
    }

    [Fact]
    public void AnOutpostDropFoundsANewBaseAwayFromTheFirst()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        Assert.Single(s.Bases(0));
        var near = map.Grid.Within(hub, 3).First(t => map.Safety(t) != LandingSafety.Unsafe && t != hub);
        Assert.Contains("too close", s.OutpostProblem(sim, 0, near) ?? "");

        var far = Enumerable.Range(0, map.Grid.Count).First(t => s.OutpostProblem(sim, 0, t) == null);
        sim.Players[0].Stock.Add(R("crew"), Fix64.FromInt(100));
        var duranium = sim.Players[0].Stock[R("duranium")];
        Do(sim, new OutpostDropCommand(0, far));
        Assert.Single(s.Deliveries);
        Assert.True(duranium - sim.Players[0].Stock[R("duranium")] >= Orbit.OutpostCost.Single(c => c.Resource == R("duranium")).Amount - Fix64.One);
        Assert.Equal("an outpost is already on its way down", s.OutpostProblem(sim, 0, far));
        for (var i = 0; i < Sec(Orbit.OutpostDescentSeconds) + 1; i++) sim.Step();
        Assert.Empty(s.Deliveries);
        Assert.Equal(2, s.Bases(0).Count);
        Assert.Equal(far, s.Bases(0)[1].Tile);
        Assert.True(s.Players[0].Surveyed[far]);
    }

    [Fact]
    public void TheShuttleFliesAUnitDownAndIsAwayForTheRoundTrip()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        var spot = Enumerable.Range(0, map.Grid.Count).First(t => map.Grid.Distance(t, hub) > 20 && s.ShuttleProblem(sim, 0, t, "survey_rover") == null);
        var units = s.Units.Count;
        Do(sim, new ShuttleCommand(0, spot, "survey_rover"));
        Assert.Equal("the drop ship is still away", s.ShuttleProblem(sim, 0, spot, "survey_rover"));
        var delivery = s.Deliveries.Single(d => d.Kind == DeliveryKind.Shuttle);
        Assert.All(s.Units, u => Assert.Equal(0, u.DeliveryId)); // the landing party came with the colony pod
        for (var i = 0; i < Sec(Orbit.ShuttleSeconds) + 1; i++) sim.Step();
        Assert.Equal(units + 1, s.Units.Count);
        Assert.Equal(spot, s.Units[^1].Tile);
        Assert.Equal(delivery.Id, s.Units[^1].DeliveryId); // the view drives it down its drop ship's ramp
        Assert.True(s.Players[0].Surveyed[spot]);
        Assert.NotNull(s.ShuttleProblem(sim, 0, spot, "survey_rover")); // still flying back up
        for (var i = 0; i < Sec(Orbit.ShuttleSeconds) + 1; i++) sim.Step();
        Assert.Null(s.ShuttleProblem(sim, 0, spot, "survey_rover"));
        Assert.Equal("the drop ship can't carry that", s.ShuttleProblem(sim, 0, spot, "terraforming_rover"));
    }

    [Fact]
    public void ShuttlesAndDropsKeepOffToxicGroundPredatorsAndNativeLand()
    {
        var sim = Landed(out _);
        var s = sim.Surface!;
        var map = s.Map;
        var toxic = Enumerable.Range(0, map.Grid.Count).First(t => map.Toxic[t]);
        Assert.NotNull(s.ShuttleProblem(sim, 0, toxic, "survey_rover"));
        Assert.NotNull(s.OutpostProblem(sim, 0, toxic));
        if (map.NativeTile >= 0)
        {
            Assert.Equal("that's the natives' land", s.ShuttleProblem(sim, 0, map.NativeTile, "survey_rover"));
            Assert.Equal("that's the natives' land", s.BeamProblem(sim, 0, map.NativeTile, "survey_team"));
        }
    }

    [Fact]
    public void TheTransporterBeamsATeamWithinRangeThenCycles()
    {
        var sim = Landed(out var hub, data: TestData.Patched(("surfaces.json", "\"dominion_war\": 3,", "\"dominion_war\": 2,")));
        var s = sim.Surface!;
        var map = s.Map;
        var level = sim.Data.Orbit!.Transporter(s.Players[0].TransporterLevel)!;
        Assert.Equal(2, level.Level);
        var far = Enumerable.Range(0, map.Grid.Count).First(t => map.Terrain(t).Passable && !map.NearNatives(t) && map.Grid.Distance(t, hub) > 40);
        Assert.Contains("out of range", s.BeamProblem(sim, 0, far, "survey_team"));
        var near = map.Grid.Within(hub, level.Range).First(t => map.Grid.Distance(t, hub) == level.Range - 1 && map.Terrain(t).Passable && s.StructureAt(t) == null && !map.NearNatives(t));
        var crew = sim.Players[0].Stock[R("crew")];
        Do(sim, new BeamCommand(0, near, "survey_team"));
        Assert.Equal("the transporter is cycling", s.BeamProblem(sim, 0, near, "survey_team"));
        for (var i = 0; i < Sec(3); i++) sim.Step();
        Assert.Equal("survey_team", s.Units[^1].DefId);
        Assert.NotEqual(0, s.Units[^1].DeliveryId);
        Assert.True(sim.Players[0].Stock[R("crew")] < crew); // the team came from the crew pool
    }

    [Fact]
    public void SuppliesBeamFromTheHoldIntoABase()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        var outside = Enumerable.Range(0, map.Grid.Count).First(t => s.BaseFor(0, t) == 0 && map.Terrain(t).Passable && !map.NearNatives(t));
        Assert.Equal("beam supplies into one of your bases", s.BeamProblem(sim, 0, outside, OrbitRules.Supplies));
        // Make room in storage so the load isn't capped by it.
        sim.Players[0].Stock.TrySpend(new[] { new ResourceAmount(R("duranium"), Fix64.FromInt(1000)) });
        var load = s.SuppliesLoad(sim, 0);
        Assert.NotEmpty(load);
        var first = load[0].Resource; // the hold empties in resource order
        var holdBefore = s.Players[0].Hold[first];
        var stockBefore = sim.Players[0].Stock[first];
        Do(sim, new BeamCommand(0, hub, OrbitRules.Supplies));
        Assert.Equal(holdBefore - load[0].Amount, s.Players[0].Hold[first]);
        for (var i = 0; i < Sec(3); i++) sim.Step();
        Assert.True(sim.Players[0].Stock[first] - stockBefore >= load[0].Amount - Fix64.One);
        var total = load.Aggregate(Fix64.Zero, (a, c) => a + c.Amount);
        Assert.True(total <= Orbit.Transporter(3)!.Cargo);
    }

    [Fact]
    public void SatellitesSweepTheirBandAndCanGoUpBeforeLanding()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var row = g.Height / 2;
        Assert.Null(s.SatelliteProblem(sim, 0, row)); // allowed from orbit, before landing
        Do(sim, new SatelliteCommand(0, row));
        Assert.Single(s.Satellites);
        for (var i = 0; i < Sec(Orbit.SatelliteSweepSeconds / 2); i++) sim.Step();
        var half = Orbit.SatelliteBandRows / 2;
        // Half the band is surveyed after half a sweep; rows outside the band aren't.
        Assert.True(s.Players[0].Surveyed[g.Index(g.Width / 4, row + half)]);
        Assert.False(s.Players[0].Surveyed[g.Index(g.Width * 3 / 4, row)]);
        Assert.False(s.Players[0].Surveyed[g.Index(g.Width / 4, row + half + 1)]);
        for (var i = 0; i < Sec(Orbit.SatelliteSweepSeconds / 2) + 2; i++) sim.Step();
        Assert.True(s.Players[0].Surveyed[g.Index(g.Width - 1, row - half)]);

        for (var i = 1; i < Orbit.SatelliteMax; i++) Do(sim, new SatelliteCommand(0, i * 5));
        Assert.Contains("satellites are up", s.SatelliteProblem(sim, 0, 3));
    }

    [Fact]
    public void DeploymentsAreDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(out var hub);
            var s = sim.Surface!;
            var map = s.Map;
            var spot = Enumerable.Range(0, map.Grid.Count).First(t => s.ShuttleProblem(sim, 0, t, "survey_rover") == null && map.Grid.Distance(t, hub) > 15);
            sim.Schedule(sim.Tick + 1, new ShuttleCommand(0, spot, "survey_rover"));
            sim.Schedule(sim.Tick + 2, new SatelliteCommand(0, 30));
            sim.Schedule(sim.Tick + 3, new BeamCommand(0, hub, OrbitRules.Supplies));
            for (var i = 0; i < Sec(40); i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
