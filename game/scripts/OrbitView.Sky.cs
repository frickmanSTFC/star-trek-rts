using Godot;

namespace FinalFrontier;

// The globe's sun follows Climate.Sun (PlanetDriver.Sky calls SetSun every frame after Refresh): the star stands over
// the subsolar column at the declination's latitude, in the globe's own frame, so the day side of the globe is the map
// columns in day on the surface and the terminator tilts with the season. The light, the ground, the two cloud layers
// and the atmosphere all get the new direction.
public partial class OrbitView
{
    private Vector3 _sunLocal = Vector3.Zero;

    /// <summary>Globe-local direction toward the star overhead at (fractional) column <paramref name="subsolarColumn"/>
    /// and latitude <paramref name="declinationDeg"/> (the same u/v mapping as <see cref="TileDirection"/>).</summary>
    public Vector3 SunLocal(float subsolarColumn, float declinationDeg)
    {
        var g = _surface.Map.Grid;
        var phi = (subsolarColumn + 0.5f) / g.Width * Mathf.Tau;
        var lat = Mathf.DegToRad(declinationDeg);
        return new Vector3(Mathf.Cos(lat) * Mathf.Sin(phi), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(phi));
    }

    /// <summary>Point the orbit sun along a globe-local direction (call after Refresh, which turns the globe).</summary>
    public void SetSun(Vector3 local)
    {
        if (local.LengthSquared() < 1e-6f) return;
        _sunLocal = local.Normalized();
        var world = (_globe.Basis * _sunLocal).Normalized();
        if (world.IsEqualApprox(_sunDir)) return;
        _sunDir = world;
        // A directional light shines along its -Z: look away from the sun (pick an up that isn't parallel).
        var up = Mathf.Abs(world.Y) > 0.98f ? Vector3.Forward : Vector3.Up;
        _light.Basis = Basis.LookingAt(-world, up);
        _groundMaterial.SetShaderParameter("sun_dir", world);
        (_clouds.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
        (_highClouds.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
        (_atmosphere.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
    }
}
