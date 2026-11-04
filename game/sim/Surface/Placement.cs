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

    /// <summary>Needs a used-up deposit that hasn't been reopened before.</summary>
    NeedsDepletedDeposit,

    /// <summary>The base it would join isn't a high enough level.</summary>
    NeedsLevel,

    /// <summary>Its tech hasn't been researched yet.</summary>
    NeedsTech,

    /// <summary>Inside the native settlement's land (Prime Directive).</summary>
    NativeLand,
}
