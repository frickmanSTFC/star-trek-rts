using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Tests;

/// <summary>Shared set-up for the space-jobs tests (docs/space-jobs-contract.md): campaign data, system games, and a pilot
/// that works a site the way a player would, from what the game shows (site cards, problems), commands only.</summary>
internal static class SpaceJobs
{
    /// <summary>The campaign's data: Dawn of Warp, Federation.</summary>
    private static readonly Lazy<GameData> CampaignData = new(() =>
        GameData.Load(TestData.Source, new DataSetup(Era: "dawn_of_warp", Factions: new[] { "federation" })));

    public static GameData Campaign => CampaignData.Value;

    public static Simulation Game(string? ship = "tereshkova", ulong seed = 1701, FitOut? fitOut = null, GameData? data = null)
    {
        data ??= Campaign;
        return ship == null ? MatchSetup.CreateSystemGame(data, seed) : fitOut != null
            ? MatchSetup.CreateSystemGame(data, seed, "federation", null, ship, fitOut)
            : MatchSetup.CreateSystemGame(data, seed, "federation", null, ship);
    }

    public static void Do(Simulation sim, Command c, int ticks = 1)
    {
        sim.Schedule(sim.Tick, c);
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    public static string Refused(Simulation sim, Command c)
    {
        var before = sim.RefusedCommands;
        Do(sim, c);
        Assert.Equal(before + 1, sim.RefusedCommands);
        return sim.LastRefusal ?? "";
    }

    public static void Seconds(Simulation sim, int seconds)
    {
        for (var i = 0; i < seconds * Simulation.TickRate; i++) sim.Step();
    }

    public static void Until(Simulation sim, Func<bool> done, int maxSeconds, string what)
    {
        for (var i = 0; i < maxSeconds * Simulation.TickRate && !done(); i++) sim.Step();
        Assert.True(done(), $"{what} (after {maxSeconds} s; last refusal: {sim.LastRefusal})");
    }

    /// <summary>A test-panel edit (counted as a debug edit).</summary>
    public static void Debug(Simulation sim, DebugSpaceField field, string? id, int value, int player = 0)
    {
        Assert.Null(sim.System!.DebugSpaceProblem(sim, new DebugSpaceCommand(player, player, field, id, value)));
        Do(sim, new DebugSpaceCommand(player, player, field, id, value));
    }

    public static SystemShip Ship(Simulation sim, int player = 0) => sim.System!.FlagshipOf(player)!;

    /// <summary>The first site of a kind in the system (null if it has none).</summary>
    public static SpaceSite? First(Simulation sim, SiteKind kind) => sim.System!.Sites.FirstOrDefault(s => s.Kind == kind);

    /// <summary>A seed whose system has a site of every phase 1 kind.</summary>
    public static ulong SeedWithEveryKind(GameData data)
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            var g = MatchSetup.CreateSystemGame(data, seed);
            if (new[] { SiteKind.Derelict, SiteKind.KnownDerelict, SiteKind.Debris }.All(k => g.System!.Sites.Any(s => s.Kind == k))) return seed;
        }
        throw new InvalidOperationException("no seed with every site kind");
    }

    /// <summary>Flies the flagship to the site and waits there (detecting it first with long-range scans from the body
    /// nearest to it, as a player would find it).</summary>
    public static void GoTo(Simulation sim, string siteId, int player = 0)
    {
        var sys = sim.System!;
        var ship = Ship(sim, player);
        Detect(sim, siteId, player);
        if (ship.AtSite == siteId) return;
        Until(sim, () => !ship.InFlight, 200, "lands somewhere first");
        Do(sim, new SiteCourseCommand(player, ship.Id, siteId));
        Until(sim, () => ship.AtSite == siteId, 300, $"reaches {siteId}");
    }

    /// <summary>Long-range scans (from the body nearest the site, flying there if needed) until the site is Detected.</summary>
    public static void Detect(Simulation sim, string siteId, int player = 0)
    {
        var sys = sim.System!;
        var ship = Ship(sim, player);
        if (sys.SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Detected) return;
        Until(sim, () => !ship.InFlight, 200, "in orbit to scan");
        if (ship.AtBody != null && sys.ScanProblem(player, ship.Id) == null) Do(sim, new LongRangeScanCommand(player, ship.Id));
        if (sys.SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Detected) return;
        var site = sys.Site(siteId)!;
        var near = sys.Bodies.OrderBy(b => FinalFrontier.Sim.Math.FixVec2.Distance(b.Position, site.Position)).First();
        if (ship.AtBody != near.Id)
        {
            Do(sim, new SetCourseCommand(player, ship.Id, near.Id));
            Until(sim, () => ship.AtBody == near.Id, 300, "reaches the body near the site");
        }
        Until(sim, () => sys.ScanProblem(player, ship.Id) == null, 200, "scanners ready");
        Do(sim, new LongRangeScanCommand(player, ship.Id));
        Assert.True(sys.SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Detected, $"{siteId} detected");
    }

    /// <summary>At the site: scan it until Identified.</summary>
    public static void Identify(Simulation sim, string siteId, int player = 0)
    {
        var sys = sim.System!;
        GoTo(sim, siteId, player);
        if (sys.SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Identified) return;
        Do(sim, new SiteScanCommand(player, Ship(sim, player).Id, siteId));
        Until(sim, () => sys.SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Identified, 120, $"{siteId} identified");
    }

    /// <summary>Runs a work action to its end.</summary>
    public static void Work(Simulation sim, string siteId, string action, Func<bool> done, int player = 0)
    {
        var sys = sim.System!;
        var problem = sys.SiteWorkProblem(player, Ship(sim, player).Id, siteId, action);
        Assert.True(problem == null, $"{action}: {problem}");
        Do(sim, new SiteWorkCommand(player, Ship(sim, player).Id, siteId, action));
        Until(sim, done, 400, $"{action} at {siteId}");
    }

    /// <summary>A team of the most the ship can send: Defense and Science first (fewer losses), the rest from the others.</summary>
    public static int[] Team(Simulation sim, int player = 0)
    {
        var sys = sim.System!;
        var status = sys.StatusOf(player)!;
        var max = sys.MaxAwayTeam(player);
        var order = new[] { "defense", "science", "exploration", "engineering" };
        var ids = sim.Data.FitOut!.Divisions.Select(d => d.Id).ToList();
        var team = new int[ids.Count];
        var left = max;
        for (var round = 0; left > 0 && round < 10; round++)
            foreach (var div in order)
            {
                var i = ids.IndexOf(div);
                if (left > 0 && team[i] < status.Crew[i].OnDuty - (status.Crew[i].OnDuty > 1 ? 1 : 0))
                {
                    team[i]++;
                    left--;
                }
            }
        return team;
    }

    /// <summary>Sends an away team, picks choice `pick` at each step (or lets the steps run out when pick is -1), and waits
    /// until it is back.</summary>
    public static AwayTeamView Board(Simulation sim, string siteId, int pick = 0, int player = 0)
    {
        var sys = sim.System!;
        var team = Team(sim, player);
        var problem = sys.AwayTeamProblem(player, siteId, team);
        Assert.True(problem == null, $"away team: {problem}");
        Do(sim, new AwayTeamCommand(player, Ship(sim, player).Id, siteId, team));
        var view = sys.AwayTeamOf(player)!;
        Assert.False(view.Done);
        while (pick >= 0 && sys.AwayTeamOf(player) is { Done: false } v && v.Step < v.Steps)
            Do(sim, new AwayTeamChoiceCommand(player, siteId, System.Math.Min(pick, v.Choices.Count - 1)));
        Until(sim, () => sys.AwayTeamOf(player) is { Done: true }, 400, "the team is back");
        return sys.AwayTeamOf(player)!;
    }
}
