using System.Collections.Generic;
using FinalFrontier.Sim.Core;

namespace FinalFrontier.Sim.Surface;

/// <summary>One source of comms coverage (docs/missions-contract.md B): a finished colony core's antenna or a relay
/// satellite parked over a tile. Radius in hexes; SatelliteId 0 for a core; Ready = deployed (a relay still drifting
/// to its station covers nothing yet).</summary>
public readonly record struct CommsSource(CommsSourceKind Kind, int Tile, int Radius, int StructureId, int SatelliteId, bool Ready);

public enum CommsSourceKind { Core, Relay }

public sealed partial class PlanetSurface
{
    // STUB (contract): agent S1 replaces this file's body. Keep the signatures.

    /// <summary>True when the tile is inside this player's comms coverage (any ready source's radius).</summary>
    public bool CommsCovered(int player, int tile) => true;

    /// <summary>This player's coverage sources, cores first (by structure id) then relays (by satellite id).</summary>
    public IReadOnlyList<CommsSource> CommsSources(int player) => System.Array.Empty<CommsSource>();

    /// <summary>Why RelaySatelliteCommand(player, tile) would be refused now, or null.</summary>
    public string? RelayProblem(Simulation sim, int player, int tile) => "relay satellites aren't built yet";
}
