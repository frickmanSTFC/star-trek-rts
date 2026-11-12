using System.Collections.Generic;
using FinalFrontier.Sim.Missions;
using Godot;

namespace FinalFrontier;

// Mission sites and markers on the map (docs/missions-contract.md B/C; render-only; Simulation.Missions).
// Sites (MissionBook.Sites): a prop per site (BuildingModels.MissionProp by Kind: probe_wreck, signal_source,
// photo_point, survey_marker) on its tile once it is Found or in sight; the probe's beacon blinks weakly, the signal
// source's glow pulses, a done site's light turns steady green; scatter within SiteClearing is cut round it (after the
// clearings, like the ruins). Markers (MissionBook.Markers, our missions only): "area" = a dashed amber rim round the
// hexes within RadiusTiles; "target" = a bright rim on the tile + a faint light pillar; "site" = a rim + a short pillar;
// "unit" = a turning diamond over that unit (it follows the vehicle or drone); each with its Label (billboard). Rebuilt
// when the marker or site list changes (checked twice a second), the pulses run every frame.
public partial class PlanetView
{
    private const float SiteClearing = 7f;
    private static readonly Color MissionAmber = new(1f, 0.74f, 0.3f);

    private sealed class SiteProp
    {
        public required Node3D Node;
        public required string Kind;
        public readonly List<(BaseMaterial3D Material, float Energy)> Beacon = new(), Glow = new();
        public bool Done;
    }

    private readonly Dictionary<int, SiteProp> _siteProps = new(); // by site id
    private readonly HashSet<int> _sitesShown = new();
    private readonly List<int> _sitesGone = new();
    private long _siteSet = long.MinValue, _siteScatterFor = long.MinValue, _markerSignature = long.MinValue;
    private float _missionClock = 1f;
    private Node3D? _markerRoot;
    private readonly List<(MeshInstance3D Pillar, StandardMaterial3D Material)> _markerPillars = new();
    private readonly List<(Node3D Diamond, int UnitId, Label3D? Label)> _markerUnits = new();

    /// <summary>Once a frame after UpdateRuins (scatter clearings are done by then).</summary>
    private void UpdateMissions(float delta)
    {
        var book = _sim.Missions;
        _missionClock += delta;
        if (_missionClock >= 0.5f)
        {
            _missionClock = 0f;
            if (book.Enabled || _siteProps.Count > 0) RefreshSites(book);
            if (book.Enabled || _markerRoot != null) RefreshMarkers(book);
        }
        foreach (var (id, p) in _siteProps)
        {
            var blink = p.Done ? 1f : p.Kind == "probe_wreck" ? (Mathf.PosMod(_time + id * 0.3f, 2.2f) < 0.12f ? 0.6f : 0.05f) : 0.5f + 0.5f * Mathf.Sin(_time * 3f + id);
            foreach (var (m, e) in p.Beacon) m.EmissionEnergyMultiplier = e * blink;
            var glow = p.Done ? 0.3f : 0.55f + 0.45f * Mathf.Sin(_time * 1.7f + id) * Mathf.Sin(_time * 0.53f + id * 2f);
            foreach (var (m, e) in p.Glow) m.EmissionEnergyMultiplier = e * glow;
        }
        // the clearings were redone (UpdateClearings restores every instance): cut ours again
        if (_siteProps.Count > 0 && _scatterRoot != null && _clearingSignature != long.MinValue && _clearingSignature != _siteScatterFor) CutSiteScatter();

        if (_markerRoot == null) return;
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2.4f);
        foreach (var (_, material) in _markerPillars) material.AlbedoColor = MissionAmber with { A = 0.06f + 0.08f * pulse };
        foreach (var (diamond, unitId, label) in _markerUnits)
        {
            var model = UnitModel(unitId);
            diamond.Visible = model != null && model.Visible;
            if (label != null) label.Visible = diamond.Visible;
            if (model == null) continue;
            var at = model.GlobalPosition + new Vector3(0, 8f + 0.6f * Mathf.Sin(_time * 2f + unitId), 0);
            diamond.Position = at;
            diamond.Rotation = new Vector3(0, _time * 1.4f, 0);
            if (label != null) label.Position = at + new Vector3(0, 2.4f, 0);
        }
    }

    private void RefreshSites(MissionBook book)
    {
        _sitesShown.Clear();
        var set = 19L;
        var sites = book.Sites(_localPlayer);
        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            if (s.Tile < 0 || s.Tile >= _surface.Map.Grid.Count || !(s.Found || Sees(s.Tile))) continue;
            _sitesShown.Add(s.Id);
            set = set * 31 + s.Tile;
            if (!_siteProps.TryGetValue(s.Id, out var p) || p.Kind != s.Kind)
            {
                p?.Node.QueueFree();
                _siteProps[s.Id] = p = MakeSiteProp(s);
            }
            if (p.Done != s.Done)
            {
                p.Done = s.Done;
                foreach (var (m, _) in p.Beacon)
                {
                    m.AlbedoColor = s.Done ? new Color(0.35f, 1f, 0.45f) : m.AlbedoColor;
                    if (s.Done) m.Emission = new Color(0.35f, 1f, 0.45f);
                }
            }
        }
        _sitesGone.Clear();
        foreach (var id in _siteProps.Keys)
            if (!_sitesShown.Contains(id)) _sitesGone.Add(id);
        foreach (var id in _sitesGone)
        {
            _siteProps[id].Node.QueueFree();
            _siteProps.Remove(id);
        }
        if (set == _siteSet) return;
        _siteSet = set;
        // the clearings are redone next frame (trees come back where a site went), then ours are cut again
        _clearingSignature = long.MinValue;
        _siteScatterFor = long.MinValue;
        _ruinScatterFor = long.MinValue;
    }

    private SiteProp MakeSiteProp(MissionSite s)
    {
        var node = BuildingModels.MissionProp(s.Kind);
        node.Name = $"MissionSite{s.Id}";
        var c = TileCentre(s.Tile);
        node.Position = c with { Y = GroundAt(c) };
        node.RotationDegrees = new Vector3(0f, Hash01(s.Tile * 15485863 + 11) * 360f, 0f);
        AddChild(node);
        var p = new SiteProp { Node = node, Kind = s.Kind };
        Lamps(node, "Beacon", p.Beacon);
        Lamps(node, "Glow", p.Glow);
        return p;
    }

    /// <summary>Hide the scatter round every shown site (added to the clearings' lists, as the ruins do).</summary>
    private void CutSiteScatter()
    {
        _siteScatterFor = _clearingSignature;
        if (_scatterChunks.Length == 0) return;
        var zero = new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero);
        var r2 = SiteClearing * SiteClearing;
        foreach (var (_, p) in _siteProps)
        {
            var centre = p.Node.Position;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.X - SiteClearing) / ChunkWidth), 0, _chunkColumns - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((centre.X + SiteClearing) / ChunkWidth), 0, _chunkColumns - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.Z - SiteClearing) / ChunkDepth), 0, _chunkRows - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((centre.Z + SiteClearing) / ChunkDepth), 0, _chunkRows - 1);
            for (var cz = z0; cz <= z1; cz++)
                for (var cx = x0; cx <= x1; cx++)
                    foreach (var batch in _scatterChunks[cz * _chunkColumns + cx])
                        for (var i = 0; i < batch.World.Length; i++)
                        {
                            var d = batch.World[i] - centre;
                            if (d.X * d.X + d.Z * d.Z > r2 || !_scatterHiddenSet.Add((batch, i))) continue;
                            batch.Multi.SetInstanceTransform(i, new Transform3D(zero, batch.Local[i].Origin));
                            _scatterHidden.Add((batch, i));
                        }
        }
    }

    // ---------------------------------------------------------------- markers

    private void RefreshMarkers(MissionBook book)
    {
        var markers = book.Markers(_localPlayer);
        // With the comms overlay up (G, or a drone order armed), a site / target marker says whether it is inside comms
        // coverage, so a relay mission's target reads at a glance.
        var comms = _commsOn || _commsArmed;
        var signature = comms ? 31L : 29L;
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            signature = (((signature * 31 + m.Tile) * 31 + m.RadiusTiles) * 31 + m.UnitId) * 31 + m.Style.Length * 7 + m.Label.Length;
            if (comms && CommsTagged(m)) signature = signature * 2 + (_surface.CommsCovered(_localPlayer, m.Tile) ? 1 : 0);
        }
        if (signature == _markerSignature) return;
        _markerSignature = signature;
        if (_markerRoot == null)
        {
            _markerRoot = new Node3D { Name = "MissionMarkers" };
            AddChild(_markerRoot);
        }
        foreach (var child in _markerRoot.GetChildren()) child.QueueFree();
        _markerPillars.Clear();
        _markerUnits.Clear();
        var grid = _surface.Map.Grid;
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m.Style == "unit" || m.UnitId > 0)
            {
                var diamond = new MeshInstance3D
                {
                    Name = $"MarkerUnit{i}",
                    Mesh = new SphereMesh { Radius = 1.1f, Height = 2.8f, RadialSegments = 4, Rings = 2 },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = MissionAmber, EmissionEnabled = true, Emission = MissionAmber, EmissionEnergyMultiplier = 2.5f,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _markerRoot.AddChild(diamond);
                var label = m.Label.Length > 0 ? MarkerLabel(m.Label, Vector3.Zero) : null;
                _markerUnits.Add((diamond, m.UnitId, label));
                continue;
            }
            if (m.Tile < 0 || m.Tile >= grid.Count) continue;
            _rimSet.Clear();
            var radius = m.Style == "area" ? Mathf.Max(0, m.RadiusTiles) : 0;
            foreach (var t in grid.Within(m.Tile, radius)) _rimSet.Add(t);
            var area = m.Style == "area";
            _markerRoot.AddChild(new MeshInstance3D
            {
                Name = $"MarkerRim{i}",
                Mesh = HexRimMesh(_rimSet, area ? 1.2f : 1.6f, 0.55f, area ? 0.6f : 0.9f),
                MaterialOverride = RimMaterial(MissionAmber, area ? 0.9f : 1.3f, area ? 2.6f : 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            var c = TileCentre(m.Tile);
            var ground = GroundAt(c);
            if (!area)
            {
                var height = m.Style == "target" ? 46f : 18f;
                var material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    AlbedoColor = MissionAmber with { A = 0.1f },
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                var pillar = new MeshInstance3D
                {
                    Name = $"MarkerPillar{i}",
                    Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 2.2f, Height = height, RadialSegments = 16, CapTop = false, CapBottom = false },
                    MaterialOverride = material,
                    Position = c with { Y = ground + height / 2f },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _markerRoot.AddChild(pillar);
                _markerPillars.Add((pillar, material));
            }
            if (m.Label.Length == 0) continue;
            var labelAt = c with { Y = ground + (area ? 10f : m.Style == "target" ? 20f : 12f) };
            if (comms && CommsTagged(m))
            {
                var covered = _surface.CommsCovered(_localPlayer, m.Tile);
                MarkerLabel($"{m.Label} · {(covered ? "IN COMMS" : "NO COMMS")}", labelAt).Modulate = covered ? RelayCommsColour : new Color(1f, 0.5f, 0.42f);
            }
            else MarkerLabel(m.Label, labelAt);
        }
    }

    /// <summary>Markers that get the comms overlay's IN COMMS / NO COMMS tag: a site or target on a tile.</summary>
    private bool CommsTagged(MissionMarker m) =>
        m.Style != "area" && m.UnitId <= 0 && m.Tile >= 0 && m.Tile < _surface.Map.Grid.Count && m.Label.Length > 0;

    private Label3D MarkerLabel(string text, Vector3 at)
    {
        var label = new Label3D
        {
            Text = text, FontSize = 30, PixelSize = 0.085f, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, Modulate = MissionAmber, Position = at,
        };
        _markerRoot!.AddChild(label);
        return label;
    }
}
