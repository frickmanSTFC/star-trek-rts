using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>What a scout drone is doing (docs/missions-contract.md B). None = not a drone.</summary>
public enum DroneState
{
    None,
    /// <summary>On its pad bay, charging (Battery rises to 1).</summary>
    Docked,
    /// <summary>Lifting off its bay (launch_seconds).</summary>
    Launching,
    /// <summary>Flying to its target.</summary>
    Flying,
    /// <summary>Circling its target tile (loiter_seconds), surveying.</summary>
    Loitering,
    /// <summary>Flying home (ordered, battery low, or out of comms coverage).</summary>
    Returning,
    /// <summary>Setting down on its bay (land_seconds).</summary>
    Landing,
    /// <summary>Battery ran out: came down (LostAtTick); the unit is removed a few seconds later.</summary>
    Lost,
}

/// <summary>A drone's state for the view and UI. Target = the tile it flies to (-1 none); Pad = its Drone Pad
/// structure id; Bay = its bay on the pad (0-based); Battery 0..1; Phase 0..1 within Launching / Landing / Loitering;
/// ReturnReason "", "ordered", "battery", "coverage", "done".</summary>
public readonly record struct DroneInfo(DroneState State, int Target, int Pad, int Bay, Fix64 Battery, Fix64 Phase,
    long LostAtTick, string ReturnReason);

public sealed partial class PlanetSurface
{
    // STUB (contract): agent S1 replaces this file's body. Keep the signatures.

    /// <summary>True for a unit that flies (data: "aerial": true). Aerial units ignore lanes, terrain and dome transits.</summary>
    public bool IsAerial(SurfaceUnit unit) => false;

    /// <summary>The drone's state (State None for any other unit).</summary>
    public DroneInfo DroneOf(SurfaceUnit unit) => new(DroneState.None, -1, 0, 0, Fix64.One, Fix64.Zero, -1, "");

    /// <summary>The drones belonging to this pad (docked or out), in bay order.</summary>
    public IReadOnlyList<SurfaceUnit> DronesOf(int padId) => System.Array.Empty<SurfaceUnit>();

    /// <summary>How many bays this structure has (0 = not a drone pad).</summary>
    public int DroneBays(SurfaceStructure s) => 0;

    /// <summary>Why LaunchDroneCommand(player, padId, tile) would be refused now, or null.</summary>
    public string? LaunchProblem(Simulation sim, int player, int padId, int tile) => "drones aren't built yet";
}
