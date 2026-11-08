using FinalFrontier.Sim.Core;

namespace FinalFrontier;

/// <summary>
/// The one place the view sends star-system orders from (docs/system-contract.md). The sim branch adds the commands
/// (<c>SetCourseCommand(player, shipId, bodyId)</c>, <c>LongRangeScanCommand(player, shipId)</c>,
/// <c>SystemSatelliteCommand(player, bodyId)</c>); until then every method is a stub that schedules nothing and says
/// so. Each returns null when the order was scheduled, else a short reason for the event feed.
/// </summary>
public static class SystemOrders
{
    /// <summary>False while the methods below are stubs (the helm then says orders aren't wired yet).</summary>
    public static readonly bool Wired = false;

    private const string NotYet = "star-system orders aren't wired to the sim yet";

    /// <summary>Set course for a body: timed flight there, orbit on arrival.</summary>
    public static string? SetCourse(Simulation sim, int player, int shipId, string bodyId, long when)
    {
        // TODO(system sim): sim.Schedule(when, new SetCourseCommand(player, shipId, bodyId)); return null;
        return NotYet;
    }

    /// <summary>Long-range scan from a ship: charts the bodies in range (cooldown in the sim).</summary>
    public static string? LongRangeScan(Simulation sim, int player, int shipId, long when)
    {
        // TODO(system sim): sim.Schedule(when, new LongRangeScanCommand(player, shipId)); return null;
        return NotYet;
    }

    /// <summary>A satellite at the body the flagship orbits: live local data there.</summary>
    public static string? DeploySatellite(Simulation sim, int player, string bodyId, long when)
    {
        // TODO(system sim): sim.Schedule(when, new SystemSatelliteCommand(player, bodyId)); return null;
        return NotYet;
    }
}
