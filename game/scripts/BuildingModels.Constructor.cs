using Godot;

namespace FinalFrontier;

/// <summary>
/// The construction rover (surfaces.json construction_rover): models/vehicles/construction_rover.glb from
/// blender/build_constructor.py --game, or a primitive stand-in with the same part names. Front = -Z (Blender +Y),
/// 7.2 long. Parts PlanetView.Founding.cs poses as it unfolds (all pivots with no rest rotation, so the angles are plain):
/// "Jack0".."Jack3" (outrigger feet, slide down local Y by <see cref="ConstructorJackDrop"/>), "DockLid" (the roof dock's
/// hatch, hinged at its front edge, opens about X to <see cref="ConstructorLidOpen"/>), "Dock" (where the exocomps leave
/// and come back), "Crane" (slew turret behind the cab, turns about Y to <see cref="ConstructorSlew"/>) with "Boom"
/// (luffs about X to <see cref="ConstructorLuff"/>; rests pointing back over the deck) and its "Hook" at the tip, and
/// "Panels" (the folded hub on the deck; the crane sets it down beside the rover). "WheelFL".."WheelRR" pivots (flat
/// tyres), "Beacon", the "TailLamp" material, "Wiper0/1" and the decal markers NameL/NameR/NameRear/NumberL/NumberR are
/// RoverLook's and the damage view's, as on the rovers.
/// </summary>
public static partial class BuildingModels
{
    public const float ConstructorJackDrop = 0.85f;
    public const float ConstructorLidOpen = -110f;
    public const float ConstructorSlew = -90f;
    public const float ConstructorLuff = -38f;

    public static Node3D Constructor(Color owner)
    {
        var n = Imported("construction_rover", owner) ?? PrimitiveConstructor(owner);
        // Selection ring: PlanetView.Selection.cs.
        // Headlights: VehicleLights adds them to live units in PlanetView (not to the build-menu renders or the founder prop).
        return n;
    }

    /// <summary>Six-wheeled chassis, cab, flatbed with the folded hub, a slewing crane and the roof dock, from primitives.</summary>
    private static Node3D PrimitiveConstructor(Color owner)
    {
        var n = new Node3D { Name = "construction_rover" };
        var body = new Node3D { Name = "Body" };
        n.AddChild(body);
        var hull = Metal(HullColour, 0.45f, 0.4f);
        var dark = Metal(new Color(0.16f, 0.17f, 0.2f), 0.4f, 0.6f);
        var yellow = Metal(new Color(0.92f, 0.62f, 0.12f), 0.2f, 0.5f);
        var tyre = Metal(new Color(0.12f, 0.12f, 0.14f), 0.1f, 0.9f);
        var stripe = Glow(owner, 1.4f);
        // Chassis, cab (front, -Z) and the flatbed.
        Add(body, new BoxMesh { Size = new Vector3(2.6f, 0.7f, 6.8f) }, new Vector3(0, 1.05f, 0), hull);
        Add(body, new BoxMesh { Size = new Vector3(2.62f, 0.16f, 6.82f) }, new Vector3(0, 1.2f, 0), stripe);
        Add(body, new BoxMesh { Size = new Vector3(2.4f, 1.4f, 2.0f) }, new Vector3(0, 2.1f, -2.4f), hull);
        Add(body, new BoxMesh { Size = new Vector3(2.1f, 0.55f, 0.08f) }, new Vector3(0, 2.35f, -3.42f), Glow(Window, 1.5f));
        Add(body, new BoxMesh { Size = new Vector3(2.5f, 0.12f, 4.6f) }, new Vector3(0, 1.48f, 1.1f), dark);
        // Roof dock: a dark bay with the hatch hinged at its front edge.
        Add(body, new BoxMesh { Size = new Vector3(1.8f, 0.2f, 1.5f) }, new Vector3(0, 2.9f, -2.35f), dark);
        var lid = new Node3D { Name = "DockLid", Position = new Vector3(0, 3.02f, -3.1f) };
        Add(lid, new BoxMesh { Size = new Vector3(1.7f, 0.06f, 1.4f) }, new Vector3(0, 0, 0.7f), yellow);
        n.AddChild(lid);
        n.AddChild(new Node3D { Name = "Dock", Position = new Vector3(0, 3.3f, -2.35f) });
        // Beacon and tail lamps (RoverLook.Lamps).
        var amber = Glow(new Color(1f, 0.6f, 0.1f), 0.4f);
        amber.ResourceName = "Amber";
        Add(body, new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.2f, RadialSegments = 12 }, new Vector3(0.8f, 3.1f, -1.6f), amber).Name = "Beacon";
        var tail = Glow(new Color(1f, 0.12f, 0.08f), 1.2f);
        tail.ResourceName = "TailLamp";
        foreach (var x in new[] { -1.05f, 1.05f })
            Add(body, new BoxMesh { Size = new Vector3(0.35f, 0.14f, 0.05f) }, new Vector3(x, 1.1f, 3.42f), tail);
        // Outrigger jacks at the deck corners.
        var k = 0;
        foreach (var z in new[] { 0.7f, 3.05f })
            foreach (var x in new[] { -1.45f, 1.45f })
            {
                var jack = new Node3D { Name = $"Jack{k++}", Position = new Vector3(x, 0.9f, z) };
                Add(jack, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.9f, RadialSegments = 8 }, new Vector3(0, 0.45f, 0), yellow);
                Add(jack, new BoxMesh { Size = new Vector3(0.5f, 0.08f, 0.5f) }, Vector3.Zero, dark);
                n.AddChild(jack);
            }
        // Slewing crane behind the cab; the boom rests pointing back over the deck.
        var crane = new Node3D { Name = "Crane", Position = new Vector3(0, 1.55f, -1.0f) };
        Add(crane, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.55f, Height = 0.6f, RadialSegments = 12 }, new Vector3(0, 0.3f, 0), yellow);
        var boom = new Node3D { Name = "Boom", Position = new Vector3(0, 0.7f, 0) };
        Add(boom, new BoxMesh { Size = new Vector3(0.32f, 0.32f, 4.2f) }, new Vector3(0, 0, 2.1f), yellow);
        var hook = new Node3D { Name = "Hook", Position = new Vector3(0, 0, 4.2f) };
        Add(hook, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 1f }, new Vector3(0, -0.5f, 0), dark);
        Add(hook, new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.3f) }, new Vector3(0, -1f, 0), dark);
        boom.AddChild(hook);
        crane.AddChild(boom);
        n.AddChild(crane);
        // The folded hub: three panel stacks under straps, on the deck.
        var panels = new Node3D { Name = "Panels", Position = new Vector3(0, 1.55f, 1.6f) };
        Add(panels, new BoxMesh { Size = new Vector3(2.2f, 1.3f, 3.2f) }, new Vector3(0, 0.65f, 0), Metal(new Color(0.78f, 0.79f, 0.8f), 0.4f, 0.5f));
        Add(panels, new BoxMesh { Size = new Vector3(2.22f, 0.14f, 3.22f) }, new Vector3(0, 0.95f, 0), stripe);
        foreach (var z in new[] { -1.1f, 0f, 1.1f })
            Add(panels, new BoxMesh { Size = new Vector3(2.26f, 1.34f, 0.08f) }, new Vector3(0, 0.65f, z), dark);
        n.AddChild(panels);
        // Six wheels on pivots named like the glbs' (WheelFL ... WheelRR, see Surface.Wheels).
        foreach (var (z, axle) in new[] { (-2.3f, "F"), (-0.85f, "M"), (2.2f, "R") })
            foreach (var (x, side) in new[] { (-1.35f, "L"), (1.35f, "R") })
            {
                var wheel = new Node3D { Name = $"Wheel{axle}{side}", Position = new Vector3(x, 0.62f, z) };
                Add(wheel, new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.45f, RadialSegments = 14 }, Vector3.Zero, tyre, new Vector3(0, 0, 90));
                n.AddChild(wheel);
            }
        return n;
    }
}
