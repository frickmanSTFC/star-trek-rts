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

    /// <summary>Only in range of a colony core that is still assembling after coming down from orbit.</summary>
    CoreAssembling,

    /// <summary>Only in range of a colony core founded on the ground that the exocomps are still building.</summary>
    CoreUnderConstruction,

    /// <summary>A crewed building on a Domes world outside every finished dome and containment field
    /// (text: <see cref="PlanetSurface.NeedsDomeProblem"/>).</summary>
    NeedsDome,

    /// <summary>An Excavation Site off a ruin site, or on one that is dug out (text: <see cref="PlanetSurface.RuinPlacementText"/>).</summary>
    NeedsRuin,

    /// <summary>Any other building on a ruin site that isn't dug out (text: <see cref="PlanetSurface.RuinPlacementText"/>).</summary>
    RuinInTheWay,
}
