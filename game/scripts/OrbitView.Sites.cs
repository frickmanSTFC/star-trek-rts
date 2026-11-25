using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// The orbit view at a space site (space jobs phase 1, docs/space-jobs-contract.md "VIEW"; render-only): while the
// flagship holds at a site (SystemSource.FlagshipSite) the globe displays (Planet, Orbit, Atmosphere) show no planet:
// the site's model in the middle (SiteModels: the glb's model or its stand-in, fitted to SiteDrawSize across, tumbling
// slowly; a station's Ring turns, beacons blink), the flagship's model beside it (the same model OrbitView.Ship.cs
// built, moved off its track, rings turning), the stars and nebula as always, and one far body: the known body nearest
// the site on the plot, a small lit sphere in its plot direction. Dragging turns the scene like the globe; the wheel
// zooms (the globe's zoom, closer). The System display is unchanged. Hooks in OrbitView.cs: Refresh → UpdateSiteView
// after UpdateShip, ApplyDisplay → ApplySiteDisplay, PickTile → -1 here.
public partial class OrbitView
{
    /// <summary>The site's model is drawn this big across, the flagship this long beside it.</summary>
    private const float SiteDrawSize = 62f, SiteShipLength = 20f;

    /// <summary>The camera sits closer than over the globe: this share of the globe display's distance.</summary>
    private const float SiteZoom = 0.42f;

    private Node3D? _siteScene;
    private Node3D? _siteModel;
    private MeshInstance3D? _siteFarBody;
    private string _siteKey = "";
    private bool _siteMode;
    private float _siteTime;
    private readonly List<(Node3D Node, Vector3 Axis, float Rate, Basis Start)> _siteTumblers = new();
    private readonly List<Node3D> _siteRings = new();
    private readonly List<Node3D> _siteBeacons = new();

    /// <summary>The site shown on the globe displays now (the flagship holds there), or null.</summary>
    public string? SiteShown => _siteMode ? _source.FlagshipSite : null;

    /// <summary>Every frame from Refresh, after UpdateShip: switch the site scene on or off, build it for a new site,
    /// turn it with the globe's drag, frame the camera on it, put the flagship beside it.</summary>
    private void UpdateSiteView(float delta, float cameraDistance)
    {
        var siteId = _source.FlagshipSite;
        var site = _source.State?.Site(siteId);
        var mode = site != null && Display != OrbitDisplay.System;
        if (mode != _siteMode)
        {
            _siteMode = mode;
            ApplyDisplay();
        }
        if (site == null)
        {
            if (_siteScene != null && _siteKey.Length > 0) ClearSiteScene();
            return;
        }
        var knowledge = _source.State!.SiteKnowledgeOf(_localPlayer, site.Id);
        var key = $"{site.Id}|{site.Kind}|{site.Variant}|{knowledge == SiteKnowledge.Spent}";
        if (key != _siteKey) BuildSiteScene(site, key);
        if (!_siteMode || _siteScene == null) return;

        _siteTime += delta;
        _siteScene.Basis = _globe.Basis; // the same drag as the globe
        for (var i = 0; i < _siteTumblers.Count; i++)
        {
            var (node, axis, rate, rest) = _siteTumblers[i];
            if (!IsInstanceValid(node)) continue;
            node.Basis = new Basis(axis, _siteTime * rate) * rest;
        }
        for (var i = 0; i < _siteRings.Count; i++)
            if (IsInstanceValid(_siteRings[i])) _siteRings[i].RotateObjectLocal(Vector3.Back, delta * 0.25f);
        var phase = Mathf.PosMod(_siteTime, 1.4f);
        var lit = phase < 0.12f || (phase > 0.28f && phase < 0.4f); // a double flash
        for (var i = 0; i < _siteBeacons.Count; i++)
            if (IsInstanceValid(_siteBeacons[i])) _siteBeacons[i].Visible = lit;

        // The camera: the display's own angle, closer in, on the site.
        var d = cameraDistance * SiteZoom;
        _camera.Position = new Vector3(0f, Mathf.Sin(_elevation), Mathf.Cos(_elevation)) * d;
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        FrameViewRect(d);

        // The flagship holds beside the site, nose across the view (its model is OrbitView.Ship.cs's).
        _followShip = false;
        HoverShip = false;
        _shipShown = false; // no hover / click-to-follow here
        _shipMarker.Visible = false;
        if (_shipRing != null) _shipRing.Visible = false;
        if (_shipHolder != null && _shipModel != null)
        {
            _shipHolder.Visible = true;
            var local = new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(SiteDrawSize * 0.78f, SiteDrawSize * 0.08f, SiteDrawSize * 0.28f));
            var t = _siteScene.GlobalTransform * local;
            _shipHolder.GlobalTransform = new Transform3D(t.Basis.Orthonormalized().Scaled(Vector3.One * (SiteShipLength / ShipDrawLength)), t.Origin);
            if (_shipFill != null)
            {
                _shipFill.Visible = true;
                var toShip = (t.Origin - _camera.GlobalPosition).Normalized();
                if (toShip.LengthSquared() > 0.5f) _shipFill.Basis = Basis.LookingAt(toShip, Mathf.Abs(toShip.Y) > 0.98f ? Vector3.Forward : Vector3.Up);
            }
        }
    }

    /// <summary>From ApplyDisplay: at a site, the globe displays hide the planet, its air, clouds, moon and tracks.</summary>
    private void ApplySiteDisplay()
    {
        if (_siteScene != null) _siteScene.Visible = _siteMode;
        if (!_siteMode) return;
        _globe.Visible = false;
        _bodyGlobe.Visible = false;
        _clouds.Visible = false;
        _highClouds.Visible = false;
        _atmosphere.Visible = false;
        _moon.Visible = false;
        _shipOrbit.Visible = false;
        _sweep.Visible = false;
        _hover.Visible = false;
    }

    private void ClearSiteScene()
    {
        _siteScene?.QueueFree();
        _siteScene = null;
        _siteModel = null;
        _siteFarBody = null;
        _siteKey = "";
        _siteTumblers.Clear();
        _siteRings.Clear();
        _siteBeacons.Clear();
    }

    private void BuildSiteScene(SpaceSite site, string key)
    {
        ClearSiteScene();
        _siteKey = key;
        _siteScene = new Node3D { Name = "SiteScene", Visible = _siteMode };
        AddChild(_siteScene);

        var model = SiteModels.Build(site.Kind, site.Variant, site.Seed);
        var box = SiteModels.Bounds(model);
        var size = Mathf.Max(1f, Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)));
        var holder = new Node3D { Name = "Model", Scale = Vector3.One * (SiteDrawSize / size) };
        model.Position = -box.GetCenter();
        holder.AddChild(model);
        _siteScene.AddChild(holder);
        _siteModel = model;
        var spent = key.EndsWith("|True");
        CollectSiteParts(model, spent);

        // The whole site tumbles a little (a station holds still); pieces with their own tumble turn on their own.
        var rng = new RandomNumberGenerator { Seed = site.Seed == 0 ? 7UL : site.Seed };
        foreach (var child in model.GetChildren())
        {
            if (child is not Node3D n || !n.HasMeta(SiteModels.TumbleMeta)) continue;
            var rate = (float)n.GetMeta(SiteModels.TumbleMeta);
            if (rate <= 0f) continue;
            var axis = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
            if (axis.LengthSquared() < 0.01f) axis = Vector3.Up;
            _siteTumblers.Add((n, axis.Normalized(), rate, n.Basis));
        }

        // One far body: the known body nearest the site on the plot, in its direction.
        var at = new Vector2(site.Position.X.ToFloat(), site.Position.Y.ToFloat());
        BodyDefLike? nearest = null;
        foreach (var b in _source.Bodies)
        {
            if (!_source.Known(b.Id)) continue;
            var p = SystemSource.PositionOf(b);
            var dist = p.DistanceTo(at);
            if (nearest == null || dist < nearest.Value.Distance) nearest = new BodyDefLike(b.ClassId, _source.Identified(b.Id), p, dist);
        }
        if (nearest is { } far)
        {
            var dir2 = far.Position - at;
            var dir = new Vector3(dir2.X, dir2.Length() * 0.18f, -dir2.Y);
            dir = dir.LengthSquared() < 1e-4f ? new Vector3(-0.6f, 0.2f, -1f).Normalized() : dir.Normalized();
            var colour = far.Identified ? SystemPlot.ClassColour(far.ClassId) : SystemPlot.Unknown;
            var radius = Mathf.Clamp(260f - far.Distance * 0.25f, 60f, 220f);
            _siteFarBody = new MeshInstance3D
            {
                Name = "FarBody",
                Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 32, Rings = 16 },
                Position = dir * 2600f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.95f, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 0.04f },
            };
            _siteScene.AddChild(_siteFarBody);
        }
    }

    private readonly record struct BodyDefLike(string ClassId, bool Identified, Vector2 Position, float Distance);

    /// <summary>Rings (turn), beacons (blink) and a dimmed look for a spent site.</summary>
    private void CollectSiteParts(Node node, bool spent)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D n)
            {
                var name = n.Name.ToString();
                if (name == "Ring") _siteRings.Add(n);
                else if (name == "Beacon" || name.StartsWith("Beacon")) _siteBeacons.Add(n);
                if (spent && n is GeometryInstance3D g) g.Transparency = 0.35f;
            }
            CollectSiteParts(child, spent);
        }
    }
}
