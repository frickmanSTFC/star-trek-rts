using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Planet-surface view (scenes/Planet.tscn, the main scene). The game opens in orbit over a generated
/// Class M world: read the scan hints, pick a landing site, watch the colony pod come down, then drive
/// survey rovers to find deposits. Like SimDriver, this only schedules commands; the sim decides.
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
    private LandingPhase _lastPhase = LandingPhase.InOrbit;

    private Vector3 _focus;
    private float _distance = 420f;
    private Vector3 _focusTarget;
    private float _distanceTarget = 420f;

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
                RightClick();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distanceTarget = Mathf.Max(50f, _distanceTarget * 0.88f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distanceTarget = Mathf.Min(600f, _distanceTarget * 1.12f);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.A, Echo: false } when Me.Phase == LandingPhase.Landed:
                _view.SelectedUnits.Clear();
                _view.SelectedUnits.UnionWith(_sim.Surface!.Units.Where(u => u.Owner == LocalPlayer).Select(u => u.Id));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.H, Echo: false }:
                if (Me.LandingTile >= 0) FlyTo(Me.LandingTile, 160f);
                else
                {
                    _focusTarget = _view.MapCentre();
                    _distanceTarget = 420f;
                }
                break;
        }
    }

    private void LeftClick(bool additive)
    {
        if (_hoverTile < 0) return;
        if (Me.Phase == LandingPhase.InOrbit)
        {
            if (_sim.Surface!.CanLand(LocalPlayer, _hoverTile))
                _sim.Schedule(_sim.Tick + InputDelayTicks, new LandColonyCommand(LocalPlayer, _hoverTile));
            return;
        }

        var unit = _sim.Surface!.Units.FirstOrDefault(u => u.Owner == LocalPlayer && u.Tile == _hoverTile);
        if (unit != null)
        {
            if (!additive) _view.SelectedUnits.Clear();
            _view.SelectedUnits.Add(unit.Id);
            _selectedTile = -1;
            return;
        }
        if (!additive) _view.SelectedUnits.Clear();
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

        var colour = new Color(1, 1, 1, 0.3f);
        if (_hoverTile >= 0 && Me.Phase == LandingPhase.InOrbit)
            colour = _sim.Surface!.CanLand(LocalPlayer, _hoverTile) ? new Color(0.3f, 1f, 0.4f, 0.55f) : new Color(1f, 0.3f, 0.25f, 0.45f);
        _view.SetHover(Me.Phase == LandingPhase.Descending ? -1 : _hoverTile, colour);
    }

    private void UpdateHud()
    {
        _hud.ShowResources(_sim, LocalPlayer);
        var surface = _sim.Surface!;
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
                var known = Enumerable.Range(0, surface.Map.Grid.Count).Count(t => Me.Surveyed[t] && surface.Map.DepositResource[t] >= 0);
                _hud.ShowBanner(_sim.Tick - Me.LandsAtTick < Simulation.TickRate * 6
                    ? "Colony founded. Right-click to send the survey rovers out."
                    : known == 0 ? "" : $"{known} deposit{(known == 1 ? "" : "s")} found");
                break;
        }

        var info = _selectedTile >= 0 ? DescribeTile(_selectedTile) : _hoverTile >= 0 ? DescribeTile(_hoverTile) : "";
        if (_view.SelectedUnits.Count > 0)
            info = $"{_view.SelectedUnits.Count} survey rover{(_view.SelectedUnits.Count == 1 ? "" : "s")} selected · right-click a tile to drive there\n" + info;
        _hud.ShowInfo(info.TrimEnd());

        var seconds = _sim.Tick / Simulation.TickRate;
        _hud.ShowStatus(
            $"{PlanetName} (seed {Seed})  ·  {seconds / 60}:{seconds % 60:00}  ·  state {_sim.StateHash():X16}\n" +
            (Me.Phase == LandingPhase.InOrbit
                ? "[Left-click] land here  [WASD / arrows] pan  [Wheel] zoom"
                : "[Left-click] select rover or tile  [Shift] add  [A] all rovers  [Right-click] drive\n[WASD / arrows] pan  [Wheel] zoom  [H] colony"));
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

    // ---------------------------------------------------------------- camera

    private void FlyTo(int tile, float distance)
    {
        _focusTarget = _view.TileCentre(tile) with { Y = 0 };
        _distanceTarget = distance;
    }

    private void MoveCamera(float delta)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (Input.IsKeyPressed(Key.Left) || (Input.IsKeyPressed(Key.A) && Me.Phase != LandingPhase.Landed)) move.X -= 1;
        if (move != Vector3.Zero) _focusTarget += move.Normalized() * _distance * 0.9f * delta;

        var k = 1f - Mathf.Exp(-4f * delta);
        _focus = _focus.Lerp(_focusTarget, k);
        _distance = Mathf.Lerp(_distance, _distanceTarget, k);
        UpdateCamera();
    }

    private void UpdateCamera() =>
        _camera.LookAtFromPosition(_focus + new Vector3(0, 0.82f, 0.57f) * _distance, _focus, Vector3.Up);
}
