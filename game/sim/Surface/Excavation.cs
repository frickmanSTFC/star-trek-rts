using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Civilizations phase 2, surface side: ruins on the colony's own world and digging them (docs/civilizations-phase2-contract.md).
// CONTRACT STUBS: the sim branch replaces every body below.

/// <summary>A ruin site on the colony's map: the dead species, how many layers it has and how many are dug.</summary>
public sealed record SurfaceRuin(int Tile, string Species, int Layers)
{
    public int LayersDug { get; internal set; }
    /// <summary>Locked by a woken defence system until this tick (0 = not locked).</summary>
    public long LockedUntilTick { get; internal set; }
    public bool Exhausted => LayersDug >= Layers;
}

/// <summary>Something a dig layer turned up. Kind: artifact, data_core, tech_fragment, resources, defence, pathogen.</summary>
public sealed record DigFind(long Tick, int Player, int Tile, string Kind, string Name, string Effect);

public sealed partial class PlanetSurface
{
    /// <summary>Every ruin site on the map (about 1 colony world in 4 has one).</summary>
    public IReadOnlyList<SurfaceRuin> Ruins => System.Array.Empty<SurfaceRuin>();

    /// <summary>The ruin on a tile, or null.</summary>
    public SurfaceRuin? RuinAt(int tile) => null;

    /// <summary>Progress 0..1 on the current layer of the dig at this Excavation Site (0 if it isn't one).</summary>
    public Fix64 DigProgress(SurfaceStructure site) => Fix64.Zero;

    /// <summary>Why the dig at this site isn't moving ("locked by a defence system: 2:10", "site exhausted"), or null.</summary>
    public string? DigProblem(SurfaceStructure site) => null;

    /// <summary>Every find so far, oldest first.</summary>
    public IReadOnlyList<DigFind> Finds => System.Array.Empty<DigFind>();

    /// <summary>Seconds the base's crew stay sick from a released pathogen (0 = healthy).</summary>
    public int CrewSickSeconds(int baseId) => 0;

    private string? RuinDebugProblem(DebugRuinCommand command) => "not built yet";

    private void ApplyRuinDebug(DebugRuinCommand command) { }
}
