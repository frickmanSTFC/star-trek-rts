using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Planet-surface view (scenes/Planet.tscn, the main scene). The game opens in orbit over a generated
/// Class M world: read the scan hints, pick a landing site, watch the colony pod come down, drive
/// survey rovers to find deposits, then build and upgrade the base. Like SimDriver, this only
/// schedules commands; the sim decides what happens.
/// </summary>
public partial class PlanetDriver : Node3D, ITestHost
{
    [Export] public int InputDelayTicks { get; set; } = 2;

    /// <summary>Change to get a different planet; the same seed always gives the same world.</summary>
    [Export] public ulong Seed { get; set; } = 1701;

    [Export] public string PlanetName { get; set; } = "Tarsis";

    /// <summary>How close unexplored ground gets to black (0..1).</summary>
    [Export(PropertyHint.Range, "0.5,0.98,0.01")] public float FogDarkness { get; set; } = 0.92f;

    /// <summary>
    /// One colour per player slot (owner colour on models, markers, HUD swatches): Federation blue and Cardassian orange
    /// first (unchanged), then six more that stay apart from them and from each other. Use <see cref="ColorOf"/>.
    /// </summary>
    private static readonly Color[] PlayerColors =
    {
        new(0.45f, 0.7f, 1f),    // 0 blue
        new(0.95f, 0.6f, 0.25f), // 1 orange
        new(0.42f, 0.85f, 0.4f), // 2 green
        new(0.88f, 0.32f, 0.36f), // 3 red
        new(0.72f, 0.5f, 0.95f), // 4 violet
        new(0.95f, 0.88f, 0.35f), // 5 yellow
        new(0.3f, 0.86f, 0.82f), // 6 teal
        new(0.95f, 0.55f, 0.8f), // 7 pink
    };

    /// <summary>A player's colour (the palette repeats past eight players).</summary>
    public static Color ColorOf(int player) => PlayerColors[((player % PlayerColors.Length) + PlayerColors.Length) % PlayerColors.Length];

    /// <summary>The player slot this machine plays. The one place to change it: the match uses it from _Ready on
    /// (clamped to the players the match has; the planet landing has one, so 0).</summary>
    [Export] public int LocalPlayerSlot { get; set; } = 0;

    /// <summary>The player this machine plays, set once the match is set up (<see cref="LocalPlayerSlot"/>).</summary>
    public int LocalPlayer { get; private set; }

    private Simulation _sim = null!;

    /// <summary>The game's data (the sim's: the test panel can tune it while the game runs).</summary>
    private GameData _data => _sim.Data;

    // Test panel (F9, dev builds only): null in release exports.
    private TestPanel? _test;
    private System.Action<int>? _pick; // the panel asked for a map tile (teleport, place at cursor)
    private string _pickPrompt = "";
    private int _cursorTile = -1;
    private string _dataDirectory = "";
    private PlanetView _view = null!;
    private PlanetHud _hud = null!;
    private HelmScreen _helm = null!;
    private string? _selectedBody;
    private List<string> _findings = new();
    private double _findingsAt = -1;
    private (int Water, int Biomass)? _planetShares;
    private Camera3D _camera = null!;
    private double _accumulator;
    private int _hoverTile = -1;
    private bool _mouseOverUi;

    // Selection: a base (its core's structure id), a building, rovers (in the view) or a tile.
    private int _selectedBase;
    private int _selectedStructure;
    private int _selectedTile = -1;

    private string? _placing;

    // Orbit view: the globe from the ship. The game opens here; O switches to the surface and back once landed.
    private OrbitView _orbit = null!;
    private bool _inOrbit;
    private string? _orbitAction; // armed deployment: land, outpost, shuttle:<unit>, beam:<payload>, satellite
    private int _orbitHover = -1;
    private Vector2 _pressAt;
    private bool _dragging;
    private bool _pressed;
    private double _switchToSurfaceAt = -1;
    private double _clock;
    private float _surfaceAmbient = -1f;
    private (float Intensity, float Bloom, float Threshold, float Level5, float Level6)? _surfaceGlow;
    private readonly Dictionary<int, (DeliveryKind Kind, string Payload, int Tile)> _seenDeliveries = new();

    /// <summary>What the big info card shows: a building id (while placing it), "tech:id" or "unit:id"; null = closed.</summary>
    private string? _info;
    private int _facing;

    private LandingPhase _lastPhase = LandingPhase.InOrbit;
    private readonly Dictionary<int, (StructureState State, string DefId, StructureStatus Status)> _seen = new();
    private readonly HashSet<int> _lowPowerBases = new();
    private readonly Dictionary<int, (UnitMode Mode, MiningStatus Status)> _seenUnits = new();
    private bool _storageFull;
    private readonly HashSet<int> _knownUnits = new();
    private readonly Dictionary<int, (bool Broken, int Level)> _seenUpkeep = new();
    private bool[] _knownTechs = System.Array.Empty<bool>();
    private readonly Dictionary<int, (long BrokeAt, bool Broken)> _seenCondition = new();
    private bool[] _knownDeposits = System.Array.Empty<bool>();

    // Camera: focus point on the ground, distance, and yaw in 90° steps (Q / E), all eased.
    private Vector3 _focus;
    private float _distance = 420f;

    /// <summary>Camera distance that shows the whole map (worked out from the map size); also the zoom-out limit.</summary>
    private float _overview = 420f;
    private Environment? _environment;
    private DirectionalLight3D? _sun;
    private Vector3 _focusTarget;
    private float _distanceTarget = 420f;

    /// <summary>Closest zoom (a rover is ~5 units long, a hex 25). Below <see cref="TiltFrom"/> the camera tilts from the
    /// 55° strategy view down toward a 20° near-ground view so vehicles are seen from the side, not from above.</summary>
    private const float MinZoom = 15f;
    private const float TiltFrom = 160f;

    /// <summary>Ground height under the focus, eased, so close-ups look at the terrain rather than at height 0.</summary>
    private float _lookY;
    private int _yawSteps;
    private float _yaw;

    public override void _Ready()
    {
        // JSON folders + draft in debug builds, res://gamedata.pack in release exports (GameDataLoader).
        var data = GameDataLoader.Load(new DataSetup(Factions: new[] { "federation" }), out _dataDirectory, out var dataMessages);
        foreach (var problem in data.Validate()) GD.PushWarning($"Data check: {problem}");

        _sim = MatchSetup.CreatePlanetLanding(data, Seed);
        LocalPlayer = Mathf.Clamp(LocalPlayerSlot, 0, _sim.Players.Count - 1);
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNode<PlanetHud>("Hud");
        _hud.LocalColour = ColorOf(LocalPlayer);
        _view = new PlanetView { FogDarkness = FogDarkness };
        AddChild(_view);
        _view.Build(_sim, LocalPlayer);
        _knownDeposits = new bool[_sim.Surface!.Map.Grid.Count];
        _knownTechs = new bool[_data.Techs.Count];

        _hud.SetResources(_data.HudResources.Select(r => (r.Index, r.Id, r.Name)).ToList());
        // The command card holds the build menu and every Vehicle Bay order; UpdateHud shows one set at a time.
        var commands = _data.BuildMenu.Select((id, i) => (id, _data.Building(id).Name, $"[{i + 1}]")).ToList();
        foreach (var unit in _data.BuildMenu.SelectMany(id => _data.Building(id).Produces).Distinct().ToList())
        {
            var key = _data.BuildMenu.Select(id => _data.Building(id).Produces.ToList().IndexOf(unit)).FirstOrDefault(i => i >= 0);
            commands.Add(($"unit:{unit}", _data.GroundVehicleNames.GetValueOrDefault(unit, unit), $"[{key + 1}]"));
        }
        foreach (var t in _data.Techs) commands.Add(($"tech:{t.Id}", t.Name, $"[{t.Index + 1}]"));
        commands.Add((FoundCard, "Found Outpost", "[F]")); // a selected construction rover's card (PlanetDriver.Founding.cs)
        var orbitCards = OrbitCards();
        for (var i = 0; i < orbitCards.Count; i++) commands.Add((orbitCards[i].Id, orbitCards[i].Name, $"[{i + 1}]"));
        _hud.Setup(commands, OnCommand, SelectBase, Upgrade, OnAction);
        _hud.SetInfoHandlers(InfoAction, () => _info = null);
        SetupBuildMenu(); // drawer, large card, placing strip and the model renders (PlanetDriver.BuildMenu.cs)
        _hud.Minimap.Setup(_sim, _view, LocalPlayer);
        _hud.Minimap.OnJump = p => _focusTarget = p;

        var g = _sim.Surface!.Map.Grid;
        var worldW = (g.Width + 0.5f) * PlanetView.HexWidth;
        var worldD = ((g.Height - 1) * 0.866f + 1.155f) * PlanetView.HexWidth;
        _overview = Mathf.Max(worldW * 0.8f, worldD * 1.35f);
        _distance = _distanceTarget = _overview;
        _environment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        _sun = GetNodeOrNull<DirectionalLight3D>("Sun");
        _focus = _focusTarget = _view.MapCentre();
        UpdateCamera();

        _orbit = new OrbitView();
        AddChild(_orbit); // in the tree before Build: it aims its light with LookAt
        _orbit.Build(_sim, LocalPlayer, _sim.Players[LocalPlayer].FactionId);
        _orbit.CurrentBodyName = PlanetName;
        _hud.SetScanHandler(i => _orbit.SetScan((ScanMode)i));

        // The helm screen: the frame and panels drawn over the orbit view, under the HUD's layer (so F1 help and the
        // event feed still draw on top of it).
        var helmLayer = new CanvasLayer { Name = "HelmLayer", Layer = 0 };
        AddChild(helmLayer);
        _helm = new HelmScreen { Name = "Helm", Visible = false };
        helmLayer.AddChild(_helm);
        _helm.ActionPicked += OnHelmAction;
        _helm.DisplayPicked += SetOrbitDisplay;
        _helm.SetInfoHandlers(InfoAction, () => _info = null);
        SetupUplink(helmLayer); // the outpost's surface feed window, over the helm
        SetupSky(); // day/night, seasons and weather from Climate (PlanetDriver.Sky.cs); before EnterOrbit changes the environment

        // The test panel exists only in debug builds (editor runs); release exports never get it.
        if (OS.IsDebugBuild())
        {
            var testLayer = new CanvasLayer { Name = "TestLayer", Layer = 2 };
            AddChild(testLayer);
            _test = new TestPanel { Visible = false };
            testLayer.AddChild(_test);
            _test.Setup(this);
        }
        EnterOrbit();
        _orbitAction = "land";
        _info = "orbit:land";
        foreach (var m in dataMessages) _hud.PushEvent($"Data refused: {m}", new Color(1f, 0.45f, 0.4f));
    }

    // ---------------------------------------------------------------- orbit

    /// <summary>The deployment cards on the command card in orbit, in key order.</summary>
    private List<(string Id, string Name)> OrbitCards()
    {
        var list = new List<(string, string)> { ("orbit:land", "Land Colony"), ("orbit:outpost", "Outpost Drop") };
        var orbit = _data.Orbit;
        if (orbit != null)
        {
            foreach (var u in orbit.ShuttlePayloads) list.Add(($"orbit:shuttle:{u}", $"Drop Ship: {ShortUnit(u)}"));
            var payloads = orbit.TransporterLevels.SelectMany(l => l.Payloads).Distinct().ToList();
            foreach (var pl in payloads) list.Add(($"orbit:beam:{pl}", pl == OrbitRules.Supplies ? "Beam Supplies" : $"Beam: {ShortUnit(pl)}"));
            list.Add(("orbit:satellite", "Survey Satellite"));
        }
        return list;
    }

    private string ShortUnit(string unitId) => unitId switch
    {
        "survey_rover" => "Rover",
        "mining_crawler" => "Crawler",
        "survey_team" => "Team",
        _ => _data.GroundVehicleNames.GetValueOrDefault(unitId, unitId),
    };

    private static readonly Dictionary<string, string> EraNames = new()
    {
        ["ent"] = "Enterprise era", ["tos"] = "TOS era", ["kelvin"] = "Kelvin timeline", ["tng"] = "Next Generation era",
        ["dominion_war"] = "Dominion War", ["voy"] = "Delta Quadrant", ["picard"] = "Picard era", ["far_future"] = "32nd century",
    };

    private void EnterOrbit()
    {
        _inOrbit = true;
        StopPlacing();
        ClearSelection();
        _view.Visible = false;
        _orbit.Visible = true;
        _orbit.Camera.MakeCurrent();
        _helm.Visible = true;
        _hud.SetOrbitMode(true);
        if (_sun != null) _sun.Visible = false;
        if (_environment != null)
        {
            _environment.FogEnabled = false;
            _environment.SsaoEnabled = false;
            if (_surfaceAmbient < 0) _surfaceAmbient = _environment.AmbientLightEnergy;
            _environment.AmbientLightEnergy = 0.07f; // space is dark: the night side should be too
            // Glow for the orbit view: the sun glint on the sea, ship lights and the limb bloom softly, with a wide faint halo (lens glow).
            if (_surfaceGlow == null)
                _surfaceGlow = (_environment.GlowIntensity, _environment.GlowBloom, _environment.GlowHdrThreshold, _environment.GetGlowLevel(5), _environment.GetGlowLevel(6));
            _environment.GlowIntensity = 0.85f;
            _environment.GlowBloom = 0.04f;
            _environment.GlowHdrThreshold = 0.9f;
            _environment.SetGlowLevel(5, 0.6f);
            _environment.SetGlowLevel(6, 0.35f);
        }
        if (Me.Phase == LandingPhase.Landed)
        {
            var tile = _view.TileAt(_focus);
            if (tile >= 0) _orbit.Focus(tile, 230f);
        }
        _hud.Minimap.Visible = !_inOrbit || Me.Phase == LandingPhase.Landed;
    }

    /// <summary>Back down to the hex map, looking at this tile.</summary>
    private void ExitOrbit(int tile, float distance)
    {
        _inOrbit = false;
        _orbitAction = null;
        if (_info != null && _info.StartsWith("orbit:")) _info = null;
        _orbit.Visible = false;
        _view.Visible = true;
        _camera.MakeCurrent();
        _helm.Visible = false;
        _helm.ShowInfo(null);
        _hud.SetOrbitMode(false);
        if (_sun != null) _sun.Visible = true;
        if (_environment != null)
        {
            _environment.FogEnabled = true;
            _environment.SsaoEnabled = true;
            if (_surfaceAmbient >= 0) _environment.AmbientLightEnergy = _surfaceAmbient;
            if (_surfaceGlow is { } glow)
            {
                _environment.GlowIntensity = glow.Intensity;
                _environment.GlowBloom = glow.Bloom;
                _environment.GlowHdrThreshold = glow.Threshold;
                _environment.SetGlowLevel(5, glow.Level5);
                _environment.SetGlowLevel(6, glow.Level6);
            }
        }
        _hud.Minimap.Visible = true;
        if (tile >= 0)
        {
            _focus = _focusTarget = _view.TileCentre(tile) with { Y = 0 };
            _distance = _distanceTarget = distance;
        }
        _orbit.SetHover(-1, Colors.White);
    }

    /// <summary>Why the armed action can't go to this tile (null = it can).</summary>
    private string? OrbitProblem(string action, int tile)
    {
        if (tile < 0) return "pick a spot on the planet";
        if (action == "land") return Surface.LandProblem(_sim, LocalPlayer, tile); // incl. "a storm over the site …" (climate)
        if (action == "outpost") return Surface.OutpostProblem(_sim, LocalPlayer, tile);
        if (action == "satellite") return Surface.SatelliteProblem(_sim, LocalPlayer, Surface.Map.Grid.Row(tile));
        if (action.StartsWith("shuttle:")) return Surface.ShuttleProblem(_sim, LocalPlayer, tile, action[8..]);
        if (action.StartsWith("beam:")) return Surface.BeamProblem(_sim, LocalPlayer, tile, action[5..]);
        return "unknown";
    }

    /// <summary>A click on the planet: send the armed deployment there, or turn the planet to centre the tile.</summary>
    private void OrbitClick(bool shift)
    {
        if (_orbit.Display == OrbitDisplay.System)
        {
            var body = _orbit.PickBody(GetViewport().GetMousePosition());
            if (body != null)
            {
                _selectedBody = body;
                _orbit.HighlightBody(body);
            }
            return;
        }
        var tile = _orbitHover;
        if (tile < 0) return;
        if (_orbitAction == null)
        {
            _orbit.Focus(tile, Mathf.Min(260f, 330f));
            return;
        }
        var problem = OrbitProblem(_orbitAction, tile);
        if (problem != null)
        {
            _hud.PushEvent($"Can't do that there: {problem}", new Color(1f, 0.7f, 0.42f));
            return;
        }
        var when = _sim.Tick + InputDelayTicks;
        var a = _orbitAction;
        if (a == "land") _sim.Schedule(when, new LandColonyCommand(LocalPlayer, tile));
        else if (a == "outpost") _sim.Schedule(when, new OutpostDropCommand(LocalPlayer, tile));
        else if (a == "satellite") _sim.Schedule(when, new SatelliteCommand(LocalPlayer, Surface.Map.Grid.Row(tile)));
        else if (a.StartsWith("shuttle:")) _sim.Schedule(when, new ShuttleCommand(LocalPlayer, tile, a[8..]));
        else if (a.StartsWith("beam:")) _sim.Schedule(when, new BeamCommand(LocalPlayer, tile, a[5..]));
        if (!shift)
        {
            _orbitAction = null;
            _info = null;
        }
    }

    private void ArmOrbit(string cardId)
    {
        if (_orbit.Display == OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.Planet); // deployments aim at the globe
        var action = cardId[6..];
        if (_orbitAction == action && _info == cardId)
        {
            _orbitAction = null; // picking it again disarms
            _info = null;
            return;
        }
        _orbitAction = action;
        _info = cardId;
    }

    private void OrbitInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when !_mouseOverUi || !b.Pressed:
                if (b.Pressed)
                {
                    _pressed = true;
                    _dragging = false;
                    _pressAt = b.Position;
                }
                else if (_pressed)
                {
                    _pressed = false;
                    if (!_dragging) OrbitClick(b.ShiftPressed);
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                _orbitAction = null;
                if (_info != null && _info.StartsWith("orbit:")) _info = null;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _orbit.Zoom(0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _orbit.Zoom(1.1f);
                break;
            case InputEventMouseMotion m when (m.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle)) != 0:
                if (_pressed && !_dragging && m.Position.DistanceTo(_pressAt) > 6f) _dragging = true;
                if (_dragging || (m.ButtonMask & MouseButtonMask.Middle) != 0) _orbit.Rotate(m.Relative.X * 0.005f, m.Relative.Y * 0.005f);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OrbitKey(key);
                break;
        }
    }

    private void OrbitKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.O when Me.Phase == LandingPhase.Landed:
                LeaveOrbit();
                break;
            case Key.Tab when _orbit.Display != OrbitDisplay.System:
                _orbit.SetScan((ScanMode)(((int)_orbit.Mode + (key.ShiftPressed ? 4 : 1)) % 5));
                break;
            case Key.Escape:
                if (_info != null) _info = null;
                _orbitAction = null;
                break;
            case Key.Enter or Key.KpEnter when _orbitAction != null && _orbitHover >= 0:
                OrbitClick(false);
                break;
            case Key.F when Me.Phase == LandingPhase.Landed:
                ToggleFeed();
                break;
            case Key.H:
                if (Me.LandingTile < 0) break;
                if (_orbit.Display == OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.Planet);
                _orbit.Focus(Me.LandingTile, 230f);
                break;
            case Key.F1:
                _hud.ToggleHelp();
                break;
            case >= Key.Key1 and <= Key.Key9:
                var cards = OrbitCards();
                var index = (int)(key.Keycode - Key.Key1);
                if (index < cards.Count) ArmOrbit(cards[index].Id);
                break;
        }
    }

    /// <summary>Hover over the globe: which tile, and whether the armed action can go there.</summary>
    private void OrbitHover(float delta)
    {
        var mouse = GetViewport().GetMousePosition();
        _mouseOverUi = _hud.BlocksMouse(mouse) || _helm.IsOverUi(mouse) || _feed.IsOverUi(mouse);
        if (_helm.ViewRect.Size.X > 32f && _helm.ViewRect.Size.Y > 32f) _orbit.SetViewRect(_helm.ViewRect, GetViewport().GetVisibleRect().Size);
        _orbitHover = _mouseOverUi ? -1 : _orbit.PickTile(mouse);
        var colour = _orbitAction == null ? new Color(1f, 1f, 1f, 0.6f)
            : OrbitProblem(_orbitAction, _orbitHover) == null ? new Color(0.3f, 1f, 0.4f, 0.95f) : new Color(1f, 0.3f, 0.25f, 0.9f);
        _orbit.SetHover(_orbitHover, colour);

        // Keys turn the planet too.
        var turn = 1.4f * delta;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.Q)) _orbit.Rotate(-turn, 0);
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.E)) _orbit.Rotate(turn, 0);
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) _orbit.Rotate(0, -turn);
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) _orbit.Rotate(0, turn);
    }

    private static readonly string[] ScanLegends =
    {
        "True colour: oceans, plains, forest, desert, hills, mountains and polar ice.",
        "Bright cells: surveyed deposits (colour = resource). Faint tint: the region's strongest reading, stronger = richer. Grey: used up.",
        "Amber: grazing herds  ·  Red: predators (unsafe to land among)  ·  Violet: a native settlement and its land: no landing or building (Prime Directive).",
        "Green: plant biomass, brighter = denser  ·  Teal: marine life  ·  Yellow-green: toxic or contaminated ground (no landing).",
        "Green: safe landing site  ·  Yellow: risky (predators or toxic ground nearby)  ·  Red: unsafe  ·  Violet: native land  ·  Blue: too close to your bases for an outpost.",
    };

    /// <summary>What the scans say about a tile (the orbit panel and tooltip).</summary>
    private string OrbitTileText(int tile)
    {
        if (tile < 0) return "Point at the planet for scan readings.";
        var map = Surface.Map;
        var sb = new StringBuilder();
        var terrain = map.Terrain(tile);
        sb.Append(terrain.Name);
        sb.Append(map.Safety(tile) switch { LandingSafety.Safe => "  ·  safe landing site", LandingSafety.Risky => "  ·  risky landing site", _ => "" });
        if (map.NearNatives(tile)) sb.Append(map.NativeTile == tile ? "\nNative settlement: a pre-warp civilization" : "\nNative land: no landing or building (Prime Directive)");
        var wild = map.WildlifeAt[tile];
        if (wild != Wildlife.None) sb.Append(wild == Wildlife.Predators ? "\nLifeforms: predators" : "\nLifeforms: grazing herds");
        if (map.Toxic[tile]) sb.Append("\nBiologics: toxic spores, contaminated ground");
        else if (map.Biomass[tile] > Fix64.Zero) sb.Append($"\nBiomass {Mathf.RoundToInt(map.Biomass[tile].ToFloat() * 100)}%");
        var r = map.DepositResource[tile];
        if (Me.Surveyed[tile]) sb.Append(r < 0 ? "\nNo deposit" : map.IsDepleted(tile) ? $"\n{_data.Resources[r].Name}: used up" : $"\nDeposit: {_data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}");
        else
        {
            var hints = map.Hints(map.Region(tile));
            sb.Append("\nRegion scan: ").Append(hints.Count == 0 ? "no readings" : string.Join(", ", hints.Take(3).Select(h => $"{_data.Resources[h.Resource].Name} ({h.Strength.ToString().ToLowerInvariant()})")));
        }
        if (_orbitAction != null)
        {
            var problem = OrbitProblem(_orbitAction, tile);
            sb.Append(problem == null ? "\nClick to send it here" : $"\nCan't send it here: {problem}");
        }
        return sb.ToString();
    }

    /// <summary>A helm button (rail, action strip or card): scans, the grid, system bodies, deployments, back to the surface.</summary>
    private void OnHelmAction(string id)
    {
        if (id.StartsWith("scan:")) _orbit.SetScan((ScanMode)int.Parse(id[5..]));
        else if (id == "grid") _orbit.Grid = !_orbit.Grid;
        else if (id.StartsWith("body:"))
        {
            _selectedBody = id[5..];
            _orbit.HighlightBody(_selectedBody);
        }
        else if (id.StartsWith("orbit:")) ArmOrbit(id);
        else if (id == "surface") LeaveOrbit();
        else if (id == "display:planet") SetOrbitDisplay(OrbitDisplay.Planet);
        else if (id == "feed") ToggleFeed();
    }

    private void SetOrbitDisplay(OrbitDisplay display)
    {
        _orbit.SetDisplay(display);
        _helm.SetDisplay(display);
        if (display != OrbitDisplay.System) _orbit.HighlightBody(null);
        else _orbit.HighlightBody(_selectedBody);
    }

    /// <summary>Down to the hex map at whatever tile is in the middle of the viewscreen (O, or the helm's SURFACE button).</summary>
    private void LeaveOrbit()
    {
        if (Me.Phase != LandingPhase.Landed) return;
        var centre = _orbit.PickTile(_orbit.ViewCentre);
        ExitOrbit(centre >= 0 ? centre : Me.LandingTile, 320f);
    }

    private static string ShortResource(string name) =>
        string.Join(" ", name.Split(' ').Select(w => w.Length <= 3 ? w : w[..3])).ToUpperInvariant();

    private static readonly string[] Compass = { "north-west", "north", "north-east", "west", "centre", "east", "south-west", "south", "south-east" };

    /// <summary>Where a region lies on the map, roughly ("north-west").</summary>
    private string RegionPlace(int region)
    {
        var def = Surface.Map.Def;
        var col = region % def.RegionColumns;
        var row = region / def.RegionColumns;
        var cx = def.RegionColumns <= 1 ? 1 : Mathf.Clamp(col * 3 / def.RegionColumns, 0, 2);
        var cy = def.RegionRows <= 1 ? 1 : Mathf.Clamp(row * 3 / def.RegionRows, 0, 2);
        return Compass[cy * 3 + cx];
    }

    /// <summary>The helm's findings feed: what the scans know (region readings, surveyed deposits, life, hazards). Refreshed once a second.</summary>
    private IReadOnlyList<string> Findings()
    {
        if (_findingsAt >= 0 && _clock - _findingsAt < 1.0) return _findings;
        _findingsAt = _clock;
        var map = Surface.Map;
        var list = new List<string>();
        for (var r = 0; r < map.RegionCount; r++)
        {
            var hints = map.Hints(r);
            if (hints.Count == 0) continue;
            var h = hints[0];
            list.Add($"{_data.Resources[h.Resource].Name}: {h.Strength.ToString().ToLowerInvariant()} reading · {RegionPlace(r)}");
        }
        int surveyed = 0, deposits = 0, herds = 0, predators = 0, toxic = 0;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (Me.Surveyed[t])
            {
                surveyed++;
                if (map.DepositResource[t] >= 0 && !map.IsDepleted(t)) deposits++;
            }
            if (map.WildlifeAt[t] == Wildlife.Predators) predators++;
            else if (map.WildlifeAt[t] != Wildlife.None) herds++;
            if (map.Toxic[t]) toxic++;
        }
        if (map.NativeTile >= 0) list.Insert(0, $"Native settlement, pre-warp · {RegionPlace(map.Region(map.NativeTile))} · their land is off-limits");
        if (predators > 0) list.Add($"Predators on {predators} sectors: landing beside them is risky");
        if (herds > 0) list.Add($"Grazing herds on {herds} sectors");
        if (toxic > 0) list.Add($"Toxic ground on {toxic} sectors: no landing there");
        if (surveyed > 0) list.Add($"Surveyed {surveyed} sectors · {deposits} deposits found");
        list.InsertRange(0, UplinkFindings());
        _findings = list;
        return list;
    }

    /// <summary>Everything the helm screen shows while in orbit: title, rail, action strip, data panel, feed, cursor readout, info card.</summary>
    private void OrbitHud(long seconds)
    {
        var orbit = _data.Orbit;
        var faction = _sim.Players[LocalPlayer].FactionId;
        var ship = orbit?.ShipFor(faction);
        var display = _orbit.Display;
        var me = Me;
        var map = Surface.Map;
        var stock = _sim.Players[LocalPlayer].Stock;
        var mouse = GetViewport().GetMousePosition();
        _helm.SetDisplay(display);
        _helm.SetClock($"{seconds / 60:00}:{seconds % 60:00}");
        _helm.SetResources(string.Join("   ", _data.HudResources.Select(r => $"{ShortResource(r.Name)} {Hud.Whole(stock[r.Index])}")));

        var hint = me.Phase == LandingPhase.Descending ? "DROP SHIPS AWAY…"
            : _orbitAction == "land" && me.Phase == LandingPhase.InOrbit ? "CLICK A GREEN (SAFE) SITE TO LAND THE COLONY"
            : _orbitAction != null ? "CLICK THE PLANET TO CHOOSE WHERE · RIGHT-CLICK CANCELS" : null;
        var shipLine = $"{ship?.Name?.ToUpperInvariant() ?? "YOUR SHIP"}{(ship?.ClassName is { Length: > 0 } c ? $" · {c.ToUpperInvariant()} CLASS" : "")}";

        // Rail: the scans (+ grid) on the globe displays, the bodies on the system plot.
        var rail = new List<HelmAction>();
        var bodies = _orbit.SystemBodies();
        if (display == OrbitDisplay.System)
        {
            foreach (var b in bodies.Where(b => b.ParentId == null))
                rail.Add(new HelmAction($"body:{b.Id}", "", b.Name.ToUpperInvariant(), Highlight: b.Id == (_selectedBody ?? bodies.FirstOrDefault(x => x.Current)?.Id)));
        }
        else if (display != OrbitDisplay.Atmosphere)
        {
            for (var i = 0; i < PlanetHud.ScanNames.Length; i++)
                rail.Add(new HelmAction($"scan:{i}", "", PlanetHud.ScanNames[i], Highlight: (int)_orbit.Mode == i));
            rail.Add(new HelmAction("grid", "", "GRID", Colour: _orbit.Grid ? HelmScreen.Cyan : null));
        }
        _helm.SetRail(rail);

        // Panel and title per display.
        var sections = new List<HelmSection>();
        switch (display)
        {
            case OrbitDisplay.System:
            {
                var selected = bodies.FirstOrDefault(b => b.Id == _selectedBody) ?? bodies.FirstOrDefault(b => b.Current);
                _helm.SetTitle("STAR SYSTEM", hint ?? $"{bodies.Count(b => b.ParentId == null)} BODIES · {shipLine}");
                if (selected != null)
                    sections.Add(new HelmSection(selected.Name.ToUpperInvariant(), HelmScreen.Peach, new List<HelmRow>
                    {
                        new("CLASS", selected.ClassName),
                        new("SIZE", selected.Size.ToString()),
                        new("MOONS", bodies.Count(b => b.ParentId == selected.Id).ToString()),
                        new("STATUS", selected.Current ? "in orbit" : "not visited", selected.Current ? HelmScreen.Green : null),
                    }));
                sections.Add(new HelmSection("BODIES", HelmScreen.Sky, bodies.Where(b => b.ParentId == null).Select(b => new HelmRow(b.Name.ToUpperInvariant(), b.ClassName, b.Current ? HelmScreen.Peach : null)).ToList()));
                _helm.SetPanel("SYSTEM DATA", sections);
                break;
            }
            case OrbitDisplay.Orbit:
            {
                var objects = _orbit.TrackedObjects();
                _helm.SetTitle("ORBITAL TRAFFIC", hint ?? $"{objects.Count} OBJECTS TRACKED · {PlanetName.ToUpperInvariant()}");
                sections.Add(new HelmSection("OBJECTS IN ORBIT", HelmScreen.Lavender, objects.Select(o => new HelmRow(o.Name.ToUpperInvariant(), $"{o.Kind} · {o.Status}", o.Colour)).ToList()));
                _helm.SetPanel("ORBIT DATA", sections);
                break;
            }
            case OrbitDisplay.Atmosphere:
                _helm.SetTitle("ATMOSPHERE", hint ?? $"{PlanetName.ToUpperInvariant()} · {shipLine}");
                sections.Add(new HelmSection("CONDITIONS", HelmScreen.Sky, AtmosphereConditions()));
                sections.AddRange(_telemetry.Sections(_readings, UplinkStatus()));
                _helm.SetPanel("ATMOSPHERIC DATA", sections);
                break;
            default:
            {
                _planetShares ??= PlanetShares();
                var mode = (int)_orbit.Mode;
                _helm.SetTitle(PlanetName.ToUpperInvariant(), hint ?? $"CLASS M · STANDARD ORBIT · {shipLine}");
                sections.Add(new HelmSection("", HelmScreen.Cyan, new List<HelmRow>
                {
                    new("SURFACE", $"{map.Grid.Width} × {map.Grid.Height} sectors"),
                    new("OCEANS", $"{_planetShares.Value.Water} %"),
                    new("BIOMASS", $"{_planetShares.Value.Biomass} % average"),
                    new("NATIVES", map.NativeTile >= 0 ? "1 settlement" : "none found", map.NativeTile >= 0 ? HelmScreen.Peach : null),
                    new("SATELLITES", $"{Surface.Satellites.Count(x => x.Owner == LocalPlayer)} / {orbit?.SatelliteMax}"),
                }));
                sections.Add(new HelmSection($"SCAN · {PlanetHud.ScanNames[mode]}", HelmScreen.Sky, new List<HelmRow> { new("", ScanLegends[mode]) }));
                sections.Add(_telemetry.Glance(_readings, UplinkStatus())); // "awaiting surface uplink" until the outpost reports
                _helm.SetPanel("PLANETARY DATA", sections);
                break;
            }
        }
        _helm.SetFeed(Findings());

        // Cursor readout: the tile under the mouse (or the body on the system plot).
        if (display == OrbitDisplay.System)
        {
            var id = _mouseOverUi ? null : _orbit.PickBody(mouse);
            var b = id == null ? null : bodies.FirstOrDefault(x => x.Id == id);
            _helm.ShowReadout(b?.Name.ToUpperInvariant(), b == null ? "" : b.ClassName, HelmScreen.Peach, mouse + new Vector2(18, 14));
        }
        else if (_orbitHover >= 0)
        {
            var lines = OrbitTileText(_orbitHover).Split('\n');
            var problem = _orbitAction == null ? null : OrbitProblem(_orbitAction, _orbitHover);
            var colour = _orbitAction != null ? (problem == null ? HelmScreen.Green : HelmScreen.Red)
                : map.Safety(_orbitHover) == LandingSafety.Safe ? HelmScreen.Green : map.Safety(_orbitHover) == LandingSafety.Risky ? HelmScreen.Peach : HelmScreen.Sky;
            _helm.ShowReadout(lines[0].ToUpperInvariant(), string.Join("\n", lines.Skip(1)), colour, mouse + new Vector2(18, 14));
        }
        else _helm.ShowReadout(null, "", HelmScreen.Sky, Vector2.Zero);

        // Action strip: the deployments (keys 1–9), then back to the surface.
        var actions = new List<HelmAction>();
        if (display == OrbitDisplay.System) actions.Add(new HelmAction("display:planet", "", "BACK TO PLANET", Colour: HelmScreen.Peach));
        var cards = OrbitCards();
        for (var i = 0; i < cards.Count; i++)
        {
            var id = cards[i].Id;
            var a = id[6..];
            var tag = "";
            var disabled = false;
            if (a == "land") disabled = me.Phase != LandingPhase.InOrbit;
            else if (a == "satellite") tag = $" {Surface.Satellites.Count(x => x.Owner == LocalPlayer)}/{orbit?.SatelliteMax}";
            else if (me.Phase != LandingPhase.Landed) disabled = true;
            else if (a.StartsWith("shuttle:") && me.ShuttleBackTick > _sim.Tick) tag = $" {(me.ShuttleBackTick - _sim.Tick) / Simulation.TickRate}S";
            else if (a.StartsWith("beam:"))
            {
                var level = orbit?.Transporter(me.TransporterLevel);
                if (level == null || !level.Payloads.Contains(a[5..])) disabled = true;
                else if (me.TransporterReadyTick > _sim.Tick) tag = $" {(me.TransporterReadyTick - _sim.Tick) / Simulation.TickRate}S";
            }
            var label = a == "land" ? "LAND" : a == "outpost" ? "OUTPOST" : a == "satellite" ? "SATELLITE"
                : a.StartsWith("shuttle:") ? $"DROP {ShortUnit(a[8..]).ToUpperInvariant()}"
                : a == "beam:" + OrbitRules.Supplies ? "BEAM SUPPLIES" : $"BEAM {ShortUnit(a[5..]).ToUpperInvariant()}";
            actions.Add(new HelmAction(id, (i + 1).ToString(), label + tag, !disabled || _orbitAction == a, _orbitAction == a)); // greyed ones still open their card
        }
        if (me.Phase == LandingPhase.Landed) actions.Add(FeedPill());
        if (me.Phase == LandingPhase.Landed) actions.Add(new HelmAction("surface", "O", "SURFACE", Colour: HelmScreen.Rust));
        _helm.SetActions(actions);

        _helm.ShowInfo(_info != null && _info.StartsWith("orbit:") ? OrbitInfo(_info) : null);
    }

    /// <summary>Share of water tiles and average land biomass, in percent (the map doesn't change, so this is worked out once).</summary>
    private (int Water, int Biomass) PlanetShares()
    {
        var map = Surface.Map;
        int water = 0, land = 0;
        var biomass = 0f;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (map.IsWater(t)) water++;
            else
            {
                land++;
                biomass += map.Biomass[t].ToFloat();
            }
        }
        return (Mathf.RoundToInt(100f * water / Mathf.Max(1, map.Grid.Count)), Mathf.RoundToInt(100f * biomass / Mathf.Max(1, land)));
    }

    private IReadOnlyList<ResourceAmount> OrbitCost(string action)
    {
        var orbit = _data.Orbit;
        if (orbit == null) return System.Array.Empty<ResourceAmount>();
        if (action == "outpost") return orbit.OutpostCost;
        if (action == "satellite") return orbit.SatelliteCost;
        if (action.StartsWith("shuttle:")) return _data.SurfaceUnit(action[8..]).Cost;
        if (action.StartsWith("beam:") && action[5..] != OrbitRules.Supplies) return _data.SurfaceUnit(action[5..]).Cost;
        return System.Array.Empty<ResourceAmount>();
    }

    /// <summary>The info card for a deployment: what it is, what it costs and needs, and why the hovered spot won't do.</summary>
    private InfoView OrbitInfo(string cardId)
    {
        var a = cardId[6..];
        var orbit = _data.Orbit!;
        var me = Me;
        var stock = _sim.Players[LocalPlayer].Stock;
        var cost = OrbitCost(a);
        var rows = new List<(string, string)>();
        string title, body, sub;
        var icon = cardId;
        if (a == "land")
        {
            title = "Land the Colony";
            sub = me.Phase == LandingPhase.InOrbit ? "First landing" : "Landed";
            body = "Send the colony pod down. It lands where you click and becomes your Colony Hub, with two survey rovers, a mining crawler and the starting supplies. Pick a green, safe site; yellow is risky (predators or toxic ground nearby), and the natives' land is off-limits.";
            rows.Add(("Needs", "flat plains or desert with open land on every side, no hazards on it"));
            rows.Add(("Descent", $"{_data.Landing.DescentSeconds} s"));
            rows.Add(("Brings", string.Join(", ", _data.Landing.Units.GroupBy(u => u).Select(g => $"{g.Count()}× {_data.GroundVehicleNames.GetValueOrDefault(g.Key, g.Key)}")) + $", {Amounts(_data.Landing.StartStock, false)}"));
        }
        else if (a == "outpost")
        {
            title = "Outpost Drop";
            sub = "Found another base";
            body = "Drop a second Colony Hub from the ship to found a new base, with its own build range, power grid and crew. It needs a safe site well away from your other bases, so it's the way to reach rich ground on the far side of the world.";
            rows.Add(("Needs", $"a safe landing site at least {orbit.OutpostMinDistance} tiles from your other bases"));
            rows.Add(("Descent", $"{orbit.OutpostDescentSeconds} s, one drop at a time"));
        }
        else if (a == "satellite")
        {
            title = "Survey Satellite";
            sub = $"{Surface.Satellites.Count(x => x.Owner == LocalPlayer)} of {orbit.SatelliteMax} in orbit";
            body = $"Put a survey satellite into orbit over a band of the planet. It circles the world every {orbit.SatelliteSweepSeconds} s, surveying every tile it passes, so deposits in its band show up without a rover. Click the latitude you want it to cover.";
            rows.Add(("Covers", $"{orbit.SatelliteBandRows} rows of tiles, all the way round"));
            rows.Add(("Works", "from orbit, even before you land"));
        }
        else if (a.StartsWith("shuttle:"))
        {
            var u = a[8..];
            var name = _data.GroundVehicleNames.GetValueOrDefault(u, u);
            title = $"Drop Ship: {name}";
            sub = me.ShuttleBackTick > _sim.Tick ? $"Drop ship away: back in {(me.ShuttleBackTick - _sim.Tick) / Simulation.TickRate} s" : "Drop ship ready";
            body = $"The drop ship flies a new {name} down from the ship to any safe spot on the planet, even far from your bases. {_data.SurfaceUnit(u).Description}";
            rows.Add(("Flight", $"{orbit.ShuttleSeconds} s down, {orbit.ShuttleSeconds} s back up before it can fly again"));
            rows.Add(("Needs", "open ground: no buildings, toxic ground, predators or native land"));
        }
        else
        {
            var payload = a[5..];
            var level = orbit.Transporter(me.TransporterLevel);
            var faction = _sim.Players[LocalPlayer].FactionId;
            var era = EraNames.GetValueOrDefault(orbit.Era, orbit.Era);
            title = payload == OrbitRules.Supplies ? "Beam Supplies" : $"Beam: {_data.GroundVehicleNames.GetValueOrDefault(payload, payload)}";
            sub = level == null ? $"No transporters ({era})" : $"{level.Name} (level {level.Level}: {_data.Faction(faction).Name}, {era})";
            body = payload == OrbitRules.Supplies
                ? "Beam cargo from the ship's hold straight into one of your bases: the quickest way to top up metals and fuel early on."
                : $"Beam a survey team down in seconds. {_data.SurfaceUnit(payload).Description}";
            if (level != null)
            {
                rows.Add(("Range", level.Range == 0 ? "anywhere on the planet" : $"within {level.Range} tiles of your bases or people"));
                if (payload == OrbitRules.Supplies)
                    rows.Add(("Cargo", $"up to {Hud.Whole(level.Cargo)} a beam · hold: {string.Join(", ", me.Hold.Select((x, i) => (x, i)).Where(p => p.x > Fix64.Zero).Select(p => $"{Hud.Whole(p.x)} {_data.Resources[p.i].Name}"))}"));
                rows.Add(("Cycle", $"{level.CooldownSeconds} s between beams"));
                if (!level.Payloads.Contains(payload)) rows.Add(("Can't", $"the {level.Name.ToLowerInvariant()} can't beam this"));
            }
            rows.Add(("Level", "set by faction and era: early transporters (Enterprise era) beam people only, later ones cargo and anywhere"));
        }
        var hover = OrbitProblem(a, _orbitHover);
        var footer = _orbitAction == a
            ? (_orbitHover < 0 ? "Click the planet to choose where  ·  right-click / Esc cancel" : hover == null ? "Click to send it here  ·  Shift+click to keep going" : $"Can't send it here: {hover}")
            : "Pick this card again to cancel";
        return new InfoView(icon, title, sub, cost.Count == 0 ? (a == "land" ? "Cost  free" : "Cost  none (from the hold)") : $"Cost  {Amounts(cost, false)}",
            stock.CanAfford(cost), body, rows, -1f, "", "", false, footer);
    }

    private SurfacePlayer Me => _sim.Surface!.Players[LocalPlayer];

    // ---------------------------------------------------------------- test panel host

    Simulation ITestHost.Sim => _sim;
    PlanetView ITestHost.View => _view;
    int ITestHost.LocalPlayer => LocalPlayer;
    int ITestHost.CursorTile => _cursorTile;
    string ITestHost.DataDirectory => _dataDirectory;
    string ITestHost.UnitName(SurfaceUnit u) => UnitName(u);
    string ITestHost.BaseName(int baseId) => BaseName(baseId);

    void ITestHost.Notify(string text, bool warn) => _hud.PushEvent(text, warn ? new Color(1f, 0.7f, 0.42f) : new Color(0.62f, 0.82f, 1f));

    void ITestHost.BeginPick(string prompt, System.Action<int> onTile)
    {
        if (_placing != null) StopPlacing();
        _pick = onTile;
        _pickPrompt = prompt;
    }

    /// <summary>What the test panel edits: the selected building, else the first selected vehicle, else the selected base's core.</summary>
    (SurfaceUnit? Unit, SurfaceStructure? Structure, int Count) ITestHost.Selection()
    {
        if (_selectedStructure != 0 && Surface.FindStructure(_selectedStructure) is { } s) return (null, s, 1);
        var units = _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null).ToList();
        if (units.Count > 0) return (units[0], null, units.Count);
        if (_selectedBase != 0 && Surface.FindStructure(_selectedBase) is { } core) return (null, core, 1);
        return (null, null, 0);
    }
    private PlanetSurface Surface => _sim.Surface!;

    public override void _Process(double delta)
    {
        // Game speed and pause come from the test panel (render-side pacing; the sim just steps).
        var speed = _test?.Speed ?? 1.0;
        if (!(_test?.Paused ?? false)) _accumulator += delta * speed;
        var tickSeconds = 1.0 / Simulation.TickRate;
        var steps = 0;
        var maxSteps = Mathf.Max(5, Mathf.CeilToInt(5 * speed));
        while (_accumulator >= tickSeconds && steps < maxSteps)
        {
            _sim.Step();
            _accumulator -= tickSeconds;
            steps++;
        }
        if (steps == maxSteps) _accumulator = 0;
        for (var owed = _test?.TakeSteps() ?? 0; owed > 0; owed--) _sim.Step(); // step one tick, skip a minute
        if (_test != null)
        {
            if (_inOrbit && _test.Visible) _test.Visible = false; // the panel works on the surface
            _view.SelectedUnits.RemoveWhere(id => Surface.FindUnit(id) == null); // removed by the panel
            _hud.SetTestMode(_sim.DebugEdits > 0 || _test.LocalEdits);
        }
        if (_inOrbit) _pick = null;

        OnPhaseChange();
        DetectEvents();
        if (_selectedStructure != 0 && Surface.FindStructure(_selectedStructure) == null) _selectedStructure = 0;
        if (Me.Phase == LandingPhase.Landed && Surface.FindStructure(_selectedBase) == null)
            _selectedBase = Surface.Bases(LocalPlayer).FirstOrDefault()?.Id ?? 0;

        _clock += delta;
        if (_switchToSurfaceAt > 0 && _clock >= _switchToSurfaceAt)
        {
            _switchToSurfaceAt = -1;
            ExitOrbit(Me.LandingTile, 260f);
        }
        var alpha = (float)(_accumulator / tickSeconds);
        if (_inOrbit)
        {
            OrbitHover((float)delta);
            _orbit.Refresh((float)delta, alpha);
        }
        else
        {
            MoveCamera((float)delta);
            UpdateHover();
        }
        _view.Refresh(alpha);
        UpdateSky((float)delta); // PlanetDriver.Sky.cs: before the uplink, which hands the feed the outpost's sky
        UpdateUplink((float)delta);
        if (!_inOrbit) _hud.Minimap.Refresh(_camera);
        UpdateHud();
    }

    private void OnPhaseChange()
    {
        if (Me.Phase == _lastPhase) return;
        _lastPhase = Me.Phase;
        if (Me.Phase == LandingPhase.Descending)
        {
            FlyTo(Me.LandingTile, 170f); // close enough to watch the three drop ships and the outpost assemble
            // From orbit: swoop in on the landing site, then cut to the surface as the pod comes down.
            if (_inOrbit)
            {
                SetOrbitDisplay(OrbitDisplay.Planet);
                _orbit.Focus(Me.LandingTile, 140f);
                _switchToSurfaceAt = _clock + 1.6;
            }
        }
        if (Me.Phase == LandingPhase.Landed)
        {
            _view.SelectedUnits.UnionWith(Surface.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
            _selectedBase = Surface.Bases(LocalPlayer).FirstOrDefault()?.Id ?? 0;
            _hud.PushEvent($"Colony founded on {PlanetName}", ColorOf(LocalPlayer));
        }
    }

    /// <summary>Turns state changes into event-feed lines: deposits found, buildings finished, upgrades, power and upkeep trouble.</summary>
    private void DetectEvents()
    {
        var map = Surface.Map;
        var found = new List<int>();
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (_knownDeposits[t] || !Me.Surveyed[t] || map.DepositResource[t] < 0) continue;
            _knownDeposits[t] = true;
            found.Add(t);
        }
        if (found.Count is > 0 and <= 2)
            foreach (var t in found)
            {
                var r = _data.Resources[map.DepositResource[t]];
                _hud.PushEvent($"Found {r.Name} {Hud.Whole(map.DepositAmount[t])}", Icons.ResourceColors.GetValueOrDefault(r.Id, Colors.White));
            }
        else if (found.Count > 2) _hud.PushEvent($"Survey found {found.Count} deposits", new Color(1f, 0.54f, 0.85f));

        foreach (var s in Surface.Structures.Where(s => s.Owner == LocalPlayer))
        {
            var now = (s.State, s.DefId, s.Status);
            if (_seen.TryGetValue(s.Id, out var was) && was != now)
            {
                var name = _data.Building(s.DefId).Name;
                // The first landing's hub finishes on the uplink tick; its "assembled" line is folded into the uplink event.
                var firstHub = was.State == StructureState.Assembling && s.Tile == Me.LandingTile;
                if (was.State != StructureState.Complete && s.State == StructureState.Complete && !firstHub && !s.Founded) // founded: FounderEvents
                    _hud.PushEvent(was.State == StructureState.Assembling ? $"{name} assembled: {BaseName(s.BaseId)} is working" : $"{name} completed", new Color(0.62f, 0.82f, 1f));
                if (was.DefId != s.DefId) _hud.PushEvent($"{BaseName(s.BaseId)} upgraded: {name} (Level {_data.Building(s.DefId).Level})", PlanetHud.Accent);
                if (was.Status != StructureStatus.NoUpkeep && s.Status == StructureStatus.NoUpkeep) _hud.PushEvent($"{name} idle: can't pay upkeep", new Color(1f, 0.7f, 0.42f));
                if (was.Status != StructureStatus.Depleted && s.Status == StructureStatus.Depleted) _hud.PushEvent($"{name}: deposit used up", new Color(1f, 0.7f, 0.42f));
                if (was.Status != StructureStatus.Paused && s.Status == StructureStatus.Paused) _hud.PushEvent($"{name} paused (P to resume)", new Color(0.6f, 0.65f, 0.75f));
                if (was.Status == StructureStatus.Paused && s.Status != StructureStatus.Paused && s.State == StructureState.Complete) _hud.PushEvent($"{name} back to work", new Color(0.62f, 0.82f, 1f));
            }
            _seen[s.Id] = now;
        }

        var warn = new Color(1f, 0.7f, 0.42f);
        foreach (var u in Surface.Units.Where(u => u.Owner == LocalPlayer && _data.SurfaceUnit(u.DefId).CanMine))
        {
            var now = (u.Mode, u.MiningStatus);
            if (_seenUnits.TryGetValue(u.Id, out var was) && was != now)
            {
                var name = UnitName(u);
                if (was.Mode != UnitMode.Deployed && u.Mode == UnitMode.Deployed && Surface.Map.DepositResource[u.Tile] >= 0)
                    _hud.PushEvent($"{name} unfurled: mining {_data.Resources[Surface.Map.DepositResource[u.Tile]].Name}", new Color(0.62f, 0.82f, 1f));
                if (was.Status != u.MiningStatus)
                {
                    if (u.MiningStatus == MiningStatus.StorageFull) _hud.PushEvent($"{name} waiting: storage full — build a Storage Container or Ore Refinery", warn);
                    if (u.MiningStatus == MiningStatus.NoDestination) _hud.PushEvent($"{name} waiting: nowhere to send ore", warn);
                    if (u.MiningStatus == MiningStatus.Depleted) _hud.PushEvent($"{name}: deposit used up, folding up", warn);
                }
            }
            _seenUnits[u.Id] = now;

            var condition = (u.BrokeAtTick, u.Broken);
            if (_seenCondition.TryGetValue(u.Id, out var before) && before != condition)
            {
                var name = UnitName(u);
                var rules = _data.SurfaceUnit(u.DefId).Breakdown;
                if (u.BrokeAtTick != before.BrokeAt && rules != null && u.Breakdown >= 0)
                {
                    var sev = rules.Severities[u.Breakdown];
                    var text = sev.LosesLoad
                        ? $"{name}: {sev.Name.ToLowerInvariant()}! Its load is lost ({Mathf.FloorToInt(u.Integrity.ToFloat())}% integrity) — send it to the Colony Hub for repair (F)"
                        : $"{name}: {sev.Name.ToLowerInvariant()} ({Mathf.FloorToInt(u.Integrity.ToFloat())}% integrity) — send it to the Colony Hub for repair (F)";
                    _hud.PushEvent(text, u.Breakdown >= 2 ? new Color(1f, 0.42f, 0.32f) : warn);
                }
                if (before.Broken && !u.Broken) _hud.PushEvent($"{name} repaired and ready to mine", new Color(0.56f, 0.83f, 0.69f));
            }
            _seenCondition[u.Id] = condition;
        }
        RoverEvents(); // hazards, flat tyres, disabled, repaired (PlanetDriver.RoverDamage.cs)
        FounderEvents(); // construction rovers founding outposts (PlanetDriver.Founding.cs)

        foreach (var u in Surface.Units.Where(u => u.Owner == LocalPlayer))
        {
            if (!_knownUnits.Add(u.Id) || u.BuiltAtTick < 0) continue;
            var bay = Surface.StructureAt(u.BuiltAtTile);
            _hud.PushEvent($"{UnitName(u)} rolled out of the {(bay != null ? _data.Building(bay.DefId).Name : "Vehicle Bay")}", new Color(0.62f, 0.82f, 1f));
        }

        if (Me.Phase == LandingPhase.Landed)
        {
            var full = Surface.FreeStorage(_sim, LocalPlayer) <= Fix64.Zero;
            if (full && !_storageFull) _hud.PushEvent("Storage full: mining has stopped — build a Storage Container or refine ore", warn);
            _storageFull = full;
        }

        foreach (var s in Surface.Structures.Where(s => s.Owner == LocalPlayer))
        {
            var now = (s.Broken, s.Level);
            if (_seenUpkeep.TryGetValue(s.Id, out var was) && was != now)
            {
                var name = _data.Building(s.DefId).Name;
                if (!was.Broken && s.Broken)
                    _hud.PushEvent(Surface.AutoMaintained(s) ? $"{name} broke down: drones are on it" : $"{name} broke down — select it and press M to send the crew", new Color(1f, 0.42f, 0.36f));
                if (was.Broken && !s.Broken) _hud.PushEvent($"{name} repaired", new Color(0.56f, 0.83f, 0.69f));
                if (s.Level > was.Level) _hud.PushEvent($"{name} upgraded to {Surface.LevelName(s)}", PlanetHud.Accent);
            }
            _seenUpkeep[s.Id] = now;
        }
        var live = new HashSet<int>();
        foreach (var d in Surface.Deliveries.Where(d => d.Owner == LocalPlayer))
        {
            live.Add(d.Id);
            _seenDeliveries[d.Id] = (d.Kind, d.Payload, d.Tile);
        }
        foreach (var id in _seenDeliveries.Keys.Where(id => !live.Contains(id)).ToList())
        {
            var (kind, payload, tile) = _seenDeliveries[id];
            _seenDeliveries.Remove(id);
            var what = _data.GroundVehicleNames.GetValueOrDefault(payload, payload);
            var landedBase = Surface.StructureAt(tile);
            _hud.PushEvent(kind switch
            {
                DeliveryKind.Outpost => landedBase != null ? $"Outpost founded: {BaseName(landedBase.Id)}" : "Outpost drop aborted: the site was taken (refunded)",
                DeliveryKind.Shuttle => $"Drop ship landed a {what}",
                _ => payload == OrbitRules.Supplies ? "Supplies beamed down" : $"{what} beamed down",
            }, new Color(0.62f, 0.82f, 1f));
        }

        var me = Surface.Players[LocalPlayer];
        for (var t = 0; t < me.Known.Length; t++)
        {
            if (me.Known[t] && !_knownTechs[t]) _hud.PushEvent($"Research complete: {_data.Techs[t].Name} — {_data.Techs[t].Effect}", new Color(0.55f, 0.88f, 1f));
            _knownTechs[t] = me.Known[t];
        }

        foreach (var b in Surface.Bases(LocalPlayer))
        {
            var low = Surface.PowerOf(b.Id).Efficiency < Fix64.One;
            if (low && _lowPowerBases.Add(b.Id)) _hud.PushEvent($"Low power at {BaseName(b.Id)}: buildings slowed", new Color(1f, 0.7f, 0.42f));
            if (!low) _lowPowerBases.Remove(b.Id);
        }
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent @event)
    {
        // Typing in a test-panel field: no game hotkeys.
        if (@event is InputEventKey && GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (@event is InputEventMouseButton { Pressed: true }) GetViewport().GuiReleaseFocus(); // clicking the map ends typing
        if (_test != null && @event is InputEventKey { Pressed: true, Echo: false } testKey)
        {
            if (testKey.Keycode == Key.F9)
            {
                if (_inOrbit) _hud.PushEvent("The test panel works on the surface: press O first", new Color(1f, 0.7f, 0.42f));
                else _test.Visible = !_test.Visible;
                return;
            }
            if (testKey.Keycode == Key.Z && (testKey.CtrlPressed || testKey.MetaPressed))
            {
                _test.Undo();
                return;
            }
            if (testKey.Keycode == Key.Space && _test.Visible && !_inOrbit)
            {
                _test.TogglePause();
                return;
            }
        }
        if (_pick != null && !_inOrbit)
        {
            switch (@event)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                    if (_hoverTile >= 0 && !_mouseOverUi)
                    {
                        var pick = _pick;
                        _pick = null;
                        pick(_hoverTile);
                    }
                    return;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    _pick = null;
                    _hud.PushEvent("Test panel: cancelled", new Color(0.62f, 0.82f, 1f));
                    return;
            }
        }
        if (_inOrbit)
        {
            OrbitInput(@event);
            return;
        }
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.O } && Me.Phase == LandingPhase.Landed)
        {
            EnterOrbit();
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                LeftClick(click.ShiftPressed);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                if (_placing != null) StopPlacing();
                else if (_founding != 0) StopFounding();
                else RightClick();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distanceTarget = Mathf.Max(MinZoom, _distanceTarget * 0.88f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distanceTarget = Mathf.Min(_overview * 1.15f, _distanceTarget * 1.12f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Middle) != 0:
                // Drag the map with the middle mouse button.
                var forward = new Vector3(0, 0, -1).Rotated(Vector3.Up, _yaw);
                var right = new Vector3(1, 0, 0).Rotated(Vector3.Up, _yaw);
                var scale = _distance * 0.0016f;
                _focusTarget -= (right * motion.Relative.X - forward * motion.Relative.Y) * scale;
                _focus = _focusTarget;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key);
                break;
        }
    }

    private void OnKey(InputEventKey key)
    {
        var landed = Me.Phase == LandingPhase.Landed;
        switch (key.Keycode)
        {
            case Key.Q:
                _yawSteps++;
                break;
            case Key.E:
                _yawSteps--;
                break;
            case Key.R when _placing != null:
                _facing = (_facing + (key.ShiftPressed ? 5 : 1)) % 6;
                break;
            case Key.R when landed && _view.SelectedUnits.Count > 0:
                RepairSelected();
                break;
            case Key.T when landed && _view.SelectedUnits.Count > 0:
                ChangeTyres();
                break;
            case Key.Escape:
                if (_placing != null) StopPlacing();
                else if (_founding != 0) StopFounding();
                else if (_info != null) _info = null; // close the card first, keep the selection
                else if (_drawer.IsOpen) CloseDrawer();
                else ClearSelection();
                break;
            case Key.B when landed:
                ToggleDrawer(); // the build drawer (PlanetDriver.BuildMenu.cs)
                break;
            case Key.I when _placing != null:
                ReopenCard();
                break;
            case Key.Enter or Key.KpEnter when _info != null:
                InfoAction();
                break;
            case Key.Delete or Key.X:
                var target = _selectedStructure != 0 ? _selectedStructure : _selectedBase;
                var picked = Surface.FindStructure(target);
                if (picked == null) break;
                // A finished, non-core building that isn't upgrading is salvaged; anything unfinished, or an upgrade, is cancelled.
                var upgrading = picked.UpgradingTo != null || picked.UpgradingLevel > 0;
                OnAction(picked.State == StructureState.Complete && !_data.Building(picked.DefId).IsCore && !upgrading ? "salvage" : "cancel");
                break;
            case Key.M when landed && _selectedStructure != 0:
                OnAction("maintain");
                break;
            case Key.P when landed && _selectedStructure != 0:
                OnAction("pause");
                break;
            case Key.F1:
                _hud.ToggleHelp();
                break;
            case Key.F when landed:
                if (!FoundKey()) ToggleDeploy(); // a construction rover picks a site; crawlers unfurl / pack up
                break;
            case Key.V when landed && _view.SelectedUnits.Count > 0:
                var wipers = _view.CycleWipers(_view.SelectedUnits);
                _hud.PushEvent(wipers switch
                {
                    null => "The selected vehicles have no wipers",
                    RoverLook.WiperMode.On => "Wipers on",
                    RoverLook.WiperMode.Off => "Wipers off",
                    _ => "Wipers on automatic: they run on wet, muddy, snowy or dusty ground",
                }, new Color(0.62f, 0.82f, 1f));
                break;
            case Key.L when landed && _view.SelectedUnits.Count > 0:
                var headlights = _view.CycleHeadlights(_view.SelectedUnits);
                _hud.PushEvent(headlights switch
                {
                    null => "The selected vehicles have no headlights",
                    VehicleLights.Mode.On => "Headlights on",
                    VehicleLights.Mode.Off => "Headlights off",
                    _ => "Headlights on automatic: on at dusk and night, in fog, storms and dust",
                }, new Color(0.62f, 0.82f, 1f));
                break;
            case Key.U when landed:
                Upgrade();
                break;
            case Key.Tab when landed:
                var bases = Surface.Bases(LocalPlayer);
                if (bases.Count == 0) break;
                var i = bases.ToList().FindIndex(b => b.Id == _selectedBase);
                SelectBase(bases[(i + 1) % bases.Count].Id);
                break;
            case Key.A when landed && _placing == null:
                ClearSelection();
                _view.SelectedUnits.UnionWith(Surface.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
                break;
            case Key.H:
                if (Me.LandingTile >= 0) FlyTo(Me.LandingTile, 260f);
                else
                {
                    _focusTarget = _view.MapCentre();
                    _distanceTarget = _overview;
                }
                break;
            case >= Key.Key1 and <= Key.Key9 when landed:
                var index = (int)(key.Keycode - Key.Key1);
                if (BuildingKeysActive) PickBuildingByKey(index, key.ShiftPressed); // drawer, building card or placing
                else if (SelectedFounder() != null)
                {
                    if (index == 0) OnCommand(FoundCard);
                }
                else if (SelectedBay() is { } orderAt)
                {
                    var produces = _data.Building(orderAt.DefId).Produces;
                    if (index < produces.Count) OnCommand($"unit:{produces[index]}");
                }
                else if (SelectedLab() != null)
                {
                    if (index < _data.Techs.Count) OnCommand($"tech:{_data.Techs[index].Id}");
                }
                else PickBuildingByKey(index, key.ShiftPressed);
                break;
            case Key.Backspace when landed && SelectedBay() != null:
                _sim.Schedule(_sim.Tick + InputDelayTicks, new CancelUnitCommand(LocalPlayer, SelectedBay()!.Id));
                break;
        }
    }

    private IEnumerable<SurfaceUnit> SelectedCrawlers() =>
        _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null && _data.SurfaceUnit(u.DefId).CanMine).Select(u => u!);

    /// <summary>F: broken crawlers head for repair; folded ones unfurl where they stand; deployed ones pack up.</summary>
    private void ToggleDeploy()
    {
        var crawlers = SelectedCrawlers().ToList();
        if (crawlers.Count == 0) return;
        var broken = crawlers.Where(u => u.Broken).ToList();
        if (broken.Count > 0)
        {
            if (Surface.NearestRepairPad(LocalPlayer, broken[0].Tile) == null) _hud.PushEvent("No colony core to repair at", new Color(1f, 0.7f, 0.42f));
            else _sim.Schedule(_sim.Tick + InputDelayTicks, new RepairCommand(LocalPlayer, broken.Select(u => u.Id).ToList()));
            crawlers = crawlers.Except(broken).ToList();
            if (crawlers.Count == 0) return;
        }
        var pack = crawlers.Where(u => u.Mode is UnitMode.Deployed or UnitMode.Deploying).Select(u => u.Id).ToList();
        if (pack.Count > 0)
        {
            _sim.Schedule(_sim.Tick + InputDelayTicks, new PackUpCommand(LocalPlayer, pack));
            return;
        }
        foreach (var u in crawlers)
        {
            var problem = Surface.MineProblem(LocalPlayer, u.Id, u.Tile);
            if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new MineCommand(LocalPlayer, u.Id, u.Tile));
            else _hud.PushEvent($"{UnitName(u)} can't unfurl here: {problem}", new Color(1f, 0.7f, 0.42f));
        }
    }

    private string BreakdownName(SurfaceUnit u)
    {
        var rules = _data.SurfaceUnit(u.DefId).Breakdown;
        return rules != null && u.Breakdown >= 0 ? rules.Severities[u.Breakdown].Name.ToLowerInvariant() : "fault";
    }

    /// <summary>A unit's type and own name, e.g. "Mk II Surveyor Talus 17".</summary>
    private string UnitName(SurfaceUnit u) =>
        string.IsNullOrEmpty(u.Name) ? TypeName(u) : $"{TypeName(u)} {CallSign(u)}";

    private string TypeName(SurfaceUnit u) => _data.GroundVehicleNames.GetValueOrDefault(u.DefId, u.DefId);

    /// <summary>The name painted on the hull, in title case for text ("TALUS" + "17" → "Talus 17").</summary>
    private static string CallSign(SurfaceUnit u)
    {
        var words = u.Name.Split(' ');
        for (var i = 0; i < words.Length; i++)
            if (words[i].Length > 1 && words[i].Any(c => c != 'I')) words[i] = words[i][0] + words[i][1..].ToLowerInvariant();
        return $"{string.Join(' ', words)} {u.Number}";
    }

    /// <summary>The selected building if it's one of our finished Vehicle Bays (its orders replace the build menu).</summary>
    private SurfaceStructure? SelectedBay()
    {
        var s = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        return s != null && s.Owner == LocalPlayer && s.State == StructureState.Complete && _data.Building(s.DefId).IsFactory ? s : null;
    }

    /// <summary>The selected building if it's one of our finished Research Labs (its techs replace the build menu).</summary>
    private SurfaceStructure? SelectedLab()
    {
        var s = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        return s != null && s.Owner == LocalPlayer && s.State == StructureState.Complete && _data.Building(s.DefId).IsLab ? s : null;
    }

    /// <summary>
    /// A command-card button (or its number key). A building, tech or vehicle opens its card, and picking the same one
    /// again (or the card's button, or Enter) places the building / starts the research / places the order; Shift+pick
    /// places a building at once (PlanetDriver.BuildMenu.cs).
    /// </summary>
    private void OnCommand(string id)
    {
        if (id.StartsWith("orbit:"))
        {
            ArmOrbit(id);
            return;
        }
        if (id == FoundCard)
        {
            ArmFounding();
            return;
        }
        if (id.StartsWith("unit:") || id.StartsWith("tech:"))
        {
            if (_info == id) InfoAction();
            else _info = id;
            return;
        }
        PickBuilding(id, Input.IsKeyPressed(Key.Shift));
    }

    /// <summary>The info card's button: start the tech, order the vehicle or place the building it shows.</summary>
    private void InfoAction()
    {
        if (_info == null) return;
        if (_info.StartsWith("tech:")) Research(_info[5..]);
        else if (_info.StartsWith("unit:")) OrderUnit(_info[5..]);
        else if (IsBuildingCard(_info)) ArmPlacement(_info, quiet: false);
    }

    private void Research(string techId)
    {
        var problem = Surface.ResearchProblem(LocalPlayer, techId);
        if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new ResearchCommand(LocalPlayer, techId));
        else _hud.PushEvent($"Can't research {_data.Tech(techId).Name}: {problem}", new Color(1f, 0.7f, 0.42f));
    }

    /// <summary>A building-panel button (or its key): upgrade, cancel, maintain, salvage the selected building.</summary>
    private void OnAction(string id)
    {
        if (RoverAction(id)) return; // the rover panel's REPAIR / CHANGE TYRE
        var s = Surface.FindStructure(_selectedStructure != 0 ? _selectedStructure : _selectedBase);
        if (s == null) return;
        var warn = new Color(1f, 0.7f, 0.42f);
        var name = _data.Building(s.DefId).Name;
        switch (id)
        {
            case "upgrade":
                var problem = Surface.UpgradeProblem(_sim, LocalPlayer, s.Id);
                if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new UpgradeCommand(LocalPlayer, s.Id));
                else _hud.PushEvent($"Can't upgrade the {name}: {problem}", warn);
                break;
            case "cancel":
                if (s.Assembling) _hud.PushEvent($"{name}: {PlanetSurface.AssemblingProblem}", warn); // came down from orbit: nothing to cancel
                else _sim.Schedule(_sim.Tick + InputDelayTicks, new CancelBuildingCommand(LocalPlayer, s.Id));
                break;
            case "maintain":
                var why = s.MaintenanceOrdered ? null : Surface.MaintenanceProblem(LocalPlayer, s.Id);
                if (why == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new MaintainCommand(LocalPlayer, s.Id));
                else _hud.PushEvent($"{name}: {why}", warn);
                break;
            case "salvage":
                _sim.Schedule(_sim.Tick + InputDelayTicks, new SalvageCommand(LocalPlayer, s.Id));
                break;
            case "pause":
                var pauseWhy = Surface.PauseProblem(LocalPlayer, s.Id, !s.Paused);
                if (pauseWhy == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new PauseCommand(LocalPlayer, s.Id, !s.Paused));
                else _hud.PushEvent($"{name}: {pauseWhy}", warn);
                break;
        }
    }

    private void OrderUnit(string unitId)
    {
        var bay = SelectedBay();
        if (bay == null) return;
        var problem = Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, unitId);
        if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new BuildUnitCommand(LocalPlayer, bay.Id, unitId));
        else _hud.PushEvent($"Can't order a {_data.GroundVehicleNames.GetValueOrDefault(unitId, unitId)}: {problem}", new Color(1f, 0.7f, 0.42f));
    }

    private void ClearSelection()
    {
        _info = null;
        _view.SelectedUnits.Clear();
        _selectedTile = -1;
        _selectedStructure = 0;
    }

    private void SelectBase(int baseId)
    {
        ClearSelection();
        _selectedBase = baseId;
        var core = Surface.FindStructure(baseId);
        if (core != null) FlyTo(core.Tile, Mathf.Min(_distanceTarget, 360f));
    }

    /// <summary>U or the base panel's button: upgrade the selected building if it has levels, otherwise the selected base.</summary>
    private void Upgrade()
    {
        var s = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        if (s != null && _data.Building(s.DefId).Levels.Count > 0) OnAction("upgrade");
        else if (_selectedBase != 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new UpgradeCommand(LocalPlayer, _selectedBase));
    }

    private void StartPlacing(string buildingId)
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (_placing == buildingId)
        {
            StopPlacing(); // pressing the same button again turns it off
            return;
        }
        StopFounding();
        _placing = buildingId;
        _view.SelectedUnits.Clear();
        _selectedStructure = 0;
        var core = Surface.FindStructure(_selectedBase);
        if (core != null) _view.ShowBuildRange(core.Tile, _data.Building(core.DefId).BuildRadius);
    }

    private void StopPlacing()
    {
        if (_info == _placing) _info = null;
        _placing = null;
        _view.HideBuildRange();
    }

    private void LeftClick(bool shift)
    {
        if (_hoverTile < 0 || _mouseOverUi) return;

        if (Me.Phase == LandingPhase.InOrbit)
        {
            if (Surface.CanLand(LocalPlayer, _hoverTile))
                _sim.Schedule(_sim.Tick + InputDelayTicks, new LandColonyCommand(LocalPlayer, _hoverTile));
            return;
        }

        if (_founding != 0)
        {
            FoundClick(shift);
            return;
        }

        if (_placing != null)
        {
            if (Surface.CheckPlacement(_sim, LocalPlayer, _placing, _hoverTile) == PlacementProblem.None)
            {
                _sim.Schedule(_sim.Tick + InputDelayTicks, new PlaceBuildingCommand(LocalPlayer, _placing, _hoverTile, _facing));
                NoteRecent(_placing); // the command card's recent tiles
                if (!shift) StopPlacing(); // Shift keeps placing the same building
            }
            return;
        }

        var unit = _view.UnitOnTile(_hoverTile, LocalPlayer); // the one nearest the mouse: several park on a tile
        if (unit != null)
        {
            if (!shift) ClearSelection();
            _view.SelectedUnits.Add(unit.Id);
            return;
        }

        ClearSelection();
        var structure = Surface.StructureAt(_hoverTile);
        if (structure != null && structure.Owner == LocalPlayer)
        {
            if (structure.Id == structure.BaseId) _selectedBase = structure.Id; // clicking a core selects its base
            else _selectedStructure = structure.Id;
            return;
        }
        _selectedTile = _hoverTile;
    }

    private void RightClick()
    {
        if (_hoverTile < 0 || _mouseOverUi || _view.SelectedUnits.Count == 0 || Me.Phase != LandingPhase.Landed) return;
        var ids = _view.SelectedUnits.OrderBy(id => id).ToArray();
        // Say why nothing happens when the target can't be reached (the sim would quietly ignore it).
        var map = Surface.Map;
        if (!map.Terrain(_hoverTile).Passable)
        {
            _hud.PushEvent($"Rovers can't go there: {map.Terrain(_hoverTile).Name.ToLowerInvariant()} are impassable", new Color(1f, 0.7f, 0.42f));
            return;
        }
        var reachable = ids.Select(Surface.FindUnit).Where(u => u != null).Count(u => Pathfinder.Find(map, u!.Tile, _hoverTile) != null);
        if (reachable == 0)
        {
            _hud.PushEvent("No route there: mountains or water are in the way", new Color(1f, 0.7f, 0.42f));
            return;
        }
        // Crawlers sent to a surveyed deposit unfurl and mine it when they get there; everything else drives.
        var drivers = new List<int>();
        var deposit = Me.Surveyed[_hoverTile] && map.DepositResource[_hoverTile] >= 0;
        foreach (var id in ids)
        {
            var u = Surface.FindUnit(id);
            if (u == null) continue;
            if (deposit && _data.SurfaceUnit(u.DefId).CanMine)
            {
                var problem = Surface.MineProblem(LocalPlayer, id, _hoverTile);
                if (problem == null)
                {
                    _sim.Schedule(_sim.Tick + InputDelayTicks, new MineCommand(LocalPlayer, id, _hoverTile));
                    continue;
                }
                _hud.PushEvent($"{UnitName(u)} can't mine there: {problem}", new Color(1f, 0.7f, 0.42f));
            }
            if (Surface.DriveProblem(u) is { } stuck)
            {
                _hud.PushEvent($"{UnitName(u)} can't drive: {stuck}", new Color(1f, 0.7f, 0.42f));
                continue;
            }
            drivers.Add(id);
        }
        if (drivers.Count > 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveUnitsCommand(LocalPlayer, drivers, _hoverTile));
    }

    private void UpdateHover()
    {
        var mouse = GetViewport().GetMousePosition();
        _mouseOverUi = _hud.BlocksMouse(mouse) || (_test?.BlocksMouse(mouse) ?? false) || BuildMenuBlocksMouse(mouse);
        _hoverTile = -1;
        if (!_mouseOverUi)
        {
            _hoverTile = _view.PickTile(_camera.ProjectRayOrigin(mouse), _camera.ProjectRayNormal(mouse));
            if (_hoverTile >= 0) _cursorTile = _hoverTile;
        }

        if (_founding != 0)
        {
            FoundingHover();
            if (_founding != 0) return;
        }
        if (_placing != null)
        {
            var ok = _hoverTile >= 0 && Surface.CheckPlacement(_sim, LocalPlayer, _placing, _hoverTile) == PlacementProblem.None;
            _view.SetGhost(_placing, _hoverTile, _facing, ok);
            _view.SetHover(-1, Colors.White);
            return;
        }
        _view.SetGhost(null, -1, 0, false);

        var colour = new Color(1, 1, 1, 0.3f);
        if (_hoverTile >= 0 && Me.Phase == LandingPhase.InOrbit)
            colour = Surface.LandProblem(_sim, LocalPlayer, _hoverTile) == null ? new Color(0.3f, 1f, 0.4f, 0.55f) : new Color(1f, 0.3f, 0.25f, 0.45f);
        // Thin up close, wider from high up so it still reads: ~0.1 units at the closest zoom, 0.5 at 100, capped at 2.
        _view.SetHover(Me.Phase == LandingPhase.Descending ? -1 : _hoverTile, colour, Mathf.Clamp(_distance * 0.005f, 0.08f, 2f));
    }

    // ---------------------------------------------------------------- HUD

    private string BaseName(int baseId)
    {
        var bases = Surface.Bases(LocalPlayer);
        var i = bases.ToList().FindIndex(b => b.Id == baseId);
        return i <= 0 ? $"{PlanetName} Colony" : $"Base {i + 1}";
    }

    private void UpdateHud()
    {
        // The card closes when what it belongs to goes away (the lab or bay is no longer selected, placing stopped).
        if (!_inOrbit)
        {
            if (_info != null && _info.StartsWith("orbit:")) _info = null; // orbit cards belong to the orbit view
            if (_info != null && (_info == FoundCard ? _founding == 0 : _info.StartsWith("tech:") ? SelectedLab() == null : _info.StartsWith("unit:") ? SelectedBay() == null : !IsBuildingCard(_info))) _info = null;
            // Founding and research keep the compact card; buildings and vehicle orders get the large card (UpdateBuildMenu).
            _hud.ShowInfo(_info == FoundCard ? FoundInfo() : _info != null && _info.StartsWith("tech:") ? TechInfo(_data.Tech(_info[5..])) : null);
        }

        var supply = 0f;
        var use = 0f;
        foreach (var b in Surface.Bases(LocalPlayer))
        {
            var p = Surface.PowerOf(b.Id);
            supply += p.Supply.ToFloat();
            use += p.Use.ToFloat();
        }
        var seconds = _sim.Tick / Simulation.TickRate;
        var stored = (Surface.Stored(_sim, LocalPlayer) + Surface.InFlight(LocalPlayer)).ToFloat();
        var (research, researchSub) = ResearchChip();
        var capacity = Surface.StorageCapacity(LocalPlayer).ToFloat();
        if (capacity <= 0f && Surface.Structures.Any(s => s.Owner == LocalPlayer && s.Assembling)) capacity = -1f; // hidden while the outpost assembles
        _hud.ShowTop(_sim, LocalPlayer, $"{PlanetName.ToUpperInvariant()} · CLASS M", use, supply, stored,
            capacity, research, researchSub, $"{seconds / 60:00}:{seconds % 60:00}");

        var landed = Me.Phase == LandingPhase.Landed;
        _hud.ShowHints(ContextHints());
        UpdateBuildMenu(); // drawer, large card, placing strip (PlanetDriver.BuildMenu.cs)
        if (_inOrbit)
        {
            _hud.ShowBanner(Me.Phase == LandingPhase.InOrbit && _orbitAction == "land"
                ? $"Orbiting {PlanetName}. Click a green (safe) site to land the colony  ·  drag to turn the planet, wheel to zoom, Tab to change scan"
                : Me.Phase == LandingPhase.Descending ? "Drop ships away…" : "");
            OrbitHud(seconds);
            return;
        }

        _hud.ShowBanner(Me.Phase switch
        {
            LandingPhase.InOrbit => $"In orbit over {PlanetName}. Choose a landing site: plains or desert with open land on every side (green).",
            LandingPhase.Descending => $"Three drop ships descending… touchdown in {(Me.LandsAtTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate} s",
            _ when _pick != null => $"{_pickPrompt}  ·  right-click / Esc cancel",
            _ when _placing != null => $"Placing {_data.Building(_placing).Name}  ·  R rotate  ·  Shift+click place more  ·  right-click / Esc stop",
            _ when _founding != 0 => $"Founding an outpost: click a green site at least {(Surface.FindUnit(_founding) is { } fr ? Surface.FoundRulesOf(fr)!.MinDistance : 0)} tiles from your bases (outside the red ring)  ·  right-click / Esc stop",
            _ => "",
        });

        // Tooltip: what's under the mouse (or why the building can't go there).
        _hud.ShowTooltip(_hoverTile < 0 ? "" : _placing != null ? DescribePlacement(_placing, _hoverTile) : _founding != 0 ? FoundTooltip(_hoverTile) : DescribeTile(_hoverTile), GetViewport().GetMousePosition());
        if (BuildTooltip() is { } buildTip) _hud.ShowTooltip(buildTip, GetViewport().GetMousePosition()); // a drawer / recent tile under the mouse

        // Bottom centre.
        var bases = Surface.Bases(LocalPlayer);
        _hud.ShowBaseTabs(bases.Select(b => (b.Id, BaseName(b.Id))).ToList(), _selectedBase);
        if (Me.Phase == LandingPhase.InOrbit) _hud.ShowDetail("Orbital scan", $"{PlanetName} · Class M", "The scan shows terrain and, per region, which resources are likely below.\nHover a tile for details; green tiles are landing sites. Once down, survey rovers find the exact deposits.");
        else if (Me.Phase == LandingPhase.Descending) _hud.ShowDetail("Landing convoy", "Descending", "Three drop ships carry the Colony Hub in three modules (it assembles on the ground and its uplink to the ship comes online), two survey rovers, a mining crawler and starting supplies.");
        else if (_selectedStructure != 0) ShowStructure(Surface.FindStructure(_selectedStructure)!);
        else if (_view.SelectedUnits.Count > 0) ShowUnits();
        else if (_selectedTile >= 0) _hud.ShowDetail(Surface.Map.Terrain(_selectedTile).Name, "Tile", DescribeTile(_selectedTile));
        else if (_selectedBase != 0) ShowBase(Surface.FindStructure(_selectedBase)!);

        var stock = _sim.Players[LocalPlayer].Stock;
        if (SelectedLab() != null)
        {
            var me = Surface.Players[LocalPlayer];
            _hud.ShowCommands($"RESEARCH · {Mathf.RoundToInt(Surface.ResearchPerMinute(LocalPlayer).ToFloat())} POINTS A MINUTE",
                _data.Techs.Select(t =>
                {
                    var known = me.Known[t.Index];
                    var locked = !known && Surface.ResearchProblem(LocalPlayer, t.Id) != null;
                    var active = me.ActiveTech == t.Index;
                    var done = me.ResearchProgress[t.Index].ToFloat() / t.Cost.ToFloat();
                    var tag = known ? "DONE" : active ? $"{Mathf.FloorToInt(done * 100)}%" : locked ? "LOCKED" : done > 0 ? $"{Mathf.FloorToInt(done * 100)}%" : "";
                    return new CommandView($"tech:{t.Id}", "", "", $"{Hud.Whole(t.Cost)} RP", !locked, active || _info == $"tech:{t.Id}", tag, false);
                }).ToList(), true);
            return;
        }
        if (SelectedFounder() is { } founder)
        {
            ShowFoundCommands(founder); // PlanetDriver.Founding.cs
            return;
        }
        if (SelectedBay() is { } bay)
        {
            var bayDef = _data.Building(bay.DefId);
            _hud.ShowCommands($"{bayDef.Name.ToUpperInvariant()} · ORDERS ({bay.Production.Count}/{bayDef.ProductionQueue})",
                bayDef.Produces.Select(u =>
                {
                    var ud = _data.SurfaceUnit(u);
                    var problem = Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, u);
                    return new CommandView($"unit:{u}", "", "", Amounts(ud.Cost, false, shortNames: true), problem == null, _info == $"unit:{u}");
                }).ToList(), true);
            ShowOrderPictures(bayDef); // vehicle renders on the order tiles
            return;
        }
        // Nothing can be placed until a base has assembled (the first hub, or an outpost dropped from orbit).
        var readyBase = Surface.Bases(LocalPlayer).Any(b => b.State == StructureState.Complete);
        var selectedCore = Surface.FindStructure(_selectedBase);
        var buildTitle = !landed ? "BUILD (after landing)"
            : selectedCore is { Assembling: true } ? $"BUILD · {BaseName(_selectedBase).ToUpperInvariant()} ASSEMBLING {PlanetSurface.AssembleSecondsLeft(selectedCore, _sim.Tick)} S"
            : selectedCore is { Founded: true } && selectedCore.State != StructureState.Complete ? $"BUILD · {BaseName(_selectedBase).ToUpperInvariant()} UNDER CONSTRUCTION {Mathf.RoundToInt(Surface.BuildFraction(selectedCore).ToFloat() * 100)}%"
            : $"BUILD AT {BaseName(_selectedBase).ToUpperInvariant()}";
        ShowBuildCommands(buildTitle, landed && readyBase); // BUILD [B] + the recent builds (PlanetDriver.BuildMenu.cs)
    }

    private void ShowBase(SurfaceStructure core)
    {
        var def = _data.Building(core.DefId);
        var power = Surface.PowerOf(core.Id);
        var members = Surface.StructuresOf(core.Id).Where(s => s.Id != core.Id).ToList();
        var built = members.Where(s => s.State == StructureState.Complete)
            .GroupBy(s => _data.Building(s.DefId).Name)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key).ToList();
        var buildings = (built.Count == 0 ? "No buildings yet" : string.Join("  ·  ", built)) + $"  ·  build radius {def.BuildRadius}";
        if (power.Efficiency < Fix64.One)
            buildings = $"Low power: buildings run at {Mathf.RoundToInt(power.Efficiency.ToFloat() * 100)}% — build a Fusion Reactor\n" + buildings;

        string line, cost = "";
        var enabled = false;
        var progress = -1f;
        if (core.UpgradingTo != null)
        {
            var next = _data.Building(core.UpgradingTo);
            line = $"Upgrading to {next.Name} (Level {next.Level}): {next.BuildQueues} build queues, radius {next.BuildRadius}";
            progress = Surface.UpgradeFraction(core).ToFloat();
        }
        else if (def.UpgradesTo.Length > 0)
        {
            var next = _data.Building(def.UpgradesTo);
            line = $"Next: {next.Name} (Level {next.Level})\n{next.BuildQueues} build queues · radius {next.BuildRadius} · +{Hud.Whole(next.PowerSupply - def.PowerSupply)} power";
            cost = Amounts(next.Cost, false, shortNames: true);
            var problem = Surface.UpgradeProblem(_sim, LocalPlayer, core.Id);
            enabled = problem == null;
            if (core.Assembling) // came down from orbit and isn't working yet: the button stays disabled
                line = $"Assembling… {PlanetSurface.AssembleSecondsLeft(core, _sim.Tick)} s: no power, storage, build range or repair pad yet\nNext: {next.Name} (Level {next.Level}) once it is done";
            else if (problem != null && problem != "not enough resources") line += $"\n({problem})";
        }
        else line = "Top level";
        if (FoundedBaseLine(core) is { } founding) // founded on the ground: the exocomps are still building it
        {
            line = founding;
            cost = "";
            enabled = false;
            progress = Surface.BuildFraction(core).ToFloat();
        }

        var queue = members.Where(s => s.State != StructureState.Complete)
            .Select(s => (_data.Building(s.DefId).Name, s.State == StructureState.Building ? Surface.BuildFraction(s).ToFloat() : -1f)).ToList();

        _hud.ShowBase(new BaseView(BaseName(core.Id), def.Name, core.DefId, def.Level, line, cost, enabled, progress,
            power.Use.ToFloat(), power.Supply.ToFloat(), buildings, def.BuildQueues, queue));
    }

    // ---------------------------------------------------------------- info cards

    private string BuildingName(string id) => _data.Building(id).Name;

    private string ModifierText(Modifier m) => m.Stat switch
    {
        Modifier.AutoMaintain => "maintenance drones",
        Modifier.Breakdown => $"breakdowns {Pct(m.Add)}",
        _ => $"{m.Stat} {Pct(m.Add)}",
    };

    /// <summary>Card for a tech: what it does, what it needs and unlocks, how long it takes, and a Start button.</summary>
    private InfoView TechInfo(TechDef t)
    {
        var me = Surface.Players[LocalPlayer];
        var known = me.Known[t.Index];
        var problem = Surface.ResearchProblem(LocalPlayer, t.Id);
        var active = me.ActiveTech == t.Index;
        var progress = me.ResearchProgress[t.Index];
        var rate = Surface.ResearchPerMinute(LocalPlayer).ToFloat();
        var labs = Surface.Structures.Count(s => s.Owner == LocalPlayer && s.State == StructureState.Complete && _data.Building(s.DefId).IsLab);

        var rows = new List<(string, string)> { ("Effect", t.Effect) };
        var affects = new List<string>();
        foreach (var m in t.Modifiers)
        {
            var what = m.Units.Count > 0 ? string.Join(", ", m.Units.Select(u => _data.GroundVehicleNames.GetValueOrDefault(u, u)))
                : m.Buildings.Count > 0 ? string.Join(", ", m.Buildings.Select(BuildingName)) : "every building";
            affects.Add($"{what}: {ModifierText(m)}");
        }
        if (affects.Count > 0) rows.Add(("Changes", string.Join("\n", affects)));
        var unlocks = _data.BuildMenu.Where(id => _data.Building(id).RequiresTech == t.Id).Select(BuildingName).ToList();
        if (unlocks.Count > 0) rows.Add(("Unlocks", string.Join(", ", unlocks)));
        rows.Add(("Needs", t.Requires.Count == 0 ? "nothing" : string.Join(", ", t.Requires.Select(r => _data.Tech(r).Name + (me.Known[_data.Tech(r).Index] ? " (done)" : " (not yet)")))));
        var neededBy = _data.Techs.Where(o => o.Requires.Contains(t.Id)).Select(o => o.Name).ToList();
        if (neededBy.Count > 0) rows.Add(("Leads to", string.Join(", ", neededBy)));

        var left = (t.Cost - progress).ToFloat();
        var time = rate > 0 ? Mathf.CeilToInt(left / rate * 60f) : -1;
        var cost = $"Cost  {Hud.Whole(t.Cost)} research points   ·   " +
            (known ? "researched" : labs == 0 ? "build a Research Lab first" : time >= 0 ? $"~{time / 60}:{time % 60:00} at {rate:0.#} points a minute" : "your labs are idle");
        var status = known ? "Researched" : active ? "Researching now" : problem != null ? $"Locked: {problem}" : progress > Fix64.Zero ? "Started: progress is kept" : "Available";

        string action;
        var enabled = false;
        if (known) action = "RESEARCHED";
        else if (problem != null) action = "LOCKED";
        else if (active) action = "RESEARCHING…";
        else
        {
            enabled = true;
            action = me.ActiveTech >= 0 ? $"SWITCH RESEARCH TO THIS [Enter]   (progress on {_data.Techs[me.ActiveTech].Name} is kept)" : "START RESEARCH [Enter]";
        }
        return new InfoView("tech", t.Name, status, cost, known || problem == null,
            t.Description.Length > 0 ? t.Description : t.Effect, rows,
            progress > Fix64.Zero || active ? (progress / t.Cost).ToFloat() : -1f,
            $"{Hud.Whole(progress)} / {Hud.Whole(t.Cost)} points", action, enabled, "");
    }

    /// <summary>Short hints for what's selected (the full list is on F1).</summary>
    private string ContextHints()
    {
        if (_inOrbit) return (Me.Phase == LandingPhase.Landed ? "O surface  ·  " : "") + "Drag / WASD turn  ·  wheel zoom  ·  Tab scan  ·  1–9 deploy  ·  right-click cancel  ·  F1 controls";
        if (Me.Phase == LandingPhase.Descending) return "F1 controls";
        if (_placing != null) return "R rotate  ·  Shift+click place more  ·  I card  ·  right-click / Esc stop";
        if (IsBuildingCard(_info)) return "Enter or pick it again: place  ·  another key: its card  ·  B drawer  ·  Esc close";
        if (_info != null && _info.StartsWith("unit:")) return "Enter or pick it again: order  ·  Backspace cancel the last order  ·  Esc close";
        if (_drawer.IsOpen) return $"1–{Mathf.Min(9, MenuBuildings().Count)} building card  ·  Shift+pick place at once  ·  B / Esc close  ·  F1 controls";
        if (_founding != 0) return "Click a site  ·  Shift+click keep picking  ·  right-click / Esc stop  ·  F1 controls";
        if (SelectedFounder() != null) return "F / 1 found outpost (pick a site)  ·  right-click drive  ·  R repair  ·  T change tyre  ·  V wipers  ·  L lights  ·  Esc back  ·  F1 controls";
        if (SelectedBay() != null) return $"1–{_data.Building(SelectedBay()!.DefId).Produces.Count} vehicle info (again to order)  ·  Backspace cancel order  ·  U upgrade  ·  M maintain  ·  P pause  ·  X salvage  ·  Esc back  ·  F1 controls";
        if (SelectedLab() != null) return $"1–{_data.Techs.Count} tech info (again to start)  ·  M maintain  ·  P pause  ·  X salvage  ·  Esc back  ·  F1 controls";
        if (_selectedStructure != 0) return "U upgrade  ·  M maintain  ·  P pause  ·  X cancel / salvage  ·  Esc back  ·  F1 controls";
        if (_view.SelectedUnits.Count > 0) return "Right-click drive / mine  ·  F unfurl / pack up / repair  ·  R repair  ·  T change tyre  ·  V wipers  ·  L lights  ·  Esc back  ·  F1 controls";
        return $"B build  ·  1–{Mathf.Min(9, MenuBuildings().Count)} building card (Shift: place at once)  ·  U upgrade base  ·  Tab next base  ·  H home  ·  F1 controls";
    }

    /// <summary>Top-bar research chip: the active tech's progress, or what to do next.</summary>
    private (string, string) ResearchChip()
    {
        var me = Surface.Players[LocalPlayer];
        if (me.ActiveTech >= 0)
        {
            var t = _data.Techs[me.ActiveTech];
            return ($"{Mathf.FloorToInt((me.ResearchProgress[t.Index] / t.Cost).ToFloat() * 100)}%", t.Name.ToUpperInvariant());
        }
        var hasLab = Surface.Structures.Any(s => s.Owner == LocalPlayer && s.State == StructureState.Complete && _data.Building(s.DefId).IsLab);
        var known = me.Known.Count(k => k);
        return ($"{known}/{_data.Techs.Count}", hasLab ? "PICK A TECH" : "BUILD A LAB");
    }

    private void ShowUnits()
    {
        var units = _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null).Select(u => u!).ToList();
        if (units.Count == 1 && _data.SurfaceUnit(units[0].DefId).CanMine)
        {
            ShowCrawler(units[0]);
            return;
        }
        if (units.Count == 1 && units[0].Owner == LocalPlayer && IsRover(units[0]))
        {
            ShowRover(units[0]);
            return;
        }
        var rovers = units.Count(u => !_data.SurfaceUnit(u.DefId).CanMine);
        var crawlers = units.Count - rovers;
        var title = units.Count == 1 ? UnitName(units[0]) : string.Join(" + ", new[]
        {
            rovers > 0 ? $"{rovers} survey rover{(rovers == 1 ? "" : "s")}" : "",
            crawlers > 0 ? $"{crawlers} mining crawler{(crawlers == 1 ? "" : "s")}" : "",
        }.Where(t => t.Length > 0));
        var names = units.Count > 1 ? string.Join(", ", units.Select(u => string.IsNullOrEmpty(u.Name) ? TypeName(u) : CallSign(u))) + "\n" : "";
        _hud.ShowDetail(title, "Selected", names +
            "Right-click a tile to drive there. Rovers survey the ground around them as they go: deposits they find appear on the map." +
            (crawlers > 0 ? "\nRight-click a deposit to send the crawler to mine it." : "") +
            "\nEsc or click a base tab to go back to the base.");
    }

    private void ShowCrawler(SurfaceUnit u)
    {
        var def = _data.SurfaceUnit(u.DefId);
        var map = Surface.Map;
        var depositName = map.DepositResource[u.Tile] >= 0 ? _data.Resources[map.DepositResource[u.Tile]].Name : "";
        var repairing = Surface.IsRepairing(u);
        var status = u.Broken && repairing
            ? u.RepairWaiting ? "Repair waiting: not enough resources"
                : Surface.CrewOut(u) < Fix64.One ? "Repair crew on the way" : $"Repairing: {Mathf.FloorToInt(u.Integrity.ToFloat())}%"
            : u.Broken && u.Path.Count > 0 ? "Broken down: driving to the repair pad"
            : u.Broken ? $"Broken down: {BreakdownName(u)}"
            : u.Mode switch
        {
            UnitMode.Deploying => $"Unfurling {Mathf.RoundToInt(Surface.DeployFraction(u).ToFloat() * 100)}%",
            UnitMode.Packing => $"Packing up {Mathf.RoundToInt((1f - Surface.DeployFraction(u).ToFloat()) * 100)}%",
            UnitMode.Deployed => u.MiningStatus switch
            {
                MiningStatus.StorageFull => "Waiting: storage full",
                MiningStatus.NoDestination => "Waiting: no storage to send to",
                _ => $"Mining {depositName}",
            },
            _ when u.Path.Count > 0 => u.DeployOnArrival ? "Driving to a deposit" : "Driving",
            _ when u.MiningStatus == MiningStatus.Depleted => "Idle: deposit used up",
            _ => "Folded up, idle",
        };
        var sb = new StringBuilder();
        sb.Append("Integrity ").Append(Mathf.FloorToInt(u.Integrity.ToFloat())).Append('%');
        if (u.Broken)
        {
            var cost = Surface.RepairCost(u);
            sb.Append("  ·  repair costs ").Append(cost.Count == 0 ? "nothing" : Amounts(cost, false));
            sb.Append("  ·  drives at ").Append(Mathf.RoundToInt(Surface.SpeedPerTick(u).ToFloat() / u.SpeedPerTick.ToFloat() * 100)).Append("% speed");
        }
        sb.AppendLine();
        var load = u.HopperResource >= 0 ? $" {_data.Resources[u.HopperResource].Name}" : "";
        sb.Append("Hopper ").Append(Hud.Whole(u.Hopper)).Append(" / ").Append(Hud.Whole(def.Hopper)).Append(load);
        if (u.Mode != UnitMode.Mobile && depositName.Length > 0)
            sb.Append("  ·  deposit ").Append(Hud.Whole(map.DepositAmount[u.Tile])).Append(" left  ·  +").Append(Hud.Whole(def.ExtractPerMinute)).Append("/min");
        sb.AppendLine();
        var depot = Surface.NearestDepot(LocalPlayer, u.Tile);
        if (depot == null) sb.AppendLine("No storage building to send loads to");
        else
        {
            var tiles = map.Grid.Distance(u.Tile, depot.Tile);
            var flight = Mathf.Max(1, Mathf.CeilToInt(tiles / def.PodSpeed.ToFloat()));
            sb.Append("Loads fly to ").Append(_data.Building(depot.DefId).Name).Append(" (").Append(tiles).Append(" tiles, ~").Append(flight).AppendLine(" s by cargo pod)");
        }
        if (u.Broken) sb.Append(repairing ? "The repair crew fix it on the pad beside the Colony Hub" : "F: drive to the Colony Hub's repair pad (or right-click the hub)");
        else sb.Append("Right-click a deposit: drive there and unfurl  ·  F ").Append(u.Mode == UnitMode.Mobile ? "unfurl here" : "pack up");
        _hud.ShowDetail(UnitName(u), status, sb.ToString());
    }

    private static string Pct(Fix64 share) => $"{(share >= Fix64.Zero ? "+" : "−")}{Mathf.RoundToInt(Mathf.Abs(share.ToFloat()) * 100)}%";

    /// <summary>Where a stat's bonus comes from, e.g. "Mk II +30% · research +20%" ("base" when there's none).</summary>
    private string Sources(SurfaceStructure s, string stat)
    {
        var parts = new List<string>();
        var level = Surface.LevelBonus(s, stat);
        var research = Surface.ResearchBonus(s.Owner, s.DefId, stat);
        if (level != Fix64.Zero) parts.Add($"{Surface.LevelName(s)} {Pct(level)}");
        if (research != Fix64.Zero) parts.Add($"research {Pct(research)}");
        return parts.Count == 0 ? "base" : string.Join(" · ", parts);
    }

    private string Names(IReadOnlyList<ResourceAmount> amounts) => string.Join(" + ", amounts.Select(a => _data.Resources[a.Resource].Name));

    /// <summary>The selected building: what it does with live numbers and where its bonuses come from, its condition and buttons.</summary>
    private void ShowStructure(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        var power = Surface.PowerOf(s.BaseId);
        var stock = _sim.Players[LocalPlayer].Stock;
        var complete = s.State == StructureState.Complete;
        var working = complete && s.Status is StructureStatus.Working or StructureStatus.LowPower;
        var red = new Color(1f, 0.42f, 0.36f);
        var (status, statusColour) = s.State switch
        {
            StructureState.Assembling => ($"Assembling… {PlanetSurface.AssembleSecondsLeft(s, _sim.Tick)} s", new Color(0.62f, 0.82f, 1f)),
            StructureState.Queued => ("Queued: waits for a free build queue", PlanetHud.Accent),
            StructureState.Building => ($"Under construction", new Color(0.62f, 0.82f, 1f)),
            _ => s.Status switch
            {
                StructureStatus.Broken => (s.AutoRepairing ? "Broken down: drones repairing" : "Broken down: press M to send the crew", red),
                StructureStatus.Paused => ("Paused: no input, no power (P resumes)", new Color(0.6f, 0.65f, 0.75f)),
                StructureStatus.NoUpkeep => ("Idle: can't pay upkeep", new Color(1f, 0.7f, 0.42f)),
                StructureStatus.Depleted => ("Idle: deposit used up", new Color(1f, 0.7f, 0.42f)),
                StructureStatus.LowPower => ($"Low power: running at {Mathf.RoundToInt(power.Efficiency.ToFloat() * 100)}%", PlanetHud.Accent),
                StructureStatus.StorageFull => ("Idle: storage full", new Color(1f, 0.7f, 0.42f)),
                StructureStatus.NoInput when def.IsFactory => ("Idle: no orders (pick a vehicle on the right)", new Color(0.6f, 0.65f, 0.75f)),
                StructureStatus.NoInput when def.IsLab => ("Idle: pick a tech on the right", new Color(0.6f, 0.65f, 0.75f)),
                StructureStatus.NoInput => (def.Recipes.Count > 0 && def.Recipes[0].In.Any(a => _data.Resources[a.Resource].Tier == "raw") ? "Idle: no ore in storage" : "Idle: nothing to work on", new Color(0.6f, 0.65f, 0.75f)),
                _ => ("Working", new Color(0.56f, 0.83f, 0.69f)),
            },
        };

        // How fast it's working right now, and why.
        var speed = Surface.SpeedMultiplier(s);
        var conditionShare = Surface.ConditionFactor(s);
        var powerShare = def.PowerUse > Fix64.Zero ? power.Efficiency : Fix64.One;
        var now = working ? speed * conditionShare * powerShare : Fix64.Zero;
        string SpeedRow()
        {
            var parts = new List<string> { Sources(s, Modifier.Speed) };
            if (conditionShare < Fix64.One) parts.Add($"wear {Mathf.RoundToInt(conditionShare.ToFloat() * 100)}%");
            if (powerShare < Fix64.One) parts.Add($"power {Mathf.RoundToInt(powerShare.ToFloat() * 100)}%");
            return $"{Mathf.RoundToInt(speed.ToFloat() * 100)}%  ({string.Join(" · ", parts)})";
        }

        var rows = new List<(string, string)>();
        if (def.IsConverter)
        {
            var yield = Surface.YieldMultiplier(s);
            var runnable = def.Recipes.Count(r => r.In.All(a => stock[a.Resource] > Fix64.Zero));
            for (var i = 0; i < def.Recipes.Count && rows.Count < 2; i++)
            {
                var r = def.Recipes[i];
                var active = working && r.In.All(a => stock[a.Resource] > Fix64.Zero);
                var cycles = active && runnable > 0 ? (def.CyclesPerMinute * now / Fix64.FromInt(runnable)).ToFloat() : 0f;
                var line = $"{Names(r.In)} → {Names(r.Out)}";
                line += active
                    ? $"   {Mathf.RoundToInt(cycles * r.In[0].Amount.ToFloat())} in → {Mathf.RoundToInt(cycles * r.Out[0].Amount.ToFloat() * yield.ToFloat())} out /min"
                    : $"   idle: no {_data.Resources[r.In.FirstOrDefault(a => stock[a.Resource] <= Fix64.Zero, r.In[0]).Resource].Name.ToLowerInvariant()}";
                rows.Add((i == 0 ? "Refining" : "", line));
            }
            var r0 = def.Recipes[0];
            rows.Add(("Yield", $"{Hud.Whole(r0.In[0].Amount)} → {(r0.Out[0].Amount * yield).ToFloat():0.##} per cycle  ({Sources(s, Modifier.Yield)})"));
            rows.Add(("Speed", $"{(def.CyclesPerMinute * speed).ToFloat():0.#} cycles/min, base {Hud.Whole(def.CyclesPerMinute)} · {SpeedRow()}"));
        }
        if (def.IsMine)
        {
            var res = Surface.Map.DepositResource[s.Tile];
            if (res >= 0)
                rows.Add(("Mining", $"{_data.Resources[res].Name}  ·  {Hud.Whole(Surface.Map.DepositAmount[s.Tile])} left" +
                    (working ? $"  ·  +{Mathf.RoundToInt((def.ExtractPerMinute * now).ToFloat())}/min" : "")));
            rows.Add(("Speed", $"{Mathf.RoundToInt((def.ExtractPerMinute * speed).ToFloat())}/min, base {Hud.Whole(def.ExtractPerMinute)} · {SpeedRow()}"));
        }
        if (def.IsLab)
        {
            var me = Surface.Players[LocalPlayer];
            rows.Add(("Research", me.ActiveTech >= 0 ? $"{_data.Techs[me.ActiveTech].Name}: {_data.Techs[me.ActiveTech].Effect}" : "Nothing chosen: pick a tech on the right"));
            rows.Add(("Rate", $"{(def.ResearchPerMinute * now).ToFloat():0.#} points/min, base {Hud.Whole(def.ResearchPerMinute)} · {SpeedRow()}"));
            rows.Add(("Known", $"{me.Known.Count(k => k)} of {_data.Techs.Count} techs"));
        }
        if (def.IsFactory)
        {
            rows.Add(("Builds", string.Join("  ·  ", def.Produces.Select(u => $"{_data.GroundVehicleNames.GetValueOrDefault(u, u)} {_data.SurfaceUnit(u).BuildSeconds} s"))));
            rows.Add(("Queue", s.Production.Count == 0 ? "empty" : string.Join(", ", s.Production.Select(u => _data.GroundVehicleNames.GetValueOrDefault(u, u))) + "   [Backspace] cancel last"));
            rows.Add(("Speed", SpeedRow()));
        }
        if (def.PowerSupply > Fix64.Zero)
            rows.Add(("Supplies", $"{Mathf.RoundToInt((def.PowerSupply * conditionShare).ToFloat())} power" + (conditionShare < Fix64.One ? $" (of {Hud.Whole(def.PowerSupply)}: worn)" : "")));
        if (def.OutputPerMinute.Count > 0 && !def.IsCore) rows.Add(("Makes", Amounts(def.OutputPerMinute, true)));
        if (def.UpkeepPerMinute.Count > 0) rows.Add(("Upkeep", Amounts(def.UpkeepPerMinute, true)));
        var misc = new List<string>();
        if (def.PowerUse > Fix64.Zero) misc.Add($"uses {Hud.Whole(def.PowerUse)} power");
        if (def.Storage > Fix64.Zero) misc.Add($"storage +{Hud.Whole(def.Storage)}");
        misc.Add($"part of {BaseName(s.BaseId)}");
        rows.Add(("Base", string.Join("  ·  ", misc)));

        // Condition and maintenance.
        var condition = def.Wears && complete ? s.Condition.ToFloat() : -1f;
        var conditionText = "";
        var maintenance = "";
        if (condition >= 0)
        {
            conditionText = $"{Mathf.FloorToInt(condition)}%  ·  " + (working ? $"wears {Surface.WearPerMinute(s).ToFloat():0.#}/min ({Sources(s, Modifier.Wear)})" : "no wear while idle");
            var crew = Surface.CrewBase(s);
            var auto = Surface.AutoMaintained(s);
            var autoWhy = Surface.LevelModifiers(s).Any(m => m.Stat == Modifier.AutoMaintain) ? Surface.LevelName(s) : "research";
            maintenance =
                s.AutoRepairing ? (s.MaintenanceWaiting ? "Drones waiting for duranium" : "Drones repairing it")
                : s.MaintenanceOrdered && crew?.CrewJob == s.Id ? crew.CrewPhase switch
                {
                    CrewPhase.Out => "Crew on the way",
                    CrewPhase.Working => s.MaintenanceWaiting ? "Crew waiting for duranium" : "Crew repairing it",
                    _ => "Crew heading back",
                }
                : s.MaintenanceOrdered ? "Waiting for the crew (busy elsewhere)"
                : auto ? $"Drones fix it below {Hud.Whole(_data.Maintenance!.AutoBelow)}% ({autoWhy})"
                : s.Broken ? "Broken: press M to send the crew"
                : s.Condition < _data.Maintenance!.DeclineBelow ? $"Worn: output down to {Mathf.RoundToInt(conditionShare.ToFloat() * 100)}%, it may break. Press M"
                : "Manual: press M to send the crew";
        }

        // Progress: construction, upgrade, assembly or research.
        var progress = -1f;
        var progressLabel = "";
        var progressText = "";
        if (s.Assembling)
        {
            progress = Surface.AssembleFraction(s, _sim.Tick).ToFloat();
            progressLabel = "Assembling";
            progressText = $"{PlanetSurface.AssembleSecondsLeft(s, _sim.Tick)} s left  ·  no output, power, storage or build range until then";
        }
        else if (!complete)
        {
            progress = Surface.BuildFraction(s).ToFloat();
            progressLabel = s.State == StructureState.Queued ? "Queued" : "Construction";
            progressText = $"{Mathf.RoundToInt(progress * 100)}%  ·  {def.BuildSeconds} s in all";
        }
        else if (s.UpgradingLevel > 0)
        {
            progress = Surface.UpgradeFraction(s).ToFloat();
            progressLabel = $"Upgrading to {def.Levels[s.UpgradingLevel - 2].Name}";
            progressText = $"{Mathf.RoundToInt(progress * 100)}%  ·  X cancels (full refund)";
        }
        else if (def.IsFactory && s.Production.Count > 0)
        {
            progress = Surface.ProductionFraction(s).ToFloat();
            progressLabel = $"Assembling {_data.GroundVehicleNames.GetValueOrDefault(s.Production[0], s.Production[0])}";
            progressText = $"{Mathf.RoundToInt(progress * 100)}%" + (s.Production.Count > 1 ? $"  ·  {s.Production.Count - 1} more queued" : "");
        }
        else if (def.IsLab && Surface.Players[LocalPlayer].ActiveTech is var at && at >= 0)
        {
            var t = _data.Techs[at];
            var done = Surface.Players[LocalPlayer].ResearchProgress[at];
            progress = (done / t.Cost).ToFloat();
            progressLabel = $"Researching {t.Name}";
            var rate = Surface.ResearchPerMinute(LocalPlayer).ToFloat();
            var left = rate > 0 ? Mathf.CeilToInt((t.Cost - done).ToFloat() / rate * 60f) : -1;
            progressText = $"{Hud.Whole(done)} / {Hud.Whole(t.Cost)} points" + (left >= 0 ? $"  ·  ~{left / 60}:{left % 60:00} left" : "");
        }

        // Buttons.
        var actions = new List<ActionView>();
        if (s.Assembling)
        {
            if (def.Wears) actions.Add(new ActionView("maintain", "MAINTAIN [M]", PlanetSurface.AssemblingProblem, false));
        }
        else if (!complete) actions.Add(new ActionView("cancel", "CANCEL [X]", "full refund", true));
        else
        {
            if (s.UpgradingLevel > 0) actions.Add(new ActionView("cancel", "CANCEL UPGRADE [X]", "full refund", true));
            else if (Surface.NextLevel(s) is { } next)
            {
                var problem = Surface.UpgradeProblem(_sim, LocalPlayer, s.Id);
                var gains = string.Join(", ", next.Modifiers.Select(m => m.Stat == Modifier.AutoMaintain ? "drones" : $"{m.Stat} {Pct(m.Add)}"));
                actions.Add(new ActionView("upgrade", $"UPGRADE → {next.Name} [U]",
                    problem is null or "not enough resources" ? Amounts(next.Cost, false, shortNames: true) : problem, problem == null));
                if (rows.Count < 6) rows.Insert(System.Math.Min(rows.Count, 3), ("Next", $"{next.Name}: {gains} · {next.BuildSeconds} s"));
            }
            if (def.Wears)
            {
                var ordered = s.MaintenanceOrdered && Surface.CrewBase(s)?.CrewJob != s.Id;
                var why = Surface.MaintenanceProblem(LocalPlayer, s.Id);
                actions.Add(ordered
                    ? new ActionView("maintain", "CANCEL MAINTENANCE [M]", "", true)
                    : new ActionView("maintain", "MAINTAIN [M]", why ?? $"~{Amounts(Surface.MaintenanceCost(s), false, shortNames: true)}", why == null && !s.MaintenanceOrdered));
            }
            if (Surface.CanPause(s.DefId))
                actions.Add(s.Paused ? new ActionView("pause", "RESUME [P]", "back to work", true) : new ActionView("pause", "PAUSE [P]", "stop: no input, no power", true));
            if (!def.IsCore && s.UpgradingLevel == 0) actions.Add(new ActionView("salvage", "SALVAGE [X]", $"+{Amounts(Surface.SalvageValue(s), false, shortNames: true)}", true));
        }

        var subtitle = def.Levels.Count > 0 ? $"{Surface.LevelName(s)} of {def.MaxLevel switch { 2 => "Mk II", 3 => "Mk III", _ => $"{def.MaxLevel}" }}" : def.Category.Replace('_', ' ');
        _hud.ShowStructure(new StructureView(def.Name, subtitle, s.DefId, status, statusColour, rows.Take(6).ToList(),
            condition, conditionText, maintenance, progress, progressLabel, progressText, actions));
    }

    private string DescribeTile(int tile)
    {
        var map = Surface.Map;
        var terrain = map.Terrain(tile);
        var sb = new StringBuilder();
        sb.Append(terrain.Name);
        sb.Append(terrain.Passable ? $" · move cost {terrain.MoveCost}" : " · impassable");
        if (terrain.Buildable) sb.Append(" · buildable");
        var structure = Surface.StructureAt(tile);
        if (structure != null)
        {
            sb.Append('\n').Append(_data.Building(structure.DefId).Name);
            if (structure.Assembling) sb.Append($" · Assembling… {PlanetSurface.AssembleSecondsLeft(structure, _sim.Tick)} s");
        }

        if (Me.Phase == LandingPhase.InOrbit)
        {
            sb.Append(map.IsLandingSite(tile) ? "\nLanding site: yes" : "\nLanding site: no");
            var hints = map.Hints(map.Region(tile));
            sb.Append("\nRegion scan: ").Append(hints.Count == 0 ? "no readings" : string.Join(", ", hints.Select(h => $"{_data.Resources[h.Resource].Name} ({h.Strength.ToString().ToLowerInvariant()})")));
        }
        else if (Me.Surveyed[tile])
        {
            var r = map.DepositResource[tile];
            if (r < 0) sb.Append("\nNo deposit");
            else if (map.IsDepleted(tile))
                sb.Append($"\nDeposit: {_data.Resources[r].Name} used up").Append(map.DepositReopened[tile] ? " (already reopened once)" : " (a Deep-Core Mine can reopen it)");
            else sb.Append($"\nDeposit: {_data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}");
            var crawler = Surface.Units.FirstOrDefault(u => u.Tile == tile && u.Mode != UnitMode.Mobile);
            if (crawler != null) sb.Append('\n').Append(UnitName(crawler)).Append(" working here");
        }
        else sb.Append("\nNot surveyed: send a rover nearby");
        if (Me.Phase == LandingPhase.Landed) sb.Append(RoverTooltip(tile));
        return sb.ToString();
    }

    private string DescribePlacement(string buildingId, int tile)
    {
        var def = _data.Building(buildingId);
        var problem = Surface.CheckPlacement(_sim, LocalPlayer, buildingId, tile);
        var sb = new StringBuilder();
        sb.Append(def.Name).Append(" · ").Append(Amounts(def.Cost, false)).Append(" · ").Append(def.BuildSeconds).Append(" s");
        if (def.PowerUse > Fix64.Zero) sb.Append("\nUses ").Append(Hud.Whole(def.PowerUse)).Append(" power");
        if (def.PowerSupply > Fix64.Zero) sb.Append("\nSupplies ").Append(Hud.Whole(def.PowerSupply)).Append(" power");
        if (def.RequiresDeposit) sb.Append("\nMines the deposit under it: +").Append(Hud.Whole(def.ExtractPerMinute)).Append("/min");
        if (def.RequiresDepleted) sb.Append("\nGoes on a used-up deposit: reopens it once at ").Append(Mathf.RoundToInt(def.ReopenShare.ToFloat() * 100)).Append("% of its first size, mines +").Append(Hud.Whole(def.ExtractPerMinute)).Append("/min");
        if (def.RequiresLevel > 0) sb.Append("\nNeeds a level ").Append(def.RequiresLevel).Append(" base (Colonial Administration)");
        if (def.Storage > Fix64.Zero) sb.Append("\nStorage +").Append(Hud.Whole(def.Storage));
        foreach (var r in def.Recipes) sb.Append("\nTurns ").Append(Amounts(r.In, false)).Append(" → ").Append(Amounts(r.Out, false));
        if (def.UpkeepPerMinute.Count > 0) sb.Append("\nUpkeep ").Append(Amounts(def.UpkeepPerMinute, true));
        sb.Append(problem == PlacementProblem.None ? "\nClick to build here" : "\nCan't build here: " + Reason(problem));
        return sb.ToString();
    }

    private string Amounts(IReadOnlyList<ResourceAmount> amounts, bool perMinute, bool shortNames = false) =>
        // Biggest amount first: "150 DUR · 50 TRI" reads better than file order.
        string.Join(shortNames ? " · " : ", ", amounts.OrderByDescending(a => a.Amount).Select(a =>
        {
            var name = _data.Resources[a.Resource].Name;
            if (shortNames) name = name.Length > 3 ? name[..3].ToUpperInvariant() : name.ToUpperInvariant();
            return $"{Hud.Whole(a.Amount)} {name}{(perMinute ? "/min" : "")}";
        }));

    private static string Reason(PlacementProblem p) => p switch
    {
        PlacementProblem.NotSurveyed => "not surveyed yet (send a rover)",
        PlacementProblem.TerrainUnbuildable => "terrain isn't buildable",
        PlacementProblem.Occupied => "something is already here",
        PlacementProblem.OutOfRange => "too far from your base",
        PlacementProblem.NeedsDeposit => "needs a deposit to mine",
        PlacementProblem.CannotAfford => "not enough resources",
        PlacementProblem.NeedsDepletedDeposit => "needs a used-up deposit that hasn't been reopened",
        PlacementProblem.NeedsLevel => "upgrade this base to Colonial Administration first",
        PlacementProblem.NeedsTech => "research it first (build a Research Lab)",
        PlacementProblem.NotLanded => "land first",
        PlacementProblem.OffMap => "off the map",
        PlacementProblem.CoreAssembling => PlanetSurface.AssemblingProblem,
        PlacementProblem.CoreUnderConstruction => "the new outpost is still being built (exocomps)",
        _ => "not available",
    };

    // ---------------------------------------------------------------- camera

    private void FlyTo(int tile, float distance)
    {
        _focusTarget = _view.TileCentre(tile) with { Y = 0 };
        _distanceTarget = distance;
    }

    private void MoveCamera(float delta)
    {
        // Pan relative to the way the camera is facing.
        var forward = new Vector3(0, 0, -1).Rotated(Vector3.Up, _yaw);
        var right = new Vector3(1, 0, 0).Rotated(Vector3.Up, _yaw);
        var move = Vector3.Zero;
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit; // a test-panel field has the keyboard
        if (!typing && (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))) move += forward;
        if (!typing && (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))) move -= forward;
        if (!typing && (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))) move += right;
        if (!typing && (Input.IsKeyPressed(Key.Left) || (Input.IsKeyPressed(Key.A) && Me.Phase != LandingPhase.Landed))) move -= right;
        if (move != Vector3.Zero) _focusTarget += move.Normalized() * _distance * 0.9f * delta;

        var k = 1f - Mathf.Exp(-4f * delta);
        _focus = _focus.Lerp(_focusTarget, k);
        _distance = Mathf.Lerp(_distance, _distanceTarget, k);

        // Haze and shadows follow the zoom: thick close up, thin from high above so the whole map stays readable.
        if (_environment != null) _environment.FogDensity = 0.45f / Mathf.Max(60f, _distance);
        if (_sun != null) _sun.DirectionalShadowMaxDistance = Mathf.Clamp(_distance * 2.2f, 400f, 6000f);

        // Keep the focus over the map.
        var g = Surface.Map.Grid;
        _focusTarget.X = Mathf.Clamp(_focusTarget.X, 0, (g.Width - 0.5f) * PlanetView.HexWidth);
        _focusTarget.Z = Mathf.Clamp(_focusTarget.Z, 0, (g.Height - 1) * 0.866f * PlanetView.HexWidth);
        _yaw = Mathf.Lerp(_yaw, _yawSteps * Mathf.Pi / 2f, 1f - Mathf.Exp(-8f * delta));
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        // Look at the ground under the focus (eased, so panning over hills doesn't jolt).
        var focusTile = _view.TileAt(_focus);
        var ground = focusTile >= 0 ? _view.TopY(focusTile) : 0f;
        _lookY = Mathf.Lerp(_lookY, ground, 0.12f);
        var look = _focus with { Y = _lookY };

        // Strategy pitch from high up, flattening toward the ground as the camera comes in.
        var t = Mathf.SmoothStep(MinZoom, TiltFrom, _distance);
        var dir = new Vector3(0, 0.34f, 0.94f).Lerp(new Vector3(0, 0.82f, 0.57f), t).Normalized();
        var eye = look + dir.Rotated(Vector3.Up, _yaw) * _distance;

        // Never dip into a hill between the camera and the focus.
        var eyeTile = _view.TileAt(eye);
        if (eyeTile >= 0) eye.Y = Mathf.Max(eye.Y, _view.TopY(eyeTile) + 4f);
        _camera.LookAtFromPosition(eye, look, Vector3.Up);
    }
}
