using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>What the orbital scan overlay shows.</summary>
public enum ScanMode
{
    Terrain,
    Resources,
    Lifeforms,
    Biologics,
    Landing,
}

/// <summary>
/// The helm viewscreen in orbit. Four displays (OrbitDisplay): Planet — the globe you turn and zoom, its scans as
/// overlays (resources, lifeforms, biologics, landing safety), an optional lat/long wireframe grid, and markers for
/// bases, vehicles, satellites and things on their way down; Orbit — the same globe further out with the tracks of
/// our ship, the survey satellites and the moon; Atmosphere — full clouds, no scan, storm-cell rings where the low
/// cloud layer is densest; System — a top-down plot of the star system from the map's bodies (SystemPlot). The
/// globe's texture comes straight from the sim's tile map, laid around the sphere (the map wraps east–west; the poles
/// beyond its rows are ice). SetViewRect shifts the framing into the part of the window the helm frame leaves open.
/// Render-only: nothing here changes the sim; picking turns a mouse position into a tile for the driver's commands.
/// </summary>
public partial class OrbitView : Node3D
{
    public const float Radius = 100f;

    /// <summary>The map's rows cover this band of the globe (v from the north pole, 0..1); beyond it are polar caps.</summary>
    private const float VTop = 0.14f, VBottom = 0.86f;

    private const int TextureWidth = 2048, TextureHeight = 1024;

    /// <summary>Thickness of the air shell above the surface (world units; the clouds sit at 1.4 and 2.4).</summary>
    private const float AtmosphereHeight = 3.5f;

    private const int PhotoSize = 256;

    /// <summary>Altitudes of the drawn orbits (world units from the centre; render-only, the sim has no orbital heights).</summary>
    private const float SatelliteOrbit = Radius + 14f, ShipOrbit = Radius + 32f, MoonOrbit = 265f;

    /// <summary>The Orbit display pulls the camera this much further out, so the moon's track fits.</summary>
    private const float OrbitZoomOut = 1.7f;

    private static readonly Color Cyan = SystemPlot.Cyan;
    private static readonly Color Peach = SystemPlot.Peach;
    private static readonly Color SkyBlue = new(0.5f, 0.82f, 1f);
    private static readonly Color Lavender = new(0.77f, 0.65f, 1f);
    private static readonly Color DropColour = new(1f, 0.65f, 0.3f);
    private static readonly Color BeamColour = new(0.6f, 0.85f, 1f);

    private Simulation _sim = null!;
    private PlanetSurface _surface = null!;
    private int _localPlayer;
    private string _factionId = "";

    private Node3D _globe = null!;
    private MeshInstance3D _clouds = null!;
    private MeshInstance3D _highClouds = null!;
    private ShaderMaterial _groundMaterial = null!;
    private Node3D _moon = null!;
    private Node3D _moonRock = null!;
    private MeshInstance3D _moonTrack = null!;
    private BodyDef? _moonBody;
    private MeshInstance3D _atmosphere = null!;
    private MeshInstance3D _grid = null!;
    private Node3D _shipOrbit = null!;
    private Node3D _shipMarker = null!;
    private Node3D _weather = null!;
    private SystemPlot _system = null!;
    private byte[] _cloudData = System.Array.Empty<byte>();
    private readonly List<(Vector3 Dir, float Density)> _stormCells = new();
    private float _cloudCover;
    private float _toxicShare, _meanBiomass;
    private long _lightsStamp = -1;
    private Vector3 _sunDir;
    private MeshInstance3D _overlay = null!;
    private StandardMaterial3D _overlayMaterial = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _light = null!;
    private MeshInstance3D _sweep = null!;
    private MeshInstance3D _hover = null!;
    private StandardMaterial3D _hoverMaterial = null!;
    private Node3D _native = null!;

    private readonly ImageTexture?[] _overlayTextures = new ImageTexture?[5];
    private readonly long[] _overlayStamp = { -1, -1, -1, -1, -1 };
    private readonly Dictionary<int, Node3D> _baseMarkers = new();
    private readonly Dictionary<int, MeshInstance3D> _unitMarkers = new();
    private readonly Dictionary<int, Node3D> _deliveryMarkers = new();
    private readonly Dictionary<int, (Node3D Sat, MeshInstance3D Band, MeshInstance3D Track)> _satMarkers = new();

    private float _yaw, _pitch, _yawTarget, _pitchTarget;
    private float _distance = 330f, _distanceTarget = 330f;
    private float _sweepTime = -1f;
    private float _time;

    // Display framing, eased: camera elevation above the globe's equator plane, the Orbit display's zoom-out, and the
    // System plot's own zoom and turn (it ignores the globe's yaw/pitch/distance).
    private float _elevation, _zoomOut = 1f;
    private float _systemDistance = 430f, _systemDistanceTarget = 430f, _systemYaw, _systemYawTarget;
    private bool _gridOn;
    private string? _currentBodyName;
    private Rect2? _viewRect;
    private Vector2 _viewScreen;

    public ScanMode Mode { get; private set; } = ScanMode.Landing;
    public Camera3D Camera => _camera;

    /// <summary>What the viewscreen shows (SetDisplay).</summary>
    public OrbitDisplay Display { get; private set; } = OrbitDisplay.Planet;

    /// <summary>The lat/long wireframe over the globe (shown on the Planet display only).</summary>
    public bool Grid
    {
        get => _gridOn;
        set
        {
            _gridOn = value;
            ApplyDisplay();
        }
    }

    /// <summary>
    /// Name (or map id) of the body the surface is on, matched against the map's bodies: it gets the peach ring in the
    /// System plot, and its first moon in the data becomes the moon in orbit. Null (the default) = the local player's
    /// home body on the map (Tarsis on mvp_duel, the same name PlanetDriver.PlanetName uses).
    /// </summary>
    public string? CurrentBodyName
    {
        get => _currentBodyName;
        set
        {
            _currentBodyName = value;
            if (_system != null) RebuildSystem();
        }
    }

    // ---------------------------------------------------------------- setup

    public void Build(Simulation sim, int localPlayer, string factionId)
    {
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;
        _factionId = factionId;

        // KeepAspect Height: Fov is the vertical angle, which FrameViewRect relies on.
        _camera = new Camera3D { Fov = 45f, Near = 0.5f, Far = 9000f, KeepAspect = Camera3D.KeepAspectEnum.Height, Position = new Vector3(0, 0, _distance) };
        AddChild(_camera);

        // Light from the front left, so the side you look at is lit with the terminator off to one side.
        _light = new DirectionalLight3D { LightEnergy = 1.7f, LightColor = new Color(1f, 0.97f, 0.9f), ShadowEnabled = false };
        AddChild(_light);
        // From the left and a little in front: about a third of the globe is in night, so the terminator, the sunset band and city lights show.
        _light.LookAtFromPosition(new Vector3(-1.0f, 0.35f, 0.55f) * 100f, Vector3.Zero, Vector3.Up);
        _sunDir = _light.GlobalTransform.Basis.Z.Normalized(); // a directional light shines along -Z, so +Z points at the sun

        AddChild(Nebula());
        AddChild(Stars());

        _globe = new Node3D { Name = "Globe" };
        AddChild(_globe);
        var ground = new MeshInstance3D { Mesh = SphereMesh(Radius, 128, 256) };
        var painted = OrbitPainter.Paint(_surface.Map, TextureWidth, TextureHeight, VTop, VBottom);
        painted.Albedo.GenerateMipmaps();
        painted.Water.GenerateMipmaps();
        painted.Normal.GenerateMipmaps();
        painted.SplatA.GenerateMipmaps();
        painted.SplatB.GenerateMipmaps();
        var albedo = ImageTexture.CreateFromImage(painted.Albedo);
        var water = ImageTexture.CreateFromImage(painted.Water);
        var normal = ImageTexture.CreateFromImage(painted.Normal);
        _groundMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet.gdshader") };
        _groundMaterial.SetShaderParameter("albedo_tex", albedo);
        _groundMaterial.SetShaderParameter("water_tex", water);
        _groundMaterial.SetShaderParameter("normal_tex", normal);
        _groundMaterial.SetShaderParameter("splat_a", ImageTexture.CreateFromImage(painted.SplatA));
        _groundMaterial.SetShaderParameter("splat_b", ImageTexture.CreateFromImage(painted.SplatB));
        _groundMaterial.SetShaderParameter("photo_layers", PhotoLayers());
        _groundMaterial.SetShaderParameter("detail_normal", new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = 6f,
            Noise = new FastNoiseLite { Seed = 78, Frequency = 0.03f, FractalOctaves = 5, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth },
        });
        _groundMaterial.SetShaderParameter("sun_dir", _sunDir);
        _groundMaterial.SetShaderParameter("planet_radius", Radius);
        _groundMaterial.SetShaderParameter("atmosphere_height", AtmosphereHeight);
        ground.MaterialOverride = _groundMaterial;
        _globe.AddChild(ground);
        RefreshLights();

        _overlayMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1, 1, 1, 0.85f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _overlay = new MeshInstance3D { Mesh = SphereMesh(Radius + 0.35f, 96, 192), MaterialOverride = _overlayMaterial, Visible = false };
        _globe.AddChild(_overlay);

        // Two cloud layers: thick swirling banks low (they also shadow the ground), thin east–west wisps higher up,
        // drifting at different speeds. Both read one small tiling 3D noise (clouds.gdshaderinc).
        _cloudData = CloudNoiseData(1701);
        var cloudNoise = CloudNoise(_cloudData);
        _clouds = CloudLayer(LowClouds, cloudNoise);
        AddChild(_clouds);
        _highClouds = CloudLayer(HighClouds, cloudNoise);
        AddChild(_highClouds);
        _groundMaterial.SetShaderParameter("cloud_noise", cloudNoise);
        _groundMaterial.SetShaderParameter("cloud_offset", LowClouds.Offset);
        _groundMaterial.SetShaderParameter("cloud_scale", LowClouds.Scale);
        _groundMaterial.SetShaderParameter("cloud_cover", LowClouds.Cover);
        _groundMaterial.SetShaderParameter("cloud_softness", LowClouds.Softness);
        _groundMaterial.SetShaderParameter("cloud_opacity", LowClouds.Opacity);
        _groundMaterial.SetShaderParameter("cloud_height", LowClouds.Height);

        // Atmosphere: one shell, ray-marched (Rayleigh + Mie scattering) in its shader.
        _atmosphere = Atmosphere();
        AddChild(_atmosphere);

        _moon = Moon();
        AddChild(_moon);
        _moonRock = _moon.GetNode<Node3D>("Body");
        // Its track (the Orbit display): the body circles the pivot's Y axis at MoonOrbit.
        _moonTrack = OrbitLines.Instance(OrbitLines.Circle(MoonOrbit, 192), Lavender with { A = 0.6f }, 3);
        _moon.AddChild(_moonTrack);

        // Scan line: a thin ring in the YZ plane, turned about Y to sweep across the face of the globe.
        var sweepMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.95f, 1f, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _sweep = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = Radius + 1.2f, OuterRadius = Radius + 2.2f, Rings = 96, RingSegments = 6 }, MaterialOverride = sweepMaterial, Visible = false };
        AddChild(_sweep);

        _hoverMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.9f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _hover = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 2.1f, Rings = 24 }, MaterialOverride = _hoverMaterial, Visible = false };
        _globe.AddChild(_hover);

        _native = new Node3D { Visible = false };
        var nativeMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.45f, 1f), EmissionEnabled = true, Emission = new Color(0.85f, 0.45f, 1f), EmissionEnergyMultiplier = 2.5f };
        _native.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.4f, Height = 3.4f, RadialSegments = 4, Rings = 2 }, MaterialOverride = nativeMat, Position = new Vector3(0, 2.5f, 0) });
        _globe.AddChild(_native);
        if (_surface.Map.NativeTile >= 0) PlaceOnSurface(_native, _surface.Map.NativeTile, 0.2f);

        // Planet display: the lat/long wireframe, just above the ground and turning with it, drawn after the clouds.
        _grid = OrbitLines.Instance(GridLines(Radius + 0.5f), Cyan with { A = 0.35f }, 3);
        _grid.Visible = false;
        _globe.AddChild(_grid);

        // Orbit display: our ship's track (render-only: a slightly tilted circle above the satellites) and a marker on it.
        _shipOrbit = new Node3D { Name = "ShipOrbit", RotationDegrees = new Vector3(14f, 0f, -6f), Visible = false };
        AddChild(_shipOrbit);
        _shipOrbit.AddChild(OrbitLines.Instance(OrbitLines.Circle(ShipOrbit, 160), Peach with { A = 0.85f }, 3));
        _shipMarker = new MeshInstance3D { Mesh = new SphereMesh { Radius = 2.2f, Height = 4.4f, RadialSegments = 4, Rings = 2 }, MaterialOverride = GlowMaterial(Peach, 3f) };
        _shipOrbit.AddChild(_shipMarker);

        // Atmosphere display: rings over the storm cells, on the low cloud layer so they drift with it.
        _weather = new Node3D { Name = "Weather", Visible = false };
        _clouds.AddChild(_weather);
        BuildWeather();

        var map = _surface.Map;
        var toxic = 0;
        var biomass = 0f;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (map.Toxic[t]) toxic++;
            biomass += map.Biomass[t].ToFloat();
        }
        _toxicShare = toxic / (float)map.Grid.Count;
        _meanBiomass = biomass / map.Grid.Count;

        _system = new SystemPlot { Name = "SystemPlot", Visible = false };
        AddChild(_system);
        RebuildSystem();

        SetScan(ScanMode.Landing);
        ApplyDisplay();
    }

    /// <summary>Latitude rings and meridians every 30°, as line segments on a sphere of this radius.</summary>
    private static ArrayMesh GridLines(float radius)
    {
        var pairs = new List<Vector3>();
        Vector3 At(float lat, float lon) => new(Mathf.Cos(lat) * Mathf.Sin(lon) * radius, Mathf.Sin(lat) * radius, Mathf.Cos(lat) * Mathf.Cos(lon) * radius);
        const int around = 144, along = 48;
        for (var l = -2; l <= 2; l++)
        {
            var lat = Mathf.DegToRad(l * 30f);
            for (var i = 0; i < around; i++)
            {
                pairs.Add(At(lat, i / (float)around * Mathf.Tau));
                pairs.Add(At(lat, (i + 1) / (float)around * Mathf.Tau));
            }
        }
        for (var m = 0; m < 12; m++)
        {
            var lon = Mathf.DegToRad(m * 30f);
            for (var j = 0; j < along; j++)
            {
                pairs.Add(At(Mathf.DegToRad(-80f + 160f * j / along), lon));
                pairs.Add(At(Mathf.DegToRad(-80f + 160f * (j + 1) / along), lon));
            }
        }
        return OrbitLines.Segments(pairs);
    }

    /// <summary>A UV sphere with u = longitude (0..1 around Y from +Z) and v = 0 at the north pole: the same mapping picking uses.</summary>
    private static ArrayMesh SphereMesh(float radius, int rings, int segments)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var j = 0; j <= rings; j++)
        {
            var v = j / (float)rings;
            var theta = v * Mathf.Pi;
            for (var i = 0; i <= segments; i++)
            {
                var u = i / (float)segments;
                var phi = u * Mathf.Tau;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                st.SetNormal(n);
                st.SetUV(new Vector2(u, v));
                st.AddVertex(n * radius);
            }
        }
        var row = segments + 1;
        for (var j = 0; j < rings; j++)
            for (var i = 0; i < segments; i++)
            {
                var a = j * row + i;
                var b = a + row;
                // Counter-clockwise seen from outside.
                st.AddIndex(a);
                st.AddIndex(a + 1);
                st.AddIndex(b);
                st.AddIndex(a + 1);
                st.AddIndex(b + 1);
                st.AddIndex(b);
            }
        st.GenerateTangents(); // the planet shader's normal maps need them
        return st.Commit();
    }

    private static MultiMeshInstance3D Stars()
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 2.2f, Height = 4.4f, RadialSegments = 4, Rings = 2 },
            InstanceCount = 1400,
        };
        var rng = new RandomNumberGenerator { Seed = 1701 };
        for (var i = 0; i < mm.InstanceCount; i++)
        {
            var dir = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1));
            if (dir.LengthSquared() < 0.01f) dir = Vector3.Up;
            var s = rng.RandfRange(0.4f, 1.3f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), dir.Normalized() * 4000f));
            var tint = rng.Randf();
            mm.SetInstanceColor(i, (tint < 0.15f ? new Color(1f, 0.85f, 0.7f) : tint < 0.3f ? new Color(0.75f, 0.85f, 1f) : Colors.White) * rng.RandfRange(0.5f, 1.4f));
        }
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
    }

    /// <summary>One cloud layer's look (clouds.gdshader uniforms); the low layer's numbers also drive the ground's cloud shadows.</summary>
    private sealed record CloudSpec(float Height, Vector3 Offset, Vector3 Scale, float Cover, float Softness, float Opacity, float AirThickness);

    private static readonly CloudSpec LowClouds = new(1.4f, Vector3.Zero, new Vector3(1.2f, 1.2f, 1.2f), 0.58f, 0.11f, 0.95f, 0.8f);
    private static readonly CloudSpec HighClouds = new(2.4f, new Vector3(0.5f, 0.2f, 0.7f), new Vector3(1.6f, 4f, 1.6f), 0.74f, 0.1f, 0.35f, 0.6f);

    /// <summary>
    /// The clouds' noise: 64³ periodic gradient (Perlin) noise with 4 lattice cells across, so it tiles exactly in all
    /// three directions (Godot's NoiseTexture3D makes it seamless by blending skirts, which smeared the clouds).
    /// </summary>
    private const int CloudN = 64;

    /// <summary>The noise values, one byte per texel, index (z · n + y) · n + x (z = slice).</summary>
    private static byte[] CloudNoiseData(int seed)
    {
        const int n = CloudN, period = 4;
        int[] grads = { 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0, 1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1, 0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 1, 1, 0, -1, 1, 0, 0, -1, 1, 0, -1, -1 };
        int Hash(int x, int y, int z)
        {
            unchecked
            {
                var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ ((uint)seed * 2654435761u);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) & 15u);
            }
        }
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        var data = new byte[n * n * n];
        for (var z = 0; z < n; z++)
        {
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) * period / n, fy = (y + 0.5f) * period / n, fz = (z + 0.5f) * period / n;
                    int cx = (int)fx, cy = (int)fy, cz = (int)fz;
                    float tx = fx - cx, ty = fy - cy, tz = fz - cz;
                    float sx = Fade(tx), sy = Fade(ty), sz = Fade(tz);
                    var sum = 0f;
                    for (var c = 0; c < 8; c++)
                    {
                        int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
                        var g = Hash((cx + dx) % period, (cy + dy) % period, (cz + dz) % period) * 3;
                        var dot = grads[g] * (tx - dx) + grads[g + 1] * (ty - dy) + grads[g + 2] * (tz - dz);
                        sum += (dx == 1 ? sx : 1f - sx) * (dy == 1 ? sy : 1f - sy) * (dz == 1 ? sz : 1f - sz) * dot;
                    }
                    data[(z * n + y) * n + x] = (byte)(Mathf.Clamp(sum * 0.75f + 0.5f, 0f, 1f) * 255f);
                }
        }
        return data;
    }

    private static ImageTexture3D CloudNoise(byte[] data)
    {
        const int n = CloudN;
        var images = new Godot.Collections.Array<Image>();
        for (var z = 0; z < n; z++)
        {
            var slice = new byte[n * n];
            System.Array.Copy(data, z * n * n, slice, 0, n * n);
            images.Add(Image.CreateFromData(n, n, false, Image.Format.R8, slice));
        }
        var texture = new ImageTexture3D();
        texture.Create(Image.Format.R8, n, n, n, false, images);
        return texture;
    }

    /// <summary>While a scan overlay is up, thin the clouds (and their shadows) so the scan stays readable.</summary>
    private void ThinClouds(bool thin)
    {
        var k = thin ? 0.3f : 1f;
        ((ShaderMaterial)_clouds.MaterialOverride).SetShaderParameter("layer_opacity", LowClouds.Opacity * k);
        ((ShaderMaterial)_highClouds.MaterialOverride).SetShaderParameter("layer_opacity", HighClouds.Opacity * k);
        _groundMaterial.SetShaderParameter("cloud_opacity", LowClouds.Opacity * k);
    }

    /// <summary>A cloud shell (Godot's own sphere, so cull_back keeps only the near side) with clouds.gdshader.</summary>
    private MeshInstance3D CloudLayer(CloudSpec spec, Texture3D noise)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clouds.gdshader"), RenderPriority = spec.Height > 2f ? 2 : 1 }; // low layer first, then the high one, then the air
        material.SetShaderParameter("cloud_noise", noise);
        material.SetShaderParameter("sun_dir", _sunDir);
        material.SetShaderParameter("sun_colour", new Vector3(_light.LightColor.R, _light.LightColor.G, _light.LightColor.B));
        material.SetShaderParameter("sun_energy", _light.LightEnergy);
        material.SetShaderParameter("layer_offset", spec.Offset);
        material.SetShaderParameter("layer_scale", spec.Scale);
        material.SetShaderParameter("layer_cover", spec.Cover);
        material.SetShaderParameter("layer_softness", spec.Softness);
        material.SetShaderParameter("layer_opacity", spec.Opacity);
        material.SetShaderParameter("air_thickness", spec.AirThickness);
        material.SetShaderParameter("planet_radius", Radius);
        var r = Radius + spec.Height;
        return new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 128, Rings = 64 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>
    /// The atmosphere shell: a sphere a little above the clouds whose shader ray-marches the air in front of each pixel
    /// (Rayleigh + Mie scattering of the orbit sun, the same numbers the planet shader uses to redden its sunlight).
    /// Drawn after the clouds (render priority) and premultiplied, so it both adds sky light and dims what lies behind.
    /// Godot's own SphereMesh, slightly larger than the shell itself so the polygon edges never clip the limb.
    /// </summary>
    private MeshInstance3D Atmosphere()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/atmosphere.gdshader"), RenderPriority = 10 };
        material.SetShaderParameter("sun_dir", _sunDir);
        material.SetShaderParameter("planet_radius", Radius);
        material.SetShaderParameter("atmosphere_height", AtmosphereHeight);
        var r = (Radius + AtmosphereHeight) * 1.01f;
        return new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 128, Rings = 64 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>
    /// The satellite-photo ground layers (tileable patches cut from NASA's Blue Marble, in OrbitPainter.PhotoLayers order)
    /// as one texture array with mipmaps. They live in textures/planet behind a .gdignore and are read as plain files, so
    /// Godot's importer never touches them; a missing file falls back to flat grey, which leaves the painted colour as is.
    /// </summary>
    private static Texture2DArray PhotoLayers()
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (var layer in OrbitPainter.PhotoLayers)
        {
            var image = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://textures/planet/{layer}.jpg"));
            if (image == null || image.IsEmpty())
            {
                GD.PushWarning($"Orbit view: missing ground photo textures/planet/{layer}.jpg");
                image = Image.CreateEmpty(PhotoSize, PhotoSize, false, Image.Format.Rgba8);
                image.Fill(new Color(0.5f, 0.5f, 0.5f));
            }
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != PhotoSize || image.GetHeight() != PhotoSize) image.Resize(PhotoSize, PhotoSize, Image.Interpolation.Lanczos);
            image.GenerateMipmaps();
            images.Add(image);
        }
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        return array;
    }

    /// <summary>A small grey moon on a slow, tilted orbit (render-only scenery).</summary>
    private static Node3D Moon()
    {
        var pivot = new Node3D { RotationDegrees = new Vector3(18f, 0, 8f) };
        var rock = new NoiseTexture2D
        {
            Width = 512, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { Seed = 9, Frequency = 0.02f, FractalOctaves = 5, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular },
            ColorRamp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0.32f, 0.31f, 0.3f), new Color(0.72f, 0.7f, 0.68f) } },
        };
        pivot.AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new SphereMesh { Radius = 9f, Height = 18f, RadialSegments = 32, Rings = 16 },
            Position = new Vector3(0, 0, -265f),
            MaterialOverride = new StandardMaterial3D { AlbedoTexture = rock, Roughness = 1f },
        });
        return pivot;
    }

    /// <summary>A faint nebula far behind the stars: an inside-out sphere with tinted noise.</summary>
    private static MeshInstance3D Nebula()
    {
        var texture = new NoiseTexture2D
        {
            Width = 1024, Height = 512, Seamless = true,
            Noise = new FastNoiseLite { Seed = 31, Frequency = 0.004f, FractalOctaves = 5, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, DomainWarpEnabled = true, DomainWarpAmplitude = 60f },
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, 0.5f, 0.75f, 1f },
                Colors = new[] { new Color(0, 0, 0, 0), new Color(0.1f, 0.05f, 0.2f, 0f), new Color(0.25f, 0.12f, 0.4f, 0.35f), new Color(0.3f, 0.45f, 0.8f, 0.5f) },
            },
        };
        return new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 6000f, Height = 12000f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Front, // seen from inside
            },
        };
    }

    // ---------------------------------------------------------------- mapping

    /// <summary>Globe-local direction of a tile's centre (odd rows sit half a tile east, as on the hex map).</summary>
    public Vector3 TileDirection(int tile)
    {
        var g = _surface.Map.Grid;
        var col = g.Column(tile);
        var row = g.Row(tile);
        var u = (col + 0.5f + ((row & 1) == 1 ? 0.5f : 0f)) / g.Width;
        var v = VTop + (row + 0.5f) / g.Height * (VBottom - VTop);
        var theta = v * Mathf.Pi;
        var phi = u * Mathf.Tau;
        return new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
    }

    /// <summary>The tile under a globe-local direction, or -1 over the polar caps.</summary>
    public int TileAtDirection(Vector3 d)
    {
        var g = _surface.Map.Grid;
        d = d.Normalized();
        var v = Mathf.Acos(Mathf.Clamp(d.Y, -1f, 1f)) / Mathf.Pi;
        if (v < VTop || v >= VBottom) return -1;
        var row = Mathf.Clamp((int)((v - VTop) / (VBottom - VTop) * g.Height), 0, g.Height - 1);
        var u = Mathf.Atan2(d.X, d.Z) / Mathf.Tau;
        if (u < 0) u += 1f;
        var col = Mathf.FloorToInt(u * g.Width - ((row & 1) == 1 ? 0.5f : 0f));
        col = ((col % g.Width) + g.Width) % g.Width;
        return g.Index(col, row);
    }

    /// <summary>The tile under the mouse, or -1 (space, or the polar caps).</summary>
    public int PickTile(Vector2 mouse)
    {
        if (Display == OrbitDisplay.System) return -1;
        // The camera's real transform carries the view-rect shift (FrameViewRect moves it, no H/V offsets), so Godot's rays are exact.
        var origin = _camera.ProjectRayOrigin(mouse) - _globe.GlobalPosition;
        var dir = _camera.ProjectRayNormal(mouse);
        // Ray–sphere: |o + t d|² = R²
        var b = origin.Dot(dir);
        var c = origin.LengthSquared() - Radius * Radius;
        var disc = b * b - c;
        if (disc < 0) return -1;
        var t = -b - Mathf.Sqrt(disc);
        if (t < 0) return -1;
        var hit = origin + dir * t;
        return TileAtDirection(_globe.GlobalTransform.Basis.Inverse() * hit);
    }

    private void PlaceOnSurface(Node3D node, int tile, float altitude)
    {
        var n = TileDirection(tile);
        node.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, n)), n * (Radius + altitude));
    }

    // ---------------------------------------------------------------- textures

    /// <summary>
    /// Paints one value per tile into a (2·width × height) image, each tile two pixels wide with odd rows shifted one
    /// pixel east (the hex stagger), then stretches it over the map's band of an equirectangular texture.
    /// </summary>
    private ImageTexture TileTexture(System.Func<int, Color> colour, Color cap, bool smooth) =>
        ImageTexture.CreateFromImage(TileImage(colour, cap, smooth));

    private Image TileImage(System.Func<int, Color> colour, Color cap, bool smooth)
    {
        var g = _surface.Map.Grid;
        var tiles = Image.CreateEmpty(g.Width * 2, g.Height, false, Image.Format.Rgba8);
        for (var t = 0; t < g.Count; t++)
        {
            var row = g.Row(t);
            var x = g.Column(t) * 2 + ((row & 1) == 1 ? 1 : 0);
            var c = colour(t);
            tiles.SetPixel(x % (g.Width * 2), row, c);
            tiles.SetPixel((x + 1) % (g.Width * 2), row, c);
        }
        var top = Mathf.RoundToInt(TextureHeight * VTop);
        var band = Mathf.RoundToInt(TextureHeight * (VBottom - VTop));
        tiles.Resize(TextureWidth, band, smooth ? Image.Interpolation.Cubic : Image.Interpolation.Nearest);
        var full = Image.CreateEmpty(TextureWidth, TextureHeight, false, Image.Format.Rgba8);
        full.Fill(cap);
        full.BlitRect(tiles, new Rect2I(0, 0, TextureWidth, band), new Vector2I(0, top));
        return full;
    }

    /// <summary>City lights for the night side: your buildings glow warm (bases brightest), the natives' settlement dimly.</summary>
    private void RefreshLights()
    {
        // Assembling cores light up when they turn Complete, so count those too; other players' buildings light up once
        // you know of them (in sight now or remembered: the sim's fog).
        RefreshSight();
        var stamp = _surface.Structures.Count * 7919L + _surface.Bases(_localPlayer).Count
            + 104729L * _surface.Structures.Count(x => x.State == StructureState.Complete)
            + 1299709L * _surface.Structures.Count(Knows);
        if (stamp == _lightsStamp) return;
        _lightsStamp = stamp;
        var map = _surface.Map;
        var glow = new Dictionary<int, float>();
        foreach (var s in _surface.Structures)
        {
            if (!Knows(s) || s.State != StructureState.Complete) continue;
            var strength = _sim.Data.Building(s.DefId).IsCore ? 1f : 0.55f;
            glow[s.Tile] = Mathf.Max(glow.GetValueOrDefault(s.Tile), strength);
            foreach (var n in map.Grid.Neighbours(s.Tile)) glow[n] = Mathf.Max(glow.GetValueOrDefault(n), strength * 0.3f);
        }
        if (map.NativeTile >= 0)
            foreach (var t in map.Grid.Within(map.NativeTile, 1)) glow[t] = Mathf.Max(glow.GetValueOrDefault(t), t == map.NativeTile ? 0.45f : 0.15f);
        var warm = new Color(1f, 0.78f, 0.45f);
        _groundMaterial.SetShaderParameter("lights_tex", TileTexture(t => glow.TryGetValue(t, out var g) ? warm * g : Colors.Black, Colors.Black, smooth: true));
    }

    /// <summary>The overlay colour of a tile for a scan (transparent where the scan has nothing to say).</summary>
    private Color ScanColour(ScanMode mode, int t)
    {
        var map = _surface.Map;
        var me = _surface.Players[_localPlayer];
        var clear = new Color(0, 0, 0, 0);
        switch (mode)
        {
            case ScanMode.Resources:
            {
                var r = map.DepositResource[t];
                if (me.Surveyed[t] && r >= 0)
                {
                    if (map.DepositAmount[t] <= Fix64.Zero) return new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    var id = _sim.Data.Resources[r].Id;
                    var c = Icons.ResourceColors.TryGetValue(id, out var rc) ? rc : new Color(0.8f, 0.6f, 1f);
                    return c with { A = 0.95f };
                }
                // Unsurveyed: the region's strongest hint, as a faint tint.
                var hints = map.Hints(map.Region(t));
                if (hints.Count == 0) return clear;
                var h = hints[0];
                var hc = Icons.ResourceColors.TryGetValue(_sim.Data.Resources[h.Resource].Id, out var hcol) ? hcol : new Color(0.8f, 0.6f, 1f);
                return hc with { A = h.Strength switch { HintStrength.Rich => 0.32f, HintStrength.Likely => 0.2f, _ => 0.1f } };
            }
            case ScanMode.Lifeforms:
                if (map.NativeTile == t) return new Color(1f, 0.4f, 1f, 1f);
                if (map.NearNatives(t)) return new Color(0.7f, 0.35f, 1f, 0.55f);
                return map.WildlifeAt[t] switch
                {
                    Wildlife.Predators => new Color(1f, 0.25f, 0.2f, 0.65f),
                    Wildlife.Herds => new Color(1f, 0.75f, 0.25f, 0.45f),
                    _ => clear,
                };
            case ScanMode.Biologics:
                if (map.Toxic[t]) return new Color(0.8f, 1f, 0.1f, 0.85f);
                var b = map.Biomass[t].ToFloat();
                return map.IsWater(t) ? new Color(0.2f, 0.9f, 0.8f, b * 0.35f) : new Color(0.2f, 1f, 0.35f, b * 0.6f);
            case ScanMode.Landing:
                if (map.IsWater(t)) return clear;
                if (map.NearNatives(t)) return new Color(0.7f, 0.35f, 1f, 0.6f);
                if (me.Phase == LandingPhase.Landed)
                    foreach (var core in _surface.Bases(_localPlayer))
                        if (map.Grid.Distance(core.Tile, t) < (_sim.Data.Orbit?.OutpostMinDistance ?? 0)) return new Color(0.3f, 0.6f, 1f, 0.3f);
                return map.Safety(t) switch
                {
                    LandingSafety.Safe => new Color(0.3f, 1f, 0.4f, 0.55f),
                    LandingSafety.Risky => new Color(1f, 0.85f, 0.2f, 0.6f),
                    _ => new Color(1f, 0.3f, 0.25f, 0.3f),
                };
            default:
                return clear;
        }
    }

    /// <summary>What the scan overlay depends on, so it's rebuilt only when that changes.</summary>
    private long OverlayStamp(ScanMode mode)
    {
        var me = _surface.Players[_localPlayer];
        return mode switch
        {
            ScanMode.Resources => SurveyedCount(me) * 31L + _surface.Structures.Count,
            ScanMode.Landing => (long)me.Phase * 100003L + _surface.Bases(_localPlayer).Count * 101L + _surface.Structures.Count,
            _ => 0,
        };
    }

    private static int SurveyedCount(SurfacePlayer me)
    {
        var n = 0;
        foreach (var s in me.Surveyed)
            if (s) n++;
        return n;
    }

    /// <summary>Switch the scan: rebuild its overlay if needed and run the sweep line across the globe.</summary>
    public void SetScan(ScanMode mode)
    {
        Mode = mode;
        _sweepTime = 0f;
        RefreshOverlay(force: true);
    }

    private void RefreshOverlay(bool force = false)
    {
        var i = (int)Mode;
        // The scan shows on the Planet and Orbit displays; Atmosphere shows the clouds at full strength instead.
        var show = Mode != ScanMode.Terrain && Display is OrbitDisplay.Planet or OrbitDisplay.Orbit;
        _overlay.Visible = show;
        ThinClouds(show);
        if (!show) return;
        var stamp = OverlayStamp(Mode);
        if (_overlayTextures[i] == null || _overlayStamp[i] != stamp)
        {
            _overlayTextures[i] = TileTexture(t => ScanColour(Mode, t), new Color(0, 0, 0, 0), smooth: false);
            _overlayStamp[i] = stamp;
        }
        if (force || _overlayMaterial.AlbedoTexture != _overlayTextures[i]) _overlayMaterial.AlbedoTexture = _overlayTextures[i];
    }

    // ---------------------------------------------------------------- camera and input

    /// <summary>Turn the globe (mouse drag or keys), in radians.</summary>
    /// <remarks>In the System display, yaw turns the plot about the star and pitch is ignored.</remarks>
    public void Rotate(float yaw, float pitch)
    {
        if (Display == OrbitDisplay.System)
        {
            _systemYawTarget += yaw;
            return;
        }
        _yawTarget += yaw;
        _pitchTarget = Mathf.Clamp(_pitchTarget + pitch, -1.25f, 1.25f);
    }

    /// <summary>Zoom: factor below 1 moves in (the System plot keeps its own zoom).</summary>
    public void Zoom(float factor)
    {
        if (Display == OrbitDisplay.System) _systemDistanceTarget = Mathf.Clamp(_systemDistanceTarget * factor, 200f, 800f);
        else _distanceTarget = Mathf.Clamp(_distanceTarget * factor, 135f, 520f);
    }

    /// <summary>Turn the globe so this tile faces the camera, and zoom to a distance.</summary>
    public void Focus(int tile, float distance)
    {
        var p = TileDirection(tile);
        _yawTarget = -Mathf.Atan2(p.X, p.Z);
        // Keep the yaw turn short: pick the equivalent angle nearest the current one.
        while (_yawTarget - _yaw > Mathf.Pi) _yawTarget -= Mathf.Tau;
        while (_yawTarget - _yaw < -Mathf.Pi) _yawTarget += Mathf.Tau;
        // Bring the tile up to the camera's elevation (0 on the Planet display), so it faces the camera on any display.
        _pitchTarget = Mathf.Clamp(Mathf.Atan2(p.Y, new Vector2(p.X, p.Z).Length()) - ElevationFor(Display), -1.25f, 1.25f);
        _distanceTarget = distance;
    }

    public bool Settled => Mathf.Abs(_distance - _distanceTarget) < 2f && Mathf.Abs(_yaw - _yawTarget) < 0.01f;

    /// <summary>Hover ring on a tile (colour says whether the armed action can go there); -1 hides it.</summary>
    public void SetHover(int tile, Color colour)
    {
        _hover.Visible = tile >= 0;
        if (tile < 0) return;
        PlaceOnSurface(_hover, tile, 0.6f);
        _hoverMaterial.AlbedoColor = colour;
    }

    // ---------------------------------------------------------------- per frame

    /// <param name="alpha">Fraction of the way to the next sim tick.</param>
    public void Refresh(float delta, float alpha)
    {
        _time += delta;
        var k = 1f - Mathf.Exp(-6f * delta);
        _yaw = Mathf.Lerp(_yaw, _yawTarget, k);
        _pitch = Mathf.Lerp(_pitch, _pitchTarget, k);
        _distance = Mathf.Lerp(_distance, _distanceTarget, 1f - Mathf.Exp(-4f * delta));
        _globe.Basis = new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Up, _yaw);
        _clouds.Basis = _globe.Basis * new Basis(Vector3.Up, _time * 0.012f);
        _highClouds.Basis = _globe.Basis * new Basis(Vector3.Up, -_time * 0.007f + 1.3f);
        _groundMaterial.SetShaderParameter("cloud_from_world", _clouds.GlobalTransform.Basis.Inverse());
        _moon.Rotation = _moon.Rotation with { Y = _time * 0.02f };
        RefreshLights();

        var slow = 1f - Mathf.Exp(-4f * delta);
        _elevation = Mathf.Lerp(_elevation, ElevationFor(Display), slow);
        _zoomOut = Mathf.Lerp(_zoomOut, Display == OrbitDisplay.Orbit ? OrbitZoomOut : 1f, slow);
        _systemDistance = Mathf.Lerp(_systemDistance, _systemDistanceTarget, slow);
        _systemYaw = Mathf.Lerp(_systemYaw, _systemYawTarget, k);
        _system.Rotation = new Vector3(0f, _systemYaw, 0f);
        var cameraDistance = Display == OrbitDisplay.System ? _systemDistance : _distance * _zoomOut;
        _camera.Position = new Vector3(0f, Mathf.Sin(_elevation), Mathf.Cos(_elevation)) * cameraDistance;
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        FrameViewRect(cameraDistance);

        var shipAngle = _time * 0.05f + 0.4f;
        _shipMarker.Position = new Vector3(Mathf.Sin(shipAngle), 0f, Mathf.Cos(shipAngle)) * ShipOrbit;

        if (_sweepTime >= 0f)
        {
            _sweepTime += delta;
            var t = _sweepTime / 1.3f;
            _sweep.Visible = t < 1f && Display is OrbitDisplay.Planet or OrbitDisplay.Orbit;
            _sweep.Basis = new Basis(Vector3.Up, Mathf.Lerp(-1.45f, 1.45f, t)) * new Basis(Vector3.Forward, Mathf.Pi / 2f);
            _overlayMaterial.AlbedoColor = new Color(1, 1, 1, Mathf.Clamp(t, 0f, 1f) * 0.85f);
            if (t >= 1f) _sweepTime = -1f;
        }
        RefreshOverlay();
        _native.Visible = Mode is ScanMode.Lifeforms or ScanMode.Landing && _surface.Map.NativeTile >= 0;

        UpdateBases();
        UpdateUnits();
        UpdateDeliveries(alpha);
        UpdateSatellites(alpha);
    }

    private static StandardMaterial3D GlowMaterial(Color c, float energy = 2.5f) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
    };

    // ---------------------------------------------------------------- fog of war (the sim's)

    private bool[] _sight = System.Array.Empty<bool>();
    private long _sightTick = -1;

    /// <summary>The local player's tiles in sight now (the sim's PlanetSurface.VisibleTiles), once per sim tick.</summary>
    private void RefreshSight()
    {
        if (_sightTick == _sim.Tick && _sight.Length > 0) return;
        _sightTick = _sim.Tick;
        _sight = _surface.VisibleTiles(_localPlayer);
    }

    private bool InSight(int tile) => tile >= 0 && tile < _sight.Length && _sight[tile];

    /// <summary>Your own structures always; another player's while in sight or once remembered.</summary>
    private bool Knows(SurfaceStructure s) => s.Owner == _localPlayer || InSight(s.Tile) || _surface.Remembers(_localPlayer, s);

    /// <summary>A beacon on each base you know of (yours, and other players' in sight or remembered): a lit mast and a
    /// ring on the ground, in the owner's colour.</summary>
    private void UpdateBases()
    {
        RefreshSight();
        var alive = new HashSet<int>();
        foreach (var core in _surface.Structures)
        {
            if (core.Id != core.BaseId || !_sim.Data.Building(core.DefId).IsCore || !Knows(core)) continue;
            alive.Add(core.Id);
            if (_baseMarkers.ContainsKey(core.Id)) continue;
            var marker = new Node3D();
            var colour = PlanetDriver.ColorOf(core.Owner);
            marker.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.5f, Height = 5f }, Position = new Vector3(0, 2.5f, 0), MaterialOverride = GlowMaterial(colour) });
            marker.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 2f, OuterRadius = 2.5f, Rings = 24 }, MaterialOverride = GlowMaterial(colour, 1.5f) });
            _globe.AddChild(marker);
            PlaceOnSurface(marker, core.Tile, 0.3f);
            _baseMarkers[core.Id] = marker;
        }
        foreach (var id in new List<int>(_baseMarkers.Keys))
        {
            if (alive.Contains(id)) continue;
            _baseMarkers[id].QueueFree();
            _baseMarkers.Remove(id);
        }
    }

    private void UpdateUnits()
    {
        var alive = new HashSet<int>();
        foreach (var u in _surface.Units)
        {
            if (u.Owner != _localPlayer && !InSight(u.Tile)) continue; // another player's vehicles only while in sight
            alive.Add(u.Id);
            if (!_unitMarkers.TryGetValue(u.Id, out var dot))
            {
                var tint = u.Owner == _localPlayer ? new Color(1f, 0.95f, 0.7f) : PlanetDriver.ColorOf(u.Owner);
                dot = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f, RadialSegments = 8, Rings = 4 }, MaterialOverride = GlowMaterial(tint, 2f) };
                _globe.AddChild(dot);
                _unitMarkers[u.Id] = dot;
            }
            dot.Position = TileDirection(u.Tile) * (Radius + 0.8f);
        }
        foreach (var id in new List<int>(_unitMarkers.Keys))
        {
            if (alive.Contains(id)) continue;
            _unitMarkers[id].QueueFree();
            _unitMarkers.Remove(id);
        }
    }

    /// <summary>Outpost pods and drop ships fall from high orbit to their tile; a beam is a shimmering column.</summary>
    private void UpdateDeliveries(float alpha)
    {
        var alive = new HashSet<int>();
        foreach (var d in _surface.Deliveries)
        {
            if (d.Owner != _localPlayer) continue;
            alive.Add(d.Id);
            if (!_deliveryMarkers.TryGetValue(d.Id, out var marker))
            {
                marker = new Node3D();
                var colour = d.Kind == DeliveryKind.Beam ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.65f, 0.3f);
                if (d.Kind == DeliveryKind.Beam)
                    marker.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 14f }, Position = new Vector3(0, 7f, 0),
                        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour with { A = 0.45f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
                else
                {
                    marker.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.8f, Height = 2.6f }, MaterialOverride = GlowMaterial(new Color(0.9f, 0.9f, 0.95f), 0.5f) });
                    marker.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0f, Height = 5f }, Position = new Vector3(0, 3.5f, 0), MaterialOverride = GlowMaterial(colour, 4f) });
                }
                _globe.AddChild(marker);
                _deliveryMarkers[d.Id] = marker;
            }
            var span = Mathf.Max(1f, d.ArriveTick - d.LaunchTick);
            var t = Mathf.Clamp((_sim.Tick - d.LaunchTick + alpha) / span, 0f, 1f);
            var altitude = d.Kind == DeliveryKind.Beam ? 0.2f : Mathf.Lerp(60f, 0.5f, 1f - (1f - t) * (1f - t));
            PlaceOnSurface(marker, d.Tile, altitude);
            if (d.Kind == DeliveryKind.Beam) marker.Scale = new Vector3(1f + Mathf.Sin(_time * 30f) * 0.15f, 1f, 1f);
        }
        foreach (var id in new List<int>(_deliveryMarkers.Keys))
        {
            if (alive.Contains(id)) continue;
            _deliveryMarkers[id].QueueFree();
            _deliveryMarkers.Remove(id);
        }
    }

    /// <summary>Each satellite circles over its band (a faint ring at that latitude), at the column its sweep has reached.</summary>
    private void UpdateSatellites(float alpha)
    {
        var orbit = _sim.Data.Orbit;
        if (orbit == null) return;
        var g = _surface.Map.Grid;
        var alive = new HashSet<int>();
        foreach (var sat in _surface.Satellites)
        {
            if (sat.Owner != _localPlayer) continue;
            alive.Add(sat.Id);
            var v = VTop + (sat.Row + 0.5f) / g.Height * (VBottom - VTop);
            var theta = v * Mathf.Pi;
            if (!_satMarkers.TryGetValue(sat.Id, out var m))
            {
                var model = new Node3D();
                var body = GlowMaterial(new Color(0.85f, 0.88f, 0.95f), 0.4f);
                model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.2f, 1.6f) }, MaterialOverride = body });
                foreach (var x in new[] { -2.2f, 2.2f })
                    model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.08f, 1.2f) }, Position = new Vector3(x, 0, 0), MaterialOverride = GlowMaterial(new Color(0.25f, 0.4f, 0.9f), 0.8f) });
                model.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f }, Position = new Vector3(0, 0.9f, 0), MaterialOverride = GlowMaterial(new Color(0.5f, 1f, 0.9f), 4f) });
                _globe.AddChild(model);
                // The band it covers: a faint ring at its latitude (TorusMesh rings lie round Y, like latitudes).
                var bandMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 1f, 0.9f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
                var ringRadius = (Radius + 0.6f) * Mathf.Sin(theta);
                var band = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = ringRadius - 0.25f, OuterRadius = ringRadius + 0.25f, Rings = 128, RingSegments = 4 },
                    Position = new Vector3(0, (Radius + 0.6f) * Mathf.Cos(theta), 0), MaterialOverride = bandMat };
                _globe.AddChild(band);
                // Its track (Orbit display): the circle it flies, at its altitude over that latitude.
                var track = OrbitLines.Instance(OrbitLines.Circle(SatelliteOrbit * Mathf.Sin(theta), 160), SkyBlue with { A = 0.75f }, 3);
                track.Position = new Vector3(0f, SatelliteOrbit * Mathf.Cos(theta), 0f);
                track.Visible = Display == OrbitDisplay.Orbit;
                _globe.AddChild(track);
                m = (model, band, track);
                _satMarkers[sat.Id] = m;
            }
            var cols = (_sim.Tick - sat.LaunchTick + alpha) * g.Width / (orbit.SatelliteSweepSeconds * Simulation.TickRate);
            var phi = (cols % g.Width) / g.Width * Mathf.Tau;
            var dir = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
            m.Sat.Position = dir * SatelliteOrbit;
            m.Sat.LookAt(_globe.GlobalTransform * Vector3.Zero, Vector3.Up);
        }
        foreach (var id in new List<int>(_satMarkers.Keys))
        {
            if (alive.Contains(id)) continue;
            _satMarkers[id].Sat.QueueFree();
            _satMarkers[id].Band.QueueFree();
            _satMarkers[id].Track.QueueFree();
            _satMarkers.Remove(id);
        }
    }

    // ---------------------------------------------------------------- displays

    /// <summary>Camera elevation above the equator plane per display (radians): Orbit looks down a little on the tracks, System well down on the plot.</summary>
    private static float ElevationFor(OrbitDisplay display) => display switch
    {
        OrbitDisplay.Orbit => 0.32f,
        OrbitDisplay.System => 0.95f,
        _ => 0f,
    };

    /// <summary>Switch the viewscreen. The camera eases to the display's framing; the globe's turn and zoom are kept for when it comes back.</summary>
    public void SetDisplay(OrbitDisplay display)
    {
        if (display == Display) return;
        Display = display;
        if (display == OrbitDisplay.System) _hover.Visible = false;
        ApplyDisplay();
    }

    /// <summary>Show what the current display needs (System hides the globe, its air, clouds, moon and markers; the others hide the plot).</summary>
    private void ApplyDisplay()
    {
        if (_system == null) return; // before Build
        var globe = Display != OrbitDisplay.System;
        _globe.Visible = globe; // the overlay, grid, hover ring and every marker are its children
        _clouds.Visible = globe;
        _highClouds.Visible = globe;
        _atmosphere.Visible = globe;
        _light.Visible = globe; // the plot is lit by its star's own point light
        _moon.Visible = globe && _moonBody != null;
        _moonTrack.Visible = Display == OrbitDisplay.Orbit;
        _shipOrbit.Visible = Display == OrbitDisplay.Orbit;
        _weather.Visible = Display == OrbitDisplay.Atmosphere;
        _grid.Visible = Display == OrbitDisplay.Planet && _gridOn;
        _system.Visible = !globe;
        if (!globe) _sweep.Visible = false;
        foreach (var m in _satMarkers.Values) m.Track.Visible = Display == OrbitDisplay.Orbit;
        RefreshOverlay(force: true);
    }

    /// <summary>The star system: the loaded map, or (on the planet-first MVP, which loads no map) maps.json mvp_duel.</summary>
    private MapDef? SystemMap()
    {
        if (_sim.Map != null) return _sim.Map;
        foreach (var m in _sim.Data.Maps)
            if (m.Id == MatchSetup.MvpMap) return m;
        return _sim.Data.Maps.Count > 0 ? _sim.Data.Maps[0] : null;
    }

    /// <summary>CurrentBodyName, else the local player's home body on the map.</summary>
    private string? CurrentName()
    {
        if (!string.IsNullOrEmpty(_currentBodyName)) return _currentBodyName;
        var map = SystemMap();
        if (map == null) return null;
        foreach (var start in map.Starts)
            if (start.Slot == _localPlayer) return start.HomeBodyId;
        return null;
    }

    private void RebuildSystem()
    {
        foreach (var child in _system.GetChildren())
        {
            _system.RemoveChild(child);
            child.QueueFree();
        }
        var map = SystemMap();
        _system.Build(map, _sim.Data, CurrentName());
        var current = SystemPlot.FindBody(map, CurrentName());
        _moonBody = null;
        if (map != null && current != null)
            foreach (var b in map.Bodies)
                if (b.Parent == current.Id)
                {
                    _moonBody = b;
                    break;
                }
        ApplyDisplay();
    }

    /// <summary>The bodies in the System plot (the map's, in map order; Current marks ours).</summary>
    public IReadOnlyList<SystemBody> SystemBodies() => _system.Bodies;

    /// <summary>Ring one body of the System plot in cyan (by id); null clears it.</summary>
    public void HighlightBody(string? id) => _system.Highlight(id);

    /// <summary>The System plot body under the mouse (id), or null (also on the other displays).</summary>
    public string? PickBody(Vector2 mouse)
    {
        if (Display != OrbitDisplay.System) return null;
        string? best = null;
        var bestDistance = float.MaxValue;
        foreach (var b in _system.Bodies)
        {
            if (!_system.TryLocate(b.Id, out var at, out var r) || _camera.IsPositionBehind(at)) continue;
            var d = _camera.UnprojectPosition(at).DistanceTo(mouse);
            var reach = r / Mathf.Max(1f, _camera.GlobalPosition.DistanceTo(at)) * ViewportSize().Y / (2f * TanHalfFov()) + 10f;
            if (d <= reach && d < bestDistance)
            {
                best = b.Id;
                bestDistance = d;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- tracked objects (Orbit display)

    /// <summary>What is in orbit, from the sim: our ship, each of our survey satellites, the moon (from the map), and our deliveries on the way down.</summary>
    public IReadOnlyList<OrbitObject> TrackedObjects()
    {
        var list = new List<OrbitObject>();
        foreach (var (o, _) in Tracked()) list.Add(o);
        return list;
    }

    /// <summary>
    /// Where a tracked object (OrbitObject.Name) is on screen, in viewport pixels; on the System display, a body (id or
    /// name). Null when it isn't drawn on this display, is behind the camera, or is hidden behind the planet.
    /// </summary>
    public Vector2? ScreenPositionOf(string objectName)
    {
        if (Display == OrbitDisplay.System)
            return _system.TryLocate(objectName, out var body, out _) && !_camera.IsPositionBehind(body) ? _camera.UnprojectPosition(body) : null;
        foreach (var (o, node) in Tracked())
        {
            if (o.Name != objectName) continue;
            if (node == null || !node.IsVisibleInTree()) return null;
            var at = node.GlobalPosition;
            if (_camera.IsPositionBehind(at) || Occluded(at)) return null;
            return _camera.UnprojectPosition(at);
        }
        return null;
    }

    private List<(OrbitObject Object, Node3D? Node)> Tracked()
    {
        var list = new List<(OrbitObject, Node3D?)>();
        var me = _surface.Players[_localPlayer];
        var ship = _sim.Data.Orbit?.ShipFor(_factionId);
        var status = new List<string> { "In orbit" };
        if (ship != null) status.Add($"{ship.ClassName} class");
        if (me.ShuttleBackTick > _sim.Tick) status.Add($"drop ship away, back in {SecondsTo(me.ShuttleBackTick)} s");
        if (me.TransporterReadyTick > _sim.Tick) status.Add($"transporter ready in {SecondsTo(me.TransporterReadyTick)} s");
        list.Add((new OrbitObject(ship?.Name ?? "Our ship", "Ship", string.Join(" · ", status), Peach), _shipMarker));

        var g = _surface.Map.Grid;
        var n = 0;
        foreach (var sat in _surface.Satellites)
        {
            if (sat.Owner != _localPlayer) continue;
            n++;
            var v = VTop + (sat.Row + 0.5f) / g.Height * (VBottom - VTop);
            var lat = Mathf.RoundToInt(90f - v * 180f);
            var where = $"{Mathf.Abs(lat)}° {(lat >= 0 ? "N" : "S")}";
            var pass = sat.Swept / g.Width;
            var sweep = pass == 0 ? $"first pass, {sat.Swept * 100 / g.Width} % swept" : $"pass {pass + 1}, band surveyed";
            list.Add((new OrbitObject($"SAT-{n}", "Satellite", $"Lat {where} · {sweep}", SkyBlue), _satMarkers.TryGetValue(sat.Id, out var m) ? m.Sat : null));
        }

        if (_moonBody != null)
        {
            var className = _moonBody.ClassId;
            foreach (var pc in _sim.Data.PlanetClasses)
                if (pc.Id == _moonBody.ClassId) className = pc.Name;
            list.Add((new OrbitObject(_moonBody.Name, "Moon", className, Lavender), _moonRock));
        }

        foreach (var d in _surface.Deliveries)
        {
            if (d.Owner != _localPlayer) continue;
            var (label, colour) = d.Kind switch
            {
                DeliveryKind.Outpost => ("Outpost drop", DropColour),
                DeliveryKind.Shuttle => ("Drop ship", DropColour),
                _ => ("Transporter beam", BeamColour),
            };
            var eta = d.ArriveTick > _sim.Tick ? $"lands in {SecondsTo(d.ArriveTick)} s" : "landing";
            list.Add((new OrbitObject($"{label} {d.Id}", d.Kind.ToString(), $"{PayloadName(d.Payload)} · {eta}", colour),
                _deliveryMarkers.TryGetValue(d.Id, out var marker) ? marker : null));
        }
        return list;
    }

    /// <summary>Whole seconds (rounded up) from now to a sim tick.</summary>
    private long SecondsTo(long tick) => (tick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate;

    private string PayloadName(string payload)
    {
        if (payload == OrbitRules.Supplies) return "Supplies";
        if (_sim.Data.GroundVehicleNames.TryGetValue(payload, out var unit)) return unit;
        foreach (var b in _sim.Data.Buildings)
            if (b.Id == payload) return b.Name;
        return payload;
    }

    /// <summary>Whether the planet hides a point from the camera.</summary>
    private bool Occluded(Vector3 at)
    {
        if (!_globe.Visible) return false;
        var o = _camera.GlobalPosition - _globe.GlobalPosition;
        var to = at - _camera.GlobalPosition;
        var length = to.Length();
        if (length < 0.001f) return false;
        var d = to / length;
        var b = o.Dot(d);
        var disc = b * b - (o.LengthSquared() - Radius * Radius);
        if (disc < 0f) return false;
        var t = -b - Mathf.Sqrt(disc);
        return t > 0f && t < length - 2f; // things just above the ground on the near side still show
    }

    // ---------------------------------------------------------------- atmosphere display

    /// <summary>What the game knows about the air and the world under it (no invented numbers: what the data lacks says unknown).</summary>
    public IReadOnlyList<HelmRow> AtmosphereRows()
    {
        var rows = new List<HelmRow>();
        var body = SystemPlot.FindBody(SystemMap(), CurrentName());
        rows.Add(new HelmRow("PLANET", body?.Name ?? CurrentName() ?? "unknown"));
        var classId = _surface.Map.Def.ClassId;
        PlanetClassDef? planetClass = null;
        foreach (var pc in _sim.Data.PlanetClasses)
            if (pc.Id == classId) planetClass = pc;
        rows.Add(new HelmRow("CLASS", planetClass?.Name ?? classId));
        if (planetClass != null) rows.Add(new HelmRow("HABITABILITY", $"{Mathf.RoundToInt(planetClass.Habitability.ToFloat() * 100f)} %"));
        var toxic = Mathf.RoundToInt(_toxicShare * 100f);
        rows.Add(_toxicShare > 0f
            ? new HelmRow("TOXIC ZONES", toxic > 0 ? $"{toxic} % of the surface" : "under 1 % of the surface", new Color(0.8f, 1f, 0.1f))
            : new HelmRow("TOXIC ZONES", "none found"));
        rows.Add(new HelmRow("BIOMASS", $"{Mathf.RoundToInt(_meanBiomass * 100f)} % average"));
        rows.Add(new HelmRow("CLOUD COVER", $"{Mathf.RoundToInt(_cloudCover * 100f)} % (low layer)"));
        rows.Add(new HelmRow("STORM CELLS", _stormCells.Count.ToString()));
        // Composition, pressure and temperature come from the outpost's sensors once its uplink is online (the driver adds
        // the telemetry sections under these rows).
        return rows;
    }

    /// <summary>
    /// Finds the storm cells: samples the low cloud layer's cover on the CPU (the same noise bytes and the same
    /// cloud_field / cloud_alpha as clouds.gdshaderinc, without the close-up detail octaves) at 2400 points, takes the
    /// mean cover in caps of 0.2 rad, and rings up to four of the densest caps (at least 0.8 rad apart, mean cover 0.6+).
    /// Also gives the layer's cover share for AtmosphereRows.
    /// </summary>
    private void BuildWeather()
    {
        _stormCells.Clear();
        const int samples = 2400;
        var dirs = new Vector3[samples];
        var alpha = new float[samples];
        var sum = 0f;
        for (var i = 0; i < samples; i++)
        {
            var y = 1f - 2f * (i + 0.5f) / samples;
            var r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            var phi = i * 2.3999632f; // golden angle: an even spread (Fibonacci sphere)
            dirs[i] = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
            alpha[i] = CloudAlpha(CloudField(dirs[i], LowClouds), dirs[i], LowClouds);
            sum += alpha[i];
        }
        _cloudCover = sum / samples;

        var near = Mathf.Cos(0.2f);
        var candidates = new List<(int Index, float Density)>();
        for (var c = 0; c < samples; c += 5)
        {
            var total = 0f;
            var count = 0;
            for (var j = 0; j < samples; j++)
            {
                if (dirs[c].Dot(dirs[j]) <= near) continue;
                total += alpha[j];
                count++;
            }
            if (count > 0) candidates.Add((c, total / count));
        }
        candidates.Sort((a, b) => b.Density.CompareTo(a.Density));
        var apart = Mathf.Cos(0.8f);
        foreach (var (c, density) in candidates)
        {
            if (_stormCells.Count >= 4 || density < 0.6f) break;
            var clear = true;
            foreach (var cell in _stormCells)
                if (cell.Dir.Dot(dirs[c]) > apart) clear = false;
            if (clear) _stormCells.Add((dirs[c], density));
        }

        var height = Radius + LowClouds.Height + 0.5f;
        foreach (var (dir, _) in _stormCells)
        {
            var cell = new Node3D { Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, dir)), dir * height) };
            cell.AddChild(OrbitLines.Instance(OrbitLines.Circle(height * 0.2f, 72), Peach with { A = 0.9f }, 3));
            cell.AddChild(OrbitLines.Instance(OrbitLines.Circle(height * 0.11f, 36, dashed: true), Peach with { A = 0.6f }, 3));
            _weather.AddChild(cell);
        }
    }

    /// <summary>The cloud noise texture as the GPU samples it: linear filtering, repeat, texel centres at (i + 0.5) / n.</summary>
    private float NoiseAt(Vector3 p)
    {
        const int n = CloudN;
        float x = p.X * n - 0.5f, y = p.Y * n - 0.5f, z = p.Z * n - 0.5f;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
        float fx = x - x0, fy = y - y0, fz = z - z0;
        static int Wrap(int i) => ((i % n) + n) % n;
        float V(int i, int j, int k) => _cloudData[(Wrap(k) * n + Wrap(j)) * n + Wrap(i)];
        var a = Mathf.Lerp(Mathf.Lerp(V(x0, y0, z0), V(x0 + 1, y0, z0), fx), Mathf.Lerp(V(x0, y0 + 1, z0), V(x0 + 1, y0 + 1, z0), fx), fy);
        var b = Mathf.Lerp(Mathf.Lerp(V(x0, y0, z0 + 1), V(x0 + 1, y0, z0 + 1), fx), Mathf.Lerp(V(x0, y0 + 1, z0 + 1), V(x0 + 1, y0 + 1, z0 + 1), fx), fy);
        return Mathf.Lerp(a, b, fz) / 255f;
    }

    /// <summary>clouds.gdshaderinc cloud_field with p_detail = 0.</summary>
    private float CloudField(Vector3 dir, CloudSpec spec)
    {
        var q = dir * spec.Scale + spec.Offset;
        var weather = NoiseAt(q * 0.3f + new Vector3(0.21f, 0.47f, 0.83f));
        var h = q * 0.5f;
        var warp = new Vector3(NoiseAt(h), NoiseAt(h + new Vector3(0.37f, 0.11f, 0.71f)), NoiseAt(h + new Vector3(0.63f, 0.89f, 0.23f))) - new Vector3(0.5f, 0.5f, 0.5f);
        q += warp * 0.15f;
        float f = 0f, a = 0.5f, total = 0f;
        for (var i = 0; i < 5; i++)
        {
            f += a * NoiseAt(q);
            total += a;
            q = q * 2.07f + new Vector3(0.13f, 0.41f, 0.29f);
            a *= 0.5f;
        }
        return f / total + (weather - 0.5f) * 0.45f;
    }

    /// <summary>clouds.gdshaderinc cloud_alpha.</summary>
    private static float CloudAlpha(float field, Vector3 dir, CloudSpec spec)
    {
        var lat = Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f));
        var x = Mathf.Clamp((field + 0.015f * Mathf.Cos(lat * 7.2f) - spec.Cover) / spec.Softness, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    // ---------------------------------------------------------------- view rect

    /// <summary>
    /// The part of the window the helm frame leaves open (in screenSize's coordinates; it is rescaled to the viewport).
    /// The camera then frames the globe (or the plot) in its centre: FrameViewRect slides the camera sideways and up
    /// in its own plane after aiming it, the same as Camera3D H/V offsets but on the real transform, so Godot's
    /// ProjectRay*/UnprojectPosition (picking, ScreenPositionOf) stay exact.
    /// </summary>
    public void SetViewRect(Rect2 viewRect, Vector2 screenSize)
    {
        _viewRect = viewRect;
        _viewScreen = screenSize;
    }

    /// <summary>Centre of the open part of the viewscreen in viewport pixels (the screen centre until SetViewRect): pick here for "the tile in the middle".</summary>
    public Vector2 ViewCentre
    {
        get
        {
            var size = ViewportSize();
            if (_viewRect is not { } r || _viewScreen.X <= 0f || _viewScreen.Y <= 0f) return size / 2f;
            return r.GetCenter() / _viewScreen * size;
        }
    }

    private Vector2 ViewportSize()
    {
        var size = _camera.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        return size.X > 0f && size.Y > 0f ? size : new Vector2(1920f, 1080f);
    }

    private float TanHalfFov() => Mathf.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f);

    /// <summary>
    /// After LookAt(origin): move the camera in its own plane so the origin (globe or star), at this depth, lands on
    /// ViewCentre. In camera space the origin is then at (-h, -v, -depth), which projects to NDC (-h / (depth · tan · aspect), -v / (depth · tan)).
    /// </summary>
    private void FrameViewRect(float depth)
    {
        var size = ViewportSize();
        var c = ViewCentre;
        var nx = c.X / size.X * 2f - 1f;
        var ny = 1f - c.Y / size.Y * 2f;
        var tan = TanHalfFov();
        var h = -nx * depth * tan * (size.X / size.Y);
        var v = -ny * depth * tan;
        _camera.Position += _camera.Basis.X * h + _camera.Basis.Y * v;
    }
}
