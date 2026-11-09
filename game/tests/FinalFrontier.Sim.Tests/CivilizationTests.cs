using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>Civilizations phase 1 (docs/civilizations-contract.md): sites rolled with the system, knowledge and study, towns,
/// hidden observation posts and their discovery, Starfleet Standing, observation research, the state hash.</summary>
public class CivilizationTests
{
    private readonly ITestOutputHelper _out;

    public CivilizationTests(ITestOutputHelper output) => _out = output;

    private static GameData Real => TestData.Real;

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    /// <summary>The first seed whose system (default landing world) has a site like this.</summary>
    private static (ulong Seed, CivSite Site) FindSeed(GameData data, Func<CivSite, bool> want)
    {
        for (ulong seed = 1; seed < 3000; seed++)
        {
            var g = SystemGenerator.Generate(data, seed);
            var site = CivGenerator.Roll(data, g.Bodies, seed, MatchSetup.HomeBody(data, g).Id).FirstOrDefault(want);
            if (site != null) return (seed, site);
        }
        throw new InvalidOperationException("no seed has such a site");
    }

    private static (Simulation Sim, CivSite Site) GameWith(GameData data, Func<CivSite, bool> want, string faction = "federation")
    {
        var (seed, site) = FindSeed(data, want);
        var sim = MatchSetup.CreateSystemGame(data, seed, faction);
        Assert.Equal(site, sim.System!.CivAt(site.BodyId));
        return (sim, site);
    }

    /// <summary>Flies the flagship to the body and waits for its sensor sweep to chart it.</summary>
    private static void GoAndChart(Simulation sim, string bodyId, int player = 0)
    {
        var sys = sim.System!;
        var ship = sys.FlagshipOf(player)!;
        sim.Schedule(sim.Tick, new SetCourseCommand(player, ship.Id, bodyId));
        for (var i = 0; i < 4000 && (ship.AtBody != bodyId || sys.KnowledgeOf(player, bodyId) < BodyKnowledge.Charted); i++) sim.Step();
        Assert.Equal(bodyId, ship.AtBody);
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(player, bodyId));
    }

    private static void Send(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>Discovery every minute, always found (rules.json post).</summary>
    private static GameData AlwaysFound(params (string File, string From, string To)[] more) => TestData.Patched(
        new[] { ("civilizations/rules.json", "\"discovery_minutes\": 10,", "\"discovery_minutes\": 1,"),
                ("civilizations/rules.json", "\"discovery_chance\": 0.1,", "\"discovery_chance\": 1,") }.Concat(more).ToArray());

    // ---------------------------------------------------------------- generation

    [Fact]
    public void RealDataHasCivilizationsAndPassesTheirChecks()
    {
        Assert.Contains("civilizations", GameData.Documents);
        Assert.NotNull(Real.Civilizations);
        Assert.Empty(Real.Validate());
        Assert.Empty(DataChecks.CivilizationProblems(Real));
        Assert.Equal(GameData.CivLevels, Real.Civilizations!.Levels.Count);
    }

    [Fact]
    public void GenerationIsPureAndDeterministic()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var g = SystemGenerator.Generate(Real, seed);
            var home = MatchSetup.HomeBody(Real, g).Id;
            var a = CivGenerator.Roll(Real, g.Bodies, seed, home);
            var b = CivGenerator.Roll(Real, SystemGenerator.Generate(Real, seed).Bodies, seed, home);
            Assert.Equal(a, b);
            foreach (var site in a)
            {
                Assert.Equal(CivGenerator.NationNames(Real, site), CivGenerator.NationNames(Real, site));
                Assert.False(string.IsNullOrWhiteSpace(site.Species));
                Assert.True(char.IsUpper(site.Species[0]), site.Species);
                if (site.Living)
                {
                    Assert.InRange((int)site.Level, 1, 6);
                    Assert.NotEqual(Temperament.Unknown, site.Temperament);
                    Assert.Equal(site.Unity == CivUnity.Unified ? 1 : site.Nations, site.Nations);
                    Assert.Equal(site.Nations, CivGenerator.NationNames(Real, site).Distinct().Count());
                    var l = Real.Civilizations!.Level((int)site.Level);
                    Assert.InRange(site.Population, l.PopulationMin, l.PopulationMax);
                    Assert.Null(site.Ruin);
                }
                else
                {
                    Assert.NotNull(Real.Civilizations!.Ruin(site.Ruin));
                    Assert.Equal(0, site.Population);
                    Assert.Empty(CivGenerator.NationNames(Real, site));
                }
            }
        }
        // A game's system rolls the same sites and doesn't touch the game's random stream.
        var (seed2, _) = FindSeed(Real, s => s.Living);
        var x = MatchSetup.CreateSystemGame(Real, seed2);
        var y = MatchSetup.CreateSystemGame(Real, seed2);
        Assert.Equal(x.System!.Civilizations, y.System!.Civilizations);
        Assert.Equal(new DeterministicRng(seed2).NextULong(), x.Rng.NextULong());
    }

    [Fact]
    public void SitesNeverSitOnTheLandingWorldAndAboutHalfOfSystemsHaveNatives()
    {
        int systems = 0, living = 0, ruins = 0;
        var c = Real.Civilizations!;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var g = SystemGenerator.Generate(Real, seed);
            foreach (var home in g.Landable(Real))
            {
                var sites = CivGenerator.Roll(Real, g.Bodies, seed, home.Id);
                Assert.DoesNotContain(sites, s => s.BodyId == home.Id);
                Assert.Equal(sites.Count, sites.Select(s => s.BodyId).Distinct().Count());
                Assert.True(sites.Count(s => s.Living) <= 1 && sites.Count(s => !s.Living) <= 1);
                foreach (var s in sites)
                {
                    var cls = g.Body(s.BodyId)!.ClassId;
                    var weights = s.Living ? c.Sites.LivingWeights : c.Sites.RuinsWeights;
                    Assert.Contains(weights, w => w.Id == cls && w.Weight > 0);
                    if (s.Living) Assert.True(Real.IsLandableClass(cls) || c.Sites.TownMapFor(cls) != null, $"{cls} has no town map");
                }
                // In body order.
                Assert.Equal(sites.Select(s => g.Bodies.ToList().FindIndex(b => b.Id == s.BodyId)).OrderBy(i => i), sites.Select(s => g.Bodies.ToList().FindIndex(b => b.Id == s.BodyId)));
            }
            var defaultSites = CivGenerator.Roll(Real, g.Bodies, seed, MatchSetup.HomeBody(Real, g).Id);
            systems++;
            if (defaultSites.Any(s => s.Living)) living++;
            if (defaultSites.Any(s => !s.Living)) ruins++;
        }
        _out.WriteLine($"living {living} / {systems}, ruins {ruins} / {systems}");
        Assert.InRange((double)living / systems, 0.38, 0.62);
        Assert.InRange((double)ruins / systems, 0.6, 0.9);
    }

    [Fact]
    public void TheDataCheckCatchesOddsOutsideTheBand()
    {
        var none = TestData.Patched(("civilizations/rules.json", "\"living_chance\": 0.58,", "\"living_chance\": 0,"));
        Assert.Empty(none.Validate());
        Assert.Contains(DataChecks.CivilizationProblems(none), p => p.Contains("expected_living_share"));
        var bad = TestData.Patched(("civilizations/rules.json", "\"class_k\": \"class_l\"", "\"class_k\": \"class_k\""));
        Assert.Contains(bad.Validate(), p => p.Contains("town_maps"));
        var noLevel = TestData.Patched(("civilizations/levels.json", "\"level\": 6,", "\"level\": 7,"));
        Assert.Contains(noLevel.Validate(), p => p.Contains("level must be 6"));
    }

    [Fact]
    public void TownsSitOnLandSpreadOutBySizeAndLevel()
    {
        var (sim, site) = GameWith(Real, s => s.Living);
        var sys = sim.System!;
        var towns = sys.TownsOn(site.BodyId);
        Assert.Same(towns, sys.TownsOn(site.BodyId)); // cached
        var level = Real.Civilizations!.Level((int)site.Level);
        Assert.InRange(towns.Count, 1, level.TownsMax);
        var map = sys.TownMap(site.BodyId)!;
        Assert.Same(map, sys.TownMap(site.BodyId));
        foreach (var t in towns)
        {
            Assert.True(map.Terrain(t.Tile).Passable && !map.IsWater(t.Tile));
            Assert.InRange(t.Size, level.SizeMin, level.SizeMax);
            Assert.Equal(level.Lit, t.Lit);
        }
        Assert.Equal(towns.Count, towns.Select(t => t.Tile).Distinct().Count());
        for (var i = 0; i < towns.Count; i++)
            for (var j = i + 1; j < towns.Count; j++)
                Assert.True(map.Grid.Distance(towns[i].Tile, towns[j].Tile) >= 2, "towns are spread out");
        // The same towns in a fresh game, nothing for ruins or a body without a civ.
        var again = MatchSetup.CreateSystemGame(Real, sys.Seed);
        Assert.Equal(towns, again.System!.TownsOn(site.BodyId));
        if (sys.Civilizations.FirstOrDefault(s => !s.Living) is { } ruins) Assert.Empty(sys.TownsOn(ruins.BodyId));
        Assert.Empty(sys.TownsOn(sys.SurfaceBody!));
        _out.WriteLine($"{site.Species} L{(int)site.Level} on {site.BodyId}: {string.Join(", ", towns)}");
    }

    // ---------------------------------------------------------------- knowledge and study

    [Fact]
    public void KnowledgeLevelsGiveTheReportFields()
    {
        var (sim, site) = GameWith(Real, s => s.Living && s.Level >= CivLevel.Industrial);
        var sys = sim.System!;
        // Detected: artificial signals only.
        var signals = sys.CivView(0, site.BodyId)!;
        Assert.True(signals.Signals);
        Assert.False(signals.Charted);
        Assert.Null(signals.Species);
        Assert.Equal(CivLevel.None, signals.LevelMin);

        GoAndChart(sim, site.BodyId);
        var charted = sys.CivView(0, site.BodyId)!;
        Assert.False(charted.Signals);
        Assert.True(charted.Charted && charted.Living);
        Assert.Equal(site.Species, charted.Species);
        Assert.Equal(1, charted.LevelMax - charted.LevelMin);
        Assert.InRange(site.Level, charted.LevelMin, charted.LevelMax);
        Assert.Equal(site.Population, charted.Population);
        Assert.Equal(sys.AwarenessOf(site.BodyId), charted.Awareness);
        Assert.NotEqual("", charted.AwarenessBand);
        Assert.Equal(Temperament.Unknown, charted.Temperament);
        Assert.Equal(CivUnity.Unknown, charted.Unity);
        Assert.Equal(0, charted.Nations);
        Assert.Empty(charted.NationNames);
        Assert.True(charted.Live);
        Assert.NotEmpty(charted.OrbitalSigns);

        // Studied from orbit (~2 min): the exact level, temperament, unity and nations.
        Run(sim, 120 * Simulation.TickRate + 5);
        var studied = sys.CivView(0, site.BodyId)!;
        Assert.Equal(Fix64.One, studied.Study);
        Assert.Equal(site.Level, studied.LevelMin);
        Assert.Equal(site.Level, studied.LevelMax);
        Assert.Equal(site.Temperament, studied.Temperament);
        Assert.Equal(site.Unity, studied.Unity);
        Assert.Equal(site.Nations, studied.Nations);
        Assert.Equal(sys.NationNamesOf(site.BodyId), studied.NationNames);
        Assert.Contains(sys.CivEvents, e => e.Kind == "study_complete" && e.Player == 0 && e.BodyId == site.BodyId);
        // Knowledge is kept after leaving.
        sim.Schedule(sim.Tick, new SetCourseCommand(0, sys.FlagshipOf(0)!.Id, sys.SurfaceBody!));
        Run(sim, 30);
        Assert.Equal(site.Level, sys.CivView(0, site.BodyId)!.LevelMin);
        Assert.False(sys.CivView(0, site.BodyId)!.Live);
    }

    [Fact]
    public void QuietCivilizationsAndRuinsShowNothingUntilCharted()
    {
        var (sim, site) = GameWith(Real, s => s.Living && s.Level <= CivLevel.IronMedieval);
        Assert.Null(sim.System!.CivView(0, site.BodyId));
        Assert.Null(sim.System.CivView(0, sim.System.SurfaceBody!)); // no civ on the landing world

        var (ruinsGame, ruins) = GameWith(Real, s => !s.Living);
        Assert.Null(ruinsGame.System!.CivView(0, ruins.BodyId));
        GoAndChart(ruinsGame, ruins.BodyId);
        var r = ruinsGame.System.CivView(0, ruins.BodyId)!;
        Assert.True(r.Charted);
        Assert.False(r.Living);
        Assert.Equal(ruins.Species, r.Species);
        Assert.Equal(Real.Civilizations!.Ruin(ruins.Ruin)!.Name, r.RuinName);
        Assert.Equal(Fix64.Zero, r.Study);
        Run(ruinsGame, 200);
        Assert.Equal(Fix64.Zero, ruinsGame.System.StudyOf(0, ruins.BodyId)); // nobody to study
        Assert.Equal("only ruins on " + ruinsGame.System.Body(ruins.BodyId)!.Name + ": nobody to observe", ruinsGame.System.PostProblem(0, ruins.BodyId));
    }

    [Fact]
    public void StudyRatesAreShipOneSatelliteTwoPostFour()
    {
        var (sim, site) = GameWith(Real, s => s.Living);
        var sys = sim.System!;
        GoAndChart(sim, site.BodyId);
        Fix64 Measure(int ticks)
        {
            var before = sys.StudyOf(0, site.BodyId);
            Run(sim, ticks);
            return sys.StudyOf(0, site.BodyId) - before;
        }
        var perTick = 1.0 / (120 * Simulation.TickRate);
        var ship = Measure(150).ToDouble();
        Assert.InRange(ship, 150 * perTick * 0.99, 150 * perTick * 1.01);
        Send(sim, new SystemSatelliteCommand(0, site.BodyId));
        Assert.Contains(sys.Satellites, s => s.BodyId == site.BodyId);
        var withSatellite = Measure(150).ToDouble();
        Assert.InRange(withSatellite, 3 * 150 * perTick * 0.99, 3 * 150 * perTick * 1.01);
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Assert.Single(sys.Posts);
        var withPost = Measure(100).ToDouble();
        Assert.InRange(withPost, 7 * 100 * perTick * 0.99, 7 * 100 * perTick * 1.01);
        Run(sim, 400);
        Assert.Equal(Fix64.One, sys.StudyOf(0, site.BodyId)); // capped at 1
    }

    [Fact]
    public void ObservationResearchGoesIntoTheActiveTechOrWaits()
    {
        var (sim, site) = GameWith(Real, s => s.Living);
        var sys = sim.System!;
        var c = Real.Civilizations!;
        var surface = sim.Surface!;
        var tech = Real.Techs.First(t => t.Requires.Count == 0 && t.Cost > Fix64.FromInt(50));
        GoAndChart(sim, site.BodyId);
        // No tech yet: the research waits in the bank.
        var rate = c.Study.ObservePerMin[(int)site.Level - 1];
        Assert.Equal(rate, sys.ObservationResearchPerMinute(0));
        Run(sim, Simulation.TicksPerMinute / 2);
        Assert.InRange(sys.ObservationBank(0).ToDouble(), rate.ToDouble() / 2 * 0.97, rate.ToDouble() / 2 * 1.03);
        // A tech (relayed with the signal delay): the bank goes into it, then the flow.
        Send(sim, new ResearchCommand(0, tech.Id));
        Run(sim, (int)sys.CommsDelayTicks(0) + 2);
        Assert.Equal(tech.Index, surface.Players[0].ActiveTech);
        Assert.Equal(Fix64.Zero, sys.ObservationBank(0));
        var before = surface.Players[0].ResearchProgress[tech.Index];
        Run(sim, 300);
        var gained = (surface.Players[0].ResearchProgress[tech.Index] - before).ToDouble();
        Assert.InRange(gained, rate.ToDouble() * 300 / Simulation.TicksPerMinute * 0.97, rate.ToDouble() * 300 / Simulation.TicksPerMinute * 1.03);
        // Study done: orbit observation stops paying; a post pays on.
        Run(sim, 120 * Simulation.TickRate);
        Assert.Equal(Fix64.Zero, sys.ObservationResearchPerMinute(0));
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Assert.Equal(c.Study.PostPerMin[(int)site.Level - 1], sys.ObservationResearchPerMinute(0));
    }

    // ---------------------------------------------------------------- posts

    [Fact]
    public void PostsAreRefusedWithReasonsAndCostLikeASatellite()
    {
        var (sim, site) = GameWith(Real, s => s.Living);
        var sys = sim.System!;
        var name = sys.Body(site.BodyId)!.Name;
        Assert.Equal($"the ship is not in orbit of {sys.NameFor(0, site.BodyId)}", sys.PostProblem(0, site.BodyId));
        Assert.Equal("no such body", sys.PostProblem(0, "nowhere"));
        Assert.Equal("no such player", sys.PostProblem(3, site.BodyId));
        Assert.Equal($"no civilization on {sys.Body(sys.SurfaceBody!)!.Name} to observe", sys.PostProblem(0, sys.SurfaceBody!));
        // Arrived but still sweeping.
        var ship = sys.FlagshipOf(0)!;
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, site.BodyId));
        while (ship.AtBody != site.BodyId) sim.Step();
        Assert.Equal($"finish the sensor sweep of {name} first", sys.PostProblem(0, site.BodyId));
        Run(sim, (int)sys.SweepTicks + 2);
        Assert.Null(sys.PostProblem(0, site.BodyId));

        var stock = sim.Players[0].Stock;
        var cost = Real.Civilizations!.Post.Cost;
        var before = cost.Select(a => stock[a.Resource]).ToList();
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Assert.Single(sys.Posts);
        for (var k = 0; k < cost.Count; k++) Assert.Equal(before[k] - cost[k].Amount, stock[cost[k].Resource]);
        Assert.Equal($"a post is already hidden on {name}", sys.PostProblem(0, site.BodyId));
        Assert.True(sys.CivView(0, site.BodyId)!.PostHere);
        Assert.Contains(sys.CivEvents, e => e.Kind == "post_placed");

        // Short of resources.
        var (poor, poorSite) = GameWith(Real, s => s.Living);
        GoAndChart(poor, poorSite.BodyId);
        foreach (var a in cost) poor.Players[0].Stock.Add(a.Resource, -poor.Players[0].Stock[a.Resource]);
        Assert.Equal("not enough resources", poor.System!.PostProblem(0, poorSite.BodyId));

        // Not the Federation; planet-only games have no system.
        var (klingon, kSite) = GameWith(Real, s => s.Living, "cardassian");
        Assert.Equal("only the Federation can place hidden observation posts for now", klingon.System!.PostProblem(0, kSite.BodyId));
        var planet = MatchSetup.CreatePlanetLanding(Real, 1701, "federation", "class_m", "tarsis");
        Send(planet, new ObservationPostCommand(0, "b1"));
        Assert.Contains("no star system", planet.LastRefusal);
    }

    [Fact]
    public void RefusedPostCommandsChangeNothing()
    {
        var (seed, site) = FindSeed(Real, s => s.Living);
        var a = MatchSetup.CreateSystemGame(Real, seed);
        var b = MatchSetup.CreateSystemGame(Real, seed);
        var bad = new Command[]
        {
            new ObservationPostCommand(0, site.BodyId), // not in orbit
            new ObservationPostCommand(0, "nowhere"),
            new ObservationPostCommand(0, a.System!.SurfaceBody!),
            new ObservationPostCommand(5, site.BodyId),
            new ObservationPostCommand(0, ""),
        };
        foreach (var c in bad)
        {
            var refused = a.RefusedCommands;
            a.Schedule(a.Tick, c);
            a.Step();
            b.Step();
            Assert.Equal(refused + 1, a.RefusedCommands);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.Empty(a.System.Posts);
    }

    [Fact]
    public void AFoundPostRaisesAwarenessCostsStandingAndStopsWorking()
    {
        var data = AlwaysFound();
        var (sim, site) = GameWith(data, s => s.Living);
        var sys = sim.System!;
        var c = data.Civilizations!;
        GoAndChart(sim, site.BodyId);
        Assert.Equal(c.Standing.Start, sys.StandingOf(0));
        var awareness = sys.AwarenessOf(site.BodyId);
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        var post = sys.Posts.Single();
        Assert.Equal(60, sys.SecondsToNextRoll(post));
        Run(sim, Simulation.TicksPerMinute);
        Assert.True(post.Found);
        Assert.True(sys.AwarenessOf(site.BodyId) >= awareness + c.Awareness.PostFound);
        Assert.Equal(1, sys.IncidentsOn(0, site.BodyId));
        Assert.Equal(c.Standing.Start + c.Post.StandingChange, sys.StandingOf(0));
        Assert.Equal(1, sys.StandingRung(0)); // 60: warning
        Assert.Contains(sys.CivEvents, e => e.Kind == "post_found" && e.Player == 0);
        Assert.Contains(sys.CivEvents, e => e.Kind == "incident" && e.Player == 0);
        Assert.Contains(sys.CivEvents, e => e.Kind == "standing" && e.Text.Contains("Warning"));
        var report = sys.CivView(0, site.BodyId)!;
        Assert.False(report.PostHere);
        Assert.True(report.PostFound);
        Assert.Equal(1, report.Incidents);
        // A found post no longer studies or pays (the ship leaves, so only the post would).
        sim.Schedule(sim.Tick, new SetCourseCommand(0, sys.FlagshipOf(0)!.Id, sys.SurfaceBody!));
        Run(sim, 5);
        var study = sys.StudyOf(0, site.BodyId);
        Run(sim, 200);
        Assert.Equal(study, sys.StudyOf(0, site.BodyId));
        Assert.Equal(Fix64.Zero, sys.ObservationResearchPerMinute(0));
    }

    [Fact]
    public void StandingLadderInquiryHoldsTheShipAndAThirdIncidentRecallsForGood()
    {
        var data = AlwaysFound();
        var (sim, site) = GameWith(data, s => s.Living);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        GoAndChart(sim, site.BodyId);
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(60, sys.StandingOf(0));
        Send(sim, new ObservationPostCommand(0, site.BodyId)); // a new post where the old one was found
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(40, sys.StandingOf(0));
        Assert.Equal(2, sys.StandingRung(0));
        Assert.True(sys.InquirySecondsLeft(0) > 200);
        Assert.StartsWith("held for a Starfleet inquiry", sys.CourseProblem(0, ship.Id, sys.SurfaceBody!));
        Assert.StartsWith("held for a Starfleet inquiry", sys.ScanProblem(0, ship.Id));
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Run(sim, Simulation.TicksPerMinute);
        // Third incident on one world: recalled for good, whatever the Standing.
        Assert.Equal(3, sys.IncidentsOn(0, site.BodyId));
        Assert.Equal(5, sys.StandingRung(0));
        Assert.True(sys.Recalled(0));
        Assert.Equal("recalled to Starfleet", sys.CourseProblem(0, ship.Id, sys.SurfaceBody!));
        Assert.Equal("recalled to Starfleet", sys.PostProblem(0, site.BodyId));
        Assert.Equal("recalled to Starfleet", sys.SatelliteProblem(0, site.BodyId));
        Assert.Contains(sys.CivEvents, e => e.Kind == "recalled");
        Assert.Equal(Fix64.Zero, sys.ObservationResearchPerMinute(0));
        var standing = sys.StandingOf(0);
        Run(sim, 2 * Simulation.TicksPerMinute);
        Assert.Equal(standing, sys.StandingOf(0)); // no recovery once recalled
        Assert.Equal(100, sys.AwarenessOf(site.BodyId)); // 3 × post_found, capped
    }

    [Fact]
    public void StandingRecoversWithNoIncidentsUpToItsStart()
    {
        var data = AlwaysFound(("civilizations/rules.json", "\"recovery_per_min\": 1,", "\"recovery_per_min\": 10,"),
            ("civilizations/rules.json", "\"recovery_after_minutes\": 10,", "\"recovery_after_minutes\": 1,"));
        var (sim, site) = GameWith(data, s => s.Living);
        var sys = sim.System!;
        GoAndChart(sim, site.BodyId);
        Send(sim, new ObservationPostCommand(0, site.BodyId));
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(60, sys.StandingOf(0));
        Run(sim, Simulation.TicksPerMinute - 10); // the quiet minute isn't over
        Assert.Equal(60, sys.StandingOf(0));
        Run(sim, 10 + Simulation.TicksPerMinute);
        Assert.InRange(sys.StandingOf(0)!.Value, 69, 70);
        Run(sim, 2 * Simulation.TicksPerMinute);
        Assert.Equal(80, sys.StandingOf(0));
        Assert.Equal(0, sys.StandingRung(0));
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(80, sys.StandingOf(0)); // never above the start
    }

    [Fact]
    public void EraBeforeThePrimeDirectiveAndOtherPowersHaveNoStanding()
    {
        var ent = GameData.Load(TestData.Source, new DataSetup(Era: "ent"));
        var always = AlwaysFound();
        Assert.Equal("ent", ent.Orbit!.Era);
        var (sim, site) = GameWith(ent, s => s.Living);
        var sys = sim.System!;
        Assert.False(sys.PrimeDirective);
        Assert.Null(sys.StandingOf(0));
        Assert.Equal(0, sys.StandingRung(0));
        Assert.True(MatchSetup.CreateSystemGame(always, 1701).System!.PrimeDirective);
        // Incidents still count, but nobody is recalled.
        var patched = GameData.Load(PatchedDir(), new DataSetup(Era: "ent"));
        var (game, s2) = GameWith(patched, s => s.Living);
        GoAndChart(game, s2.BodyId);
        for (var k = 0; k < 3; k++)
        {
            Send(game, new ObservationPostCommand(0, s2.BodyId));
            Run(game, Simulation.TicksPerMinute);
        }
        Assert.Equal(3, game.System!.IncidentsOn(0, s2.BodyId));
        Assert.Null(game.System.StandingOf(0));
        Assert.False(game.System.Recalled(0));
        Assert.Null(game.System.PostProblem(0, s2.BodyId));
        Assert.DoesNotContain(game.System.CivEvents, e => e.Kind is "standing" or "recalled");
        // A Cardassian captain in a Prime Directive era: no Standing either.
        var (cardassian, _) = GameWith(Real, s => s.Living, "cardassian");
        Assert.Null(cardassian.System!.StandingOf(0));
    }

    private static string PatchedDir()
    {
        var dir = TestData.CopyData();
        TestData.Replace(dir, "civilizations/rules.json", "\"discovery_minutes\": 10,", "\"discovery_minutes\": 1,");
        TestData.Replace(dir, "civilizations/rules.json", "\"discovery_chance\": 0.1,", "\"discovery_chance\": 1,");
        return dir;
    }

    [Fact]
    public void AwarenessRisesFromWhatTheNativesCanSee()
    {
        var (sim, site) = GameWith(Real, s => s.Living && s.Level >= CivLevel.EarlyWarp);
        var sys = sim.System!;
        var c = Real.Civilizations!;
        Assert.Equal(c.Awareness.Start[(int)site.Level - 1], sys.AwarenessOf(site.BodyId));
        GoAndChart(sim, site.BodyId);
        var before = sys.AwarenessOf(site.BodyId);
        Send(sim, new SystemSatelliteCommand(0, site.BodyId));
        Run(sim, 2 * Simulation.TicksPerMinute);
        // Ship (L5+) and satellite (L4+) seen: ship_per_min + satellite_per_min.
        var expected = 2 * (c.Awareness.ShipPerMin + c.Awareness.SatellitePerMin).ToDouble();
        Assert.InRange(sys.AwarenessOf(site.BodyId) - before, expected - 1.5, expected + 1);
    }

    // ---------------------------------------------------------------- determinism and the hash

    [Fact]
    public void SameCommandsSameGameWithDiscoveryRolls()
    {
        var data = TestData.Patched(("civilizations/rules.json", "\"discovery_minutes\": 10,", "\"discovery_minutes\": 1,"),
            ("civilizations/rules.json", "\"discovery_chance\": 0.1,", "\"discovery_chance\": 0.4,"));
        var (seed, site) = FindSeed(data, s => s.Living);
        var games = new[] { MatchSetup.CreateSystemGame(data, seed), MatchSetup.CreateSystemGame(data, seed) };
        foreach (var g in games) GoAndChart(g, site.BodyId);
        Assert.Equal(games[0].StateHash(), games[1].StateHash());
        for (var minute = 0; minute < 6; minute++)
        {
            foreach (var g in games)
            {
                if (g.System!.PostProblem(0, site.BodyId) == null) g.Schedule(g.Tick, new ObservationPostCommand(0, site.BodyId));
                Run(g, Simulation.TicksPerMinute);
            }
            Assert.Equal(games[0].StateHash(), games[1].StateHash());
        }
        Assert.Equal(games[0].System!.CivEvents, games[1].System!.CivEvents);
        _out.WriteLine(string.Join("\n", games[0].System!.CivEvents));
    }

    [Fact]
    public void CivilizationStateIsHashedOnlyInSystemGames()
    {
        // A data set without the document.
        var dir = TestData.CopyData();
        var manifest = Path.Combine(dir, DataManifest.FileName);
        var text = File.ReadAllText(manifest);
        var start = text.IndexOf(",\n    \"civilizations\": [", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = text.IndexOf(']', start);
        File.WriteAllText(manifest, text.Remove(start, end + 1 - start));
        Directory.Delete(Path.Combine(dir, "civilizations"), true);
        var without = GameData.Load(dir);
        Assert.Null(without.Civilizations);
        Assert.Empty(without.Validate());

        // Planet-only games (the balance runner's): the same hashes with or without civilizations.
        var a = MatchSetup.CreatePlanetLanding(Real, 1701, "federation", "class_m", "tarsis");
        var b = MatchSetup.CreatePlanetLanding(without, 1701, "federation", "class_m", "tarsis");
        for (var i = 0; i < 300; i++)
        {
            a.Step();
            b.Step();
        }
        Assert.Equal(a.StateHash(), b.StateHash());

        // System games: the sites are in the hash, and study moves it.
        var (seed, site) = FindSeed(Real, s => s.Living);
        var x = MatchSetup.CreateSystemGame(Real, seed);
        var y = MatchSetup.CreateSystemGame(without, seed);
        Assert.Empty(y.System!.Civilizations);
        Assert.NotEqual(x.StateHash(), y.StateHash());
        GoAndChart(x, site.BodyId);
        var h = x.StateHash();
        Assert.True(x.System!.StudyOf(0, site.BodyId) > Fix64.Zero || x.System.KnowledgeOf(0, site.BodyId) == BodyKnowledge.Charted);
        x.Step();
        Assert.NotEqual(h, x.StateHash());
    }
}
