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
    private readonly Dictionary<int, (UnitMode Mode, MiningStatus Status)> _seenUnits = new();
    private bool _storageFull;
    private readonly HashSet<int> _knownUnits = new();
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
        // The command card holds the build menu and every Vehicle Bay order; UpdateHud shows one set at a time.
        var commands = _data.BuildMenu.Select((id, i) => (id, _data.Building(id).Name, $"[{i + 1}]")).ToList();
        foreach (var unit in _data.BuildMenu.SelectMany(id => _data.Building(id).Produces).Distinct().ToList())
        {
            var key = _data.BuildMenu.Select(id => _data.Building(id).Produces.ToList().IndexOf(unit)).FirstOrDefault(i => i >= 0);
            commands.Add(($"unit:{unit}", _data.GroundVehicleNames.GetValueOrDefault(unit, unit), $"[{key + 1}]"));
        }
        _hud.Setup(commands, OnCommand, SelectBase, Upgrade);
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
                _distanceTarget = Mathf.Max(90f, _distanceTarget * 0.88f);
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
                var picked = Surface.FindStructure(target);
                if (picked == null) break;
                // A finished building (not a core) is salvaged; anything unfinished, or a core's upgrade, is cancelled.
                Command order = picked.State == StructureState.Complete && !_data.Building(picked.DefId).IsCore
                    ? new SalvageCommand(LocalPlayer, picked.Id)
                    : new CancelBuildingCommand(LocalPlayer, picked.Id);
                _sim.Schedule(_sim.Tick + InputDelayTicks, order);
                break;
            case Key.F when landed:
                ToggleDeploy();
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
                if (SelectedBay() is { } orderAt)
                {
                    var produces = _data.Building(orderAt.DefId).Produces;
                    if (index < produces.Count) OrderUnit(produces[index]);
                }
                else if (index < _data.BuildMenu.Count) StartPlacing(_data.BuildMenu[index]);
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

    private string UnitName(SurfaceUnit u) => _data.GroundVehicleNames.GetValueOrDefault(u.DefId, u.DefId);

    /// <summary>The selected building if it's one of our finished Vehicle Bays (its orders replace the build menu).</summary>
    private SurfaceStructure? SelectedBay()
    {
        var s = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        return s != null && s.Owner == LocalPlayer && s.State == StructureState.Complete && _data.Building(s.DefId).IsFactory ? s : null;
    }

    /// <summary>A command-card button: a building to place, or "unit:id" to order at the selected bay.</summary>
    private void OnCommand(string id)
    {
        if (id.StartsWith("unit:")) OrderUnit(id[5..]);
        else StartPlacing(id);
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
            drivers.Add(id);
        }
        if (drivers.Count > 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveUnitsCommand(LocalPlayer, drivers, _hoverTile));
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
        var stored = (Surface.Stored(_sim, LocalPlayer) + Surface.InFlight(LocalPlayer)).ToFloat();
        _hud.ShowTop(_sim, LocalPlayer, $"{PlanetName.ToUpperInvariant()} · CLASS M", use, supply, stored,
            Surface.StorageCapacity(LocalPlayer).ToFloat(), $"{seconds / 60:00}:{seconds % 60:00}");

        var landed = Me.Phase == LandingPhase.Landed;
        _hud.ShowHints(Me.Phase == LandingPhase.InOrbit
            ? "Left-click: land  ·  WASD / middle-drag pan  ·  Q/E turn  ·  wheel zoom"
            : $"Left-click select  ·  right-click drive / mine  ·  F unfurl / pack up / repair  ·  1–{_data.BuildMenu.Count} build  ·  R rotate  ·  U upgrade  ·  X cancel / salvage  ·  Backspace cancel order  ·  Tab next base  ·  Q/E turn  ·  H home");

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
        else if (Me.Phase == LandingPhase.Descending) _hud.ShowDetail("Colony pod", "Descending", "The pod carries the Colony Hub (with a small storage vault), two survey rovers, a mining crawler and starting supplies.");
        else if (_selectedStructure != 0) ShowStructure(Surface.FindStructure(_selectedStructure)!);
        else if (_view.SelectedUnits.Count > 0) ShowUnits();
        else if (_selectedTile >= 0) _hud.ShowDetail(Surface.Map.Terrain(_selectedTile).Name, "Tile", DescribeTile(_selectedTile));
        else if (_selectedBase != 0) ShowBase(Surface.FindStructure(_selectedBase)!);

        var stock = _sim.Players[LocalPlayer].Stock;
        if (SelectedBay() is { } bay)
        {
            var bayDef = _data.Building(bay.DefId);
            _hud.ShowCommands($"{bayDef.Name.ToUpperInvariant()} · ORDERS ({bay.Production.Count}/{bayDef.ProductionQueue})",
                bayDef.Produces.Select(u =>
                {
                    var ud = _data.SurfaceUnit(u);
                    var problem = Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, u);
                    return new CommandView($"unit:{u}", "", "", Amounts(ud.Cost, false, shortNames: true), problem == null, false);
                }).ToList(), true);
            return;
        }
        _hud.ShowCommands(landed ? $"BUILD AT {BaseName(_selectedBase).ToUpperInvariant()}" : "BUILD (after landing)",
            _data.BuildMenu.Select((id, i) =>
            {
                var def = _data.Building(id);
                return new CommandView(id, $"[{i + 1}]", def.Name, Amounts(def.Cost, false, shortNames: true), stock.CanAfford(def.Cost), _placing == id);
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

    private void ShowUnits()
    {
        var units = _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null).Select(u => u!).ToList();
        if (units.Count == 1 && _data.SurfaceUnit(units[0].DefId).CanMine)
        {
            ShowCrawler(units[0]);
            return;
        }
        var rovers = units.Count(u => !_data.SurfaceUnit(u.DefId).CanMine);
        var crawlers = units.Count - rovers;
        var title = string.Join(" + ", new[]
        {
            rovers > 0 ? $"{rovers} survey rover{(rovers == 1 ? "" : "s")}" : "",
            crawlers > 0 ? $"{crawlers} mining crawler{(crawlers == 1 ? "" : "s")}" : "",
        }.Where(t => t.Length > 0));
        _hud.ShowDetail(title, "Selected",
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
                StructureStatus.StorageFull => "Idle: storage full",
                StructureStatus.NoInput when def.IsFactory => "Idle: no orders",
                StructureStatus.NoInput => def.Recipes.Count > 0 && def.Recipes[0].In.Any(a => _data.Resources[a.Resource].Tier == "raw") ? "Idle: no ore in storage" : "Idle: nothing to work on",
                _ => "Working",
            },
        };
        var sb = new StringBuilder();
        sb.Append("Part of ").AppendLine(BaseName(s.BaseId));
        if (def.PowerSupply > Fix64.Zero) sb.Append("Supplies ").Append(Hud.Whole(def.PowerSupply)).AppendLine(" power");
        if (def.PowerUse > Fix64.Zero) sb.Append("Uses ").Append(Hud.Whole(def.PowerUse)).AppendLine(" power");
        if (def.OutputPerMinute.Count > 0) sb.Append("Makes ").AppendLine(Amounts(def.OutputPerMinute, true));
        if (def.Storage > Fix64.Zero) sb.Append("Storage +").AppendLine(Hud.Whole(def.Storage));
        foreach (var r in def.Recipes) sb.Append("Turns ").Append(Amounts(r.In, false)).Append(" → ").AppendLine(Amounts(r.Out, false));
        if (def.IsConverter) sb.Append("Up to ").Append(Hud.Whole(def.CyclesPerMinute)).AppendLine(" cycles/min");
        if (def.IsMine && Surface.Map.DepositResource[s.Tile] >= 0)
            sb.Append("Mining ").Append(_data.Resources[Surface.Map.DepositResource[s.Tile]].Name).Append(" +").Append(Hud.Whole(def.ExtractPerMinute))
              .Append("/min · ").Append(Hud.Whole(Surface.Map.DepositAmount[s.Tile])).AppendLine(" left");
        if (def.UpkeepPerMinute.Count > 0) sb.Append("Upkeep ").AppendLine(Amounts(def.UpkeepPerMinute, true));
        if (def.IsFactory && s.State == StructureState.Complete)
        {
            sb.Append("Builds ").AppendLine(string.Join("  ·  ", def.Produces.Select(u =>
                $"{_data.GroundVehicleNames.GetValueOrDefault(u, u)} ({Amounts(_data.SurfaceUnit(u).Cost, false, shortNames: true)}, {_data.SurfaceUnit(u).BuildSeconds} s)")));
            if (s.Production.Count == 0) sb.AppendLine("No orders: pick a vehicle on the right (or press its number)");
            else
            {
                sb.Append("Assembling ").Append(_data.GroundVehicleNames.GetValueOrDefault(s.Production[0], s.Production[0])).Append(' ')
                  .Append(Mathf.FloorToInt(Surface.ProductionFraction(s).ToFloat() * 100)).Append('%');
                if (s.Production.Count > 1)
                    sb.Append("  ·  then ").Append(string.Join(", ", s.Production.Skip(1).Select(u => _data.GroundVehicleNames.GetValueOrDefault(u, u))));
                sb.AppendLine().AppendLine("[Backspace] cancel the last order (full refund)");
            }
        }
        if (s.State != StructureState.Complete) sb.Append("[X] cancel (full refund)");
        else if (!def.IsCore) sb.Append("[X] salvage: get back ").Append(Amounts(Surface.SalvageValue(s.DefId), false));
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
            if (r < 0) sb.Append("\nNo deposit");
            else if (map.IsDepleted(tile))
                sb.Append($"\nDeposit: {_data.Resources[r].Name} used up").Append(map.DepositReopened[tile] ? " (already reopened once)" : " (a Deep-Core Mine can reopen it)");
            else sb.Append($"\nDeposit: {_data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}");
            var crawler = Surface.Units.FirstOrDefault(u => u.Tile == tile && u.Mode != UnitMode.Mobile);
            if (crawler != null) sb.Append('\n').Append(UnitName(crawler)).Append(" working here");
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
        if (def.RequiresDepleted) sb.Append("\nGoes on a used-up deposit: reopens it once at ").Append(Mathf.RoundToInt(def.ReopenShare.ToFloat() * 100)).Append("% of its first size, mines +").Append(Hud.Whole(def.ExtractPerMinute)).Append("/min");
        if (def.RequiresLevel > 0) sb.Append("\nNeeds a level ").Append(def.RequiresLevel).Append(" base (Colonial Administration)");
        if (def.Storage > Fix64.Zero) sb.Append("\nStorage +").Append(Hud.Whole(def.Storage));
        foreach (var r in def.Recipes) sb.Append("\nTurns ").Append(Amounts(r.In, false)).Append(" → ").Append(Amounts(r.Out, false));
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
        PlacementProblem.NeedsDepletedDeposit => "needs a used-up deposit that hasn't been reopened",
        PlacementProblem.NeedsLevel => "upgrade this base to Colonial Administration first",
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
