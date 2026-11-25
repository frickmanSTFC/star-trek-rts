using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 2 in the orbit view at a site (docs/space-jobs-contract.md "Phase 2"; render-only): while the flagship
// mines the asteroid it holds at, an amber cutting beam runs from the ship to the rock and a stream of ore chunks drifts
// from the rock to the ship; while it has a wreck in tow (held at another site, e.g. the station), that wreck's model
// trails behind the ship on a blue tractor line. Every node is made once per site scene (children of _siteScene, freed
// with it) and moved each frame: no per-frame allocation. Called from UpdateSiteView once the ship holder is placed.
public partial class OrbitView
{
    private const int OreChunks = 10;

    private Node3D? _workFx;
    private MeshInstance3D? _mineBeam, _towLine;
    private StandardMaterial3D? _mineBeamMat, _towLineMat;
    private readonly MeshInstance3D?[] _ore = new MeshInstance3D?[OreChunks];
    private Node3D? _towModel;
    private string _towKey = "";

    /// <summary>The towed model's drawn size (a little bigger than the ship: wrecks are long).</summary>
    private const float TowDrawSize = 24f;

    private void ClearSiteWork()
    {
        // The nodes are children of the site scene, freed with it.
        _workFx = null;
        _mineBeam = _towLine = null;
        _mineBeamMat = _towLineMat = null;
        for (var i = 0; i < OreChunks; i++) _ore[i] = null;
        _towModel = null;
        _towKey = "";
    }

    private void UpdateSiteWork(string siteId)
    {
        if (_siteScene == null || _source.State is not { } sys || _shipHolder == null) return;
        EnsureWorkFx();
        var player = _source.Player;
        var shipAt = _shipHolder.GlobalPosition;
        var rockAt = _siteScene.GlobalPosition;

        // Mining: beam + ore stream.
        var mining = sys.SiteWorkProgress(player, siteId) is { } work && work.Label == "Mining";
        _mineBeam!.Visible = mining;
        for (var i = 0; i < OreChunks; i++) _ore[i]!.Visible = mining;
        if (mining)
        {
            var toShip = shipAt - rockAt;
            var length = toShip.Length();
            var dir = length > 1e-3f ? toShip / length : Vector3.Up;
            var surface = rockAt + dir * (SiteDrawSize * 0.42f);
            Stretch(_mineBeam, shipAt, surface, 0.35f + 0.1f * Mathf.Sin(_siteTime * 23f));
            _mineBeamMat!.AlbedoColor = new Color(1f, 0.72f, 0.3f, 0.55f + 0.25f * Mathf.Sin(_siteTime * 37f));
            var side = dir.Cross(Vector3.Up);
            if (side.LengthSquared() < 1e-4f) side = Vector3.Right;
            side = side.Normalized();
            var up = side.Cross(dir).Normalized();
            for (var i = 0; i < OreChunks; i++)
            {
                var t = Mathf.PosMod(_siteTime * 0.22f + i / (float)OreChunks, 1f);
                var wobble = side * Mathf.Sin(i * 2.4f + t * 5f) * 2.2f + up * Mathf.Cos(i * 1.7f + t * 4f) * 1.6f;
                var chunk = _ore[i]!;
                chunk.GlobalPosition = surface.Lerp(shipAt, t) + wobble * (1f - t);
                chunk.Scale = Vector3.One * Mathf.Lerp(1f, 0.35f, t) * (0.8f + 0.4f * ((i * 37) % 10) / 10f);
                chunk.RotateObjectLocal(Vector3.Up, 0.03f + i * 0.004f);
            }
        }

        // A wreck in tow (held somewhere else): its model behind the ship on a tractor line.
        var tow = sys.TowOf(player);
        var towSite = tow != null && tow != siteId ? sys.Site(tow) : null;
        var key = towSite == null ? "" : $"{towSite.Id}|{towSite.Kind}|{towSite.Variant}";
        if (key != _towKey)
        {
            _towKey = key;
            _towModel?.QueueFree();
            _towModel = null;
            if (towSite != null)
            {
                var model = SiteModels.Build(towSite.Kind, towSite.Variant, towSite.Seed);
                var box = SiteModels.Bounds(model);
                var size = Mathf.Max(1f, Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)));
                model.Position = -box.GetCenter();
                _towModel = new Node3D { Name = "TowedModel", Scale = Vector3.One * (TowDrawSize / size) };
                _towModel.AddChild(model);
                _workFx!.AddChild(_towModel);
            }
        }
        _towLine!.Visible = _towModel != null;
        if (_towModel != null)
        {
            var aft = _shipHolder.GlobalBasis.Z.Normalized(); // the ships' bows are -Z
            var at = shipAt + aft * (SiteShipLength * 0.5f + TowDrawSize * 0.8f) + Vector3.Down * 2f;
            _towModel.GlobalPosition = at;
            _towModel.RotateObjectLocal(new Vector3(0.3f, 1f, 0.2f).Normalized(), 0.002f); // a slow tumble on the line
            Stretch(_towLine, shipAt + aft * (SiteShipLength * 0.4f), at, 0.18f);
            _towLineMat!.AlbedoColor = new Color(0.45f, 0.75f, 1f, 0.35f + 0.15f * Mathf.Sin(_siteTime * 6f));
        }
    }

    private void EnsureWorkFx()
    {
        if (_workFx != null && IsInstanceValid(_workFx)) return;
        _workFx = new Node3D { Name = "SiteWorkFx" };
        _siteScene!.AddChild(_workFx);
        (_mineBeam, _mineBeamMat) = Beam("MineBeam");
        (_towLine, _towLineMat) = Beam("TowLine");
        var chunkMesh = new SphereMesh { Radius = 0.9f, Height = 1.5f, RadialSegments = 6, Rings = 3 };
        var chunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.38f, 0.33f), Roughness = 0.95f, EmissionEnabled = true, Emission = new Color(1f, 0.55f, 0.2f), EmissionEnergyMultiplier = 0.25f };
        for (var i = 0; i < OreChunks; i++)
        {
            var chunk = new MeshInstance3D { Name = $"Ore{i}", Mesh = chunkMesh, MaterialOverride = chunkMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _workFx.AddChild(chunk);
            _ore[i] = chunk;
        }
    }

    private (MeshInstance3D, StandardMaterial3D) Beam(string name)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = Colors.White,
        };
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = mat,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _workFx!.AddChild(node);
        return (node, mat);
    }

    /// <summary>A unit cylinder stretched from a to b, radius r (global space).</summary>
    private static void Stretch(MeshInstance3D beam, Vector3 a, Vector3 b, float r)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 1e-3f)
        {
            beam.Visible = false;
            return;
        }
        var y = d / length;
        var x = y.Cross(Mathf.Abs(y.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(y);
        beam.GlobalTransform = new Transform3D(new Basis(x * r, y * length, z * r), (a + b) * 0.5f);
    }
}
