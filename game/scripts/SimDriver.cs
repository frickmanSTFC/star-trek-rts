using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Ai;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Bridges Godot and the simulation: steps the sim at a fixed rate, turns mouse input into
/// commands, and draws ships with one MultiMesh per player (interpolated between ticks).
/// Nothing here changes game state directly; it only schedules commands.
/// </summary>
public partial class SimDriver : Node3D
{
    /// <summary>Lockstep input delay: local commands run this many ticks after they're issued.</summary>
    [Export] public int InputDelayTicks { get; set; } = 2;

    [Export] public ulong Seed { get; set; } = 1701;

    private const int LocalPlayer = 0;
    private static readonly Color[] PlayerColors = { new(0.45f, 0.7f, 1f), new(0.95f, 0.6f, 0.25f) };
    private static readonly Color SelectedColor = new(1f, 1f, 0.55f);

    private Simulation _sim = null!;
    private ChaseAi _ai = null!;
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
        var dataDir = GameData.FindDataDirectory(ProjectSettings.GlobalizePath("res://"));
        var data = GameData.Load(dataDir);
        foreach (var problem in data.Validate()) GD.PushWarning($"Data check: {problem}");

        _sim = MatchSetup.CreateMvpSkirmish(data, Seed);
        _ai = new ChaseAi(1);
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNode<Hud>("Hud");

        _system = new SystemView();
        AddChild(_system);
        _system.Build(_sim, PlayerColors);

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
            foreach (var c in _ai.Think(_sim)) _sim.Schedule(_sim.Tick + InputDelayTicks, c);
            _sim.Step();
            _accumulator -= tickSeconds;
            steps++;
        }
        if (steps == 5) _accumulator = 0;

        PanCamera((float)delta);
        DrawShips((float)(_accumulator / tickSeconds));
        _system.Refresh();
        _hud.Show(_sim, LocalPlayer, _selectedBody, _selected);
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
            mm.SetInstanceColor(i, _selected.Contains(s.Id) ? SelectedColor : PlayerColors[s.Owner % PlayerColors.Length]);
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
