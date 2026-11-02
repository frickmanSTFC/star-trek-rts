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
public partial class PlanetDriver : Node3D
{
    [Export] public int InputDelayTicks { get; set; } = 2;

    /// <summary>Change to get a different planet; the same seed always gives the same world.</summary>
    [Export] public ulong Seed { get; set; } = 1701;

    [Export] public string PlanetName { get; set; } = "Tarsis";

    /// <summary>How close unexplored ground gets to black (0..1).</summary>
    [Export(PropertyHint.Range, "0.5,0.98,0.01")] public float FogDarkness { get; set; } = 0.92f;

    public static readonly Color[] PlayerColors = { new(0.45f, 0.7f, 1f), new(0.95f, 0.6f, 0.25f) };

    private const int LocalPlayer = 0;

    private Simulation _sim = null!;
    private GameData _data = null!;
    private PlanetView _view = null!;
    private PlanetHud _hud = null!;
    private Camera3D _camera = null!;
    private double _accumulator;
    private int _hoverTile = -1;
    private bool _mouseOverUi;

    // Selection: a base (its core's structure id), a building, rovers (in the view) or a tile.
    private int _selectedBase;
    private int _selectedStructure;
    private int _selectedTile = -1;

    private string? _placing;
    private int _facing;

    private LandingPhase _lastPhase = LandingPhase.InOrbit;
    private readonly Dictionary<int, (StructureState State, string DefId, StructureStatus Status)> _seen = new();
    private readonly HashSet<int> _lowPowerBases = new();
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
    private int _yawSteps;
    private float _yaw;

    public override void _Ready()
    {
        _data = GameData.Load(GameData.FindDataDirectory(ProjectSettings.GlobalizePath("res://")));
        foreach (var problem in _data.Validate()) GD.PushWarning($"Data check: {problem}");

        _sim = MatchSetup.CreatePlanetLanding(_data, Seed);
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNode<PlanetHud>("Hud");
        _view = new PlanetView { FogDarkness = FogDarkness };
        AddChild(_view);
        _view.Build(_sim, LocalPlayer);
        _knownDeposits = new bool[_sim.Surface!.Map.Grid.Count];

        _hud.SetResources(_data.HudResources.Select(r => (r.Index, r.Id, r.Name)).ToList());
        _hud.Setup(_data.BuildMenu.Select((id, i) => (id, _data.Building(id).Name, $"[{i + 1}]")).ToList(), StartPlacing, SelectBase, Upgrade);
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
    }

    private SurfacePlayer Me => _sim.Surface!.Players[LocalPlayer];
    private PlanetSurface Surface => _sim.Surface!;

    public override void _Process(double delta)
    {
        _accumulator += delta;
        var tickSeconds = 1.0 / Simulation.TickRate;
        var steps = 0;
        while (_accumulator >= tickSeconds && steps < 5)
        {
            _sim.Step();
            _accumulator -= tickSeconds;
            steps++;
        }
        if (steps == 5) _accumulator = 0;

        OnPhaseChange();
        DetectEvents();
        if (_selectedStructure != 0 && Surface.FindStructure(_selectedStructure) == null) _selectedStructure = 0;
        if (Me.Phase == LandingPhase.Landed && Surface.FindStructure(_selectedBase) == null)
            _selectedBase = Surface.Bases(LocalPlayer).FirstOrDefault()?.Id ?? 0;

        MoveCamera((float)delta);
        UpdateHover();
        _view.Refresh((float)(_accumulator / tickSeconds));
        _hud.Minimap.Refresh(_camera);
        UpdateHud();
    }

    private void OnPhaseChange()
    {
        if (Me.Phase == _lastPhase) return;
        _lastPhase = Me.Phase;
        if (Me.Phase == LandingPhase.Descending) FlyTo(Me.LandingTile, 260f);
        if (Me.Phase == LandingPhase.Landed)
        {
            _view.SelectedUnits.UnionWith(Surface.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
            _selectedBase = Surface.Bases(LocalPlayer).FirstOrDefault()?.Id ?? 0;
            _hud.PushEvent($"Colony founded on {PlanetName}", PlayerColors[0]);
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
                if (was.State != StructureState.Complete && s.State == StructureState.Complete) _hud.PushEvent($"{name} completed", new Color(0.62f, 0.82f, 1f));
                if (was.DefId != s.DefId) _hud.PushEvent($"{BaseName(s.BaseId)} upgraded: {name} (Level {_data.Building(s.DefId).Level})", PlanetHud.Accent);
                if (was.Status != StructureStatus.NoUpkeep && s.Status == StructureStatus.NoUpkeep) _hud.PushEvent($"{name} idle: can't pay upkeep", new Color(1f, 0.7f, 0.42f));
                if (was.Status != StructureStatus.Depleted && s.Status == StructureStatus.Depleted) _hud.PushEvent($"{name}: deposit used up", new Color(1f, 0.7f, 0.42f));
            }
            _seen[s.Id] = now;
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
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                LeftClick(click.ShiftPressed);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                if (_placing != null) StopPlacing();
                else RightClick();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distanceTarget = Mathf.Max(60f, _distanceTarget * 0.88f);
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
            case Key.Escape:
                if (_placing != null) StopPlacing();
                else ClearSelection();
                break;
            case Key.Delete or Key.X:
                var target = _selectedStructure != 0 ? _selectedStructure : _selectedBase;
                if (target != 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new CancelBuildingCommand(LocalPlayer, target));
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
                if (index < _data.BuildMenu.Count) StartPlacing(_data.BuildMenu[index]);
                break;
        }
    }

    private void ClearSelection()
    {
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

    private void Upgrade()
    {
        if (_selectedBase != 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new UpgradeCommand(LocalPlayer, _selectedBase));
    }

    private void StartPlacing(string buildingId)
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (_placing == buildingId)
        {
            StopPlacing(); // pressing the same button again turns it off
            return;
        }
        _placing = buildingId;
        _view.SelectedUnits.Clear();
        _selectedStructure = 0;
        var core = Surface.FindStructure(_selectedBase);
        if (core != null) _view.ShowBuildRange(core.Tile, _data.Building(core.DefId).BuildRadius);
    }

    private void StopPlacing()
    {
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

        if (_placing != null)
        {
            if (Surface.CheckPlacement(_sim, LocalPlayer, _placing, _hoverTile) == PlacementProblem.None)
            {
                _sim.Schedule(_sim.Tick + InputDelayTicks, new PlaceBuildingCommand(LocalPlayer, _placing, _hoverTile, _facing));
                if (!shift) StopPlacing(); // Shift keeps placing the same building
            }
            return;
        }

        var unit = Surface.Units.FirstOrDefault(u => u.Owner == LocalPlayer && u.Tile == _hoverTile);
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
        _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveUnitsCommand(LocalPlayer, ids, _hoverTile));
    }

    private void UpdateHover()
    {
        var mouse = GetViewport().GetMousePosition();
        _mouseOverUi = _hud.BlocksMouse(mouse);
        _hoverTile = -1;
        if (!_mouseOverUi)
        {
            _hoverTile = _view.PickTile(_camera.ProjectRayOrigin(mouse), _camera.ProjectRayNormal(mouse));
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
            colour = Surface.CanLand(LocalPlayer, _hoverTile) ? new Color(0.3f, 1f, 0.4f, 0.55f) : new Color(1f, 0.3f, 0.25f, 0.45f);
        _view.SetHover(Me.Phase == LandingPhase.Descending ? -1 : _hoverTile, colour);
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
        var supply = 0f;
        var use = 0f;
        foreach (var b in Surface.Bases(LocalPlayer))
        {
            var p = Surface.PowerOf(b.Id);
            supply += p.Supply.ToFloat();
            use += p.Use.ToFloat();
        }
        var seconds = _sim.Tick / Simulation.TickRate;
        _hud.ShowTop(_sim, LocalPlayer, $"{PlanetName.ToUpperInvariant()} · CLASS M", use, supply, $"{seconds / 60:00}:{seconds % 60:00}");

        var landed = Me.Phase == LandingPhase.Landed;
        _hud.ShowHints(Me.Phase == LandingPhase.InOrbit
            ? "Left-click: land  ·  WASD / middle-drag pan  ·  Q/E turn  ·  wheel zoom"
            : "Left-click select  ·  right-click drive  ·  1–3 build  ·  R rotate  ·  U upgrade  ·  X cancel  ·  Tab next base  ·  Q/E turn  ·  H home");

        _hud.ShowBanner(Me.Phase switch
        {
            LandingPhase.InOrbit => $"In orbit over {PlanetName}. Choose a landing site: plains or desert with open land on every side (green).",
            LandingPhase.Descending => $"Colony pod descending… touchdown in {(Me.LandsAtTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate} s",
            _ when _placing != null => $"Placing {_data.Building(_placing).Name}  ·  R rotate  ·  Shift+click place more  ·  right-click / Esc stop",
            _ => "",
        });

        // Tooltip: what's under the mouse (or why the building can't go there).
        _hud.ShowTooltip(_hoverTile < 0 ? "" : _placing != null ? DescribePlacement(_placing, _hoverTile) : DescribeTile(_hoverTile), GetViewport().GetMousePosition());

        // Bottom centre.
        var bases = Surface.Bases(LocalPlayer);
        _hud.ShowBaseTabs(bases.Select(b => (b.Id, BaseName(b.Id))).ToList(), _selectedBase);
        if (Me.Phase == LandingPhase.InOrbit) _hud.ShowDetail("Orbital scan", $"{PlanetName} · Class M", "The scan shows terrain and, per region, which resources are likely below.\nHover a tile for details; green tiles are landing sites. Once down, survey rovers find the exact deposits.");
        else if (Me.Phase == LandingPhase.Descending) _hud.ShowDetail("Colony pod", "Descending", "The pod carries the Colony Hub, two survey rovers and starting supplies.");
        else if (_selectedStructure != 0) ShowStructure(Surface.FindStructure(_selectedStructure)!);
        else if (_view.SelectedUnits.Count > 0)
            _hud.ShowDetail($"{_view.SelectedUnits.Count} survey rover{(_view.SelectedUnits.Count == 1 ? "" : "s")}", "Selected",
                "Right-click a tile to drive there. Rovers survey the ground around them as they go: deposits they find appear on the map.\nEsc or click a base tab to go back to the base.");
        else if (_selectedTile >= 0) _hud.ShowDetail(Surface.Map.Terrain(_selectedTile).Name, "Tile", DescribeTile(_selectedTile));
        else if (_selectedBase != 0) ShowBase(Surface.FindStructure(_selectedBase)!);

        var stock = _sim.Players[LocalPlayer].Stock;
        _hud.ShowCommands(landed ? $"BUILD AT {BaseName(_selectedBase).ToUpperInvariant()}" : "BUILD (after landing)",
            _data.BuildMenu.Select((id, i) =>
            {
                var def = _data.Building(id);
                return new CommandView(id, $"[{i + 1}]", def.Name, Amounts(def.Cost, false), stock.CanAfford(def.Cost), _placing == id);
            }).ToList(), landed);
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
            line = $"Next: {next.Name} (Level {next.Level}) · {next.BuildQueues} build queues, radius {next.BuildRadius}, +{Hud.Whole(next.PowerSupply - def.PowerSupply)} power";
            cost = Amounts(next.Cost, false, shortNames: true);
            var problem = Surface.UpgradeProblem(_sim, LocalPlayer, core.Id);
            enabled = problem == null;
            if (problem != null && problem != "not enough resources") line += $"\n({problem})";
        }
        else line = "Top level";

        var queue = members.Where(s => s.State != StructureState.Complete)
            .Select(s => (_data.Building(s.DefId).Name, s.State == StructureState.Building ? Surface.BuildFraction(s).ToFloat() : -1f)).ToList();

        _hud.ShowBase(new BaseView(BaseName(core.Id), def.Name, core.DefId, def.Level, line, cost, enabled, progress,
            power.Use.ToFloat(), power.Supply.ToFloat(), buildings, def.BuildQueues, queue));
    }

    private void ShowStructure(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        var power = Surface.PowerOf(s.BaseId);
        var status = s.State switch
        {
            StructureState.Queued => "Queued: waits for a free build queue",
            StructureState.Building => $"Under construction: {Mathf.RoundToInt(Surface.BuildFraction(s).ToFloat() * 100)}%",
            _ => s.Status switch
            {
                StructureStatus.NoUpkeep => "Idle: can't pay upkeep",
                StructureStatus.Depleted => "Idle: deposit used up",
                StructureStatus.LowPower => $"Low power: running at {Mathf.RoundToInt(power.Efficiency.ToFloat() * 100)}%",
                _ => "Working",
            },
        };
        var sb = new StringBuilder();
        sb.Append("Part of ").AppendLine(BaseName(s.BaseId));
        if (def.PowerSupply > Fix64.Zero) sb.Append("Supplies ").Append(Hud.Whole(def.PowerSupply)).AppendLine(" power");
        if (def.PowerUse > Fix64.Zero) sb.Append("Uses ").Append(Hud.Whole(def.PowerUse)).AppendLine(" power");
        if (def.OutputPerMinute.Count > 0) sb.Append("Makes ").AppendLine(Amounts(def.OutputPerMinute, true));
        if (def.RequiresDeposit && Surface.Map.DepositResource[s.Tile] >= 0)
            sb.Append("Mining ").Append(_data.Resources[Surface.Map.DepositResource[s.Tile]].Name).Append(" +").Append(Hud.Whole(def.ExtractPerMinute))
              .Append("/min · ").Append(Hud.Whole(Surface.Map.DepositAmount[s.Tile])).AppendLine(" left");
        if (def.UpkeepPerMinute.Count > 0) sb.Append("Upkeep ").AppendLine(Amounts(def.UpkeepPerMinute, true));
        if (s.State != StructureState.Complete) sb.Append("[X] cancel (full refund)");
        _hud.ShowDetail(def.Name, status, sb.ToString().TrimEnd());
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
        if (structure != null) sb.Append('\n').Append(_data.Building(structure.DefId).Name);

        if (Me.Phase == LandingPhase.InOrbit)
        {
            sb.Append(map.IsLandingSite(tile) ? "\nLanding site: yes" : "\nLanding site: no");
            var hints = map.Hints(map.Region(tile));
            sb.Append("\nRegion scan: ").Append(hints.Count == 0 ? "no readings" : string.Join(", ", hints.Select(h => $"{_data.Resources[h.Resource].Name} ({h.Strength.ToString().ToLowerInvariant()})")));
        }
        else if (Me.Surveyed[tile])
        {
            var r = map.DepositResource[tile];
            sb.Append(r < 0 ? "\nNo deposit" : $"\nDeposit: {_data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}");
        }
        else sb.Append("\nNot surveyed: send a rover nearby");
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
        if (def.UpkeepPerMinute.Count > 0) sb.Append("\nUpkeep ").Append(Amounts(def.UpkeepPerMinute, true));
        sb.Append(problem == PlacementProblem.None ? "\nClick to build here" : "\nCan't build here: " + Reason(problem));
        return sb.ToString();
    }

    private string Amounts(IReadOnlyList<ResourceAmount> amounts, bool perMinute, bool shortNames = false) =>
        string.Join(shortNames ? " · " : ", ", amounts.Select(a =>
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
        PlacementProblem.NotLanded => "land first",
        PlacementProblem.OffMap => "off the map",
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
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move += forward;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move -= forward;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move += right;
        if (Input.IsKeyPressed(Key.Left) || (Input.IsKeyPressed(Key.A) && Me.Phase != LandingPhase.Landed)) move -= right;
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
        var offset = new Vector3(0, 0.82f, 0.57f).Rotated(Vector3.Up, _yaw) * _distance;
        _camera.LookAtFromPosition(_focus + offset, _focus, Vector3.Up);
    }
}
