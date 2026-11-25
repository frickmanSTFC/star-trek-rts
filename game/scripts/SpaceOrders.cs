using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;

namespace FinalFrontier;

/// <summary>
/// The space-jobs orders (docs/space-jobs-contract.md, commands 40–46): each asks the sim's own problem check first
/// (SiteCourseProblem, SiteScanProblem, SiteWorkProblem, AwayTeamProblem, AwayChoiceProblem, RepairProblem, HoldProblem)
/// and only then schedules the command; the sim checks again when it runs. Returns why it can't (null = sent).
/// </summary>
public static class SpaceOrders
{
    private const string NoSystem = "there is no star system in this game";

    public static string? Course(Simulation sim, int player, int shipId, string siteId, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.SiteCourseProblem(player, shipId, siteId) is { } why) return why;
        sim.Schedule(when, new SiteCourseCommand(player, shipId, siteId));
        return null;
    }

    public static string? Scan(Simulation sim, int player, int shipId, string siteId, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.SiteScanProblem(player, shipId, siteId) is { } why) return why;
        sim.Schedule(when, new SiteScanCommand(player, shipId, siteId));
        return null;
    }

    public static string? Work(Simulation sim, int player, int shipId, string siteId, string action, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.SiteWorkProblem(player, shipId, siteId, action) is { } why) return why;
        sim.Schedule(when, new SiteWorkCommand(player, shipId, siteId, action));
        return null;
    }

    /// <param name="crew">How many from each division, in fit_out.json division order.</param>
    public static string? AwayTeam(Simulation sim, int player, int shipId, string siteId, IReadOnlyList<int> crew, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.AwayTeamProblem(player, siteId, crew) is { } why) return why;
        sim.Schedule(when, new AwayTeamCommand(player, shipId, siteId, crew));
        return null;
    }

    public static string? AwayChoice(Simulation sim, int player, string siteId, int choice, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.AwayChoiceProblem(player, siteId, choice) is { } why) return why;
        sim.Schedule(when, new AwayTeamChoiceCommand(player, siteId, choice));
        return null;
    }

    /// <param name="action">"patch", "fix:&lt;system&gt;" or "dock".</param>
    public static string? Repair(Simulation sim, int player, int shipId, string action, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.RepairProblem(player, action) is { } why) return why;
        sim.Schedule(when, new ShipRepairCommand(player, shipId, action));
        return null;
    }

    /// <param name="action">"sell", "drop", "jettison" or "hire".</param>
    public static string? Hold(Simulation sim, int player, int shipId, string action, string item, int count, long when)
    {
        if (sim.System is not { } sys) return NoSystem;
        if (sys.HoldProblem(player, action, item, count) is { } why) return why;
        sim.Schedule(when, new ShipHoldCommand(player, shipId, action, item, count));
        return null;
    }
}
