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
/// survey rovers to find deposits, then place mines, reactors and habitats. Like SimDriver, this only
/// schedules commands; the sim decides what happens.
/// </summary>
public partial class PlanetDriver : Node3D
{
    [Export] public int InputDelayTicks { get; set; } = 2;

    /// <summary>Change to get a different planet; the same seed always gives the same world.</summary>
    [Export] public ulong Seed { get; set; } = 1701;

    [Export] public string PlanetName { get; set; } = "Tarsis";

    public static readonly Color[] PlayerColors = { new(0.45f, 0.7f, 1f), new(0.95f, 0.6f, 0.25f) };

    private const int LocalPlayer = 0;

    private Simulation _sim = null!;
    private PlanetView _view = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private double _accumulator;
    private int _hoverTile = -1;
    private int _selectedTile = -1;
    private int _selectedStructure;
    private LandingPhase _lastPhase = LandingPhase.InOrbit;

    // Build mode: which building is being placed, and which way it faces (0–5, 60° steps).
    private string? _placing;
    private int _facing;

    // Camera: focus point on the ground, distance, and yaw in 90° steps (Q / E), all eased.
    private Vector3 _focus;
    private float _distance = 420f;
    private Vector3 _focusTarget;
    private float _distanceTarget = 420f;
    private int _yawSteps;
    private float _yaw;

    public override void _Ready()
    {
        var data = GameData.Load(GameData.FindDataDirectory(ProjectSettings.GlobalizePath("res://")));
        foreach (var problem in data.Validate()) GD.PushWarning($"Data check: {problem}");

        _sim = MatchSetup.CreatePlanetLanding(data, Seed);
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNode<Hud>("Hud");
        _view = new PlanetView();
        AddChild(_view);
        _view.Build(_sim, LocalPlayer);

        _hud.SetBuildMenu(data.BuildMenu.Select((id, i) =>
        {
            var def = data.Building(id);
            var cost = string.Join(", ", def.Cost.Select(c => $"{Hud.Whole(c.Amount)} {data.Resources[c.Resource].Name}"));
            return (id, $"[{i + 1}] {def.Name}\n{cost}");
        }), StartPlacing);

        _focus = _focusTarget = _view.MapCentre();
        UpdateCamera();
    }

    private SurfacePlayer Me => _sim.Surface!.Players[LocalPlayer];

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

        if (Me.Phase != _lastPhase)
        {
            _lastPhase = Me.Phase;
            if (Me.Phase == LandingPhase.Descending) FlyTo(Me.LandingTile, 160f);
            if (Me.Phase == LandingPhase.Landed) _view.SelectedUnits.UnionWith(_sim.Surface!.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
        }
        if (_selectedStructure != 0 && _sim.Surface!.FindStructure(_selectedStructure) == null) _selectedStructure = 0;

        MoveCamera((float)delta);
        UpdateHover();
        _view.Refresh((float)(_accumulator / tickSeconds));
        UpdateHud();
    }

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
                _distanceTarget = Mathf.Max(50f, _distanceTarget * 0.88f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distanceTarget = Mathf.Min(600f, _distanceTarget * 1.12f);
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
                else
                {
                    _view.SelectedUnits.Clear();
                    _selectedTile = -1;
                    _selectedStructure = 0;
                }
                break;
            case Key.Delete or Key.X when _selectedStructure != 0:
                _sim.Schedule(_sim.Tick + InputDelayTicks, new CancelBuildingCommand(LocalPlayer, _selectedStructure));
                break;
            case Key.A when landed && _placing == null:
                _view.SelectedUnits.Clear();
                _view.SelectedUnits.UnionWith(_sim.Surface!.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
                break;
            case Key.H:
                if (Me.LandingTile >= 0) FlyTo(Me.LandingTile, 160f);
                else
                {
                    _focusTarget = _view.MapCentre();
                    _distanceTarget = 420f;
                }
                break;
            case >= Key.Key1 and <= Key.Key9 when landed:
                var index = (int)(key.Keycode - Key.Key1);
                if (index < _sim.Data.BuildMenu.Count) StartPlacing(_sim.Data.BuildMenu[index]);
                break;
        }
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
        var hub = _sim.Surface!.Structures.FirstOrDefault(s => s.Owner == LocalPlayer && _sim.Data.Building(s.DefId).BuildRadius > 0);
        if (hub != null) _view.ShowBuildRange(hub.Tile, _sim.Data.Building(hub.DefId).BuildRadius);
    }

    private void StopPlacing()
    {
        _placing = null;
        _view.HideBuildRange();
    }

    private void LeftClick(bool shift)
    {
        if (_hoverTile < 0) return;
        var surface = _sim.Surface!;

        if (Me.Phase == LandingPhase.InOrbit)
        {
            if (surface.CanLand(LocalPlayer, _hoverTile))
                _sim.Schedule(_sim.Tick + InputDelayTicks, new LandColonyCommand(LocalPlayer, _hoverTile));
            return;
        }

        if (_placing != null)
        {
            if (surface.CheckPlacement(_sim, LocalPlayer, _placing, _hoverTile) == PlacementProblem.None)
            {
                _sim.Schedule(_sim.Tick + InputDelayTicks, new PlaceBuildingCommand(LocalPlayer, _placing, _hoverTile, _facing));
                if (!shift) StopPlacing(); // Shift keeps placing the same building
            }
            return;
        }

        var unit = surface.Units.FirstOrDefault(u => u.Owner == LocalPlayer && u.Tile == _hoverTile);
        if (unit != null)
        {
            if (!shift) _view.SelectedUnits.Clear();
            _view.SelectedUnits.Add(unit.Id);
            _selectedTile = -1;
            _selectedStructure = 0;
            return;
        }

        if (!shift) _view.SelectedUnits.Clear();
        var structure = surface.StructureAt(_hoverTile);
        _selectedStructure = structure != null && structure.Owner == LocalPlayer ? structure.Id : 0;
        _selectedTile = _hoverTile;
    }

    private void RightClick()
    {
        if (_hoverTile < 0 || _view.SelectedUnits.Count == 0 || Me.Phase != LandingPhase.Landed) return;
        var ids = _view.SelectedUnits.OrderBy(id => id).ToArray();
        _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveUnitsCommand(LocalPlayer, ids, _hoverTile));
    }

    private void UpdateHover()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);
        _hoverTile = -1;
        if (Mathf.Abs(dir.Y) > 1e-5f)
        {
            var t = (2.5f - origin.Y) / dir.Y; // roughly the height of lowland tops
            if (t > 0) _hoverTile = _view.TileAt(origin + dir * t);
        }

        var surface = _sim.Surface!;
        if (_placing != null)
        {
            var ok = _hoverTile >= 0 && surface.CheckPlacement(_sim, LocalPlayer, _placing, _hoverTile) == PlacementProblem.None;
            _view.SetGhost(_placing, _hoverTile, _facing, ok);
            _view.SetHover(-1, Colors.White);
            return;
        }
        _view.SetGhost(null, -1, 0, false);

        var colour = new Color(1, 1, 1, 0.3f);
        if (_hoverTile >= 0 && Me.Phase == LandingPhase.InOrbit)
            colour = surface.CanLand(LocalPlayer, _hoverTile) ? new Color(0.3f, 1f, 0.4f, 0.55f) : new Color(1f, 0.3f, 0.25f, 0.45f);
        _view.SetHover(Me.Phase == LandingPhase.Descending ? -1 : _hoverTile, colour);
    }

    // ---------------------------------------------------------------- HUD text

    private void UpdateHud()
    {
        var surface = _sim.Surface!;
        _hud.ShowResources(_sim, LocalPlayer);
        _hud.ShowBuildMenu(Me.Phase == LandingPhase.Landed, _placing);

        switch (Me.Phase)
        {
            case LandingPhase.InOrbit:
                _hud.ShowBanner($"In orbit over {PlanetName}. Choose a landing site: plains or desert with open land on every side (green).");
                break;
            case LandingPhase.Descending:
                var left = (Me.LandsAtTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate;
                _hud.ShowBanner($"Colony pod descending… touchdown in {left} s");
                break;
            default:
                _hud.ShowBanner(_placing != null ? $"Placing {_sim.Data.Building(_placing).Name}: [R] rotate  [Shift]+click place more  [Right-click / Esc] stop"
                    : _sim.Tick - Me.LandsAtTick < Simulation.TickRate * 6 ? "Colony founded. Right-click to send the survey rovers out; build from the bar below."
                    : "");
                break;
        }

        string info;
        if (_placing != null) info = DescribePlacement(_placing, _hoverTile);
        else if (_selectedStructure != 0) info = DescribeStructure(surface.FindStructure(_selectedStructure)!);
        else
        {
            info = _selectedTile >= 0 ? DescribeTile(_selectedTile) : _hoverTile >= 0 ? DescribeTile(_hoverTile) : "";
            if (_view.SelectedUnits.Count > 0)
                info = $"{_view.SelectedUnits.Count} survey rover{(_view.SelectedUnits.Count == 1 ? "" : "s")} selected · right-click a tile to drive there\n" + info;
        }
        _hud.ShowInfo(info.TrimEnd());

        var seconds = _sim.Tick / Simulation.TickRate;
        _hud.ShowStatus(
            $"{PlanetName} (seed {Seed})  ·  {seconds / 60}:{seconds % 60:00}  ·  state {_sim.StateHash():X16}\n" +
            (Me.Phase == LandingPhase.InOrbit
                ? "[Left-click] land here  [WASD / arrows] pan  [Q / E] turn view  [Wheel] zoom"
                : "[Left-click] select  [Shift] add  [A] all rovers  [Right-click] drive  [1–3] build  [X] cancel build\n" +
                  "[WASD / arrows] pan  [Q / E] turn view  [Wheel] zoom  [H] colony"));
    }

    private string DescribeTile(int tile)
    {
        var data = _sim.Data;
        var map = _sim.Surface!.Map;
        var terrain = map.Terrain(tile);
        var sb = new StringBuilder();
        sb.Append(terrain.Name);
        sb.Append(terrain.Passable ? $"  ·  move cost {terrain.MoveCost}" : "  ·  impassable");
        sb.AppendLine(terrain.Buildable ? "  ·  buildable" : "");

        var structure = _sim.Surface.StructureAt(tile);
        if (structure != null) sb.Append(data.Building(structure.DefId).Name).AppendLine(structure.Owner == LocalPlayer ? " (yours)" : "");

        if (Me.Phase == LandingPhase.InOrbit)
        {
            sb.AppendLine(map.IsLandingSite(tile) ? "Landing site: yes" : "Landing site: no");
            var hints = map.Hints(map.Region(tile));
            sb.Append("Region scan: ").Append(hints.Count == 0 ? "no readings" : string.Join(", ", hints.Select(h => $"{data.Resources[h.Resource].Name} ({h.Strength.ToString().ToLowerInvariant()})")));
        }
        else if (Me.Surveyed[tile])
        {
            var r = map.DepositResource[tile];
            sb.Append(r < 0 ? "Surveyed: no deposit" : $"Deposit: {data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}");
        }
        else sb.Append("Not surveyed: send a rover within range to find out what's here");
        return sb.ToString();
    }

    private string DescribePlacement(string buildingId, int tile)
    {
        var data = _sim.Data;
        var def = data.Building(buildingId);
        var sb = new StringBuilder();
        sb.AppendLine(def.Name);
        sb.Append("Cost: ").AppendLine(Amounts(def.Cost, false));
        sb.Append("Build time: ").Append(def.BuildSeconds).AppendLine(" s");
        if (def.OutputPerMinute.Count > 0) sb.Append("Makes: ").AppendLine(Amounts(def.OutputPerMinute, true));
        if (def.RequiresDeposit) sb.Append("Mines the deposit under it: +").Append(Hud.Whole(def.ExtractPerMinute)).AppendLine("/min");
        if (def.UpkeepPerMinute.Count > 0) sb.Append("Upkeep: ").AppendLine(Amounts(def.UpkeepPerMinute, true));
        if (tile < 0) return sb.ToString();

        var problem = _sim.Surface!.CheckPlacement(_sim, LocalPlayer, buildingId, tile);
        sb.Append(problem == PlacementProblem.None ? "Click to build here" : "Can't build here: " + Reason(problem));
        var r = _sim.Surface.Map.DepositResource[tile];
        if (def.RequiresDeposit && r >= 0 && Me.Surveyed[tile])
            sb.Append($"\nDeposit: {data.Resources[r].Name} {Hud.Whole(_sim.Surface.Map.DepositAmount[tile])}");
        return sb.ToString();
    }

    private string DescribeStructure(SurfaceStructure s)
    {
        var data = _sim.Data;
        var def = data.Building(s.DefId);
        var surface = _sim.Surface!;
        var sb = new StringBuilder();
        sb.AppendLine(def.Name);
        switch (s.State)
        {
            case StructureState.Queued:
                sb.AppendLine("Queued: waits for a free build queue").Append("[X] cancel (full refund)");
                return sb.ToString();
            case StructureState.Building:
                sb.Append("Under construction: ").Append(Mathf.RoundToInt(surface.BuildFraction(s).ToFloat() * 100)).AppendLine("%").Append("[X] cancel (full refund)");
                return sb.ToString();
        }

        sb.AppendLine(s.Status switch
        {
            StructureStatus.NoUpkeep => "Idle: can't pay upkeep (" + Amounts(def.UpkeepPerMinute, true) + ")",
            StructureStatus.Depleted => "Idle: deposit used up",
            _ => "Working",
        });
        if (def.OutputPerMinute.Count > 0) sb.Append("Makes: ").AppendLine(Amounts(def.OutputPerMinute, true));
        if (def.RequiresDeposit && surface.Map.DepositResource[s.Tile] >= 0)
            sb.Append("Mining ").Append(data.Resources[surface.Map.DepositResource[s.Tile]].Name).Append(": +").Append(Hud.Whole(def.ExtractPerMinute))
              .Append("/min, ").Append(Hud.Whole(surface.Map.DepositAmount[s.Tile])).AppendLine(" left");
        if (def.UpkeepPerMinute.Count > 0) sb.Append("Upkeep: ").AppendLine(Amounts(def.UpkeepPerMinute, true));
        if (def.BuildRadius > 0) sb.Append("Build radius ").Append(def.BuildRadius).Append(" · ").Append(def.BuildQueues).Append(" build queue").AppendLine(def.BuildQueues == 1 ? "" : "s");
        return sb.ToString();
    }

    private string Amounts(IReadOnlyList<ResourceAmount> amounts, bool perMinute) =>
        string.Join(", ", amounts.Select(a => perMinute
            ? $"{Hud.Whole(a.Amount)} {_sim.Data.Resources[a.Resource].Name}/min"
            : $"{Hud.Whole(a.Amount)} {_sim.Data.Resources[a.Resource].Name}"));

    private static string Reason(PlacementProblem p) => p switch
    {
        PlacementProblem.NotSurveyed => "not surveyed yet (send a rover)",
        PlacementProblem.TerrainUnbuildable => "terrain isn't buildable",
        PlacementProblem.Occupied => "something is already here",
        PlacementProblem.OutOfRange => "too far from the colony",
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
        _yaw = Mathf.Lerp(_yaw, _yawSteps * Mathf.Pi / 2f, 1f - Mathf.Exp(-8f * delta));
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var offset = new Vector3(0, 0.82f, 0.57f).Rotated(Vector3.Up, _yaw) * _distance;
        _camera.LookAtFromPosition(_focus + offset, _focus, Vector3.Up);
    }
}
