using Godot;

namespace FinalFrontier;

/// <summary>
/// A live, slowly turning 3D model of a ship for the campaign's ship pick page (docs/ship-model-contract.md "Ship pick
/// page"; the approach is ModelThumbnails' hero studio). Its own <see cref="SubViewport"/> with its own World3D: a
/// camera fitted to the model's bounds, a key light with shadows, a dim cool fill and a rim, no sky to see (the
/// background is see-through over a near-black panel). The rings turn at the contract's rates (ShipModels.Spin); a
/// left-drag turns the ship by hand (and tips the view a little), then the slow turn carries on. The viewport's
/// size follows the control's; it renders only while the control is visible in the tree and goes with it when the
/// page is rebuilt. <see cref="Create"/> returns null for a ship without a model: the page keeps its still picture.
/// Render-only.
/// </summary>
public partial class ShipViewer : Control
{
    /// <summary>The model is scaled to this length in the studio (sane depth and shadow ranges whatever its units).</summary>
    private const float StudioLength = 20f;

    private const float Fov = 30f, TurnRate = 0.16f, MinPitch = -0.3f, MaxPitch = 0.9f;

    /// <summary>The first view: the bow toward the camera's left, a little above.</summary>
    public const float StartYaw = 2.2f, StartPitch = 0.28f;

    private readonly SubViewport _port;
    private readonly Camera3D _camera;
    private readonly Node3D _pivot;
    private readonly Node3D _ship;
    private readonly DirectionalLight3D _key;
    private TextureRect _picture = null!;
    private float _halfWidth, _halfHeight; // the turning model's bounding cylinder (about the vertical axis)
    private bool _dragging;
    private float _hold; // seconds the slow turn still waits after a drag

    public string ShipId { get; }

    /// <summary>The turn about the vertical axis and the camera's height angle, radians (kept across page rebuilds).</summary>
    public float Yaw { get; set; } = StartYaw;
    public float Pitch { get; set; } = StartPitch;

    /// <summary>The viewer for a ship, or null when it has no 3D model (ShipModels.Build). <paramref name="lander"/> =
    /// the drop ship to hang on its berths (null = leave them empty).</summary>
    public static ShipViewer? Create(string shipId, DropShipSpec? lander, Color owner)
    {
        var ship = ShipModels.Build(shipId);
        return ship == null ? null : new ShipViewer(shipId, ship, lander, owner);
    }

    private ShipViewer(string shipId, Node3D ship, DropShipSpec? lander, Color owner)
    {
        ShipId = shipId;
        _ship = ship;
        Name = "ShipViewer";
        MouseFilter = MouseFilterEnum.Stop; // the drag
        MouseDefaultCursorShape = CursorShape.Drag;
        ClipContents = true;

        // Size, own world, see-through background, MSAA and update mode are set before the viewport enters the tree
        // (it does when this control does).
        _port = new SubViewport
        {
            Name = "ShipStudio",
            Size = new Vector2I(640, 400),
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, // _Process turns it on while shown
            PositionalShadowAtlasSize = 0,
        };
        AddChild(_port);

        // Space: little ambient, reflections from a dark plain sky so bare metal is not black. No fog, glow or SSAO.
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.05f, 0.07f, 0.13f),
                SkyHorizonColor = new Color(0.16f, 0.2f, 0.3f),
                GroundBottomColor = new Color(0.02f, 0.03f, 0.05f),
                GroundHorizonColor = new Color(0.12f, 0.15f, 0.22f),
                SunAngleMax = 30f,
            },
        };
        _port.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.4f, 0.48f, 0.68f),
                AmbientLightEnergy = 0.22f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = 1.05f,
                GlowEnabled = false,
                FogEnabled = false,
                VolumetricFogEnabled = false,
                SsaoEnabled = false,
            },
        });

        // The sun from the upper left in front (the rings shadow the spine); a dim cool fill from the right, a rim from behind.
        _key = new DirectionalLight3D
        {
            Name = "Key",
            LightColor = new Color(1f, 0.97f, 0.92f),
            LightEnergy = 1.7f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 90f,
            RotationDegrees = new Vector3(-38f, -42f, 0f),
        };
        _port.AddChild(_key);
        _port.AddChild(new DirectionalLight3D { Name = "Fill", LightColor = new Color(0.55f, 0.68f, 1f), LightEnergy = 0.28f, RotationDegrees = new Vector3(-12f, 75f, 0f) });
        _port.AddChild(new DirectionalLight3D { Name = "Rim", LightColor = new Color(0.8f, 0.88f, 1f), LightEnergy = 0.6f, RotationDegrees = new Vector3(-20f, 160f, 0f) });

        _camera = new Camera3D { Name = "Camera", Fov = Fov, Near = 0.5f, Far = 400f, KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true };
        _port.AddChild(_camera);

        // The ship, scaled to the studio and centred on the pivot so it turns about its middle.
        _pivot = new Node3D { Name = "Pivot" };
        _port.AddChild(_pivot);
        var box = ShipModels.Bounds(ship); // before the landers: they sit inside the hull's box anyway
        if (lander != null) ShipModels.HangLanders(ship, lander, owner);
        var scale = StudioLength / Mathf.Max(1f, box.Size.Z);
        ship.Scale = Vector3.One * scale;
        ship.Position = -box.GetCenter() * scale;
        _pivot.AddChild(ship);
        _halfWidth = new Vector2(box.Size.X, box.Size.Z).Length() * 0.5f * scale;
        _halfHeight = box.Size.Y * 0.5f * scale;
    }

    public override void _Ready()
    {
        var back = new Panel { Name = "Back", MouseFilter = MouseFilterEnum.Ignore };
        back.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.012f, 0.018f, 0.035f, 1f), new Color(HudKit.Line, 0.6f), 0));
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(back);

        _picture = new TextureRect
        {
            Name = "Picture",
            Texture = _port.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, // the viewport is kept at this control's size
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _picture.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_picture);

        var s = HudKit.ScaleFor(GetViewport().GetVisibleRect().Size);
        var hint = HudKit.Label(ShipModels.IsStandIn(_ship) ? "DEBUG STAND-IN  ·  DRAG TO TURN" : "DRAG TO TURN", 11 * s, new Color(HudKit.Dim, 0.75f), condensed: true, bold: true);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(hint);
        hint.GrowVertical = GrowDirection.Begin;
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize, Mathf.RoundToInt(8 * s));

        FitPort();
        Aim();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsNodeReady()) FitPort();
    }

    /// <summary>The viewport renders at the control's size (capped), so the picture is never stretched.</summary>
    private void FitPort()
    {
        var size = new Vector2I(Mathf.Clamp(Mathf.RoundToInt(Size.X), 16, 1920), Mathf.Clamp(Mathf.RoundToInt(Size.Y), 16, 1200));
        if (_port.Size != size) _port.Size = size;
    }

    public override void _Process(double delta)
    {
        var shown = IsVisibleInTree();
        var mode = shown ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (_port.RenderTargetUpdateMode != mode) _port.RenderTargetUpdateMode = mode;
        if (!shown) return;
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Left)) _dragging = false; // released outside the window
        if (_dragging) _hold = 1.5f;
        else if (_hold > 0f) _hold -= (float)delta;
        else Yaw = Mathf.PosMod(Yaw + TurnRate * (float)delta, Mathf.Tau);
        ShipModels.Spin(_ship, delta);
        Aim();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _dragging = b.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _dragging:
                Yaw = Mathf.PosMod(Yaw + m.Relative.X * 0.01f, Mathf.Tau);
                Pitch = Mathf.Clamp(Pitch + m.Relative.Y * 0.006f, MinPitch, MaxPitch);
                AcceptEvent();
                break;
        }
    }

    /// <summary>Turns the model and puts the camera at its height angle, just far enough that the model's bounding
    /// cylinder fits the picture at every turn (so nothing leaves the frame while it spins).</summary>
    private void Aim()
    {
        _pivot.Rotation = new Vector3(0f, Yaw, 0f);
        var size = _port.Size;
        var aspect = size.Y > 0 ? (float)size.X / size.Y : 1.6f;
        var tanV = Mathf.Tan(Mathf.DegToRad(Fov * 0.5f));
        var tanH = tanV * aspect;
        float sin = Mathf.Sin(Pitch), cos = Mathf.Cos(Pitch);
        // Widest point: the rim seen side on, at the pivot's depth. Tallest: the near rim's edge, nearer by w·cos.
        var distance = Mathf.Max(_halfWidth / tanH, (_halfHeight * cos + _halfWidth * Mathf.Abs(sin)) / tanV + _halfWidth * cos) / 0.94f;
        var position = new Vector3(0f, sin, cos) * distance;
        _camera.Transform = new Transform3D(Basis.LookingAt(-position, Vector3.Up), position);
        _camera.Near = Mathf.Max(0.2f, distance - _halfWidth * 1.6f);
        _camera.Far = distance + _halfWidth * 3f;
        _key.DirectionalShadowMaxDistance = distance + _halfWidth * 2f;
    }
}
