using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>One of the local player's vehicles on the ping (kept by the driver, drawn by <see cref="PingOverlay"/>).</summary>
public sealed class PingMark
{
    public int Id;

    /// <summary>The name on its hull ("TALUS 17").</summary>
    public string Name = "";

    /// <summary>Its type ("Mk II Surveyor").</summary>
    public string Kind = "";

    /// <summary>Where its model is, the ground under it and how tall it is (PlanetView.PingTargets).</summary>
    public Vector3 At;
    public float GroundY, Top;

    /// <summary>Disabled, broken down or lost: drawn in the warning colour.</summary>
    public bool Alert;

    /// <summary>On the map this frame (not riding in a drop ship).</summary>
    public bool Live;

    // The overlay's own caches: text widths at the font sizes last drawn with, and the distance text.
    internal float NameWidth, KindWidth, DistanceWidth;
    internal int NameSize = -1, KindSize = -1, DistanceSize = -1;
    internal string Distance = "";
    internal int Hexes = -1;
}

/// <summary>
/// The vehicle ping (owner 2026-11-17: "We need a way to find vehicles on the surface, there are too many rocks and things
/// to find them. Maybe a "Ping" to show where they are."), drawn in 2D over the 3D view and under the HUD, so trees,
/// rocks, buildings, fog and the zoom never hide it. For every vehicle on screen: rings spreading over the ground where
/// it stands (the real ground circle, projected), a tall pulsing beam with a head, and its name and type beside the
/// head (beams take different heights where names would overlap). For every vehicle off screen: an arrow at the rim of
/// the open view pointing to it, with its name and how far it is from the middle of the view. Broken-down and disabled
/// vehicles are orange. The driver hands over the marks every frame while the ping runs (PlanetDriver.Ping.cs) and
/// names one mark to keep showing while the mouse is on its row in the vehicle list.
/// Nothing here allocates per frame: lines, circles and cached strings only.
/// </summary>
public partial class PingOverlay : Control
{
    public static readonly Color PingColour = new(0.5f, 0.93f, 1f);
    public static readonly Color AlertColour = new(1f, 0.62f, 0.3f);
    private static readonly Color Shade = new(0f, 0.02f, 0.05f);

    private const int RingSegments = 28;
    private const float RingPeriod = 1.5f;      // seconds for one ring to spread
    private const float FadeIn = 0.12f, FadeOut = 0.9f;

    private Camera3D? _camera;
    private List<PingMark>? _marks;
    private float _age = -1f, _life;
    private int _solo;
    private Vector3 _focus;
    private float _bottomHeight = PlanetHud.BottomHeight;
    private float _time;
    private readonly List<Rect2> _taken = new();
    private Font _bold = null!, _plain = null!;

    // This frame's camera, for Project.
    private Transform3D _toCamera;
    private float _halfW, _halfH, _pixelsPerSlope;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bold = HudKit.Condensed(true);
        _plain = HudKit.Condensed();
        Visible = false;
    }

    /// <summary>Once a frame while there is something to show. <paramref name="age"/> runs 0 → <paramref name="life"/>
    /// while the ping is up (negative: not pinging); <paramref name="soloId"/> is a mark shown regardless (0 = none);
    /// <paramref name="focus"/> is the ground point at the middle of the view (distances are measured from it);
    /// <paramref name="bottomHeight"/> is the bottom panel's height now.</summary>
    public void Present(Camera3D camera, List<PingMark> marks, float age, float life, int soloId, Vector3 focus, float bottomHeight, float delta)
    {
        _camera = camera;
        _marks = marks;
        _age = age;
        _life = life;
        _solo = soloId;
        _focus = focus;
        _bottomHeight = bottomHeight;
        _time += delta;
        if (!Visible) Visible = true;
        QueueRedraw();
    }

    public void Clear()
    {
        if (!Visible) return;
        Visible = false;
        _marks = null;
    }

    /// <summary>The ping's opacity at this age (0 when it isn't running).</summary>
    private float Envelope() =>
        _age < 0f || _age >= _life ? 0f : Mathf.Min(1f, _age / FadeIn) * Mathf.Min(1f, (_life - _age) / FadeOut);

    public override void _Draw()
    {
        if (_camera == null || _marks == null || !_camera.IsInsideTree()) return;
        var screen = Size;
        if (screen.X < 16f || screen.Y < 16f) return;
        var s = HudKit.ScaleFor(screen);
        _toCamera = _camera.GlobalTransform.AffineInverse();
        _halfW = screen.X * 0.5f;
        _halfH = screen.Y * 0.5f;
        _pixelsPerSlope = _halfH / Mathf.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f); // vertical field of view (keep-height aspect)

        // The open view: between the top bar and the bottom panel.
        var top = PlanetHud.TopHeight + 4f;
        var bottom = screen.Y - _bottomHeight - 4f;
        if (bottom - top < 80f)
        {
            top = 0f;
            bottom = screen.Y;
        }
        var view = new Rect2(4f, top, screen.X - 8f, bottom - top);
        var envelope = Envelope();
        _taken.Clear();

        // On-screen vehicles first (their names take their places), then the arrows for the rest.
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < _marks.Count; i++)
            {
                var mark = _marks[i];
                if (!mark.Live) continue;
                var alpha = mark.Id == _solo ? 1f : envelope;
                if (alpha <= 0.01f) continue;
                var front = Project(mark.At, out var at);
                var inView = front && view.HasPoint(at);
                if (pass == 0 && inView) DrawBeacon(mark, at, alpha, s, view);
                else if (pass == 1 && !inView) DrawArrow(mark, front, at, alpha, s, view);
            }
    }

    /// <summary>A world point on the screen; false when it is behind the camera.</summary>
    private bool Project(Vector3 world, out Vector2 screen)
    {
        var c = _toCamera * world;
        var depth = -c.Z;
        if (depth < 0.05f)
        {
            screen = default;
            return false;
        }
        screen = new Vector2(_halfW + c.X / depth * _pixelsPerSlope, _halfH - c.Y / depth * _pixelsPerSlope);
        return true;
    }

    private static float Frac(float x) => x - Mathf.Floor(x);

    private void DrawBeacon(PingMark mark, Vector2 at, float alpha, float s, Rect2 view)
    {
        var colour = mark.Alert ? AlertColour : PingColour;
        var seed = mark.Id * 0.6180339f;
        var distance = _camera!.GlobalPosition.DistanceTo(mark.At);

        // Rings spreading over the ground: the real circle round the vehicle, projected; wider from farther away.
        var widest = Mathf.Clamp(distance * 0.075f, 9f, 140f);
        var ground = new Vector3(mark.At.X, mark.GroundY + 0.3f, mark.At.Z);
        for (var k = 0; k < 2; k++)
        {
            var p = Frac(_time / RingPeriod + k * 0.5f + seed);
            var radius = Mathf.Lerp(2.5f, widest, 1f - (1f - p) * (1f - p));
            var fade = (1f - p) * Mathf.Sqrt(1f - p);
            var ringColour = new Color(colour, alpha * fade * 0.95f);
            var under = new Color(Shade, alpha * fade * 0.45f);
            var width = Mathf.Lerp(3.2f, 1.6f, p) * s;
            var haveLast = Project(ground + new Vector3(0, 0, radius), out var last);
            for (var i = 1; i <= RingSegments; i++)
            {
                var a = Mathf.Tau * i / RingSegments;
                var have = Project(ground + new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius), out var next);
                if (have && haveLast)
                {
                    DrawLine(last, next, under, width + 2f * s);
                    DrawLine(last, next, ringColour, width, true);
                }
                last = next;
                haveLast = have;
            }
        }

        // The beam: straight up the screen from the vehicle's top. Names that would overlap take different heights.
        if (!Project(mark.At + new Vector3(0, mark.Top, 0), out var foot)) foot = at;
        var nameSize = Mathf.RoundToInt(15f * s);
        var kindSize = Mathf.RoundToInt(11f * s);
        Measure(mark, nameSize, kindSize, 0);
        var labelWidth = Mathf.Max(mark.NameWidth, mark.KindWidth) + 6f * s;
        var labelHeight = 32f * s;
        var length = 96f * s;
        var label = new Rect2();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            length = (attempt == 4 ? 64f : 96f + 30f * attempt) * s;
            var head = foot + new Vector2(0, -length);
            // Beside the head: to the right, or to the left when that would leave the view.
            var x = head.X + 13f * s + labelWidth > view.End.X ? head.X - 13f * s - labelWidth : head.X + 13f * s;
            label = new Rect2(x, head.Y - 12f * s, labelWidth, labelHeight);
            if (!Overlaps(label)) break;
        }
        _taken.Add(label);

        var tip = foot + new Vector2(0, -length);
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 7f + seed * 5f);
        DrawLine(foot, tip, new Color(Shade, alpha * 0.4f), 7f * s);
        DrawLine(foot, tip, new Color(colour, alpha * (0.16f + 0.1f * pulse)), 10f * s);
        DrawLine(foot, tip, new Color(colour, alpha * 0.55f), 4f * s);
        DrawLine(foot, tip, new Color(1f, 1f, 1f, alpha * 0.95f), 1.6f * s, true);
        // A bright bead running up the beam.
        var bead = Frac(_time * 1.25f + seed);
        DrawCircle(foot.Lerp(tip, bead), 3.2f * s, new Color(1f, 1f, 1f, alpha * (1f - bead)));
        // Where it stands, and the head.
        DrawCircle(at, 4.5f * s, new Color(Shade, alpha * 0.5f));
        DrawCircle(at, 3f * s, new Color(colour, alpha));
        DrawCircle(tip, (7.5f + 1.5f * pulse) * s, new Color(Shade, alpha * 0.55f));
        DrawCircle(tip, (5f + 1.5f * pulse) * s, new Color(colour, alpha));
        DrawArc(tip, (10f + 3f * pulse) * s, 0f, Mathf.Tau, 24, new Color(colour, alpha * (0.85f - 0.45f * pulse)), 1.6f * s, true);

        var textAt = new Vector2(label.Position.X + 3f * s, label.Position.Y + 14f * s);
        Text(_bold, textAt, mark.Name, nameSize, new Color(1f, 1f, 1f, alpha), alpha, s);
        Text(_plain, textAt + new Vector2(0, 14f * s), mark.Kind, kindSize, new Color(colour, alpha), alpha, s);
    }

    private void DrawArrow(PingMark mark, bool front, Vector2 at, float alpha, float s, Rect2 view)
    {
        var colour = mark.Alert ? AlertColour : PingColour;
        var centre = view.GetCenter();
        Vector2 dir;
        if (front) dir = at - centre;
        else
        {
            // Behind the camera: by where it lies in the camera's own axes (to the side, and down the screen).
            var c = _toCamera * mark.At;
            dir = new Vector2(c.X, -c.Y);
        }
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector2.Down;

        // Where the line from the middle of the view toward it meets the view's rim (a little inside).
        var inset = 24f * s;
        var half = new Vector2(Mathf.Max(view.Size.X * 0.5f - inset, 10f), Mathf.Max(view.Size.Y * 0.5f - inset, 10f));
        var tx = Mathf.Abs(dir.X) > 1e-4f ? half.X / Mathf.Abs(dir.X) : float.MaxValue;
        var ty = Mathf.Abs(dir.Y) > 1e-4f ? half.Y / Mathf.Abs(dir.Y) : float.MaxValue;
        var sideways = tx < ty; // on the left or right rim
        var rim = centre + dir * Mathf.Min(tx, ty);

        // How far: from the middle of the view, in hexes.
        var hexes = Mathf.RoundToInt(new Vector2(mark.At.X - _focus.X, mark.At.Z - _focus.Z).Length() / PlanetView.HexWidth);
        if (hexes != mark.Hexes)
        {
            mark.Hexes = hexes;
            mark.Distance = hexes == 1 ? "1 hex" : hexes + " hexes";
            mark.DistanceSize = -1;
        }
        var nameSize = Mathf.RoundToInt(14f * s);
        var distanceSize = Mathf.RoundToInt(11f * s);
        Measure(mark, nameSize, -1, distanceSize);
        var width = Mathf.Max(mark.NameWidth, mark.DistanceWidth) + 6f * s;
        var height = 30f * s;

        // Arrows that point the same way step along the rim until their names are clear of each other.
        var pos = rim;
        var label = new Rect2();
        for (var attempt = 0; attempt < 9; attempt++)
        {
            var step = (attempt + 1) / 2 * ((attempt & 1) == 1 ? 1f : -1f);
            pos = sideways
                ? new Vector2(rim.X, Mathf.Clamp(rim.Y + step * (height + 4f * s), centre.Y - half.Y, centre.Y + half.Y))
                : new Vector2(Mathf.Clamp(rim.X + step * (width + 26f * s), centre.X - half.X, centre.X + half.X), rim.Y);
            float x, y;
            if (sideways)
            {
                x = dir.X > 0f ? pos.X - 16f * s - width : pos.X + 16f * s;
                y = pos.Y - height * 0.5f;
            }
            else
            {
                x = Mathf.Clamp(pos.X - width * 0.5f, view.Position.X + 2f, view.End.X - width - 2f);
                y = dir.Y > 0f ? pos.Y - 14f * s - height : pos.Y + 14f * s;
            }
            label = new Rect2(x, y, width, height);
            if (!Overlaps(label)) break;
        }
        _taken.Add(label);

        // The arrow: a chevron pointing out of the view, with a ring pulsing behind it.
        var perp = new Vector2(-dir.Y, dir.X);
        var beat = Frac(_time * 1.3f + mark.Id * 0.37f);
        var point = pos + dir * (9f + 3f * (1f - beat)) * s;
        var wingA = pos - dir * 5f * s + perp * 9f * s;
        var wingB = pos - dir * 5f * s - perp * 9f * s;
        DrawArc(pos, (7f + 12f * beat) * s, 0f, Mathf.Tau, 20, new Color(colour, alpha * (1f - beat) * 0.8f), 1.6f * s, true);
        DrawLine(wingA, point, new Color(Shade, alpha * 0.6f), 7f * s);
        DrawLine(wingB, point, new Color(Shade, alpha * 0.6f), 7f * s);
        DrawLine(wingA, point, new Color(colour, alpha), 3.4f * s, true);
        DrawLine(wingB, point, new Color(colour, alpha), 3.4f * s, true);

        var nameAt = new Vector2(label.Position.X + 3f * s, label.Position.Y + 13f * s);
        var distanceAt = nameAt + new Vector2(0, 13f * s);
        if (sideways && dir.X > 0f)
        {
            // On the right rim the text ends at the arrow.
            nameAt.X = label.End.X - 3f * s - mark.NameWidth;
            distanceAt.X = label.End.X - 3f * s - mark.DistanceWidth;
        }
        Text(_bold, nameAt, mark.Name, nameSize, new Color(1f, 1f, 1f, alpha), alpha, s);
        Text(_plain, distanceAt, mark.Distance, distanceSize, new Color(colour, alpha), alpha, s);
    }

    private bool Overlaps(Rect2 rect)
    {
        for (var i = 0; i < _taken.Count; i++)
            if (_taken[i].Intersects(rect)) return true;
        return false;
    }

    /// <summary>Text with a dark outline, so it reads on snow, sand and at night.</summary>
    private void Text(Font font, Vector2 at, string text, int size, Color colour, float alpha, float s)
    {
        if (text.Length == 0) return;
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, Mathf.Max(3, Mathf.RoundToInt(4f * s)), new Color(Shade, alpha * 0.9f));
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, colour);
    }

    /// <summary>Text widths, measured again only when the size (or the distance text) changed. A size of -1 is skipped.</summary>
    private void Measure(PingMark mark, int nameSize, int kindSize, int distanceSize)
    {
        if (nameSize > 0 && mark.NameSize != nameSize)
        {
            mark.NameSize = nameSize;
            mark.NameWidth = _bold.GetStringSize(mark.Name, HorizontalAlignment.Left, -1, nameSize).X;
        }
        if (kindSize > 0 && mark.KindSize != kindSize)
        {
            mark.KindSize = kindSize;
            mark.KindWidth = _plain.GetStringSize(mark.Kind, HorizontalAlignment.Left, -1, kindSize).X;
        }
        if (distanceSize > 0 && mark.DistanceSize != distanceSize)
        {
            mark.DistanceSize = distanceSize;
            mark.DistanceWidth = _plain.GetStringSize(mark.Distance, HorizontalAlignment.Left, -1, distanceSize).X;
        }
    }
}
