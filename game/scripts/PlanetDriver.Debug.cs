using System.Diagnostics;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The F8 debug overlay's surface / orbit side (owner 2026-11-11: "we need to add framerate and other debug options to the
// game that can be turned on/off"; DebugOptions, DebugOverlay). Render-only and read-only: nothing here schedules a
// command or changes sim state (StateHash only reads it).
//
// Hooks (one line each): SetupDebug() at the end of _Ready; DebugStep() in place of _sim.Step() in _Process (times the
// stepping while the overlay is on, otherwise just steps). The overlay calls DebugFrame every frame while it is on (the
// world draws) and the Debug* text builders at 4 Hz, so nothing here runs while it is off.
//
// The F7 frame-time readout (PlanetDriver.Graphics.cs) is folded in: while the overlay shows Performance the readout
// hides and the Performance card carries its compare line; with Performance off the cards start below the readout.
public partial class PlanetDriver : IDebugHost
{
    private DebugOverlay _debug = null!;

    // Sim step timing (only while the overlay is on).
    private bool _debugTiming;
    private long _debugStepTime, _debugStepMax;
    private int _debugSteps;
    private double _debugRateAt = -1;
    private int _debugRateSteps;
    private float _debugRate = -1f, _debugStepAvg, _debugStepPeak;
    private int _debugFrames;

    // State hash, every 2 s while the Simulation card is shown.
    private double _debugHashAt = -10;
    private string _debugHash = "";

    // World draws: built while their option is on (with the overlay), freed when off.
    private Node3D? _debugWorld;
    private MeshInstance3D? _debugGrid, _debugPaths, _debugRings;
    private ImmediateMesh? _debugPathMesh, _debugRingMesh;
    private double _debugRingsAt = -10;

    /// <summary>The overlay (end of _Ready). Its own CanvasLayer 7, also handles F8 / Shift+F8 itself.</summary>
    private void SetupDebug()
    {
        _debug = new DebugOverlay { Name = "DebugOverlay", Host = this };
        AddChild(_debug);
    }

    /// <summary>One sim step, timed while the overlay is on.</summary>
    private void DebugStep()
    {
        if (!_debugTiming)
        {
            _sim.Step();
            return;
        }
        var t = Stopwatch.GetTimestamp();
        _sim.Step();
        var d = Stopwatch.GetTimestamp() - t;
        _debugStepTime += d;
        _debugStepMax = System.Math.Max(_debugStepMax, d);
        _debugSteps++;
        _debugRateSteps++;
    }

    /// <summary>The F7 readout hides while the overlay's Performance card shows the same numbers.</summary>
    private static bool DebugCoversFrameTime => DebugOptions.Shows(DebugOptions.Performance);

    // ---------------------------------------------------------------- IDebugHost

    Rect2 IDebugHost.DebugArea(Vector2 screen)
    {
        Rect2 area;
        if (_inOrbit && _helm.ViewRect.Size.X > 64f && _helm.ViewRect.Size.Y > 64f)
        {
            var view = _helm.ViewRect;
            area = new Rect2(view.Position + new Vector2(10, 10), view.Size * new Vector2(0.62f, 1f) - new Vector2(0, 20));
        }
        else area = new Rect2(12, PlanetHud.TopHeight + 10, screen.X * 0.62f, screen.Y - PlanetHud.TopHeight - PlanetHud.BottomHeight - 20);
        // The F7 readout stays when Performance is off: start below it.
        if (_gfxFps.Visible)
        {
            var below = _gfxFps.Position.Y + _gfxFps.Size.Y + 6;
            if (below > area.Position.Y) area = new Rect2(area.Position.X, below, area.Size.X, Mathf.Max(120f, area.End.Y - below));
        }
        return area;
    }

    void IDebugHost.DebugPlaced(bool shown, float right) => _hud.SetFeedLeft(shown ? right + 10f : 0f);

    void IDebugHost.DebugStop()
    {
        _debugTiming = false;
        FreeGrid();
        FreePaths();
        FreeRings();
        if (_debugWorld != null)
        {
            _debugWorld.QueueFree();
            _debugWorld = null;
        }
    }

    void IDebugHost.DebugFrame(float delta)
    {
        _debugTiming = true;
        _debugFrames++;
        var surface = !_inOrbit && IsInsideTree();
        if (DebugOptions.HexGrid) EnsureGrid();
        else FreeGrid();
        if (DebugOptions.Paths) DrawPaths();
        else FreePaths();
        if (DebugOptions.Footprints)
        {
            if (_debugRings == null || _clock - _debugRingsAt > 0.5) DrawRings();
        }
        else FreeRings();
        if (_debugWorld != null) _debugWorld.Visible = surface;
    }

    void IDebugHost.DebugPerformance(StringBuilder sb)
    {
        // Sim stepping: per step and per frame, ticks a second vs the 15 Hz the game wants (× speed).
        var perMs = 1000.0 / Stopwatch.Frequency;
        if (_debugSteps > 0)
        {
            _debugStepAvg = (float)(_debugStepTime * perMs / _debugSteps);
            _debugStepPeak = (float)(_debugStepMax * perMs);
        }
        var perFrame = _debugFrames > 0 ? (float)(_debugStepTime * perMs / _debugFrames) : 0f;
        _debugStepTime = _debugStepMax = 0;
        _debugSteps = 0;
        _debugFrames = 0;
        if (_debugRateAt < 0) _debugRateAt = _clock;
        if (_clock - _debugRateAt >= 1.0)
        {
            _debugRate = (float)(_debugRateSteps / (_clock - _debugRateAt));
            _debugRateSteps = 0;
            _debugRateAt = _clock;
        }
        var speed = _test?.Speed ?? 1.0;
        var paused = (_test?.Paused ?? false) || GetTree().Paused;
        var want = paused ? 0.0 : Simulation.TickRate * speed;
        sb.Append($"sim step {_debugStepAvg,5:0.00} ms  max {_debugStepPeak,5:0.00} · {perFrame:0.00} ms/frame\n");
        sb.Append(_debugRate >= 0f ? $"ticks/s {_debugRate,5:0.0} of {want:0.#}" : "ticks/s  …");
        if (_debugRate >= 0f && want > 0 && _debugRate < want * 0.9) sb.Append("  (falling behind)");
        sb.Append('\n');
        if (GraphicsQuality.CompareClassic || GraphicsQuality.ShowFps)
            sb.Append($"F7 {GraphicsQuality.ModeName}{(_gfxCompareLine.Length > 0 ? " · " + _gfxCompareLine : "")}\n");
    }

    void IDebugHost.DebugSimulation(StringBuilder sb)
    {
        var tick = _sim.Tick;
        var seconds = tick / Simulation.TickRate;
        var speed = _test?.Speed ?? 1.0;
        var paused = _test?.Paused ?? false;
        sb.Append($"tick {tick:N0} · {seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00} game time\n");
        sb.Append($"speed ×{speed:0.##}{(paused ? " · PAUSED (test panel)" : "")}{(GetTree().Paused ? " · pause card" : "")} · {(_inOrbit ? "orbit" : "surface")} · {Me.Phase}\n");
        var units = Surface.Units;
        var structures = Surface.Structures;
        var ownUnits = 0;
        foreach (var u in units)
            if (u.Owner == LocalPlayer) ownUnits++;
        var ownStructures = 0;
        foreach (var s in structures)
            if (s.Owner == LocalPlayer) ownStructures++;
        sb.Append($"players {_sim.Players.Count} · units {units.Count} (own {ownUnits}) · structures {structures.Count} (own {ownStructures})\n");
        sb.Append($"refused commands {_sim.RefusedCommands}\n");
        if (_sim.LastRefusal is { } last) sb.Append("  last: ").Append(Clip(last, 96)).Append('\n');
        sb.Append($"test edits {_sim.DebugEdits}{(_test?.LocalEdits == true ? " + render-only edits" : "")}\n");
        if (_clock - _debugHashAt >= 2.0)
        {
            _debugHashAt = _clock;
            var t = Stopwatch.GetTimestamp();
            var hash = _sim.StateHash();
            var ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            _debugHash = $"state hash {hash:X16} ({ms:0.0} ms, every 2 s)";
        }
        sb.Append(_debugHash);
    }

    void IDebugHost.DebugCursor(StringBuilder sb)
    {
        Vector3 eye;
        if (_inOrbit)
        {
            eye = _orbit.Camera.GlobalPosition;
            sb.Append($"orbit camera ({eye.X:0}, {eye.Y:0}, {eye.Z:0}) · {_orbit.Display}\n");
        }
        else
        {
            eye = _camera.GlobalPosition;
            sb.Append($"camera ({eye.X:0.0}, {eye.Y:0.0}, {eye.Z:0.0})\n");
            sb.Append($"focus ({_focus.X:0.0}, {_focus.Z:0.0}) · distance {_distance:0.0} · yaw {Mathf.RadToDeg(_yaw):0}°\n");
        }
        var tile = _inOrbit ? _orbitHover : _hoverTile;
        var last = tile < 0 && !_inOrbit && _cursorTile >= 0;
        if (last) tile = _cursorTile;
        var map = Surface.Map;
        if (tile < 0 || tile >= map.Grid.Count)
        {
            sb.Append(_mouseOverUi ? "mouse over the UI" : "no tile under the mouse");
            return;
        }
        var terrain = map.Terrain(tile);
        sb.Append($"tile {tile}{(last ? " (last)" : "")} · col {map.Grid.Column(tile)} row {map.Grid.Row(tile)} · {terrain.Name}\n");
        sb.Append($"  move {terrain.MoveCost}{(terrain.Passable ? "" : " (impassable)")}{(terrain.Buildable ? " · buildable" : "")}{(map.IsWater(tile) ? " · water" : "")}\n");
        sb.Append($"elevation {map.Elevation[tile].ToFloat():0.000} · ground y {_view.TopY(tile):0.0}\n");
        var fog = _view.FogOff ? "fog off (test panel)" : Surface.IsVisible(LocalPlayer, tile) ? "in sight" : Surface.IsExplored(LocalPlayer, tile) ? "explored (remembered)" : "unexplored";
        sb.Append($"fog: {fog} · comms {(Surface.CommsCovered(LocalPlayer, tile) ? "covered" : "not covered")}\n");
        var here = Surface.StructureAt(tile);
        var vehicles = 0;
        foreach (var u in Surface.Units)
            if (u.Tile == tile) vehicles++;
        sb.Append($"block radius {Surface.BlockRadius(tile).ToFloat():0.0}");
        if (here != null) sb.Append($" · {_data.Building(here.DefId).Name} #{here.Id}");
        if (vehicles > 0) sb.Append($" · {vehicles} vehicle{(vehicles == 1 ? "" : "s")}");
        if (map.DepositResource[tile] >= 0) sb.Append($"\ndeposit {_data.Resources[map.DepositResource[tile]].Name} {map.DepositAmount[tile].ToFloat():0}");
    }

    void IDebugHost.DebugSelection(StringBuilder sb)
    {
        var (unit, structure, count) = ((ITestHost)this).Selection();
        if (unit == null && structure == null)
        {
            sb.Append(_selectedTile >= 0 ? $"tile {_selectedTile} selected (no vehicle or building)" : "nothing selected");
            return;
        }
        if (unit != null) UnitLines(sb, unit, count);
        else StructureLines(sb, structure!);
    }

    private void UnitLines(StringBuilder sb, SurfaceUnit u, int count)
    {
        sb.Append($"unit #{u.Id} {u.DefId}{(count > 1 ? $" (first of {count})" : "")}\n");
        sb.Append($"  {Clip(UnitName(u), 40)} · owner {u.Owner}\n");
        sb.Append($"tile {u.Tile} · pos ({u.Pos.X.ToFloat():0.0}, {u.Pos.Y.ToFloat():0.0}) · {u.Mode} {u.ModeTicks}t\n");
        var next = u.Path.Count > 1 ? $" (next {u.Path[1]}, goal {u.Path[^1]})" : u.Path.Count == 1 ? $" (goal {u.Path[0]})" : "";
        sb.Append($"path {u.Path.Count} tiles{next} · route {u.Route.Count} pts\n");
        if (u.WaitTicks > 0 || u.PushTicks > 0 || u.BlockedBy != 0) sb.Append($"wait {u.WaitTicks}t · push {u.PushTicks}t · blocked by #{u.BlockedBy}\n");
        sb.Append($"integrity {u.Integrity.ToFloat():0.0}{(u.Broken ? " · BROKEN" : "")}{(u.JuryRigged ? " · jury-rigged" : "")}");
        if (u.Breakdown >= 0) sb.Append($" · breakdown {u.Breakdown}");
        if (u.RepairTicks > 0 || u.RepairWaiting) sb.Append($" · repair {u.RepairTicks}t{(u.RepairWaiting ? " waiting" : "")}");
        sb.Append('\n');
        sb.Append($"speed {u.SpeedPerTick.ToFloat() * Simulation.TickRate:0.00}/s · survey {u.SurveyRadius}");
        if (u.FlatTyres != 0 || u.Spares > 0) sb.Append($" · flats 0x{u.FlatTyres:X} · spares {u.Spares}{(u.TyreChangeTicks > 0 ? $" · changing {u.TyreChangeTicks}t" : "")}");
        sb.Append('\n');
        if (u.HopperResource >= 0 || u.Hopper > Fix64.Zero || u.MiningStatus != default)
        {
            var res = u.HopperResource >= 0 && u.HopperResource < _data.Resources.Count ? _data.Resources[u.HopperResource].Name : "–";
            sb.Append($"hopper {u.Hopper.ToFloat():0.0} {res} · mining {u.MiningStatus}\n");
        }
        if (u.FoundTile >= 0) sb.Append($"founding at tile {u.FoundTile}\n");
        if (Surface.IsAerial(u))
        {
            var d = Surface.DroneOf(u);
            sb.Append($"drone {d.State} · battery {d.Battery.ToFloat() * 100f:0}% · target {d.Target} · pad #{d.Pad} bay {d.Bay}");
            if (d.ReturnReason.Length > 0) sb.Append($" · {d.ReturnReason}");
            sb.Append('\n');
        }
    }

    private void StructureLines(StringBuilder sb, SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        var footprint = _data.FootprintOf(s.DefId);
        sb.Append($"building #{s.Id} {s.DefId} · owner {s.Owner} · base #{s.BaseId}\n");
        sb.Append($"  {Clip(def.Name, 40)}\n");
        sb.Append($"tile {s.Tile} · facing {s.Facing} · {s.State} · {s.Status}\n");
        sb.Append($"level {s.Level}");
        if (s.UpgradingTo != null || s.UpgradingLevel > 0) sb.Append($" → {(s.UpgradingTo ?? $"Mk {s.UpgradingLevel}")} ({s.UpgradeTicks}t)");
        sb.Append($" · condition {s.Condition.ToFloat():0.0}{(s.Broken ? " · BROKEN" : "")}{(s.Paused ? " · paused" : "")}{(s.NoWear ? " · no wear" : "")}{(s.JuryRigged ? " · jury-rigged" : "")}\n");
        if (s.State != StructureState.Complete) sb.Append($"build ticks {s.BuildTicks}{(s.Assembling ? $" · ready at tick {s.ReadyTick}" : "")}\n");
        if (s.Production.Count > 0) sb.Append($"production {s.Production.Count} queued · {s.ProductionProgress.ToFloat() * 100f:0}% · next {s.Production[0]}\n");
        if (s.Fabrication.Count > 0) sb.Append($"fabrication {s.Fabrication.Count} queued\n");
        if (s.CrewJob != 0) sb.Append($"crew job #{s.CrewJob} · {s.CrewPhase} {s.CrewTicks}t\n");
        if (s.MaintenanceOrdered || s.AutoRepairing) sb.Append($"maintenance {(s.MaintenanceOrdered ? "ordered" : "")}{(s.AutoRepairing ? " auto" : "")}{(s.MaintenanceWaiting ? " waiting" : "")}\n");
        sb.Append($"footprint {footprint.Radius.ToFloat():0.0} · lane ring {(footprint.Radius + _data.Lanes.Clearance).ToFloat() * 1.04f:0.0}");
        if (Surface.AccessPoint(s.Tile) is { } a) sb.Append($" · access ({a.X.ToFloat():0.0}, {a.Y.ToFloat():0.0})");
        if (s.WorkSpeed != Fix64.One) sb.Append($" · work ×{s.WorkSpeed.ToFloat():0.#}");
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // ---------------------------------------------------------------- world draws

    private Node3D DebugWorld()
    {
        if (_debugWorld != null) return _debugWorld;
        // A child of the driver, not PlanetView: the outpost feed moves everything under PlanetView to its own layer.
        _debugWorld = new Node3D { Name = "DebugWorld" };
        AddChild(_debugWorld);
        return _debugWorld;
    }

    private static StandardMaterial3D LineMaterial(bool depthTest) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        VertexColorUseAsAlbedo = true,
        NoDepthTest = !depthTest,
        DisableFog = true,
        DisableReceiveShadows = true,
        RenderPriority = 2,
    };

    private MeshInstance3D LineNode(string name, Mesh mesh, bool depthTest)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = LineMaterial(depthTest),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 4f,
        };
        DebugWorld().AddChild(node);
        return node;
    }

    /// <summary>Every tile's six edges, two segments each following the ground, 0.4 above it; built once (~0.1–0.3 s on
    /// the 128×88 map) and kept while the option is on.</summary>
    private void EnsureGrid()
    {
        if (_debugGrid != null) return;
        var grid = Surface.Map.Grid;
        var radius = PlanetView.HexWidth / Mathf.Sqrt(3f);
        var verts = new Vector3[grid.Count * 6 * 4];
        var colours = new Color[verts.Length];
        var corner = new Vector3[6];
        var line = new Color(0.55f, 0.9f, 1f, 0.32f);
        var n = 0;
        for (var t = 0; t < grid.Count; t++)
        {
            var c = _view.TileCentre(t);
            for (var k = 0; k < 6; k++)
            {
                var a = Mathf.DegToRad(30f + 60f * k);
                var p = new Vector3(c.X + radius * Mathf.Cos(a), 0, c.Z + radius * Mathf.Sin(a));
                p.Y = _view.GroundAt(p) + 0.4f;
                corner[k] = p;
            }
            for (var k = 0; k < 6; k++)
            {
                var p0 = corner[k];
                var p1 = corner[(k + 1) % 6];
                var mid = (p0 + p1) * 0.5f;
                mid.Y = _view.GroundAt(mid) + 0.4f;
                verts[n] = p0;
                verts[n + 1] = mid;
                verts[n + 2] = mid;
                verts[n + 3] = p1;
                for (var j = 0; j < 4; j++) colours[n + j] = line;
                n += 4;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        _debugGrid = LineNode("HexGrid", mesh, depthTest: true);
    }

    private void FreeGrid()
    {
        if (_debugGrid == null) return;
        _debugGrid.QueueFree();
        _debugGrid = null;
    }

    /// <summary>Every frame: each moving vehicle's current route points (bright, its owner's colour) then the tile
    /// centres still to enter (dim), drawn through buildings (no depth test).</summary>
    private void DrawPaths()
    {
        if (_debugPaths == null)
        {
            _debugPathMesh = new ImmediateMesh();
            _debugPaths = LineNode("VehiclePaths", _debugPathMesh, depthTest: false);
        }
        var mesh = _debugPathMesh!;
        mesh.ClearSurfaces();
        if (_inOrbit) return;
        var alpha = (float)(_accumulator * Simulation.TickRate);
        var begun = false;
        foreach (var u in Surface.Units)
        {
            if (u.Path.Count == 0 && u.Route.Count == 0) continue;
            if (!begun)
            {
                mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                begun = true;
            }
            var colour = ColorOf(u.Owner);
            var from = _view.UnitPos(u, alpha) + Vector3.Up * 0.8f;
            mesh.SurfaceSetColor(colour);
            foreach (var r in u.Route)
            {
                var p = new Vector3(r.X.ToFloat(), 0f, r.Y.ToFloat());
                p.Y = _view.GroundAt(p) + 0.8f;
                mesh.SurfaceAddVertex(from);
                mesh.SurfaceAddVertex(p);
                from = p;
            }
            var dim = new Color(colour, 0.55f);
            mesh.SurfaceSetColor(dim);
            for (var i = 0; i < u.Path.Count; i++)
            {
                if (u.Path[i] == u.Tile) continue; // the tile it is on (kept in Path until it parks)
                var p = _view.TileCentre(u.Path[i]) + Vector3.Up * 0.8f;
                mesh.SurfaceAddVertex(from);
                mesh.SurfaceAddVertex(p);
                from = p;
            }
            // A small cross at the end.
            mesh.SurfaceSetColor(colour);
            mesh.SurfaceAddVertex(from + new Vector3(-1.2f, 0, -1.2f));
            mesh.SurfaceAddVertex(from + new Vector3(1.2f, 0, 1.2f));
            mesh.SurfaceAddVertex(from + new Vector3(-1.2f, 0, 1.2f));
            mesh.SurfaceAddVertex(from + new Vector3(1.2f, 0, -1.2f));
        }
        if (begun) mesh.SurfaceEnd();
    }

    private void FreePaths()
    {
        if (_debugPaths == null) return;
        _debugPaths.QueueFree();
        _debugPaths = null;
        _debugPathMesh = null;
    }

    /// <summary>Twice a second: every structure's footprint (solid, its owner's colour) and lane ring (footprint +
    /// clearance, ×1.04: where vehicles bend round it; dim), its access point (cross), and deployed / unfolding
    /// vehicles' footprints (lanes.cs BlockRadius).</summary>
    private void DrawRings()
    {
        if (_debugRings == null)
        {
            _debugRingMesh = new ImmediateMesh();
            _debugRings = LineNode("Footprints", _debugRingMesh, depthTest: true);
        }
        _debugRingsAt = _clock;
        var mesh = _debugRingMesh!;
        mesh.ClearSurfaces();
        if (_inOrbit) return;
        var clearance = _data.Lanes.Clearance.ToFloat();
        var begun = false;
        void Begin()
        {
            if (begun) return;
            mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            begun = true;
        }
        foreach (var s in Surface.Structures)
        {
            Begin();
            var centre = _view.TileCentre(s.Tile);
            var footprint = _data.FootprintOf(s.DefId);
            var r = footprint.Radius.ToFloat();
            var colour = ColorOf(s.Owner);
            Ring(mesh, centre, r, colour);
            Ring(mesh, centre, (r + clearance) * 1.04f, new Color(colour, 0.4f));
            if (Surface.AccessPoint(s.Tile) is { } a)
            {
                var p = new Vector3(a.X.ToFloat(), 0, a.Y.ToFloat());
                p.Y = _view.GroundAt(p) + 0.6f;
                mesh.SurfaceSetColor(new Color(1f, 1f, 1f, 0.85f));
                mesh.SurfaceAddVertex(p + new Vector3(-1f, 0, 0));
                mesh.SurfaceAddVertex(p + new Vector3(1f, 0, 0));
                mesh.SurfaceAddVertex(p + new Vector3(0, 0, -1f));
                mesh.SurfaceAddVertex(p + new Vector3(0, 0, 1f));
            }
        }
        foreach (var u in Surface.Units)
        {
            if (u.Mode == UnitMode.Mobile || Surface.IsAerial(u)) continue;
            Begin();
            var r = _data.DeployedFootprintOf(u.DefId).ToFloat();
            var centre = _view.TileCentre(u.Tile);
            Ring(mesh, centre, r, new Color(1f, 0.85f, 0.35f, 0.9f));
            Ring(mesh, centre, (r + clearance) * 1.04f, new Color(1f, 0.85f, 0.35f, 0.35f));
        }
        if (begun) mesh.SurfaceEnd();
    }

    private void Ring(ImmediateMesh mesh, Vector3 centre, float radius, Color colour)
    {
        const int Segments = 32;
        mesh.SurfaceSetColor(colour);
        var prev = Vector3.Zero;
        for (var i = 0; i <= Segments; i++)
        {
            var a = Mathf.Tau * i / Segments;
            var p = new Vector3(centre.X + radius * Mathf.Cos(a), 0, centre.Z + radius * Mathf.Sin(a));
            p.Y = _view.GroundAt(p) + 0.6f;
            if (i > 0)
            {
                mesh.SurfaceAddVertex(prev);
                mesh.SurfaceAddVertex(p);
            }
            prev = p;
        }
    }

    private void FreeRings()
    {
        if (_debugRings == null) return;
        _debugRings.QueueFree();
        _debugRings = null;
        _debugRingMesh = null;
    }
}
