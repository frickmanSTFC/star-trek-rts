using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Survey satellites in the orbit view (docs/satellite-contract.md "View / UI"; render-only). Each of our satellites is
// the era's model (BuildingModels.Satellite) on its track over its band: it rises from the ship and unfolds its wings
// while deploying (Satellite.ReadyTick), drifts to its new latitude on a retask with its thrusters lit, then flies east
// at the sweep's column. A soft swath on the globe shows the strip it is imaging under it, a faint ring the band it
// covers, a line its track (Orbit display). Snap() flashes it (and aims its camera at a tile) when the driver takes a
// picture; hover and selection draw a ring round it; Follow keeps it facing the camera.

public partial class OrbitView
{
    /// <summary>The model is drawn this much bigger than its glb (1.85 long) so it reads at the helm's zoom.</summary>
    private const float SatelliteScale = 1.6f;

    /// <summary>Half the swath's width along the track, radians of longitude.</summary>
    private const float SwathHalf = 0.075f;

    private sealed class SatelliteView
    {
        public Node3D Holder = null!;
        public SatelliteRig Rig = null!;
        public MeshInstance3D Band = null!;
        public MeshInstance3D Track = null!;
        public MeshInstance3D Swath = null!;
        public MeshInstance3D Ring = null!;
        public StandardMaterial3D RingMaterial = null!;
        public int Row;
        public int FromRow;
        public long FromTick;
        public int AimTile = -1;
        public float AimUntil;
        public Vector3 Dir;
    }

    private readonly Dictionary<int, SatelliteView> _sats = new();
    private readonly List<int> _satGone = new();
    private readonly HashSet<int> _satAlive = new();

    /// <summary>Under the mouse (drawn with a white ring) and picked (cyan ring); null = none. Set by the driver.</summary>
    public int? HoverSatellite { get; set; }
    public int? SelectedSatellite { get; set; }

    /// <summary>The globe turns to keep this satellite facing the camera (FOLLOW); a drag stops it.</summary>
    public int? FollowSatellite { get; set; }

    private bool Prewarp => _sim.Data.Orbit?.Era == "dawn_of_warp";

    /// <summary>Take a picture: the satellite's flash pulses and its camera turns to the tile (-1 = straight down) for a moment.</summary>
    public void Snap(int satelliteId, int tile)
    {
        if (!_sats.TryGetValue(satelliteId, out var v)) return;
        v.Rig.Flash();
        v.AimTile = tile;
        v.AimUntil = _time + 1.4f;
    }

    /// <summary>The tile under a satellite now (its sub-satellite point), or -1 while it is still rising from the ship.</summary>
    public int SubSatelliteTile(int satelliteId) =>
        _sats.TryGetValue(satelliteId, out var v) && v.Dir != Vector3.Zero ? TileAtDirection(v.Dir) : -1;

    /// <summary>Our satellite nearest the mouse (within ~20 px of its model, not behind the planet), or null.</summary>
    public int? PickSatellite(Vector2 mouse)
    {
        if (Display == OrbitDisplay.System || !_atColony) return null;
        int? best = null;
        var bestDistance = 20f * Mathf.Max(1f, ViewportSize().Y / 720f);
        foreach (var (id, v) in _sats)
        {
            if (!v.Holder.IsVisibleInTree()) continue;
            var at = v.Holder.GlobalPosition;
            if (_camera.IsPositionBehind(at) || Occluded(at)) continue;
            var d = _camera.UnprojectPosition(at).DistanceTo(mouse);
            if (d < bestDistance)
            {
                best = id;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Each of our satellites: model, swath, band ring and track.</summary>
    private void UpdateSatellites(float alpha, float delta)
    {
        var orbit = _sim.Data.Orbit;
        if (orbit == null) return;
        var g = _surface.Map.Grid;
        var now = _sim.Tick + alpha;
        var lapTicks = (float)orbit.SatelliteSweepSeconds * Simulation.TickRate;
        _satAlive.Clear();
        foreach (var sat in _surface.Satellites)
        {
            if (sat.Owner != _localPlayer || sat.Kind == SatelliteKind.Relay || _relayIds.Contains(sat.Id)) continue; // relays: OrbitView.Relays.cs
            _satAlive.Add(sat.Id);
            if (!_sats.TryGetValue(sat.Id, out var v))
            {
                v = NewSatelliteView(sat);
                _sats[sat.Id] = v;
            }
            if (sat.Row != v.Row)
            {
                // Retasked: drift from the band it was over to the new one while the sim's manoeuvre lasts.
                v.FromRow = v.Row;
                v.FromTick = _sim.Tick;
                v.Row = sat.Row;
                RebuildBand(v, sat);
            }

            float fold = 0f, thrust = 0f;
            Vector3 dir;
            var radius = SatelliteOrbit;
            var deploying = now < sat.ReadyTick;
            var trackCols = sat.StartColumn + sat.SweepBase + Mathf.Max(0f, now - sat.ReadyTick) * g.Width / lapTicks; // unfolded over StartColumn
            if (deploying && sat.Retasks == 0)
            {
                // Rise from the ship (wings folded), then unfold over the band's start.
                var f = Mathf.Clamp((now - sat.LaunchTick) / Mathf.Max(1f, sat.ReadyTick - sat.LaunchTick), 0f, 1f);
                var end = SatDirection(sat.Row, sat.StartColumn, g.Height, g.Width); // over the spot that was clicked
                var from = _globe.ToLocal(_shipMarker.GlobalPosition);
                var rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(f / 0.6f, 0f, 1f));
                dir = from.Normalized().Slerp(end, rise).Normalized();
                radius = Mathf.Lerp(from.Length(), SatelliteOrbit, rise);
                fold = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((f - 0.6f) / 0.38f, 0f, 1f));
                thrust = f < 0.62f ? 1f : 0.25f;
            }
            else if (deploying)
            {
                var span = Mathf.Max(1f, sat.ReadyTick - v.FromTick);
                var f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((now - v.FromTick) / span, 0f, 1f));
                var a = SatDirection(v.FromRow, trackCols, g.Height, g.Width);
                var b = SatDirection(sat.Row, trackCols, g.Height, g.Width);
                dir = a.Slerp(b, f).Normalized();
                thrust = 0.8f;
            }
            else dir = SatDirection(sat.Row, trackCols, g.Height, g.Width);
            v.Dir = deploying && sat.Retasks == 0 && fold > 0.5f ? Vector3.Zero : dir;

            // Frame: up = away from the planet, forward (-Z) = east along the track.
            var phi = Mathf.Atan2(dir.X, dir.Z);
            var east = new Vector3(Mathf.Cos(phi), 0f, -Mathf.Sin(phi));
            var z = -east;
            var x = dir.Cross(z).Normalized();
            z = x.Cross(dir).Normalized();
            v.Holder.Transform = new Transform3D(new Basis(x, dir, z), dir * radius);

            Vector3? aim = null;
            if (v.AimTile >= 0 && _time < v.AimUntil)
                aim = v.Holder.Transform.AffineInverse() * (TileDirection(v.AimTile) * Radius);
            v.Rig.Pose(fold, thrust, aim, _time, delta);

            // The swath: on the band, centred under it, only while it sweeps.
            v.Swath.Visible = !deploying;
            v.Swath.Basis = new Basis(Vector3.Up, phi);

            // Hover / selection ring, a constant size on screen, facing the camera.
            var ringed = SelectedSatellite == sat.Id || HoverSatellite == sat.Id;
            v.Ring.Visible = ringed && v.Holder.IsVisibleInTree();
            if (v.Ring.Visible)
            {
                var at = v.Holder.GlobalPosition;
                var toCamera = (_camera.GlobalPosition - at).Normalized();
                var size = 0.03f * _camera.GlobalPosition.DistanceTo(at);
                v.Ring.GlobalTransform = new Transform3D(new Basis(new Quaternion(Vector3.Up, toCamera)).Scaled(new Vector3(size, size, size)), at);
                v.RingMaterial.AlbedoColor = SelectedSatellite == sat.Id ? Cyan : new Color(1f, 1f, 1f, 0.75f);
            }
        }
        _satGone.Clear();
        foreach (var id in _sats.Keys)
            if (!_satAlive.Contains(id)) _satGone.Add(id);
        foreach (var id in _satGone)
        {
            var v = _sats[id];
            foreach (var node in new Node[] { v.Holder, v.Band, v.Track, v.Swath, v.Ring }) node.QueueFree();
            _sats.Remove(id);
        }

        UpdateLocate();

        // FOLLOW: keep the followed satellite's sub-point facing the camera.
        if (FollowSatellite is { } follow && _sats.TryGetValue(follow, out var followed) && Display != OrbitDisplay.System)
        {
            var p = followed.Holder.Position.Normalized();
            _yawTarget = -Mathf.Atan2(p.X, p.Z);
            while (_yawTarget - _yaw > Mathf.Pi) _yawTarget -= Mathf.Tau;
            while (_yawTarget - _yaw < -Mathf.Pi) _yawTarget += Mathf.Tau;
            _pitchTarget = Mathf.Clamp(Mathf.Atan2(p.Y, new Vector2(p.X, p.Z).Length()) - ElevationFor(Display), -1.25f, 1.25f);
        }
        else if (FollowSatellite != null && !_sats.ContainsKey(FollowSatellite.Value)) FollowSatellite = null;
    }

    /// <summary>Globe-local direction over a row and a (fractional, wrapping) sweep column.</summary>
    private static Vector3 SatDirection(int row, float cols, int height, int width)
    {
        var v = VTop + (row + 0.5f) / height * (VBottom - VTop);
        var theta = v * Mathf.Pi;
        var phi = Mathf.PosMod(cols, width) / width * Mathf.Tau;
        return new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
    }

    private SatelliteView NewSatelliteView(Satellite sat)
    {
        var v = new SatelliteView { Row = sat.Row, FromRow = sat.Row, FromTick = sat.LaunchTick };
        v.Holder = new Node3D { Name = $"Satellite{sat.Id}" };
        var model = BuildingModels.Satellite(PlanetDriver.ColorOf(sat.Owner), Prewarp);
        model.Scale = Vector3.One * SatelliteScale;
        v.Holder.AddChild(model);
        _globe.AddChild(v.Holder);
        v.Rig = new SatelliteRig(model, sat.Id * 0.73f);

        v.RingMaterial = OrbitLines.Material(Cyan, 4);
        v.RingMaterial.NoDepthTest = true;
        v.Ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 40, RingSegments = 3 },
            MaterialOverride = v.RingMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _globe.AddChild(v.Ring);

        var swathMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 3, // after the clouds, like the grid
        };
        v.Swath = new MeshInstance3D { MaterialOverride = swathMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _globe.AddChild(v.Swath);
        v.Band = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _globe.AddChild(v.Band);
        v.Track = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _globe.AddChild(v.Track);
        RebuildBand(v, sat);
        return v;
    }

    /// <summary>The band ring, its track and the swath strip for the satellite's current band.</summary>
    private void RebuildBand(SatelliteView v, Satellite sat)
    {
        var g = _surface.Map.Grid;
        var theta = (VTop + (sat.Row + 0.5f) / g.Height * (VBottom - VTop)) * Mathf.Pi;
        var ringRadius = (Radius + 0.6f) * Mathf.Sin(theta);
        v.Band.Mesh = new TorusMesh { InnerRadius = ringRadius - 0.25f, OuterRadius = ringRadius + 0.25f, Rings = 128, RingSegments = 4 };
        v.Band.Position = new Vector3(0, (Radius + 0.6f) * Mathf.Cos(theta), 0);
        v.Band.MaterialOverride ??= new StandardMaterial3D { AlbedoColor = new Color(0.5f, 1f, 0.9f, 0.3f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        v.Track.Mesh = OrbitLines.Circle(SatelliteOrbit * Mathf.Sin(theta), 160);
        v.Track.MaterialOverride ??= OrbitLines.Material(SkyBlue with { A = 0.75f }, 3);
        v.Track.Position = new Vector3(0f, SatelliteOrbit * Mathf.Cos(theta), 0f);
        v.Track.Visible = Display == OrbitDisplay.Orbit;

        // Swath: a soft patch over the band's rows, longitude ±SwathHalf about φ = 0 (turned to the satellite each frame),
        // brightest on the line under it and fading toward the band's edges.
        var half = (_sim.Data.Orbit?.SatelliteBandRows ?? 1) / 2;
        var first = Mathf.Max(0, sat.Row - half);
        var last = Mathf.Min(g.Height - 1, sat.Row + half);
        var t0 = (VTop + first / (float)g.Height * (VBottom - VTop)) * Mathf.Pi;
        var t1 = (VTop + (last + 1f) / g.Height * (VBottom - VTop)) * Mathf.Pi;
        const int across = 10, along = 12;
        var verts = new Vector3[(across + 1) * (along + 1)];
        var colours = new Color[verts.Length];
        var index = new List<int>();
        var tint = new Color(0.55f, 1f, 0.95f);
        for (var i = 0; i <= along; i++)
        {
            var s = i / (float)along * 2f - 1f; // -1 (behind) .. 1 (ahead)
            var lon = s * SwathHalf;
            var fadeLon = Mathf.Pow(1f - Mathf.Abs(s), 1.6f) * (s > 0 ? 0.55f : 1f); // the strip behind it lingers
            for (var j = 0; j <= across; j++)
            {
                var r = j / (float)across;
                var th = Mathf.Lerp(t0, t1, r);
                var edge = Mathf.SmoothStep(0f, 0.25f, r) * Mathf.SmoothStep(0f, 0.25f, 1f - r);
                var k = i * (across + 1) + j;
                verts[k] = new Vector3(Mathf.Sin(th) * Mathf.Sin(lon), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(lon)) * (Radius + 0.6f);
                colours[k] = tint * (0.32f * fadeLon * (0.35f + 0.65f * edge));
                colours[k].A = 1f;
                if (i < along && j < across)
                {
                    var n = k + across + 1;
                    index.AddRange(new[] { k, n, k + 1, k + 1, n, n + 1 });
                }
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        v.Swath.Mesh = mesh;
    }

    /// <summary>Show the tracks on the Orbit display only (ApplyDisplay).</summary>
    private void ApplySatelliteDisplay()
    {
        foreach (var v in _sats.Values) v.Track.Visible = Display == OrbitDisplay.Orbit;
    }

    /// <summary>Latitude of a map row's centre on the globe, degrees (+ north): the same band the textures use.</summary>
    public float RowLatitude(int row)
    {
        var g = _surface.Map.Grid;
        var v = VTop + (row + 0.5f) / g.Height * (VBottom - VTop);
        return 90f - v * 180f;
    }

    /// <summary>The model node of a satellite (tracked objects, screen positions), or null.</summary>
    private Node3D? SatelliteNode(int id) => _sats.TryGetValue(id, out var v) ? v.Holder : null;
}

public partial class OrbitView
{
    private Image? _albedoImage;

    /// <summary>A crop of the painted globe round a tile (wrapping east–west), scaled to size: the satellite photos'
    /// fallback when the surface can't be rendered. Null before Build.</summary>
    public Image? GlobeCrop(int tile, Vector2I size)
    {
        if (_albedoImage == null || tile < 0) return null;
        var src = _albedoImage;
        var w = src.GetWidth();
        var h = src.GetHeight();
        var d = TileDirection(tile);
        var u = Mathf.PosMod(Mathf.Atan2(d.X, d.Z) / Mathf.Tau, 1f);
        var v = Mathf.Acos(Mathf.Clamp(d.Y, -1f, 1f)) / Mathf.Pi;
        // About 7 tiles across, like the 3D camera's view.
        var g = _surface.Map.Grid;
        var cw = Mathf.Max(8, Mathf.RoundToInt(w * 7f / g.Width));
        var ch = Mathf.Max(8, Mathf.RoundToInt(cw * size.Y / (float)size.X));
        var x0 = Mathf.RoundToInt(u * w) - cw / 2;
        var y0 = Mathf.Clamp(Mathf.RoundToInt(v * h) - ch / 2, 0, h - ch);
        var crop = Image.CreateEmpty(cw, ch, false, src.GetFormat());
        x0 = (x0 % w + w) % w;
        var first = Mathf.Min(cw, w - x0);
        crop.BlitRect(src, new Rect2I(x0, y0, first, ch), Vector2I.Zero);
        if (first < cw) crop.BlitRect(src, new Rect2I(0, y0, cw - first, ch), new Vector2I(first, 0));
        crop.Resize(size.X, size.Y, Image.Interpolation.Bilinear);
        return crop;
    }

    private MeshInstance3D? _locate;
    private StandardMaterial3D? _locateMaterial;
    private float _locateUntil = -1f;

    /// <summary>Turn the globe to a tile and pulse a ring on it for a few seconds (a satellite finding picked in the card).</summary>
    public void Locate(int tile)
    {
        if (tile < 0 || !_atColony) return;
        if (_locate == null)
        {
            _locateMaterial = new StandardMaterial3D { AlbedoColor = Peach, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, RenderPriority = 4 };
            _locate = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1.9f, OuterRadius = 2.4f, Rings = 32, RingSegments = 4 }, MaterialOverride = _locateMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _globe.AddChild(_locate);
        }
        FollowSatellite = null;
        PlaceOnSurface(_locate, tile, 0.7f);
        _locateUntil = _time + 5f;
        if (Display == OrbitDisplay.System) SetDisplay(OrbitDisplay.Planet);
        Focus(tile, Mathf.Min(_distanceTarget, 230f));
    }

    /// <summary>The located ring's pulse (from UpdateSatellites).</summary>
    private void UpdateLocate()
    {
        if (_locate == null || _locateMaterial == null) return;
        var left = _locateUntil - _time;
        _locate.Visible = left > 0f;
        if (left <= 0f) return;
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 7f);
        var basis = _locate.Basis.Orthonormalized();
        _locate.Basis = basis.Scaled(Vector3.One * (1f + 0.35f * pulse));
        _locateMaterial.AlbedoColor = Peach with { A = Mathf.Clamp(left, 0f, 1f) * (0.55f + 0.45f * pulse) };
    }
}
