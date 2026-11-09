using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The main menu (scenes/MainMenu.tscn, the main scene; docs/menu-contract.md A). A slowly turning planet over a
/// starfield behind code-built screens in the HUD's look (HudKit): the title (CAMPAIGN, MULTIPLAYER locked, SETTINGS,
/// DEBUG, QUIT), then for the campaign the faction screen (MainMenu.Campaign.cs: seven great powers, only the
/// Federation open), the era screen (only Dawn of Warp open) and the star-system picker (MainMenu.SystemPick.cs: LAND
/// HERE → GameSetup.StartCampaign → Planet.tscn). DEBUG = GameSetup.StartDebug → Planet.tscn, the sandbox as before.
/// Each screen is rebuilt when shown and when the window's UI scale changes; Esc goes back one screen.
/// </summary>
public partial class MainMenu : Node3D
{
    private enum Page { Title, Faction, Era, System, Settings }

    private Page _page = Page.Title;
    private Page _settingsBack = Page.Title;
    private Control _ui = null!;
    private Control? _pageRoot;

    /// <summary>Above the page (rebuilt pages don't free what is here): the tutorial cards.</summary>
    private Control _overlay = null!;

    private float _builtScale = -1f;
    private Vector2 _builtSize;
    private float _s = 1f;

    private Node3D _planet = null!;
    private Node3D _clouds = null!;
    private Node3D _moon = null!;
    private double _time;

    /// <summary>The game data (loaded on the first CAMPAIGN press, with the campaign's setup so the star systems the picker
    /// shows are the ones the game generates).</summary>
    private GameData? _data;

    public const string CampaignFaction = "federation";
    public const string CampaignEra = "dawn_of_warp";

    public override void _Ready()
    {
        GameSettings.Load();
        GameSettings.Apply();
        GetTree().Paused = false; // the pause card's MAIN MENU unpauses first; this is a second guard
        BuildBackdrop();

        var layer = new CanvasLayer { Name = "MenuLayer", Layer = 1 };
        AddChild(layer);
        _ui = new Control { Name = "Ui", MouseFilter = Control.MouseFilterEnum.Ignore };
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_ui);
        _overlay = new Control { Name = "Overlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_overlay);
        ShowPage(Page.Title);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _planet.RotateY((float)delta * 0.025f);
        _clouds.RotateY((float)delta * 0.008f);
        var a = (float)_time * 0.05f;
        _moon.Position = new Vector3(Mathf.Cos(a) * 4.4f, 0.9f + Mathf.Sin(a) * 0.35f, Mathf.Sin(a) * 4.4f);

        var size = GetViewport().GetVisibleRect().Size;
        var s = HudKit.ScaleFor(size);
        // Rebuild for a new window size or UI scale (not the settings page: a slider may be in a drag).
        if (_dirty || (_page != Page.Settings && (!Mathf.IsEqualApprox(s, _builtScale) || size != _builtSize))) Rebuild();
        if (_cardDirty)
        {
            _cardDirty = false;
            RefreshCard();
        }
        if (_page == Page.System) UpdatePicker();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        switch (_page)
        {
            case Page.Faction: ShowPage(Page.Title); break;
            case Page.Era: ShowPage(Page.Faction); break;
            case Page.System: ShowPage(Page.Era); break;
            case Page.Settings: ShowPage(_settingsBack); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---------------------------------------------------------------- pages

    /// <summary>Switch pages. The rebuild runs in the next _Process, never inside a button's pressed signal (the
    /// button that was pressed is freed by it).</summary>
    private void ShowPage(Page page)
    {
        var entering = page == Page.System && _page != Page.System;
        if (_page == Page.System && page != Page.System) LeavePicker();
        _page = page;
        if (entering) Roll();
        _dirty = true;
    }

    private bool _dirty;
    private bool _cardDirty;

    private void Rebuild()
    {
        _dirty = false;
        var size = GetViewport().GetVisibleRect().Size;
        _builtSize = size;
        _builtScale = _s = HudKit.ScaleFor(size);
        if (_pageRoot != null)
        {
            _ui.RemoveChild(_pageRoot);
            _pageRoot.QueueFree();
        }
        _pageRoot = _page switch
        {
            Page.Faction => BuildFactionPage(),
            Page.Era => BuildEraPage(),
            Page.System => BuildPickerPage(),
            Page.Settings => BuildSettingsPage(),
            _ => BuildTitlePage(),
        };
        _pageRoot.Name = $"Page{_page}";
        _ui.AddChild(_pageRoot);
        _ui.MoveChild(_pageRoot, _overlay.GetIndex()); // under the overlay
        if (_page == Page.System) AfterPickerBuilt();
    }

    /// <summary>A full-screen page: a dimming backdrop (more over the busy screens) and a margin box with a column inside.</summary>
    private (Control Root, VBoxContainer Column) NewPage(float dim, float margin = 48f)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (dim > 0f)
        {
            var shade = new ColorRect { Color = new Color(0.008f, 0.012f, 0.03f, dim), MouseFilter = Control.MouseFilterEnum.Ignore };
            shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(shade);
        }
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var m = Mathf.RoundToInt(margin * _s);
        pad.AddThemeConstantOverride("margin_left", m);
        pad.AddThemeConstantOverride("margin_right", m);
        pad.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(margin * 0.75f * _s));
        pad.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(margin * 0.75f * _s));
        root.AddChild(pad);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * _s));
        pad.AddChild(column);
        return (root, column);
    }

    /// <summary>Eyebrow, title and a sub line at the top of a page.</summary>
    private void Header(VBoxContainer column, string eyebrow, string title, string sub)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * _s));
        box.AddChild(HudKit.Label(eyebrow, 14 * _s, HudKit.Accent, condensed: true, bold: true));
        box.AddChild(HudKit.Label(title, 40 * _s, HudKit.Ink, condensed: true, bold: true));
        if (sub.Length > 0)
        {
            var l = HudKit.Label(sub, 15 * _s, HudKit.Dim);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(l);
        }
        column.AddChild(box);
    }

    private static Control Spacer(bool expand = true, float height = 0f) => new()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(0, height),
    };

    /// <summary>A bottom row: buttons on the left, a gap, buttons on the right.</summary>
    private HBoxContainer BottomBar(VBoxContainer column)
    {
        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * _s));
        column.AddChild(bar);
        return bar;
    }

    private Button Action(string text, bool primary, System.Action onPress, float width = 150f)
    {
        var b = HudKit.ActionButton(text, primary, _s);
        b.CustomMinimumSize = new Vector2(width * _s, 38 * _s);
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * _s));
        b.Pressed += onPress;
        return b;
    }

    // ---------------------------------------------------------------- title

    private Control BuildTitlePage()
    {
        var (root, column) = NewPage(0f, 72f);
        column.AddChild(Spacer());
        column.AddChild(HudKit.Label("A STAR TREK FAN PROTOTYPE", 15 * _s, HudKit.Accent, condensed: true, bold: true));
        column.AddChild(HudKit.Label("FINAL FRONTIER", 76 * _s, HudKit.Ink, condensed: true, bold: true));
        column.AddChild(HudKit.Label("Survey, land, build: a colony and fleet RTS", 18 * _s, HudKit.Sky, condensed: true));
        column.AddChild(Spacer(false, 26 * _s));

        var buttons = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        buttons.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        column.AddChild(buttons);
        MenuRow(buttons, "CAMPAIGN", "Start with the Federation at the dawn of warp", true, false, () => ShowPage(Page.Faction));
        MenuRow(buttons, "MULTIPLAYER", "Coming later", false, true, null);
        MenuRow(buttons, "SETTINGS", "Display, controls, tutorial hints", false, false, () =>
        {
            _settingsBack = Page.Title;
            ShowPage(Page.Settings);
        });
        MenuRow(buttons, "DEBUG", "The sandbox as before: saved world and seed, world chooser (F2), test panel (F9)", false, false, StartDebug);
        MenuRow(buttons, "QUIT", "", false, false, () => GetTree().Quit());

        column.AddChild(Spacer());
        column.AddChild(HudKit.Label("Prototype build · every number is a placeholder · fan project, not affiliated with Paramount", 12 * _s, HudKit.Dim));
        return root;
    }

    /// <summary>One title-screen button with its hint beside it; a locked one is disabled with a padlock.</summary>
    private void MenuRow(VBoxContainer parent, string text, string hint, bool primary, bool locked, System.Action? onPress)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(16 * _s));
        var b = HudKit.ActionButton(text, primary, _s);
        b.CustomMinimumSize = new Vector2(300 * _s, 46 * _s);
        b.Alignment = HorizontalAlignment.Left;
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(20 * _s));
        if (locked)
        {
            b.Disabled = true;
            b.TooltipText = hint;
            var lockIcon = new LockIcon { Colour = HudKit.Dim };
            lockIcon.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
            lockIcon.OffsetLeft = -34 * _s;
            lockIcon.OffsetRight = -16 * _s;
            lockIcon.OffsetTop = -10 * _s;
            lockIcon.OffsetBottom = 10 * _s;
            b.AddChild(lockIcon);
        }
        else if (onPress != null) b.Pressed += onPress;
        row.AddChild(b);
        if (hint.Length > 0)
        {
            var l = HudKit.Label(hint, 14 * _s, locked ? HudKit.Dim : new Color(HudKit.Dim, 0.9f), condensed: true);
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(l);
        }
        parent.AddChild(row);
    }

    private void StartDebug()
    {
        GameSetup.StartDebug();
        GetTree().ChangeSceneToFile(GameSetup.PlanetScene);
    }

    // ---------------------------------------------------------------- settings

    private Control BuildSettingsPage()
    {
        var (root, column) = NewPage(0.55f);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(centre);
        var panel = new SettingsPanel();
        panel.Build(_s, inGame: false);
        panel.Done += () => ShowPage(_settingsBack);
        centre.AddChild(panel);
        return root;
    }

    // ---------------------------------------------------------------- data

    /// <summary>The campaign's data (loaded once per menu visit). Null (with an error printed) if it can't load.</summary>
    private GameData? Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                _data = GameDataLoader.Load(new DataSetup(Era: CampaignEra, Factions: new[] { CampaignFaction }), out _, out _);
            }
            catch (System.Exception e)
            {
                GD.PushError($"Main menu: the game data didn't load: {e.Message}");
            }
            return _data;
        }
    }

    // ---------------------------------------------------------------- backdrop

    /// <summary>The 3D scene behind the menu: camera, a sun, a noise-textured planet with clouds, a rim of air and a
    /// small moon, and a sphere of stars (MultiMesh of tiny unlit spheres).</summary>
    private void BuildBackdrop()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.006f, 0.008f, 0.02f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.42f, 0.6f),
            AmbientLightEnergy = 0.12f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.05f,
        };
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });

        var camera = new Camera3D { Name = "Camera3D", Fov = 45f, Far = 2000f, Current = true };
        AddChild(camera);

        var sun = new DirectionalLight3D { Name = "Sun", LightEnergy = 1.6f, LightColor = new Color(1f, 0.96f, 0.9f) };
        AddChild(sun);
        sun.Basis = Basis.FromEuler(new Vector3(-0.35f, -1.15f, 0f)); // from the upper left, a little behind

        // The planet sits low on the right so the title column stays over empty space.
        var system = new Node3D { Name = "Backdrop", Position = new Vector3(2.6f, -1.55f, -8.5f) };
        AddChild(system);
        system.RotateZ(0.32f); // axial tilt

        _planet = new Node3D { Name = "Planet" };
        system.AddChild(_planet);
        _planet.AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = GroundMaterial(),
        });
        _clouds = new Node3D { Name = "Clouds" };
        _planet.AddChild(_clouds);
        _clouds.AddChild(new MeshInstance3D
        {
            Name = "CloudShell",
            Mesh = new SphereMesh { Radius = 2.44f, Height = 4.88f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = CloudMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        system.AddChild(new MeshInstance3D
        {
            Name = "Air",
            Mesh = new SphereMesh { Radius = 2.58f, Height = 5.16f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = RimShader } },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _moon = new MeshInstance3D
        {
            Name = "Moon",
            Mesh = new SphereMesh { Radius = 0.32f, Height = 0.64f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.62f, 0.6f, 0.58f),
                AlbedoTexture = Noise(256, 128, 0.02f, 4, new[] { 0f, 1f }, new[] { new Color(0.45f, 0.44f, 0.43f), new Color(0.85f, 0.83f, 0.8f) }),
                Roughness = 1f,
            },
        };
        system.AddChild(_moon);

        AddChild(Stars());
    }

    private static NoiseTexture2D Noise(int w, int h, float frequency, int octaves, float[] offsets, Color[] colours, bool normal = false)
    {
        var ramp = new Gradient { Offsets = offsets, Colors = colours };
        return new NoiseTexture2D
        {
            Width = w,
            Height = h,
            Seamless = true,
            AsNormalMap = normal,
            BumpStrength = 6f,
            ColorRamp = normal ? null : ramp,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = frequency, FractalOctaves = octaves, Seed = 1701 },
        };
    }

    private static StandardMaterial3D GroundMaterial()
    {
        // Deep sea → shallows → beach → green → dry → rock → snow.
        var offsets = new[] { 0f, 0.47f, 0.5f, 0.52f, 0.6f, 0.7f, 0.8f, 0.88f };
        var colours = new[]
        {
            new Color(0.02f, 0.06f, 0.18f), new Color(0.05f, 0.16f, 0.36f), new Color(0.1f, 0.28f, 0.42f), new Color(0.55f, 0.52f, 0.38f),
            new Color(0.17f, 0.33f, 0.13f), new Color(0.42f, 0.38f, 0.22f), new Color(0.4f, 0.36f, 0.33f), new Color(0.92f, 0.93f, 0.95f),
        };
        return new StandardMaterial3D
        {
            AlbedoTexture = Noise(1024, 512, 0.006f, 6, offsets, colours),
            NormalEnabled = true,
            NormalTexture = Noise(1024, 512, 0.006f, 6, offsets, colours, normal: true),
            NormalScale = 0.6f,
            Roughness = 0.75f,
            MetallicSpecular = 0.4f,
        };
    }

    private static StandardMaterial3D CloudMaterial() => new()
    {
        AlbedoTexture = Noise(1024, 512, 0.009f, 5, new[] { 0f, 0.52f, 0.72f, 1f },
            new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.75f), new Color(1f, 1f, 1f, 0.95f) }),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 1f,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// <summary>A thin glowing limb: additive fresnel on a shell a little larger than the clouds.</summary>
    private const string RimShader = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
uniform vec4 rim_colour : source_color = vec4(0.32, 0.58, 1.0, 1.0);
void fragment() {
	float f = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	f = pow(f, 3.0);
	ALBEDO = rim_colour.rgb * f * 1.3; // additive: no ALPHA needed
}
";

    private static MultiMeshInstance3D Stars()
    {
        const int count = 1600;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 3 },
        };
        mm.InstanceCount = count;
        var rng = new RandomNumberGenerator { Seed = 4242 };
        for (var i = 0; i < count; i++)
        {
            // Uniform on a sphere, a little denser in a band (a hint of the galaxy's plane).
            var z = rng.RandfRange(-1f, 1f);
            if (i % 3 == 0) z *= 0.25f;
            var t = rng.RandfRange(0f, Mathf.Tau);
            var r = Mathf.Sqrt(1f - z * z);
            var dir = new Vector3(r * Mathf.Cos(t), z, r * Mathf.Sin(t));
            dir = dir.Rotated(Vector3.Right, 0.9f);
            var size = 0.9f + Mathf.Pow(rng.Randf(), 6f) * 2.6f; // ~1-3 px at 900 units
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), dir * 900f));
            var warmth = rng.Randf();
            var tint = warmth < 0.2f ? new Color(1f, 0.82f, 0.65f) : warmth > 0.85f ? new Color(0.7f, 0.82f, 1f) : new Color(1f, 1f, 1f);
            var brightness = 0.35f + rng.Randf() * 0.65f;
            mm.SetInstanceColor(i, tint * brightness);
        }
        return new MultiMeshInstance3D
        {
            Name = "Stars",
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
            },
        };
    }
}

/// <summary>A small padlock drawn in code (no font glyph needed): a rounded body and a shackle.</summary>
public partial class LockIcon : Control
{
    public Color Colour { get; set; } = HudKit.Dim;

    public LockIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var size = Size;
        var side = Mathf.Min(size.X, size.Y);
        var o = (size - new Vector2(side, side)) / 2f;
        var w = side * 0.78f;
        var h = side * 0.52f;
        var body = new Rect2(o + new Vector2((side - w) / 2f, side - h), new Vector2(w, h));
        DrawRect(body, Colour);
        var centre = new Vector2(o.X + side / 2f, body.Position.Y);
        var radius = w * 0.32f;
        var width = Mathf.Max(1.5f, side * 0.11f);
        DrawArc(centre, radius, Mathf.Pi, Mathf.Tau, 16, Colour, width, true);
        DrawLine(centre + new Vector2(-radius, 0), centre + new Vector2(-radius, side * 0.04f), Colour, width);
        DrawLine(centre + new Vector2(radius, 0), centre + new Vector2(radius, side * 0.04f), Colour, width);
        // keyhole
        DrawCircle(new Vector2(centre.X, body.Position.Y + h * 0.42f), Mathf.Max(1f, side * 0.07f), new Color(0.02f, 0.03f, 0.07f));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }
}
