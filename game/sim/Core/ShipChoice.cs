using System.Collections.Generic;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Core;

// The campaign's ship pick, game side (docs/ship-pick-contract.md). CONTRACT STUB: the sim agent fills these in.
public sealed partial class Simulation
{
    private readonly Dictionary<int, string> _shipChoice = new();

    /// <summary>The ship this player picked at the campaign start, or null (a sandbox / a game with no pick).</summary>
    public ShipChoiceDef? ShipChoiceOf(int player) => _shipChoice.TryGetValue(player, out var id) ? Data.ShipChoice(id) : null;

    /// <summary>Whether this player may land a colony (no pick, or a ship that colonizes).</summary>
    public bool CanColonize(int player) => ShipChoiceOf(player) is not { Colonizes: false };

    /// <summary>Why this player can't land a colony ("the Tereshkova can't land a colony"), or null.</summary>
    public string? ColonizeProblem(int player) => ShipChoiceOf(player) is { Colonizes: false } s ? $"the {s.Name} can't land a colony" : null;

    /// <summary>Records the pick (MatchSetup, before tick 0).</summary>
    internal void SetShipChoice(int player, string shipId) => _shipChoice[player] = shipId;
}
