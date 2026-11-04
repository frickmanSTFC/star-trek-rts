using System.Collections.Generic;
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
/// The view from the player's ship in orbit: the planet as a globe you turn and zoom, its scans as overlays
/// (resources, lifeforms, biologics, landing safety), markers for bases, vehicles, satellites and things on their way
/// down, and the faction's ship in the foreground. The globe's texture comes straight from the sim's tile map, laid
/// around the sphere (the map wraps east–west; the poles beyond its rows are ice). Render-only: nothing here
/// changes the sim; picking turns a mouse position into a tile for the driver's commands.
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

    private Simulation _sim = null!;
    private PlanetSurface _surface = null!;
    private int _localPlayer;

    private Node3D _globe = null!;
    private MeshInstance3D _clouds = null!;
    private MeshInstance3D _highClouds = null!;
    private ShaderMaterial _groundMaterial = null!;
    private Node3D _moon = null!;
    private long _lightsStamp = -1;
    private Vector3 _sunDir;
    private MeshInstance3D _overlay = null!;
    private StandardMaterial3D _overlayMaterial = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _light = null!;
    private Node3D _ship = null!;
    private MeshInstance3D _sweep = null!;
    private MeshInstance3D _hover = null!;
    private StandardMaterial3D _hoverMaterial = null!;
    private Node3D _native = null!;

    private readonly ImageTexture?[] _overlayTextures = new ImageTexture?[5];
    private readonly long[] _overlayStamp = { -1, -1, -1, -1, -1 };
    private readonly Dictionary<int, Node3D> _baseMarkers = new();
    private readonly Dictionary<int, MeshInstance3D> _unitMarkers = new();
    private readonly Dictionary<int, Node3D> _deliveryMarkers = new();
    private readonly Dictionary<int, (Node3D Sat, MeshInstance3D Band)> _satMarkers = new();

    private float _yaw, _pitch, _yawTarget, _pitchTarget;
    private float _distance = 330f, _distanceTarget = 330f;
    private float _sweepTime = -1f;
    private float _time;

    public ScanMode Mode { get; private set; } = ScanMode.Landing;
    public Camera3D Camera => _camera;

    // ---------------------------------------------------------------- setup

    public void Build(Simulation sim, int localPlayer, string factionId)
    {
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;

        _camera = new Camera3D { Fov = 45f, Near = 0.5f, Far = 9000f, Position = new Vector3(0, 0, _distance) };
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

        // Two cloud layers: thick swirling banks low, thin wisps higher up, drifting at different speeds.
        _clouds = CloudLayer(Radius + 1.4f, 1701, 0.0045f, 0.5f, 0.9f);
        AddChild(_clouds);
        _highClouds = CloudLayer(Radius + 2.4f, 4242, 0.009f, 0.62f, 0.45f);
        AddChild(_highClouds);

        // Atmosphere: one shell, ray-marched (Rayleigh + Mie scattering) in its shader.
        AddChild(Atmosphere());

        _moon = Moon();
        AddChild(_moon);

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

        _ship = FactionShip(factionId, PlanetDriver.PlayerColors[0]);
        _camera.AddChild(_ship); // rides with the camera: the view from the ship
        _ship.Position = new Vector3(-30f, -21f, -66f);
        _ship.RotationDegrees = new Vector3(8f, -28f, 4f);

        SetScan(ScanMode.Landing);
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

    /// <summary>A cloud shell: domain-warped noise for swirls, cut off below <paramref name="cover"/> so there are clear skies between banks.</summary>
    private static MeshInstance3D CloudLayer(float radius, int seed, float frequency, float cover, float opacity)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            Frequency = frequency,
            FractalOctaves = 6,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            DomainWarpEnabled = true,
            DomainWarpAmplitude = 40f,
            DomainWarpFrequency = frequency * 1.5f,
        };
        var texture = new NoiseTexture2D
        {
            Width = 2048, Height = 1024, Seamless = true, Noise = noise,
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, cover, cover + 0.12f, 1f },
                Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 0), new Color(1, 1, 1, opacity * 0.6f), new Color(1, 1, 1, opacity) },
            },
        };
        return new MeshInstance3D
        {
            Mesh = SphereMesh(radius, 64, 128),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
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
        var origin = _camera.ProjectRayOrigin(mouse);
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
        var stamp = _surface.Structures.Count * 7919L + _surface.Bases(_localPlayer).Count;
        if (stamp == _lightsStamp) return;
        _lightsStamp = stamp;
        var map = _surface.Map;
        var glow = new Dictionary<int, float>();
        foreach (var s in _surface.Structures)
        {
            if (s.Owner != _localPlayer || s.State != StructureState.Complete) continue;
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
        _overlay.Visible = Mode != ScanMode.Terrain;
        if (Mode == ScanMode.Terrain) return;
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
    public void Rotate(float yaw, float pitch)
    {
        _yawTarget += yaw;
        _pitchTarget = Mathf.Clamp(_pitchTarget + pitch, -1.25f, 1.25f);
    }

    /// <summary>Zoom: factor below 1 moves in.</summary>
    public void Zoom(float factor) => _distanceTarget = Mathf.Clamp(_distanceTarget * factor, 135f, 520f);

    /// <summary>Turn the globe so this tile faces the camera, and zoom to a distance.</summary>
    public void Focus(int tile, float distance)
    {
        var p = TileDirection(tile);
        _yawTarget = -Mathf.Atan2(p.X, p.Z);
        // Keep the yaw turn short: pick the equivalent angle nearest the current one.
        while (_yawTarget - _yaw > Mathf.Pi) _yawTarget -= Mathf.Tau;
        while (_yawTarget - _yaw < -Mathf.Pi) _yawTarget += Mathf.Tau;
        _pitchTarget = Mathf.Clamp(Mathf.Atan2(p.Y, new Vector2(p.X, p.Z).Length()), -1.25f, 1.25f);
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
        _moon.Rotation = _moon.Rotation with { Y = _time * 0.02f };
        RefreshLights();
        _camera.Position = new Vector3(0, 0, _distance);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        _ship.Position = new Vector3(-30f, -21f + Mathf.Sin(_time * 0.6f) * 0.6f, -66f);

        if (_sweepTime >= 0f)
        {
            _sweepTime += delta;
            var t = _sweepTime / 1.3f;
            _sweep.Visible = t < 1f;
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

    /// <summary>A beacon on each of your bases: a lit mast and a ring on the ground.</summary>
    private void UpdateBases()
    {
        var alive = new HashSet<int>();
        foreach (var core in _surface.Bases(_localPlayer))
        {
            alive.Add(core.Id);
            if (_baseMarkers.ContainsKey(core.Id)) continue;
            var marker = new Node3D();
            var colour = PlanetDriver.PlayerColors[core.Owner % PlanetDriver.PlayerColors.Length];
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
            if (u.Owner != _localPlayer) continue;
            alive.Add(u.Id);
            if (!_unitMarkers.TryGetValue(u.Id, out var dot))
            {
                dot = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f, RadialSegments = 8, Rings = 4 }, MaterialOverride = GlowMaterial(new Color(1f, 0.95f, 0.7f), 2f) };
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

    /// <summary>Outpost pods and shuttles fall from high orbit to their tile; a beam is a shimmering column.</summary>
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
                m = (model, band);
                _satMarkers[sat.Id] = m;
            }
            var cols = (_sim.Tick - sat.LaunchTick + alpha) * g.Width / (orbit.SatelliteSweepSeconds * Simulation.TickRate);
            var phi = (cols % g.Width) / g.Width * Mathf.Tau;
            var dir = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
            m.Sat.Position = dir * (Radius + 14f);
            m.Sat.LookAt(_globe.GlobalTransform * Vector3.Zero, Vector3.Up);
        }
        foreach (var id in new List<int>(_satMarkers.Keys))
        {
            if (alive.Contains(id)) continue;
            _satMarkers[id].Sat.QueueFree();
            _satMarkers[id].Band.QueueFree();
            _satMarkers.Remove(id);
        }
    }

    // ---------------------------------------------------------------- the ship

    /// <summary>The player's ship, close to the camera in the lower left: shapes by faction (built from primitives).</summary>
    private static Node3D FactionShip(string faction, Color owner)
    {
        StandardMaterial3D Hull(Color c) => new() { AlbedoColor = c, Metallic = 0.5f, Roughness = 0.4f };
        var n = new Node3D();
        void Add(Mesh mesh, Vector3 at, Material m, Vector3? rot = null)
        {
            var mi = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = m };
            if (rot is { } r) mi.RotationDegrees = r;
            n.AddChild(mi);
        }
        switch (faction)
        {
            case "borg":
            {
                Add(new BoxMesh { Size = new Vector3(22, 22, 22) }, Vector3.Zero, Hull(new Color(0.18f, 0.2f, 0.18f)));
                for (var i = 0; i < 10; i++)
                    Add(new BoxMesh { Size = new Vector3(22.2f, 0.4f, 0.4f) }, new Vector3(0, -10f + i * 2.2f, 11.1f), GlowMaterial(new Color(0.3f, 1f, 0.35f), 2f));
                break;
            }
            case "klingon":
            {
                var green = Hull(new Color(0.32f, 0.38f, 0.3f));
                Add(new BoxMesh { Size = new Vector3(4, 2.4f, 16) }, Vector3.Zero, green);
                Add(new SphereMesh { Radius = 3f, Height = 3.4f }, new Vector3(0, 0, -9f), green);
                foreach (var x in new[] { -1f, 1f })
                    Add(new BoxMesh { Size = new Vector3(14, 0.6f, 6) }, new Vector3(x * 8f, -1.5f, 3f), green, new Vector3(0, 0, x * -12f));
                Add(new SphereMesh { Radius = 1f, Height = 2f }, new Vector3(0, -0.6f, -11.6f), GlowMaterial(new Color(1f, 0.25f, 0.2f), 3f));
                break;
            }
            case "romulan":
            {
                var green = Hull(new Color(0.3f, 0.5f, 0.38f));
                foreach (var y in new[] { -3.5f, 3.5f })
                    Add(new CylinderMesh { TopRadius = 7f, BottomRadius = 7f, Height = 0.8f, RadialSegments = 20 }, new Vector3(0, y, 2f), green, new Vector3(0, 0, 0));
                Add(new BoxMesh { Size = new Vector3(2, 8, 4) }, new Vector3(0, 0, 8f), green);
                Add(new SphereMesh { Radius = 2f, Height = 3f }, new Vector3(0, 0, -6f), green);
                Add(new SphereMesh { Radius = 0.9f, Height = 1.8f }, new Vector3(0, 0, 8f), GlowMaterial(new Color(0.4f, 1f, 0.5f), 3f));
                break;
            }
            case "federation":
            default:
            {
                var hull = Hull(faction == "federation" ? new Color(0.82f, 0.84f, 0.88f) : new Color(0.6f, 0.55f, 0.5f));
                // Saucer, neck, engineering hull, two nacelles on pylons.
                Add(new CylinderMesh { TopRadius = 9f, BottomRadius = 9.6f, Height = 1.6f, RadialSegments = 40 }, new Vector3(0, 1.5f, -6f), hull);
                Add(new SphereMesh { Radius = 2.6f, Height = 1.6f, IsHemisphere = true }, new Vector3(0, 2.3f, -6f), hull);
                Add(new BoxMesh { Size = new Vector3(1.6f, 3f, 4f) }, new Vector3(0, -0.4f, 0.5f), hull, new Vector3(-25, 0, 0));
                Add(new CapsuleMesh { Radius = 2f, Height = 13f }, new Vector3(0, -2f, 5f), hull, new Vector3(90, 0, 0));
                Add(new SphereMesh { Radius = 1.1f, Height = 1.4f }, new Vector3(0, -2f, -1.6f), GlowMaterial(new Color(0.35f, 0.7f, 1f), 3f));
                foreach (var x in new[] { -1f, 1f })
                {
                    Add(new BoxMesh { Size = new Vector3(5.5f, 0.5f, 2f) }, new Vector3(x * 3.4f, 0.2f, 8f), hull, new Vector3(0, 0, x * 35f));
                    Add(new CapsuleMesh { Radius = 1.1f, Height = 15f }, new Vector3(x * 6f, 2.4f, 9f), hull, new Vector3(90, 0, 0));
                    Add(new BoxMesh { Size = new Vector3(0.5f, 0.3f, 12f) }, new Vector3(x * 6.9f, 2.4f, 9f), GlowMaterial(new Color(0.4f, 0.7f, 1f), 3.5f));
                    Add(new SphereMesh { Radius = 1.05f, Height = 2.1f }, new Vector3(x * 6f, 2.4f, 1.7f), GlowMaterial(new Color(1f, 0.3f, 0.2f), 3.5f));
                }
                Add(new CylinderMesh { TopRadius = 9.65f, BottomRadius = 9.65f, Height = 0.2f, RadialSegments = 40 }, new Vector3(0, 1f, -6f), GlowMaterial(owner, 1.2f));
                break;
            }
        }
        n.AddChild(new OmniLight3D { LightColor = new Color(0.9f, 0.95f, 1f), LightEnergy = 0.6f, OmniRange = 40f, Position = new Vector3(10, 10, 10) });
        return n;
    }
}
