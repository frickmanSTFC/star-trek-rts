using Godot;

namespace FinalFrontier;

/// <summary>
/// One drop ship model's numbers for <see cref="DropShipView"/> and <see cref="LandingView"/> (render-only). Model space
/// as Godot sees the glb: front -Z, up +Y, the ramp aft (+Z); Blender (x, y, z) = Godot (x, z, -y). <see cref="Halyard"/>
/// (build_dropship.py, the 24th-century ship every era but Dawn of Warp flies) is the default, with the numbers the
/// view always had; the three pre-warp ships (blender/build_dropship_prewarp.py --game, owner 2026-11-10) are
/// <see cref="Lander"/>, <see cref="Hopper"/> and <see cref="Freighter"/>, picked per player by
/// <see cref="DropShipHulls.PreWarpModel"/>. Numbers are the script's printed SPEC lines.
/// </summary>
public sealed class DropShipSpec
{
    /// <summary>The glb under res://models/vehicles/ (without .glb).</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>The ramp's hinge (the "Ramp" pivot) and the length of its main slab.</summary>
    public required Vector3 Hinge { get; init; }
    public required float RampLength { get; init; }

    /// <summary>The second slab ("RampTip", a child of Ramp at the main slab's end), folded back on the walking face and
    /// unfolded +180° about its local X once the main ramp is down; 0 = a one-piece ramp.</summary>
    public float TipLength { get; init; }

    /// <summary>The open ramp's angle about its local X (the toe rests on the ground).</summary>
    public float RampOpenDegrees { get; init; } = 109.6f;

    /// <summary>Ship origin to the open ramp's toe along +Z (where the ship stands from the unit's spot).</summary>
    public required float ToeReach { get; init; }

    /// <summary>The rear wheels of a unit parked in the bay (the start of its drive out).</summary>
    public required Vector3 BayStart { get; init; }

    /// <summary>The four pads on the ground (Leg0 front-left, Leg1 front-right, Leg2 rear-left, Leg3 rear-right).</summary>
    public required Vector3[] PadSpots { get; init; }

    /// <summary>Points under the landed ship and its open ramp that must be clear, flat ground.</summary>
    public required Vector3[] Footprint { get; init; }

    /// <summary>Leg fold in flight about each leg's local X (front legs -LegFold, rear +LegFold), and the feet's turn
    /// the other way (= LegFold keeps the pads level; less turns them with the leg).</summary>
    public float LegFold { get; init; } = 90f;
    public float FootFold { get; init; } = 90f;

    /// <summary>True: the climb-out pitches over toward the nose (aft thrusters lit); false: straight up (B Hopper).</summary>
    public bool PitchOver { get; init; } = true;

    /// <summary>LandingView's slung module: the cables' hard points under the belly, and the module's base below the
    /// ship's origin (its top must stay under the engine bells).</summary>
    public float BellyY { get; init; } = 0.85f;
    public float CarryDrop { get; init; } = 4f;

    /// <summary>The bay's own light (an OmniLight while the ramp is down).</summary>
    public Vector3 BayLamp { get; init; } = new(0, 3.8f, 2.6f);

    /// <summary>Landing lights and the belly/door flood (VehicleLights, Kind.DropShip).</summary>
    public required VehicleLights.Spec Lights { get; init; }

    public bool HasTip => TipLength > 0.01f;

    public string Path => $"res://models/vehicles/{Id}.glb";

    // ---------------------------------------------------------------- the models

    /// <summary>Halyard 07 (build_dropship.py): the numbers DropShipView always used.</summary>
    public static readonly DropShipSpec Halyard = new()
    {
        Id = "dropship",
        Title = "Halyard",
        Hinge = new Vector3(0, 1.25f, 6f),
        RampLength = 3.32f,
        ToeReach = 9.15f,
        BayStart = new Vector3(0, 1.25f, -0.5f),
        PadSpots = new[] { new Vector3(-4.25f, 0, -2.4f), new Vector3(4.25f, 0, -2.4f), new Vector3(-4.25f, 0, 5.5f), new Vector3(4.25f, 0, 5.5f) },
        Footprint = new[]
        {
            new Vector3(0, 0, -6.6f), new Vector3(-2.3f, 0, -4.5f), new Vector3(2.3f, 0, -4.5f), new Vector3(-2.3f, 0, 0), new Vector3(2.3f, 0, 0),
            new Vector3(0, 0, 0), new Vector3(-2.3f, 0, 5.8f), new Vector3(2.3f, 0, 5.8f), new Vector3(-3.75f, 0, -5.9f), new Vector3(3.75f, 0, -5.9f),
            new Vector3(-3.75f, 0, 3.9f), new Vector3(3.75f, 0, 3.9f), new Vector3(-4.25f, 0, -2.4f), new Vector3(4.25f, 0, -2.4f),
            new Vector3(-4.25f, 0, 5.5f), new Vector3(4.25f, 0, 5.5f), new Vector3(0, 0, 9.1f), new Vector3(-1.6f, 0, 8.6f), new Vector3(1.6f, 0, 8.6f),
        },
        // build_dropship.py: nose landing lamps (±0.85, 2.25, -6.62); belly (BELLY 0.85) under the bay.
        Lights = Lamps(new(0, 2.25f, -6.9f), new(0, 0.7f, 1.2f), -90f, 70f),
    };

    /// <summary>A Lander: octagonal descent stage (8.4 across), cargo module + cab to 8.0, four corner bells, a
    /// two-piece ramp from a deck 2.43 up, legs folding straight up beside the tanks with the pads turned flat.</summary>
    public static readonly DropShipSpec Lander = new()
    {
        Id = "dropship_prewarp_a",
        Title = "Lander",
        Hinge = new Vector3(0, 2.43f, 4.1f),
        RampLength = 3.75f,
        TipLength = 3.08f,
        ToeReach = 10.53f,
        BayStart = new Vector3(0, 2.43f, -3.05f),
        PadSpots = new[] { new Vector3(-5.02f, 0, -5.02f), new Vector3(5.02f, 0, -5.02f), new Vector3(-5.02f, 0, 5.02f), new Vector3(5.02f, 0, 5.02f) },
        Footprint = new[]
        {
            new Vector3(0, 0, -4.5f), new Vector3(0, 0, 0), new Vector3(-4.4f, 0, 0), new Vector3(4.4f, 0, 0), new Vector3(-3.1f, 0, -3.1f),
            new Vector3(3.1f, 0, -3.1f), new Vector3(-3.1f, 0, 3.1f), new Vector3(3.1f, 0, 3.1f), new Vector3(-5.02f, 0, -5.02f),
            new Vector3(5.02f, 0, -5.02f), new Vector3(-5.02f, 0, 5.02f), new Vector3(5.02f, 0, 5.02f), new Vector3(0, 0, 4.6f),
            new Vector3(0, 0, 7.5f), new Vector3(-1.6f, 0, 7.5f), new Vector3(1.6f, 0, 7.5f), new Vector3(0, 0, 10.5f), new Vector3(-1.6f, 0, 10.2f),
            new Vector3(1.6f, 0, 10.2f),
        },
        LegFold = 123.6f,
        FootFold = 33.6f,
        BellyY = 1.3f,
        CarryDrop = 4.3f,
        BayLamp = new Vector3(0, 5.4f, 2.5f),
        // Landing lamps under the stage (0, 1.27, ∓3.6); the flood over the door (lintel 6.33 up, 4.2 aft) down the ramp.
        Lights = Lamps(new(0, 1.15f, -3.75f), new(0, 6.5f, 4.3f), -135f, 60f),
    };

    /// <summary>B Hopper: an upright rocket 7.2 wide and 13.3 tall, four clustered bells, a side door that unfolds into
    /// a two-piece ramp from a floor 2.58 up, legs folding ~130° straight up the barrel, a straight climb-out.</summary>
    public static readonly DropShipSpec Hopper = new()
    {
        Id = "dropship_prewarp_b",
        Title = "Hopper",
        Hinge = new Vector3(0, 2.58f, 3.98f),
        RampLength = 3.82f,
        TipLength = 3.47f,
        ToeReach = 10.85f,
        BayStart = new Vector3(0, 2.58f, -2.05f),
        PadSpots = new[] { new Vector3(-4.49f, 0, -4.49f), new Vector3(4.49f, 0, -4.49f), new Vector3(-4.49f, 0, 4.49f), new Vector3(4.49f, 0, 4.49f) },
        Footprint = new[]
        {
            new Vector3(0, 0, -3.6f), new Vector3(0, 0, 0), new Vector3(-3.6f, 0, 0), new Vector3(3.6f, 0, 0), new Vector3(0, 0, 3.6f),
            new Vector3(-2.5f, 0, -2.5f), new Vector3(2.5f, 0, -2.5f), new Vector3(-2.5f, 0, 2.5f), new Vector3(2.5f, 0, 2.5f),
            new Vector3(-4.49f, 0, -4.49f), new Vector3(4.49f, 0, -4.49f), new Vector3(-4.49f, 0, 4.49f), new Vector3(4.49f, 0, 4.49f),
            new Vector3(0, 0, 6f), new Vector3(-1.6f, 0, 7.5f), new Vector3(1.6f, 0, 7.5f), new Vector3(0, 0, 10.8f), new Vector3(-1.6f, 0, 10.5f),
            new Vector3(1.6f, 0, 10.5f),
        },
        LegFold = 129.8f,
        FootFold = 39.8f,
        PitchOver = false,
        BellyY = 2f,
        CarryDrop = 4.2f,
        BayLamp = new Vector3(0, 5.6f, 2f),
        // Landing lamps under the base (0, 1.95, ∓2.3); the flood over the door (6.63 up, 3.98 aft) down the ramp.
        Lights = Lamps(new(0, 1.85f, -2.45f), new(0, 6.8f, 4.2f), -135f, 60f),
    };

    /// <summary>C Freighter: Halyard's layout (13.7 long, rear ramp from 1.25 up) with four engines hung on outriggers
    /// and orange tanks on its back; legs fold as Halyard's.</summary>
    public static readonly DropShipSpec Freighter = new()
    {
        Id = "dropship_prewarp_c",
        Title = "Freighter",
        Hinge = new Vector3(0, 1.25f, 6.4f),
        RampLength = 3.32f,
        ToeReach = 9.53f,
        BayStart = new Vector3(0, 1.25f, -0.9f),
        PadSpots = new[] { new Vector3(-5.65f, 0, -3.5f), new Vector3(5.65f, 0, -3.5f), new Vector3(-5.65f, 0, 4.55f), new Vector3(5.65f, 0, 4.55f) },
        Footprint = new[]
        {
            new Vector3(0, 0, -7.2f), new Vector3(-2.3f, 0, -4.5f), new Vector3(2.3f, 0, -4.5f), new Vector3(-2.3f, 0, 0), new Vector3(2.3f, 0, 0),
            new Vector3(0, 0, 0), new Vector3(-2.3f, 0, 5.8f), new Vector3(2.3f, 0, 5.8f), new Vector3(-4.15f, 0, -3.5f), new Vector3(4.15f, 0, -3.5f),
            new Vector3(-4.15f, 0, 4.55f), new Vector3(4.15f, 0, 4.55f), new Vector3(-5.65f, 0, -3.5f), new Vector3(5.65f, 0, -3.5f),
            new Vector3(-5.65f, 0, 4.55f), new Vector3(5.65f, 0, 4.55f), new Vector3(0, 0, 9.5f), new Vector3(-1.6f, 0, 9f), new Vector3(1.6f, 0, 9f),
        },
        BayLamp = new Vector3(0, 3.8f, 3.4f),
        // Nose landing lamp (0, 1.0, -5.2) under the chin; belly (0.85) under the bay as on Halyard.
        Lights = Lamps(new(0, 0.95f, -5.45f), new(0, 0.7f, 1.6f), -90f, 70f),
    };

    /// <summary>The pre-warp models in <see cref="DropShipHulls.PreWarpModel"/> order.</summary>
    public static readonly DropShipSpec[] PreWarp = { Lander, Hopper, Freighter };

    /// <summary>The model a player's drop ships use: Halyard, or in a Dawn of Warp match the player's pre-warp pick (if
    /// its glb is there; else Halyard).</summary>
    public static DropShipSpec For(string? era, ulong seed, int player)
    {
        if (!DropShipHulls.IsPreWarp(era)) return Halyard;
        var spec = PreWarp[DropShipHulls.PreWarpModel(seed, player)];
        return ResourceLoader.Exists(spec.Path) ? spec : Halyard;
    }

    private static VehicleLights.Spec Lamps(Vector3 landing, Vector3 flood, float floodPitch, float floodAngle)
    {
        var lamps = new[] { new VehicleLights.Lamp(VehicleLights.Role.Landing, landing, -50f, 95f, 28f, 10f, true), new VehicleLights.Lamp(VehicleLights.Role.Belly, flood, floodPitch, 16f, floodAngle, 4f) };
        return new VehicleLights.Spec(lamps, lamps, System.Array.Empty<Vector3>(), 0.3f, Vector2.Zero);
    }
}
