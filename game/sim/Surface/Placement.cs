namespace FinalFrontier.Sim.Surface;

/// <summary>Why a building can't go on a tile (None = it can).</summary>
public enum PlacementProblem
{
    None,
    NotInBuildMenu,
    OffMap,
    NotLanded,
    NotSurveyed,
    TerrainUnbuildable,
    Occupied,
    OutOfRange,
    NeedsDeposit,
    CannotAfford,
}
