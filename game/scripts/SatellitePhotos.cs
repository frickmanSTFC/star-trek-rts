using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>How a satellite picture is shown (a canvas shader on the picture: <see cref="SatellitePhotos.PhotoShader"/>).</summary>
public enum PhotoMode
{
    Visual,
    Infrared,
    Terrain,
}

/// <summary>One picture a satellite took: what, where and when (for the stamp), how it is shown, and the image.</summary>
public sealed record SatellitePhoto(int SatelliteId, long Tick, int Tile, PhotoMode Mode, string Title, string Stamp, Texture2D Texture,
    bool Finding, bool FromGlobe);

/// <summary>
/// The survey satellites' camera (docs/satellite-contract.md "Photo feed"; render-only). A picture of the ground under a
/// satellite is rendered from the real surface: one small SubViewport (320×200, sharing the main World3D) with a camera
/// that sees only <see cref="SurfaceFeed.FeedLayer"/>, looking almost straight down from 260 above the tile. In orbit
/// PlanetView is hidden, so while pictures are wanted this asks SurfaceFeed to hold the surface on that layer
/// (<see cref="SurfaceFeed.HoldSurface"/>, the outpost feed's own trick) and waits until it is. One capture at a time:
/// the viewport renders (UpdateMode Always) for two frames counted on RenderingServer.FramePostDraw, the image is copied
/// into an ImageTexture and the viewport goes back to Disabled. If the surface isn't shown within a few frames the
/// picture is a crop of the orbit globe's painted texture instead (<see cref="OrbitView.GlobeCrop"/>). Cost: pictures
/// are only queued while the satellite card is open (every few seconds of sweeping) or on a new finding (at most a
/// few waiting); each is ~3 frames of a 320×200 render. The last <see cref="Kept"/> per satellite are kept.
/// </summary>
public partial class SatellitePhotos : Node
{
    public static readonly Vector2I PhotoSize = new(320, 200);

    /// <summary>Pictures kept per satellite (the card's strip).</summary>
    public const int Kept = 8;

    private const int MaxWaiting = 4;
    private const int WarmFrames = 2;

    private readonly record struct Job(int SatelliteId, long Tick, int Tile, PhotoMode Mode, string Title, string Stamp, bool Finding);

    private PlanetView _view = null!;
    private SurfaceFeed _feed = null!;
    private OrbitView _orbit = null!;
    private SubViewport _port = null!;
    private Camera3D _camera = null!;
    private bool _watching;

    private readonly List<Job> _queue = new();
    private Job? _job;
    private int _drawn;
    private int _waited;
    private readonly Dictionary<int, List<SatellitePhoto>> _photos = new();
    private static readonly List<SatellitePhoto> None = new();

    /// <summary>Keep the surface ready for pictures (the satellite card is open), not only while one is waiting.</summary>
    public bool Wanted { get; set; }

    /// <summary>Goes up whenever a picture arrives (the card rebuilds its strip then).</summary>
    public int Version { get; private set; }

    /// <summary>A picture is being taken or waits.</summary>
    public bool Busy => _job != null || _queue.Count > 0;

    public void Setup(PlanetView view, SurfaceFeed feed, OrbitView orbit)
    {
        _view = view;
        _feed = feed;
        _orbit = orbit;
    }

    public override void _Ready()
    {
        Name = "SatellitePhotos";
        // Size, update mode and world are set before it enters the tree; it shares the main World3D (the surface's nodes).
        _port = new SubViewport
        {
            Name = "SatellitePort",
            Size = PhotoSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = false,
            OwnWorld3D = false,
            PositionalShadowAtlasSize = 0,
        };
        AddChild(_port);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.03f, 0.05f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.66f, 0.74f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapWhite = 6f,
            FogEnabled = true, // a little air between the satellite and the ground
            FogLightColor = new Color(0.6f, 0.7f, 0.85f),
            FogDensity = 0.0009f,
        };
        _camera = new Camera3D { Name = "SatelliteCamera", Fov = 38f, Near = 2f, Far = 1600f, CullMask = SurfaceFeed.FeedLayer, Environment = environment };
        _port.AddChild(_camera);
        _camera.Current = true; // this viewport's camera
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
        if (_job != null && _port.RenderTargetUpdateMode == SubViewport.UpdateMode.Always) _drawn++;
    }

    /// <summary>The pictures a satellite took, oldest first (at most <see cref="Kept"/>).</summary>
    public IReadOnlyList<SatellitePhoto> PhotosOf(int satelliteId) => _photos.TryGetValue(satelliteId, out var list) ? list : None;

    /// <summary>Queue a picture of a tile (dropped when a few already wait, unless it is a finding: then the oldest
    /// periodic one makes room).</summary>
    public void Request(int satelliteId, long tick, int tile, PhotoMode mode, string title, string stamp, bool finding)
    {
        if (tile < 0) return;
        if (_queue.Count >= MaxWaiting)
        {
            var periodic = _queue.FindIndex(j => !j.Finding);
            if (!finding || periodic < 0) return;
            _queue.RemoveAt(periodic);
        }
        _queue.Add(new Job(satelliteId, tick, tile, mode, title, stamp, finding));
    }

    /// <summary>Forget a satellite's pictures (it is gone).</summary>
    public void Forget(int satelliteId) => _photos.Remove(satelliteId);

    /// <summary>Once a frame from the driver, after the feed's refresh.</summary>
    public void Update(bool inOrbit)
    {
        _feed.HoldSurface = inOrbit && (Wanted || Busy);
        if (!inOrbit)
        {
            _queue.Clear();
            if (_job != null) Finish(null);
            return;
        }
        if (_job is { } job)
        {
            if (_drawn >= WarmFrames)
            {
                var image = _port.GetTexture().GetImage();
                Finish(image != null && !image.IsEmpty() ? image : null);
            }
            else if (++_waited > 30) Finish(null); // never drew: fall back to the globe
            return;
        }
        if (_queue.Count == 0) return;
        if (!_feed.SurfaceShown)
        {
            // The surface comes onto the feed layer on the feed's next refresh; if it doesn't, use the globe's paint.
            if (++_waited < 8) return;
            var fallback = _queue[0];
            _queue.RemoveAt(0);
            _waited = 0;
            Store(fallback, _orbit.GlobeCrop(fallback.Tile, PhotoSize), fromGlobe: true);
            return;
        }
        _job = _queue[0];
        _queue.RemoveAt(0);
        _drawn = 0;
        _waited = 0;
        var ground = _view.TileCentre(_job.Value.Tile);
        // Nearly straight down (a slight lean from the south shows relief), north up in the picture.
        var eye = ground + new Vector3(0f, 260f, 38f);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt((ground - eye).Normalized(), Vector3.Forward), eye);
        _port.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    private void Finish(Image? image)
    {
        _port.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        var job = _job!.Value;
        _job = null;
        _drawn = 0;
        _waited = 0;
        if (image != null) Store(job, image, fromGlobe: false);
        else Store(job, _orbit.GlobeCrop(job.Tile, PhotoSize), fromGlobe: true);
    }

    private void Store(Job job, Image? image, bool fromGlobe)
    {
        if (image == null) return;
        if (!_photos.TryGetValue(job.SatelliteId, out var list)) _photos[job.SatelliteId] = list = new List<SatellitePhoto>();
        list.Add(new SatellitePhoto(job.SatelliteId, job.Tick, job.Tile, job.Mode, job.Title, job.Stamp, ImageTexture.CreateFromImage(image), job.Finding, fromGlobe));
        if (list.Count > Kept) list.RemoveAt(0);
        Version++;
    }

    // ================================================================== how a picture is shown

    /// <summary>The picture modes as a canvas shader (uniform mode 0 visual, 1 infrared, 2 terrain): visual = the frame
    /// with a little contrast and a faint scan pattern; infrared = luminance and warmth through a false-colour ramp (water
    /// reads cold); terrain = edges of the luminance as green contour lines over a dark relief, with a grid.</summary>
    public const string PhotoShader = @"
shader_type canvas_item;

uniform int mode = 0;
uniform vec2 texel = vec2(0.003125, 0.005);

float lum(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

vec3 ramp(float t) {
    t = clamp(t, 0.0, 1.0);
    vec3 a = vec3(0.02, 0.0, 0.08), b = vec3(0.45, 0.0, 0.6), c = vec3(0.95, 0.15, 0.1), d = vec3(1.0, 0.85, 0.2), e = vec3(1.0, 1.0, 0.92);
    if (t < 0.25) return mix(a, b, t / 0.25);
    if (t < 0.5) return mix(b, c, (t - 0.25) / 0.25);
    if (t < 0.78) return mix(c, d, (t - 0.5) / 0.28);
    return mix(d, e, (t - 0.78) / 0.22);
}

void fragment() {
    vec3 col = texture(TEXTURE, UV).rgb;
    if (mode == 1) {
        float l = lum(col);
        float warm = clamp(col.r - col.b * 0.8 + 0.35, 0.0, 1.0);
        float water = smoothstep(0.05, 0.2, col.b - col.r);
        col = ramp(mix(l * 0.6 + warm * 0.55, 0.06, water));
    } else if (mode == 2) {
        float c = lum(texture(TEXTURE, UV).rgb);
        float gx = lum(texture(TEXTURE, UV + vec2(texel.x, 0.0)).rgb) - lum(texture(TEXTURE, UV - vec2(texel.x, 0.0)).rgb);
        float gy = lum(texture(TEXTURE, UV + vec2(0.0, texel.y)).rgb) - lum(texture(TEXTURE, UV - vec2(0.0, texel.y)).rgb);
        float edge = clamp(length(vec2(gx, gy)) * 5.0, 0.0, 1.0);
        float contour = smoothstep(0.82, 0.98, abs(fract(c * 9.0) - 0.5) * 2.0);
        vec2 cell = fract(UV * vec2(8.0, 5.0));
        float grid = step(cell.x, 0.012) + step(cell.y, 0.02);
        col = vec3(0.02, 0.06, 0.05) + vec3(0.1, 0.32, 0.22) * c + vec3(0.3, 1.0, 0.55) * max(edge, contour * 0.5) + vec3(0.15, 0.5, 0.4) * clamp(grid, 0.0, 1.0) * 0.35;
    } else {
        col = clamp((col - 0.5) * 1.12 + 0.52, 0.0, 1.0);
        col *= 0.96 + 0.04 * sin(UV.y * 400.0);
    }
    vec2 d = UV - 0.5;
    col *= 1.0 - dot(d, d) * 0.6;
    COLOR = vec4(col, 1.0);
}
";
}
