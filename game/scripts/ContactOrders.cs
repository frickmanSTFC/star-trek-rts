using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;

namespace FinalFrontier;

/// <summary>
/// Civilizations phase 2 orders (docs/civilizations-phase2-contract.md): <c>FirstContactCommand</c>,
/// <c>ContactChoiceCommand</c>, <c>MembershipCommand</c>, <c>AllyShipOrderCommand</c>. Like SystemOrders: each asks the
/// sim's own check first (so the view shows the sim's reason) and returns null when the order was scheduled, else a
/// short reason for the event feed. First contact below L5 is the exception: the sim's General Order 1 text is a
/// warning, not a refusal, so the caller confirms it and passes <c>anyway</c>.
/// </summary>
public static class ContactOrders
{
    private const string NoSystem = "there is no star system in this game";

    /// <summary>The sim's FirstContactProblem is the General Order 1 warning (the command still goes ahead as a
    /// violation), not a hard refusal. Read from its words, since the sim gives text, not a code.</summary>
    public static bool IsGeneralOrder(string? problem)
    {
        if (string.IsNullOrEmpty(problem)) return false;
        var p = problem.ToLowerInvariant();
        return p.Contains("general order") || p.Contains("prime directive") || p.Contains("pre-warp");
    }

    public static string? FirstContact(Simulation sim, int player, string bodyId, long when, bool anyway)
    {
        if (sim.System is not { } system) return NoSystem;
        var why = system.FirstContactProblem(player, bodyId);
        if (why != null && !(anyway && IsGeneralOrder(why))) return why;
        sim.Schedule(when, new FirstContactCommand(player, bodyId));
        return null;
    }

    /// <summary>A pick at the current step (the sim refuses a stale one: the step may have timed out).</summary>
    public static string? Choose(Simulation sim, int player, string bodyId, int choice, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.ContactEventOf(player, bodyId) == null) return "the talks are over";
        sim.Schedule(when, new ContactChoiceCommand(player, bodyId, choice));
        return null;
    }

    public static string? Membership(Simulation sim, int player, string bodyId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.MembershipProblem(player, bodyId) is { } why) return why;
        sim.Schedule(when, new MembershipCommand(player, bodyId));
        return null;
    }

    /// <summary>bodyId null = escort the flagship, else fly there and scout (chart) it.</summary>
    public static string? AllyOrder(Simulation sim, int player, int shipId, string? bodyId, long when)
    {
        if (sim.System is not { } system) return NoSystem;
        if (system.AllyOrderProblem(player, shipId, bodyId) is { } why) return why;
        sim.Schedule(when, new AllyShipOrderCommand(player, shipId, bodyId));
        return null;
    }
}
