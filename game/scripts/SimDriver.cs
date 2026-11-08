using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Ai;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Star-system view (scenes/System.tscn). Bridges Godot and the simulation: steps the sim at a fixed rate, turns mouse input into
/// commands, and draws ships with one MultiMesh per player (interpolated between ticks).
/// Nothing here changes game state directly; it only schedules commands.
/// </summary>
public partial class SimDriver : Node3D
{
    /// <summary>Lockstep input delay: local commands run this many ticks after they're issued.</summary>
    [Export] public int InputDelayTicks { get; set; } = 2;

    [Export] public ulong Seed { get; set; } = 1701;

    /// <summary>The player slot this machine plays (clamped to the match's players); every other slot gets a ChaseAi.</summary>
    [Export] public int LocalPlayerSlot { get; set; } = 0;

    /// <summary>The player this machine plays, set once the match is set up.</summary>
    public int LocalPlayer { get; private set; }
    private static readonly Color SelectedColor = new(1f, 1f, 0.55f);

    private Simulation _sim = null!;
    private readonly List<ChaseAi> _ais = new();
    private SystemView _system = null!;
    private Hud _hud = null!;
    private readonly Dictionary<int, Vector3> _previous = new();
    private readonly HashSet<int> _selected = new();
    private Body? _selectedBody;
    private readonly List<MultiMeshInstance3D> _fleets = new();
    private double _accumulator;

    private Camera3D _camera = null!;
    private Vector3 _cameraFocus = Vector3.Zero;
    private float _cameraDistance = 650f;

    public override void _Ready()
    {
        var data = GameDataLoader.Load(new DataSetup(Factions: MatchSetup.MvpFactions), out _, out _);
        foreach (var problem in data.Validate()) GD.PushWarning($"Data check: {problem}");

        _sim = MatchSetup.CreateMvpSkirmish(data, Seed);
        LocalPlayer = Mathf.Clamp(LocalPlayerSlot, 0, _sim.Players.Count - 1);
        for (var p = 0; p < _sim.Players.Count; p++)
            if (p != LocalPlayer) _ais.Add(new ChaseAi(p));
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNode<Hud>("Hud");

        _system = new SystemView();
        AddChild(_system);
        _system.Build(_sim);

        var mesh = BuildShipMesh();
        for (var player = 0; player < _sim.Players.Count; player++)
        {
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
            };
            multimesh.InstanceCount = _sim.Ships.Count(s => s.Owner == player);
            var instance = new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                MaterialOverride = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.6f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            AddChild(instance);
            _fleets.Add(instance);
        }

        foreach (var s in _sim.Ships) _previous[s.Id] = ToWorld(s.Position);
        FocusHome();
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        var tickSeconds = 1.0 / Simulation.TickRate;
        var steps = 0;
        while (_accumulator >= tickSeconds && steps < 5) // cap catch-up after a hitch
        {
            foreach (var s in _sim.Ships) _previous[s.Id] = ToWorld(s.Position);
            foreach (var ai in _ais)
                foreach (var c in ai.Think(_sim)) _sim.Schedule(_sim.Tick + InputDelayTicks, c);
            _sim.Step();
            _accumulator -= tickSeconds;
            steps++;
        }
        if (steps == 5) _accumulator = 0;

        PanCamera((float)delta);
        DrawShips((float)(_accumulator / tickSeconds));
        _system.Refresh();
        _hud.ShowResources(_sim, LocalPlayer);
        _hud.ShowBanner("");
        _hud.ShowInfo(_selectedBody != null ? DescribeBody(_selectedBody) : _selected.Count > 0 ? DescribeShips() : "");
        var seconds = _sim.Tick / Simulation.TickRate;
        _hud.ShowStatus(
            $"{_sim.Map?.Name}  ·  {seconds / 60}:{seconds % 60:00}  ·  state {_sim.StateHash():X16}\n" +
            "[Left-click] select ship or body  [Shift] add  [A] all ships  [Right-click] move\n" +
            "[WASD / arrows] pan  [Wheel] zoom  [H] home");
    }

    private string DescribeBody(Body body)
    {
        var data = _sim.Data;
        var cls = data.PlanetClass(body.ClassId);
        var sb = new StringBuilder();
        sb.AppendLine(body.Name);
        sb.AppendLine(cls.Name);
        sb.Append("Size ").Append(body.Size).Append("   Habitability ").Append(Hud.Whole(cls.Habitability * Fix64.FromInt(100))).AppendLine("%");
        if (body.ParentIndex >= 0) sb.Append("Moon of ").AppendLine(_sim.Bodies[body.ParentIndex].Name);
        var deposits = data.Resources.Where(r => body.Deposits[r.Index] > Fix64.Zero)
            .Select(r => $"{r.Name} {Hud.Whole(body.Deposits[r.Index])}").ToList();
        sb.Append("Deposits: ").AppendLine(deposits.Count == 0 ? "none" : string.Join(" · ", deposits));
        var colony = body.ColonyId == 0 ? null : _sim.FindColony(body.ColonyId);
        if (colony == null) return sb.Append("Unclaimed").ToString();
        sb.Append("Colony: ").Append(data.Faction(_sim.Players[colony.Owner].FactionId).Name)
            .AppendLine(colony.Owner == LocalPlayer ? " (you)" : "");
        foreach (var b in colony.Buildings)
        {
            var def = data.Building(b.DefId);
            sb.Append("  · ").Append(def.Name);
            if (def.OutputPerMinute.Count > 0)
                sb.Append("  ").Append(string.Join(", ", def.OutputPerMinute.Select(o => $"+{Hud.Whole(o.Amount)} {data.Resources[o.Resource].Name}"))).Append("/min");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private string DescribeShips()
    {
        var names = _selected.Select(_sim.FindShip).Where(s => s != null)
            .GroupBy(s => _sim.Data.ShipClass(s!.ClassKey).Name)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return $"{_selected.Count} ship{(_selected.Count == 1 ? "" : "s")} selected\n{string.Join(", ", names)}\nRight-click to move.";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                SelectAt(click.Position, click.ShiftPressed);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } click:
                OrderMove(click.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _cameraDistance = Mathf.Max(120f, _cameraDistance * 0.9f);
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _cameraDistance = Mathf.Min(2200f, _cameraDistance * 1.1f);
                UpdateCamera();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.A, Echo: false }:
                _selectedBody = null;
                _selected.Clear();
                foreach (var s in _sim.Ships.Where(s => s.Owner == LocalPlayer)) _selected.Add(s.Id);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.H, Echo: false }:
                FocusHome();
                break;
        }
    }

    /// <summary>Own ships take priority over bodies; clicking empty space clears the selection.</summary>
    private void SelectAt(Vector2 screen, bool additive)
    {
        if (!RaycastPlane(screen, out var point)) return;

        Ship? best = null;
        var bestDist = float.MaxValue;
        foreach (var s in _sim.Ships)
        {
            if (s.Owner != LocalPlayer) continue;
            var d = ToWorld(s.Position).DistanceTo(point);
            var pickRadius = 6f + s.HullSize * 3f;
            if (d < pickRadius && d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }

        if (best != null)
        {
            if (!additive) _selected.Clear();
            _selectedBody = null;
            _selected.Add(best.Id);
            return;
        }

        if (additive) return;
        _selected.Clear();
        _selectedBody = _system.BodyAt(point);
    }

    private void OrderMove(Vector2 screen)
    {
        if (_selected.Count == 0 || !RaycastPlane(screen, out var point)) return;
        // Float -> fixed happens here, once, on the issuing machine. The fixed value is what every peer runs.
        var target = new FixVec2(Fix64.FromDouble(point.X), Fix64.FromDouble(-point.Z));
        var ids = _selected.OrderBy(id => id).ToArray();
        _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveCommand(LocalPlayer, ids, target));
    }

    private void DrawShips(float alpha)
    {
        var index = new int[_fleets.Count];
        foreach (var s in _sim.Ships)
        {
            var mm = _fleets[s.Owner].Multimesh;
            var i = index[s.Owner]++;
            var pos = _previous[s.Id].Lerp(ToWorld(s.Position), alpha);
            var facing = new Vector3(s.Heading.X.ToFloat(), 0, -s.Heading.Y.ToFloat());
            var basis = facing.LengthSquared() > 0.0001f ? Basis.LookingAt(facing, Vector3.Up) : Basis.Identity;
            var scale = 3f + s.HullSize * 2.5f;
            mm.SetInstanceTransform(i, new Transform3D(basis.Scaled(new Vector3(scale, scale, scale)), pos));
            mm.SetInstanceColor(i, _selected.Contains(s.Id) ? SelectedColor : PlanetDriver.ColorOf(s.Owner));
        }
    }

    private void FocusHome()
    {
        var home = _sim.Colonies.FirstOrDefault(c => c.Owner == LocalPlayer);
        _cameraFocus = home == null ? Vector3.Zero : ToWorld(_sim.Bodies[home.BodyIndex].Position) * 0.6f;
        UpdateCamera();
    }

    private void PanCamera(float delta)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (Input.IsKeyPressed(Key.Left)) move.X -= 1; // A is select-all
        if (move == Vector3.Zero) return;
        _cameraFocus += move.Normalized() * _cameraDistance * 0.8f * delta;
        UpdateCamera();
    }

    private void UpdateCamera() =>
        _camera.LookAtFromPosition(_cameraFocus + new Vector3(0, 0.8f, 0.6f) * _cameraDistance, _cameraFocus, Vector3.Up);

    private bool RaycastPlane(Vector2 screen, out Vector3 point)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        point = Vector3.Zero;
        if (Mathf.Abs(dir.Y) < 1e-5f) return false;
        var t = -origin.Y / dir.Y;
        if (t < 0) return false;
        point = origin + dir * t;
        return true;
    }

    /// <summary>Sim (x, y) on the plane → Godot (x, 0, -y), so sim +y is "up" on screen.</summary>
    public static Vector3 ToWorld(FixVec2 p) => new(p.X.ToFloat(), 0, -p.Y.ToFloat());

    /// <summary>A small dart pointing along -Z (Godot's forward), placeholder until real models.</summary>
    private static Mesh BuildShipMesh()
    {
        var tip = new Vector3(0, 0, -1);
        var left = new Vector3(-0.6f, 0, 0.6f);
        var right = new Vector3(0.6f, 0, 0.6f);
        var top = new Vector3(0, 0.3f, 0.25f);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var v in new[] { tip, top, right, tip, left, top, left, right, top, tip, right, left })
            st.AddVertex(v);
        st.GenerateNormals();
        return st.Commit();
    }
}
