using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

// The campaign's ship pick, game side (docs/ship-pick-contract.md). Set once by MatchSetup.CreateSystemGame(..., shipId)
// before the player is added; read by the star system (the flagship's class, name, speed and start orbit), the surface
// (no landing for a ship that doesn't colonize; a colonizer's start units) and the missions. Hashed only when set.
public sealed partial class Simulation
{
    /// <summary>The pick per player slot (null = none), only as long as the highest slot that has one.</summary>
    private readonly List<string?> _shipChoice = new();

    /// <summary>The ship this player picked at the campaign start, or null (a sandbox / a game with no pick).</summary>
    public ShipChoiceDef? ShipChoiceOf(int player) =>
        player >= 0 && player < _shipChoice.Count ? Data.ShipChoice(_shipChoice[player]) : null;

    /// <summary>Whether this player may land a colony (no pick, or a ship that colonizes).</summary>
    public bool CanColonize(int player) => ShipChoiceOf(player) is not { Colonizes: false };

    /// <summary>Why this player can't land a colony ("the Tereshkova can't land a colony"), or null. Every way down to the
    /// surface asks it first: LandColony, OutpostDrop, Shuttle (drop ship), Beam and FoundOutpost are refused with it, and
    /// PlanetSurface.LandProblem / OutpostProblem / ShuttleProblem / BeamProblem return it.</summary>
    public string? ColonizeProblem(int player) => ShipChoiceOf(player) is { Colonizes: false } s ? $"the {s.Name} can't land a colony" : null;

    /// <summary>Records the pick (MatchSetup, before the player is added and before tick 0).</summary>
    internal void SetShipChoice(int player, string shipId)
    {
        while (_shipChoice.Count <= player) _shipChoice.Add(null);
        _shipChoice[player] = shipId;
    }

    /// <summary>The picks, only when one is set (games without a pick keep the hashes they always had).</summary>
    private void AddShipChoiceToHash(ref StateHasher h)
    {
        if (_shipChoice.Count == 0) return;
        h.Add("ship_choice");
        h.Add(_shipChoice.Count);
        foreach (var id in _shipChoice)
        {
            h.Add(id != null);
            if (id != null) h.Add(id);
        }
    }
}
