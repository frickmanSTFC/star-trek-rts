using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;

namespace FinalFrontier;

/// <summary>
/// The one place the view sends star-system orders from (docs/system-contract.md): <c>SetCourseCommand</c>,
/// <c>LongRangeScanCommand</c>, <c>SystemSatelliteCommand</c>, <c>ObservationPostCommand</c>. Each asks the sim's own check first (so the helm shows the
/// sim's reason) and returns null when the order was scheduled, else a short reason for the event feed.
/// </summary>
public static class SystemOrders
{
    /// <summary>True: the methods below send real commands (they were stubs before the sim branch landed).</summary>
    public static readonly bool Wired = true;

    private const string NoSystem = "there is no star system in this game";

    /// <summary>Set course for a body: timed flight there, orbit on arrival.</summary>
    public static string? SetCourse(Simulation sim, int player, int shipId, string bodyId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.CourseProblem(player, shipId, bodyId) is { } why) return why;
        sim.Schedule(when, new SetCourseCommand(player, shipId, bodyId));
        return null;
    }

    /// <summary>Long-range scan from a ship: charts the bodies in range (cooldown in the sim).</summary>
    public static string? LongRangeScan(Simulation sim, int player, int shipId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.ScanProblem(player, shipId) is { } why) return why;
        sim.Schedule(when, new LongRangeScanCommand(player, shipId));
        return null;
    }

    /// <summary>A hidden observation post on a civilization's world (civilizations phase 1): placed from orbit like a
    /// satellite; studies them ×4 but may be found.</summary>
    public static string? ObservationPost(Simulation sim, int player, string bodyId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.PostProblem(player, bodyId) is { } why) return why;
        sim.Schedule(when, new ObservationPostCommand(player, bodyId));
        return null;
    }

    /// <summary>A satellite at the body the flagship orbits: live local data there.</summary>
    public static string? DeploySatellite(Simulation sim, int player, string bodyId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.SatelliteProblem(player, bodyId) is { } why) return why;
        sim.Schedule(when, new SystemSatelliteCommand(player, bodyId));
        return null;
    }
}
