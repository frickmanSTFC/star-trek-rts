using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Visual pass G2 (docs/visual-pass-contract.md), GraphicsQuality.Effects: small extra effects near the camera.
// Render-only, everything under one "VisualEffects" root (not under any model, so people boxes and the build-piece
// printer never see it), built on first use, freed when the flag goes off (the scene is then exactly as before).
//  - Heat shimmer (shaders/heat_haze.gdshader): over the stack tips of working refineries (Smoke0/1), foundries (the
//    stack), fusion reactors and life support plants (Vent0/1), and under drop ships burning low (DropShipView.Burning).
//    HazeCap columns: ShipHaze for ships (every frame), the rest for stacks (re-chosen once a second, nearest first).
//  - Embers: sparks drifting up from working furnaces (refinery FurnaceLight, foundry crucible), EmberCap nearest.
//  - Dust motes: one GPU particle cloud in front of a low camera by day, catching the light, drifting on turbulence.
//  - Rotor downwash: a ring of dust under scout drones flying low (DownwashCap).
// Rivals' buildings and drones only while shown live (ShowsLive). No allocation per frame (fixed pools, reused lists).
public partial class PlanetView
{
    private const int HazeCap = 14, ShipHaze = 3, EmberCap = 8, DownwashCap = 4;
    private const float EffectRange = 300f, EmberRange = 220f, MoteCeiling = 90f;

    private sealed class HeatSite
    {
        public required Node3D Model;
        public required int Owner;
        public required int Tile;
        public readonly Node3D?[] Tips = new Node3D?[2];
        public readonly Vector3[] TipOffset = new Vector3[2];
        public float Width = 2.4f, Height = 7f;
        public Node3D? Furnace;
        public Vector3 FurnaceOffset;
        public bool Working;
    }

    private Node3D? _effectsRoot;
    private bool _effectsOn;
    private readonly Dictionary<int, HeatSite> _heatSites = new();
    private readonly List<int> _effectScratch = new();
    private readonly List<(float Dist, Vector3 At, float Width, float Height)> _hazeCandidates = new();
    private readonly List<(float Dist, Vector3 At)> _emberCandidates = new();
    private MeshInstance3D[] _shimmer = System.Array.Empty<MeshInstance3D>();
    private ShaderMaterial[] _shimmerMaterial = System.Array.Empty<ShaderMaterial>();
    private CpuParticles3D[] _embers = System.Array.Empty<CpuParticles3D>();
    private CpuParticles3D[] _downwash = System.Array.Empty<CpuParticles3D>();
    private GpuParticles3D? _motes;
    private StandardMaterial3D? _moteMaterial;
    private readonly Vector3[] _shipUnder = new Vector3[ShipHaze];
    private readonly float[] _shipThrottle = new float[ShipHaze], _shipHeight = new float[ShipHaze];
    private float _effectTick = 999f;

    // ---------------------------------------------------------------- switching

    private void ApplyEffects(bool on)
    {
        if (on == _effectsOn) return;
        _effectsOn = on;
        if (on)
        {
            BuildEffects();
            _effectTick = 999f;
            return;
        }
        _effectsRoot?.QueueFree();
        _effectsRoot = null;
        _shimmer = System.Array.Empty<MeshInstance3D>();
        _shimmerMaterial = System.Array.Empty<ShaderMaterial>();
        _embers = System.Array.Empty<CpuParticles3D>();
        _downwash = System.Array.Empty<CpuParticles3D>();
        _motes = null;
        _moteMaterial = null;
        _heatSites.Clear();
    }

    private void BuildEffects()
    {
        _effectsRoot = new Node3D { Name = "VisualEffects" };
        AddChild(_effectsRoot);

        var hazeShader = GD.Load<Shader>("res://shaders/heat_haze.gdshader");
        var quad = new QuadMesh { Size = Vector2.One };
        _shimmer = new MeshInstance3D[HazeCap];
        _shimmerMaterial = new ShaderMaterial[HazeCap];
        for (var i = 0; i < HazeCap; i++)
        {
            var material = new ShaderMaterial { Shader = hazeShader, RenderPriority = -2 };
            material.SetShaderParameter("seed", i * 0.37f);
            _shimmerMaterial[i] = material;
            _shimmer[i] = new MeshInstance3D
            {
                Name = "HeatHaze",
                Mesh = quad,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                ExtraCullMargin = 4f,
            };
            _effectsRoot.AddChild(_shimmer[i]);
        }

        _embers = new CpuParticles3D[EmberCap];
        var emberMesh = new QuadMesh { Size = new Vector2(0.08f, 0.08f), Material = GlowSpeck() };
        var emberRamp = new Gradient
        {
            Offsets = new[] { 0f, 0.25f, 0.7f, 1f },
            Colors = new[] { new Color(1f, 0.85f, 0.45f, 1f), new Color(1f, 0.55f, 0.15f, 1f), new Color(0.8f, 0.2f, 0.05f, 0.7f), new Color(0.4f, 0.08f, 0.02f, 0f) },
        };
        for (var i = 0; i < EmberCap; i++)
        {
            _embers[i] = new CpuParticles3D
            {
                Name = "Embers",
                Amount = 18,
                Lifetime = 2.4f,
                Emitting = false,
                LocalCoords = false,
                Mesh = emberMesh,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.7f,
                Direction = Vector3.Up,
                Spread = 28f,
                InitialVelocityMin = 0.8f,
                InitialVelocityMax = 2.4f,
                Gravity = new Vector3(0.15f, 0.35f, 0f),
                DampingMin = 0.2f,
                DampingMax = 0.6f,
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.3f,
                ColorRamp = emberRamp,
                Visible = false,
            };
            _effectsRoot.AddChild(_embers[i]);
        }

        _downwash = new CpuParticles3D[DownwashCap];
        var puff = new QuadMesh { Size = new Vector2(0.9f, 0.9f), Material = SoftPuff() };
        for (var i = 0; i < DownwashCap; i++)
        {
            _downwash[i] = new CpuParticles3D
            {
                Name = "Downwash",
                Amount = 36,
                Lifetime = 1.3f,
                Emitting = false,
                LocalCoords = false,
                Mesh = puff,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.8f,
                Direction = Vector3.Right,
                Spread = 180f,
                Flatness = 0.95f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 7f,
                DampingMin = 3f,
                DampingMax = 5f,
                Gravity = new Vector3(0f, 0.25f, 0f),
                ScaleAmountMin = 0.7f,
                ScaleAmountMax = 1.8f,
                ColorRamp = new Gradient
                {
                    Offsets = new[] { 0f, 0.2f, 1f },
                    Colors = new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f) },
                },
                Visible = false,
            };
            _effectsRoot.AddChild(_downwash[i]);
        }

        // Dust motes: tiny glinting specks, drifting on turbulence, in a box in front of the camera.
        _moteMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1f, 0.93f, 0.8f, 0.5f),
            AlbedoTexture = SoftDot(),
            DisableReceiveShadows = true,
        };
        _moteMaterial.SetMeta(VehicleLights.NoBoostMeta, true);
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(14f, 5f, 14f),
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 0.02f,
            InitialVelocityMax = 0.15f,
            Gravity = new Vector3(0f, -0.01f, 0f),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 1.2f,
            TurbulenceNoiseScale = 7f,
            TurbulenceNoiseSpeed = new Vector3(0.05f, 0.02f, 0.05f),
            TurbulenceInfluenceMin = 0.04f,
            TurbulenceInfluenceMax = 0.1f,
            ScaleMin = 0.6f,
            ScaleMax = 1.4f,
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Offsets = new[] { 0f, 0.2f, 0.8f, 1f },
                    Colors = new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f) },
                },
            },
        };
        _motes = new GpuParticles3D
        {
            Name = "DustMotes",
            Amount = 90,
            Lifetime = 7f,
            Preprocess = 3f,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.06f, 0.06f), Material = _moteMaterial },
            VisibilityAabb = new Aabb(new Vector3(-40f, -15f, -40f), new Vector3(80f, 30f, 80f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _effectsRoot.AddChild(_motes);
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Place this frame's shimmer, embers, motes and downwash (Refresh, after the drop ships and drones).</summary>
    private void UpdateEffects(float delta)
    {
        if (!_effectsOn || _effectsRoot == null) return;
        _effectTick += delta;
        if (_effectTick >= 1f)
        {
            _effectTick = 0f;
            RefreshHeatSites();
            PlaceStacks();
        }
        PlaceShipHaze();
        PlaceDownwash();
        PlaceMotes();
    }

    /// <summary>Once a second: the working hot buildings (their tips found once per model).</summary>
    private void RefreshHeatSites()
    {
        var list = _surface.Structures;
        _effectScratch.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var kind = s.DefId switch
            {
                "refinery" => 1,
                "foundry" => 2,
                "fusion_reactor" or "life_support_plant" => 3,
                _ => 0,
            };
            if (kind == 0 || s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            _effectScratch.Add(s.Id);
            if (!_heatSites.TryGetValue(s.Id, out var site) || site.Model != v.Model)
                _heatSites[s.Id] = site = FindHeat(kind, v.Model, s.Owner, s.Tile);
            site.Working = (s.Status is StructureStatus.Working or StructureStatus.LowPower) && v.Model.Visible
                && ShowsLive(s.Owner, s.Tile);
        }
        if (_heatSites.Count > _effectScratch.Count)
        {
            var gone = new List<int>(); // only when a hot building went (rare)
            foreach (var id in _heatSites.Keys)
                if (!_effectScratch.Contains(id))
                    gone.Add(id);
            foreach (var id in gone) _heatSites.Remove(id);
        }
    }

    private static HeatSite FindHeat(int kind, Node3D model, int owner, int tile)
    {
        var site = new HeatSite { Model = model, Owner = owner, Tile = tile };
        switch (kind)
        {
            case 1: // refinery: two stacks, the furnace
                site.Tips[0] = model.FindChild("Smoke0", true, false) as Node3D;
                site.Tips[1] = model.FindChild("Smoke1", true, false) as Node3D;
                site.Furnace = model.FindChild("FurnaceLight", true, false) as Node3D;
                site.Width = 2.6f;
                site.Height = 8f;
                break;
            case 2: // foundry (primitive): the stack is the cylinder of top radius 0.55; the crucible in front
                foreach (var child in model.GetChildren())
                    if (child is MeshInstance3D { Mesh: CylinderMesh c } mi && Mathf.IsEqualApprox(c.TopRadius, 0.55f))
                    {
                        site.Tips[0] = mi;
                        site.TipOffset[0] = new Vector3(0f, c.Height / 2f, 0f);
                        break;
                    }
                site.Furnace = model;
                site.FurnaceOffset = new Vector3(0f, 1.9f, 1.2f);
                site.Width = 2.2f;
                site.Height = 6.5f;
                break;
            default: // reactor / life support: the vents
                site.Tips[0] = model.FindChild("Vent0", true, false) as Node3D;
                site.Tips[1] = model.FindChild("Vent1", true, false) as Node3D;
                site.Width = 3.2f;
                site.Height = 8f;
                break;
        }
        return site;
    }

    /// <summary>Stack shimmer and embers for the nearest working sites (once a second; stacks don't move).</summary>
    private void PlaceStacks()
    {
        _hazeCandidates.Clear();
        _emberCandidates.Clear();
        foreach (var site in _heatSites.Values)
        {
            if (!site.Working || !IsInstanceValid(site.Model) || !site.Model.IsInsideTree()) continue;
            for (var k = 0; k < 2; k++)
                if (site.Tips[k] is { } tip && IsInstanceValid(tip) && tip.IsInsideTree())
                {
                    var at = ToLocal(tip.ToGlobal(site.TipOffset[k]));
                    var d = at.DistanceTo(_eye);
                    if (d < EffectRange) _hazeCandidates.Add((d, at, site.Width, site.Height));
                }
            if (site.Furnace is { } furnace && IsInstanceValid(furnace) && furnace.IsInsideTree())
            {
                var at = ToLocal(furnace.ToGlobal(site.FurnaceOffset));
                var d = at.DistanceTo(_eye);
                if (d < EmberRange) _emberCandidates.Add((d, at));
            }
        }
        _hazeCandidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        _emberCandidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        for (var i = ShipHaze; i < HazeCap; i++)
        {
            var c = i - ShipHaze;
            if (c >= _hazeCandidates.Count)
            {
                if (_shimmer[i].Visible) _shimmer[i].Visible = false;
                continue;
            }
            var (_, at, width, height) = _hazeCandidates[c];
            _shimmer[i].Transform = new Transform3D(Basis.FromScale(new Vector3(width, height, 1f)), at + new Vector3(0f, height * 0.45f, 0f));
            _shimmerMaterial[i].SetShaderParameter("heat", 1f);
            if (!_shimmer[i].Visible) _shimmer[i].Visible = true;
        }
        for (var i = 0; i < EmberCap; i++)
        {
            var on = i < _emberCandidates.Count;
            if (on) _embers[i].Position = _emberCandidates[i].At;
            if (_embers[i].Emitting != on) _embers[i].Emitting = on;
            if (on && !_embers[i].Visible) _embers[i].Visible = true;
        }
    }

    /// <summary>Shimmer under drop ships burning low: a wide column from the ground up to the ship (every frame).</summary>
    private void PlaceShipHaze()
    {
        var n = _dropShips.Burning(_shipUnder, _shipThrottle, _shipHeight);
        for (var i = 0; i < ShipHaze; i++)
        {
            var on = i < n && _shipUnder[i].DistanceTo(_eye) < EffectRange;
            if (_shimmer[i].Visible != on) _shimmer[i].Visible = on;
            if (!on) continue;
            var h = Mathf.Clamp(_shipHeight[i], 4f, 26f) + 3f;
            _shimmer[i].Transform = new Transform3D(Basis.FromScale(new Vector3(13f, h, 1f)), _shipUnder[i] + new Vector3(0f, h * 0.42f, 0f));
            _shimmerMaterial[i].SetShaderParameter("heat", Mathf.Clamp(_shipThrottle[i] * 1.3f, 0f, 1.4f) * (1f - Mathf.Clamp((_shipHeight[i] - 40f) / 30f, 0f, 1f)));
        }
    }

    /// <summary>Dust blown out under scout drones flying low over dry ground.</summary>
    private void PlaceDownwash()
    {
        var n = 0;
        foreach (var v in _scoutDrones.Values)
        {
            if (n >= DownwashCap) break;
            if (!v.Placed || !v.Holder.Visible || v.Crashed || v.RotorSpeed < 20f || v.Alt > 7f) continue;
            var ground = v.Pos with { Y = GroundAt(v.Pos) };
            if (v.Pos.Y - ground.Y > 7f || ground.DistanceTo(_eye) > EffectRange) continue;
            var tile = TileAt(ground);
            var kind = tile >= 0 ? SprayRig.KindFor(_surface.Map, tile, FrostAt(tile)) : SprayKind.None;
            if (kind is SprayKind.None or SprayKind.Water) continue;
            var p = _downwash[n];
            p.Position = ground + new Vector3(0f, 0.25f, 0f);
            var colour = kind is SprayKind.Snow or SprayKind.FrostGrit ? new Color(0.92f, 0.94f, 0.97f) : Look.SprayDust with { A = 1f };
            if (p.Color != colour) p.Color = colour;
            p.SpeedScale = Mathf.Clamp(1.4f - (v.Pos.Y - ground.Y) / 7f, 0.5f, 1.2f); // livelier the lower it flies
            if (!p.Emitting) p.Emitting = true;
            if (!p.Visible) p.Visible = true;
            n++;
        }
        for (var i = n; i < DownwashCap; i++)
            if (_downwash[i].Emitting) _downwash[i].Emitting = false;
    }

    /// <summary>Motes drift in front of a low camera by day in calm, dry air.</summary>
    private void PlaceMotes()
    {
        if (_motes == null || _moteMaterial == null) return;
        var camera = GetViewport()?.GetCamera3D();
        var on = false;
        if (camera != null && IsVisibleInTree() && camera.GlobalPosition.DistanceSquaredTo(ToGlobal(_eye)) < 4f)
        {
            var eye = _eye;
            var height = eye.Y - GroundAt(eye);
            var forward = ToLocal(camera.GlobalPosition - camera.GlobalBasis.Z) - eye;
            var target = eye + forward.Normalized() * 22f;
            target.Y = GroundAt(target) + 3.5f;
            var day = _skyDaylightAt?.Invoke(target) ?? 1f;
            on = height < MoteCeiling && day > 0.3f && RainNow < 0.15f;
            if (on)
            {
                _motes.Position = target;
                var alpha = 0.5f * Mathf.Clamp((day - 0.3f) / 0.3f, 0f, 1f) * Mathf.Clamp((MoteCeiling - height) / 30f, 0f, 1f);
                if (Mathf.Abs(_moteMaterial.AlbedoColor.A - alpha) > 0.02f) _moteMaterial.AlbedoColor = _moteMaterial.AlbedoColor with { A = alpha };
            }
        }
        if (_motes.Emitting != on) _motes.Emitting = on;
        if (on && !_motes.Visible) _motes.Visible = true;
    }

    // ---------------------------------------------------------------- materials

    private static StandardMaterial3D GlowSpeck()
    {
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(2.2f, 1.6f, 1f),
            AlbedoTexture = SoftDot(),
        };
        m.SetMeta(VehicleLights.NoBoostMeta, true);
        return m;
    }

    private static StandardMaterial3D SoftPuff()
    {
        var m = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = SoftDot(),
            Roughness = 1f,
            DisableReceiveShadows = true,
        };
        m.SetMeta(VehicleLights.NoBoostMeta, true);
        return m;
    }

    private static GradientTexture2D? _softDot;

    private static GradientTexture2D SoftDot() => _softDot ??= new GradientTexture2D
    {
        Width = 32,
        Height = 32,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 1f },
            Colors = new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f) },
        },
    };
}
