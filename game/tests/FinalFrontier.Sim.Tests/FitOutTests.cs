using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Fit Out Your Ship (docs/fit-out-contract.md, data/missions/fit_out.json): the data and its checks, the presets, what the
/// rules refuse, the landing stock and vehicles, every lever moving the right way by the data's amount, the perks and the
/// cargo that acts in play, and the promise that a game without a fit-out is the game it always was.
/// </summary>
public class FitOutTests
{
    private static GameData Real => TestData.Real;
    private const ulong Seed = 1701;
    private const string File = "missions/fit_out.json";
    private static readonly string[] Ships = { "horizon", "cochrane" };
    private static readonly string[] Focuses = { "explorer", "builder", "combatant", "scientist", "allround" };

    private static Fix64 F(int v) => Fix64.FromInt(v);
    private static Fix64 P(string v) => Fix64.Parse(v);
    private static int Sec(int s) => s * Simulation.TickRate;
    private static int R(string id) => Real.Resource(id).Index;

    private static void Near(double expected, Fix64 actual, double within = 1e-6) =>
        Assert.True(System.Math.Abs(expected - actual.ToDouble()) <= within, $"{actual.ToDouble()} is not {expected}");

    // ---------------------------------------------------------------- helpers

    private static FitOut Preset(string ship, string focus) => FitOutRules.Preset(Real, ship, focus)!;

    /// <summary>A lean legal load (18 containers): no medical stores, so only the crew moves the sickness lever.</summary>
    private static Dictionary<string, int> Cargo(params (string Line, int Count)[] more)
    {
        var cargo = new Dictionary<string, int> { ["sat"] = 1, ["mat"] = 8, ["lab"] = 2, ["armour"] = 2, ["fuel"] = 4 };
        foreach (var (line, count) in more) cargo[line] = count;
        return cargo;
    }

    /// <summary>The Horizon (120 crew) with 84 heads in one division (70%: strength +0.18) and 12 in each other (10%: -0.06),
    /// under that division's focus.</summary>
    private static FitOut Heavy(string division, params (string Line, int Count)[] cargo)
    {
        var rules = Real.FitOut!;
        var fit = new FitOut(rules.Focuses.First(f => f.Division == division).Id, rules.Divisions.Select(d => d.Id == division ? 84 : 12).ToArray(), Cargo(cargo));
        Assert.Null(FitOutRules.Problem(Real, "horizon", fit));
        return fit;
    }

    private static Simulation Game(string ship, FitOut? fit, GameData? data = null, ulong seed = Seed) =>
        MatchSetup.CreateSystemGame(data ?? Real, seed, "federation", null, ship, fit);

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static void Steps(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    /// <summary>Lands on the first site and waits for the hub.</summary>
    private static SurfaceStructure Land(Simulation sim)
    {
        var s = sim.Surface!;
        Do(sim, new LandColonyCommand(0, Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t))));
        Steps(sim, TestData.ReadyTicks(sim.Data));
        StepUntil(sim, () => s.Bases(0).Count == 1 && s.Bases(0)[0].State == StructureState.Complete, Sec(60)); // (a longer descent off class M)
        var hub = s.Bases(0).Single();
        Assert.Equal(StructureState.Complete, hub.State);
        TestData.NoStockTargets(sim);
        return hub;
    }

    /// <summary>The Horizon without a fit-out and with this one, same seed, both landed (hubs out).</summary>
    private static (Simulation None, Simulation With) Pair(FitOut fit, out SurfaceStructure hubNone, out SurfaceStructure hubWith, GameData? data = null, ulong seed = Seed)
    {
        var none = Game("horizon", null, data, seed);
        var with = Game("horizon", fit, data, seed);
        Assert.NotNull(with.FitOutOf(0));
        hubNone = Land(none);
        hubWith = Land(with);
        Assert.Equal(hubNone.Tile, hubWith.Tile);
        return (none, with);
    }

    private static int FreeTile(Simulation sim, SurfaceStructure hub, string building, int skip = 0) =>
        sim.Surface!.Map.Grid.Within(hub.Tile, 3).Where(t => sim.Surface.CheckPlacement(sim, 0, building, t) == PlacementProblem.None).Skip(skip).First();

    /// <summary>A finished building beside the hub (test panel: free, at once).</summary>
    private static SurfaceStructure Place(Simulation sim, SurfaceStructure hub, string building, int skip = 0)
    {
        var tile = FreeTile(sim, hub, building, skip);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, building, tile, 0));
        return sim.Surface!.StructureAt(tile)!;
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void TheDataLoadsAndPasses()
    {
        var f = Real.FitOut!;
        Assert.Equal(new[] { "exploration", "engineering", "science", "defense" }, f.Divisions.Select(d => d.Id));
        Assert.Equal(Focuses, f.Focuses.Select(x => x.Id));
        Assert.Equal((P("0.10"), P("0.30"), P("0.004")), (f.CrewFloorShare, f.FocusFloorShare, f.EffectPerPoint));
        Assert.Equal(F(25), f.EvenSharePct);
        Assert.Equal(new[] { "duranium", "tritanium", "deuterium" }, f.ReplacesStartStock);
        Assert.Equal(new[] { ("horizon", 120, 30, "allround"), ("cochrane", 210, 24, "builder") }, f.Ships.Take(2).Select(s => (s.ShipId, s.Crew, s.Hold, s.Suggest)));
        Assert.Equal(new[] { ("exploration", 2), ("engineering", 8), ("science", 2), ("defense", 2), ("ship", 4) }, f.Groups.Select(g => (g.Id, g.Floor)));
        Assert.Equal(new[] { "sat", "drone", "rover", "mat", "kits", "crawler", "lab", "med", "armour", "fuel", "prov" }, f.LinesFor(false).Select(l => l.Id));
        Assert.Equal((1, 6, 20), (f.Line("mat")!.Containers, f.Line("mat")!.Min, f.Line("mat")!.Max));
        Assert.Equal(new[] { new FitOutStock("duranium", 50), new FitOutStock("tritanium", 30) }, f.Line("mat")!.Stock);
        Assert.Equal(("mining_crawler", 4), (f.Line("crawler")!.Unit, f.Line("crawler")!.Containers));
        Assert.Equal((1, 20), (f.Line("sat")!.Satellites, f.Line("lab")!.Research));
        Assert.Null(f.Focus("allround")!.Division);
        Assert.Equal("", f.Focus("allround")!.PerkText);
        Assert.Equal(5, f.Focus("explorer")!.LandingReveal);
        // Every lever the data lists is one the game reads, the 14 crew levers have a division, and the game reads no others.
        Assert.Equal(FitOutRules.KnownLevers.OrderBy(x => x), f.Levers.Select(l => l.Id).OrderBy(x => x));
        Assert.Equal(14, f.LeversFor(false).Count(l => l.Division.Length > 0));
        Assert.True(FitOutRules.Applies(Real, "horizon") && FitOutRules.Applies(Real, "cochrane"));
        foreach (var ship in new[] { "galaxy", null }) // (the four ships that never land are fitted out since phase 2: FitOutSpaceTests)
        {
            Assert.False(FitOutRules.Applies(Real, ship));
            if (ship != null) Assert.Null(FitOutRules.Preset(Real, ship, "explorer"));
        }
        Assert.Null(FitOutRules.Preset(Real, "horizon", "miner"));
        Assert.Empty(Real.Validate());
    }

    [Theory]
    [InlineData("\"id\": \"drive_speed\"", "\"id\": \"warp_speed\"", "reads no such lever")]
    [InlineData("\"crew_pct\": [45, 25, 18, 12]", "\"crew_pct\": [45, 25, 18, 13]", "sum to 100")]
    [InlineData("\"crew_pct\": [45, 25, 18, 12]", "\"crew_pct\": [45, 25, 30]", "one share for each of the 4 divisions")]
    [InlineData("\"stock\": { \"deuterium\": 25 }", "\"stock\": { \"unobtainium\": 25 }", "unknown resource 'unobtainium'")]
    [InlineData("\"stock\": { \"deuterium\": 25 }", "\"stock\": { \"crew\": 1 }", "no cargo line gives Deuterium")]
    [InlineData("\"stock\": { \"crew\": 5 }", "\"stock\": { \"energy\": 5 }", "never energy")]
    [InlineData("\"unit\": \"mining_crawler\"", "\"unit\": \"hover_tank\"", "unknown surface unit 'hover_tank'")]
    [InlineData("\"unit\": \"survey_rover\"", "\"unit\": \"scout_drone\"", "flies from a pad")]
    [InlineData("\"name\": \"Defense stores\", \"floor\": 2", "\"name\": \"Defense stores\", \"floor\": 9", "can't fill its floor")]
    [InlineData("\"group\": \"defense\", \"containers\": 1", "\"group\": \"weapons\", \"containers\": 1", "unknown cargo group 'weapons'")]
    [InlineData("\"levers\": { \"sickness\": -0.08 }", "\"levers\": { \"sickness\": -0.3 }", "would take the lever to 0")]
    [InlineData("\"perk_levers\": { \"hub_workshop\": 1.3 }", "\"perk_levers\": { \"hub_workshop\": 0 }", "must be above 0")]
    [InlineData("\"perk_levers\": { \"hub_workshop\": 1.3 }", "\"perk_levers\": { \"warp_core\": 1.3 }", "'warp_core' is not one of the levers")]
    [InlineData("\"ship\": \"cochrane\"", "\"ship\": \"bastion\"", "base: mat is cargo for a colony ship")]
    [InlineData("\"ship\": \"cochrane\"", "\"ship\": \"galaxy\"", "no such ship")]
    [InlineData("\"suggest\": \"builder\"", "\"suggest\": \"miner\"", "unknown focus 'miner'")]
    [InlineData("\"focus_floor_share\": 0.30", "\"focus_floor_share\": 0.05", "crew_floor_share <= focus_floor_share")]
    [InlineData("\"effect_per_point\": 0.004", "\"effect_per_point\": 0.02", "could reach 0")]
    [InlineData("\"satellites\": 1", "\"satellites\": 2", "more ready satellites than a player may have up")]
    // A preset the rules would refuse: a base its fill order doesn't bring up to a group's floor, a hold the base doesn't fit,
    // floors that leave no room for a focus's own percentages.
    [InlineData("\"hold\": 30, \"suggest\": \"allround\",\n        \"base\": { \"sat\": 1, \"mat\": 8,", "\"hold\": 30, \"suggest\": \"allround\",\n        \"base\": { \"sat\": 1, \"mat\": 5,", "the Explorer preset is not a legal fit-out: Engineering stores needs at least 8 containers")]
    [InlineData("\"hold\": 24", "\"hold\": 12", "preset is not a legal fit-out: the hold takes 12 containers")]
    [InlineData("\"crew_floor_share\": 0.10", "\"crew_floor_share\": 0.28", "preset is not a legal fit-out")]
    public void BadFitOutDataIsReported(string from, string to, string problem)
    {
        var data = TestData.Patched((File, from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    [Fact]
    public void ADataSetWithoutTheBlockHasNoFitOuts()
    {
        var data = TestData.Patched((File, "\"fit_out\": {", "\"_fit_out_off\": {"));
        Assert.Null(data.FitOut);
        Assert.Empty(data.Validate());
        Assert.False(FitOutRules.Applies(data, "horizon"));
        Assert.Null(FitOutRules.Preset(data, "horizon", "explorer"));
        Assert.Null(data.LeanestFitOutStock());
        // A fit-out handed to such a game is ignored: it is the 5-argument game.
        var plain = MatchSetup.CreateSystemGame(data, Seed, "federation", null, "horizon");
        var given = MatchSetup.CreateSystemGame(data, Seed, "federation", null, "horizon", Preset("horizon", "explorer"));
        Assert.Null(given.FitOutOf(0));
        Assert.Equal(plain.StateHash(), given.StateHash());
    }

    // ---------------------------------------------------------------- presets and refusals

    [Fact]
    public void EveryPresetIsLegalOnBothShips()
    {
        var f = Real.FitOut!;
        foreach (var ship in f.Ships)
            foreach (var focus in f.Focuses)
            {
                var fit = FitOutRules.Preset(Real, ship.ShipId, focus.Id)!;
                var space = FitOutRules.IsSpace(Real, ship.ShipId);
                Assert.Null(FitOutRules.Problem(Real, ship.ShipId, fit));
                Assert.Equal(focus.Id, fit.Focus);
                Assert.Equal(ship.Crew, fit.Crew.Sum());
                Assert.InRange(FitOutRules.ContainersUsed(Real, fit), space ? 3 : 18, ship.Hold);
                Assert.Equal(f.LinesFor(space).Select(l => l.Id).OrderBy(x => x), fit.Cargo.Keys.OrderBy(x => x)); // every line of its kind, 0 included
                for (var d = 0; d < f.Divisions.Count; d++)
                    Assert.True(fit.Crew[d] >= FitOutRules.CrewFloor(Real, ship.ShipId, focus.Id, f.Divisions[d].Id));
                // A preset's crew is within a head of the focus's percentages.
                for (var d = 0; d < f.Divisions.Count; d++)
                    Assert.InRange(fit.Crew[d] * 100.0 / ship.Crew, focus.CrewPct[d] - 100.0 / ship.Crew, focus.CrewPct[d] + 100.0 / ship.Crew);
            }
    }

    [Fact]
    public void PresetsFollowTheDatasPercentagesAndFillOrder()
    {
        Assert.Equal(new[] { 30, 30, 30, 30 }, Preset("horizon", "allround").Crew);
        Assert.Equal(new[] { 54, 30, 22, 14 }, Preset("horizon", "explorer").Crew); // 21.6 and 14.4: the 0.6 gets the spare head
        Assert.Equal(new[] { 42, 95, 42, 31 }, Preset("cochrane", "builder").Crew); // 94.5 and 31.5: the first of the two
        Assert.Equal(new[] { 53, 53, 52, 52 }, Preset("cochrane", "allround").Crew); // 52.5 each
        int N(FitOut fit, string line) => FitOutRules.CargoCount(fit, line);

        // Horizon, All-rounder: the base load (18 containers), then 12 from the fill order.
        var all = Preset("horizon", "allround");
        Assert.Equal((1, 2, 0, 11, 2, 0), (N(all, "sat"), N(all, "drone"), N(all, "rover"), N(all, "mat"), N(all, "kits"), N(all, "crawler")));
        Assert.Equal((2, 1, 3, 6, 1), (N(all, "lab"), N(all, "med"), N(all, "armour"), N(all, "fuel"), N(all, "prov")));
        Assert.Equal(30, FitOutRules.ContainersUsed(Real, all));
        Assert.Equal(11 + 2, FitOutRules.ContainersUsed(Real, all, "engineering"));

        // Horizon, Builder: the crawler (4 containers) fits; in Cochrane's smaller hold (6 free) it never does.
        var builder = Preset("horizon", "builder");
        Assert.Equal((12, 3, 1, 5), (N(builder, "mat"), N(builder, "kits"), N(builder, "crawler"), N(builder, "fuel")));
        Assert.Equal(30, FitOutRules.ContainersUsed(Real, builder));
        var tender = Preset("cochrane", "builder");
        Assert.Equal((12, 2, 0, 4), (N(tender, "mat"), N(tender, "kits"), N(tender, "crawler"), N(tender, "fuel")));
        Assert.Equal(24, FitOutRules.ContainersUsed(Real, tender));
        // Cochrane, All-rounder lands with exactly the stock of a game with no fit-out: 500 duranium, 300 tritanium, 150 deuterium.
        var even = FitOutRules.LandingStock(Real, "cochrane", Preset("cochrane", "allround"));
        Assert.Equal((500, 300, 150), (even["duranium"], even["tritanium"], even["deuterium"]));
    }

    [Fact]
    public void FloorsAndBudgetsRefuseABadFitOut()
    {
        string? Why(string ship, string focus, int[] crew, params (string Line, int Count)[] cargo) =>
            FitOutRules.Problem(Real, ship, new FitOut(focus, crew, Cargo(cargo)));
        var even = new[] { 30, 30, 30, 30 };
        Assert.Null(Why("horizon", "allround", even));

        // Crew: every division 10% (12 of 120, 21 of 210), the focus's own 30% (36, 63), everyone assigned.
        Assert.Equal((12, 36, 21, 63), (FitOutRules.CrewFloor(Real, "horizon", "allround", "science"), FitOutRules.CrewFloor(Real, "horizon", "scientist", "science"),
            FitOutRules.CrewFloor(Real, "cochrane", "explorer", "defense"), FitOutRules.CrewFloor(Real, "cochrane", "combatant", "defense")));
        Assert.Equal(0, FitOutRules.CrewFloor(Real, "galaxy", "combatant", "defense"));
        Assert.Equal("Science needs at least 12 crew", Why("horizon", "allround", new[] { 40, 40, 11, 29 }));
        Assert.Equal("Exploration needs at least 36 crew", Why("horizon", "explorer", even));
        Assert.Null(Why("horizon", "explorer", new[] { 36, 30, 30, 24 }));
        Assert.Equal("1 of the crew is still unassigned", Why("horizon", "allround", new[] { 30, 30, 30, 29 }));
        Assert.Equal("20 of the crew are still unassigned", Why("horizon", "allround", new[] { 25, 25, 25, 25 }));
        Assert.Equal("121 crew assigned, and the ship has 120", Why("horizon", "allround", new[] { 31, 30, 30, 30 }));
        Assert.Contains("one number for each of the 4 divisions", Why("horizon", "allround", new[] { 60, 60 }));
        Assert.Equal("unknown focus 'miner'", Why("horizon", "miner", even));
        Assert.Equal("the galaxy is not fitted out", Why("galaxy", "combatant", even));
        Assert.Equal("Defense needs at least 21 crew", Why("cochrane", "allround", new[] { 70, 70, 50, 20 }));

        // Cargo: each line's min and max, each group's floor, the hold.
        Assert.Equal("Building materials: at least 6 must be loaded", Why("horizon", "allround", even, ("mat", 5)));
        Assert.Equal("Building materials: at most 20 can be loaded", Why("horizon", "allround", even, ("mat", 21)));
        Assert.Equal("Extra survey rover: at most 2 can be loaded", Why("horizon", "allround", even, ("rover", 3)));
        Assert.Equal("Fuel: at least 4 must be loaded", Why("horizon", "allround", even, ("fuel", 0)));
        Assert.Equal("Scout drone: the count can't be below 0", Why("horizon", "allround", even, ("drone", -1)));
        Assert.Equal("Defense stores needs at least 2 containers (it has 1)", Why("horizon", "allround", even, ("armour", 1)));
        Assert.Equal("Exploration gear needs at least 2 containers (it has 1)", Why("horizon", "allround", even, ("sat", 0), ("drone", 1)));
        Assert.Null(Why("horizon", "allround", even, ("sat", 0), ("drone", 2)));
        Assert.Equal("Engineering stores needs at least 8 containers (it has 7)", Why("horizon", "allround", even, ("mat", 7)));
        Assert.Null(Why("horizon", "allround", even, ("mat", 6), ("kits", 2))); // the floor is the group's, not one line's
        Assert.Equal("the hold takes 30 containers, and this load needs 31", Why("horizon", "allround", even, ("mat", 20), ("prov", 1)));
        Assert.Null(Why("horizon", "allround", even, ("mat", 20)));
        Assert.Equal("the hold takes 24 containers, and this load needs 30", Why("cochrane", "builder", new[] { 42, 95, 42, 31 }, ("mat", 20)));
        Assert.Equal("unknown cargo 'guard'", Why("horizon", "allround", even, ("guard", 1)));
        // The hold need not be full, and a line left out counts as 0.
        Assert.Null(FitOutRules.Problem(Real, "horizon", new FitOut("allround", even, Cargo())));
        Assert.Equal(18, FitOutRules.ContainersUsed(Real, new FitOut("allround", even, Cargo())));
    }

    [Fact]
    public void ABadFitOutIsReplacedByTheShipsSuggestedPreset()
    {
        foreach (var ship in Ships)
        {
            var bad = new FitOut("explorer", new[] { 1, 1, 1, 1 }, new Dictionary<string, int> { ["mat"] = 99 });
            Assert.NotNull(FitOutRules.Problem(Real, ship, bad));
            var sim = Game(ship, bad);
            var suggested = Preset(ship, Real.FitOut!.Ship(ship)!.Suggest);
            var got = sim.FitOutOf(0)!;
            Assert.Equal(suggested.Focus, got.Focus);
            Assert.Equal(suggested.Crew, got.Crew);
            foreach (var line in Real.FitOut.Cargo) Assert.Equal(FitOutRules.CargoCount(suggested, line.Id), FitOutRules.CargoCount(got, line.Id));
            Assert.Equal(Game(ship, suggested).StateHash(), sim.StateHash());
        }
    }

    // ---------------------------------------------------------------- no fit-out = the game as it was

    [Theory]
    [InlineData(null)]
    [InlineData("horizon")]
    [InlineData("cochrane")]
    [InlineData("tereshkova")]
    [InlineData("bastion")]
    public void NoFitOutIsTheGameItAlwaysWas(string? ship)
    {
        // The 5-argument game, the 6-argument game with no fit-out, and (for a ship the data doesn't fit out, or no ship) the
        // 6-argument game handed one anyway: the same hashes through the landing, a satellite and four minutes of play.
        var games = new List<Simulation>
        {
            MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship),
            MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship, null),
        };
        if (!FitOutRules.Applies(Real, ship)) games.Add(MatchSetup.CreateSystemGame(Real, Seed, "federation", null, ship, Preset("horizon", "explorer")));
        var colonizes = games[0].CanColonize(0);
        var site = Enumerable.Range(0, games[0].Surface!.Map.Grid.Count).First(t => games[0].Surface!.CanLand(0, t));
        for (var tick = 0; tick < Sec(240); tick++)
        {
            foreach (var g in games)
            {
                if (tick == 5 && colonizes) g.Schedule(g.Tick, new LandColonyCommand(0, site));
                if (tick == Sec(60)) g.Schedule(g.Tick, new SatelliteCommand(0, 20, 3));
                if (tick == Sec(70) && colonizes) g.Schedule(g.Tick, new MoveUnitsCommand(0, g.Surface!.Units.Select(u => u.Id).ToList(), g.Surface.Map.Grid.Within(site, 4).Last()));
                g.Step();
            }
            if (tick % 30 != 0 && tick != Sec(240) - 1) continue;
            var hash = games[0].StateHash();
            foreach (var g in games) Assert.Equal(hash, g.StateHash());
        }
        foreach (var g in games)
        {
            Assert.Null(g.FitOutOf(0));
            Assert.Equal(0, g.FitOutFreeSatellites(0));
            Assert.Equal(0, g.FitOutLandingReveal(0));
            Assert.Empty(g.FitOutUnits(0));
            foreach (var lever in FitOutRules.KnownLevers) Assert.Equal(Fix64.One, g.FitOutFactor(0, lever));
        }
        Assert.Equal(Fix64.One, FitOutRules.Factor(Real, "horizon", null, FitOutRules.DriveSpeed));
        Assert.Equal(Fix64.One, FitOutRules.Factor(Real, null, Preset("horizon", "explorer"), FitOutRules.DriveSpeed));
        Assert.Equal(Fix64.One, FitOutRules.Factor(Real, "bastion", Preset("horizon", "explorer"), FitOutRules.DriveSpeed));
    }

    [Fact]
    public void TheSurfaceHelpersWithAnOwnerAgreeWithTheOldOnesWithoutAFitOut()
    {
        var sim = Game("horizon", null);
        var s = sim.Surface!;
        foreach (var b in Real.BuildMenu)
        {
            Assert.Equal(s.BuildTicksOf(b), s.BuildTicksOf(b, 0));
            foreach (var level in Real.Building(b).Levels) Assert.Equal(s.LevelTicksOf(level), s.LevelTicksOf(level, 0));
        }
    }

    // ---------------------------------------------------------------- strength, levers, perks

    [Fact]
    public void AnEvenSplitHasNoStrengthAndAPresetAboutEightPercent()
    {
        var even = Preset("horizon", "allround");
        foreach (var d in Real.FitOut!.Divisions) Assert.Equal(Fix64.Zero, FitOutRules.Strength(Real, "horizon", even, d.Id));
        foreach (var lever in Real.FitOut.Levers.Where(l => l.Division.Length > 0 && l.Id != FitOutRules.Sickness))
            Assert.Equal(Fix64.One, FitOutRules.Factor(Real, "horizon", even, lever.Id)); // (its one case of medical stores moves sickness)
        // Explorer on the Horizon: 54 of 120 = 45%, 20 points over an even quarter: +8%; Defense at 14 heads: about -5%.
        var explorer = Preset("horizon", "explorer");
        Near(0.08, FitOutRules.Strength(Real, "horizon", explorer, "exploration"));
        Near(0.004 * (100.0 * 14 / 120 - 25), FitOutRules.Strength(Real, "horizon", explorer, "defense"));
        // The most (everyone else at 10%) is +18%, the least -6%.
        var heavy = Heavy("science");
        Near(0.18, FitOutRules.Strength(Real, "horizon", heavy, "science"));
        Near(-0.06, FitOutRules.Strength(Real, "horizon", heavy, "defense"));
        Assert.Equal(Fix64.Zero, FitOutRules.Strength(Real, "horizon", heavy, "weapons"));
        Assert.Equal(Fix64.One, FitOutRules.Factor(Real, "horizon", heavy, "warp_speed"));
    }

    public static IEnumerable<object[]> CrewLevers() => TestData.Real.FitOut!.LeversFor(false).Where(l => l.Division.Length > 0).Select(l => new object[] { l.Id });

    [Theory]
    [MemberData(nameof(CrewLevers))]
    public void EveryCrewLeverMovesTheRightWayByTheDatasAmount(string leverId)
    {
        var rules = Real.FitOut!;
        var lever = rules.Lever(leverId)!;
        var other = rules.Divisions.First(d => d.Id != lever.Division).Id;
        // 70% of the crew in its division: 0.004 x 45 points = 0.18 stronger; 10% (another division heavy): 0.06 weaker.
        var strong = FitOutRules.Factor(Real, "horizon", Heavy(lever.Division), leverId);
        var weak = FitOutRules.Factor(Real, "horizon", Heavy(other), leverId);
        Near(lever.MoreIsBetter ? 1.18 : 0.82, strong);
        Near(lever.MoreIsBetter ? 0.94 : 1.06, weak);
        // And the game reads the same number.
        var sim = Game("horizon", Heavy(lever.Division));
        Assert.Equal(strong, sim.FitOutFactor(0, leverId));
        Assert.Equal(Fix64.One, sim.FitOutFactor(1, leverId)); // nobody else's
    }

    [Fact]
    public void PerksAndCargoMoveTheirLevers()
    {
        Fix64 Factor(FitOut fit, string lever) => FitOutRules.Factor(Real, "horizon", fit, lever);
        var even = new[] { 30, 30, 30, 30 };
        FitOut Even(string focus, params (string Line, int Count)[] cargo) => new("allround", even, Cargo(cargo));

        // Field armour: 5% less hazard and wildlife damage each. Medical stores: sickness 8% shorter each.
        Near(0.90, Factor(Even("allround"), FitOutRules.HazardDamage)); // the lean load carries 2
        Near(0.80, Factor(Even("allround", ("armour", 4)), FitOutRules.WildlifeDamage));
        Near(1.0, Factor(Even("allround", ("armour", 0), ("med", 2)), FitOutRules.HazardDamage));
        Near(0.68, Factor(Even("allround", ("med", 4)), FitOutRules.Sickness));
        Near(0.82 * 0.76, Factor(Heavy("science", ("med", 3)), FitOutRules.Sickness)); // crew and cargo multiply

        // Perks. Combatant: vehicles take half damage from wildlife and ruin defences (on top of the armour).
        var combatant = Heavy("defense", ("armour", 4));
        Near(0.5 * 0.8, Factor(combatant, FitOutRules.WildlifeDamage));
        Near(0.5, Factor(combatant, FitOutRules.RuinVehicleDamage));
        Near(0.8, Factor(combatant, FitOutRules.HazardDamage)); // not hazards
        // Builder: the hub workshop 30% faster. Scientist: mission research +20%. Explorer: the landing reveals 5 hexes.
        Near(1.3, Factor(Preset("horizon", "builder"), FitOutRules.HubWorkshop));
        Near(1.2, Factor(Preset("horizon", "scientist"), FitOutRules.MissionResearch));
        Assert.Equal(5, FitOutRules.LandingReveal(Real, Preset("horizon", "explorer")));
        // The All-rounder has none, and nobody has another focus's.
        foreach (var focus in Focuses)
        {
            var fit = Preset("horizon", focus);
            Assert.Equal(focus == "builder" ? P("1.3") : Fix64.One, Factor(fit, FitOutRules.HubWorkshop));
            Assert.Equal(focus == "scientist" ? P("1.2") : Fix64.One, Factor(fit, FitOutRules.MissionResearch));
            Assert.Equal(focus == "combatant" ? P("0.5") : Fix64.One, Factor(fit, FitOutRules.RuinVehicleDamage));
            Assert.Equal(focus == "explorer" ? 5 : 0, FitOutRules.LandingReveal(Real, fit));
        }
        Assert.Equal(0, FitOutRules.LandingReveal(Real, null));
    }

    // ---------------------------------------------------------------- the landing

    [Fact]
    public void TheLandingStockComesFromTheHold()
    {
        // Horizon, All-rounder: 11 building materials, 6 fuel, 2 kit sets, 2 drones, 1 provisions; the ship's own +200
        // duranium and +30 crew on top; dilithium and latinum as always.
        var sim = Game("horizon", Preset("horizon", "allround"));
        Fix64 Has(string res) => sim.Players[0].Stock[R(res)];
        Assert.Equal(F(11 * 50 + 200), Has("duranium"));
        Assert.Equal(F(11 * 30), Has("tritanium"));
        Assert.Equal(F(6 * 25), Has("deuterium"));
        Assert.Equal(F(150 + 30 + 5), Has("crew"));
        Assert.Equal(F(2), Has("scout_drone"));
        foreach (var kit in new[] { "mechanical_spares", "electrical_spares", "rover_spares", "environmental_spares" }) Assert.Equal(F(2), Has(kit));
        var plain = Game("horizon", null);
        Assert.Equal(plain.Players[0].Stock[R("dilithium")], Has("dilithium"));
        Assert.Equal(plain.Players[0].Stock[R("latinum")], Has("latinum"));

        // Every preset on both ships: the page's summary (LandingStock) is the game's stock, resource by resource.
        foreach (var ship in Ships)
            foreach (var focus in Focuses)
            {
                var fit = Preset(ship, focus);
                var game = Game(ship, fit);
                var summary = FitOutRules.LandingStock(Real, ship, fit);
                foreach (var res in Real.Resources)
                    Assert.True(F(summary.TryGetValue(res.Id, out var n) ? n : 0) == game.Players[0].Stock[res.Index], $"{ship} {focus} {res.Id}");
                Assert.Equal(F(FitOutRules.ResearchBanked(Real, fit)), game.Surface!.DigResearchBank(0));
                Assert.Equal(FitOutRules.FreeSatellites(Real, fit), game.FitOutFreeSatellites(0));
            }
        // With no fit-out the summary is the old stock (start stock + the ship's bonus).
        var old = FitOutRules.LandingStock(Real, "cochrane", null!);
        var tender = Game("cochrane", null);
        foreach (var res in Real.Resources) Assert.Equal(F(old.TryGetValue(res.Id, out var n) ? n : 0), tender.Players[0].Stock[res.Index]);
    }

    [Theory]
    [InlineData("horizon")]
    [InlineData("cochrane")]
    public void EveryPresetLandsWithItsVehiclesAndReachesItsFirstBase(string ship)
    {
        foreach (var focus in Focuses)
        {
            var fit = Preset(ship, focus);
            var sim = Game(ship, fit);
            var hub = Land(sim);
            var s = sim.Surface!;
            Assert.True(s.UplinkOnline(0, sim.Tick));
            Assert.Equal(FitOutRules.LandingUnits(Real, ship, fit), s.Units.OrderBy(u => u.Id).Select(u => u.DefId));
            Assert.Equal(System.Math.Max(PlanetSurface.ConvoyShips, s.Units.Count), s.DropShips.Count); // each rides a convoy ship
            foreach (var u in s.Units) Assert.NotNull(s.DropShips.SingleOrDefault(d => d.UnitId == u.Id));
            // The base works: it can build, every drop ship gets back aboard, and nothing was refused.
            Do(sim, new PlaceBuildingCommand(0, "storage_container", FreeTile(sim, hub, "storage_container"), 0));
            StepUntil(sim, () => s.DropShips.Count == 0 && s.Structures.Any(x => x.DefId == "storage_container" && x.State == StructureState.Complete), Sec(600));
            Assert.Equal(0, sim.RefusedCommands);
        }
    }

    [Fact]
    public void ExtraVehiclesLandLikeAShipsStartUnits()
    {
        // The most the hold can add: two rovers and a crawler, on the Cochrane (which lands a construction rover anyway).
        var fit = new FitOut("explorer", new[] { 90, 50, 40, 30 }, Cargo(("sat", 0), ("rover", 2), ("crawler", 1), ("mat", 6)));
        Assert.Null(FitOutRules.Problem(Real, "cochrane", fit));
        Assert.Equal(new[] { "survey_rover", "survey_rover", "mining_crawler", "construction_rover", "survey_rover", "survey_rover", "mining_crawler" },
            FitOutRules.LandingUnits(Real, "cochrane", fit));
        Assert.Equal(new[] { "survey_rover", "survey_rover", "mining_crawler" }, FitOutRules.CargoUnits(Real, fit));
        var sim = Game("cochrane", fit);
        Assert.Equal(FitOutRules.CargoUnits(Real, fit), sim.FitOutUnits(0));
        Assert.Empty(sim.Surface!.Units); // nothing before the landing
        Land(sim);
        var s = sim.Surface!;
        Assert.Equal(FitOutRules.LandingUnits(Real, "cochrane", fit), s.Units.OrderBy(u => u.Id).Select(u => u.DefId));
        Assert.Equal(7, s.DropShips.Count(d => d.Role == DropShipRole.Convoy && d.UnitId != 0));
        Assert.Equal(7, s.Units.Select(u => u.Name).Distinct().Count()); // named like any unit
        StepUntil(sim, () => s.DropShips.Count == 0, Sec(600));
        // With no fit-out: the usual three and the construction rover.
        var plain = Game("cochrane", null);
        Land(plain);
        Assert.Equal(4, plain.Surface!.Units.Count);
    }

    [Fact]
    public void TheExplorersLandingRevealsFiveHexes()
    {
        var (none, with) = Pair(Preset("horizon", "explorer"), out var hubNone, out var hubWith);
        var g = with.Surface!.Map.Grid;
        foreach (var t in g.Within(hubWith.Tile, 5)) Assert.True(with.Surface.Players[0].Surveyed[t], $"tile {t}");
        Assert.Contains(g.Within(hubNone.Tile, 5), t => !none.Surface!.Players[0].Surveyed[t]);
        foreach (var t in g.Within(hubNone.Tile, 3)) Assert.True(none.Surface!.Players[0].Surveyed[t]);
        Assert.Equal(5, with.FitOutLandingReveal(0));
    }

    // ---------------------------------------------------------------- determinism and the hash

    [Fact]
    public void TheSameFitOutTwiceGivesTheSameHashesAndADifferentOneDoesNot()
    {
        var fit = Preset("horizon", "explorer");
        var a = Game("horizon", fit);
        var b = Game("horizon", Preset("horizon", "explorer"));
        var site = Enumerable.Range(0, a.Surface!.Map.Grid.Count).First(t => a.Surface.CanLand(0, t));
        for (var tick = 0; tick < Sec(180); tick++)
        {
            foreach (var g in new[] { a, b })
            {
                if (tick == 3) g.Schedule(g.Tick, new LandColonyCommand(0, site));
                if (tick == Sec(40)) g.Schedule(g.Tick, new SatelliteCommand(0, 30, 10));
                if (tick == Sec(60)) g.Schedule(g.Tick, new MoveUnitsCommand(0, g.Surface!.Units.Select(u => u.Id).ToList(), g.Surface.Map.Grid.Within(site, 4).Last()));
                g.Step();
            }
            if (tick % 15 == 0 || tick == Sec(180) - 1) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(0, a.RefusedCommands);
        Assert.Equal(FitOutRules.FreeSatellites(Real, fit), a.FitOutFreeSatellites(0) + a.Surface.Satellites.Count); // one of its ready satellites is up

        // The fit-out is in the hash: no fit-out, one head moved, one container swapped, another focus.
        var start = Game("horizon", fit).StateHash();
        Assert.NotEqual(Game("horizon", null).StateHash(), start);
        var crew = fit.Crew.ToArray();
        crew[0]--;
        crew[1]++;
        var moved = new FitOut(fit.Focus, crew, fit.Cargo);
        Assert.Null(FitOutRules.Problem(Real, "horizon", moved));
        Assert.NotEqual(Game("horizon", moved).StateHash(), start);
        var cargo = new Dictionary<string, int>(fit.Cargo);
        cargo["sat"]--; // 2 containers: nothing else changes at tick 0 but the ready satellites
        var lighter = new FitOut(fit.Focus, fit.Crew, cargo);
        Assert.Null(FitOutRules.Problem(Real, "horizon", lighter));
        Assert.NotEqual(Game("horizon", lighter).StateHash(), start);
        var science = new[] { 28, 28, 36, 28 }; // legal under both focuses: only the focus (the perk) differs
        Assert.NotEqual(Game("horizon", new FitOut("allround", science, Cargo())).StateHash(), Game("horizon", new FitOut("scientist", science, Cargo())).StateHash());
        // The game keeps its own copy: changing the lists afterwards changes nothing.
        var mine = new Dictionary<string, int>(fit.Cargo);
        var heads = fit.Crew.ToArray();
        var sim = Game("horizon", new FitOut(fit.Focus, heads, mine));
        heads[0] = 0;
        mine["mat"] = 0;
        Assert.Equal(start, sim.StateHash());
        Assert.Equal(fit.Crew[0], sim.FitOutOf(0)!.Crew[0]);
    }

    // ---------------------------------------------------------------- the levers in the game

    [Fact]
    public void ExplorationDrivesFasterAndSweepsSooner()
    {
        var (none, with) = Pair(Heavy("exploration", ("sat", 2)), out _, out _);
        // drive_speed: every ground vehicle, same tile, same weather.
        for (var i = 0; i < none.Surface!.Units.Count; i++)
        {
            var a = none.Surface.Units[i];
            var b = with.Surface!.Units[i];
            Assert.Equal((a.DefId, a.Tile), (b.DefId, b.Tile));
            Near(none.Surface.SpeedPerTick(a).ToDouble() * 1.18, with.Surface.SpeedPerTick(b));
        }
        // ... and it gets there sooner.
        var from = none.Surface.Units[0].Tile;
        var goal = none.Surface.Map.Grid.Within(from, 5).First(t => none.Surface.Map.Grid.Distance(t, from) == 5 && none.Surface.Map.Terrain(t).Passable &&
            none.Surface.StructureAt(t) == null && !none.Surface.IsLandingPad(t));
        var arrive = new List<long>();
        foreach (var g in new[] { none, with })
        {
            var rover = g.Surface!.Units[0];
            Do(g, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
            StepUntil(g, () => rover.Path.Count == 0 && rover.Tile == goal, Sec(300));
            arrive.Add(g.Tick);
        }
        Assert.True(arrive[1] < arrive[0], $"{arrive[1]} vs {arrive[0]}");

        // satellite_sweep: a lap of the band in 60 s x 0.82 (738 ticks instead of 900).
        var width = none.Surface.Map.Grid.Width;
        foreach (var g in new[] { none, with }) Do(g, new SatelliteCommand(0, 30, 0));
        Steps(none, Sec(40));
        Steps(with, Sec(40));
        var slow = none.Surface.Satellites.Single();
        var fast = with.Surface!.Satellites.Single();
        Assert.InRange(slow.Swept, (none.Tick - 1 - slow.ReadyTick) * width / 900, (none.Tick - slow.ReadyTick) * width / 900);
        Assert.InRange(fast.Swept, (with.Tick - 1 - fast.ReadyTick) * width / 738, (with.Tick - fast.ReadyTick) * width / 738);
        Assert.Equal(none.Tick - slow.ReadyTick, with.Tick - fast.ReadyTick); // the same time sweeping
        Assert.True(fast.Swept > slow.Swept);
    }

    [Fact]
    public void ReadySatellitesLaunchWithoutPaying()
    {
        var plain = Game("horizon", null);
        var sim = Game("horizon", Heavy("exploration", ("sat", 2)));
        var stock = sim.Players[0].Stock;
        var cost = Real.Orbit!.SatelliteCost;
        Assert.Equal(2, sim.FitOutFreeSatellites(0));
        var before = (stock[R("duranium")], stock[R("tritanium")]);
        Do(sim, new SatelliteCommand(0, 20, 0));
        Do(sim, new SatelliteCommand(0, 40, 0));
        Assert.Equal(before, (stock[R("duranium")], stock[R("tritanium")])); // both free
        Assert.Equal(0, sim.FitOutFreeSatellites(0));
        Assert.Equal(2, sim.Surface!.Satellites.Count);
        Do(sim, new SatelliteCommand(0, 60, 0)); // the third is paid for
        Assert.Equal(before.Item1 - cost.Single(c => c.Resource == R("duranium")).Amount, stock[R("duranium")]);
        // A ready one goes up even with nothing in stock; after it, the usual refusal.
        var broke = Game("horizon", Heavy("exploration"));
        var bs = broke.Players[0].Stock;
        bs.Add(R("duranium"), -bs[R("duranium")]);
        Assert.Null(broke.Surface!.SatelliteProblem(broke, 0, 20));
        Do(broke, new SatelliteCommand(0, 20, 0));
        Assert.Equal("not enough resources", broke.Surface.SatelliteProblem(broke, 0, 40));
        // No fit-out: paid as always.
        var had = plain.Players[0].Stock[R("duranium")];
        Do(plain, new SatelliteCommand(0, 20, 0));
        Assert.True(plain.Players[0].Stock[R("duranium")] < had);
    }

    [Fact]
    public void ExplorationStretchesTheDroneBattery()
    {
        var (none, with) = Pair(Heavy("exploration"), out var hubNone, out var hubWith);
        var drain = new List<double>();
        foreach (var (g, hub) in new[] { (none, hubNone), (with, hubWith) })
        {
            var s = g.Surface!;
            var pad = Place(g, hub, "drone_pad");
            g.Players[0].Stock.Add(R("scout_drone"), F(1) - g.Players[0].Stock[R("scout_drone")]);
            StepUntil(g, () => s.DronesOf(pad.Id).Count > 0 && s.DroneOf(s.DronesOf(pad.Id)[0]).Battery == Fix64.One, Sec(120));
            var drone = s.DronesOf(pad.Id)[0];
            var target = s.Map.Grid.Within(hub.Tile, 4).First(t => s.Map.Grid.Distance(hub.Tile, t) == 4 && s.LaunchProblem(g, 0, pad.Id, t) == null);
            Do(g, new LaunchDroneCommand(0, pad.Id, target));
            StepUntil(g, () => s.DroneOf(drone).State == DroneState.Flying, Sec(20));
            var b0 = s.DroneOf(drone).Battery;
            Steps(g, 10);
            drain.Add((b0 - s.DroneOf(drone).Battery).ToDouble() / 10);
        }
        var seconds = Real.DroneOf("scout_drone")!.BatterySeconds.ToDouble();
        Assert.Equal(1 / (seconds * Simulation.TickRate), drain[0], 9);
        Assert.Equal(1 / (seconds * 1.18 * Simulation.TickRate), drain[1], 9);
    }

    [Fact]
    public void EngineeringBuildsRepairsAndFabricatesFasterAndWearsLess()
    {
        var (none, with) = Pair(Heavy("engineering"), out var hubNone, out var hubWith);
        var a = none.Surface!;
        var b = with.Surface!;

        // build_time: x 0.82, to the nearest tick; the old helper (no owner) is untouched.
        foreach (var id in new[] { "storage_container", "refinery", "science_lab" })
        {
            var ticks = a.BuildTicksOf(id);
            Assert.Equal(ticks, b.BuildTicksOf(id));
            Assert.Equal(ticks, a.BuildTicksOf(id, 0));
            Assert.InRange(b.BuildTicksOf(id, 0), (int)(ticks * 0.82), (int)(ticks * 0.82) + 1);
        }
        var level = Real.Building("refinery").Levels[0];
        Assert.InRange(b.LevelTicksOf(level, 0), (int)(a.LevelTicksOf(level) * 0.82), (int)(a.LevelTicksOf(level) * 0.82) + 1);
        // ... and a real building goes up in that time.
        var took = new List<long>();
        foreach (var (g, hub) in new[] { (none, hubNone), (with, hubWith) })
        {
            var tile = FreeTile(g, hub, "storage_container");
            Do(g, new PlaceBuildingCommand(0, "storage_container", tile, 0));
            var built = g.Surface!.StructureAt(tile)!;
            var from = g.Tick;
            StepUntil(g, () => built.State == StructureState.Complete, Sec(200));
            took.Add(g.Tick - from);
        }
        Assert.InRange(took[0], a.BuildTicksOf("storage_container") - 1, a.BuildTicksOf("storage_container") + 1);
        Assert.InRange(took[1], b.BuildTicksOf("storage_container", 0) - 1, b.BuildTicksOf("storage_container", 0) + 1);

        // wear: x 0.82.
        var ra = Place(none, hubNone, "refinery");
        var rb = Place(with, hubWith, "refinery");
        Near(a.WearPerMinute(ra).ToDouble() * 0.82, b.WearPerMinute(rb));

        // fabrication: a Machine Shop 18% faster; the hub workshop 18% faster and, the Builder's perk, 30% on top.
        var sa = Place(none, hubNone, "machine_shop");
        var sb = Place(with, hubWith, "machine_shop");
        Near(a.SecondsAt(sa, "structural_frame").ToDouble() / 1.18, b.SecondsAt(sb, "structural_frame"), 1e-5);
        Near(a.SecondsAt(hubNone, "mechanical_spares").ToDouble() / (1.18 * 1.3), b.SecondsAt(hubWith, "mechanical_spares"), 1e-5);

        // repair_speed: the crew weld 18% more condition a tick.
        var heal = new List<double>();
        foreach (var (g, refinery) in new[] { (none, ra), (with, rb) })
        {
            foreach (var kit in new[] { "mechanical_spares", "electrical_spares", "rover_spares", "environmental_spares" }) g.Players[0].Stock.Add(R(kit), F(20));
            Do(g, new DebugStructureCommand(0, refinery.Id, DebugStructureField.Condition, F(40)));
            Do(g, new MaintainCommand(0, refinery.Id));
            StepUntil(g, () => refinery.Condition > F(40), Sec(120));
            var c0 = refinery.Condition;
            Steps(g, 10);
            Assert.True(refinery.Condition < F(100));
            heal.Add((refinery.Condition - c0).ToDouble() / 10);
        }
        Assert.Equal(heal[0] * 1.18, heal[1], 6);
    }

    [Fact]
    public void ScienceResearchesFasterAndTheLabEquipmentIsBanked()
    {
        var fit = Heavy("science", ("lab", 3));
        var (none, with) = Pair(fit, out var hubNone, out var hubWith);
        Assert.Equal(F(60), with.Surface!.DigResearchBank(0)); // 3 cases x 20 points, waiting for a tech
        Assert.Equal(Fix64.Zero, none.Surface!.DigResearchBank(0));
        var tech = Real.Techs.First(t => t.Id == "alloy_metallurgy" || t.Cost > F(90)); // dearer than the bank
        var rate = new List<double>();
        var progress = new List<double>();
        foreach (var (g, hub) in new[] { (none, hubNone), (with, hubWith) })
        {
            var s = g.Surface!;
            Place(g, hub, "science_lab");
            foreach (var need in tech.Requires) Do(g, new DebugTechCommand(0, 0, need, true, Fix64.Zero));
            Do(g, new ResearchCommand(0, tech.Id));
            Steps(g, 5);
            var p0 = s.Players[0].ResearchProgress[tech.Index];
            Steps(g, 30);
            rate.Add(s.ResearchPerMinute(0).ToDouble());
            progress.Add((s.Players[0].ResearchProgress[tech.Index] - p0).ToDouble());
            if (g == with) Assert.True(p0 >= F(60), "the banked points went into the first tech"); // + a few ticks of lab work
            else Assert.True(p0 < F(10));
        }
        Assert.True(rate[0] > 0);
        Assert.Equal(rate[0] * 1.18, rate[1], 6);
        Assert.Equal(progress[0] * 1.18, progress[1], 6);
        Assert.Equal(Fix64.Zero, with.Surface.DigResearchBank(0));
    }

    [Fact]
    public void TheScientistGetsMoreResearchFromMissions()
    {
        var plain = Game("horizon", null);
        var builder = Game("horizon", Preset("horizon", "builder"));
        var scientist = Game("horizon", Preset("horizon", "scientist"));
        var banked = scientist.Surface!.DigResearchBank(0); // its lab equipment
        var other = builder.Surface!.DigResearchBank(0);
        plain.Surface!.MissionResearch(plain, 0, F(50));
        builder.Surface.MissionResearch(builder, 0, F(50));
        scientist.Surface.MissionResearch(scientist, 0, F(50));
        Assert.Equal(F(50), plain.Surface.DigResearchBank(0));
        Assert.Equal(other + F(50), builder.Surface.DigResearchBank(0));
        Near(banked.ToDouble() + 60, scientist.Surface.DigResearchBank(0));
    }

    // -- digs: only one kind of find left, quick layers (the helpers of ExcavationTests, on a system game) --

    private const string Ex = "surfaces/excavation.json";

    private static GameData Only(string kind, int layerSeconds, params (string File, string From, string To)[] more)
    {
        var weights = new Dictionary<string, int> { ["artifact"] = 26, ["data_core"] = 14, ["tech_fragment"] = 10, ["resources"] = 26, ["defence"] = 14, ["pathogen"] = 10 };
        var patches = weights.Select(w => (Ex, $"{{ \"kind\": \"{w.Key}\", \"weight\": {w.Value} }}", $"{{ \"kind\": \"{w.Key}\", \"weight\": {(w.Key == kind ? 100 : 0)} }}")).ToList();
        patches.Add((Ex, "\"seconds_per_layer\": 120", $"\"seconds_per_layer\": {layerSeconds}"));
        patches.AddRange(more);
        return TestData.Patched(patches.ToArray());
    }

    /// <summary>A finished Excavation Site on a new ruin near the hub, and a free tile beside it.</summary>
    private static SurfaceStructure Site(Simulation sim, SurfaceStructure hub, out int neighbour)
    {
        var s = sim.Surface!;
        var g = s.Map.Grid;
        Do(sim, new DebugSurveyAllCommand(0, 0));
        bool Open(int t) => s.Map.Terrain(t).Buildable && s.StructureAt(t) == null && s.Map.DepositResource[t] < 0 && s.RuinAt(t) == null &&
                            !s.Map.NearNatives(t) && !s.IsLandingPad(t) && !s.Units.Any(u => u.Tile == t) && g.Distance(hub.Tile, t) <= 3;
        var tile = g.Within(hub.Tile, 3).First(t => g.Distance(hub.Tile, t) >= 2 && Open(t) && g.Neighbours(t).Any(n => n != hub.Tile && Open(n)));
        neighbour = g.Neighbours(tile).First(n => n != hub.Tile && Open(n));
        Do(sim, new DebugRuinCommand(0, tile, true));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "excavation_site", tile, 0));
        var site = s.StructureAt(tile)!;
        Assert.Equal(StructureState.Complete, site.State);
        return site;
    }

    [Fact]
    public void ScienceDigsFasterAndIsSickForLess()
    {
        // dig_time: a 30 s layer (450 ticks) takes 450 x 0.82 = 369.
        var slow = Only("artifact", 30);
        var (none, with) = Pair(Heavy("science"), out var hubNone, out var hubWith, slow);
        var siteNone = Site(none, hubNone, out _);
        var siteWith = Site(with, hubWith, out _);
        Steps(none, 100);
        Steps(with, 100);
        var a = none.Surface!.DigProgress(siteNone).ToDouble();
        var b = with.Surface!.DigProgress(siteWith).ToDouble();
        Assert.True(a > 0.1);
        Assert.Equal(a * 450 / 369, b, 6);

        // sickness: 300 s x 0.82 from the crew, and x (1 - 0.08 x 4) more with four cases of medical stores.
        var plague = Only("pathogen", 2);
        var seconds = new List<int>();
        foreach (var fit in new[] { null, Heavy("science"), Heavy("science", ("med", 4)) })
        {
            var g = Game("horizon", fit, plague);
            var hub = Land(g);
            Site(g, hub, out _);
            StepUntil(g, () => g.Surface!.Finds.Count > 0, Sec(30));
            Assert.Equal("pathogen", g.Surface!.Finds[0].Kind);
            seconds.Add(g.Surface.CrewSickSeconds(hub.Id));
        }
        Assert.Equal(300, seconds[0]);
        Assert.InRange(seconds[1], 245, 246);
        Assert.InRange(seconds[2], 167, 168);
    }

    [Fact]
    public void DefenseTakesLessFromARuinsDefencesAndTheCombatantsVehiclesHalf()
    {
        var armed = Only("defence", 2, (Ex, "\"defence\": { \"damage\": [20, 40]", "\"defence\": { \"damage\": [30, 30]"), (Ex, "\"disarmed_chance\": 0.15", "\"disarmed_chance\": 0"));
        var results = new List<(double Building, double Vehicle)>();
        foreach (var fit in new[] { null, Heavy("defense") })
        {
            var g = Game("horizon", fit, armed);
            var hub = Land(g);
            var site = Site(g, hub, out var neighbour);
            var rover = g.Surface!.Units.First(u => u.DefId == "survey_rover");
            Do(g, new DebugMoveUnitCommand(0, rover.Id, neighbour));
            Do(g, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(100)));
            Do(g, new DebugStructureCommand(0, site.Id, DebugStructureField.Condition, F(100)));
            StepUntil(g, () => g.Surface.Finds.Count > 0, Sec(30));
            Assert.Equal("defence", g.Surface.Finds[0].Kind);
            results.Add(((F(100) - site.Condition).ToDouble(), (F(100) - rover.Integrity).ToDouble()));
        }
        // No fit-out: 30 to both. Defense at 70% of the crew: x 0.82; the Combatant's vehicles half of that.
        Assert.InRange(results[0].Building, 30, 30.3); // (a little wear on the site while it dug)
        Assert.Equal(30, results[0].Vehicle, 6);
        Assert.InRange(results[1].Building, 30 * 0.82, 30 * 0.82 + 0.3);
        Assert.Equal(30 * 0.82 * 0.5, results[1].Vehicle, 5);
    }

    private static readonly Lazy<ulong> PredatorSeed = new(() =>
    {
        // A system whose home world has predators on open ground (most do).
        for (var seed = Seed; seed < Seed + 60; seed++)
        {
            var map = MatchSetup.CreateSystemGame(TestData.Real, seed).Surface!.Map;
            if (Enumerable.Range(0, map.Grid.Count).Any(t => map.WildlifeAt[t] == Wildlife.Predators && !map.Toxic[t] && map.Terrain(t).Passable)) return seed;
        }
        throw new InvalidOperationException("no seed with predators");
    });

    private static SurfaceUnit RoverInALair(Simulation g)
    {
        Land(g);
        var s = g.Surface!;
        var lair = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.WildlifeAt[t] == Wildlife.Predators && !s.Map.Toxic[t] && s.Map.Terrain(t).Passable && s.StructureAt(t) == null);
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        Do(g, new DebugMoveUnitCommand(0, rover.Id, lair));
        Do(g, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(100)));
        return rover;
    }

    [Fact]
    public void DefenseKeepsWildlifeOffAndArmourAndTheCombatantSoftenTheBite()
    {
        const string rover = "surfaces/units/survey_rover.json";
        const string wild = "\"wildlife\": { \"chance_per_min\": 0.4, \"damage\": [8, 20] }";
        var seed = PredatorSeed.Value;

        // wildlife_damage: a 20-point bite is 18 with two cases of armour, 16 with four, and half of that for the Combatant.
        var bite = TestData.Patched((rover, wild, "\"wildlife\": { \"chance_per_min\": 100000, \"damage\": [20, 20] }"));
        var even = new[] { 30, 30, 30, 30 };
        var cases = new (FitOut? Fit, int Damage)[]
        {
            (null, 20), (new FitOut("allround", even, Cargo()), 18), (new FitOut("allround", even, Cargo(("armour", 4))), 16),
            (Heavy("defense"), 9), (Heavy("defense", ("armour", 4)), 8),
        };
        foreach (var (fit, damage) in cases)
        {
            var g = Game("horizon", fit, bite, seed);
            var unit = RoverInALair(g);
            StepUntil(g, () => unit.LastHazard == PlanetSurface.WildlifeHazard, 5);
            Assert.Equal(damage, unit.LastHazardDamage);
        }

        // wildlife (the rate): at 1.1 a tick it bites every tick with no fit-out; x 0.82 (Defense at 70%) it sometimes doesn't.
        var often = TestData.Patched((rover, wild, "\"wildlife\": { \"chance_per_min\": 990, \"damage\": [1, 1] }"));
        var bites = new List<int>();
        foreach (var fit in new[] { null, Heavy("defense") })
        {
            var g = Game("horizon", fit, often, seed);
            var unit = RoverInALair(g);
            var before = unit.Integrity;
            Steps(g, 60);
            bites.Add((before - unit.Integrity).FloorToInt()); // one point a bite
        }
        Assert.Equal(60, bites[0]);
        Assert.InRange(bites[1], 35, 59);
    }

    [Fact]
    public void ExplorationMeetsFewerHazardsAndArmourSoftensThem()
    {
        const string rover = "surfaces/units/survey_rover.json";
        // A hazard every tick on the landing ground (plains, desert), each of a fixed size (rock strike and sinkhole are the two there).
        (string, string, string)[] Certain(int damage) => new[]
        {
            (rover, "\"plains\": { \"wear\": 0.4, \"hazard\": 0.03", "\"plains\": { \"wear\": 0.4, \"hazard\": 990"),
            (rover, "\"desert\": { \"wear\": 0.6, \"hazard\": 0.05", "\"desert\": { \"wear\": 0.6, \"hazard\": 990"),
            (rover, "\"weight\": 50, \"damage\": [4, 12]", $"\"weight\": 50, \"damage\": [{damage}, {damage}]"),
            (rover, "\"weight\": 20, \"damage\": [10, 25]", $"\"weight\": 20, \"damage\": [{damage}, {damage}]"),
        };

        // Drives a Mk I rover five hexes over the landing ground: the ticks it drove on plains or desert, the hazards it
        // met there and the first one's damage.
        (int Ticks, int Hits, int First) Drive(GameData data, FitOut? fit)
        {
            var g = Game("horizon", fit, data);
            Land(g);
            var s = g.Surface!;
            var unit = s.Units.First(u => u.DefId == "survey_rover");
            var goal = s.Map.Grid.Within(unit.Tile, 5).First(t => s.Map.Grid.Distance(t, unit.Tile) == 5 && s.Map.Terrain(t).Id is "plains" or "desert" &&
                s.StructureAt(t) == null && !s.IsLandingPad(t));
            Do(g, new MoveUnitsCommand(0, new[] { unit.Id }, goal));
            int ticks = 0, hits = 0, first = 0;
            for (var i = 0; i < Sec(120) && unit.Path.Count > 0 && !unit.Broken; i++)
            {
                var open = s.Map.Terrain(unit.Tile).Id is "plains" or "desert";
                g.Step();
                if (!open) continue;
                ticks++;
                if (unit.HazardAtTick != g.Tick - 1) continue;
                if (hits++ == 0) first = unit.LastHazardDamage;
            }
            return (ticks, hits, first);
        }

        // rover_hazard (the rate): at 1.1 a tick a rover with no fit-out meets one every tick of driving; x 0.82 (Exploration
        // at 70% of the crew) it sometimes doesn't.
        var often = TestData.Patched(Certain(1));
        var plain = Drive(often, null);
        var careful = Drive(often, Heavy("exploration"));
        Assert.True(plain.Ticks >= 25, $"{plain.Ticks} ticks");
        Assert.Equal(plain.Ticks, plain.Hits);
        Assert.InRange(careful.Hits, careful.Ticks * 6 / 10, careful.Ticks - 1);

        // hazard_damage: a 10-point hit is 9 with two cases of field armour and 8 with four; the Combatant's perk is not for hazards.
        var hard = TestData.Patched(Certain(10));
        var even = new[] { 30, 30, 30, 30 };
        Assert.Equal(10, Drive(hard, null).First);
        Assert.Equal(9, Drive(hard, new FitOut("allround", even, Cargo())).First);
        Assert.Equal(8, Drive(hard, new FitOut("allround", even, Cargo(("armour", 4)))).First);
        Assert.Equal(8, Drive(hard, Heavy("defense", ("armour", 4))).First);
    }

    private static readonly Lazy<ulong> DomesSeed = new(() =>
    {
        // A system whose landable world needs domes (Class L: about half of them).
        for (var seed = Seed; seed < Seed + 60; seed++)
            if (MatchSetup.CreateSystemGame(TestData.Real, seed).Surface!.NeedsDomes) return seed;
        throw new InvalidOperationException("no seed with a Domes world");
    });

    [Fact]
    public void DefenseLosesFewerCrewWhenTheAirFails()
    {
        // A hub without its own life support on a Domes world: no air from the start; after the grace time crew are lost.
        var data = TestData.Patched(("buildings/surface/cores.json", "\"storage\": 1500, \"life_support\": 2,", "\"storage\": 1500, \"life_support\": 0,"));
        var lost = new List<double>();
        foreach (var fit in new[] { null, Heavy("defense") })
        {
            var g = Game("horizon", fit, data, DomesSeed.Value);
            var hub = Land(g);
            Assert.True(g.Surface!.NeedsDomes);
            StepUntil(g, () => hub.LifeSupportFailing, Sec(data.Habitability!.Domes!.GraceSeconds + 5));
            var before = hub.CrewLost;
            Steps(g, Sec(60));
            lost.Add((hub.CrewLost - before).ToDouble());
        }
        Assert.True(lost[0] > 1, $"{lost[0]}");
        Assert.Equal(lost[0] * 0.82, lost[1], 5);
    }

    // ---------------------------------------------------------------- the mission reach check

    [Fact]
    public void TheMissionReachCheckAlsoWalksTheLeanestFitOut()
    {
        // Every cargo line at its minimum: 6 building materials and 4 fuel, nothing else from the hold.
        var lean = Real.LeanestFitOutStock()!;
        Fix64 Of(string res) => lean.Where(a => a.Resource == R(res)).Select(a => a.Amount).DefaultIfEmpty(Fix64.Zero).Single();
        Assert.Equal((F(300), F(180), F(100)), (Of("duranium"), Of("tritanium"), Of("deuterium")));
        Assert.Equal(Real.Landing.StartStock.Single(a => a.Resource == R("crew")).Amount, Of("crew"));
        Assert.Empty(Real.MissionReachProblems());
    }
}
