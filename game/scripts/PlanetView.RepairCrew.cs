using Godot;

namespace FinalFrontier;

// The repair crews (building maintenance from each core, vehicle repairs on the pad) as CrewModels figures
// (owner 2026-11-08: "switch the repair crews to the new figures too"). Render-only. The walk timing still follows
// the sim's crew timers (UpdateBuildingCrews / UpdateCrew); this file only makes the person and poses them.
public partial class PlanetView
{
    private static StandardMaterial3D? _torchFlame, _torchSpark;

    /// <summary>An engineer in the owner's colour with a welding torch ("Torch": "Flame" + "Sparks") on the tool in the
    /// right hand (the glb's ToolTip marker follows the hand; the primitive stand-in's "Tool").</summary>
    private static Node3D NewRepairer(Color owner, int seed)
    {
        var person = CrewModels.Build("engineer", owner, seed);
        var anchor = person.FindChild("ToolTip", true, false) as Node3D
            ?? person.FindChild("Tool", true, false) as Node3D
            ?? person;
        _torchFlame ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.65f, 0.85f, 1f),
            EmissionEnabled = true,
            Emission = new Color(0.65f, 0.85f, 1f),
            EmissionEnergyMultiplier = 6f,
        };
        _torchSpark ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.85f, 0.45f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.85f, 0.45f),
            EmissionEnergyMultiplier = 5f,
        };
        var torch = new Node3D { Name = "Torch" };
        if (anchor == person) torch.Position = new Vector3(0.3f, 1.0f, -0.4f); // no hand marker: in front at hand height
        torch.AddChild(new MeshInstance3D
        {
            Name = "Flame",
            Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3, Material = _torchFlame },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        });
        torch.AddChild(new CpuParticles3D
        {
            Name = "Sparks",
            Amount = 24,
            Lifetime = 0.5f,
            Emitting = false,
            LocalCoords = false,
            Direction = new Vector3(0, 0.6f, -1f),
            Spread = 55f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 4f,
            Gravity = new Vector3(0, -9f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1f,
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 4, Rings = 2, Material = _torchSpark },
        });
        anchor.AddChild(torch);
        return person;
    }

    /// <summary>Plays Walk / Work / Idle (only when it changes), matches the walk cycle to how fast the person really
    /// moved this frame (the sim's timers set the pace, so the feet would skate otherwise), and lights the torch while
    /// welding.</summary>
    private void PoseRepairer(Node3D person, Vector3 before, bool walking, bool welding, int i, float delta)
    {
        var action = walking ? "Walk" : welding ? "Work" : "Idle";
        if (!person.HasMeta("rc_action") || person.GetMeta("rc_action").AsString() != action)
        {
            person.SetMeta("rc_action", action);
            CrewModels.Play(person, action);
        }
        if (CrewModels.Player(person) is { } player)
        {
            var moved = new Vector2(person.Position.X - before.X, person.Position.Z - before.Z).Length();
            var walkSpeed = CrewModels.WalkSpeed * person.GetChild<Node3D>(0).Scale.Y;
            player.SpeedScale = walking && delta > 0f ? Mathf.Clamp(moved / delta / walkSpeed, 0.6f, 2.6f) : 1f;
        }
        if (person.FindChild("Torch", true, false) is not Node3D torch) return;
        if (torch.GetNodeOrNull<CpuParticles3D>("Sparks") is { } sparks && sparks.Emitting != welding) sparks.Emitting = welding;
        if (torch.GetNodeOrNull<Node3D>("Flame") is { } flame) flame.Visible = welding && Mathf.Sin(_time * 40f + i * 3f) > -0.6f;
    }
}
