using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Relay satellites in the orbit view (docs/missions-contract.md B; render-only). The colony's relays come from
// PlanetSurface.CommsSources (Kind Relay: SatelliteId, station Tile, Radius, Ready); UpdateSatellites skips them (they
// don't sweep). Each is the era's model (BuildingModels.RelaySatellite ×RelayScale) parked over its station at
// RelayOrbit, dish to the planet: while deploying (Ready false; the Satellite's LaunchTick → ReadyTick) it drifts from
// the ship marker to the station over the first 75% with its panels folded and thrusters lit, then unfolds; a new
// station (retask) slerps it over 6 s, thrusters lit. Its coverage shows as a soft cap on the globe (Radius hexes,
// brighter rim; faint and pulsing while it deploys), a faint pulsing link line runs to the ship marker once it is
// ready, the Link material pulses, the beacon blinks. PreviewRelay draws a would-be coverage cap while the helm's RELAY
// action is armed; PickRelay finds one under the mouse.
public partial class OrbitView
{
    private const float RelayScale = 1.6f;
    private const float RelayOrbit = Radius + 20f;
    private const float RelayRetaskSeconds = 6f;
    private static readonly Color RelayColour = new(0.45f, 1f, 0.7f);

    private sealed class RelayView
    {
        public required Node3D Holder, Model;
        public Node3D? PanelL, PanelR;
        public Basis PanelLRest, PanelRRest;
        public readonly List<(BaseMaterial3D Material, float Energy)> Link = new(), Beacon = new(), Thrusters = new();
        public required MeshInstance3D Cap;
        public required StandardMaterial3D CapMaterial;
        public required MeshInstance3D LinkLine;
        public required StandardMaterial3D LinkMaterial;
        public int Tile = -1, Radius = -1;
        public Vector3 FromDir, Dir;
        public float MoveStart = -100f;
    }

    private readonly Dictionary<int, RelayView> _relays = new();
    private readonly HashSet<int> _relayIds = new();
    private readonly List<int> _relayGone = new();
    private readonly Dictionary<long, ArrayMesh> _coverMeshes = new(); // by station tile and radius (CoverageMesh)
    private MeshInstance3D? _relayPreview;
    private StandardMaterial3D? _relayPreviewMaterial;

    /// <summary>While the helm's RELAY action is armed: the coverage a relay over this tile would give (radius in
    /// hexes; ≤ 0 = the contract's 14). -1 hides it.</summary>
    public void PreviewRelay(int tile, int radiusTiles = 14, bool ok = true)
    {
        if (tile < 0 || !_atColony)
        {
            if (_relayPreview != null) _relayPreview.Visible = false;
            return;
        }
        if (_relayPreview == null)
        {
            _relayPreviewMaterial = CapMaterial();
            _relayPreview = new MeshInstance3D { Name = "RelayPreview", MaterialOverride = _relayPreviewMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _globe.AddChild(_relayPreview);
        }
        _relayPreview.Mesh = CoverageMesh(tile, radiusTiles > 0 ? radiusTiles : 14);
        _relayPreview.Transform = Transform3D.Identity;
        var pulse = 0.6f + 0.4f * Mathf.Sin(_time * 4f);
        _relayPreviewMaterial!.AlbedoColor = (ok ? new Color(0.85f, 1f, 0.95f) : new Color(1f, 0.45f, 0.4f)) * pulse;
        _relayPreview.Visible = true;
    }

    /// <summary>Our relay satellite nearest the mouse (within ~20 px, not behind the planet), or null.</summary>
    public int? PickRelay(Vector2 mouse)
    {
        if (Display == OrbitDisplay.System || !_atColony) return null;
        int? best = null;
        var bestDistance = 20f * Mathf.Max(1f, ViewportSize().Y / 720f);
        foreach (var (id, v) in _relays)
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

    /// <summary>A relay's model node (screen positions), or null.</summary>
    public Node3D? RelayNode(int satelliteId) => _relays.TryGetValue(satelliteId, out var v) ? v.Holder : null;

    /// <summary>Each of our relay satellites; before UpdateSatellites (it skips the ids gathered here).</summary>
    private void UpdateRelays(float alpha, float delta)
    {
        _relayIds.Clear();
        var sources = _surface.CommsSources(_localPlayer);
        var now = _sim.Tick + alpha;
        for (var i = 0; i < sources.Count; i++)
        {
            var c = sources[i];
            if (c.Kind != CommsSourceKind.Relay || c.SatelliteId <= 0 || c.Tile < 0) continue;
            _relayIds.Add(c.SatelliteId);
            if (!_relays.TryGetValue(c.SatelliteId, out var v)) _relays[c.SatelliteId] = v = NewRelayView(c);
            PoseRelay(c, v, now, delta);
        }
        if (_relays.Count == _relayIds.Count) return;
        _relayGone.Clear();
        foreach (var id in _relays.Keys)
            if (!_relayIds.Contains(id)) _relayGone.Add(id);
        foreach (var id in _relayGone)
        {
            var v = _relays[id];
            v.Holder.QueueFree();
            v.Cap.QueueFree();
            v.LinkLine.QueueFree();
            _relays.Remove(id);
        }
    }

    private RelayView NewRelayView(CommsSource c)
    {
        var holder = new Node3D { Name = $"Relay{c.SatelliteId}" };
        var model = BuildingModels.RelaySatellite(PlanetDriver.ColorOf(_localPlayer), Prewarp);
        model.Scale = Vector3.One * RelayScale;
        holder.AddChild(model);
        _globe.AddChild(holder);
        var capMaterial = CapMaterial();
        var cap = new MeshInstance3D { Name = $"RelayCover{c.SatelliteId}", MaterialOverride = capMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _globe.AddChild(cap);
        var linkMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = RelayColour with { A = 0.3f },
            RenderPriority = 3,
        };
        var link = new MeshInstance3D
        {
            Name = $"RelayLink{c.SatelliteId}",
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1f, RadialSegments = 6, CapTop = false, CapBottom = false },
            MaterialOverride = linkMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _globe.AddChild(link);
        var v = new RelayView { Holder = holder, Model = model, Cap = cap, CapMaterial = capMaterial, LinkLine = link, LinkMaterial = linkMaterial };
        v.PanelL = model.FindChild("PanelL", true, false) as Node3D;
        v.PanelR = model.FindChild("PanelR", true, false) as Node3D;
        if (v.PanelL != null) v.PanelLRest = v.PanelL.Basis;
        if (v.PanelR != null) v.PanelRRest = v.PanelR.Basis;
        RelayLamps(model, "Link", v.Link);
        RelayLamps(model, "Beacon", v.Beacon);
        for (var k = 0; k < 4; k++) RelayLamps(model, $"Thruster{k}", v.Thrusters);
        return v;
    }

    private static void RelayLamps(Node3D model, string part, List<(BaseMaterial3D, float)> into)
    {
        if (model.FindChild(part, true, false) is not Node3D node) return;
        void Walk(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(i)) is BaseMaterial3D m)
                    {
                        m.EmissionEnabled = true;
                        into.Add((m, m.EmissionEnergyMultiplier > 0.05f ? m.EmissionEnergyMultiplier : 3f));
                    }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(node);
    }

    private void PoseRelay(CommsSource c, RelayView v, float now, float delta)
    {
        var station = TileDirection(c.Tile);
        var moved = c.Tile != v.Tile;
        if (moved)
        {
            v.FromDir = v.Tile < 0 ? station : (v.Dir == Vector3.Zero ? station : v.Dir);
            v.MoveStart = v.Tile < 0 ? -100f : _time;
            v.Tile = c.Tile;
        }
        if (moved || c.Radius != v.Radius)
        {
            v.Radius = c.Radius;
            v.Cap.Mesh = CoverageMesh(c.Tile, Mathf.Max(0, c.Radius)); // the exact hexes it covers, round its station
        }

        float fold = 0f, thrust = 0f;
        var radius = RelayOrbit;
        Vector3 dir;
        var sat = _surface.SatelliteById(c.SatelliteId);
        if (!c.Ready && sat != null && now < sat.ReadyTick && v.MoveStart < 0f)
        {
            // Deploying: from the ship to the station with the panels folded, then unfold.
            var f = Mathf.Clamp((now - sat.LaunchTick) / Mathf.Max(1f, sat.ReadyTick - sat.LaunchTick), 0f, 1f);
            var from = _globe.ToLocal(_shipMarker.GlobalPosition);
            var drift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(f / 0.75f, 0f, 1f));
            dir = from.Normalized().Slerp(station, drift).Normalized();
            radius = Mathf.Lerp(from.Length(), RelayOrbit, drift);
            fold = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((f - 0.75f) / 0.23f, 0f, 1f));
            thrust = f < 0.78f ? 1f : 0.2f;
        }
        else if (v.MoveStart >= 0f && _time - v.MoveStart < RelayRetaskSeconds)
        {
            var f = Mathf.SmoothStep(0f, 1f, (_time - v.MoveStart) / RelayRetaskSeconds);
            dir = v.FromDir.Slerp(station, f).Normalized();
            thrust = 0.8f;
        }
        else dir = station;
        v.Dir = dir;

        // Frame: up = away from the planet (the dish faces it), forward (-Z) = east.
        var phi = Mathf.Atan2(dir.X, dir.Z);
        var east = new Vector3(Mathf.Cos(phi), 0f, -Mathf.Sin(phi));
        var z = -east;
        var x = dir.Cross(z).Normalized();
        z = x.Cross(dir).Normalized();
        v.Holder.Transform = new Transform3D(new Basis(x, dir, z), dir * radius);
        // a slow turn about the nadir axis keeps it from looking frozen
        v.Model.Basis = new Basis(Vector3.Up, Mathf.Sin(_time * 0.2f + c.SatelliteId) * 0.15f).Scaled(Vector3.One * RelayScale);

        var a = Mathf.DegToRad(90f * fold);
        if (v.PanelL != null) v.PanelL.Basis = v.PanelLRest * new Basis(Vector3.Back, -a);
        if (v.PanelR != null) v.PanelR.Basis = v.PanelRRest * new Basis(Vector3.Back, a);

        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2.6f + c.SatelliteId);
        foreach (var (m, e) in v.Link) m.EmissionEnergyMultiplier = e * (c.Ready ? 0.5f + 0.9f * pulse : 0.15f);
        var blink = Mathf.PosMod(_time + c.SatelliteId * 0.41f, 1.4f) < 0.14f ? 1f : 0.1f;
        foreach (var (m, e) in v.Beacon) m.EmissionEnergyMultiplier = e * blink;
        foreach (var (m, _) in v.Thrusters) m.EmissionEnergyMultiplier = thrust * (2.5f + 1.5f * Mathf.Sin(_time * 31f));

        // Coverage cap over the station (always the station, not where it drifts).
        var shown = Display != OrbitDisplay.System && _atColony;
        v.Cap.Visible = shown;
        v.CapMaterial.AlbedoColor = c.Ready ? new Color(1f, 1f, 1f) * (0.85f + 0.15f * pulse) : new Color(1f, 1f, 1f) * (0.2f + 0.25f * pulse);

        // Link line to the ship marker.
        v.LinkLine.Visible = shown && c.Ready && fold <= 0.01f;
        if (v.LinkLine.Visible)
        {
            var from = v.Holder.Position;
            var to = _globe.ToLocal(_shipMarker.GlobalPosition);
            var span = to - from;
            var length = span.Length();
            if (length < 0.1f) v.LinkLine.Visible = false;
            else
            {
                var basis = new Basis(new Quaternion(Vector3.Up, span / length)).Scaled(new Vector3(1f, length, 1f));
                v.LinkLine.Transform = new Transform3D(basis, from + span * 0.5f);
                v.LinkMaterial.AlbedoColor = RelayColour with { A = 0.12f + 0.18f * pulse };
            }
        }
    }

    // ---------------------------------------------------------------- mission marks on the globe

    /// <summary>A mission marker as the globe draws it: the hexes within RadiusTiles of Tile (an "area" marker = only its
    /// rim hexes; others = all of them, the centre brightest).</summary>
    public readonly record struct GlobeMark(int Tile, int RadiusTiles, bool Area);

    private readonly List<(MeshInstance3D Mesh, StandardMaterial3D Material)> _marks = new();
    private long _marksSignature = long.MinValue;

    /// <summary>Every frame from the driver: our missions' markers on the globe (the surface's markers can't be seen
    /// from orbit, and the relay missions are aimed from here), each tinted (gold; green / red while the RELAY preview
    /// does / doesn't reach it), pulsing. Meshes are rebuilt only when the marks change.</summary>
    public void ShowMissionMarks(IReadOnlyList<GlobeMark> marks, IReadOnlyList<Color> tints)
    {
        if (marks.Count == 0 && _marks.Count == 0) return; // nothing to draw or clear (also before the globe is built)
        var g = _surface.Map.Grid;
        var signature = 17L;
        foreach (var m in marks) signature = ((signature * 31 + m.Tile) * 31 + m.RadiusTiles) * 2 + (m.Area ? 1 : 0);
        if (signature != _marksSignature)
        {
            _marksSignature = signature;
            foreach (var (mesh, _) in _marks) mesh.QueueFree();
            _marks.Clear();
            foreach (var m in marks)
            {
                if (m.Tile < 0 || m.Tile >= g.Count) continue;
                var radius = Mathf.Clamp(m.RadiusTiles, 0, 12);
                var white = new Color(1f, 1f, 1f);
                var mesh = HexPatchMesh(g.Within(m.Tile, radius).Where(t => !m.Area || g.Distance(m.Tile, t) == radius),
                    t => (t == m.Tile && !m.Area ? white * 0.75f : white * (m.Area ? 0.35f : 0.28f)) with { A = 1f }, m.Area ? 0.8f : 0.9f, 0.8f);
                var material = CapMaterial();
                material.RenderPriority = 4; // over the relays' coverage
                var node = new MeshInstance3D { Name = $"MissionMark{_marks.Count}", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _globe.AddChild(node);
                _marks.Add((node, material));
            }
        }
        var shown = Display != OrbitDisplay.System && _atColony;
        var pulse = 0.65f + 0.35f * Mathf.Sin(_time * 3f);
        for (var i = 0; i < _marks.Count; i++)
        {
            _marks[i].Mesh.Visible = shown;
            _marks[i].Material.AlbedoColor = (i < tints.Count ? tints[i] : new Color(0.98f, 0.8f, 0.4f)) * pulse;
        }
    }

    private static StandardMaterial3D CapMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = 3, // after the clouds, like the satellites' swath
    };

    /// <summary>The hexes a relay over `station` covers (every tile within `hexes`, the sim's PlanetSurface.CommsCovered
    /// rule), each drawn as a hexagon on the globe at R+0.65 through the same row / column mapping as the textures: a
    /// faint fill, the rim hexes brighter (vertex colours, additive). The old round cap was sized by equator longitude,
    /// so it reached about twice as far north and south as the real coverage (rows are packed tighter on the globe than
    /// columns) and further east–west away from the equator: a target could sit inside the drawn circle and still be out
    /// of reach. Cached by station and radius (a few dozen kept: the RELAY preview makes one per hovered tile).</summary>
    private ArrayMesh CoverageMesh(int station, int hexes)
    {
        var key = (long)station * 1024 + hexes;
        if (_coverMeshes.TryGetValue(key, out var cached)) return cached;
        if (_coverMeshes.Count > 48) _coverMeshes.Clear();
        var g = _surface.Map.Grid;
        var tiles = station >= 0 && station < g.Count ? g.Within(station, hexes) : new List<int>();
        var inner = RelayColour * 0.07f;
        var rim = RelayColour * 0.42f;
        inner.A = rim.A = 1f;
        var mesh = HexPatchMesh(tiles, t => g.Distance(station, t) >= hexes || !AllNeighboursWithin(g, t, station, hexes) ? rim : inner, 0.92f, 0.65f);
        _coverMeshes[key] = mesh;
        return mesh;
    }

    private static bool AllNeighboursWithin(HexGrid g, int tile, int centre, int hexes)
    {
        var n = 0;
        foreach (var t in g.Neighbours(tile))
        {
            if (g.Distance(centre, t) > hexes) return false;
            n++;
        }
        return n == 6; // a hex at the map's edge is a rim hex too
    }

    /// <summary>One hexagon per tile on the globe (pointy-top, odd rows half a column east, like the map; `inset` 0..1
    /// shrinks each about its centre so the grid shows) at R + lift, coloured per tile: an additive vertex-colour mesh.</summary>
    private ArrayMesh HexPatchMesh(IEnumerable<int> tiles, System.Func<int, Color> colour, float inset, float lift)
    {
        var g = _surface.Map.Grid;
        var verts = new List<Vector3>();
        var colours = new List<Color>();
        var index = new List<int>();
        var r = Radius + lift;
        foreach (var t in tiles)
        {
            var row = g.Row(t);
            var cu = g.Column(t) + 0.5f + ((row & 1) == 1 ? 0.5f : 0f);
            var cv = row + 0.5f;
            var c = colour(t);
            var centre = verts.Count;
            verts.Add(GlobeDirection(cu, cv, g.Width, g.Height) * r);
            colours.Add(c);
            for (var k = 0; k < 6; k++)
            {
                // A pointy-top hex one column wide: corners at 30° + 60°·k, ±½ column, ±⅓ / ±⅔ of a row.
                var a = Mathf.DegToRad(30f + 60f * k);
                verts.Add(GlobeDirection(cu + Mathf.Cos(a) / Mathf.Sqrt(3f) * inset, cv + Mathf.Sin(a) * (2f / 3f) * inset, g.Width, g.Height) * r);
                colours.Add(c);
            }
            for (var k = 0; k < 6; k++)
            {
                index.Add(centre);
                index.Add(centre + 1 + k);
                index.Add(centre + 1 + (k + 1) % 6);
            }
        }
        var mesh = new ArrayMesh();
        if (verts.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Globe-local direction of a point in map units (column + fraction, row + fraction), as TileDirection.</summary>
    private static Vector3 GlobeDirection(float column, float row, int width, int height)
    {
        var theta = (VTop + row / height * (VBottom - VTop)) * Mathf.Pi;
        var phi = column / width * Mathf.Tau;
        return new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
    }
}
