using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Civilizations phase 2, star-system side (docs/civilizations-phase2-contract.md). CONTRACT STUBS: the sim branch replaces
// every body below; the signatures are what the view agent builds against.

/// <summary>Relations between one player and a living civilization.</summary>
public enum ContactStage { None, Contact, TradePartner, Candidate, Allied }

/// <summary>What an allied native ship is doing.</summary>
public enum AllyTask { Home, Escort, Scout }

/// <summary>One option at a step of a first-contact event. ShockShift = how it moves the culture-shock chance (percentage points).</summary>
public sealed record ContactChoice(string Text, string Hint, int ShockShift);

/// <summary>A running first-contact event (~4 min): Step 0..Steps-1, each with its choices; Chosen = the picks so far
/// (-1 = not yet, the default is taken when the step's time runs out). ShockChance = the current chance (%) it ends in
/// culture shock instead of a smooth start as a trade partner.</summary>
public sealed record ContactEvent(string BodyId, int Player, long StartTick, long EndTick, int Step, int Steps,
    IReadOnlyList<ContactChoice> Choices, IReadOnlyList<int> Chosen, int ShockChance, long StepEndTick);

public sealed partial class StarSystemState
{
    /// <summary>The player's relations with the body's civilization.</summary>
    public ContactStage RelationsWith(int player, string bodyId) => ContactStage.None;

    /// <summary>The player's running first-contact event at the body, or null.</summary>
    public ContactEvent? ContactEventOf(int player, string bodyId) => null;

    /// <summary>Why the player can't start first contact there now (null = they can; a pre-warp world returns the
    /// General Order 1 text in a Prime Directive era, but FirstContactCommand still goes ahead as a violation).</summary>
    public string? FirstContactProblem(int player, string bodyId) => "not built yet";

    /// <summary>Why the player can't take relations one step further now (null = they can).</summary>
    public string? MembershipProblem(int player, string bodyId) => "not built yet";

    /// <summary>The civilization refuses contact with the player for this many more seconds (0 = it doesn't).</summary>
    public int RefusesContactSeconds(int player, string bodyId) => 0;

    /// <summary>What trade partners and allied worlds give the player a minute.</summary>
    public IReadOnlyList<ResourceAmount> TradeIncomePerMinute(int player) => System.Array.Empty<ResourceAmount>();

    /// <summary>The unique local tech a contacted civilization shared with the player (name), or null.</summary>
    public string? UniqueTechFrom(int player, string bodyId) => null;

    /// <summary>The player's allied native ships (SystemShip with AllyOf == player).</summary>
    public IReadOnlyList<SystemShip> AlliesOf(int player) => System.Array.Empty<SystemShip>();

    /// <summary>Why the ally can't take that order (bodyId null = escort the flagship); null = it can.</summary>
    public string? AllyOrderProblem(int player, int shipId, string? bodyId) => "not built yet";

    /// <summary>Test panel: why a civ debug command would be refused (null = it runs).</summary>
    public string? DebugCivProblem(Simulation sim, DebugCivCommand command) => "not built yet";

    /// <summary>Test panel: runs an accepted civ debug command.</summary>
    internal void ApplyDebugCiv(Simulation sim, DebugCivCommand command) { }

    /// <summary>Stub: refuses the phase 2 commands.</summary>
    private void ApplyContact(Simulation sim, Command command) => sim.Refuse(command, "first contact not built yet");
}
