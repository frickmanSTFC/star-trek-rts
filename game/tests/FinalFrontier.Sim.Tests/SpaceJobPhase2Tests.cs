using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using Xunit.Abstractions;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>The phase 2 space jobs (data/missions/space_jobs.json: pay_dirt, ice_run, tow_job) played on the real rules on
/// every space ship and both colony ships (landed first, sites within a flight of their colony), twice with the same
/// hashes; and the pool's declined offers coming back.</summary>
public class SpaceJobPhase2Tests
{
    private readonly ITestOutputHelper _out;

    public SpaceJobPhase2Tests(ITestOutputHelper output) => _out = output;

    public static readonly string[] Jobs = { "pay_dirt", "ice_run", "tow_job" };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ship in new[] { "tereshkova", "armstrong", "bastion", "meridian", "horizon", "cochrane" })
            foreach (var job in Jobs)
                yield return new object[] { ship, job };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryPhase2JobCanBeDoneOnEveryShip(string ship, string job)
    {
        var a = Play(ship, job);
        var b = Play(ship, job);
        Assert.Equal(a, b);
    }

    private static MissionState State(Simulation sim, string id) => sim.Missions.PoolStates(0).First(s => s.Id == id).State;

    /// <summary>Whether the site would do for the job (its kind and variant).</summary>
    private static bool Fits(SpaceSite s, string job) => job switch
    {
        "pay_dirt" => s.Kind == SiteKind.Asteroid && s.Variant is "common" or "metal",
        "ice_run" => s.Kind == SiteKind.Asteroid && s.Variant == "ice",
        _ => s.Kind is SiteKind.Derelict or SiteKind.KnownDerelict,
    };

    /// <summary>A seed whose system has a site for the job (a colony ship: within its reach of the colony's body).</summary>
    private static ulong SeedFor(string ship, string job, bool colony)
    {
        for (ulong seed = 1; seed < 600; seed++)
        {
            var g = Game(ship, seed);
            var sys = g.System!;
            var home = sys.Body(sys.SurfaceBody)!;
            var reach = (long)Campaign.SpaceSites!.ColonyReachSeconds * Simulation.TickRate;
            if (sys.Sites.Any(s => Fits(s, job) && (!colony || sys.TravelTicksFor(Ship(g), home.Position, s.Position) <= reach))) return seed;
        }
        throw new InvalidOperationException($"no seed for {job} on {ship}");
    }

    private static void MakeRoom(Simulation sim, int containers)
    {
        var sys = sim.System!;
        while (sys.StatusOf(0)!.HoldSize - sys.StatusOf(0)!.HoldUsed < containers)
        {
            var line = sys.StatusOf(0)!.Hold.FirstOrDefault(l => l.Item is "probe" or "ssat" or "post" or "patch_kit" or "medical");
            Assert.True(line != null, "nothing left to jettison");
            Do(sim, new ShipHoldCommand(0, Ship(sim).Id, "jettison", line!.Item, 1));
        }
    }

    private List<ulong> Play(string ship, string job)
    {
        var colony = Campaign.ShipChoice(ship)!.Colonizes;
        var sim = Game(ship, SeedFor(ship, job, colony));
        var sys = sim.System!;
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice(ship)!.Chain));
        if (colony)
        {
            var map = sim.Surface!.Map;
            Do(sim, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t))));
            for (var i = 0; i <= TestData.ReadyTicks(Campaign) + Simulation.TickRate; i++) sim.Step();
        }
        var home = sys.Body(sys.SurfaceBody)!;
        var reach = (long)Campaign.SpaceSites!.ColonyReachSeconds * Simulation.TickRate;
        var site = sys.Sites.First(s => Fits(s, job) && (!colony || sys.TravelTicksFor(Ship(sim), home.Position, s.Position) <= reach));
        Detect(sim, site.Id);
        if (job == "tow_job") Identify(sim, site.Id); // its trigger asks identified
        // Offered as soon as a fitting site is known; an offer that lapsed while the ship flew comes back (return_seconds).
        // The other pool jobs may hold the same site with their offers: decline them, as a player would.
        Until(sim, () =>
        {
            foreach (var (id, state) in sim.Missions.PoolStates(0))
                if (id != job && state == MissionState.Offered) sim.Schedule(sim.Tick, new MissionChoiceCommand(0, id, "decline"));
            return State(sim, job) == MissionState.Offered;
        }, Campaign.Missions!.Chain("space_jobs")!.ReturnSeconds + 10, $"{job} offered");
        Do(sim, new MissionChoiceCommand(0, job, "accept"));
        Assert.Equal(MissionState.Active, State(sim, job));
        var bound = sim.Missions.SpaceMarkers(0).First(m => m.Kind == "site").SiteId!; // the site the job bound (the lowest-numbered fit)
        site = sys.Site(bound)!;
        Identify(sim, site.Id);
        var ship0 = Ship(sim);
        var station = First(sim, SiteKind.Station)!;
        switch (job)
        {
            case "pay_dirt":
            case "ice_run":
            {
                MakeRoom(sim, 2);
                Work(sim, site.Id, "mine", () => sys.StatusOf(0)!.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix)).Sum(l => l.Count) >= 60
                    || sys.SiteWorkProgress(0, site.Id) == null);
                Assert.True(sys.StatusOf(0)!.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix)).Sum(l => l.Count) >= 60, "60 ore mined");
                if (sys.SiteWorkProgress(0, site.Id) != null) Do(sim, new SiteWorkCommand(0, ship0.Id, site.Id, "stop"));
                if (colony && job == "ice_run")
                {
                    // A colony ship can take the ice home.
                    Do(sim, new SetCourseCommand(0, ship0.Id, home.Id));
                    Until(sim, () => ship0.AtBody == home.Id, 300, "home");
                    foreach (var line in sys.StatusOf(0)!.Hold.Where(l => l.Item == "ore:deuterium").ToList())
                        Do(sim, new ShipHoldCommand(0, ship0.Id, "drop", line.Item, line.Count));
                }
                else
                {
                    GoTo(sim, station.Id);
                    foreach (var line in sys.StatusOf(0)!.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix)).ToList())
                        Do(sim, new ShipHoldCommand(0, ship0.Id, "sell", line.Item, line.Count));
                }
                break;
            }
            case "tow_job":
                if (!sys.SiteWork(0, site.Id)!.Alongside) Work(sim, site.Id, "alongside", () => sys.SiteWork(0, site.Id)!.Alongside);
                Do(sim, new SiteWorkCommand(0, ship0.Id, site.Id, "tow"));
                Assert.Equal(site.Id, sys.TowOf(0));
                Do(sim, new SiteCourseCommand(0, ship0.Id, station.Id));
                Until(sim, () => ship0.AtSite == station.Id, 400, "at the station with the tow");
                Do(sim, new ShipHoldCommand(0, ship0.Id, "drop", StarSystemState.TowItem, 1));
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
    public void ADeclinedPoolOfferComesBackAfterTheChainsReturnSeconds()
    {
        var (sim, rock) = AsteroidTests.WithAsteroid("common");
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice("tereshkova")!.Chain));
        Detect(sim, rock.Id);
        Until(sim, () => State(sim, "pay_dirt") == MissionState.Offered, Campaign.Missions!.Chain("space_jobs")!.ReturnSeconds + 10, "offered");
        Do(sim, new MissionChoiceCommand(0, "pay_dirt", "decline"));
        Assert.Equal(MissionState.Declined, State(sim, "pay_dirt"));
        var back = Campaign.Missions!.Chain("space_jobs")!.ReturnSeconds;
        Assert.True(back > 0);
        Seconds(sim, back - 2);
        Assert.Equal(MissionState.Declined, State(sim, "pay_dirt"));
        Until(sim, () => State(sim, "pay_dirt") == MissionState.Offered, 5, "offered again");
        // And a lapsed offer comes back too.
        Until(sim, () => State(sim, "pay_dirt") == MissionState.Declined, Campaign.Missions.Rules.OfferSeconds + 2, "lapsed");
        Until(sim, () => State(sim, "pay_dirt") == MissionState.Offered, back + 3, "offered once more");
    }
}
