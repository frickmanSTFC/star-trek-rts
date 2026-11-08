using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// CONTRACT (2026-11-08, hostile worlds: gravity + Veyra with domes). Stubs so the view and model agents can build
// against the API while the sim agent fills it in. Shapes are fixed; bodies are placeholders. See
// docs/hostile-worlds-contract.md.

/// <summary>A world's protection tier, cheapest first (gravity doc "Life and habitability limits").</summary>
public enum HabitabilityTier { Open, Masks, Suits, Domes, Tech, Impossible }

public sealed partial class PlanetSurface
{
    /// <summary>This surface's protection tier (worst of gravity, air, temperature, radiation). Fixed per game.</summary>
    public HabitabilityTier Tier => HabitabilityTier.Open;

    /// <summary>This surface's gravity in g (body override, else its class environment).</summary>
    public Fix64 GravityG => Fix64.One;

    /// <summary>True when the tile lies under a dome or containment field of the player's (any finished core's
    /// field, or a finished Pressure Dome's), so crewed buildings may stand there on a Domes world.</summary>
    public bool Covered(int player, int tile) => true;

    /// <summary>How many tiles round its own tile a structure's dome / containment field reaches (0 = none).
    /// Cores: 1 on a Domes world; pressure_dome: 2. Only finished structures cover.</summary>
    public int DomeRadiusOf(SurfaceStructure s) => 0;

    /// <summary>Life support of a base on a Domes world: 1 = fully supplied, 0 = no air (crew loss). 1 elsewhere.</summary>
    public Fix64 LifeSupportOf(int baseId) => Fix64.One;
}
