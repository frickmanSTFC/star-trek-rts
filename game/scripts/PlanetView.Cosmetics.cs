using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Achievement cosmetics on the local player's own models (render-only; CosmeticLook does the materials). PlanetView
// dresses our vehicles as it makes them (DressVehicle in UpdateRovers), our founders' stand-ins and Pressure Domes, and
// re-applies the picks once a frame when Cosmetics.Version moves (UpdateCosmetics in Refresh, before the rovers).
// Other players' models are never dressed.
public partial class PlanetView
{
    /// <summary>The player this machine plays (ConstructionView's beams, DropShipView's names).</summary>
    public int LocalPlayer => _localPlayer;

    private int _cosmeticsVersion = int.MinValue;

    /// <summary>A new vehicle model: ours gets the paint, pattern and finish (and the names' ink to match).</summary>
    private void DressVehicle(SurfaceUnit u, Node3D model, Color colour)
    {
        if (u.Owner != _localPlayer) return;
        CosmeticLook.Dress(model, colour);
        if (_grime.TryGetValue(u.Id, out var grime)) grime.Ink = CosmeticLook.Ink;
    }

    /// <summary>A new building model: our Pressure Domes' glass takes the dome tint.</summary>
    private void DressStructure(SurfaceStructure s, Node3D model)
    {
        if (s.Owner == _localPlayer && s.DefId == "pressure_dome") CosmeticLook.DressGlass(model);
    }

    /// <summary>Once a frame: re-apply the picks to everything already dressed when they changed.</summary>
    private void UpdateCosmetics()
    {
        CosmeticLook.Refresh();
        if (_cosmeticsVersion == Cosmetics.Version) return;
        _cosmeticsVersion = Cosmetics.Version;
        var ink = CosmeticLook.Ink;
        foreach (var u in _surface.Units)
            if (u.Owner == _localPlayer && _grime.TryGetValue(u.Id, out var grime)) grime.Ink = ink;
    }
}
