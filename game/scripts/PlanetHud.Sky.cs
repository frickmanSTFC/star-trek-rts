using Godot;

namespace FinalFrontier;

/// <summary>What the top bar's sky icon draws.</summary>
public enum SkyGlyph { Sun, Moon, Cloud, Rain, Storm, Snow, Fog, Dust, Wind }

// The top bar's local sky readout (PlanetDriver.Sky): "DAY 3 · SPRING · 14:20" over "RAIN · 12 °C", with a drawn icon,
// for the view's centre. Built on first use just left of the mission clock.
public partial class PlanetHud
{
    private HBoxContainer? _skyChip;
    private Label _skyMain = null!, _skySub = null!;
    private SkyIconView _skyIcon = null!;

    public void ShowSky(string main, string sub, string tooltip, SkyGlyph glyph, bool forced)
    {
        if (_skyChip == null)
        {
            var row = _clock.GetParent();
            _skyChip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            _skyChip.AddThemeConstantOverride("separation", 8);
            _skyIcon = new SkyIconView { CustomMinimumSize = new Vector2(28, 28), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            _skyChip.AddChild(_skyIcon);
            var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", -3);
            _skyMain = MakeLabel("", 15, Ink);
            _skySub = MakeLabel("", 11, Dim);
            text.AddChild(_skyMain);
            text.AddChild(_skySub);
            _skyChip.AddChild(text);
            row.AddChild(_skyChip);
            row.MoveChild(_skyChip, _clock.GetIndex()); // just before the clock
            row.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            row.MoveChild(row.GetChild(row.GetChildCount() - 1), _clock.GetIndex()); // a little gap
        }
        if (_skyMain.Text != main) _skyMain.Text = main;
        if (_skySub.Text != sub) _skySub.Text = sub;
        _skySub.AddThemeColorOverride("font_color", forced ? Accent : Dim);
        _skyChip.TooltipText = tooltip;
        _skyIcon.Glyph = glyph;
    }
}

/// <summary>A small hand-drawn weather icon (no texture needed).</summary>
public partial class SkyIconView : Control
{
    private SkyGlyph _glyph = (SkyGlyph)(-1);

    public SkyGlyph Glyph
    {
        get => _glyph;
        set
        {
            if (value == _glyph) return;
            _glyph = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var s = Size.X > 0 ? Size.X : 28f;
        var c = new Vector2(s / 2f, s / 2f);
        var sun = new Color(1f, 0.82f, 0.36f);
        var cloud = new Color(0.78f, 0.82f, 0.9f);
        var dark = new Color(0.5f, 0.55f, 0.66f);
        var rain = new Color(0.5f, 0.75f, 1f);
        switch (_glyph)
        {
            case SkyGlyph.Sun:
                DrawCircle(c, s * 0.2f, sun);
                for (var i = 0; i < 8; i++)
                {
                    var d = Vector2.Right.Rotated(i * Mathf.Pi / 4f);
                    DrawLine(c + d * s * 0.28f, c + d * s * 0.42f, sun, 2f, true);
                }
                break;
            case SkyGlyph.Moon:
                DrawCircle(c, s * 0.3f, new Color(0.85f, 0.88f, 1f));
                DrawCircle(c + new Vector2(s * 0.14f, -s * 0.1f), s * 0.27f, new Color(0.02f, 0.03f, 0.063f));
                break;
            case SkyGlyph.Fog:
                for (var i = 0; i < 4; i++)
                    DrawLine(new Vector2(s * (0.12f + 0.06f * (i % 2)), s * (0.3f + 0.14f * i)), new Vector2(s * (0.88f - 0.06f * (i % 2)), s * (0.3f + 0.14f * i)), cloud, 2f, true);
                break;
            case SkyGlyph.Wind:
            case SkyGlyph.Dust:
                var col = _glyph == SkyGlyph.Dust ? new Color(0.95f, 0.66f, 0.36f) : cloud;
                for (var i = 0; i < 3; i++)
                {
                    var y = s * (0.3f + 0.2f * i);
                    var points = new Vector2[9];
                    for (var k = 0; k < points.Length; k++)
                    {
                        var x = s * (0.12f + 0.09f * k - 0.05f * (i % 2));
                        points[k] = new Vector2(x, y + Mathf.Sin(k * 0.9f + i) * s * 0.04f);
                    }
                    DrawPolyline(points, col, 2f, true);
                }
                break;
            default:
                // A cloud (three puffs on a flat base), with rain, flakes or a bolt under it.
                var body = _glyph == SkyGlyph.Cloud ? cloud : dark.Lightened(0.25f);
                var top = c + new Vector2(0, -s * 0.08f);
                DrawCircle(top + new Vector2(-s * 0.16f, s * 0.04f), s * 0.15f, body);
                DrawCircle(top + new Vector2(s * 0.02f, -s * 0.04f), s * 0.2f, body);
                DrawCircle(top + new Vector2(s * 0.2f, s * 0.05f), s * 0.13f, body);
                DrawRect(new Rect2(top + new Vector2(-s * 0.3f, s * 0.04f), new Vector2(s * 0.62f, s * 0.14f)), body);
                var below = s * 0.72f;
                if (_glyph == SkyGlyph.Rain)
                    for (var i = 0; i < 3; i++)
                        DrawLine(new Vector2(s * (0.32f + 0.17f * i), below - s * 0.04f), new Vector2(s * (0.28f + 0.17f * i), below + s * 0.16f), rain, 2f, true);
                else if (_glyph == SkyGlyph.Snow)
                    for (var i = 0; i < 3; i++)
                        DrawCircle(new Vector2(s * (0.3f + 0.2f * i), below + s * 0.06f * (i % 2)), s * 0.05f, Colors.White);
                else if (_glyph == SkyGlyph.Storm)
                    DrawPolyline(new[] { new Vector2(s * 0.55f, below - s * 0.06f), new Vector2(s * 0.42f, below + s * 0.08f), new Vector2(s * 0.54f, below + s * 0.08f), new Vector2(s * 0.42f, below + s * 0.24f) }, sun, 2f, true);
                break;
        }
    }
}
