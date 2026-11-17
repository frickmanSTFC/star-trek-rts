using Godot;

namespace FinalFrontier;

// The player's flagship in the orbit view (docs/ship-model-contract.md "Orbit view"; render-only). When the picked
// ship has a 3D model (ShipModels: models/ships/<id>.glb) it rides where the peach ship marker is, on the ship track
// at R+32: bow along the direction of travel, dorsal side away from the planet, lit by the orbit view's sun (plus a
// faint fill from the camera's side that reaches only the ship, so its night side is not a black cut-out), drawn about
// 14 units long (far bigger than life, like the satellites), rings turning. Shown on the Planet and Orbit displays
// while the flagship is in orbit of the body on screen; hidden on System and Atmosphere and while it is in flight
// (the marker comes back then). Which ship: the sim's pick for the local player, else GameSetup's, else in a debug
// build with no pick (the sandbox) the Cochrane, so the model can be seen there too.
// FollowShip is the satellites' FOLLOW for the ship: the ship does not turn with the globe, so instead of turning
// the globe the camera flies round to it (along an arc that stays clear of the planet) and holds a close three-quarter
// view from ahead and outside, the planet behind it; the wheel zooms that view; any manual turn, a Focus, leaving the
// display or the ship leaving lets go and the camera flies back. PickFlagship is the mouse test (never while an
// action is armed: the driver asks only then), HoverShip draws a ring round it.
// Hooks in OrbitView.cs: Refresh → UpdateShip, Rotate / Focus → the follow lets go, Zoom → ZoomShip, Tracked → ShipTrackNode.

public partial class OrbitView
{
    /// <summary>The model is drawn this long, whatever its real size (the satellites are ~3 long, the globe 200 across).</summary>
    private const float ShipDrawLength = 14f;

    /// <summary>The ship's own render layer (besides layer 1): the fill light's cull mask, so it lights nothing else.</summary>
    public const uint ShipLayer = 1u << 11;

    /// <summary>The follow view: camera distance from the ship (wheel: Min..Max) and the seconds the camera takes to fly there.</summary>
    private const float ShipLookStart = 30f, ShipLookMin = 16f, ShipLookMax = 90f, ShipLookSeconds = 1.3f;

    private Node3D? _shipHolder;
    private Node3D? _shipModel;
    private DirectionalLight3D? _shipFill;
    private MeshInstance3D? _shipRing;
    private StandardMaterial3D? _shipRingMaterial;
    private bool _shipTried, _shipShown, _followShip;
    private float _shipLook; // 0 = the display's own camera .. 1 = the follow view
    private float _shipLookDistance = ShipLookStart, _shipLookTarget = ShipLookStart;

    /// <summary>The ship whose model the orbit view shows: the sim's pick, else the menu's, else (debug builds, no pick) the Cochrane.</summary>
    public string? ShipModelId => _sim.ShipChoiceOf(_localPlayer)?.Id ?? GameSetup.ShipId ?? (OS.IsDebugBuild() ? ShipModels.DebugShip : null);

    /// <summary>The flagship's model is on screen now (Planet / Orbit display, in orbit of the body shown).</summary>
    public bool ShipShown => _shipShown;

    /// <summary>The flagship has a model and is in orbit (not in flight), whatever the display: SHIP can be offered.</summary>
    public bool ShipAvailable => _shipHolder != null && (_source.Flagship is not { } f || !SystemSource.InFlight(f));

    /// <summary>The mouse is over the ship (the driver sets it): a ring is drawn round it.</summary>
    public bool HoverShip { get; set; }

    /// <summary>The camera flies to the ship and stays with it (SHIP on the helm rail, or a click on the ship). Turning
    /// the globe by hand, a Focus, a satellite's FOLLOW or the ship going out of view lets go.</summary>
    public bool FollowShip
    {
        get => _followShip;
        set
        {
            if (value == _followShip) return;
            _followShip = value;
            if (!value) return;
            FollowSatellite = null;
            _shipLookTarget = ShipLookStart; // each new look starts at the standard distance
        }
    }

    /// <summary>The ship's node for the tracked-objects row and screen positions: the model while it shows, else the marker.</summary>
    private Node3D ShipTrackNode => _shipShown && _shipHolder != null ? _shipHolder : _shipMarker;

    /// <summary>Leaving the orbit view (down to the surface) drops the follow, so coming back starts on the planet.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationVisibilityChanged || Visible) return;
        _followShip = false;
        _shipLook = 0f;
    }

    /// <summary>Debug: throw the model away and build it again (ShipModels.DebugStandIn was flipped).</summary>
    public void RebuildShip()
    {
        _shipHolder?.QueueFree();
        _shipFill?.QueueFree();
        _shipRing?.QueueFree();
        _shipHolder = _shipModel = null;
        _shipFill = null;
        _shipRing = null;
        _shipRingMaterial = null;
        _shipTried = _shipShown = _followShip = false;
        _shipLook = 0f;
        if (_shipMarker != null) _shipMarker.Visible = true;
    }

    /// <summary>The model, once (the first frame in orbit): scaled to its drawn length, landers on a colony ship's
    /// berths, its own layer added for the fill light; plus that light and the hover ring. No model = nothing is made
    /// and the marker stays as it was.</summary>
    private void EnsureShip()
    {
        if (_shipTried) return;
        _shipTried = true;
        var model = ShipModels.Build(ShipModelId);
        if (model == null) return;
        var length = ShipModels.LengthOf(model); // before the landers
        if (_sim.CanColonize(_localPlayer))
            ShipModels.HangLanders(model, DropShipSpec.For(_sim.Data.Orbit?.Era, _surface.Map.Seed, _localPlayer), PlanetDriver.ColorOf(_localPlayer));
        model.Scale = Vector3.One * (ShipDrawLength / length);
        ShipModels.AddLayers(model, ShipLayer);
        _shipModel = model;
        _shipHolder = new Node3D { Name = "Flagship", Visible = false };
        _shipHolder.AddChild(model);
        AddChild(_shipHolder);

        // A faint cool fill from the camera's side, for the ship alone (planetshine, more or less).
        _shipFill = new DirectionalLight3D
        {
            Name = "FlagshipFill",
            LightColor = new Color(0.6f, 0.72f, 1f),
            LightEnergy = 0.3f,
            ShadowEnabled = false,
            LightCullMask = ShipLayer,
            Visible = false,
        };
        AddChild(_shipFill);

        _shipRingMaterial = OrbitLines.Material(new Color(1f, 1f, 1f, 0.75f), 4);
        _shipRingMaterial.NoDepthTest = true;
        _shipRing = new MeshInstance3D
        {
            Name = "FlagshipRing",
            Mesh = new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1f, Rings = 48, RingSegments = 3 },
            MaterialOverride = _shipRingMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_shipRing);
    }

    /// <summary>The ship under the mouse: within its drawn half-length on screen (at least ~18 px), in front of the
    /// camera and not behind the planet. False when it isn't shown.</summary>
    public bool PickFlagship(Vector2 mouse)
    {
        if (!_shipShown || _shipHolder == null) return false;
        var at = _shipHolder.GlobalPosition;
        if (_camera.IsPositionBehind(at) || Occluded(at)) return false;
        var height = ViewportSize().Y;
        var distance = Mathf.Max(1f, _camera.GlobalPosition.DistanceTo(at));
        var reach = Mathf.Max(18f * Mathf.Max(1f, height / 720f), ShipDrawLength * 0.5f / distance * height / (2f * TanHalfFov()));
        return _camera.UnprojectPosition(at).DistanceTo(mouse) <= reach;
    }

    /// <summary>The wheel while following: the follow view's own distance. True when it took the zoom.</summary>
    private bool ZoomShip(float factor)
    {
        if (!_followShip) return false;
        _shipLookTarget = Mathf.Clamp(_shipLookTarget * factor, ShipLookMin, ShipLookMax);
        return true;
    }

    /// <summary>Every frame from Refresh, after the display's camera is framed and the marker is placed: the model's
    /// place and turn on the track, its rings, the fill light, the hover ring, and the follow camera.</summary>
    /// <param name="cameraDistance">The display's own camera distance this frame (the follow view blends from it).</param>
    private void UpdateShip(float delta, float cameraDistance)
    {
        EnsureShip();
        var flagship = _source.Flagship;
        var shown = _shipHolder != null && _shipModel != null && Display is OrbitDisplay.Planet or OrbitDisplay.Orbit
            && (flagship == null || !SystemSource.InFlight(flagship));
        _shipShown = shown;
        _shipMarker.Visible = !shown; // the peach marker stands in whenever the model doesn't show
        if (_shipHolder != null) _shipHolder.Visible = shown;
        if (_shipFill != null) _shipFill.Visible = shown;
        if (!shown)
        {
            // Nothing to look at: back to the display's own camera at once (the System plot has its own framing).
            _followShip = false;
            HoverShip = false;
            _shipLook = 0f;
            if (_shipRing != null) _shipRing.Visible = false;
            return;
        }
        if (FollowSatellite != null) _followShip = false;

        // On the track where the marker is: up = away from the planet, bow (-Z) = the direction of travel.
        var track = _shipOrbit.Basis;
        var position = track * _shipMarker.Position;
        var outward = position.Normalized();
        var normal = track.Y.Normalized(); // the track's own "north"
        var ahead = normal.Cross(outward).Normalized(); // the marker's angle grows with time: this is the way it moves
        var z = -ahead;
        _shipHolder!.Transform = new Transform3D(new Basis(outward.Cross(z), outward, z), position);
        ShipModels.Spin(_shipModel!, delta);

        // The follow camera: blend the display's camera (pointing at the planet) into a close view of the ship from
        // ahead, outside and a little above its track. Direction and distance from the planet are blended apart, so
        // the path is an arc round the planet, never through it.
        _shipLook = Mathf.MoveToward(_shipLook, _followShip ? 1f : 0f, delta / ShipLookSeconds);
        _shipLookDistance = Mathf.Lerp(_shipLookDistance, _shipLookTarget, 1f - Mathf.Exp(-6f * delta));
        if (_shipLook > 0.0005f)
        {
            var b = Mathf.SmoothStep(0f, 1f, _shipLook);
            var from = new Vector3(0f, Mathf.Sin(_elevation), Mathf.Cos(_elevation)) * cameraDistance;
            var to = position + (outward * 0.62f + normal * 0.38f + ahead * 0.55f).Normalized() * _shipLookDistance;
            var direction = from.Normalized().Slerp(to.Normalized(), b);
            direction = direction.LengthSquared() < 1e-4f ? to.Normalized() : direction.Normalized();
            var eye = direction * Mathf.Lerp(from.Length(), to.Length(), b);
            var target = position * b;
            var up = Vector3.Up.Slerp(normal, b).Normalized();
            _camera.Position = eye;
            _camera.LookAt(target, up);
            FrameViewRect(eye.DistanceTo(target)); // the ship (not the planet) lands in the middle of the open view
        }

        var camera = _camera.GlobalPosition;
        var toShip = position - camera;
        var range = toShip.Length();
        if (range > 0.01f)
        {
            var dir = toShip / range;
            _shipFill!.Basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Forward : Vector3.Up); // shines the way the camera looks
        }

        // Hover ring: facing the camera, round the whole ship (not while the follow view is on it).
        var ringed = HoverShip && !_followShip && _shipLook < 0.05f && range > 0.01f;
        _shipRing!.Visible = ringed;
        if (ringed)
        {
            var size = Mathf.Max(ShipDrawLength * 0.62f, 0.03f * range);
            _shipRing.GlobalTransform = new Transform3D(new Basis(new Quaternion(Vector3.Up, -toShip / range)).Scaled(new Vector3(size, size, size)), position);
        }
    }
}
