using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Fit Out Your Ship, phase 2 (docs/fit-out-contract.md "the four ships that never land"; data/missions/fit_out.json):
/// the four space ships' data, their own cargo lines and levers, every space lever moving the right way by the data's
/// amount where the star system reads it (scans, sweeps, flights, study, culture shock, time alongside), the space perks,
/// and the body satellites, posts and sensor probes from the hold that are used up and then cost as before. The chains
/// played with every preset are in ShipPickTests (EveryFocusPresetFinishesTheShipsChain, NoFitOutPlaysTheChainExactlyAsBefore).
/// </summary>
public class FitOutSpaceTests
{
    private static GameData Real => TestData.Real;
    private const ulong Seed = 1701;
    private const string File = "missions/fit_out.json";
    private static readonly string[] SpaceShips = { "tereshkova", "armstrong", "bastion", "meridian" };

    private static void Near(double expected, Fix64 actual, double within = 1e-6) =>
        Assert.True(System.Math.Abs(expected - actual.ToDouble()) <= within, $"{actual.ToDouble()} is not {expected}");

    private static void Steps(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
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

    /// <summary>The Tereshkova's 40 crew with 28 in one division (70%: strength +0.18) and 4 in each other (10%: −0.06),
    /// under that division's focus, with this cargo (the base load if none given).</summary>
    private static FitOut Heavy(string division, string ship = "tereshkova", params (string Line, int Count)[] cargo)
    {
        var rules = Real.FitOut!;
        var crew = rules.Ship(ship)!.Crew;
        var low = (crew + 9) / 10;
        var load = new Dictionary<string, int> { ["probe"] = 1, ["mass"] = 1, ["gifts"] = 1 };
        foreach (var (line, count) in cargo) load[line] = count;
        var fit = new FitOut(rules.Focuses.First(f => f.Division == division).Id,
            rules.Divisions.Select(d => d.Id == division ? crew - low * (rules.Divisions.Count - 1) : low).ToArray(), load);
        Assert.Null(FitOutRules.Problem(Real, ship, fit));
        return fit;
    }

    private static FitOut Even(string ship, params (string Line, int Count)[] cargo)
    {
        var fit = FitOutRules.Preset(Real, ship, "allround")!;
        var load = fit.Cargo.ToDictionary(kv => kv.Key, kv => 0);
        load["probe"] = 1;
        load["mass"] = 1;
        load["gifts"] = 1;
        foreach (var (line, count) in cargo) load[line] = count;
        return new FitOut("allround", fit.Crew, load);
    }

    private static Simulation Game(string ship, FitOut? fit, ulong seed = Seed, string? start = null, GameData? data = null) =>
        MatchSetup.CreateSystemGame(data ?? Real, seed, "federation", start, ship, fit);

    private static void Zero(Simulation sim) =>
        Do(sim, new DebugStockCommand(0, 0, sim.Data.Resources.Select(r => new ResourceAmount(r.Index, Fix64.Zero)).ToList()));

    private static void GoTo(Simulation sim, string bodyId)
    {
        var ship = sim.System!.FlagshipOf(0)!;
        if (ship.AtBody == bodyId) return;
        Do(sim, new SetCourseCommand(0, ship.Id, bodyId));
        for (var i = 0; i < 6000 && ship.AtBody != bodyId; i++) sim.Step();
        Assert.Equal(bodyId, ship.AtBody);
    }

    /// <summary>A seed with a living civilization and that body (the flagship can start in orbit of it: Charted at once).</summary>
    private static (ulong Seed, string Body) CivSeed()
    {
        for (ulong seed = 1; seed < 3000; seed++)
        {
            var g = SystemGenerator.Generate(Real, seed);
            var site = CivGenerator.Roll(Real, g.Bodies, seed, MatchSetup.HomeBody(Real, g).Id).FirstOrDefault(s => s.Living);
            if (site != null) return (seed, site.BodyId);
        }
        throw new InvalidOperationException("no seed has a living civilization");
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void TheFourShipsAreFittedOutWithTheirOwnCargoAndLevers()
    {
        var f = Real.FitOut!;
        Assert.Empty(Real.Validate());
        Assert.Equal(new[] { ("tereshkova", 40, 12, "explorer"), ("armstrong", 6, 6, "builder"), ("bastion", 85, 14, "combatant"), ("meridian", 350, 20, "scientist") },
            f.Ships.Skip(2).Select(s => (s.ShipId, s.Crew, s.Hold, s.Suggest)));
        Assert.Equal(new[] { "ssat", "probe", "mass", "post", "gifts", "patch", "med_space" }, f.LinesFor(true).Select(l => l.Id)); // patch, med_space: space jobs (docs/space-jobs-contract.md)
        Assert.Equal(new[] { ("exploration", 1), ("engineering", 1), ("science", 1), ("defense", 0), ("ship", 0) }, f.Groups.Select(g => (g.Id, g.SpaceFloor)));
        Assert.Equal(FitOutRules.SpaceLevers.OrderBy(x => x), f.LeversFor(true).Select(l => l.Id).OrderBy(x => x));
        Assert.Equal(6, f.LeversFor(true).Count(l => l.Division.Length > 0));
        Assert.Equal((1, 1, 1, -3), (f.Line("ssat")!.SystemSatellites, f.Line("post")!.Posts, f.Line("probe")!.Probes, f.Line("gifts")!.ShockPoints));
        foreach (var ship in SpaceShips)
        {
            Assert.True(FitOutRules.Applies(Real, ship) && FitOutRules.IsSpace(Real, ship));
            foreach (var focus in f.Focuses)
            {
                var fit = FitOutRules.Preset(Real, ship, focus.Id)!;
                Assert.Null(FitOutRules.Problem(Real, ship, fit));
                Assert.All(fit.Cargo.Keys, k => Assert.True(f.Line(k)!.Space, $"{ship} {focus.Id} carries {k}"));
            }
        }
        Assert.False(FitOutRules.IsSpace(Real, "horizon") || FitOutRules.IsSpace(Real, "cochrane") || FitOutRules.IsSpace(Real, "galaxy"));

        // Presets follow the base load and the fill order: Tereshkova's Explorer brings three body satellites and three probes.
        var t = FitOutRules.Preset(Real, "tereshkova", "explorer")!;
        Assert.Equal((3, 3, 2, 0, 1), (FitOutRules.CargoCount(t, "ssat"), FitOutRules.CargoCount(t, "probe"), FitOutRules.CargoCount(t, "mass"),
            FitOutRules.CargoCount(t, "post"), FitOutRules.CargoCount(t, "gifts")));
        Assert.Equal(12, FitOutRules.ContainersUsed(Real, t));
        Assert.Equal((3, 0, 3, -3), (FitOutRules.FreeSystemSatellites(Real, t), FitOutRules.FreePosts(Real, t), FitOutRules.Probes(Real, t), FitOutRules.ShockPoints(Real, t)));
        // Armstrong (6 crew, 6 containers): the Builder's floors are 2 engineers and 1 of each other.
        var a = FitOutRules.Preset(Real, "armstrong", "builder")!;
        Assert.Equal(new[] { 1, 3, 1, 1 }, a.Crew);
        Assert.Equal(6, FitOutRules.ContainersUsed(Real, a));
        // Nothing lands from a ship that never lands.
        Assert.Empty(FitOutRules.LandingUnits(Real, "tereshkova", t).Skip(Real.Landing.Units.Count));
        Assert.Empty(FitOutRules.CargoStock(Real, t));
    }

    [Fact]
    public void EachKindOfShipCarriesOnlyItsOwnLines()
    {
        var space = FitOutRules.Preset(Real, "bastion", "combatant")!;
        var load = space.Cargo.ToDictionary(kv => kv.Key, kv => kv.Value);
        load["mat"] = 6;
        Assert.Equal("Building materials lands with a colony, and the Bastion never lands", FitOutRules.Problem(Real, "bastion", space with { Cargo = load }));
        var colony = FitOutRules.Preset(Real, "horizon", "allround")!;
        load = colony.Cargo.ToDictionary(kv => kv.Key, kv => kv.Value);
        load["probe"] = 1;
        Assert.Equal("Sensor probe is for a ship that never lands", FitOutRules.Problem(Real, "horizon", colony with { Cargo = load }));
        // The other kind's lines at 0 (the page holds every line) are fine, and the colony minimums don't apply in space.
        load = space.Cargo.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var l in Real.FitOut!.LinesFor(false)) load[l.Id] = 0;
        Assert.Null(FitOutRules.Problem(Real, "bastion", space with { Cargo = load }));
        // Space floors: exploration, engineering and science 1 each; defense and ship stores none.
        Assert.Equal("Exploration gear needs at least 1 containers (it has 0)", FitOutRules.Problem(Real, "tereshkova", Even("tereshkova", ("probe", 0))));
        Assert.Equal("Science stores needs at least 1 containers (it has 0)", FitOutRules.Problem(Real, "tereshkova", Even("tereshkova", ("gifts", 0))));
        Assert.Null(FitOutRules.Problem(Real, "tereshkova", Even("tereshkova", ("gifts", 0), ("post", 1))));
        Assert.Equal("Reaction mass: at most 6 can be loaded", FitOutRules.Problem(Real, "meridian", Even("meridian", ("mass", 7))));
        Assert.Equal("the hold takes 6 containers, and this load needs 7", FitOutRules.Problem(Real, "armstrong", Even("armstrong", ("ssat", 2))));
    }

    [Theory]
    [InlineData("\"levers\": { \"sickness\": -0.08 }", "\"levers\": { \"flight_time\": -0.08 }", "flight_time is a space lever, and this line lands with the colony")]
    [InlineData("\"levers\": { \"flight_time\": -0.02 }", "\"levers\": { \"drive_speed\": 0.02 }", "drive_speed is not a space lever")]
    [InlineData("\"probes\": 1 }", "\"probes\": 1, \"stock\": { \"duranium\": 5 } }", "a space line never lands")]
    [InlineData("\"research\": 20 }", "\"research\": 20, \"posts\": 1 }", "are for ships that never land")]
    [InlineData("\"space_perk_levers\": { \"study_time\": 0.75 }", "\"space_perk_levers\": { \"research_rate\": 1.2 }", "research_rate is not a space lever")]
    [InlineData("\"perk_levers\": { \"mission_research\": 1.2 }", "\"perk_levers\": { \"study_time\": 0.75 }", "study_time is a space lever")]
    [InlineData("\"id\": \"scan_cooldown\", \"space\": true,", "\"id\": \"scan_cooldown\",", "needs \"space\": true")]
    [InlineData("\"name\": \"Defense stores\", \"floor\": 2, \"space_floor\": 0", "\"name\": \"Defense stores\", \"floor\": 2, \"space_floor\": 1", "can't fill its space_floor")]
    [InlineData("\"system_satellites\": 1 }", "\"system_satellites\": 2 }", "more system satellites than a player may place")]
    [InlineData("\"posts\": 1 }", "\"posts\": 2 }", "more listening posts than a player may have hidden")]
    [InlineData("\"shock_points\": -3 }", "\"shock_points\": -30 }", "within 100 points")]
    [InlineData("{ \"ship\": \"armstrong\", \"crew\": 6, \"hold\": 6, \"suggest\": \"builder\",\n        \"base\": { \"probe\": 1,", "{ \"ship\": \"armstrong\", \"crew\": 6, \"hold\": 6, \"suggest\": \"builder\",\n        \"base\": { \"fuel\": 1, \"probe\": 1,", "base: fuel is cargo for a colony ship")]
    [InlineData("\"hold\": 6, \"suggest\": \"builder\"", "\"hold\": 2, \"suggest\": \"builder\"", "preset is not a legal fit-out: the hold takes 2 containers")]
    public void BadSpaceDataIsReported(string from, string to, string problem)
    {
        var data = TestData.Patched((File, from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    // ---------------------------------------------------------------- no fit-out = the game as it was

    [Theory]
    [InlineData("tereshkova")]
    [InlineData("armstrong")]
    [InlineData("bastion")]
    [InlineData("meridian")]
    public void NoFitOutIsTheGameItAlwaysWas(string ship)
    {
        // The 5-argument game and the 6-argument game with no fit-out: the same hashes through scans, flights and a satellite.
        var a = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship);
        var b = MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship, null);
        var other = a.System!.Bodies.First(x => x.Id != a.System.FlagshipOf(0)!.AtBody).Id;
        for (var tick = 0; tick < 240 * Simulation.TickRate; tick++)
        {
            foreach (var g in new[] { a, b })
            {
                var flagship = g.System!.FlagshipOf(0)!.Id;
                if (tick == 2) g.Schedule(g.Tick, new LongRangeScanCommand(0, flagship));
                if (tick == 4) g.Schedule(g.Tick, new SystemSatelliteCommand(0, g.System.FlagshipOf(0)!.AtBody!));
                if (tick == 30) g.Schedule(g.Tick, new SetCourseCommand(0, flagship, other));
                if (tick == 30 * Simulation.TickRate) g.Schedule(g.Tick, new LongRangeScanCommand(0, flagship)); // recharging: refused alike
                g.Step();
            }
            if (tick % 60 == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var g in new[] { a, b })
        {
            Assert.Null(g.FitOutOf(0));
            Assert.Equal((0, 0, 0, 0), (g.FitOutFreeSystemSatellites(0), g.FitOutFreePosts(0), g.FitOutProbes(0), g.FitOutShockPoints(0)));
            foreach (var lever in FitOutRules.KnownLevers) Assert.Equal(Fix64.One, g.FitOutFactor(0, lever));
            var sys = g.System!;
            Assert.Equal(sys.ScanCooldownTicks, sys.ScanCooldownTicksFor(0));
            Assert.Equal(sys.SweepTicks, sys.SweepTicksFor(0));
            Assert.Equal(sys.ScanRange, sys.ScanRangeFor(0));
        }
        // And a fitted-out space ship's game is a different one (the fit-out is in the hash).
        Assert.NotEqual(MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship).StateHash(),
            MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship, FitOutRules.Preset(Real, ship, "allround")).StateHash());
    }

    [Fact]
    public void AFittedOutSpaceShipKeepsTheStartStock()
    {
        var plain = Game("tereshkova", null);
        var fitted = Game("tereshkova", FitOutRules.Preset(Real, "tereshkova", "explorer"));
        foreach (var r in Real.Resources) Assert.Equal(plain.Players[0].Stock[r.Index], fitted.Players[0].Stock[r.Index]);
        // A bad one is replaced by the ship's suggested preset.
        var bad = new FitOut("explorer", new[] { 1, 1, 1, 1 }, new Dictionary<string, int> { ["ssat"] = 9 });
        Assert.Equal("explorer", Game("tereshkova", bad).FitOutOf(0)!.Focus);
        Assert.Equal(FitOutRules.Preset(Real, "tereshkova", "explorer")!.Crew, Game("tereshkova", bad).FitOutOf(0)!.Crew);
    }

    // ---------------------------------------------------------------- levers

    public static IEnumerable<object[]> SpaceCrewLevers() => TestData.Real.FitOut!.LeversFor(true).Where(l => l.Division.Length > 0).Select(l => new object[] { l.Id });

    [Theory]
    [MemberData(nameof(SpaceCrewLevers))]
    public void EverySpaceCrewLeverMovesTheRightWayByTheDatasAmount(string leverId)
    {
        var rules = Real.FitOut!;
        var lever = rules.Lever(leverId)!;
        var other = rules.Divisions.First(d => d.Id != lever.Division).Id;
        var focus = rules.Focuses.First(f => f.Division == lever.Division);
        var perk = focus.SpacePerkLevers.Where(e => e.Lever == leverId).Select(e => e.Value.ToDouble()).DefaultIfEmpty(1.0).Single();
        var cargo = rules.LinesFor(true).Sum(l => l.Levers.Where(e => e.Lever == leverId).Sum(e => e.Value.ToDouble()) * (l.Id is "probe" or "mass" or "gifts" ? 1 : 0));
        var strong = FitOutRules.Factor(Real, "tereshkova", Heavy(lever.Division), leverId);
        var weak = FitOutRules.Factor(Real, "tereshkova", Heavy(other), leverId);
        // The other focus's perk on this lever, if it has one.
        var otherPerk = rules.Focuses.First(f => f.Division == other).SpacePerkLevers.Where(e => e.Lever == leverId).Select(e => e.Value.ToDouble()).DefaultIfEmpty(1.0).Single();
        Near((lever.MoreIsBetter ? 1.18 : 0.82) * perk * (1 + cargo), strong, 1e-4);
        Near((lever.MoreIsBetter ? 0.94 : 1.06) * otherPerk * (1 + cargo), weak, 1e-4);
        var sim = Game("tereshkova", Heavy(lever.Division));
        Assert.Equal(strong, sim.FitOutFactor(0, leverId));
        // A space lever does nothing on a colony ship, and a colony lever nothing on a ship that never lands.
        Assert.Equal(Fix64.One, FitOutRules.Factor(Real, "horizon", FitOutRules.Preset(Real, "horizon", focus.Id), leverId));
        foreach (var colony in rules.LeversFor(false)) Assert.Equal(Fix64.One, sim.FitOutFactor(0, colony.Id));
    }

    [Fact]
    public void ExplorationRechargesScansAndSweepsSoonerAndTheExplorerSeesFarther()
    {
        var none = Game("tereshkova", null);
        var fit = Heavy("exploration");
        var with = Game("tereshkova", fit);
        var sys = with.System!;
        var factor = FitOutRules.Factor(Real, "tereshkova", fit, FitOutRules.ScanCooldown);
        Near(0.82, factor, 1e-4);
        Assert.Equal((Fix64.FromInt((int)none.System!.ScanCooldownTicks) * factor).FloorToInt(), sys.ScanCooldownTicksFor(0));
        Assert.Equal((Fix64.FromInt((int)none.System.SweepTicks) * FitOutRules.Factor(Real, "tereshkova", fit, FitOutRules.SweepTime)).FloorToInt(), sys.SweepTicksFor(0));
        Assert.Equal(Fix64.FromInt(400), sys.ScanRangeFor(0)); // the Explorer's perk: 320 x 1.25
        Assert.Equal(Fix64.FromInt(320), sys.ScanRangeFor(1)); // nobody else's

        // In play: the recharge after a scan (no probe used: the scanners were ready) and the sweep on arrival.
        var ship = sys.FlagshipOf(0)!;
        Do(with, new LongRangeScanCommand(0, ship.Id));
        Assert.Equal(with.Tick - 1 + sys.ScanCooldownTicksFor(0), ship.ScanReadyTick);
        Assert.Equal(1, with.FitOutProbes(0)); // the base load's probe is still aboard
        var target = sys.Bodies.First(b => b.Id != ship.AtBody && sys.KnowledgeOf(0, b.Id) < BodyKnowledge.Charted).Id;
        GoTo(with, target);
        Assert.Equal(sys.SweepTicksFor(0), ship.SweepDoneTick - ship.SweepStartTick);
        // The scan reaches the bodies within 400 of the ship.
        var at = sys.ShipPosition(ship, with.Tick);
        Assert.Equal(sys.Bodies.Where(b => FixVec2.Distance(b.Position, at) <= Fix64.FromInt(400)).Select(b => b.Id), sys.ScanTargets(ship));
    }

    [Fact]
    public void EngineeringReactionMassAndTheBuilderShortenFlights()
    {
        var none = Game("armstrong", null);
        var plain = none.System!;
        var ship = plain.FlagshipOf(0)!;
        var to = plain.Bodies.First(b => b.Id != ship.AtBody).Id;
        var ticks = plain.TravelTicksFor(ship, ship.AtBody!, to);
        // Armstrong, Builder preset: 3 of 6 engineers (50%: strength +0.1), the perk x 0.92, two reaction mass x 0.96.
        var fit = FitOutRules.Preset(Real, "armstrong", "builder")!;
        var mass = FitOutRules.CargoCount(fit, "mass");
        var factor = FitOutRules.Factor(Real, "armstrong", fit, FitOutRules.FlightTime);
        Near(0.9 * 0.92 * (1 - 0.02 * mass), factor, 1e-4);
        var with = Game("armstrong", fit);
        var fitted = with.System!.FlagshipOf(0)!;
        var expected = (Fix64.FromInt((int)ticks) * factor).FloorToInt();
        Assert.Equal(expected, with.System.TravelTicksFor(fitted, fitted.AtBody!, to));
        Do(with, new SetCourseCommand(0, fitted.Id, to));
        Assert.Equal(expected, fitted.ArriveTick - fitted.DepartTick);
        // Neutral traffic flies as it always did.
        foreach (var s in with.System.Ships.Where(s => s.Owner < 0)) Assert.Equal(Fix64.One, with.FitOutFactor(s.Owner, FitOutRules.FlightTime));
    }

    [Fact]
    public void ScienceAndTheScientistStudyFasterAndGiftsSoftenTheShock()
    {
        var (seed, body) = CivSeed();
        Fix64 Rate(FitOut? fit)
        {
            var sim = Game("meridian", fit, seed, body);
            var sys = sim.System!;
            Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, body)); // its start orbit is known from the start
            Steps(sim, 30);
            var a = sys.StudyOf(0, body);
            Steps(sim, 300);
            return sys.StudyOf(0, body) - a;
        }
        var fit = FitOutRules.Preset(Real, "meridian", "scientist")!;
        var factor = FitOutRules.Factor(Real, "meridian", fit, FitOutRules.StudyTime);
        Near((1 - FitOutRules.Strength(Real, "meridian", fit, "science").ToDouble()) * 0.75, factor, 1e-4); // the perk: a quarter less
        Near(Rate(null).ToDouble() / factor.ToDouble(), Rate(fit), 1e-3);

        // Culture shock: the chance x the shock_chance lever, then 3 points off for each case of medicine and gifts.
        int Shock(FitOut? f)
        {
            var sim = Game("meridian", f, seed, body);
            Do(sim, new DebugCivCommand(0, 0, body, DebugCivField.Level, Fix64.FromInt(5)));
            Do(sim, new DebugCivCommand(0, 0, body, DebugCivField.Temperament, Fix64.FromInt((int)Temperament.Wary)));
            Do(sim, new DebugCivCommand(0, 0, body, DebugCivField.FinishStudy, Fix64.Zero));
            Do(sim, new FirstContactCommand(0, body));
            return sim.System!.ContactEventOf(0, body)!.ShockChance;
        }
        var c = Real.Civilizations!.Contact!;
        var plain = Shock(null);
        Assert.InRange(plain, c.ShockMin + 1, c.ShockMax - 15); // not at a clamp, so the arithmetic below shows
        var gifts = FitOutRules.CargoCount(fit, "gifts");
        Assert.True(gifts > 0);
        var shockFactor = FitOutRules.Factor(Real, "meridian", fit, FitOutRules.ShockChance);
        Assert.True(shockFactor < Fix64.One);
        Assert.Equal(System.Math.Clamp((Fix64.FromInt(plain) * shockFactor + Fix64.Half).FloorToInt() - 3 * gifts, c.ShockMin, c.ShockMax), Shock(fit));
        Assert.Equal(plain, Shock(Even("meridian", ("gifts", 0), ("post", 1)))); // an even split and no gifts: as today
    }

    [Fact]
    public void DefenseAndTheCombatantNeedLessTimeAlongside()
    {
        var watch = Real.Missions!.List.First(m => m.Chain == "watch_station" && m.Stages.Any(s => s.Objectives.Any(o => o.HoldSeconds > 0 && o.Cond.Kind == "with_ship")));
        var objective = watch.Stages.SelectMany(s => s.Objectives).First(o => o.HoldSeconds > 0 && o.Cond.Kind == "with_ship");
        Assert.Equal(30, objective.HoldSeconds);
        Assert.Equal(30, Game("bastion", null).Missions.HoldSecondsOf(0, objective));
        // Combatant preset: the perk (x 2/3: 20 s) and 45% Defense (x 0.92): 18 s.
        var fit = FitOutRules.Preset(Real, "bastion", "combatant")!;
        var factor = FitOutRules.Factor(Real, "bastion", fit, FitOutRules.AlongsideTime);
        Near(0.6667 * (1 - FitOutRules.Strength(Real, "bastion", fit, "defense").ToDouble()), factor, 1e-4);
        var with = Game("bastion", fit);
        Assert.Equal((Fix64.FromInt(30) * factor + Fix64.Half).FloorToInt(), with.Missions.HoldSecondsOf(0, objective));
        Assert.Equal(18, with.Missions.HoldSecondsOf(0, objective));
        // Other holds (ship_at) don't change.
        var shipAt = Real.Missions.List.SelectMany(m => m.Stages).SelectMany(s => s.Objectives).FirstOrDefault(o => o.HoldSeconds > 0 && o.Cond.Kind == "ship_at");
        if (shipAt != null) Assert.Equal(shipAt.HoldSeconds, with.Missions.HoldSecondsOf(0, shipAt));
        // In the running mission the objective's target reads the same.
        Assert.Null(with.Missions.Enable("watch_station"));
        with.Step();
        Do(with, new DebugMissionCommand(0, 0, watch.Id, DebugMissionAction.Start, "", "", -1));
        var stage = watch.Stages.First(s => s.Objectives.Contains(objective));
        Do(with, new DebugMissionCommand(0, 0, watch.Id, DebugMissionAction.GotoStage, stage.Id, "", -1));
        var view = with.Missions.For(0).First(v => v.Id == watch.Id);
        Assert.Equal(Fix64.FromInt(18), view.Objectives.First(o => o.Id == objective.Id).Target);
    }

    // ---------------------------------------------------------------- what the hold brings

    [Fact]
    public void BodySatellitesFromTheHoldCostNothingUntilTheyRunOut()
    {
        var fit = Even("tereshkova", ("ssat", 1), ("gifts", 1));
        var sim = Game("tereshkova", fit);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        Assert.Equal(1, sim.FitOutFreeSystemSatellites(0));
        Zero(sim);
        Do(sim, new SystemSatelliteCommand(0, ship.AtBody!)); // free, with an empty stock
        Assert.Equal(0, sim.FitOutFreeSystemSatellites(0));
        var next = sys.Bodies.First(b => b.Id != ship.AtBody).Id;
        GoTo(sim, next);
        Assert.Contains("not enough resources", Refused(sim, new SystemSatelliteCommand(0, next)));
        // With stock again it costs what it always did.
        var cost = Real.SystemRules!.SatelliteCost;
        Do(sim, new DebugStockCommand(0, 0, cost.Select(c => new ResourceAmount(c.Resource, c.Amount + c.Amount)).ToList()));
        Do(sim, new SystemSatelliteCommand(0, next));
        foreach (var c in cost) Assert.Equal(c.Amount, sim.Players[0].Stock[c.Resource]);
        Assert.Equal(2, sys.Satellites.Count(s => s.Owner == 0));
    }

    [Fact]
    public void AListeningPostFromTheHoldCostsNothing()
    {
        var (seed, body) = CivSeed();
        var cost = Real.Civilizations!.Post.Cost;
        foreach (var posts in new[] { 1, 0 })
        {
            var sim = Game("meridian", Even("meridian", ("post", posts), ("gifts", 1)), seed, body);
            Assert.Equal(posts, sim.FitOutFreePosts(0));
            var before = cost.Select(c => sim.Players[0].Stock[c.Resource]).ToList();
            Assert.Null(sim.System!.PostProblem(0, body));
            Do(sim, new ObservationPostCommand(0, body));
            Assert.Equal(0, sim.FitOutFreePosts(0));
            for (var i = 0; i < cost.Count; i++)
                Assert.Equal(posts > 0 ? before[i] : before[i] - cost[i].Amount, sim.Players[0].Stock[cost[i].Resource]);
        }
        // No post aboard and an empty stock: refused as always.
        var poor = Game("meridian", Even("meridian", ("gifts", 1)), seed, body);
        Zero(poor);
        Assert.Contains("not enough resources", Refused(poor, new ObservationPostCommand(0, body)));
    }

    [Fact]
    public void SensorProbesScanWithoutTheRechargeWaitUntilTheyRunOut()
    {
        var sim = Game("tereshkova", Even("tereshkova", ("probe", 2)));
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        Assert.False(sys.ScanUsesProbe(0, ship.Id));
        Do(sim, new LongRangeScanCommand(0, ship.Id)); // the scanners were ready: no probe
        var ready = ship.ScanReadyTick;
        Assert.Equal(2, sim.FitOutProbes(0));
        Assert.True(sys.ScanUsesProbe(0, ship.Id));
        Assert.Null(sys.ScanProblem(0, ship.Id));
        Do(sim, new LongRangeScanCommand(0, ship.Id)); // recharging: a probe scans
        Assert.Equal((1, ready), (sim.FitOutProbes(0), ship.ScanReadyTick)); // the recharge runs on as it was
        Assert.Equal(sim.Tick - 1, ship.LastScanTick);
        Do(sim, new LongRangeScanCommand(0, ship.Id));
        Assert.Equal(0, sim.FitOutProbes(0));
        Assert.Contains("recharging", Refused(sim, new LongRangeScanCommand(0, ship.Id)));
        Assert.False(sys.ScanUsesProbe(0, ship.Id));
        // Without a fit-out the second scan is refused, as it always was.
        var plain = Game("tereshkova", null);
        Do(plain, new LongRangeScanCommand(0, plain.System!.FlagshipOf(0)!.Id));
        Assert.Contains("recharging", Refused(plain, new LongRangeScanCommand(0, plain.System.FlagshipOf(0)!.Id)));
    }

    [Fact]
    public void WhatIsLeftAboardIsHashedAndTheSameFitOutTwiceIsTheSameGame()
    {
        var fit = Even("tereshkova", ("probe", 2), ("ssat", 1));
        var a = Game("tereshkova", fit);
        var b = Game("tereshkova", fit);
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var g in new[] { a, b }) Do(g, new LongRangeScanCommand(0, g.System!.FlagshipOf(0)!.Id));
        Assert.Equal(a.StateHash(), b.StateHash());
        // A probe used in one game and not the other: the hashes part (the probe count is hashed).
        Do(a, new LongRangeScanCommand(0, a.System!.FlagshipOf(0)!.Id));
        b.Step();
        Assert.Equal(1, a.FitOutProbes(0));
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
