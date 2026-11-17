using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// The campaign's ship pick (docs/ship-pick-contract.md): data/missions/ship_choice.json, the picked flagship (class,
/// name, speed, start orbit), the colonizers' start bonus, no landing for the others, and the four space chains played to
/// the end through commands only (twice: the same hash sequence).
/// </summary>
public class ShipPickTests
{
    private readonly ITestOutputHelper _out;

    public ShipPickTests(ITestOutputHelper output) => _out = output;

    private static GameData Real => TestData.Real;

    /// <summary>The campaign's data: Dawn of Warp, Federation (no Prime Directive, no transporters).</summary>
    private static readonly Lazy<GameData> CampaignData = new(() =>
        GameData.Load(TestData.Source, new DataSetup(Era: "dawn_of_warp", Factions: new[] { "federation" })));

    private static GameData Campaign => CampaignData.Value;

    private const ulong Seed = 1701;

    private static string Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        return sim.LastRefusal ?? "";
    }

    private static void Ok(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, sim.LastRefusal);
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void TheSixShipsComeFromTheData()
    {
        var ships = Real.ShipChoices;
        Assert.Equal(new[] { "horizon", "cochrane", "tereshkova", "armstrong", "bastion", "meridian" }, ships.Select(s => s.Id));
        Assert.Equal(new[] { "horizon", "cochrane" }, ships.Where(s => s.Colonizes).Select(s => s.Id));
        Assert.Equal(new[] { "first_light", "first_light", "long_survey", "priority_run", "watch_station", "envoy" }, ships.Select(s => s.Chain));
        Assert.Equal(Fix64.FromInt(2), Real.ShipChoice("armstrong")!.Speed);
        Assert.Equal(Fix64.Parse("0.8"), Real.ShipChoice("horizon")!.Speed);
        Assert.Equal(30, Real.ShipChoice("horizon")!.StartBonus["crew"]);
        Assert.Equal(new[] { "construction_rover" }, Real.ShipChoice("cochrane")!.StartUnits);
        Assert.Equal("res://textures/ships/tereshkova.jpg", Real.ShipChoice("tereshkova")!.Card);
        foreach (var s in ships.Where(s => !s.Colonizes)) Assert.True(Real.Missions!.Chain(s.Chain)!.Space, s.Id);
        Assert.False(Real.Missions!.Chain("first_light")!.Space);
        Assert.Null(Real.ShipChoice("galaxy"));
        Assert.Empty(Real.Validate());
        Assert.Empty(Campaign.Validate());
        // Every space chain has five main missions and no side jobs.
        foreach (var chain in new[] { "long_survey", "priority_run", "watch_station", "envoy" })
        {
            Assert.Equal(5, Real.Missions.List.Count(m => m.Chain == chain && m.IsMain));
            Assert.Equal(0, Real.Missions.List.Count(m => m.Chain == chain && !m.IsMain));
        }
    }

    [Theory]
    [InlineData("\"id\": \"bastion\"", "\"id\": \"armstrong\"", "unique lower-case id")]
    [InlineData("\"chain\": \"envoy\"", "\"chain\": \"embassy\"", "unknown mission chain 'embassy'")]
    [InlineData("\"chain\": \"envoy\"", "\"chain\": \"first_light\"", "plays a space chain")]
    [InlineData("\"colonizes\": true, \"chain\": \"first_light\",\n      \"crew\": \"210 crew\"", "\"colonizes\": true, \"chain\": \"long_survey\",\n      \"crew\": \"210 crew\"", "colony chain")]
    [InlineData("\"crew\": 30, \"duranium\": 200", "\"crew\": 30, \"unobtainium\": 200", "unknown resource 'unobtainium'")]
    [InlineData("\"crew\": 30, \"duranium\": 200", "\"crew\": 30, \"energy\": 200", "never energy")]
    [InlineData("\"start_units\": [\"construction_rover\"]", "\"start_units\": [\"hover_tank\"]", "unknown surface unit 'hover_tank'")]
    [InlineData("\"speed\": 2.0", "\"speed\": 0", "speed must be above 0")]
    [InlineData("\"role\": \"courier\"", "\"role\": \"pirate\"", "role 'pirate'")]
    [InlineData("\"start_bonus\": {}, \"start_units\": [] },\n    { \"id\": \"armstrong\"", "\"start_bonus\": { \"crew\": 5 }, \"start_units\": [] },\n    { \"id\": \"armstrong\"", "only a colonizer")]
    public void BadShipDataIsReported(string from, string to, string problem)
    {
        var data = TestData.Patched(("missions/ship_choice.json", from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    [Theory]
    [InlineData("long_survey.json", "{ \"ship_at\": { \"body\": \"signal\" } }", "{ \"ship_at\": { \"body\": \"signl\" } }", "'signl' is not bound")]
    [InlineData("long_survey.json", "\"pick\": \"gas_or_ice\"", "\"pick\": \"gassy\"", "pick 'gassy'")]
    [InlineData("priority_run.json", "\"state\": \"delivered\", \"count\": 3", "\"state\": \"lost\", \"count\": 3", "state 'lost'")]
    [InlineData("watch_station.json", "{ \"with_ship\": { \"ship\": \"convoy\", \"at_arrival\": true } }", "{ \"with_ship\": { \"ship\": \"edge\", \"at_arrival\": true } }", "'edge' is not bound")]
    [InlineData("envoy.json", "\"stage\": \"studied\"", "\"stage\": \"befriended\"", "stage 'befriended'")]
    [InlineData("envoy.json", "\"title\": \"Find the civilization\"", "\"title\": \"Find {world}\"", "can't have placeholders")]
    [InlineData("priority_run.json", "{ \"load_cargo\": { \"id\": \"pouch\", \"label\": \"Sealed pouch\", \"to\": \"moon\" } }", "{ \"load_cargo\": { \"id\": \"pouch\", \"label\": \"Sealed pouch\" } }", "needs a body")]
    [InlineData("watch_station.json", "{ \"marker\": { \"at\": \"freighter\", \"label\": \"Inspect\" } } ],\n          \"objectives\": [\n            { \"id\": \"inspect\", \"text\": \"Stay with {freighter} for 30 s\", \"cond\": { \"with_ship\": { \"ship\": \"freighter\"",
        "{ \"marker\": { \"at\": \"freighter\", \"label\": \"Inspect\" } } ],\n          \"objectives\": [\n            { \"id\": \"inspect\", \"text\": \"Stay with {freighter} for 30 s\", \"cond\": { \"ship_at\": { \"body\": \"freighter\"", "is not a body")]
    public void BadSpaceMissionDataIsReported(string file, string from, string to, string problem)
    {
        var data = TestData.Patched(("missions/" + file, from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    // ---------------------------------------------------------------- the game

    [Fact]
    public void NoPickIsTheGameItAlwaysWas()
    {
        var a = MatchSetup.CreateSystemGame(Real, Seed);
        var b = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, null);
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(a.StateHash(), b.StateHash());
            a.Step();
            b.Step();
        }
        Assert.Null(a.ShipChoiceOf(0));
        Assert.True(a.CanColonize(0));
        Assert.Null(a.ColonizeProblem(0));
        Assert.Equal(Fix64.One, a.System!.FlagshipOf(0)!.Speed);
    }

    [Fact]
    public void ThePickIsTheFlagshipAndIsHashed()
    {
        var plain = MatchSetup.CreateSystemGame(Real, Seed);
        var sim = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "tereshkova");
        var ship = sim.System!.FlagshipOf(0)!;
        Assert.Equal("tereshkova", sim.ShipChoiceOf(0)!.Id);
        Assert.Equal("prewarp.tereshkova", ship.ClassId);
        Assert.Equal("Tereshkova", ship.Name);
        Assert.StartsWith("UE-", ship.Registry);
        Assert.Equal("survey", ship.Role);
        Assert.Equal(Fix64.Parse("1.1"), ship.Speed);
        Assert.Same(Real.ShipChoice("tereshkova"), sim.System.PickedShip(ship));
        Assert.Null(plain.System!.PickedShip(plain.System.FlagshipOf(0)!));
        Assert.NotEqual(plain.StateHash(), sim.StateHash());
        Assert.NotEqual(MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "horizon").StateHash(),
            MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "cochrane").StateHash());
        Assert.Throws<ArgumentException>(() => MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "galaxy"));
    }

    [Fact]
    public void AShipThatNeverLandsStartsInOrbitOfAnyBody()
    {
        ulong seed = 1;
        while (!SystemGenerator.Generate(Real, seed).Bodies.Any(b => b.ClassId is "class_j" or "class_t") || !SystemGenerator.Generate(Real, seed).Bodies.Any(b => b.Parent != null)) seed++;
        var system = SystemGenerator.Generate(Real, seed);
        var giant = system.Bodies.First(b => b.ClassId is "class_j" or "class_t");
        var home = MatchSetup.HomeBody(Real, system);
        var sim = MatchSetup.CreateSystemGame(Real, seed, "federation", giant.Name.ToUpperInvariant(), "bastion");
        var sys = sim.System!;
        Assert.Equal(giant.Id, sys.FlagshipOf(0)!.AtBody);
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, giant.Id));
        Assert.Equal(home.Id, sys.SurfaceBody); // the surface is still the generator's pick ...
        Assert.Equal(BodyKnowledge.Detected, sys.KnowledgeOf(0, home.Id)); // ... and not where the ship is
        Assert.Equal(LandingPhase.InOrbit, sim.Surface!.Players[0].Phase);
        // A belt or a moon works too; an unknown body doesn't.
        var moon = system.Bodies.First(b => b.Parent != null);
        Assert.Equal(moon.Id, MatchSetup.CreateSystemGame(Real, seed, "federation", moon.Id, "meridian").System!.FlagshipOf(0)!.AtBody);
        Assert.Throws<ArgumentException>(() => MatchSetup.CreateSystemGame(Real, seed, "federation", "nowhere", "bastion"));
        // No start body: the generator's landable pick.
        Assert.Equal(home.Id, MatchSetup.CreateSystemGame(Real, seed, "federation", null, "bastion").System!.FlagshipOf(0)!.AtBody);
    }

    [Fact]
    public void SpeedDividesTheFlagshipsFlightTime()
    {
        var plain = MatchSetup.CreateSystemGame(Real, Seed);
        var fast = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "armstrong");
        var from = plain.System!.FlagshipOf(0)!.AtBody!;
        var to = plain.System.Bodies.Where(b => b.Id != from).OrderByDescending(b => plain.System.TravelTicks(from, b.Id)).First().Id;
        var normal = plain.System.TravelTicks(from, to);
        var ship = fast.System!.FlagshipOf(0)!;
        Assert.Equal(normal / 2, fast.System.TravelTicksFor(ship, from, to));
        Ok(fast, new SetCourseCommand(0, ship.Id, to));
        Assert.Equal(normal / 2, ship.ArriveTick - ship.DepartTick);
        var slow = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, "horizon");
        var hs = slow.System!.FlagshipOf(0)!;
        Assert.True(slow.System.TravelTicksFor(hs, from, to) > normal);
        // Neutral traffic is untouched.
        foreach (var s in fast.System.Ships.Where(s => s.IsNeutral)) Assert.Equal(Fix64.One, s.Speed);
    }

    [Fact]
    public void AShipThatNeverLandsCantLandAColonyAnyWay()
    {
        var system = SystemGenerator.Generate(Real, Seed);
        var home = MatchSetup.HomeBody(Real, system);
        // In orbit of the colony body itself: only the ship rule stands in the way.
        var sim = MatchSetup.CreateSystemGame(Real, Seed, "federation", home.Id, "tereshkova");
        var s = sim.Surface!;
        Assert.Equal(home.Id, sim.System!.FlagshipOf(0)!.AtBody);
        Assert.False(sim.CanColonize(0));
        const string text = "the Tereshkova can't land a colony";
        Assert.Equal(text, sim.ColonizeProblem(0));
        var tile = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        Assert.Equal(text, s.LandProblem(sim, 0, tile));
        Assert.Equal(text, s.OutpostProblem(sim, 0, tile));
        Assert.Equal(text, s.ShuttleProblem(sim, 0, tile, "survey_rover"));
        Assert.Equal(text, s.BeamProblem(sim, 0, tile, OrbitRules.Supplies));
        var hash = sim.StateHash();
        var tick = sim.Tick;
        foreach (var c in new Command[]
                 {
                     new LandColonyCommand(0, tile), new OutpostDropCommand(0, tile), new ShuttleCommand(0, tile, "survey_rover"),
                     new BeamCommand(0, tile, OrbitRules.Supplies), new FoundOutpostCommand(0, 1, tile),
                 })
            Assert.Contains(text, Refused(sim, c));
        Assert.Equal(LandingPhase.InOrbit, s.Players[0].Phase);
        Assert.Empty(s.Units);
        // Refused commands change nothing but the clock: a twin game that got none of them agrees.
        var twin = MatchSetup.CreateSystemGame(Real, Seed, "federation", home.Id, "tereshkova");
        for (var i = tick; i < sim.Tick; i++) twin.Step();
        Assert.Equal(twin.StateHash(), sim.StateHash());
        Assert.NotEqual(hash, sim.StateHash());
        // A colonizer in the same place lands.
        var colony = MatchSetup.CreateSystemGame(Real, Seed, "federation", home.Id, "horizon");
        Assert.Null(colony.Surface!.LandProblem(colony, 0, tile));
        Ok(colony, new LandColonyCommand(0, tile));
    }

    [Fact]
    public void ColonizersGetTheirStartBonus()
    {
        var data = Real;
        var plain = MatchSetup.CreateSystemGame(data, Seed);
        var horizon = MatchSetup.CreateSystemGame(data, Seed, "federation", null, "horizon");
        var cochrane = MatchSetup.CreateSystemGame(data, Seed, "federation", null, "cochrane");
        var tereshkova = MatchSetup.CreateSystemGame(data, Seed, "federation", null, "tereshkova");
        Fix64 Has(Simulation sim, string res) => sim.Players[0].Stock[data.Resource(res).Index];
        Assert.Equal(Has(plain, "crew") + Fix64.FromInt(30), Has(horizon, "crew"));
        Assert.Equal(Has(plain, "duranium") + Fix64.FromInt(200), Has(horizon, "duranium"));
        foreach (var kit in new[] { "mechanical_spares", "electrical_spares", "rover_spares", "environmental_spares" })
        {
            Assert.Equal(Has(plain, kit) + Fix64.FromInt(4), Has(cochrane, kit));
            Assert.Equal(Has(plain, kit), Has(horizon, kit));
        }
        Assert.Equal(Has(plain, "crew"), Has(tereshkova, "crew")); // no bonus for a ship that never lands
        // Cochrane's construction rover lands with the colony, after the usual units.
        int[] Land(Simulation sim)
        {
            var s = sim.Surface!;
            sim.Schedule(sim.Tick, new LandColonyCommand(0, Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t))));
            for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
            return new[] { s.Units.Count, s.Units.Count(u => u.DefId == "construction_rover") };
        }
        var usual = Land(plain);
        var tender = Land(cochrane);
        Assert.Equal(usual[0] + 1, tender[0]);
        Assert.Equal(usual[1] + 1, tender[1]);
        Assert.Equal(usual[0], Land(horizon)[0]);
    }

    [Fact]
    public void HasLivingCivilizationAgreesWithTheGame()
    {
        int yes = 0, no = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var living = MatchSetup.HasLivingCivilization(Real, seed);
            var sim = MatchSetup.CreateSystemGame(Real, seed, "federation", null, "meridian");
            Assert.Equal(living, sim.System!.Civilizations.Any(c => c.Living));
            if (living) yes++;
            else no++;
        }
        Assert.True(yes > 0 && no > 0, $"{yes} with, {no} without");
    }

    [Fact]
    public void ColonizersKeepFirstLightAndItsGate()
    {
        var sim = MatchSetup.CreateSystemGame(Campaign, Seed, "federation", null, "horizon");
        Assert.Null(sim.Missions.Enable("first_light"));
        Assert.False(sim.Missions.SpaceChain);
        for (var i = 0; i < 30 * Simulation.TickRate; i++) sim.Step();
        Assert.Empty(sim.Missions.For(0)); // nothing before the outpost's uplink
        Assert.False(sim.Surface!.BuildingUnlocked(0, "science_lab")); // the build menu is locked to start_menu as before
    }

    [Fact]
    public void ASpaceChainRunsWithoutAnUplinkAndLeavesTheBuildMenuAlone()
    {
        var sim = MatchSetup.CreateSystemGame(Campaign, Seed, "federation", null, "tereshkova");
        Assert.Null(sim.Missions.Enable("long_survey"));
        Assert.True(sim.Missions.SpaceChain);
        for (var i = 0; i < 2 * Simulation.TickRate; i++) sim.Step();
        var view = Assert.Single(sim.Missions.For(0));
        Assert.Equal("outbound", view.Id);
        Assert.Equal(MissionState.Active, view.State);
        Assert.Contains(sim.Missions.Messages(0), m => m.Giver == "science" && m.Channel == "comms");
        Assert.True(sim.Surface!.BuildingUnlocked(0, "science_lab"));
        Assert.Empty(sim.Missions.Markers(0)); // no surface markers for a space chain
    }

    private static void Debug(Simulation sim, string mission, DebugMissionAction action)
    {
        var c = new DebugMissionCommand(0, 0, mission, action, "", "", -1);
        Assert.Null(sim.DebugProblem(c));
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void Seconds(Simulation sim, int seconds)
    {
        for (var i = 0; i < seconds * Simulation.TickRate; i++) sim.Step();
    }

    [Fact]
    public void ACourierTimerStartsTheRunAgainAndADistressCallCanBeLost()
    {
        var courier = MatchSetup.CreateSystemGame(Campaign, Seed, "federation", null, "armstrong");
        Assert.Null(courier.Missions.Enable("priority_run"));
        Seconds(courier, 2);
        var first = courier.Missions.View(0, "shakedown")!;
        Assert.Equal(0, first.StageIndex);
        Assert.True(first.TimerEndTick > 0);
        Seconds(courier, 101); // never leaves orbit
        var again = courier.Missions.View(0, "shakedown")!;
        Assert.Equal(MissionState.Active, again.State);
        Assert.Equal(0, again.StageIndex);
        Assert.True(again.TimerEndTick > first.TimerEndTick);
        Assert.Contains(courier.Missions.Messages(0), m => m.Text.Contains("out of time"));

        var patrol = MatchSetup.CreateSystemGame(Campaign, Seed, "federation", null, "bastion");
        Assert.Null(patrol.Missions.Enable("watch_station"));
        Seconds(patrol, 1);
        Debug(patrol, "patrol_route", DebugMissionAction.Complete);
        Debug(patrol, "traffic_check", DebugMissionAction.Complete);
        Seconds(patrol, 2);
        Assert.Equal(MissionState.Active, patrol.Missions.View(0, "distress_call")!.State);
        Assert.Contains(patrol.Missions.SpaceMarkers(0), m => m.Kind == "beacon" && m.Label == "Distress beacon");
        Seconds(patrol, 121);
        var lost = patrol.Missions.View(0, "distress_call")!;
        Assert.Equal(MissionState.Failed, lost.State);
        Assert.Equal("timeout", lost.Outcome);
        Assert.Equal(MissionState.Active, patrol.Missions.View(0, "escort")!.State); // the chain goes on
    }

    [Fact]
    public void CargoShowsAboardAndOnThePlot()
    {
        var sim = MatchSetup.CreateSystemGame(Campaign, Seed, "federation", null, "armstrong");
        Assert.Null(sim.Missions.Enable("priority_run"));
        Seconds(sim, 1);
        Debug(sim, "shakedown", DebugMissionAction.Complete);
        Seconds(sim, 1);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var pickup = Assert.Single(sim.Missions.SpaceMarkers(0), m => m.Kind == "pickup");
        Assert.Equal("Vaccine crates", pickup.Label);
        Assert.Empty(sim.Missions.CargoAboard(0));
        Ok(sim, new SetCourseCommand(0, ship.Id, pickup.BodyId!));
        for (var i = 0; i < 120 * Simulation.TickRate && sim.Missions.CargoAboard(0).Count == 0; i++) sim.Step();
        var dropoff = Assert.Single(sim.Missions.SpaceMarkers(0), m => m.Kind == "dropoff");
        Assert.Equal($"Vaccine crates → {sys.Body(dropoff.BodyId)!.Name}", Assert.Single(sim.Missions.CargoAboard(0)));
        Assert.DoesNotContain(sim.Missions.SpaceMarkers(0), m => m.Kind == "pickup");
        Ok(sim, new SetCourseCommand(0, ship.Id, dropoff.BodyId!));
        for (var i = 0; i < 120 * Simulation.TickRate && sim.Missions.View(0, "medicine_run")!.State == MissionState.Active; i++) sim.Step();
        Assert.Equal(MissionState.Completed, sim.Missions.View(0, "medicine_run")!.State);
        Assert.Empty(sim.Missions.CargoAboard(0));
        Assert.Contains(sim.Missions.Messages(0), m => m.Text.StartsWith("Vaccine crates: delivered"));
    }

    // ---------------------------------------------------------------- the four chains, played

    /// <summary>A campaign game with the ship picked and its chain on, played to the end by <see cref="Pilot"/> (commands
    /// only); returns the hash every second and the game.</summary>
    private (List<ulong> Hashes, Simulation Sim, Pilot Pilot) Play(string shipId, ulong seed, int maxMinutes, string? start = null)
        => Play(shipId, seed, maxMinutes, start, false, null);

    /// <summary>As above; sixArgs = through the 6-argument game with this fit-out (null = none: it must be the same game).</summary>
    private (List<ulong> Hashes, Simulation Sim, Pilot Pilot) Play(string shipId, ulong seed, int maxMinutes, string? start, bool sixArgs, FitOut? fitOut)
    {
        var data = Campaign;
        var sim = sixArgs ? MatchSetup.CreateSystemGame(data, seed, "federation", start, shipId, fitOut) : MatchSetup.CreateSystemGame(data, seed, "federation", start, shipId);
        if (sixArgs && fitOut != null) Assert.NotNull(sim.FitOutOf(0));
        Assert.Null(sim.Missions.Enable(data.ShipChoice(shipId)!.Chain));
        var pilot = new Pilot(sim);
        var hashes = new List<ulong>();
        var chain = sim.Missions.States(0);
        for (var second = 0; second < maxMinutes * 60; second++)
        {
            pilot.Think();
            for (var i = 0; i < Simulation.TickRate; i++) sim.Step();
            hashes.Add(sim.StateHash());
            if (sim.Missions.States(0).All(s => s.State is MissionState.Completed or MissionState.Failed)) break;
        }
        foreach (var line in pilot.Log) _out.WriteLine(line);
        _out.WriteLine($"{shipId}: {sim.Tick / Simulation.TickRate} s, refused {sim.RefusedCommands} ({sim.LastRefusal})");
        foreach (var m in sim.Missions.Messages(0).Where(m => m.Channel == "comms")) _out.WriteLine($"  {m.Tick / Simulation.TickRate} s {m.GiverName}: {m.Text}");
        var states = sim.Missions.States(0);
        Assert.Equal(5, states.Count);
        Assert.All(states, s => Assert.True(s.State == MissionState.Completed, $"{s.Id} is {s.State}"));
        return (hashes, sim, pilot);
    }

    private void PlayTwice(string shipId, ulong seed, int maxMinutes, string? start = null)
    {
        var a = Play(shipId, seed, maxMinutes, start);
        var b = Play(shipId, seed, maxMinutes, start);
        Assert.Equal(a.Hashes, b.Hashes);
        Assert.Equal(0, a.Sim.DebugEdits);
    }

    /// <summary>The first seed whose system has a gas giant and a moon (seed 1701's is all rocks and belts).</summary>
    private static ulong RichSeed()
    {
        ulong seed = 1;
        while (SystemGenerator.Generate(Real, seed) is var g && (!g.Bodies.Any(b => b.ClassId is "class_j" or "class_t") || !g.Bodies.Any(b => b.Parent != null))) seed++;
        return seed;
    }

    [Fact]
    public void TheLongSurveyPlaysToTheEnd()
    {
        var rich = RichSeed();
        PlayTwice("tereshkova", rich, 60);
        var (_, sim, _) = Play("tereshkova", Seed, 60);
        Assert.All(sim.System!.Bodies, b => Assert.Equal(BodyKnowledge.Charted, sim.System.KnowledgeOf(0, b.Id)));
        Assert.Contains("long_survey_done", sim.Missions.Flags(0));
        // Close Look picks a gas giant or an ice body when the system has one, and names it.
        var game = MatchSetup.CreateSystemGame(Campaign, rich, "federation", null, "tereshkova");
        Assert.Null(game.Missions.Enable("long_survey"));
        game.Step();
        var start = new DebugMissionCommand(0, 0, "close_look", DebugMissionAction.Start, "", "", -1);
        Assert.Null(game.DebugProblem(start));
        game.Schedule(game.Tick, start);
        game.Step();
        var target = Assert.Single(game.Missions.SpaceMarkers(0));
        Assert.Contains(game.System!.Body(target.BodyId)!.ClassId, new[] { "class_j", "class_t", "ice_moon" });
        Assert.NotEqual(BodyKnowledge.Detected, game.System.KnowledgeOf(0, target.BodyId!));
    }

    [Fact]
    public void PriorityRunPlaysToTheEnd()
    {
        PlayTwice("armstrong", Seed, 40);
        var (_, sim, pilot) = Play("armstrong", Seed, 40);
        Assert.True(pilot.SawCargoAboard, "cargo was never aboard");
        Assert.True(pilot.SawPickup && pilot.SawDropoff, "no pick-up / drop-off markers");
        Assert.Empty(sim.Missions.CargoAboard(0)); // delivered and the missions are over
    }

    [Fact]
    public void WatchStationPlaysToTheEnd()
    {
        PlayTwice("bastion", Seed, 60);
        var (_, _, pilot) = Play("bastion", RichSeed(), 60);
        Assert.True(pilot.SawShipMarker && pilot.SawBeacon, "no ship marker or beacon");
    }

    [Fact]
    public void EnvoyPlaysToTheEndInDawnOfWarp()
    {
        // A system with a living civilization (Meridian's system page only offers those), from another body.
        ulong seed = 1;
        while (!MatchSetup.HasLivingCivilization(Campaign, seed)) seed++;
        var system = SystemGenerator.Generate(Campaign, seed);
        var civ = CivGenerator.Roll(Campaign, system.Bodies, seed, MatchSetup.HomeBody(Campaign, system).Id).First(c => c.Living);
        var start = system.Bodies.First(b => b.Id != civ.BodyId).Id;
        Assert.False(MatchSetup.CreateSystemGame(Campaign, seed, "federation", start, "meridian").System!.PrimeDirective);
        PlayTwice("meridian", seed, 60, start);
        var (_, sim, _) = Play("meridian", seed, 60, start);
        Assert.True(sim.System!.RelationsWith(0, civ.BodyId) >= ContactStage.Contact);
        Assert.Contains("envoy_done", sim.Missions.Flags(0));
    }

    // ---------------------------------------------------------------- the four chains with a fit-out (docs/fit-out-contract.md phase 2)

    /// <summary>Each ship's game for the play-throughs: the seed and start orbit its chain is played from above.</summary>
    private static (ulong Seed, string? Start) Setup(string shipId)
    {
        if (shipId != "meridian") return (shipId == "tereshkova" ? RichSeed() : Seed, null);
        ulong seed = 1;
        while (!MatchSetup.HasLivingCivilization(Campaign, seed)) seed++;
        var system = SystemGenerator.Generate(Campaign, seed);
        var civ = CivGenerator.Roll(Campaign, system.Bodies, seed, MatchSetup.HomeBody(Campaign, system).Id).First(c => c.Living);
        return (seed, system.Bodies.First(b => b.Id != civ.BodyId).Id);
    }

    [Theory]
    [InlineData("tereshkova")]
    [InlineData("armstrong")]
    [InlineData("bastion")]
    [InlineData("meridian")]
    public void NoFitOutPlaysTheChainExactlyAsBefore(string shipId)
    {
        // The 5-argument game and the 6-argument one handed no fit-out: the same hash every second to the chain's end.
        var (seed, start) = Setup(shipId);
        var plain = Play(shipId, seed, 60, start);
        var none = Play(shipId, seed, 60, start, true, null);
        Assert.Null(none.Sim.FitOutOf(0));
        Assert.Equal(plain.Hashes, none.Hashes);
    }

    [Theory]
    [InlineData("tereshkova")]
    [InlineData("armstrong")]
    [InlineData("bastion")]
    [InlineData("meridian")]
    public void EveryFocusPresetFinishesTheShipsChain(string shipId)
    {
        var (seed, start) = Setup(shipId);
        var plain = Play(shipId, seed, 60, start);
        foreach (var focus in Campaign.FitOut!.Focuses)
        {
            var fit = FitOutRules.Preset(Campaign, shipId, focus.Id)!;
            Assert.Null(FitOutRules.Problem(Campaign, shipId, fit));
            var (hashes, sim, _) = Play(shipId, seed, 60, start, true, fit); // asserts every mission of the chain completed
            Assert.Equal(0, sim.DebugEdits);
            _out.WriteLine($"{shipId} {focus.Id}: {hashes.Count} s (no fit-out: {plain.Hashes.Count} s)");
            if (focus.Id == Campaign.FitOut.Ship(shipId)!.Suggest)
                Assert.Equal(hashes, Play(shipId, seed, 60, start, true, fit).Hashes); // the same fit-out twice: the same game
        }
    }

    /// <summary>
    /// Plays a space chain the way a player would, from what the game shows: the active mission's objectives, the space
    /// markers on the system plot and the ship's card. Commands only (SetCourse, LongRangeScan, SystemSatellite,
    /// ObservationPost, FirstContact, ContactChoice), sent once a second.
    /// </summary>
    private sealed class Pilot
    {
        private readonly Simulation _sim;
        public readonly List<string> Log = new();
        public bool SawCargoAboard, SawPickup, SawDropoff, SawShipMarker, SawBeacon;

        public Pilot(Simulation sim) => _sim = sim;

        private StarSystemState Sys => _sim.System!;
        private SystemShip Ship => Sys.FlagshipOf(0)!;

        private void Send(Command c) => _sim.Schedule(_sim.Tick, c);

        /// <summary>Flies to the body (once; nothing while in flight or already there).</summary>
        private void Go(string body)
        {
            if (Ship.InFlight || Ship.AtBody == body) return;
            if (Sys.CourseProblem(0, Ship.Id, body) != null) return;
            Log.Add($"{_sim.Tick / Simulation.TickRate} s: course for {Sys.Body(body)!.Name}");
            Send(new SetCourseCommand(0, Ship.Id, body));
        }

        private void Scan()
        {
            if (Sys.ScanProblem(0, Ship.Id) == null) Send(new LongRangeScanCommand(0, Ship.Id));
        }

        private string? Nearest(IEnumerable<string> bodies)
        {
            var at = Sys.ShipPosition(Ship, _sim.Tick);
            return bodies.OrderBy(b => FixVec2.Distance(Sys.Body(b)!.Position, at)).ThenBy(b => b, StringComparer.Ordinal).FirstOrDefault();
        }

        private string? NearestUncharted() =>
            Nearest(Sys.Bodies.Where(b => Sys.KnowledgeOf(0, b.Id) < BodyKnowledge.Charted).Select(b => b.Id));

        public void Think()
        {
            var view = _sim.Missions.For(0).FirstOrDefault(v => v.State == MissionState.Active && v.Kind == MissionKind.Main);
            if (view == null) return;
            var markers = _sim.Missions.SpaceMarkers(0);
            SawPickup |= markers.Any(m => m.Kind == "pickup");
            SawDropoff |= markers.Any(m => m.Kind == "dropoff");
            SawShipMarker |= markers.Any(m => m.Kind == "ship");
            SawBeacon |= markers.Any(m => m.Kind == "beacon");
            SawCargoAboard |= _sim.Missions.CargoAboard(0).Count > 0;
            var open = view.Objectives.Where(o => !o.Done).ToList();
            if (open.Any(o => o.Text.Contains("long-range scan", StringComparison.OrdinalIgnoreCase))) Scan();
            if (Ship.InFlight) return;
            switch (view.Id)
            {
                case "outbound":
                case "whole_map":
                    if (open.Any(o => o.Id is "chart" or "all") && (Sys.KnowledgeOf(0, Ship.AtBody!) == BodyKnowledge.Charted) && NearestUncharted() is { } next)
                        Go(next);
                    return;
                case "eyes_everywhere":
                    if (Sys.SatelliteProblem(0, Ship.AtBody!) == null) Send(new SystemSatelliteCommand(0, Ship.AtBody!));
                    else if (Nearest(Sys.Bodies.Where(b => !Sys.Satellites.Any(s => s.Owner == 0 && s.BodyId == b.Id)).Select(b => b.Id)) is { } free) Go(free);
                    return;
                case "traffic_check":
                {
                    var target = markers.First(m => m.Kind == "ship");
                    var freighter = Sys.FindShip(target.ShipId)!;
                    var where = freighter.AtBody ?? freighter.ToBody;
                    if (where != null) Go(where);
                    return;
                }
                case "listening_post":
                    if (markers.FirstOrDefault(m => m.Kind == "target") is { BodyId: { } world })
                    {
                        if (Ship.AtBody != world) Go(world);
                        else if (Sys.PostProblem(0, world) == null) Send(new ObservationPostCommand(0, world));
                    }
                    return;
                case "first_words":
                    if (markers.FirstOrDefault(m => m.Kind == "target") is { BodyId: { } people })
                    {
                        if (Ship.AtBody != people) Go(people);
                        else if (Sys.ContactEventOf(0, people) is { } ev)
                        {
                            if (ev.Chosen[ev.Step] < 0) Send(new ContactChoiceCommand(0, people, 1)); // envoy team, cultural exchange, limited
                        }
                        else if (Sys.FirstContactProblem(0, people) == null) Send(new FirstContactCommand(0, people));
                    }
                    return;
            }
            // Everything else: pick-ups first, then drop-offs, then beacons, then targets (waypoint order).
            var go = Nearest(markers.Where(m => m.Kind == "pickup").Select(m => m.BodyId!))
                     ?? Nearest(markers.Where(m => m.Kind == "dropoff").Select(m => m.BodyId!))
                     ?? markers.Where(m => m.Kind == "beacon").Select(m => m.BodyId).FirstOrDefault()
                     ?? markers.Where(m => m.Kind == "target").OrderBy(m => m.Label, StringComparer.Ordinal).Select(m => m.BodyId).FirstOrDefault();
            if (go != null) Go(go);
        }
    }
}
