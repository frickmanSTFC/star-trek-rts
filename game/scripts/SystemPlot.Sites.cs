using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>A space site on the system plot (render-only): id, what the player calls it, kind, knowledge.</summary>
public sealed record SystemSiteMark(string Id, string Title, SiteKind Kind, SiteKnowledge Knowledge);

// Space sites on the system plot (space jobs phase 1, docs/space-jobs-contract.md "VIEW"; the design page's plot):
// a billboard glyph per site the local player knows, by kind and knowledge — an unknown contact is a grey diamond with
// a "?" ("UNKNOWN · SIZE ~2"), wrecks and debris rust, asteroids and comets amber, anomalies and probes lavender rings,
// lifepods cyan dots, the station sky blue; a spent site is dimmed. Label right of the glyph: the site's title from
// its report (+ SCANNING n % / WORKING / HERE). A dashed sky ring round the flagship = the site-scan reach (only once
// any site is known), and a dashed line from each ship flying to a site to that site (redrawn every frame through one
// ImmediateMesh: no allocation). The glyph nodes are rebuilt only when the set of known sites or their knowledge
// changes; positions follow the sites every frame (comets and drifting wrecks move); labels update 4 times a second.
public partial class SystemPlot
{
    public static readonly Color SiteGrey = Color.FromHtml("#8d909a");
    public static readonly Color SiteRust = Color.FromHtml("#c8664a");
    public static readonly Color SiteAmber = Color.FromHtml("#f5c04a");
    public static readonly Color SiteLavender = Color.FromHtml("#a9a3e0");
    public static readonly Color SiteCyan = Color.FromHtml("#5ff2e6");
    public static readonly Color SiteSky = Color.FromHtml("#7fb0e8");

    /// <summary>Site-scan reach in map units when the sim has no number for it (the contract's placeholder).</summary>
    public const float DefaultSiteScanRange = 120f;

    private sealed class SiteNode
    {
        public Node3D Node = null!;
        public Sprite3D Glyph = null!;
        public Label3D Label = null!;
        public Label3D? Mark;
        public string Text = "";
        public SiteKnowledge Knowledge;
    }

    private enum GlyphKind { Unknown, Wreck, Debris, Rock, Anomaly, Pods, Station }

    private static readonly Dictionary<GlyphKind, ImageTexture> GlyphTextures = new();
    private readonly Dictionary<string, SiteNode> _siteNodes = new();
    private readonly List<SystemSiteMark> _siteMarks = new();
    private readonly List<string> _siteGone = new();
    private Node3D? _siteLayer;
    private MeshInstance3D? _scanRing;
    private MeshInstance3D? _siteCourseNode;
    private ImmediateMesh? _siteCourseMesh;
    private long _siteStamp = long.MinValue;
    private double _siteLabelsAt = -1;
    private string? _highlightSite;

    /// <summary>The sites on the plot as the player knows them (picking, the rail).</summary>
    public IReadOnlyList<SystemSiteMark> Sites => _siteMarks;

    /// <summary>Site-scan reach in map units (set by the driver from the sim when it has a number; else the placeholder).</summary>
    public float SiteScanRange { get; set; } = DefaultSiteScanRange;

    public static Color SiteColour(SiteKind kind, SiteKnowledge knowledge) => knowledge < SiteKnowledge.Identified ? SiteGrey : kind switch
    {
        SiteKind.Derelict or SiteKind.KnownDerelict or SiteKind.Debris or SiteKind.StrandedShip or SiteKind.HiddenShip => SiteRust,
        SiteKind.Asteroid or SiteKind.RogueAsteroid or SiteKind.Comet => SiteAmber,
        SiteKind.Anomaly or SiteKind.AlienProbe => SiteLavender,
        SiteKind.Lifepods => SiteCyan,
        SiteKind.Station => SiteSky,
        _ => SiteGrey,
    };

    private static GlyphKind GlyphOf(SiteKind kind, SiteKnowledge knowledge) => knowledge < SiteKnowledge.Identified ? GlyphKind.Unknown : kind switch
    {
        SiteKind.Derelict or SiteKind.KnownDerelict or SiteKind.StrandedShip or SiteKind.HiddenShip => GlyphKind.Wreck,
        SiteKind.Debris => GlyphKind.Debris,
        SiteKind.Asteroid or SiteKind.RogueAsteroid or SiteKind.Comet => GlyphKind.Rock,
        SiteKind.Anomaly or SiteKind.AlienProbe => GlyphKind.Anomaly,
        SiteKind.Lifepods => GlyphKind.Pods,
        SiteKind.Station => GlyphKind.Station,
        _ => GlyphKind.Unknown,
    };

    /// <summary>A short word for a site kind ("DERELICT", "DEBRIS", "ASTEROID"…).</summary>
    public static string KindWord(SiteKind kind) => kind switch
    {
        SiteKind.KnownDerelict => "DERELICT",
        SiteKind.RogueAsteroid => "ROGUE ASTEROID",
        SiteKind.StrandedShip => "STRANDED SHIP",
        SiteKind.HiddenShip => "CONTACT",
        SiteKind.AlienProbe => "PROBE",
        SiteKind.Lifepods => "LIFEPODS",
        _ => kind.ToString().ToUpperInvariant(),
    };

    private static Vector3 SitePlot(SpaceSite s) => new(s.Position.X.ToFloat() * MapScale, 0f, -s.Position.Y.ToFloat() * MapScale);

    /// <summary>Every frame from Refresh (after the ships moved): rebuild the glyphs if the known set changed, move them,
    /// the labels 4×/s, the scan ring round the flagship, the course lines to sites.</summary>
    private void UpdateSites(SystemSource source)
    {
        var sys = source.State;
        if (_siteLayer == null)
        {
            _siteLayer = new Node3D { Name = "Sites" };
            AddChild(_siteLayer);
        }
        _siteMarks.Clear();
        if (sys == null)
        {
            ClearSites();
            if (_scanRing != null) _scanRing.Visible = false;
            _siteCourseMesh?.ClearSurfaces();
            return;
        }

        // What we know, as a stamp: rebuild the glyph of a site whose knowledge changed.
        long stamp = 17;
        var sites = sys.Sites;
        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            var k = sys.SiteKnowledgeOf(source.Player, s.Id);
            unchecked { stamp = stamp * 31 + s.Id.GetHashCode() * 7 + (int)k; }
        }
        var rebuild = stamp != _siteStamp;
        _siteStamp = stamp;

        var now = Time.GetTicksMsec() / 1000.0;
        var labels = rebuild || now - _siteLabelsAt >= 0.25;
        if (labels) _siteLabelsAt = now;
        var flagship = source.Flagship;
        var here = source.SiteOf(flagship);
        var bound = source.SiteBoundOf(flagship);

        if (rebuild)
        {
            _siteGone.Clear();
            foreach (var id in _siteNodes.Keys) _siteGone.Add(id);
        }
        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            var k = sys.SiteKnowledgeOf(source.Player, s.Id);
            if (k == SiteKnowledge.Unknown) continue;
            if (rebuild) _siteGone.Remove(s.Id);
            if (!_siteNodes.TryGetValue(s.Id, out var node) || node.Knowledge != k)
            {
                if (node != null) node.Node.QueueFree();
                node = NewSiteNode(s, k);
                _siteNodes[s.Id] = node;
            }
            node.Node.Position = SitePlot(s) + Vector3.Up * 0.4f;
            if (labels)
            {
                var report = sys.SiteReportFor(source.Player, s.Id);
                var title = report?.Title is { Length: > 0 } t ? t : k < SiteKnowledge.Identified ? "Unknown contact" : s.Name;
                var text = k < SiteKnowledge.Identified ? $"UNKNOWN · SIZE ~{s.Size}" : $"{title.ToUpperInvariant()} · {KindWord(s.Kind)}";
                if (k == SiteKnowledge.Spent) text += " · SPENT";
                else if (sys.SiteScanShare(source.Player, s.Id) is { } share) text += $" · SCANNING {Mathf.RoundToInt(Mathf.Clamp(share.ToFloat(), 0f, 1f) * 100)} %";
                else if (sys.SiteWorkProgress(source.Player, s.Id) is { } work) text += $" · {work.Label.ToUpperInvariant()} {Mathf.RoundToInt(Mathf.Clamp(work.Share.ToFloat(), 0f, 1f) * 100)} %";
                else if (here == s.Id) text += " · HERE";
                else if (bound == s.Id) text += " · COURSE";
                if (text != node.Text)
                {
                    node.Text = text;
                    node.Label.Text = text;
                }
            }
            _siteMarks.Add(new SystemSiteMark(s.Id, k < SiteKnowledge.Identified ? "Unknown contact" : s.Name, s.Kind, k));
        }
        if (rebuild)
        {
            foreach (var id in _siteGone)
            {
                _siteNodes[id].Node.QueueFree();
                _siteNodes.Remove(id);
            }
            _siteGone.Clear();
        }

        UpdateScanRing(source, flagship);
        DrawSiteCourses(source);
    }

    private void ClearSites()
    {
        foreach (var n in _siteNodes.Values) n.Node.QueueFree();
        _siteNodes.Clear();
        _siteStamp = long.MinValue;
    }

    private SiteNode NewSiteNode(SpaceSite s, SiteKnowledge k)
    {
        var colour = SiteColour(s.Kind, k);
        var spent = k == SiteKnowledge.Spent;
        var tint = spent ? colour.Darkened(0.45f) with { A = 0.7f } : colour;
        var node = new Node3D { Name = $"Site_{s.Id}" };
        var scale = k < SiteKnowledge.Identified ? 0.75f + 0.1f * Mathf.Clamp(s.Size, 1, 5) : 1f;
        var glyph = new Sprite3D
        {
            Name = "Glyph",
            Texture = GlyphTexture(GlyphOf(s.Kind, k)),
            Modulate = tint,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            PixelSize = 0.085f * scale,
            NoDepthTest = true,
            RenderPriority = 19,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        node.AddChild(glyph);
        Label3D? mark = null;
        if (k < SiteKnowledge.Identified)
        {
            mark = Label("?", Vector3.Zero, tint, 22);
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.OutlineSize = 0;
            node.AddChild(mark);
        }
        var label = Label("", Vector3.Zero, tint, 16);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Offset = new Vector2(3.6f / label.PixelSize, 0f); // right of the glyph
        node.AddChild(label);
        _siteLayer!.AddChild(node);
        return new SiteNode { Node = node, Glyph = glyph, Label = label, Mark = mark, Knowledge = k };
    }

    private void UpdateScanRing(SystemSource source, SystemShip? flagship)
    {
        var show = flagship != null && _siteMarks.Count > 0 && _ships.TryGetValue(flagship.Id, out _);
        if (!show)
        {
            if (_scanRing != null) _scanRing.Visible = false;
            return;
        }
        if (_scanRing == null)
        {
            _scanRing = OrbitLines.Instance(OrbitLines.Circle(1f, 96, dashed: true), SiteSky with { A = 0.35f });
            _scanRing.Name = "SiteScanRing";
            _siteLayer!.AddChild(_scanRing);
        }
        _scanRing.Visible = true;
        var at = _ships[flagship!.Id].Node.Position;
        _scanRing.Position = new Vector3(at.X, 0.3f, at.Z);
        var r = Mathf.Max(1f, SiteScanRange * MapScale);
        _scanRing.Scale = new Vector3(r, 1f, r);
    }

    /// <summary>Dashed lines from every ship flying to a site to that site (ours peach, others dim).</summary>
    private void DrawSiteCourses(SystemSource source)
    {
        if (_siteCourseMesh == null)
        {
            _siteCourseMesh = new ImmediateMesh();
            _siteCourseNode = new MeshInstance3D
            {
                Name = "SiteCourses",
                Mesh = _siteCourseMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            _siteLayer!.AddChild(_siteCourseNode);
        }
        _siteCourseMesh.ClearSurfaces();
        var started = false;
        var ships = source.Ships;
        for (var i = 0; i < ships.Count; i++)
        {
            var s = ships[i];
            if (source.SiteBoundOf(s) is not { } siteId || source.State?.Site(siteId) is not { } site) continue;
            if (!_ships.TryGetValue(s.Id, out var entry)) continue;
            var a = entry.Node.Position with { Y = 0.6f };
            var b = SitePlot(site) + Vector3.Up * 0.6f;
            var length = a.DistanceTo(b);
            if (length < 0.5f) continue;
            if (!started)
            {
                _siteCourseMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                started = true;
            }
            var colour = s.Owner == source.Player ? Peach with { A = 0.9f } : Neutral with { A = 0.4f };
            var n = Mathf.Max(1, Mathf.FloorToInt(length / 2.4f));
            for (var d = 0; d < n; d += 2)
            {
                _siteCourseMesh.SurfaceSetColor(colour);
                _siteCourseMesh.SurfaceAddVertex(a.Lerp(b, d / (float)n));
                _siteCourseMesh.SurfaceSetColor(colour);
                _siteCourseMesh.SurfaceAddVertex(a.Lerp(b, Mathf.Min(1f, (d + 1) / (float)n)));
            }
        }
        if (started) _siteCourseMesh.SurfaceEnd();
    }

    /// <summary>Where a site parked ship sits: beside the site's glyph, a little apart from the others there.</summary>
    private bool SiteParking(SystemSource source, SystemShip s, Dictionary<string, int> parked, out Vector3 at, out Vector3 facing)
    {
        at = facing = Vector3.Zero;
        if (source.SiteOf(s) is not { } siteId || source.State?.Site(siteId) is not { } site) return false;
        var k = parked.GetValueOrDefault("site:" + siteId);
        parked["site:" + siteId] = k + 1;
        var angle = 2.6f + k * 0.8f;
        var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * 5.2f;
        at = SitePlot(site) + offset + Vector3.Up * 1.2f;
        facing = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
        return true;
    }

    /// <summary>Facing of a ship flying to a site: from where it is toward the site (Forward if unknown).</summary>
    private static Vector3 SiteHeading(SystemSource source, SystemShip s, Vector3 at)
    {
        if (source.SiteBoundOf(s) is not { } siteId || source.State?.Site(siteId) is not { } site) return Vector3.Forward;
        var d = SitePlot(site) - at;
        d.Y = 0f;
        return d.LengthSquared() < 1e-4f ? Vector3.Forward : d;
    }

    /// <summary>Ring one site in cyan; null clears it.</summary>
    public void HighlightSite(string? id)
    {
        _highlightSite = id;
        if (id != null)
        {
            _highlightBody = null;
            _highlightShip = null;
        }
        PlaceHighlight();
    }

    /// <summary>Where a site's glyph is (global); false when it isn't on the plot.</summary>
    public bool TryLocateSite(string id, out Vector3 global)
    {
        if (_siteNodes.TryGetValue(id, out var n) && n.Node.IsInsideTree())
        {
            global = n.Node.GlobalPosition;
            return true;
        }
        global = Vector3.Zero;
        return false;
    }

    // ---------------------------------------------------------------- glyph textures (painted once, 64², white)

    private static ImageTexture GlyphTexture(GlyphKind kind)
    {
        if (GlyphTextures.TryGetValue(kind, out var tex)) return tex;
        const int size = 64;
        const float k = 2.6f; // the design page's glyph units (±11) to pixels
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        Func<Vector2, float> alpha = kind switch
        {
            GlyphKind.Unknown => p => Stroke(Mathf.Abs(Mathf.Abs(p.X) + Mathf.Abs(p.Y) - 8.5f * k), 1.6f),
            GlyphKind.Wreck => p => Mathf.Max(PolyStroke(p, WreckHull, 1.9f), Stroke(SegmentDistance(p, new Vector2(-3, -6) * k, new Vector2(1, 4) * k), 1.1f)),
            GlyphKind.Debris => DebrisAlpha,
            GlyphKind.Rock => p => Inside(p, RockShape) ? 1f : Stroke(PolyDistance(p, RockShape), 1f),
            GlyphKind.Anomaly => p => Mathf.Max(Stroke(Mathf.Abs(p.Length() - 10f * k), 1.6f) * 0.45f,
                Mathf.Max(Stroke(Mathf.Abs(p.Length() - 6.5f * k), 1.6f) * 0.7f, Stroke(Mathf.Abs(p.Length() - 3f * k), 1.6f) * 0.95f)),
            GlyphKind.Pods => PodsAlpha,
            _ => p => Mathf.Max(Stroke(Mathf.Abs(p.Length() - 8.5f * k), 2.4f), Mathf.Max(Disc(p, Vector2.Zero, 2.4f * k),
                Mathf.Max(Stroke(SegmentDistance(p, new Vector2(-8.5f, 0) * k, new Vector2(8.5f, 0) * k), 1.2f), Stroke(SegmentDistance(p, new Vector2(0, -8.5f) * k, new Vector2(0, 8.5f) * k), 1.2f)))),
        };
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // 2×2 supersampled, centred, y down = the page's SVG y
                var a = 0f;
                for (var sy = 0; sy < 2; sy++)
                    for (var sx = 0; sx < 2; sx++)
                        a += alpha(new Vector2(x + 0.25f + sx * 0.5f - size / 2f, y + 0.25f + sy * 0.5f - size / 2f));
                img.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(a / 4f, 0f, 1f)));
            }
        img.GenerateMipmaps();
        tex = ImageTexture.CreateFromImage(img);
        GlyphTextures[kind] = tex;
        return tex;
    }

    private static readonly Vector2[] WreckHull = Scaled(new Vector2[] { new(-9, 4), new(-3, -6), new(9, -3), new(6, 5) });
    private static readonly Vector2[] RockShape = Scaled(new Vector2[] { new(-8, -2), new(-4, -8), new(4, -7), new(9, -1), new(6, 7), new(-3, 8), new(-9, 3) });

    private static Vector2[] Scaled(Vector2[] points)
    {
        for (var i = 0; i < points.Length; i++) points[i] *= 2.6f;
        return points;
    }

    private static float Stroke(float distance, float halfWidth) => Mathf.Clamp(halfWidth + 0.5f - distance, 0f, 1f);

    private static float Disc(Vector2 p, Vector2 c, float r) => Mathf.Clamp(r + 0.5f - p.DistanceTo(c), 0f, 1f);

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp((p - a).Dot(ab) / Mathf.Max(1e-4f, ab.LengthSquared()), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    private static float PolyDistance(Vector2 p, Vector2[] poly)
    {
        var d = float.MaxValue;
        for (var i = 0; i < poly.Length; i++) d = Mathf.Min(d, SegmentDistance(p, poly[i], poly[(i + 1) % poly.Length]));
        return d;
    }

    private static float PolyStroke(Vector2 p, Vector2[] poly, float halfWidth) => Stroke(PolyDistance(p, poly), halfWidth);

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    // the page's six small plates: (x, y, size), each turned by x · 9°
    private static readonly float[,] DebrisPieces = { { -8, -3, 2.2f }, { -2, 5, 1.6f }, { 5, -5, 2f }, { 8, 4, 1.3f }, { 0, -1, 1.2f }, { -6, 7, 1f } };
    private static readonly Vector2[] PodSpots = { new(-6, -4), new(4, -6), new(7, 3), new(-3, 6), new(0, 0) };

    private static float DebrisAlpha(Vector2 p)
    {
        var a = 0f;
        for (var i = 0; i < DebrisPieces.GetLength(0); i++)
        {
            var c = new Vector2(DebrisPieces[i, 0] + DebrisPieces[i, 2] * 0.8f, DebrisPieces[i, 1] + DebrisPieces[i, 2] * 0.5f) * 2.6f;
            var q = (p - c).Rotated(-Mathf.DegToRad(DebrisPieces[i, 0] * 9f));
            var hw = DebrisPieces[i, 2] * 0.8f * 2.6f;
            var hh = DebrisPieces[i, 2] * 0.5f * 2.6f;
            if (Mathf.Abs(q.X) <= hw && Mathf.Abs(q.Y) <= hh) a = 1f;
        }
        return a;
    }

    private static float PodsAlpha(Vector2 p)
    {
        var a = Stroke(Mathf.Abs(p.Length() - 11f * 2.6f), 1.2f) * 0.4f;
        foreach (var c in PodSpots) a = Mathf.Max(a, Disc(p, c * 2.6f, 2f * 2.6f));
        return a;
    }
}
