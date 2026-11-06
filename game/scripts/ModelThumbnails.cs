using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Pictures of buildings and vehicles for the HUD, rendered in the game from the real models (owner 2026-11-06: runtime
/// renders, not images in the repo). Two studios, each a <see cref="SubViewport"/> with its own World3D (own camera,
/// key / fill / rim lights and Environment, nothing else: no fog, scatter or sky shaders reach it):
/// <list type="bullet">
/// <item>Thumbnails, 256²: <see cref="Thumb"/> queues a key ("refinery", "unit:mining_crawler") the first time it is
/// asked for and returns null until it is ready (callers show the line icon meanwhile). One job at a time: the model is
/// built with the plain look in the player's colour (<see cref="BuildingModels.Build(string, Color)"/> or
/// <see cref="BuildingModels.Vehicle"/>), framed at the same 3/4 angle (38° round from the front, 27° down), rendered
/// for two frames (counted on RenderingServer.FramePostDraw, so glb materials and shader pipelines have settled) and
/// copied into an <see cref="ImageTexture"/> with mipmaps; then the model is freed and the viewport goes back to
/// Disabled. <see cref="Version"/> goes up whenever a picture arrives or the cache is dropped.</item>
/// <item>Hero, 640×512: <see cref="ShowHero"/> puts the live model on a slow turntable for the large card
/// (UpdateMode Always only while it is shown; <see cref="HideHero"/> frees the model and stops rendering).
/// <see cref="HeroTexture"/> is the viewport's texture for a TextureRect.</item>
/// </list>
/// Render-only: nothing here reads or changes the sim.
/// </summary>
public partial class ModelThumbnails : Node
{
    public const int ThumbSize = 256;
    public static readonly Vector2I HeroSize = new(640, 512);

    /// <summary>The camera's angle round the model from its front (+Z) and down from level, degrees.</summary>
    private const float Round = 38f, Down = 27f, Fov = 30f;

    /// <summary>Frames a job renders before it is captured.</summary>
    private const int WarmFrames = 2;

    private readonly Func<string, Color, Node3D?> _build;
    private Color _owner;
    private readonly Dictionary<string, ImageTexture> _cache = new();
    private readonly List<string> _queue = new();
    private readonly HashSet<string> _queued = new();

    private SubViewport _studio = null!;
    private Camera3D _studioCamera = null!;
    private Node3D _studioStage = null!;
    private string? _job;
    private Node3D? _jobModel;
    private int _drawn;
    private int _jobAge;

    private SubViewport _hero = null!;
    private Camera3D _heroCamera = null!;
    private Node3D _heroPivot = null!;
    private Node3D? _heroModel;
    private string? _heroKey;
    private float _heroYaw;

    private bool _watching;

    /// <summary>Goes up every time a thumbnail is added or the cache is dropped (player colour changed).</summary>
    public int Version { get; private set; }

    /// <summary>The live turntable's picture (only changes while <see cref="ShowHero"/> is on).</summary>
    public Texture2D HeroTexture => _hero.GetTexture();

    /// <summary>Extra yaw from dragging the card's picture, radians (added to the slow turn).</summary>
    public float HeroDrag { get; set; }

    /// <param name="build">Builds the model for a key in the given colour (null = nothing to show).</param>
    public ModelThumbnails(Func<string, Color, Node3D?> build, Color owner)
    {
        _build = build;
        _owner = owner;
    }

    public override void _Ready()
    {
        Name = "ModelThumbnails";
        _studio = MakeStudio("ThumbStudio", new Vector2I(ThumbSize, ThumbSize), out _studioCamera, out _studioStage);
        _hero = MakeStudio("HeroStudio", HeroSize, out _heroCamera, out _heroPivot);
        RenderingServer.FramePostDraw += OnFramePostDraw;
        _watching = true;
    }

    public override void _ExitTree()
    {
        if (_watching) RenderingServer.FramePostDraw -= OnFramePostDraw;
        _watching = false;
    }

    private void OnFramePostDraw()
    {
        if (_job != null && _jobModel != null) _drawn++;
    }

    /// <summary>
    /// A SubViewport with its own World3D: size, MSAA, transparent background and UpdateMode are set before it enters
    /// the tree, then the environment, the three lights, a camera and an empty stage for the model are added.
    /// </summary>
    private SubViewport MakeStudio(string name, Vector2I size, out Camera3D camera, out Node3D stage)
    {
        var vp = new SubViewport
        {
            Name = name,
            Size = size,
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            PositionalShadowAtlasSize = 0, // no omni / spot shadows in here: the models' own lamps just glow
        };
        AddChild(vp);

        // Ambient and reflections from a plain procedural sky (so metal reads as metal), but the background is the
        // clear colour, which TransparentBg makes see-through. No fog, glow or SSAO.
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.16f, 0.24f, 0.4f),
                SkyHorizonColor = new Color(0.5f, 0.56f, 0.66f),
                GroundBottomColor = new Color(0.05f, 0.06f, 0.09f),
                GroundHorizonColor = new Color(0.3f, 0.32f, 0.38f),
                SunAngleMax = 30f,
            },
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            GlowEnabled = false,
            FogEnabled = false,
            VolumetricFogEnabled = false,
            SsaoEnabled = false,
        };
        vp.AddChild(new WorldEnvironment { Environment = env });

        // Key light from the front left, high; a cool fill from the right without shadows; a rim from behind.
        var key = new DirectionalLight3D
        {
            Name = "Key",
            LightColor = new Color(1f, 0.96f, 0.9f),
            LightEnergy = 1.5f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 120f,
        };
        vp.AddChild(key);
        key.RotationDegrees = new Vector3(-48f, -32f, 0f);
        var fill = new DirectionalLight3D { Name = "Fill", LightColor = new Color(0.62f, 0.74f, 1f), LightEnergy = 0.45f };
        vp.AddChild(fill);
        fill.RotationDegrees = new Vector3(-20f, 70f, 0f);
        var rim = new DirectionalLight3D { Name = "Rim", LightColor = new Color(0.8f, 0.88f, 1f), LightEnergy = 0.8f };
        vp.AddChild(rim);
        rim.RotationDegrees = new Vector3(-28f, 165f, 0f);

        camera = new Camera3D { Name = "Camera", Fov = Fov, Near = 0.2f, Far = 600f };
        vp.AddChild(camera);
        camera.Current = true; // this viewport's own camera; the game's camera is untouched
        stage = new Node3D { Name = "Stage" };
        vp.AddChild(stage);
        return vp;
    }

    // ---------------------------------------------------------------- thumbnails

    /// <summary>The thumbnail for a key, or null while it is being made (it is queued the first time it is asked for).</summary>
    public Texture2D? Thumb(string key)
    {
        if (_cache.TryGetValue(key, out var t)) return t;
        if (_queued.Add(key)) _queue.Add(key);
        return null;
    }

    /// <summary>The player's colour for every picture; a new colour drops the cache (pictures come back as they are asked for).</summary>
    public void SetOwner(Color owner)
    {
        if (owner.IsEqualApprox(_owner)) return;
        _owner = owner;
        _cache.Clear();
        _queued.Clear();
        _queue.Clear();
        CancelJob();
        Version++;
        if (_heroKey != null) ShowHero(_heroKey, force: true);
    }

    public override void _Process(double delta)
    {
        if (_heroModel != null)
        {
            _heroYaw += (float)delta * 0.35f;
            _heroPivot.Rotation = new Vector3(0, _heroYaw + HeroDrag, 0);
        }

        if (_job != null)
        {
            _jobAge++;
            if (_drawn >= WarmFrames) Capture();
            else if (_jobAge > 30) CancelJob(); // never drew (minimised window?): try again later
            return;
        }
        while (_queue.Count > 0 && _job == null)
        {
            var key = _queue[0];
            _queue.RemoveAt(0);
            StartJob(key);
        }
    }

    private void StartJob(string key)
    {
        var model = _build(key, _owner);
        if (model == null)
        {
            _cache[key] = ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
            Version++;
            return;
        }
        _job = key;
        _jobModel = Stage(_studioStage, model, key);
        Frame(_studioCamera, _studioStage, 1.0f);
        _drawn = 0;
        _jobAge = 0;
        _studio.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    private void Capture()
    {
        var image = _studio.GetTexture().GetImage();
        if (image != null && !image.IsEmpty())
        {
            image.GenerateMipmaps();
            _cache[_job!] = ImageTexture.CreateFromImage(image);
            Version++;
        }
        else _queued.Remove(_job!); // try again the next time someone asks
        ClearJob();
    }

    private void CancelJob()
    {
        if (_job != null) _queued.Remove(_job);
        ClearJob();
    }

    private void ClearJob()
    {
        _studio.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_jobModel != null)
        {
            _studioStage.RemoveChild(_jobModel);
            _jobModel.QueueFree();
        }
        foreach (var child in _studioStage.GetChildren()) child.QueueFree(); // the ground disc
        _jobModel = null;
        _job = null;
        _drawn = 0;
    }

    // ---------------------------------------------------------------- hero turntable

    /// <summary>The live, slowly turning model for the large card. Same key again = keep it (and its angle).</summary>
    public void ShowHero(string key, bool force = false)
    {
        if (!force && key == _heroKey && _heroModel != null) return;
        ClearHero();
        var model = _build(key, _owner);
        _heroKey = key;
        if (model == null) return;
        _heroModel = Stage(_heroPivot, model, key);
        _heroPivot.Rotation = Vector3.Zero;
        Frame(_heroCamera, _heroPivot, 0.92f);
        _heroYaw = 0;
        HeroDrag = 0;
        _hero.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    /// <summary>Stops the turntable (the viewport stops rendering) and frees its model.</summary>
    public void HideHero()
    {
        ClearHero();
        _heroKey = null;
    }

    private void ClearHero()
    {
        _hero.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        foreach (var child in _heroPivot.GetChildren())
        {
            _heroPivot.RemoveChild(child);
            child.QueueFree();
        }
        _heroModel = null;
    }

    // ---------------------------------------------------------------- staging

    /// <summary>
    /// Puts the model on the stage, centred over the origin in XZ (so it turns about its middle) and facing the camera's
    /// side: buildings face +Z already, vehicles face -Z and are turned round. Adds a dark ground disc under it.
    /// </summary>
    private static Node3D Stage(Node3D stage, Node3D model, string key)
    {
        var holder = new Node3D { Name = "Model" };
        stage.AddChild(holder);
        holder.AddChild(model);
        if (key.StartsWith("unit:")) model.RotationDegrees = new Vector3(0, 180f, 0);
        var box = Bounds(holder);
        var centre = box.GetCenter();
        holder.Position = new Vector3(-centre.X, 0, -centre.Z);
        var radius = Mathf.Max(box.Size.X, box.Size.Z) * 0.5f;
        stage.AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new CylinderMesh { TopRadius = radius * 1.12f + 0.6f, BottomRadius = radius * 1.12f + 0.6f, Height = 0.08f, RadialSegments = 48 },
            Position = new Vector3(0, -0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.09f, 0.14f), Roughness = 0.92f },
        });
        return holder;
    }

    /// <summary>Aims the camera at the stage's bounding sphere from the fixed 3/4 angle; fill = share of the frame.</summary>
    private static void Frame(Camera3D camera, Node3D stage, float fill)
    {
        var box = Bounds(stage);
        var centre = box.GetCenter();
        var radius = Mathf.Max(0.5f, box.Size.Length() * 0.5f);
        var aspect = 1f;
        if (camera.GetViewport() is SubViewport vp && vp.Size.Y > 0) aspect = Mathf.Min(1f, (float)vp.Size.X / vp.Size.Y);
        var half = Mathf.DegToRad(Fov * 0.5f);
        var distance = radius / Mathf.Sin(half) / fill / Mathf.Max(0.6f, aspect);
        var r = Mathf.DegToRad(Round);
        var d = Mathf.DegToRad(Down);
        var dir = new Vector3(Mathf.Sin(r) * Mathf.Cos(d), Mathf.Sin(d), Mathf.Cos(r) * Mathf.Cos(d));
        // Look a little below the centre so tall models (derricks, stacks) don't crowd the top edge.
        var target = centre with { Y = centre.Y * 0.85f };
        camera.LookAtFromPosition(target + dir * distance, target, Vector3.Up);
        camera.Far = distance + radius * 4f;
        foreach (var child in stage.GetParent().GetChildren())
            if (child is DirectionalLight3D { ShadowEnabled: true } key) key.DirectionalShadowMaxDistance = distance + radius * 2f;
    }

    /// <summary>The visible meshes' box in <paramref name="root"/>'s space (lights, particles and labels left out).</summary>
    private static Aabb Bounds(Node3D root)
    {
        var inverse = root.GlobalTransform.AffineInverse();
        var any = false;
        var box = new Aabb();
        void Walk(Node n)
        {
            if (n is Node3D { Visible: false }) return;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                var b = (inverse * mi.GlobalTransform) * mi.GetAabb();
                box = any ? box.Merge(b) : b;
                any = true;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        return any ? box : new Aabb(new Vector3(-3, 0, -3), new Vector3(6, 4, 6));
    }
}
