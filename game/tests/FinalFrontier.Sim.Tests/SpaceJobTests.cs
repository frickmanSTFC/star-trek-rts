using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using Xunit.Abstractions;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>The phase 1 space jobs (data/missions/space_jobs.json): each played on the real rules (offered from a site a
/// long-range scan found, accepted, worked with commands only), twice with the same hashes, on every space ship and both
/// colony ships (those landed first, offered only sites within a flight of their colony).</summary>
public class SpaceJobTests
{
    private readonly ITestOutputHelper _out;

    public SpaceJobTests(ITestOutputHelper output) => _out = output;

    private static readonly Dictionary<string, SiteKind> JobSite = new()
    {
        ["quiet_hulk"] = SiteKind.Derelict,
        ["dead_in_the_water"] = SiteKind.KnownDerelict,
        ["what_happened_here"] = SiteKind.Debris,
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ship in new[] { "tereshkova", "armstrong", "bastion", "meridian", "horizon", "cochrane" })
            foreach (var job in JobSite.Keys)
                yield return new object[] { ship, job };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryJobCanBeDoneOnEveryShip(string ship, string job)
    {
        var a = Play(ship, job);
        var b = Play(ship, job);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ThePoolIsOfferedOnlyInSystemGamesAndNotListedAsTheChain()
    {
        var sim = Game("tereshkova", SeedWithEveryKind(Campaign));
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice("tereshkova")!.Chain));
        Seconds(sim, 1);
        Assert.Equal(5, sim.Missions.States(0).Count); // the chain alone (tests and the test panel count on it)
        Assert.Equal(JobSite.Keys.Concat(SpaceJobPhase2Tests.Jobs).OrderBy(x => x), sim.Missions.PoolStates(0).Select(s => s.Id).OrderBy(x => x)); // + phase 2's
        Assert.NotEqual("space_jobs", sim.Missions.ChainId);
        Assert.NotNull(sim.Missions.EnableProblem("space_jobs")); // never a game's chain

        var planet = CommandFuzzTests.TwoPlayersLanded(missions: true);
        Assert.Empty(planet.Missions.PoolStates(0));
    }

    [Fact]
    public void ACrippledShipFailsItsActiveSpaceJobs()
    {
        var sim = Game("tereshkova", SeedWithEveryKind(Campaign));
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice("tereshkova")!.Chain));
        var field = First(sim, SiteKind.Debris)!;
        Detect(sim, field.Id);
        Until(sim, () => State(sim, "what_happened_here") == MissionState.Offered, 5, "offered");
        Do(sim, new MissionChoiceCommand(0, "what_happened_here", "accept"));
        Debug(sim, DebugSpaceField.Hull, null, 0);
        Seconds(sim, 2);
        Assert.Equal(MissionState.Failed, State(sim, "what_happened_here"));
    }

    private static MissionState State(Simulation sim, string id) => sim.Missions.PoolStates(0).First(s => s.Id == id).State;

    /// <summary>A seed whose colony ship would be offered every job (a site of each kind within its reach).</summary>
    private static ulong ColonySeed(string ship)
    {
        for (ulong seed = 1; seed < 400; seed++)
        {
            var g = Game(ship, seed);
            var sys = g.System!;
            var colony = sys.Body(sys.SurfaceBody)!;
            var reach = (long)Campaign.SpaceSites!.ColonyReachSeconds * Simulation.TickRate;
            if (JobSite.Values.All(k => sys.Sites.Any(s => s.Kind == k && sys.TravelTicksFor(Ship(g), colony.Position, s.Position) <= reach))) return seed;
        }
        throw new InvalidOperationException("no seed");
    }

    private List<ulong> Play(string ship, string job)
    {
        var colony = Campaign.ShipChoice(ship)!.Colonizes;
        var seed = colony ? ColonySeed(ship) : SeedWithEveryKind(Campaign);
        var sim = Game(ship, seed);
        var sys = sim.System!;
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice(ship)!.Chain));
        if (colony)
        {
            var map = sim.Surface!.Map;
            var tile = Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
            Do(sim, new LandColonyCommand(0, tile));
            for (var i = 0; i <= TestData.ReadyTicks(Campaign) + Simulation.TickRate; i++) sim.Step();
            Assert.True(sim.Surface.UplinkOnline(0, sim.Tick));
        }
        var kind = JobSite[job];
        var site = colony
            ? sys.Sites.First(s => s.Kind == kind && sys.TravelTicksFor(Ship(sim), sys.Body(sys.SurfaceBody)!.Position, s.Position) <= (long)Campaign.SpaceSites!.ColonyReachSeconds * Simulation.TickRate)
            : First(sim, kind)!;
        Detect(sim, site.Id);
        Until(sim, () => State(sim, job) == MissionState.Offered, 5, $"{job} offered");
        Do(sim, new MissionChoiceCommand(0, job, "accept"));
        Assert.Equal(MissionState.Active, State(sim, job));
        Assert.Contains(sim.Missions.SpaceMarkers(0), m => m.Kind == "site" && m.SiteId == site.Id);

        Identify(sim, site.Id);
        switch (job)
        {
            case "quiet_hulk":
                Work(sim, site.Id, "alongside", () => sys.SiteWork(0, site.Id)!.Alongside);
                Board(sim, site.Id, 0);
                break;
            case "dead_in_the_water":
                Work(sim, site.Id, "alongside", () => sys.SiteWork(0, site.Id)!.Alongside);
                if (sys.SiteWorkProblem(0, Ship(sim).Id, site.Id, "restore_power_kit") == null)
                    Do(sim, new SiteWorkCommand(0, Ship(sim).Id, site.Id, "restore_power_kit"));
                else Work(sim, site.Id, "restore_power", () => sys.SiteWork(0, site.Id)!.Powered);
                Work(sim, site.Id, "recover_logs", () => sys.SiteWork(0, site.Id)!.Logs);
                Board(sim, site.Id, 0);
                break;
            case "what_happened_here":
                // A preset can fill the hold: throw out used-up cargo to make room, as a player would.
                while (sys.SiteWorkProblem(0, Ship(sim).Id, site.Id, "sweep") is { } p && p.StartsWith("the hold is full"))
                {
                    var line = sys.StatusOf(0)!.Hold.First(l => l.Item is "probe" or "ssat" or "post" or "patch_kit" or "medical");
                    Do(sim, new ShipHoldCommand(0, Ship(sim).Id, "jettison", line.Item, 1));
                }
                Work(sim, site.Id, "sweep", () => sys.StatusOf(0)!.Hold.Any(l => l.Item == "salvage:black_box"));
                break;
        }
        Until(sim, () => State(sim, job) == MissionState.Completed, 10, $"{job} completed");
        _out.WriteLine($"{ship} {job}: {sim.Tick / Simulation.TickRate} s, hull {sys.StatusOf(0)!.Hull}, crew {sys.StatusOf(0)!.CrewTotal}/{sys.StatusOf(0)!.CrewStart}");
        foreach (var l in sys.StatusOf(0)!.Log) _out.WriteLine($"  {l.Tick / Simulation.TickRate} s {l.Text}");
        var hashes = new List<ulong>();
        for (var s = 0; s < 20; s++)
        {
            Seconds(sim, 1);
            hashes.Add(sim.StateHash());
        }
        Assert.Equal(0, sim.DebugEdits);
        return hashes;
    }
}
