using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// CONTRACT (2026-11-08, "system view: ships, travel, system fog, generated systems"). Shapes are fixed so the view
// agent can build against them; bodies are placeholder stubs the sim agent fills in. See docs/system-contract.md.

/// <summary>What a player knows about a body. Live readings are separate (<see cref="StarSystemState.HasLiveData"/>).</summary>
public enum BodyKnowledge
{
    /// <summary>Not even known to exist (not used at start: every body is at least Detected).</summary>
    Unknown,
    /// <summary>Position and rough size only.</summary>
    Detected,
    /// <summary>Class, moons, gravity, atmosphere, the resources that may be there; kept for good once learned.</summary>
    Charted,
}

/// <summary>A ship in the star system: ours, a rival's, or neutral traffic (Owner -1).</summary>
public sealed class SystemShip
{
    public required int Id { get; init; }
    /// <summary>Player slot, or -1 for neutral traffic.</summary>
    public required int Owner { get; init; }
    /// <summary>ships.json class id (neutral traffic uses civilian classes).</summary>
    public required string ClassId { get; init; }
    public required string Name { get; init; }
    public string Registry { get; init; } = "";
    /// <summary>Body it is in orbit of, or null while in flight.</summary>
    public string? AtBody { get; set; }
    /// <summary>In flight: where from / to and when (sim ticks).</summary>
    public string? FromBody { get; set; }
    public string? ToBody { get; set; }
    public long DepartTick { get; set; }
    public long ArriveTick { get; set; }
    /// <summary>What it carries, for the info card (neutral traffic: "deuterium", "ore", "colonists"…), or null.</summary>
    public string? Cargo { get; set; }
}

/// <summary>
/// The star system of a game: its star, its bodies (static positions, MapDef units, like maps.json), the ships, and
/// each player's knowledge of every body. Built by <see cref="SystemGenerator"/> from the seed (or from a maps.json
/// map). Lives on <c>Simulation.System</c> (null for games without one, e.g. the balance runner's planet-only games).
/// </summary>
public sealed partial class StarSystemState
{
    public StarDef Star { get; init; } = new("Kalandra", "G", Fix64.FromInt(40));
    public IReadOnlyList<BodyDef> Bodies { get; init; } = new List<BodyDef>();
    public IReadOnlyList<SystemShip> Ships => _ships;
    private readonly List<SystemShip> _ships = new();

    /// <summary>The body the colony surface belongs to (where the game's PlanetSurface is), or null.</summary>
    public string? SurfaceBody { get; init; }

    public BodyKnowledge KnowledgeOf(int player, string bodyId) => BodyKnowledge.Detected;

    /// <summary>True while the player has a satellite at the body (or its colony there with the uplink up).</summary>
    public bool HasLiveData(int player, string bodyId) => false;

    /// <summary>The player's own ship (their flagship), or null.</summary>
    public SystemShip? FlagshipOf(int player) => null;

    /// <summary>Where a ship is on the plot at a tick (MapDef units; interpolated in flight).</summary>
    public FixVec2 ShipPosition(SystemShip ship, long tick) => FixVec2.Zero;

    /// <summary>Flight time between two bodies in ticks (impulse; 20-90 s by distance, placeholder).</summary>
    public long TravelTicks(string fromBody, string toBody) => 0;

    /// <summary>Why the ship can't set course there now, or null.</summary>
    public string? CourseProblem(int player, int shipId, string bodyId) => "not implemented yet";

    /// <summary>Resources that may be found on a body as the player knows it (Charted: from its class's yields and any
    /// survey; Detected: none). Ids from resources.json.</summary>
    public IReadOnlyList<string> PossibleResources(int player, string bodyId) => new List<string>();
}
