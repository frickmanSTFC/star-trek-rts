using System.Collections.Generic;
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
    private readonly Dictionary<int, ArrayMesh> _capMeshes = new(); // by radius in hexes
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
        _relayPreview.Mesh = CapMesh(radiusTiles > 0 ? radiusTiles : 14);
        _relayPreview.Basis = new Basis(new Quaternion(Vector3.Up, TileDirection(tile)));
        _relayPreview.Position = Vector3.Zero;
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
        if (c.Tile != v.Tile)
        {
            v.FromDir = v.Tile < 0 ? station : (v.Dir == Vector3.Zero ? station : v.Dir);
            v.MoveStart = v.Tile < 0 ? -100f : _time;
            v.Tile = c.Tile;
        }
        if (c.Radius != v.Radius)
        {
            v.Radius = c.Radius;
            v.Cap.Mesh = CapMesh(Mathf.Max(1, c.Radius));
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
        v.Cap.Basis = new Basis(new Quaternion(Vector3.Up, station));
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

    private static StandardMaterial3D CapMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = 3, // after the clouds, like the satellites' swath
    };

    /// <summary>A soft spherical cap round +Y at R+0.65 covering `hexes` + ½ hexes of longitude at the equator: faint
    /// inside, a brighter band at the rim (vertex colours, additive). Cached by radius.</summary>
    private ArrayMesh CapMesh(int hexes)
    {
        if (_capMeshes.TryGetValue(hexes, out var cached)) return cached;
        var g = _surface.Map.Grid;
        var angle = Mathf.Min(1.4f, (hexes + 0.5f) * Mathf.Tau / Mathf.Max(1, g.Width));
        const int rings = 14, segs = 64;
        var verts = new List<Vector3>();
        var colours = new List<Color>();
        var index = new List<int>();
        var r = Radius + 0.65f;
        verts.Add(Vector3.Up * r);
        colours.Add((RelayColour * 0.05f) with { A = 1f });
        for (var j = 1; j <= rings; j++)
        {
            var t = j / (float)rings;
            var th = angle * t;
            var rimBand = Mathf.SmoothStep(0.78f, 0.97f, t) * (1f - Mathf.SmoothStep(0.97f, 1f, t) * 0.6f);
            var c = RelayColour * (0.05f + 0.08f * t + 0.32f * rimBand);
            c.A = 1f;
            for (var i = 0; i < segs; i++)
            {
                var ph = Mathf.Tau * i / segs;
                verts.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * r);
                colours.Add(c);
            }
        }
        for (var i = 0; i < segs; i++)
        {
            index.Add(0);
            index.Add(1 + (i + 1) % segs);
            index.Add(1 + i);
        }
        for (var j = 1; j < rings; j++)
            for (var i = 0; i < segs; i++)
            {
                var a = 1 + (j - 1) * segs + i;
                var b = 1 + (j - 1) * segs + (i + 1) % segs;
                var cc = 1 + j * segs + i;
                var d = 1 + j * segs + (i + 1) % segs;
                index.Add(a); index.Add(b); index.Add(cc);
                index.Add(b); index.Add(d); index.Add(cc);
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _capMeshes[hexes] = mesh;
        return mesh;
    }
}
