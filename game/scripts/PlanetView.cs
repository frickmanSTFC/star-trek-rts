using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Draws a planet surface: hex terrain prisms, surveyed deposits, orbital-scan hints per region,
/// the hover marker, the colony pod's descent, structures and survey rovers. Render-only.
/// </summary>
public partial class PlanetView : Node3D
{
    /// <summary>World width of one hex (flat side to flat side).</summary>
    public const float HexWidth = 10f;

    private static readonly float HexRadius = HexWidth / Mathf.Sqrt(3f);

    private Simulation _sim = null!;
    private PlanetSurface _surface = null!;
    private int _localPlayer;

    private MultiMesh _tiles = null!;
    private MultiMesh _deposits = null!;
    private MultiMesh _rovers = null!;
    private readonly List<Label3D> _hintLabels = new();
    private MeshInstance3D _hover = null!;
    private StandardMaterial3D _hoverMaterial = null!;
    private Node3D _pod = null!;
    private sealed class StructureVisual
    {
        public required Node3D Model;
        public required Label3D Label;
        public required StructureState State;
    }

    private readonly Dictionary<int, StructureVisual> _structures = new();
    private Node3D? _ghost;
    private string? _ghostId;
    private StandardMaterial3D _ghostMaterial = null!;
    private StandardMaterial3D _hologram = null!;
    private MeshInstance3D _rangeRing = null!;
    private int _surveyedCount = -1;
    private LandingPhase _lastPhase = (LandingPhase)(-1);

    /// <summary>Placeholder deposit colours until real art.</summary>
    private static readonly Dictionary<string, Color> DepositColors = new()
    {
        ["duranium"] = new(0.75f, 0.78f, 0.85f),
        ["tritanium"] = new(0.55f, 0.7f, 1f),
        ["deuterium"] = new(0.4f, 0.95f, 1f),
        ["dilithium"] = new(1f, 0.45f, 0.85f),
        ["topaline"] = new(1f, 0.85f, 0.3f),
        ["cormaline"] = new(0.5f, 1f, 0.55f),
    };

    public void Build(Simulation sim, int localPlayer)
    {
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;
        BuildTiles();
        BuildHints();
        _deposits = AddMultiMesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.4f, Height = 3.2f, RadialSegments = 5 }, true, emissive: true);
        _rovers = AddMultiMesh(new BoxMesh { Size = new Vector3(2.4f, 1.4f, 3.4f) }, true, emissive: false);

        _hoverMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1, 1, 1, 0.35f),
        };
        _hover = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = HexRadius, BottomRadius = HexRadius, Height = 0.4f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = _hoverMaterial,
            Visible = false,
        };
        AddChild(_hover);

        _ghostMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.45f),
        };
        _hologram = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.45f, 0.8f, 1f, 0.35f),
        };
        _rangeRing = new MeshInstance3D { Visible = false };
        AddChild(_rangeRing);

        _pod = BuildPod();
        _pod.Visible = false;
        AddChild(_pod);
        Refresh(0f);
    }

    // ---------------------------------------------------------------- coordinates

    public Vector3 TileCentre(int tile)
    {
        var c = _surface.Map.Grid.Centre(tile);
        return new Vector3(c.X.ToFloat() * HexWidth, TopY(tile), c.Y.ToFloat() * HexWidth);
    }

    /// <summary>Height of a tile's top surface.</summary>
    public float TopY(int tile)
    {
        var t = _surface.Map.Terrain(tile);
        if (t.Height == 0) return 0.4f;
        return 0.8f + t.Height * 1.4f + _surface.Map.Elevation[tile].ToFloat() * 1.2f;
    }

    public Vector3 MapCentre()
    {
        var g = _surface.Map.Grid;
        return new Vector3((g.Width - 0.5f) * HexWidth / 2f, 0, (g.Height - 1) * 0.866f * HexWidth / 2f);
    }

    /// <summary>The tile under a world point (nearest centre), or -1 off the map.</summary>
    public int TileAt(Vector3 p)
    {
        var g = _surface.Map.Grid;
        var row = Mathf.RoundToInt(p.Z / (0.866f * HexWidth));
        var col = Mathf.RoundToInt(p.X / HexWidth - ((row & 1) == 1 ? 0.5f : 0f));
        var best = -1;
        var bestDist = float.MaxValue;
        for (var r = row - 1; r <= row + 1; r++)
            for (var c = col - 1; c <= col + 1; c++)
            {
                if (!g.InBounds(c, r)) continue;
                var i = g.Index(c, r);
                var centre = TileCentre(i);
                var d = new Vector2(centre.X - p.X, centre.Z - p.Z).LengthSquared();
                if (d < bestDist)
                {
                    best = i;
                    bestDist = d;
                }
            }
        return bestDist <= HexRadius * HexRadius ? best : -1;
    }

    // ---------------------------------------------------------------- per frame

    /// <param name="alpha">Fraction of the way to the next sim tick (for smooth motion).</param>
    public void Refresh(float alpha)
    {
        var me = _surface.Players[_localPlayer];
        var surveyed = 0;
        foreach (var s in me.Surveyed)
            if (s) surveyed++;
        if (surveyed != _surveyedCount || me.Phase != _lastPhase)
        {
            _surveyedCount = surveyed;
            _lastPhase = me.Phase;
            ColourTiles(me);
            PlaceDeposits(me);
            foreach (var l in _hintLabels) l.Visible = me.Phase == LandingPhase.InOrbit;
        }

        UpdatePod(me, alpha);
        UpdateStructures((float)GetProcessDeltaTime());
        UpdateRovers(alpha);
    }

    public void SetHover(int tile, Color color)
    {
        _hover.Visible = tile >= 0;
        if (tile < 0) return;
        _hover.Position = TileCentre(tile) + new Vector3(0, 0.25f, 0);
        _hoverMaterial.AlbedoColor = color;
    }

    /// <summary>Rover ids the view should draw highlighted.</summary>
    public HashSet<int> SelectedUnits { get; } = new();

    // ---------------------------------------------------------------- building

    private void BuildTiles()
    {
        var map = _surface.Map;
        _tiles = AddMultiMesh(new CylinderMesh { TopRadius = HexRadius * 0.97f, BottomRadius = HexRadius * 0.97f, Height = 1f, RadialSegments = 6, Rings = 1 }, true, emissive: false);
        _tiles.InstanceCount = map.Grid.Count;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            var top = TopY(t);
            var c = TileCentre(t);
            var basis = Basis.Identity.Scaled(new Vector3(1, top, 1));
            _tiles.SetInstanceTransform(t, new Transform3D(basis, new Vector3(c.X, top / 2f, c.Z)));
        }
    }

    private void ColourTiles(SurfacePlayer me)
    {
        var map = _surface.Map;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            var colour = Color.FromHtml(map.Terrain(t).Color);
            colour = colour.Lightened((map.Elevation[t].ToFloat() - 0.5f) * 0.15f);
            // From orbit everything is equally visible; once down, unsurveyed land reads darker.
            if (me.Phase != LandingPhase.InOrbit && !me.Surveyed[t]) colour = colour.Darkened(0.35f);
            _tiles.SetInstanceColor(t, colour);
        }
    }

    private void PlaceDeposits(SurfacePlayer me)
    {
        var map = _surface.Map;
        var tiles = new List<int>();
        for (var t = 0; t < map.Grid.Count; t++)
            if (me.Surveyed[t] && map.DepositResource[t] >= 0) tiles.Add(t);
        _deposits.InstanceCount = tiles.Count;
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var id = _sim.Data.Resources[map.DepositResource[t]].Id;
            var size = Mathf.Clamp(map.DepositAmount[t].ToFloat() / 500f, 0.6f, 1.6f);
            var pos = TileCentre(t) + new Vector3(2.2f, 1.6f * size, -1.2f);
            _deposits.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(size, size, size)), pos));
            _deposits.SetInstanceColor(i, DepositColors.TryGetValue(id, out var c) ? c : new Color(0.8f, 0.6f, 1f));
        }
    }

    private void BuildHints()
    {
        var map = _surface.Map;
        var data = _sim.Data;
        for (var region = 0; region < map.RegionCount; region++)
        {
            var sum = Vector3.Zero;
            var n = 0;
            for (var t = 0; t < map.Grid.Count; t++)
            {
                if (map.Region(t) != region) continue;
                sum += TileCentre(t);
                n++;
            }
            if (n == 0) continue;
            var lines = new List<string>();
            foreach (var strength in new[] { HintStrength.Rich, HintStrength.Likely, HintStrength.Traces })
            {
                var names = new List<string>();
                foreach (var h in map.Hints(region))
                    if (h.Strength == strength) names.Add(data.Resources[h.Resource].Name);
                if (names.Count > 0) lines.Add($"{strength}: {string.Join(", ", names)}");
            }
            if (lines.Count == 0) lines.Add("No readings");
            var label = new Label3D
            {
                Text = string.Join("\n", lines),
                Position = sum / n + new Vector3(0, 40, 0),
                FontSize = 44,
                PixelSize = 0.18f,
                OutlineSize = 12,
                Modulate = new Color(1f, 0.86f, 0.5f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
            };
            AddChild(label);
            _hintLabels.Add(label);
        }
    }

    private Node3D BuildPod()
    {
        var pod = new Node3D();
        pod.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 2.2f, Height = 6f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.87f, 0.9f), Metallic = 0.4f, Roughness = 0.35f },
        });
        pod.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.6f, BottomRadius = 0f, Height = 7f, RadialSegments = 12 },
            Position = new Vector3(0, -6f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1f, 0.6f, 0.2f, 0.75f),
            },
            Name = "Flame",
        });
        pod.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 4f, OmniRange = 40f, Position = new Vector3(0, -6, 0) });
        return pod;
    }

    private MultiMesh AddMultiMesh(Mesh mesh, bool colours, bool emissive)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colours, Mesh = mesh };
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        if (emissive) material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
        return mm;
    }

    // ---------------------------------------------------------------- dynamic objects

    private void UpdatePod(SurfacePlayer me, float alpha)
    {
        _pod.Visible = me.Phase == LandingPhase.Descending;
        if (!_pod.Visible) return;
        var total = (float)(me.LandsAtTick - me.DescentStartTick);
        var t = Mathf.Clamp((_sim.Tick - me.DescentStartTick + alpha) / total, 0f, 1f);
        var eased = 1f - (1f - t) * (1f - t) * (1f - t); // fast entry, slow final braking
        var ground = TileCentre(me.LandingTile);
        _pod.Position = ground + new Vector3(0, Mathf.Lerp(260f, 3.5f, eased), 0);
        _pod.GetNode<MeshInstance3D>("Flame").Scale = new Vector3(1, Mathf.Lerp(0.6f, 1.4f, t), 1);
    }

    /// <summary>
    /// Keeps one model per structure: a blue hologram that rises with construction progress, swapped for
    /// the solid model on completion. Labels show queue/progress or why a building is idle.
    /// </summary>
    private void UpdateStructures(float delta)
    {
        var seen = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            seen.Add(s.Id);
            var complete = s.State == StructureState.Complete;
            if (!_structures.TryGetValue(s.Id, out var v) || (v.State != StructureState.Complete && complete))
            {
                if (v != null)
                {
                    v.Model.QueueFree();
                    v.Label.QueueFree();
                }
                var model = BuildingModels.Build(s.DefId, PlanetDriver.PlayerColors[s.Owner % PlanetDriver.PlayerColors.Length]);
                if (!complete) BuildingModels.Override(model, _hologram);
                model.Position = TileCentre(s.Tile);
                model.RotationDegrees = new Vector3(0, -60f * s.Facing, 0);
                AddChild(model);
                var label = new Label3D
                {
                    Position = TileCentre(s.Tile) + new Vector3(0, 11f, 0),
                    FontSize = 36,
                    PixelSize = 0.09f,
                    OutlineSize = 10,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                };
                AddChild(label);
                v = new StructureVisual { Model = model, Label = label, State = s.State };
                _structures[s.Id] = v;
            }
            v.State = s.State;

            if (!complete)
            {
                var f = Mathf.Max(0.08f, _surface.BuildFraction(s).ToFloat());
                v.Model.Scale = new Vector3(1, f, 1);
                v.Label.Text = s.State == StructureState.Queued ? "Queued" : $"Building {Mathf.RoundToInt(f * 100)}%";
                v.Label.Modulate = new Color(0.6f, 0.85f, 1f);
                continue;
            }

            v.Model.Scale = Vector3.One;
            v.Label.Text = s.Status switch
            {
                StructureStatus.NoUpkeep => "Idle: can't pay upkeep",
                StructureStatus.Depleted => "Deposit used up",
                _ => "",
            };
            v.Label.Modulate = new Color(1f, 0.45f, 0.35f);
            if (s.Status == StructureStatus.Working && v.Model.GetNodeOrNull<Node3D>("Spin") is { } spin)
                spin.RotateY(delta * 2.5f);
        }

        // Cancelled buildings disappear.
        foreach (var id in new List<int>(_structures.Keys))
        {
            if (seen.Contains(id)) continue;
            _structures[id].Model.QueueFree();
            _structures[id].Label.QueueFree();
            _structures.Remove(id);
        }
    }

    /// <summary>Show a see-through building at a tile while placing (green if allowed, red if not); null hides it.</summary>
    public void SetGhost(string? buildingId, int tile, int facing, bool allowed)
    {
        if (buildingId != _ghostId)
        {
            _ghost?.QueueFree();
            _ghost = null;
            _ghostId = buildingId;
            if (buildingId != null)
            {
                _ghost = BuildingModels.Build(buildingId, Colors.White);
                BuildingModels.Override(_ghost, _ghostMaterial);
                AddChild(_ghost);
            }
        }
        if (_ghost == null) return;
        _ghost.Visible = tile >= 0;
        if (tile < 0) return;
        _ghost.Position = TileCentre(tile) + new Vector3(0, 0.1f, 0);
        _ghost.RotationDegrees = new Vector3(0, -60f * facing, 0);
        _ghostMaterial.AlbedoColor = allowed ? new Color(0.3f, 1f, 0.4f, 0.45f) : new Color(1f, 0.3f, 0.25f, 0.45f);
    }

    /// <summary>Outline of where the player may build (hexes within the colony's build radius).</summary>
    public void ShowBuildRange(int centre, int radius)
    {
        _rangeRing.Visible = centre >= 0;
        if (centre < 0) return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.55f, 0.85f, 1f, 0.12f),
        };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = HexRadius * 0.9f, BottomRadius = HexRadius * 0.9f, Height = 0.15f, RadialSegments = 6, Rings = 1 } };
        var tiles = _surface.Map.Grid.Within(centre, radius);
        mm.InstanceCount = tiles.Count;
        for (var i = 0; i < tiles.Count; i++) mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, TileCentre(tiles[i]) + new Vector3(0, 0.12f, 0)));
        foreach (var c in _rangeRing.GetChildren()) c.QueueFree();
        _rangeRing.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
    }

    public void HideBuildRange() => _rangeRing.Visible = false;

    private void UpdateRovers(float alpha)
    {
        var units = _surface.Units;
        _rovers.InstanceCount = units.Count;
        for (var i = 0; i < units.Count; i++)
        {
            var u = units[i];
            var from = TileCentre(u.Tile);
            var pos = from;
            var facing = Vector3.Forward;
            if (u.Path.Count > 0)
            {
                var to = TileCentre(u.Path[0]);
                var cost = _surface.Map.Terrain(u.Path[0]).MoveCost;
                var f = Mathf.Clamp((u.Progress.ToFloat() + u.SpeedPerTick.ToFloat() * alpha) / cost, 0f, 1f);
                pos = from.Lerp(to, f);
                facing = (to - from) with { Y = 0 };
            }
            var basis = facing.LengthSquared() > 0.001f ? Basis.LookingAt(facing.Normalized(), Vector3.Up) : Basis.Identity;
            _rovers.SetInstanceTransform(i, new Transform3D(basis, pos + new Vector3(0, 0.9f, 0)));
            var colour = SelectedUnits.Contains(u.Id) ? new Color(1f, 1f, 0.55f) : PlanetDriver.PlayerColors[u.Owner % PlanetDriver.PlayerColors.Length];
            _rovers.SetInstanceColor(i, colour);
        }
    }
}
