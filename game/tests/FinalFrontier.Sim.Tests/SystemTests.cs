using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>Generated star systems (SystemGenerator) and the system state (StarSystemState): flight, fog, satellites,
/// scans, neutral traffic, orbit actions only at the colony's body, and the state hash.</summary>
public class SystemTests
{
    private readonly ITestOutputHelper _out;

    public SystemTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestData.Real;

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static string Describe(GeneratedSystem s) =>
        $"{s.Star.Name} ({s.Star.Class}, r {s.Star.Radius}): " + string.Join("; ", s.Entries.Select(e =>
            $"{e.Id} {e.Body.Name} {e.Body.ClassId} size {e.Body.Size} at {e.Body.Position} parent {e.Body.Parent ?? "-"} {e.Zone} au {e.Climate.Au} " +
            $"day {e.Climate.DaySeconds} year {e.Climate.YearDays} tilt {e.Climate.AxialTiltDeg} ecc {e.Climate.Eccentricity} {e.Climate.ClimateId} {e.Climate.Retrograde} {e.Climate.Locked}"));

    // ---------------------------------------------------------------- generation

    [Fact]
    public void GenerationIsDeterministic()
    {
        foreach (var seed in new ulong[] { 1, 1701, 99999, 0, ulong.MaxValue })
            Assert.Equal(Describe(SystemGenerator.Generate(Data, seed)), Describe(SystemGenerator.Generate(Data, seed)));
        var distinct = Enumerable.Range(1, 20).Select(s => Describe(SystemGenerator.Generate(Data, (ulong)s))).Distinct().Count();
        Assert.Equal(20, distinct);
        _out.WriteLine(Describe(SystemGenerator.Generate(Data, 1701)));
    }

    [Fact]
    public void SystemsHaveTheGeneratorsShape()
    {
        var g = Data.SystemGenerator!;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = SystemGenerator.Generate(Data, seed);
            var planets = s.Entries.Where(e => !e.IsMoon).ToList();
            Assert.InRange(planets.Count, g.PlanetsMin, g.PlanetsMax);
            Assert.Equal(s.Bodies.Count, s.Bodies.Select(b => b.Id).Distinct().Count());
            Assert.Equal(s.Bodies.Count, s.Bodies.Select(b => b.Name).Distinct().Count());
            Assert.NotNull(Data.Climate!.Star(s.Star.Class));
            foreach (var p in planets)
            {
                var r = p.Body.Position.Length();
                Assert.InRange(r.ToDouble(), g.OrbitMin.ToDouble() - 0.01, g.OrbitMax.ToDouble() + 0.01);
                Assert.Equal(g.ZoneAt(r).Id, p.Zone);
            }
            foreach (var m in s.Entries.Where(e => e.IsMoon))
            {
                var parent = s.Body(m.Body.Parent!)!;
                var d = FixVec2.Distance(parent.Position, m.Body.Position).ToDouble();
                Assert.InRange(d, g.MoonDistanceMin.ToDouble() - 0.01, g.MoonDistanceMax.ToDouble() + 30);
                Assert.Null(parent.Parent); // no moons of moons
                Assert.StartsWith(parent.Name, m.Body.Name);
            }
            foreach (var b in s.Bodies) Assert.InRange(b.Size, 1, 7);
            Assert.True(s.Bodies.Max(b => b.Position.Length()).ToDouble() < 600, "bodies stay within the plot (~540)");
        }
    }

    [Fact]
    public void ClassMIsRare()
    {
        int hzPlanets = 0, hzM = 0, planets = 0, m = 0, systemsWithM = 0, bodies = 0, fallbacks = 0;
        var byClass = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = SystemGenerator.Generate(Data, seed);
            if (s.FallbackUsed) fallbacks++;
            foreach (var e in s.Entries)
            {
                bodies++;
                byClass[e.Body.ClassId] = byClass.GetValueOrDefault(e.Body.ClassId) + 1;
                if (e.IsMoon) continue;
                planets++;
                if (e.Body.ClassId == "class_m") m++;
                if (e.Zone != Data.SystemGenerator!.HabitableZone) continue;
                hzPlanets++;
                if (e.Body.ClassId == "class_m") hzM++;
            }
            if (s.Bodies.Any(b => b.ClassId == "class_m")) systemsWithM++;
        }
        var share = (double)hzM / hzPlanets;
        _out.WriteLine($"200 seeds: {bodies} bodies, {planets} planets/belts, {hzPlanets} in the habitable zone; class M {hzM} = {share:P1} of habitable-zone planets, {(double)m / planets:P1} of all planets; {systemsWithM} systems of 200 have one; fallback used {fallbacks}×");
        _out.WriteLine(string.Join(", ", byClass.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(share <= 0.07, $"class M is {share:P1} of habitable-zone planets (about 1 in 20 or less)");
        Assert.True(hzM > 0, "class M still happens");
        Assert.Equal(m, hzM); // only in the habitable zone (moons never roll M)
    }

    [Fact]
    public void EverySystemHasALandableBody()
    {
        var fallbacks = 0;
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var s = SystemGenerator.Generate(Data, seed * 7919 + 3);
            Assert.NotEmpty(s.Landable(Data));
            if (s.FallbackUsed)
            {
                fallbacks++;
                Assert.Single(s.Landable(Data));
                Assert.Equal(Data.SystemGenerator!.LandableFallback, s.Landable(Data)[0].ClassId);
            }
        }
        _out.WriteLine($"fallback used on {fallbacks} of 1000 seeds");
        Assert.True(fallbacks > 0, "the fallback rule is exercised");
        Assert.Equal(SystemGenerator.Generate(Data, 1701).Landable(Data).Select(b => b.Id), MatchSetup.LandableBodies(Data, 1701).Select(b => b.Id));
    }

    [Fact]
    public void GeneratedBodiesHaveClimates()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var s = SystemGenerator.Generate(Data, seed);
            foreach (var e in s.Entries)
            {
                var c = e.Climate;
                Assert.Equal(e.Id, c.Id);
                Assert.NotNull(Data.Climate!.Template(c.ClimateId));
                Assert.Equal(Data.Climate.ClassClimate(e.Body.ClassId), c.ClimateId);
                Assert.True(c.DaySeconds >= 30 && c.YearDays > Fix64.Zero);
                if (e.IsMoon)
                {
                    var parent = s.Climate.Body(e.Body.Parent!)!;
                    Assert.Equal(parent.Id, c.Parent);
                    Assert.Equal(FinalFrontier.Sim.Data.SpinLock.Parent, c.Locked);
                    Assert.True(Fix64.Abs(c.YearSeconds - parent.YearSeconds) <= parent.YearSeconds / 50, "a moon's year is its planet's");
                }
                else Assert.True(c.Au > Fix64.Zero);
                var summary = Climate.Summary(Data, s.Climate, e.Id)!;
                Assert.Equal(e.Id, summary.BodyId);
                if (e.Zone == "habitable" && e.Body.ClassId == "class_m")
                    Assert.InRange(summary.MeanTemperatureC.ToDouble(), -40, 60);
            }
        }
    }

    [Fact]
    public void ClimateWorksOnAGeneratedHomeWorld()
    {
        foreach (var seed in new ulong[] { 1, 1701, 99999 })
        {
            var sim = MatchSetup.CreateSystemGame(Data, seed);
            var map = sim.Surface!.Map;
            var home = sim.System!.SurfaceBody!;
            Assert.Equal(home, map.ClimateBodyId);
            Assert.Same(sim.System.Generated!.Climate.Body(home), Climate.BodyOf(Data, map));
            Assert.Equal(sim.System.Body(home)!.ClassId, map.Def.ClassId);
            var tile = map.Grid.Count / 2;
            var seenDay = false;
            var seenNight = false;
            for (long tick = 0; tick < 2L * Climate.BodyOf(Data, map)!.DaySeconds * Simulation.TickRate; tick += 600)
            {
                var at = Climate.At(Data, map, tile, tick);
                Assert.InRange(at.TemperatureC.ToDouble(), -150, 150);
                seenDay |= at.Daylight > Fix64.Half;
                seenNight |= at.Night;
                Climate.Weather(Data, map, tile, tick);
                Telemetry.Read(Data, map, tile, tick);
            }
            Assert.True(seenDay && seenNight, "the generated world turns");
            // A generated body's climate differs from the stand-in (Tarsis) climate of a planet-only game.
            var planetOnly = MatchSetup.CreatePlanetLanding(Data, seed, planetClass: map.Def.ClassId).Surface!.Map;
            Assert.NotEqual(Climate.BodyOf(Data, planetOnly)!.Id, Climate.BodyOf(Data, map)!.Id);
            Assert.True(sim.Surface.Map.Grid.Count > 0 && Enumerable.Range(0, map.Grid.Count).Any(t => sim.Surface.CanLand(0, t)));
        }
    }

    // ---------------------------------------------------------------- set-up

    [Fact]
    public void ASystemGameStartsInOrbitOfItsHome()
    {
        foreach (var seed in new ulong[] { 1, 1701, 99999, 42 })
        {
            var sim = MatchSetup.CreateSystemGame(Data, seed);
            var sys = sim.System!;
            var landable = MatchSetup.LandableBodies(Data, seed);
            var expect = landable.FirstOrDefault(b => b.ClassId == "class_m") ?? landable.FirstOrDefault(b => b.ClassId == "class_l") ?? landable[0];
            Assert.Equal(expect.Id, sys.SurfaceBody);
            var flag = sys.FlagshipOf(0)!;
            Assert.Equal(sys.SurfaceBody, flag.AtBody);
            Assert.Equal("federation.Galaxy", flag.ClassId);
            Assert.Equal("U.S.S. Endeavour", flag.Name);
            foreach (var b in sys.Bodies)
                Assert.Equal(b.Id == sys.SurfaceBody ? BodyKnowledge.Charted : BodyKnowledge.Detected, sys.KnowledgeOf(0, b.Id));
            var neutral = sys.Ships.Where(s => s.IsNeutral).ToList();
            Assert.InRange(neutral.Count, 3, 5);
            foreach (var n in neutral)
            {
                Assert.True(Data.HasShipClass(n.ClassId));
                Assert.Contains(Data.ShipClass(n.ClassId).Role, new[] { "freighter", "tanker", "science", "colony" });
                Assert.False(string.IsNullOrEmpty(n.Name));
                Assert.False(string.IsNullOrEmpty(n.Registry));
                Assert.False(string.IsNullOrEmpty(n.Cargo));
                Assert.NotNull(sys.Body(n.AtBody));
            }
            Assert.Equal(sys.Ships.Count, sys.Ships.Select(s => s.Id).Distinct().Count());
            Assert.Equal(LandingPhase.InOrbit, sim.Surface!.Players[0].Phase);
        }
    }

    [Fact]
    public void TheHomeBodyCanBeChosen()
    {
        var seed = Enumerable.Range(1, 400).Select(s => (ulong)s).First(s => MatchSetup.LandableBodies(Data, s).Count >= 2);
        var landable = MatchSetup.LandableBodies(Data, seed);
        var second = landable[1];
        var sim = MatchSetup.CreateSystemGame(Data, seed, "cardassian", second.Name.ToUpperInvariant());
        Assert.Equal(second.Id, sim.System!.SurfaceBody);
        Assert.Equal(second.ClassId, sim.Surface!.Map.Def.ClassId);
        Assert.Equal("cardassian.Galor", sim.System.FlagshipOf(0)!.ClassId);
        Assert.Equal(second.Id, MatchSetup.CreateSystemGame(Data, seed, homeBody: second.Id).System!.SurfaceBody);
        var notLandable = SystemGenerator.Generate(Data, seed).Bodies.First(b => !Data.IsLandableClass(b.ClassId));
        Assert.Throws<ArgumentException>(() => MatchSetup.CreateSystemGame(Data, seed, homeBody: notLandable.Id));
        Assert.Throws<ArgumentException>(() => MatchSetup.CreateSystemGame(Data, seed, homeBody: "no such world"));
        // Different home bodies get different surfaces.
        Assert.NotEqual(SystemGenerator.SurfaceSeed(seed, landable[0].Id), SystemGenerator.SurfaceSeed(seed, second.Id));
    }

    // ---------------------------------------------------------------- flight

    [Fact]
    public void FlightTakesTwentyToNinetySecondsByDistance()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        foreach (var a in sys.Bodies)
            foreach (var b in sys.Bodies)
            {
                var t = sys.TravelTicks(a.Id, b.Id);
                if (a.Id == b.Id) Assert.Equal(0, t);
                else Assert.InRange(t, 20 * Simulation.TickRate, 90 * Simulation.TickRate);
                Assert.Equal(t, sys.TravelTicks(b.Id, a.Id));
            }
        var home = sys.Body(sys.SurfaceBody)!;
        var near = sys.Bodies.Where(b => b.Id != home.Id).OrderBy(b => FixVec2.Distance(b.Position, home.Position)).First();
        var far = sys.Bodies.OrderBy(b => FixVec2.Distance(b.Position, home.Position)).Last();
        Assert.True(sys.TravelTicks(home.Id, near.Id) < sys.TravelTicks(home.Id, far.Id));
    }

    [Fact]
    public void ASetCourseFliesThereArrivesAndChartsIt()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var home = sys.SurfaceBody!;
        var target = sys.Bodies.First(b => b.Id != home && sys.KnowledgeOf(0, b.Id) == BodyKnowledge.Detected);
        Assert.Null(sys.CourseProblem(0, ship.Id, target.Id));
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, target.Id));
        var start = sim.Tick;
        sim.Step();
        var travel = sys.TravelTicks(home, target.Id);
        Assert.Null(ship.AtBody);
        Assert.Equal(home, ship.FromBody);
        Assert.Equal(target.Id, ship.ToBody);
        Assert.Equal(start + travel, ship.ArriveTick);
        Assert.Contains("in flight to unknown body", sys.CourseProblem(0, ship.Id, home));
        Assert.Equal(sys.Body(home)!.Position, sys.ShipPosition(ship, start));
        Assert.Equal(target.Position, sys.ShipPosition(ship, start + travel));
        var mid = sys.ShipPosition(ship, start + travel / 2);
        var expected = (sys.Body(home)!.Position + target.Position) * Fix64.Half;
        Assert.True(FixVec2.Distance(mid, expected) < Fix64.FromInt(2));
        Assert.True(sys.EtaSeconds(ship, sim.Tick) > 0);
        Assert.NotEqual(FixVec2.Zero, sys.Heading(ship));

        Run(sim, (int)(start + travel - sim.Tick)); // the step at ArriveTick hasn't run yet
        Assert.Null(ship.AtBody);
        Assert.Equal(BodyKnowledge.Detected, sys.KnowledgeOf(0, target.Id));
        sim.Step();
        Assert.Equal(target.Id, ship.AtBody);
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, target.Id));
        Assert.Equal(target.Name, sys.NameFor(0, target.Id));

        // Charted is kept after leaving.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, home));
        Run(sim, (int)sys.TravelTicks(target.Id, home) + 2);
        Assert.Equal(home, ship.AtBody);
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, target.Id));
    }

    [Fact]
    public void BadCoursesAreRefusedAndChangeNothing()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 99999);
        var twin = MatchSetup.CreateSystemGame(Data, 99999);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var neutral = sys.Ships.First(s => s.IsNeutral);
        var other = sys.Bodies.First(b => b.Id != sys.SurfaceBody).Id;
        Assert.Equal("no such ship", sys.CourseProblem(0, 999, other));
        Assert.Equal("not your ship", sys.CourseProblem(0, neutral.Id, other));
        Assert.Equal("not your ship", sys.CourseProblem(1, ship.Id, other));
        Assert.Equal("no such body", sys.CourseProblem(0, ship.Id, "nowhere"));
        Assert.StartsWith("already in orbit of ", sys.CourseProblem(0, ship.Id, sys.SurfaceBody!));
        var refused = sim.RefusedCommands;
        sim.Schedule(0, new SetCourseCommand(0, 999, other));
        sim.Schedule(0, new SetCourseCommand(0, neutral.Id, other));
        sim.Schedule(0, new SetCourseCommand(0, ship.Id, sys.SurfaceBody!));
        sim.Schedule(0, new SetCourseCommand(0, ship.Id, "nowhere"));
        sim.Schedule(0, new SetCourseCommand(0, ship.Id, null!));
        sim.Schedule(0, new LongRangeScanCommand(0, neutral.Id));
        sim.Schedule(0, new SystemSatelliteCommand(0, other));
        sim.Schedule(0, new SystemSatelliteCommand(5, other));
        sim.Step();
        twin.Step();
        Assert.Equal(refused + 8, sim.RefusedCommands);
        Assert.Equal(twin.StateHash(), sim.StateHash());

        // In flight: no second course.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, other));
        sim.Step();
        Assert.Null(ship.AtBody);
        var refusedNow = sim.RefusedCommands;
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, sys.SurfaceBody!));
        sim.Step();
        Assert.Equal(refusedNow + 1, sim.RefusedCommands);
        Assert.Contains("in flight", sim.LastRefusal);
        Assert.Equal(other, ship.ToBody);
    }

    [Fact]
    public void SystemCommandsAreRefusedInPlanetOnlyGames()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Null(sim.System);
        var hash = sim.StateHash();
        sim.Schedule(0, new SetCourseCommand(0, 1, "b1"));
        sim.Schedule(0, new LongRangeScanCommand(0, 1));
        sim.Schedule(0, new SystemSatelliteCommand(0, "b1"));
        sim.Step();
        Assert.Equal(3, sim.RefusedCommands);
        Assert.Contains("no star system", sim.LastRefusal);
        var other = MatchSetup.CreatePlanetLanding(Data, 1701);
        other.Step();
        Assert.Equal(other.StateHash(), sim.StateHash());
        Assert.NotEqual(hash, sim.StateHash());
    }

    // ---------------------------------------------------------------- fog: satellites and scans

    [Fact]
    public void LiveDataOnlyWithASatellite()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var home = sys.SurfaceBody!;
        var target = sys.Bodies.First(b => b.Id != home).Id;
        Assert.False(sys.HasLiveData(0, home));
        Assert.False(sys.HasLiveData(0, target));
        Assert.StartsWith("the ship is not in orbit of", sys.SatelliteProblem(0, target));

        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, target));
        Run(sim, (int)sys.TravelTicks(home, target) + 2);
        Assert.Equal(target, ship.AtBody);
        Assert.False(sys.HasLiveData(0, target)); // charted, not live
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, target));
        var stock = sim.Players[0].Stock[Data.Resource("duranium").Index];
        Assert.Null(sys.SatelliteProblem(0, target));
        sim.Schedule(sim.Tick, new SystemSatelliteCommand(0, target));
        sim.Step();
        Assert.True(sys.HasLiveData(0, target));
        Assert.Single(sys.Satellites);
        Assert.Equal(stock - Fix64.FromInt(60), sim.Players[0].Stock[Data.Resource("duranium").Index]);
        Assert.StartsWith("a satellite is already at", sys.SatelliteProblem(0, target));

        // Leaving keeps the satellite there (live for good) and the chart.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, home));
        Run(sim, (int)sys.TravelTicks(home, target) + 2);
        Assert.True(sys.HasLiveData(0, target));
        Assert.False(sys.HasLiveData(0, home));
        Assert.False(sys.HasLiveData(1, target));
        Assert.True(sys.Report(0, target)!.Live);
    }

    [Fact]
    public void TheColonysUplinkGivesLiveDataForItsBody()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var tile = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, tile));
        Run(sim, TestData.ReadyTicks(Data) + 2);
        Assert.True(sim.Surface.UplinkOnline(0, sim.Tick));
        Assert.True(sys.HasLiveData(0, sys.SurfaceBody!));
        // The colony's surveyed deposits join the class's yields.
        var possible = sys.PossibleResources(0, sys.SurfaceBody!);
        var cls = Data.PlanetClass(sys.Body(sys.SurfaceBody)!.ClassId);
        Assert.Equal(cls.Yields.Where(y => Data.Resources.Any(r => r.Id == y)), possible.Take(cls.Yields.Count));
        var map = sim.Surface.Map;
        foreach (var t in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.Players[0].Surveyed[t] && map.DepositResource[t] >= 0))
            Assert.Contains(Data.Resources[map.DepositResource[t]].Id, possible);
    }

    [Fact]
    public void ReportsShowOnlyWhatIsKnown()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var other = sys.Bodies.First(b => b.Id != sys.SurfaceBody);
        var detected = sys.Report(0, other.Id)!;
        Assert.Equal(BodyKnowledge.Detected, detected.Knowledge);
        Assert.Null(detected.Name);
        Assert.Null(detected.ClassId);
        Assert.Empty(detected.Resources);
        Assert.Empty(sys.PossibleResources(0, other.Id));
        Assert.Equal(other.Position, detected.Position);
        Assert.Equal(StarSystemState.UnknownBodyName, sys.NameFor(0, other.Id));
        var home = sys.Report(0, sys.SurfaceBody!)!;
        Assert.Equal(BodyKnowledge.Charted, home.Knowledge);
        Assert.Equal(sys.Body(sys.SurfaceBody)!.Name, home.Name);
        Assert.True(home.Landable);
        Assert.NotNull(home.GravityG);
        Assert.NotNull(home.MeanTemperatureC);
        Assert.Equal(Data.PlanetClass(home.ClassId!).Yields.Where(y => Data.Resources.Any(r => r.Id == y)), home.Resources);
        Assert.Equal(sys.MoonsOf(sys.SurfaceBody!).Select(m => m.Id), home.Moons);
        Assert.False(string.IsNullOrEmpty(home.Zone));
    }

    [Fact]
    public void ALongRangeScanChartsWhatIsInRangeThenRecharges()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var range = Data.SystemRules!.ScanRange;
        var at = sys.Body(ship.AtBody)!.Position;
        var inRange = sys.Bodies.Where(b => FixVec2.Distance(b.Position, at) <= range).Select(b => b.Id).ToList();
        var outOfRange = sys.Bodies.Where(b => FixVec2.Distance(b.Position, at) > range).Select(b => b.Id).ToList();
        Assert.True(inRange.Count > 1 && outOfRange.Count > 0, "seed 1701 has bodies on both sides of the range");
        Assert.Equal(inRange, sys.ScanTargets(ship));
        sim.Schedule(sim.Tick, new LongRangeScanCommand(0, ship.Id));
        sim.Step();
        foreach (var id in inRange) Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, id));
        foreach (var id in outOfRange) Assert.Equal(BodyKnowledge.Detected, sys.KnowledgeOf(0, id));
        Assert.StartsWith("the long-range scanners are recharging", sys.ScanProblem(0, ship.Id));
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, new LongRangeScanCommand(0, ship.Id));
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        Run(sim, Data.SystemRules.ScanCooldownSeconds * Simulation.TickRate);
        Assert.Null(sys.ScanProblem(0, ship.Id));
        // Not in flight.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, outOfRange[0]));
        sim.Step();
        Assert.StartsWith("the ship is in flight", sys.ScanProblem(0, ship.Id));
    }

    // ---------------------------------------------------------------- orbit actions

    [Fact]
    public void OrbitActionsOnlyAtTheColonysBody()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var surface = sim.Surface!;
        var ship = sys.FlagshipOf(0)!;
        var home = sys.SurfaceBody!;
        var name = sys.Body(home)!.Name;
        var away = sys.Bodies.First(b => b.Id != home).Id;
        var tile = Enumerable.Range(0, surface.Map.Grid.Count).First(t => surface.CanLand(0, t));
        Assert.Null(surface.LandProblem(sim, 0, tile));
        Assert.Null(surface.SatelliteProblem(sim, 0, 10));

        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, away));
        sim.Step();
        var text = $"the ship is not in orbit of {name}";
        Assert.Equal(text, sys.OrbitProblem(0));
        Assert.Equal(text, surface.LandProblem(sim, 0, tile)); // in flight
        Assert.Equal(text, surface.SatelliteProblem(sim, 0, 10));
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, new LandColonyCommand(0, tile));
        sim.Schedule(sim.Tick, new SatelliteCommand(0, 10));
        sim.Step();
        Assert.Equal(refused + 2, sim.RefusedCommands);
        Assert.Equal(LandingPhase.InOrbit, surface.Players[0].Phase);
        Assert.Empty(surface.Satellites);

        Run(sim, (int)sys.TravelTicks(home, away) + 2);
        Assert.Equal(away, ship.AtBody);
        Assert.Equal(text, surface.LandProblem(sim, 0, tile)); // in orbit elsewhere

        // Back home: land, then the drop ship, outpost drop and transporter work only while the ship is there.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, home));
        Run(sim, (int)sys.TravelTicks(home, away) + 2);
        Assert.Null(surface.LandProblem(sim, 0, tile));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, tile));
        Run(sim, TestData.ReadyTicks(Data) + 2);
        Assert.Equal(LandingPhase.Landed, surface.Players[0].Phase);
        var near = surface.Map.Grid.Within(tile, 3).First(t => surface.Map.Terrain(t).Passable && surface.StructureAt(t) == null && surface.Map.Grid.Distance(t, tile) >= 2);
        var shuttleHome = surface.ShuttleProblem(sim, 0, near, "survey_rover");
        var beamHome = surface.BeamProblem(sim, 0, near, "survey_team");
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, away));
        sim.Step();
        Assert.Equal(text, surface.ShuttleProblem(sim, 0, near, "survey_rover"));
        Assert.Equal(text, surface.BeamProblem(sim, 0, near, "survey_team"));
        Assert.Equal(text, surface.OutpostProblem(sim, 0, near));
        Assert.NotEqual(text, shuttleHome);
        Assert.NotEqual(text, beamHome);
    }

    [Fact]
    public void PlanetOnlyGamesHaveNoOrbitGate()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var tile = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        Assert.Null(sim.Surface.LandProblem(sim, 0, tile));
        Assert.Null(sim.Surface.SatelliteProblem(sim, 0, 5));
    }

    // ---------------------------------------------------------------- traffic, determinism, hash

    [Fact]
    public void NeutralTrafficIsDeterministic()
    {
        var a = MatchSetup.CreateSystemGame(Data, 1701);
        var b = MatchSetup.CreateSystemGame(Data, 1701);
        var hashes = new List<ulong>();
        for (var i = 0; i < 6000; i++)
        {
            a.Step();
            b.Step();
            if (i % 300 == 0)
            {
                Assert.Equal(a.StateHash(), b.StateHash());
                hashes.Add(a.StateHash());
            }
        }
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        var neutral = a.System!.Ships.Where(s => s.IsNeutral).ToList();
        Assert.True(neutral.Sum(s => s.Trips) >= neutral.Count, "they fly between the bodies");
        foreach (var s in neutral)
        {
            Assert.True(s.AtBody != null ? a.System.Body(s.AtBody) != null : a.System.Body(s.ToBody) != null && a.System.Body(s.FromBody) != null);
            if (s.AtBody == null) Assert.NotEqual(s.FromBody, s.ToBody);
        }
        // Neutral ships never chart anything for a player.
        var c = MatchSetup.CreateSystemGame(Data, 1701);
        Run(c, 6000);
        Assert.Equal(1, c.System!.Bodies.Count(x => c.System.KnowledgeOf(0, x.Id) == BodyKnowledge.Charted));
        // Traffic doesn't touch the game's random stream.
        Assert.Equal(MatchSetup.CreatePlanetLanding(Data, 1701).Rng.NextULong(), MatchSetup.CreateSystemGame(Data, 1701).Rng.NextULong());
    }

    [Fact]
    public void TheSystemIsInTheStateHash()
    {
        var a = MatchSetup.CreateSystemGame(Data, 1);
        var b = MatchSetup.CreateSystemGame(Data, 1);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), MatchSetup.CreateSystemGame(Data, 2).StateHash());
        var ship = a.System!.FlagshipOf(0)!;
        var target = a.System.Bodies.First(x => x.Id != a.System.SurfaceBody).Id;
        a.Schedule(a.Tick + 3, new SetCourseCommand(0, ship.Id, target));
        Assert.NotEqual(a.StateHash(), b.StateHash()); // waiting in the queue
        b.Schedule(b.Tick + 3, new SetCourseCommand(0, ship.Id, target));
        Assert.Equal(a.StateHash(), b.StateHash());
        Run(a, 5);
        Run(b, 5);
        Assert.Equal(a.StateHash(), b.StateHash());
        var before = a.StateHash();
        ship.Cargo = "contraband";
        Assert.NotEqual(before, a.StateHash());
        ship.Cargo = null;
        Assert.Equal(before, a.StateHash());

        // Same tick, different satellite / scan / knowledge => different hash.
        var c = MatchSetup.CreateSystemGame(Data, 1);
        var d = MatchSetup.CreateSystemGame(Data, 1);
        c.Schedule(0, new LongRangeScanCommand(0, c.System!.FlagshipOf(0)!.Id));
        c.Step();
        d.Step();
        Assert.NotEqual(c.StateHash(), d.StateHash());
    }

    [Fact]
    public void RandomSystemCommandsNeverThrowAndStayDeterministic()
    {
        List<ulong> Play()
        {
            var sim = MatchSetup.CreateSystemGame(Data, 4242);
            sim.Schedule(0, new DebugAddPlayerCommand(0, "cardassian"));
            var rng = new DeterministicRng(99);
            var hashes = new List<ulong>();
            var ids = new List<string?> { null, "", "nowhere" };
            ids.AddRange(sim.System!.Bodies.Select(b => b.Id));
            for (var tick = 0; tick < 4000; tick++)
            {
                if (rng.NextInt(4) == 0)
                {
                    var p = rng.NextInt(-1, 4);
                    var ship = rng.NextInt(-1, 12);
                    var body = ids[rng.NextInt(ids.Count)];
                    var tile = rng.NextInt(-5, sim.Surface!.Map.Grid.Count + 5);
                    Command c = rng.NextInt(8) switch
                    {
                        0 or 1 or 2 => new SetCourseCommand(p, ship, body!),
                        3 => new LongRangeScanCommand(p, ship),
                        4 => new SystemSatelliteCommand(p, body!),
                        5 => new LandColonyCommand(p, tile),
                        6 => new SatelliteCommand(p, rng.NextInt(-2, 100)),
                        _ => new ShuttleCommand(p, tile, "survey_rover"),
                    };
                    sim.Schedule(sim.Tick, c);
                }
                sim.Step();
                if (tick % 200 == 0) hashes.Add(sim.StateHash());
            }
            Assert.Empty(CommandFuzzTests.Problems(sim));
            Assert.Equal(2, sim.System!.Ships.Count(s => !s.IsNeutral)); // the added Cardassian got a ship too
            return hashes;
        }
        Assert.Equal(Play(), Play());
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void TheSystemDataPassesItsChecks()
    {
        Assert.NotNull(Data.SystemGenerator);
        Assert.NotNull(Data.SystemRules);
        foreach (var seed in DataChecks.PlanetSeeds) Assert.Empty(DataChecks.SystemProblems(Data, seed));
        Assert.Equal(new[] { 20, 90 }, new[] { Data.SystemRules!.TravelMinSeconds, Data.SystemRules.TravelMaxSeconds });
        Assert.Contains(Data.TrafficClasses(), c => c.Role == "tanker");
    }

    [Theory]
    [InlineData("\"weights\": { \"class_m\": 5,", "\"weights\": { \"class_q\": 5,", "unknown planet class 'class_q'")]
    [InlineData("\"distance\": [240, 380]", "\"distance\": [250, 380]", "must start where the previous zone")]
    [InlineData("\"landable_fallback\": \"class_l\"", "\"landable_fallback\": \"class_k\"", "landable_fallback 'class_k' has no surface")]
    [InlineData("\"planets\": [5, 8]", "\"planets\": [5, 40]", "planets needs 1 <= min <= max <= 20")]
    [InlineData("\"classes\": { \"G\": 46,", "\"classes\": { \"Q\": 46,", "'Q' is not in climate/stars.json")]
    [InlineData("\"class_j\": [1, 3],", "\"class_j\": [1, 9],", "moons counts class_j")]
    [InlineData("\"seconds\": [20, 90]", "\"seconds\": [90, 20]", "travel seconds needs 1 <= min <= max")]
    [InlineData("\"factions\": [\"federation\", \"ferengi\"", "\"factions\": [\"nobody\", \"ferengi\"", "traffic faction 'nobody' is unknown")]
    public void BrokenSystemDataIsReported(string from, string to, string problem)
    {
        var file = from.Contains("seconds\": [20") || from.Contains("factions\"") ? "maps/system.json" : "maps/generator.json";
        var data = TestData.Patched((file, from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void TheDataSetsTheFlightTimesAndTheScan()
    {
        var data = TestData.Patched(("maps/system.json", "\"seconds\": [20, 90]", "\"seconds\": [30, 30]"),
            ("maps/system.json", "\"range\": 320, \"cooldown_seconds\": 90", "\"range\": 5000, \"cooldown_seconds\": 10"));
        Assert.Empty(data.Validate());
        var sim = MatchSetup.CreateSystemGame(data, 1701);
        var sys = sim.System!;
        foreach (var b in sys.Bodies.Where(b => b.Id != sys.SurfaceBody))
            Assert.Equal(30 * Simulation.TickRate, sys.TravelTicks(sys.SurfaceBody!, b.Id));
        sim.Schedule(0, new LongRangeScanCommand(0, sys.FlagshipOf(0)!.Id));
        sim.Step();
        Assert.All(sys.Bodies, b => Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, b.Id)));
        Assert.Equal(10 * Simulation.TickRate, sys.FlagshipOf(0)!.ScanReadyTick);
    }
}
