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
        _lookMaterial?.SetShaderParameter("sun_dir", world); // another landable body's look-only globe (OrbitView.Bodies.cs)
        (_clouds.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
        (_highClouds.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
        (_atmosphere.MaterialOverride as ShaderMaterial)?.SetShaderParameter("sun_dir", world);
    }

    private Node3D? _climateStorms;
    private int _climateStormVersion = -1;

    /// <summary>
    /// Storm rings on the Atmosphere display where Climate has stormy weather (the driver samples it every few seconds):
    /// they replace the cloud-noise rings while there are any, and the noise rings come back when there are none.
    /// Every frame in orbit (so a display change shows the right set); the rings are rebuilt when <paramref name="version"/> changes.
    /// </summary>
    public void SetClimateStorms(System.Collections.Generic.IReadOnlyList<int> tiles, int version)
    {
        if (version != _climateStormVersion)
        {
            _climateStormVersion = version;
            if (_climateStorms == null)
            {
                _climateStorms = new Node3D { Name = "ClimateStorms" };
                _globe.AddChild(_climateStorms);
            }
            foreach (var child in _climateStorms.GetChildren()) child.QueueFree();
            var height = Radius + LowClouds.Height + 0.5f;
            foreach (var tile in tiles)
            {
                var dir = TileDirection(tile);
                var cell = new Node3D { Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, dir)), dir * height) };
                cell.AddChild(OrbitLines.Instance(OrbitLines.Circle(height * 0.2f, 72), Peach with { A = 0.9f }, 3));
                cell.AddChild(OrbitLines.Instance(OrbitLines.Circle(height * 0.11f, 36, dashed: true), Peach with { A = 0.6f }, 3));
                _climateStorms.AddChild(cell);
            }
        }
        var atmosphere = Display == OrbitDisplay.Atmosphere;
        if (_climateStorms != null) _climateStorms.Visible = atmosphere && tiles.Count > 0;
        _weather.Visible = atmosphere && tiles.Count == 0;
    }
}
