using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>A ground vehicle cycling through the edge of a dome / containment field (docs/missions-contract.md A):
/// it stops at the hex edge where the field's shell stands for domes.transit_seconds while its own field forms and
/// meshes with the big one. StructureId = the dome or core whose field it is crossing; Entering = going under cover;
/// Fraction 0 → 1 over the pause; Contact = where it stands (the edge crossing point, world units like
/// <see cref="SurfaceUnit.Pos"/>); FromTile → ToTile is the edge being crossed; ticks from the sim clock.</summary>
public readonly record struct DomeTransit(int StructureId, bool Entering, Fix64 Fraction, FixVec2 Contact,
    int FromTile, int ToTile, long StartTick, long EndTick);

public sealed partial class PlanetSurface
{
    // STUB (contract): agent S1 replaces this file's body. Keep the signatures.

    /// <summary>The vehicle's dome transit under way, or null (always null on worlds without the domes kit, for aerial
    /// units and for vehicles not at a field edge).</summary>
    public DomeTransit? DomeTransitOf(SurfaceUnit unit) => null;

    /// <summary>The most recent finished transit of this vehicle (for the view's after-glow: the field lingers ~1.2 s
    /// after it drives through), or null.</summary>
    public DomeTransit? LastDomeTransitOf(SurfaceUnit unit) => null;
}
