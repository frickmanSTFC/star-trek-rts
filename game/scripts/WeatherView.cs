using System;
using Godot;

namespace FinalFrontier;

/// <summary>What the weather looks like at the view's centre right now (eased by PlanetDriver.Sky), 0..1 amounts.</summary>
public struct WeatherLook
{
    public float Rain, Snow, Dust, Fog, Cloud;
    public float Lightning;   // 0..1 how often bolts come (storms)
    public Vector3 Wind;      // world velocity of the air (m/s, horizontal): which way and how hard it blows
    public float Light;       // how lit the scene is (0 night .. 1 noon), for unshaded particles
    public bool DesertGround; // dust kicks up from the ground here (desert / dry plains)
}

/// <summary>
/// Falling weather around the surface camera (render-only, one emitter set; never the whole map): rain streaks with
/// splashes on the ground, snowflakes drifting on the wind, blowing dust, and lightning flashes (a directional flash
/// light; the driver also flashes the sky). The emitters sit between the camera and the ground it looks at and size
/// themselves to the zoom; from high above nothing falls (the haze and the wet/snowy ground carry it).
/// Child of PlanetDriver, not PlanetView, so the helm's surface feed (which shows PlanetView on its own layer and has
/// its own weather) doesn't see it; hidden in orbit.
/// </summary>
public partial class WeatherView : Node3D
{
    private GpuParticles3D _rain = null!, _snow = null!, _dust = null!;
    private ParticleProcessMaterial _rainProcess = null!, _snowProcess = null!, _dustProcess = null!;
    private StandardMaterial3D _rainDraw = null!, _snowDraw = null!, _dustDraw = null!, _splashDraw = null!;
    private CpuParticles3D _splash = null!;
    private DirectionalLight3D _flash = null!;
    private readonly Vector3[] _splashPoints = new Vector3[192];
    private Vector3 _splashCentre = new(float.NaN, 0, 0);
    private float _splashRadius;
    private float _scale = -1f, _extent = -1f, _height = -1f;
    private float _flashT = -1f, _nextBolt = 6f, _flashLevel;
    private readonly RandomNumberGenerator _rng = new() { Seed = 7331 };
    private float _time;

    /// <summary>Brightness of the current lightning flash (0..1), for the sky and ambient.</summary>
    public float Flash => _flashLevel;

    /// <summary>Blowing dust's colour (sRGB): the planet class's (PlanetLook.StormDust; rust on Veyra).</summary>
    public Color DustColour { get; set; } = new(0.78f, 0.58f, 0.36f);

    private const float RainLifetime = 1.6f;
    private const float SnowLifetime = 7f;

    public void Build()
    {
        var soft = new GradientTexture2D
        {
            Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
        };

        // Rain: thin boxes stretched along their velocity (align Y), so a streak reads from any side.
        _rainProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            Direction = new Vector3(0, -1, 0),
            Spread = 2f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 60f,
            InitialVelocityMax = 70f,
        };
        _rainProcess.SetParticleFlag(ParticleProcessMaterial.ParticleFlags.AlignYToVelocity, true);
        _rainDraw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.72f, 0.8f, 0.9f, 0.32f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _rain = Emitter("Rain", 6000, RainLifetime, _rainProcess, new BoxMesh { Size = new Vector3(0.05f, 1.8f, 0.05f), Material = _rainDraw });

        // Splashes: little spurts at points on the ground around the view's centre (refreshed as it moves).
        _splashDraw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoTexture = soft,
            AlbedoColor = new Color(0.8f, 0.86f, 0.95f, 0.5f),
        };
        _splash = new CpuParticles3D
        {
            Name = "Splash",
            Amount = 320,
            Lifetime = 0.35,
            LocalCoords = false,
            Emitting = false,
            Mesh = new QuadMesh { Size = new Vector2(0.35f, 0.35f), Material = _splashDraw },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Points,
            Direction = new Vector3(0, 1, 0),
            Spread = 35f,
            InitialVelocityMin = 2f,
            InitialVelocityMax = 4.5f,
            Gravity = new Vector3(0, -22f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_splash);

        // Snow: soft billboards drifting down with the wind and a little turbulence.
        _snowProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            Direction = new Vector3(0, -1, 0),
            Spread = 12f,
            Gravity = new Vector3(0, -1.5f, 0),
            InitialVelocityMin = 4f,
            InitialVelocityMax = 7f,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 2.5f,
            TurbulenceNoiseScale = 6f,
            TurbulenceInfluenceMin = 0.05f,
            TurbulenceInfluenceMax = 0.15f,
        };
        _snowDraw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoTexture = soft,
            AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
        };
        _snow = Emitter("Snow", 5000, SnowLifetime, _snowProcess, new QuadMesh { Size = new Vector2(0.3f, 0.3f), Material = _snowDraw });

        // Dust: big faint puffs racing downwind low over the ground.
        _dustProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            Direction = new Vector3(1, 0, 0),
            Spread = 10f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 12f,
            InitialVelocityMax = 20f,
            ScaleMin = 0.6f,
            ScaleMax = 1.6f,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 3f,
            TurbulenceNoiseScale = 9f,
            TurbulenceInfluenceMin = 0.05f,
            TurbulenceInfluenceMax = 0.2f,
        };
        _dustDraw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoTexture = soft,
            AlbedoColor = DustColour with { A = 0.14f },
        };
        _dust = Emitter("Dust", 900, 3.0f, _dustProcess, new QuadMesh { Size = new Vector2(9f, 9f), Material = _dustDraw });

        // Lightning: a cold-white light from high up, flashed for a few frames.
        _flash = new DirectionalLight3D
        {
            Name = "LightningFlash",
            LightColor = new Color(0.82f, 0.87f, 1f),
            LightEnergy = 0f,
            ShadowEnabled = false,
            Visible = false,
            RotationDegrees = new Vector3(-72f, 30f, 0f),
        };
        AddChild(_flash);
    }

    private GpuParticles3D Emitter(string name, int amount, float lifetime, ParticleProcessMaterial process, Mesh mesh)
    {
        var p = new GpuParticles3D
        {
            Name = name,
            Amount = amount,
            AmountRatio = 0f,
            Lifetime = lifetime,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-300, -300, -300), new Vector3(600, 600, 600)),
        };
        AddChild(p);
        return p;
    }

    /// <summary>
    /// Once a frame on the surface. <paramref name="focus"/> = the ground point the camera looks at,
    /// <paramref name="eye"/> = the camera, <paramref name="distance"/> = its zoom distance.
    /// </summary>
    public void Update(in WeatherLook w, Vector3 focus, Vector3 eye, float distance, Func<Vector3, float> groundAt, float delta)
    {
        _time += delta;
        // Nothing falls from high above: the particles would be specks.
        var near = 1f - Mathf.SmoothStep(500f, 900f, distance);
        var extent = Mathf.Clamp(distance * 0.55f, 30f, 260f);
        var height = Mathf.Clamp(distance * 0.35f, 18f, 140f);
        var scale = Mathf.Clamp(distance / 70f, 0.7f, 4f);
        var centre = focus.Lerp(eye, 0.3f);
        Position = Vector3.Zero;

        // Sizes follow the zoom (only rewritten when they change by more than a few percent).
        if (Mathf.Abs(extent - _extent) > _extent * 0.05f || Mathf.Abs(scale - _scale) > _scale * 0.05f || Mathf.Abs(height - _height) > _height * 0.05f)
        {
            _extent = extent;
            _scale = scale;
            _height = height;
            var box = new Vector3(extent, height, extent);
            _rainProcess.EmissionBoxExtents = box;
            _snowProcess.EmissionBoxExtents = box;
            _dustProcess.EmissionBoxExtents = new Vector3(extent, Mathf.Min(height, 6f + scale * 3f), extent);
            _rainProcess.ScaleMin = _rainProcess.ScaleMax = scale;
            _snowProcess.ScaleMin = scale * 0.7f;
            _snowProcess.ScaleMax = scale * 1.3f;
            // Fall the whole box in a lifetime, whatever the zoom.
            var fall = Mathf.Max(55f, 2f * height / RainLifetime);
            _rainProcess.InitialVelocityMin = fall;
            _rainProcess.InitialVelocityMax = fall * 1.15f;
            var drift = Mathf.Max(4f, 2f * height / SnowLifetime);
            _snowProcess.InitialVelocityMin = drift * 0.8f;
            _snowProcess.InitialVelocityMax = drift * 1.2f;
            var aabb = new Aabb(new Vector3(-extent - 80f, -height * 2f - 80f, -extent - 80f), new Vector3(2f * extent + 160f, height * 4f + 160f, 2f * extent + 160f));
            _rain.VisibilityAabb = aabb;
            _snow.VisibilityAabb = aabb;
            _dust.VisibilityAabb = aabb;
        }

        // Rain falls on the wind (slanted streaks in storms).
        var windH = new Vector3(w.Wind.X, 0, w.Wind.Z);
        var rainDir = (Vector3.Down * 9f + windH * 0.45f).Normalized();
        _rainProcess.Direction = rainDir;
        _rain.GlobalPosition = centre + Vector3.Up * height * 0.4f;
        SetAmount(_rain, w.Rain * near);
        var light = Mathf.Lerp(0.12f, 1f, w.Light) * (1f + 2f * _flashLevel);
        _rainDraw.AlbedoColor = new Color(0.72f * light, 0.8f * light, 0.9f * light, 0.18f + 0.22f * w.Rain);

        _snowProcess.Direction = (Vector3.Down * 3f + windH * 0.35f).Normalized();
        _snow.GlobalPosition = centre + Vector3.Up * height * 0.4f;
        SetAmount(_snow, w.Snow * near);
        var snowLight = Mathf.Lerp(0.18f, 1f, w.Light) * (1f + 1.5f * _flashLevel);
        _snowDraw.AlbedoColor = new Color(snowLight, snowLight, snowLight * 1.03f, 0.9f);

        // Dust: low over the ground at the view's centre, blowing downwind.
        var windLength = windH.Length();
        if (windLength > 0.1f) _dustProcess.Direction = (windH / windLength + Vector3.Up * 0.05f).Normalized();
        var gust = Mathf.Max(8f, windLength * 1.4f);
        _dustProcess.InitialVelocityMin = gust * 0.7f;
        _dustProcess.InitialVelocityMax = gust * 1.2f;
        _dustProcess.ScaleMin = scale * 0.6f;
        _dustProcess.ScaleMax = scale * 1.6f;
        _dust.GlobalPosition = focus with { Y = groundAt(focus) + Mathf.Min(height, 6f + scale * 3f) * 0.6f };
        SetAmount(_dust, w.Dust * near);
        var dustLight = Mathf.Lerp(0.1f, 1f, w.Light);
        _dustDraw.AlbedoColor = new Color(DustColour.R * dustLight, DustColour.G * dustLight, DustColour.B * dustLight, 0.08f + 0.14f * w.Dust);

        // Splashes where it rains near the camera (not from far up).
        var splashOn = w.Rain > 0.12f && distance < 380f;
        if (splashOn)
        {
            var radius = Mathf.Clamp(distance * 0.5f, 20f, 160f);
            if (float.IsNaN(_splashCentre.X) || _splashCentre.DistanceTo(focus) > radius * 0.25f || Mathf.Abs(radius - _splashRadius) > _splashRadius * 0.2f)
            {
                _splashCentre = focus;
                _splashRadius = radius;
                for (var i = 0; i < _splashPoints.Length; i++)
                {
                    var a = _rng.Randf() * Mathf.Tau;
                    var r = Mathf.Sqrt(_rng.Randf()) * radius;
                    var p = focus + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    _splashPoints[i] = p with { Y = groundAt(p) + 0.15f };
                }
                _splash.EmissionPoints = _splashPoints;
            }
            _splash.ScaleAmountMin = 0.6f * scale;
            _splash.ScaleAmountMax = 1.2f * scale;
            _splashDraw.AlbedoColor = new Color(0.8f * light, 0.86f * light, 0.95f * light, 0.25f + 0.3f * w.Rain);
        }
        if (_splash.Emitting != splashOn) _splash.Emitting = splashOn;

        UpdateLightning(w.Lightning, delta);
    }

    private static void SetAmount(GpuParticles3D p, float amount)
    {
        var ratio = Mathf.Clamp(amount, 0f, 1f);
        var on = ratio > 0.01f;
        if (!Mathf.IsEqualApprox(p.AmountRatio, ratio)) p.AmountRatio = ratio;
        if (p.Emitting != on) p.Emitting = on;
    }

    private void UpdateLightning(float storm, float delta)
    {
        if (storm > 0.05f)
        {
            _nextBolt -= delta * storm;
            if (_nextBolt <= 0f && _flashT < 0f)
            {
                _flashT = 0f;
                _nextBolt = _rng.RandfRange(3f, 11f);
                _flash.RotationDegrees = new Vector3(-_rng.RandfRange(45f, 80f), _rng.RandfRange(0f, 360f), 0f);
            }
        }
        if (_flashT >= 0f)
        {
            _flashT += delta;
            // Two or three hard spikes over a third of a second, then a dim flicker out.
            var t = _flashT;
            var f = t < 0.06f ? 1f : t < 0.12f ? 0.15f : t < 0.2f ? 0.8f : t < 0.26f ? 0.1f : t < 0.32f ? 0.45f : Mathf.Max(0f, 0.3f - (t - 0.32f) * 1.5f);
            _flashLevel = f;
            if (t > 0.6f)
            {
                _flashT = -1f;
                _flashLevel = 0f;
            }
        }
        _flash.LightEnergy = _flashLevel * 3.2f;
        var visible = _flashLevel > 0.001f;
        if (_flash.Visible != visible) _flash.Visible = visible;
    }

    /// <summary>Stop everything at once (leaving the surface).</summary>
    public void Stop()
    {
        _rain.Emitting = false;
        _snow.Emitting = false;
        _dust.Emitting = false;
        _splash.Emitting = false;
        _flashT = -1f;
        _flashLevel = 0f;
        _flash.Visible = false;
    }
}
