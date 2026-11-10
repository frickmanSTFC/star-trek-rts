using Godot;

namespace FinalFrontier;

/// <summary>
/// The survey satellite for the orbit view (docs/satellite-contract.md "Model"; blender/build_satellite.py --game):
/// models/orbit/survey_satellite.glb (the standard probe) or survey_satellite_prewarp.glb (Dawn of Warp), or a
/// primitive stand-in with the same part names when the glb isn't there or imported. Godot axes: -Z = direction of
/// travel, +Y = zenith, -Y = nadir (the planet). Parts the view drives (SatelliteRig): "Body", "PanelL"/"PanelR"
/// (wings, hinge at the bus side; rest = open; fold = Basis(Back, ±a), a 0..90°), "Dish" (spins about local +Y),
/// "Camera" (looks along local -Y at rest; "Lens" and "Flash" are its children), "Beacon", "Thruster0..3", material
/// "Owner" recoloured.
/// </summary>
public static partial class BuildingModels
{
    public const string SatellitePath = "res://models/orbit/survey_satellite.glb";
    public const string SatellitePrewarpPath = "res://models/orbit/survey_satellite_prewarp.glb";

    /// <summary>The era's satellite (pre-warp in Dawn of Warp), else the standard glb, else the stand-in.</summary>
    public static Node3D Satellite(Color owner, bool prewarp) =>
        (prewarp ? ImportedFile(SatellitePrewarpPath, "survey_satellite", owner) : null)
        ?? ImportedFile(SatellitePath, "survey_satellite", owner)
        ?? PrimitiveSatellite(owner, prewarp);

    /// <summary>What the card calls the model.</summary>
    public static string SatelliteModelName(bool prewarp) => prewarp ? "Earth survey satellite (pre-warp)" : "Orbital survey probe";

    private static Node3D PrimitiveSatellite(Color owner, bool prewarp)
    {
        var n = new Node3D { Name = "survey_satellite" };
        var hullColour = prewarp ? new Color(0.85f, 0.68f, 0.3f) : new Color(0.82f, 0.84f, 0.88f);
        var hull = Metal(hullColour, 0.5f, 0.35f);
        var dark = Metal(new Color(0.14f, 0.15f, 0.18f), 0.5f, 0.5f);
        var cells = Metal(new Color(0.1f, 0.18f, 0.42f), 0.4f, 0.25f);

        var body = new Node3D { Name = "Body" };
        n.AddChild(body);
        Add(body, new BoxMesh { Size = prewarp ? new Vector3(0.62f, 0.55f, 0.7f) : new Vector3(0.5f, 0.36f, 1.7f) }, Vector3.Zero, hull);
        Add(body, new BoxMesh { Size = prewarp ? new Vector3(0.64f, 0.06f, 0.72f) : new Vector3(0.06f, 0.05f, 1.5f) },
            prewarp ? new Vector3(0, 0.25f, 0) : new Vector3(0, 0.2f, 0), Metal(owner, 0.3f, 0.4f));

        foreach (var (name, side) in new[] { ("PanelL", -1f), ("PanelR", 1f) })
        {
            var hinge = new Node3D { Name = name, Position = new Vector3(side * (prewarp ? 0.32f : 0.26f), 0f, 0.05f) };
            var span = prewarp ? 1.15f : 1.05f;
            Add(hinge, new BoxMesh { Size = new Vector3(span, 0.025f, prewarp ? 0.62f : 0.5f) }, new Vector3(side * (span * 0.5f + 0.04f), 0, 0), cells);
            Add(hinge, new BoxMesh { Size = new Vector3(0.06f, 0.05f, 0.08f) }, new Vector3(side * 0.03f, 0, 0), dark);
            n.AddChild(hinge);
        }

        var dish = new Node3D { Name = "Dish", Position = prewarp ? new Vector3(0, 0.55f, -0.1f) : new Vector3(0, -0.24f, 0.3f) };
        var plate = Add(dish, new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.08f, Height = 0.08f, RadialSegments = 16 }, Vector3.Zero, Metal(new Color(0.75f, 0.78f, 0.82f), 0.7f, 0.3f));
        if (!prewarp) plate.RotationDegrees = new Vector3(180f, 0, 0);
        Add(dish, new BoxMesh { Size = new Vector3(0.05f, 0.03f, 0.3f) }, new Vector3(0, prewarp ? 0.05f : -0.05f, 0), Glow(new Color(0.55f, 0.8f, 1f), 3f));
        n.AddChild(dish);

        var camera = new Node3D { Name = "Camera", Position = prewarp ? new Vector3(0, -0.32f, 0) : new Vector3(0, -0.22f, -0.55f) };
        Add(camera, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.1f, Height = 0.22f, RadialSegments = 14 }, new Vector3(0, -0.06f, 0), dark);
        var lens = Add(camera, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.02f, RadialSegments = 14 }, new Vector3(0, -0.18f, 0),
            Glow(new Color(0.55f, 0.8f, 1f), 2f));
        lens.Name = "Lens";
        var flash = Add(camera, new TorusMesh { InnerRadius = 0.065f, OuterRadius = 0.085f, Rings = 16, RingSegments = 4 }, new Vector3(0, -0.17f, 0),
            Glow(new Color(1f, 0.97f, 0.92f), 1f));
        flash.Name = "Flash";
        n.AddChild(camera);

        var beacon = Add(n, new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 }, prewarp ? new Vector3(0, 0.32f, 0.3f) : new Vector3(0, 0.3f, 0.6f),
            Glow(prewarp ? new Color(1f, 0.2f, 0.1f) : new Color(0.65f, 0.85f, 1f), 6f));
        beacon.Name = "Beacon";
        for (var k = 0; k < 4; k++)
        {
            var x = (k % 2 == 0 ? -1f : 1f) * (prewarp ? 0.33f : 0.27f);
            var z = k < 2 ? (prewarp ? -0.36f : -0.8f) : (prewarp ? 0.36f : 0.85f);
            var thruster = Add(n, new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, new Vector3(x, 0, z),
                Glow(new Color(0.6f, 0.8f, 1f), 0f));
            thruster.Name = $"Thruster{k}";
        }
        return n;
    }
}
