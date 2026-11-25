using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 3 in the orbit view at a site (docs/space-jobs-contract.md "Phase 3"; render-only): what the event
// sites look like while the flagship holds at one. A comet: a glowing coma round the nucleus and jets of gas, both (and
// the tail) bright while its tail window is open, and a cyan sampling beam into the tail while the ship samples. A rogue
// asteroid: a blue tractor beam on the rock while the ship pushes it (its dust trail is part of the model). Lifepods: the
// pods still waiting (picked-up and lost ones hidden), the one being brought in drawn along a tractor beam to the ship.
// A stranded ship: a red distress light pulsing until its power is back, then a steady warm one. An ion storm: a cloud of
// lavender plasma inside its shells and lightning that flickers more the deeper the ship records, the ship holding at
// the ring it records at. Every node is made once per site scene (children of _siteScene, freed with it) and moved each
// frame: no per-frame allocation. Called from UpdateSiteView after UpdateSiteWork.
public partial class OrbitView
{
    private const int StormBolts = 3;

    private Node3D? _eventFx;
    private string _eventFxSite = "";
    private MeshInstance3D? _eventBeam;
    private StandardMaterial3D? _eventBeamMat;
    private MeshInstance3D? _coma;
    private StandardMaterial3D? _comaMat, _tailMat;
    private CpuParticles3D? _jets, _stormCloud;
    private readonly MeshInstance3D?[] _bolts = new MeshInstance3D?[StormBolts];
    private StandardMaterial3D? _boltMat;
    private readonly float[] _boltUntil = new float[StormBolts];
    private readonly Vector3[] _boltA = new Vector3[StormBolts], _boltB = new Vector3[StormBolts];
    private OmniLight3D? _distressLight;
    private readonly List<Node3D> _podNodes = new();
    private readonly List<Vector3> _podRest = new();
    private readonly RandomNumberGenerator _fxRng = new() { Seed = 4242 };
    private Vector3 _shipSpot = new(SiteDrawSize * 0.78f, SiteDrawSize * 0.08f, SiteDrawSize * 0.28f);

    /// <summary>Where the flagship holds beside the site (in the site scene): the usual spot, or at an ion storm the ring
    /// it records at (eased there).</summary>
    private Vector3 SiteShipSpot(SpaceSite site, float delta)
    {
        var spot = new Vector3(SiteDrawSize * 0.78f, SiteDrawSize * 0.08f, SiteDrawSize * 0.28f);
        if (site.Kind == SiteKind.Anomaly && _source.State is { } sys && sys.WorkSiteOf(_source.Player) == site.Id && sys.SiteKindOf(site)?.Anomaly is { } an)
        {
            var action = sys.WorkActionOf(_source.Player);
            var ring = action switch { "record_outer" => an.Ring("outer"), "record_middle" => an.Ring("middle"), "record_inner" => an.Ring("inner"), _ => null };
            var outer = 0f;
            for (var i = 0; i < an.Rings.Count; i++) outer = Mathf.Max(outer, an.Rings[i].Radius.ToFloat());
            if (ring != null && outer > 0f)
                spot = spot.Normalized() * (SiteDrawSize * 0.5f * 0.95f * ring.Radius.ToFloat() / outer + SiteShipLength * 0.3f);
        }
        _shipSpot = _shipSpot.Lerp(spot, Mathf.Clamp(delta * 1.5f, 0f, 1f));
        return _shipSpot;
    }

    private void ClearEventFx()
    {
        // The nodes are children of the site scene, freed with it.
        _eventFx = null;
        _eventFxSite = "";
        _eventBeam = _coma = null;
        _eventBeamMat = _comaMat = _tailMat = _boltMat = null;
        _jets = _stormCloud = null;
        for (var i = 0; i < StormBolts; i++)
        {
            _bolts[i] = null;
            _boltUntil[i] = 0f;
        }
        _distressLight = null;
        _podNodes.Clear();
        _podRest.Clear();
        _shipSpot = new Vector3(SiteDrawSize * 0.78f, SiteDrawSize * 0.08f, SiteDrawSize * 0.28f);
    }

    private void UpdateSiteEvent(SpaceSite site, float delta)
    {
        if (_siteScene == null || _source.State is not { } sys || _shipHolder == null) return;
        if (site.Kind is not (SiteKind.RogueAsteroid or SiteKind.Comet or SiteKind.Lifepods or SiteKind.StrandedShip or SiteKind.Anomaly)) return;
        EnsureEventFx(site);
        var player = _source.Player;
        var tick = _source.Sim.Tick;
        var action = sys.WorkSiteOf(player) == site.Id ? sys.WorkActionOf(player) : null;
        var shipAt = _shipHolder.GlobalPosition;
        var centre = _siteScene.GlobalPosition;
        var over = site.Outcome.Length > 0;
        _eventBeam!.Visible = false;
        switch (site.Kind)
        {
            case SiteKind.RogueAsteroid:
                if (action == "push" && !over)
                {
                    var dir = (shipAt - centre).Normalized();
                    Stretch(_eventBeam, shipAt, centre + dir * (SiteDrawSize * 0.4f), 0.9f + 0.25f * Mathf.Sin(_siteTime * 9f));
                    _eventBeamMat!.AlbedoColor = new Color(0.45f, 0.75f, 1f, 0.35f + 0.15f * Mathf.Sin(_siteTime * 13f));
                    _eventBeam.Visible = true;
                }
                break;
            case SiteKind.Comet:
            {
                var (ws, we) = sys.TailWindow(site);
                var active = !over && tick >= ws && tick <= we;
                var pulse = 0.5f + 0.5f * Mathf.Sin(_siteTime * 1.7f);
                _comaMat!.AlbedoColor = new Color(0.75f, 0.88f, 1f, active ? 0.10f + 0.05f * pulse : 0.035f);
                if (_tailMat != null) _tailMat.AlbedoColor = new Color(0.7f, 0.85f, 1f, active ? 0.2f + 0.06f * pulse : 0.05f);
                _jets!.Emitting = active;
                if (action == "sample" && _siteModel != null)
                {
                    var tail = _siteModel.GlobalBasis.Z.Normalized(); // the tail runs along the nucleus's +Z
                    Stretch(_eventBeam, shipAt, centre + tail * (SiteDrawSize * 1.1f), 0.6f + 0.15f * Mathf.Sin(_siteTime * 11f));
                    _eventBeamMat!.AlbedoColor = new Color(0.4f, 1f, 0.95f, 0.3f + 0.15f * Mathf.Sin(_siteTime * 17f));
                    _eventBeam.Visible = true;
                }
                break;
            }
            case SiteKind.Lifepods:
            {
                // The pod being brought in: the waiting one with the least air.
                var next = -1;
                var least = long.MaxValue;
                for (var k = 0; k < _podNodes.Count && k < 31; k++)
                {
                    var gone = ((site.PodsTaken | site.PodsLost) & (1 << k)) != 0;
                    if (IsInstanceValid(_podNodes[k])) _podNodes[k].Visible = !gone;
                    if (gone || k >= site.PodCount) continue;
                    var air = sys.PodAirTick(site, k);
                    if (air < least)
                    {
                        least = air;
                        next = k;
                    }
                }
                for (var k = 0; k < _podNodes.Count; k++)
                    if (IsInstanceValid(_podNodes[k]) && k != next) _podNodes[k].Position = _podRest[k];
                if (action == "pickup" && next >= 0 && next < _podNodes.Count && IsInstanceValid(_podNodes[next]) && sys.SiteWorkProgress(player, site.Id) is { } work)
                {
                    var pod = _podNodes[next];
                    var rest = _siteModel!.ToGlobal(_podRest[next]);
                    var t = Mathf.Clamp(work.Share.ToFloat(), 0f, 1f);
                    pod.GlobalPosition = rest.Lerp(shipAt, t * t * 0.9f);
                    Stretch(_eventBeam, shipAt, pod.GlobalPosition, 0.45f);
                    _eventBeamMat!.AlbedoColor = new Color(0.45f, 0.75f, 1f, 0.4f + 0.15f * Mathf.Sin(_siteTime * 15f));
                    _eventBeam.Visible = true;
                }
                break;
            }
            case SiteKind.StrandedShip:
                if (_distressLight != null)
                {
                    var dark = !over && site.AirTick >= 0;
                    _distressLight.LightColor = dark ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.82f, 0.6f);
                    _distressLight.LightEnergy = over ? 0.2f : dark ? 1.2f + 1.2f * Mathf.Max(0f, Mathf.Sin(_siteTime * 5f)) : 1.4f;
                }
                break;
            case SiteKind.Anomaly:
            {
                _stormCloud!.Emitting = !over;
                // Lightning: more of it while the ship records deep inside.
                var rate = action switch { "record_inner" => 2.4f, "record_middle" => 1.2f, "record_outer" => 0.7f, _ => 0.45f };
                var r = SiteDrawSize * 0.5f;
                for (var i = 0; i < StormBolts; i++)
                {
                    var bolt = _bolts[i]!;
                    if (_siteTime > _boltUntil[i] + 0.15f && _fxRng.Randf() < rate * delta && !over)
                    {
                        _boltUntil[i] = _siteTime + _fxRng.RandfRange(0.06f, 0.18f);
                        _boltA[i] = RandomInBall(r * 0.35f);
                        _boltB[i] = action == "record_inner" && _fxRng.Randf() < 0.4f ? _siteScene.ToLocal(_shipHolder.GlobalPosition) * 0.9f : RandomInBall(r * 0.95f);
                    }
                    var on = _siteTime <= _boltUntil[i];
                    bolt.Visible = on;
                    if (on) Stretch(bolt, _siteScene.ToGlobal(_boltA[i]), _siteScene.ToGlobal(_boltB[i]), 0.25f + 0.2f * _fxRng.Randf());
                }
                _boltMat!.AlbedoColor = new Color(0.85f, 0.8f, 1f, 0.75f + 0.25f * _fxRng.Randf());
                break;
            }
        }
    }

    private Vector3 RandomInBall(float radius)
    {
        var v = new Vector3(_fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f));
        if (v.LengthSquared() > 1f) v = v.Normalized();
        return v * radius;
    }

    private void EnsureEventFx(SpaceSite site)
    {
        if (_eventFx != null && IsInstanceValid(_eventFx) && _eventFxSite == site.Id) return;
        _eventFx = new Node3D { Name = "SiteEventFx" };
        _eventFxSite = site.Id;
        _siteScene!.AddChild(_eventFx);
        _eventBeamMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = Colors.White,
        };
        _eventBeam = new MeshInstance3D
        {
            Name = "EventBeam",
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = _eventBeamMat,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _eventFx.AddChild(_eventBeam);
        switch (site.Kind)
        {
            case SiteKind.Comet:
            {
                _comaMat = Additive(new Color(0.75f, 0.88f, 1f, 0.08f));
                _coma = new MeshInstance3D
                {
                    Name = "Coma",
                    Mesh = new SphereMesh { Radius = SiteDrawSize * 0.62f, Height = SiteDrawSize * 1.24f, RadialSegments = 24, Rings = 12 },
                    MaterialOverride = _comaMat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _eventFx.AddChild(_coma);
                if (_siteModel?.FindChild("Tail", true, false) is MeshInstance3D { MaterialOverride: StandardMaterial3D tm }) _tailMat = tm;
                _jets = Cloud("Jets", 70, 2.6f, SiteDrawSize * 0.2f, new Color(0.85f, 0.93f, 1f, 0.35f), 1.6f, 3.2f, 9f, 18f);
                break;
            }
            case SiteKind.Lifepods:
                if (_siteModel != null)
                    foreach (var child in _siteModel.GetChildren())
                        if (child is Node3D pod)
                        {
                            _podNodes.Add(pod);
                            _podRest.Add(pod.Position);
                        }
                break;
            case SiteKind.StrandedShip:
                _distressLight = new OmniLight3D { Name = "Distress", OmniRange = SiteDrawSize * 0.9f, LightEnergy = 1.5f, Position = new Vector3(0f, SiteDrawSize * 0.22f, 0f) };
                _eventFx.AddChild(_distressLight);
                break;
            case SiteKind.Anomaly:
            {
                _stormCloud = Cloud("Plasma", 140, 5f, SiteDrawSize * 0.45f, new Color(0.65f, 0.58f, 1f, 0.16f), 5f, 11f, 0.5f, 2.5f);
                _boltMat = Additive(new Color(0.85f, 0.8f, 1f, 0.9f));
                for (var i = 0; i < StormBolts; i++)
                {
                    _bolts[i] = new MeshInstance3D
                    {
                        Name = $"Bolt{i}",
                        Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 6, Rings = 1 },
                        MaterialOverride = _boltMat,
                        Visible = false,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    _eventFx.AddChild(_bolts[i]);
                }
                break;
            }
        }
    }

    private static StandardMaterial3D Additive(Color colour) => new()
    {
        AlbedoColor = colour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
    };

    /// <summary>A soft additive particle cloud (billboards) in a ball of the radius round the scene's centre.</summary>
    private CpuParticles3D Cloud(string name, int amount, float life, float radius, Color colour, float sizeMin, float sizeMax, float speedMin, float speedMax)
    {
        var material = Additive(Colors.White);
        material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        var cloud = new CpuParticles3D
        {
            Name = name,
            Amount = amount,
            Lifetime = life,
            Emitting = true,
            LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius,
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = speedMin,
            InitialVelocityMax = speedMax,
            Gravity = Vector3.Zero,
            DampingMin = 0.5f,
            DampingMax = 1.5f,
            ScaleAmountMin = sizeMin,
            ScaleAmountMax = sizeMax,
            Color = colour,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new QuadMesh { Size = Vector2.One, Material = material },
        };
        _eventFx!.AddChild(cloud);
        return cloud;
    }
}
