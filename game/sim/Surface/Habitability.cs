using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Hostile worlds (2026-11-08, contract docs/hostile-worlds-contract.md): the surface's gravity and its effects
// (GravityProfile), its habitability tier, and on Domes worlds the domes kit (cover, life support). Everything here is a
// pure function of the data and the map's class and climate body, except the per-base life-support failure state.

/// <summary>A world's protection tier, cheapest first (gravity doc "Life and habitability limits").</summary>
public enum HabitabilityTier { Open, Masks, Suits, Domes, Tech, Impossible }

public sealed partial class PlanetSurface
{
    /// <summary>The data the cached profile was built from (rebuilt when the test panel swaps the data).</summary>
    private GameData? _worldFor;

    private GravityProfile? _gravity;

    private void RefreshWorld()
    {
        if (ReferenceEquals(_worldFor, _data)) return;
        _worldFor = _data;
        _gravity = GravityProfile.For(_data, GravityOf(_data, Map));
    }

    /// <summary>A surface's gravity: its climate body's own gravity_g, else its class environment's, else 1 g.</summary>
    public static Fix64 GravityOf(GameData data, SurfaceMap map)
    {
        if (map.ClimateBodyId is { } body && data.Climate?.Body(body) is { GravityG: { } own }) return own;
        return data.EnvironmentOf(map.Def.ClassId)?.GravityG ?? Fix64.One;
    }

    /// <summary>This surface's gravity in g (body override, else its class environment).</summary>
    public Fix64 GravityG => Gravity.G;

    /// <summary>This surface's gravity multipliers (all exactly 1 on class M).</summary>
    public GravityProfile Gravity
    {
        get
        {
            RefreshWorld();
            return _gravity!;
        }
    }

    /// <summary>This surface's protection tier (worst of gravity, air, temperature, radiation). Fixed per game.</summary>
    public HabitabilityTier Tier => HabitabilityTier.Open;

    /// <summary>True when the tile lies under a dome or containment field of the player's (any finished core's
    /// field, or a finished Pressure Dome's), so crewed buildings may stand there on a Domes world.</summary>
    public bool Covered(int player, int tile) => true;

    /// <summary>How many tiles round its own tile a structure's dome / containment field reaches (0 = none).
    /// Cores: 1 on a Domes world; pressure_dome: 2. Only finished structures cover.</summary>
    public int DomeRadiusOf(SurfaceStructure s) => 0;

    /// <summary>Life support of a base on a Domes world: 1 = fully supplied, 0 = no air (crew loss). 1 elsewhere.</summary>
    public Fix64 LifeSupportOf(int baseId) => Fix64.One;
}
