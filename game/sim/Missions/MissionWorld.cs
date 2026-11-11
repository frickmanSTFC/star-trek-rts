using System.Collections.Generic;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Missions;

/// <summary>A drone of a player's as the missions see it: what it is doing and the tile it is over.</summary>
internal readonly record struct MissionDrone(DroneState State, int Tile);

/// <summary>
/// What the mission conditions read about drones and comms coverage (docs/missions-contract.md B, owner S1). The default
/// reads the surface's API (Surface/Drones.cs IsAerial / DroneOf, Surface/Comms.cs CommsCovered / CommsSources); tests
/// swap in their own (MissionBook.World) so the drone and relay missions can be played before S1's drones and relays
/// exist. The game never changes it. Must be a pure function of game state (it is read inside the step).
/// </summary>
internal class MissionWorld
{
    public static readonly MissionWorld Default = new();

    /// <summary>The player's drones (aerial units), in unit order.</summary>
    public virtual IEnumerable<MissionDrone> Drones(PlanetSurface s, int player)
    {
        foreach (var u in s.Units)
            if (u.Owner == player && s.IsAerial(u)) yield return new MissionDrone(s.DroneOf(u).State, u.Tile);
    }

    /// <summary>Inside the player's comms coverage.</summary>
    public virtual bool Covered(PlanetSurface s, int player, int tile) => s.CommsCovered(player, tile);

    /// <summary>The player's coverage sources (cores, relays).</summary>
    public virtual IReadOnlyList<CommsSource> Sources(PlanetSurface s, int player) => s.CommsSources(player);
}
