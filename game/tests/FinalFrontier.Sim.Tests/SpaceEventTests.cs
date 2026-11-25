using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using Xunit.Abstractions;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>Space jobs phase 3 (docs/space-jobs-contract.md "Phase 3"; data maps/events.json): events arriving on the
/// system's clock (announced, Detected for everyone), rogue asteroids (pushed aside, charges, a bounded strike on the
/// colony), comets (the tail's window), lifepods (air, least air first), stranded ships (power, escort, bait), anomalies
/// (rings), the five pool jobs on every ship, refusals and determinism.</summary>
public class SpaceEventTests
{
    private readonly ITestOutputHelper _out;

    public SpaceEventTests(ITestOutputHelper output) => _out = output;

    public static readonly string[] Jobs = { "rogue_rock", "catch_the_comet", "pods_in_the_dark", "dead_stick", "into_the_storm" };

    /// <summary>The campaign data with text changed in maps/events.json.</summary>
    internal static GameData Events(params (string From, string To)[] patches)
    {
        var dir = TestData.CopyData();
        try
        {
            foreach (var (from, to) in patches) TestData.Replace(dir, "maps/events.json", from, to);
            return GameData.Load(dir, new DataSetup(Era: "dawn_of_warp", Factions: new[] { "federation" }));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private const string Weights = "\"weights\": { \"rogue_asteroid\": 2, \"comet\": 2, \"lifepods\": 2, \"stranded_ship\": 2, \"anomaly\": 1 }";

    /// <summary>Events of one kind only, arriving fast (calm 5 s, one every 20 s, up to 20 live), no bait: what the pool jobs
    /// are played on (the real clock and placement, only sooner).</summary>
    private static readonly Dictionary<string, GameData> OnlyCache = new();

    private static GameData Only(string kind)
    {
        lock (OnlyCache)
        {
            if (OnlyCache.TryGetValue(kind, out var d)) return d;
            d = Events(("\"calm_seconds\": 300, \"every_seconds\": [300, 420], \"max_live\": 3", "\"calm_seconds\": 5, \"every_seconds\": [20, 20], \"max_live\": 20"),
                (Weights, $"\"weights\": {{ \"{kind}\": 1 }}"), ("\"bait\": 20", "\"bait\": 0"));
            OnlyCache[kind] = d;
            return d;
        }
    }

    private static SpaceSite Spawn(Simulation sim, string kind, int variant = -1)
    {
        var before = sim.System!.Sites.Count;
        Debug(sim, DebugSpaceField.SpawnEvent, kind, variant);
        Assert.Equal(before + 1, sim.System.Sites.Count);
        return sim.System.Sites[^1];
    }

    private static Fix64 Latinum(Simulation sim) => sim.Players[0].Stock[sim.Data.Resource("latinum").Index];

    private static void Land(Simulation sim)
    {
        var map = sim.Surface!.Map;
        Do(sim, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t))));
        for (var i = 0; i <= TestData.ReadyTicks(sim.Data) + Simulation.TickRate; i++) sim.Step();
    }

    // ---------------------------------------------------------------- the clock

    [Fact]
    public void EventsArriveOnTheSystemClockAnnouncedAndDetectedForEveryone()
    {
        var a = Clock(out var notesA);
        var b = Clock(out var notesB);
        Assert.Equal(a, b);
        Assert.Equal(notesA, notesB);
        var ev = Campaign.SpaceSites!.Events!;
        Assert.NotEmpty(notesA);
        Assert.True(notesA[0].Tick >= (long)(ev.CalmSeconds + ev.EveryMin) * Simulation.TickRate, $"first event at {notesA[0].Tick}");
    }

    private List<ulong> Clock(out List<(long Tick, SiteKind Kind, string Site)> notes)
    {
        var sim = Game("tereshkova", 1701);
        var sys = sim.System!;
        var ev = Campaign.SpaceSites!.Events!;
        var seen = new HashSet<string>();
        notes = new List<(long, SiteKind, string)>();
        var hashes = new List<ulong>();
        for (var t = 0; t < (ev.CalmSeconds + ev.EveryMax * 4) * Simulation.TickRate; t++)
        {
            sim.Step();
            Assert.True(sys.LiveEvents() <= ev.MaxLive);
            foreach (var s in sys.Sites)
                if (s.Event && seen.Add(s.Id))
                {
                    Assert.Equal(sim.Tick - 1, s.SpawnTick); // arrived in the step just run
                    Assert.True(sys.SiteKnowledgeOf(0, s.Id) >= SiteKnowledge.Detected, "an event is Detected for everyone");
                    Assert.Contains(sys.StatusOf(0)!.Log, l => l.Tick == s.SpawnTick && l.Text.Contains(s.Name));
                    Assert.Contains(sys.EventNotes, n => n.SiteId == s.Id);
                    notes.Add((s.SpawnTick, s.Kind, s.Id));
                }
            if (t % 600 == 0) hashes.Add(sim.StateHash());
        }
        _out.WriteLine(string.Join(", ", notes.Select(n => $"{n.Tick / Simulation.TickRate}s {n.Kind}")));
        return hashes;
    }

    [Fact]
    public void PhaseOneAndTwoSitesAreWhereTheyWereWithoutThePhase3Data()
    {
        var none = Events(("\"anomalies\": { \"count\": [0, 1]", "\"anomalies\": { \"count\": [0, 0]"));
        var storms = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var real = Game(null, seed).System!.Sites;
            var without = Game(null, seed, data: none).System!.Sites;
            Assert.Equal(without.Select(s => (s.Id, s.Kind, s.Variant, s.Position, s.Size)), real.Where(s => s.Kind != SiteKind.Anomaly).Select(s => (s.Id, s.Kind, s.Variant, s.Position, s.Size)));
            storms += real.Count(s => s.Kind == SiteKind.Anomaly);
            Assert.All(real.Where(s => s.Kind == SiteKind.Anomaly), s => Assert.True(s.PathStart == 0 && !s.Event));
        }
        Assert.InRange(storms, 1, 40); // count [0, 1]: some systems start with a storm
    }

    // ---------------------------------------------------------------- rogue asteroids

    [Fact]
    public void ARogueAsteroidPushedAsideVeersOffAndPays()
    {
        var sim = Game("bastion", 1701);
        var sys = sim.System!;
        var rock = Spawn(sim, "rogue_asteroid");
        Assert.True(rock.Event);
        Assert.Equal(SiteKnowledge.Detected, sys.SiteKnowledgeOf(0, rock.Id));
        var start = rock.Position;
        Seconds(sim, 5);
        Assert.NotEqual(start, rock.Position); // it moves on its path
        Assert.Equal(sys.SitePosition(rock, sim.Tick - 1), rock.Position); // where the last step put it
        Assert.True(sys.EventOf(rock.Id)!.ImpactTick > sim.Tick);
        Identify(sim, rock.Id);
        var ship = Ship(sim);
        Assert.Equal(sys.SitePosition(rock, sim.Tick), sys.ShipPosition(ship, sim.Tick)); // holding at it: going along
        Assert.EndsWith("come alongside first", Refused(sim, new SiteWorkCommand(0, ship.Id, rock.Id, "push")));
        Work(sim, rock.Id, "alongside", () => sys.SiteWork(0, rock.Id)!.Alongside);
        var latinum = Latinum(sim);
        Work(sim, rock.Id, "push", () => rock.Outcome.Length > 0);
        Assert.Equal("deflected", rock.Outcome);
        Assert.Equal(0, rock.OutcomeBy);
        Assert.True(sys.SiteWork(0, rock.Id)!.Deflected);
        Assert.True(Latinum(sim) > latinum, "the reward");
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, rock.Id));
        var at = rock.Position;
        Seconds(sim, 2);
        Assert.True(rock.Position.Length() > at.Length(), "it veers out of the system");
        Assert.Equal(rock.PathEnd, rock.ExpireTick);
        Assert.Equal(-1, sys.EventOf(rock.Id)!.ImpactTick); // no longer on course
        Assert.NotNull(sys.SiteWorkProblem(0, ship.Id, rock.Id, "push"));
    }

    [Fact]
    public void ChargesFromAnAwayTeamTurnTheRockOrFizzle()
    {
        var sim = Game("bastion", 1701);
        var sys = sim.System!;
        var rock = Spawn(sim, "rogue_asteroid");
        Identify(sim, rock.Id);
        Work(sim, rock.Id, "alongside", () => sys.SiteWork(0, rock.Id)!.Alongside);
        Do(sim, new AwayTeamCommand(0, Ship(sim).Id, rock.Id, Team(sim)));
        var fizzle = Campaign.ShipStatus!.Table("charges")!.Outcomes.ToList().FindIndex(o => !o.Deflect);
        Debug(sim, DebugSpaceField.FinishAwayTeam, null, fizzle);
        Assert.Equal("", rock.Outcome); // the charges barely moved it
        Assert.NotNull(sys.AwayTeamProblem(0, rock.Id, null)); // one team per rock
        Assert.Null(sys.SiteWorkProblem(0, Ship(sim).Id, rock.Id, "push")); // the tractor still can

        var rock2 = Spawn(sim, "rogue_asteroid");
        Identify(sim, rock2.Id);
        Work(sim, rock2.Id, "alongside", () => sys.SiteWork(0, rock2.Id)!.Alongside);
        Do(sim, new AwayTeamCommand(0, Ship(sim).Id, rock2.Id, Team(sim)));
        Debug(sim, DebugSpaceField.FinishAwayTeam, null, 0); // turned
        Assert.Equal("deflected", rock2.Outcome);
        Assert.True(sys.SiteWork(0, rock2.Id)!.Deflected);
    }

    [Fact]
    public void ARogueStrikeOnTheColonyIsBoundedDeterministicAndDestroysNothing()
    {
        var a = Strike();
        var b = Strike();
        Assert.Equal(a, b);
    }

    private List<string> Strike()
    {
        var data = Events(("\"flight_seconds\": [360, 480]", "\"flight_seconds\": [60, 60]"), ("\"spread\": 6, \"radius\": 3, \"damage\": [20, 40]", "\"spread\": 0, \"radius\": 1, \"damage\": [99, 99]"));
        var sim = Game("cochrane", 1701, data: data);
        var sys = sim.System!;
        Land(sim);
        var surface = sim.Surface!;
        var structures = surface.Structures.Count;
        var units = surface.Units.Count;
        var hub = surface.Structures.First(s => s.Id == s.BaseId);
        var strikes = 0;
        var log = new List<string>();
        for (var k = 0; k < 40 && strikes < 2; k++)
        {
            var rock = Spawn(sim, "rogue_asteroid");
            if (rock.TargetBody != sys.SurfaceBody) continue; // colony_chance: about half head for the colony
            Until(sim, () => rock.Outcome.Length > 0, 90, "the rock arrives");
            Assert.Equal("impact", rock.Outcome);
            Assert.Equal(sys.Body(sys.SurfaceBody)!.Position, rock.Position);
            strikes++;
            Assert.Equal(hub.Tile, surface.LastImpactTile); // spread 0: on the core's tile
            Assert.Equal(Fix64.One, hub.Condition); // 99 damage from 100: down to 1, never below
            log.Add(sys.StatusOf(0)!.Log.Last(l => l.Text.Contains("struck")).Text);
        }
        Assert.Equal(2, strikes);
        Assert.Equal(structures, surface.Structures.Count); // nothing destroyed
        Assert.Equal(units, surface.Units.Count);
        Assert.All(surface.Structures, s => Assert.True(s.Condition >= Fix64.One));
        Assert.All(surface.Units, u => Assert.True(u.Integrity >= Fix64.One || u.Drone != null));
        Assert.True(surface.Structures.Count(s => s.Condition < Fix64.FromInt(100)) <= data.SpaceSites!.Kind(SiteKind.RogueAsteroid)!.Rogue!.Impact.MaxBuildings);
        log.Add(sim.StateHash().ToString());
        _out.WriteLine(string.Join("\n", log));
        return log;
    }

    // ---------------------------------------------------------------- comets

    [Fact]
    public void ACometsTailCanBeSampledOnlyInItsWindow()
    {
        var sim = Game("meridian", 1701);
        var sys = sim.System!;
        var comet = Spawn(sim, "comet");
        var view = sys.EventOf(comet.Id)!;
        Assert.True(view.WindowStart > sim.Tick && view.WindowEnd > view.WindowStart && view.WindowEnd < view.PathEnd);
        Identify(sim, comet.Id);
        var ship = Ship(sim);
        if (sim.Tick < view.WindowStart)
            Assert.Contains("isn't active yet", Refused(sim, new SiteWorkCommand(0, ship.Id, comet.Id, "sample")));
        Until(sim, () => sys.SiteWorkProblem(0, ship.Id, comet.Id, "sample") == null, 600, "the tail is active");
        var hull = sys.StatusOf(0)!.Hull;
        Do(sim, new SiteWorkCommand(0, ship.Id, comet.Id, "sample"));
        Until(sim, () => sys.SiteWork(0, comet.Id)!.Sampled, 60, "sampled");
        Assert.True(sys.StatusOf(0)!.Hull < hull, "dust in the tail");
        Assert.Contains(sys.StatusOf(0)!.Hold, l => l.Item == "ore:deuterium");
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, comet.Id));
        Assert.Equal("", comet.Outcome); // others can still sample it
        // It leaves at the end of its crossing (the ship holding there keeps it on the chart until it goes).
        Do(sim, new SetCourseCommand(0, ship.Id, sys.Bodies[0].Id));
        Until(sim, () => sys.Site(comet.Id) == null, 900, "the comet left");
    }

    // ---------------------------------------------------------------- lifepods

    [Fact]
    public void LifepodsArePickedUpLeastAirFirstAndLostWhenTheAirRunsOut()
    {
        var sim = Game("tereshkova", 1701);
        var sys = sim.System!;
        var pods = Spawn(sim, "lifepods");
        var all = sys.PodsOf(pods.Id);
        Assert.InRange(all.Count, 6, 12);
        Identify(sim, pods.Id);
        var waiting = sys.PodsOf(pods.Id).Where(p => p.State == "waiting").OrderBy(p => p.AirTick).ThenBy(p => p.Index).ToList();
        Assert.NotEmpty(waiting);
        var crew = sys.StatusOf(0)!.CrewTotal;
        Debug(sim, DebugSpaceField.Crew, "exploration", -3); // a short crew: survivors join it
        Do(sim, new SiteWorkCommand(0, Ship(sim).Id, pods.Id, "pickup"));
        Until(sim, () => sys.SiteWork(0, pods.Id)!.Picked >= 1, 30, "a pod aboard");
        Assert.Equal("taken", sys.PodsOf(pods.Id)[waiting[0].Index].State); // the least air first
        Until(sim, () => pods.Outcome.Length > 0, 900, "no pods left");
        Assert.Equal("cleared", pods.Outcome);
        var end = sys.PodsOf(pods.Id);
        Assert.Equal(end.Count, end.Count(p => p.State != "waiting"));
        Assert.Equal(sys.SiteWork(0, pods.Id)!.Picked, end.Count(p => p.State == "taken"));
        Assert.True(sys.StatusOf(0)!.CrewTotal > crew - 3 || sys.StatusOf(0)!.Hold.Any(l => l.Item.StartsWith(StarSystemState.SurvivorsPrefix)), "survivors aboard");
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, pods.Id));

        // Left alone, the pods' air runs out one by one.
        var late = Spawn(sim, "lifepods");
        Until(sim, () => late.Outcome.Length > 0, 700, "every pod lost");
        Assert.All(sys.PodsOf(late.Id), p => Assert.Equal("lost", p.State));
        Assert.Contains(sys.StatusOf(0)!.Log, l => l.Text.Contains("ran out of air"));
    }

    // ---------------------------------------------------------------- stranded ships

    [Fact]
    public void AStrandedShipPoweredAndEscortedToItsDestinationIsRescued()
    {
        var sim = Game("armstrong", 1701, data: Events(("\"bait\": 20", "\"bait\": 0")));
        var sys = sim.System!;
        var wreck = Spawn(sim, "stranded_ship");
        Assert.True(wreck.AirTick > sim.Tick);
        Identify(sim, wreck.Id);
        var ship = Ship(sim);
        Assert.EndsWith("restore its power first", Refused(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "escort")));
        Work(sim, wreck.Id, "alongside", () => sys.SiteWork(0, wreck.Id)!.Alongside);
        Work(sim, wreck.Id, "restore_power", () => sys.SiteWork(0, wreck.Id)!.Powered);
        Assert.Equal(-1, wreck.AirTick); // its air is safe now
        var plain = sys.TravelTicksFor(ship, ship.AtSite == null ? FixVec2.Zero : wreck.Position, sys.Body(wreck.TargetBody)!.Position);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "escort"));
        Assert.Equal(wreck.Id, sys.EscortOf(0));
        Assert.True(sys.TravelTicksFor(ship, wreck.Position, sys.Body(wreck.TargetBody)!.Position) > plain, "slower with an escort");
        var latinum = Latinum(sim);
        Do(sim, new SetCourseCommand(0, ship.Id, wreck.TargetBody));
        Seconds(sim, 3);
        Assert.Equal(sys.ShipPosition(ship, sim.Tick - 1), wreck.Position); // it follows (as of the last step)
        Until(sim, () => wreck.Outcome.Length > 0, 400, "brought in");
        Assert.Equal("rescued", wreck.Outcome);
        Assert.True(sys.SiteWork(0, wreck.Id)!.Rescued);
        Assert.Null(sys.EscortOf(0));
        Assert.True(Latinum(sim) > latinum);
        Seconds(sim, 1);
        Assert.Null(sys.Site(wreck.Id)); // gone into orbit there
    }

    [Fact]
    public void AnEscortShowsInTheHoldAndCanBeStoppedAndPickedUpAgain()
    {
        var sim = Game("tereshkova", 1701, data: Events(("\"bait\": 20", "\"bait\": 0")));
        var sys = sim.System!;
        var wreck = Spawn(sim, "stranded_ship");
        Identify(sim, wreck.Id);
        var ship = Ship(sim);
        Work(sim, wreck.Id, "alongside", () => sys.SiteWork(0, wreck.Id)!.Alongside);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "restore_power_kit"));
        Assert.True(sys.SiteWork(0, wreck.Id)!.Powered);
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "escort"));
        Assert.Contains(sys.StatusOf(0)!.Hold, l => l.Item == StarSystemState.EscortItem && l.Containers == 0);
        Assert.NotNull(sys.HoldProblem(0, "sell", StarSystemState.EscortItem, 1));
        Do(sim, new ShipHoldCommand(0, ship.Id, "jettison", StarSystemState.EscortItem, 1));
        Assert.Null(sys.EscortOf(0));
        Assert.DoesNotContain(sys.StatusOf(0)!.Hold, l => l.Item == StarSystemState.EscortItem);
        Assert.Null(sys.SiteWorkProblem(0, ship.Id, wreck.Id, "escort")); // still adrift here, still powered
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "escort"));
        Do(sim, new SiteWorkCommand(0, ship.Id, wreck.Id, "release"));
        Assert.Null(sys.EscortOf(0));
    }

    [Fact]
    public void ABaitCallSpringsAnAmbushOnArrival()
    {
        var sim = Game("bastion", 1701, data: Events(("\"bait\": 20", "\"bait\": 100")));
        var sys = sim.System!;
        var bait = Spawn(sim, "stranded_ship");
        var hull = sys.StatusOf(0)!.Hull;
        GoTo(sim, bait.Id);
        Assert.Equal("trap", bait.Outcome);
        Assert.True(sys.StatusOf(0)!.Hull < hull);
        Assert.NotEmpty(sys.StatusOf(0)!.Down);
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, bait.Id));
    }

    [Fact]
    public void AStrandedShipsCrewDiesWhenNobodyComes()
    {
        var sim = Game("tereshkova", 1701);
        var sys = sim.System!;
        var wreck = Spawn(sim, "stranded_ship");
        Until(sim, () => wreck.Outcome.Length > 0, 1000, "the air ran out");
        Assert.Equal("lost", wreck.Outcome);
    }

    // ---------------------------------------------------------------- anomalies

    [Fact]
    public void RecordingAStormPaysByRingAndTheInnerRingHurts()
    {
        var sim = Game("meridian", 1701);
        var sys = sim.System!;
        var storm = Spawn(sim, "anomaly");
        var an = Campaign.SpaceSites!.Kind(SiteKind.Anomaly)!.Anomaly!;
        Identify(sim, storm.Id);
        var ship = Ship(sim);
        var hull = sys.StatusOf(0)!.Hull;
        Do(sim, new SiteWorkCommand(0, ship.Id, storm.Id, "record_outer"));
        Assert.Equal("outer", sys.RecordingRing(0));
        Seconds(sim, an.EverySeconds * 3);
        Assert.Equal(an.Ring("outer")!.Research * 3, sys.SiteWork(0, storm.Id)!.Recorded);
        Assert.Equal(hull, sys.StatusOf(0)!.Hull); // the outer ring is safe
        Do(sim, new SiteWorkCommand(0, ship.Id, storm.Id, "stop"));
        Do(sim, new SiteWorkCommand(0, ship.Id, storm.Id, "record_inner"));
        Seconds(sim, an.EverySeconds * 2);
        Assert.True(sys.StatusOf(0)!.Hull < hull, "the inner ring wears the hull");
        Until(sim, () => sys.SiteWork(0, storm.Id)!.Recorded >= an.ResearchCap || sys.RecordingRing(0) == null, 600, "recorded");
        if (sys.SiteWork(0, storm.Id)!.Recorded >= an.ResearchCap) Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, storm.Id));
        Assert.True(storm.Position != storm.PathFrom || sim.Tick == storm.PathStart, "the storm drifts");
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void RefusedEventCommandsChangeNothing()
    {
        var (a, _) = RefusalGame();
        var (b, ids) = RefusalGame();
        var ship = Ship(b).Id;
        var bad = new Command[]
        {
            new SiteWorkCommand(0, ship, ids[0], "push"), // not there
            new SiteWorkCommand(0, ship, ids[1], "pickup"), // the tractor is down
            new SiteWorkCommand(0, ship, ids[1], "push"), // not a rock
            new SiteWorkCommand(0, ship, ids[1], "record_inner"), // not a storm
            new SiteWorkCommand(0, ship, ids[2], "escort"), // not there
            new SiteWorkCommand(0, ship, ids[3], "sample"), // not there
            new SiteWorkCommand(0, ship, ids[1], "release"), // nothing in tow or escorted
            new SiteWorkCommand(0, ship, ids[1], "escort"), // not a stranded ship
            new SiteWorkCommand(1, ship, ids[1], "pickup"), // not your ship
            new DebugSpaceCommand(0, 0, DebugSpaceField.SpawnEvent, "station", -1), // not an event kind
            new DebugSpaceCommand(0, 0, DebugSpaceField.SpawnEvent, "comet", 7), // no such variant
        };
        foreach (var c in bad) b.Schedule(b.Tick, c);
        a.Step();
        b.Step();
        Assert.Equal(bad.Length, b.RefusedCommands);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    private static (Simulation Sim, string[] Ids) RefusalGame()
    {
        var sim = Game("tereshkova", 1701);
        var ids = new[] { Spawn(sim, "rogue_asteroid").Id, Spawn(sim, "lifepods").Id, Spawn(sim, "stranded_ship").Id, Spawn(sim, "comet").Id };
        Identify(sim, ids[1]);
        Debug(sim, DebugSpaceField.SystemDown, "tractor", 600);
        return (sim, ids);
    }

    // ---------------------------------------------------------------- pool jobs

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ship in new[] { "tereshkova", "armstrong", "bastion", "meridian", "horizon", "cochrane" })
            foreach (var job in Jobs)
                yield return new object[] { ship, job };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryPhase3JobCanBeDoneOnEveryShip(string ship, string job)
    {
        var a = Play(ship, job);
        var b = Play(ship, job);
        Assert.Equal(a, b);
    }

    private static string KindOf(string job) => job switch
    {
        "rogue_rock" => "rogue_asteroid",
        "catch_the_comet" => "comet",
        "pods_in_the_dark" => "lifepods",
        "dead_stick" => "stranded_ship",
        _ => "anomaly",
    };

    private static MissionState State(Simulation sim, string id) => sim.Missions.PoolStates(0).First(s => s.Id == id).State;

    private List<ulong> Play(string ship, string job)
    {
        var colony = Campaign.ShipChoice(ship)!.Colonizes;
        var sim = Game(ship, 1701, data: Only(KindOf(job)));
        var sys = sim.System!;
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice(ship)!.Chain));
        sim.Step();
        if (colony) Land(sim);
        Until(sim, () =>
        {
            foreach (var (id, state) in sim.Missions.PoolStates(0))
                if (id != job && state == MissionState.Offered) sim.Schedule(sim.Tick, new MissionChoiceCommand(0, id, "decline"));
            return State(sim, job) == MissionState.Offered;
        }, 900, $"{job} offered");
        Do(sim, new MissionChoiceCommand(0, job, "accept"));
        Assert.Equal(MissionState.Active, State(sim, job));
        var site = sys.Site(sim.Missions.SpaceMarkers(0).First(m => m.Kind == "site").SiteId)!;
        Identify(sim, site.Id);
        var ship0 = Ship(sim);
        switch (job)
        {
            case "rogue_rock":
                Work(sim, site.Id, "alongside", () => sys.SiteWork(0, site.Id)!.Alongside);
                Work(sim, site.Id, "push", () => site.Outcome.Length > 0);
                break;
            case "catch_the_comet":
                Until(sim, () => sys.SiteWorkProblem(0, ship0.Id, site.Id, "sample") == null, 600, "the tail is active");
                Work(sim, site.Id, "sample", () => sys.SiteWork(0, site.Id)!.Sampled);
                break;
            case "pods_in_the_dark":
                Work(sim, site.Id, "pickup", () => sys.SiteWork(0, site.Id)!.Picked >= 3);
                break;
            case "dead_stick":
                Work(sim, site.Id, "alongside", () => sys.SiteWork(0, site.Id)!.Alongside);
                Work(sim, site.Id, "restore_power", () => sys.SiteWork(0, site.Id)!.Powered);
                Do(sim, new SiteWorkCommand(0, ship0.Id, site.Id, "escort"));
                Do(sim, new SetCourseCommand(0, ship0.Id, site.TargetBody));
                Until(sim, () => sys.SiteWork(0, site.Id)!.Rescued, 400, "brought in");
                break;
            case "into_the_storm":
                Work(sim, site.Id, "record_middle", () => sys.SiteWork(0, site.Id)!.Recorded >= 60);
                break;
        }
        Until(sim, () => State(sim, job) == MissionState.Completed, 10, $"{job} completed");
        _out.WriteLine($"{ship} {job}: {sim.Tick / Simulation.TickRate} s, hull {sys.StatusOf(0)!.Hull}");
        var hashes = new List<ulong>();
        for (var s = 0; s < 10; s++)
        {
            Seconds(sim, 1);
            hashes.Add(sim.StateHash());
        }
        Assert.Equal(0, sim.DebugEdits);
        return hashes;
    }

    [Fact]
    public void AJobFailsWhenItsEventCanNoLongerBeDone()
    {
        var sim = Game("tereshkova", 1701, data: Only("rogue_asteroid"));
        var sys = sim.System!;
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice("tereshkova")!.Chain));
        sim.Step();
        Until(sim, () => State(sim, "rogue_rock") == MissionState.Offered, 300, "offered");
        Do(sim, new MissionChoiceCommand(0, "rogue_rock", "accept"));
        var rock = sys.Site(sim.Missions.SpaceMarkers(0).First(m => m.Kind == "site").SiteId)!;
        Until(sim, () => rock.Outcome.Length > 0, 600, "the rock strikes"); // nobody turns it
        Seconds(sim, 2);
        Assert.Equal(MissionState.Failed, State(sim, "rogue_rock"));
    }
}
