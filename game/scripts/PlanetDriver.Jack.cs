using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The Jack Skellington raid, HUD side (docs/jack-contract.md; sim Surface/Jack.cs): switches the raid on for this game,
/// turns the sim's JackEvents into event-feed lines, sounds a red-alert klaxon and flashes a big INCOMING ATTACK banner
/// with the countdown, then a smaller "attacking" line with the time left; flies the camera to him when he rises.
/// </summary>
public partial class PlanetDriver
{
    private bool _jackOn;
    private int _jackEventsSeen;
    private CanvasLayer? _jackLayer;
    private Label _jackTitle = null!, _jackSub = null!;
    private ColorRect _jackEdge = null!;

    private void UpdateJackUi(double delta)
    {
        if (_sim.Surface is not { } s) return;
        if (!_jackOn)
        {
            _jackOn = true;
            s.JackEnabled = true; // a joke for Craig: every game (owner 2026-11-11)
        }
        JackFeed(s);
        JackBanner(s);
    }

    private void JackFeed(PlanetSurface s)
    {
        var events = s.JackEvents;
        if (_jackEventsSeen > events.Count) _jackEventsSeen = 0;
        for (var i = _jackEventsSeen; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Player != LocalPlayer) continue;
            var colour = e.Kind switch
            {
                "warning" => HelmScreen.Red,
                "arrived" => new Color(0.5f, 1f, 0.45f),
                "pumpkin" => new Color(1f, 0.6f, 0.15f),
                "bats" => new Color(0.75f, 0.55f, 1f),
                "shriek" => new Color(0.92f, 0.92f, 0.86f),
                _ => HelmScreen.Lavender,
            };
            _hud.PushEvent(e.Text, colour);
            if (e.Kind == "warning")
            {
                Sfx.Ui("jack_alarm", 2f);
                Sfx.UiLater("jack_alarm", 2.7f, 1f);
                Sfx.UiLater("jack_alarm", 5.4f, 0f);
            }
            if (e.Kind == "arrived" && s.JackOf(LocalPlayer) is { Tile: >= 0 } j && !_inOrbit) FlyTo(j.Tile, 240f);
        }
        _jackEventsSeen = events.Count;
    }

    private void JackBanner(PlanetSurface s)
    {
        var j = s.JackOf(LocalPlayer);
        var phase = j?.PhaseAt(_sim.Tick) ?? JackPhase.Gone;
        if (phase == JackPhase.Gone)
        {
            if (_jackLayer != null) _jackLayer.Visible = false;
            return;
        }
        EnsureJackLayer();
        _jackLayer!.Visible = true;
        var size = GetViewport().GetVisibleRect().Size;
        var scale = Mathf.Clamp(size.Y / 720f, 0.8f, 2f);
        var pulse = 0.5f + 0.5f * Mathf.Sin((float)_clock * 7f);
        switch (phase)
        {
            case JackPhase.Warning:
            {
                var secs = j!.SecondsToArrival(_sim.Tick);
                _jackTitle.Text = "⚠  INCOMING ATTACK  ⚠";
                _jackSub.Text = $"UNKNOWN LIFEFORM: JACK SKELLINGTON  ·  ARRIVAL IN 0:{secs:00}";
                _jackTitle.Modulate = new Color(1f, 0.2f + 0.25f * pulse, 0.15f);
                _jackTitle.AddThemeFontSizeOverride("font_size", (int)(40 * scale));
                _jackEdge.Visible = true;
                _jackEdge.Color = new Color(1f, 0f, 0f, 0.05f + 0.12f * pulse);
                break;
            }
            case JackPhase.Rising:
            case JackPhase.Rampage:
            {
                var left = (int)System.Math.Max(0, (j!.LeaveTick - _sim.Tick) / Simulation.TickRate);
                _jackTitle.Text = "JACK SKELLINGTON IS ATTACKING THE COLONY!";
                _jackSub.Text = $"Hold on! He'll get bored in {left / 60}:{left % 60:00}";
                _jackTitle.Modulate = new Color(1f, 0.55f + 0.15f * pulse, 0.1f);
                _jackTitle.AddThemeFontSizeOverride("font_size", (int)(26 * scale));
                _jackEdge.Visible = true;
                _jackEdge.Color = new Color(0.2f, 1f, 0.3f, 0.03f + 0.03f * pulse);
                break;
            }
            default:
                _jackTitle.Text = "HAPPY HALLOWEEN!";
                _jackSub.Text = "Jack Skellington takes a bow and vanishes into the mist";
                _jackTitle.Modulate = new Color(1f, 0.6f, 0.1f);
                _jackTitle.AddThemeFontSizeOverride("font_size", (int)(36 * scale));
                _jackEdge.Visible = false;
                break;
        }
        _jackSub.AddThemeFontSizeOverride("font_size", (int)(17 * scale));
        var box = (Control)_jackTitle.GetParent();
        box.Position = new Vector2(0, size.Y * 0.17f);
        box.Size = new Vector2(size.X, 0);
    }

    private void EnsureJackLayer()
    {
        if (_jackLayer != null) return;
        _jackLayer = new CanvasLayer { Name = "JackAlert", Layer = 3 };
        AddChild(_jackLayer);
        _jackEdge = new ColorRect { Name = "JackEdge", MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(1, 0, 0, 0) };
        _jackEdge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _jackEdge.Material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = "shader_type canvas_item;\n" +
                       "void fragment() {\n" +
                       "  vec2 d = abs(UV - 0.5) * 2.0;\n" +
                       "  float edge = smoothstep(0.55, 1.0, max(d.x, d.y));\n" +
                       "  COLOR = vec4(COLOR.rgb, COLOR.a * edge * 3.0);\n" +
                       "}\n",
            },
        };
        _jackLayer.AddChild(_jackEdge);
        var box = new VBoxContainer { Name = "JackBox", MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        _jackLayer.AddChild(box);
        _jackTitle = JackLabel(40);
        _jackSub = JackLabel(17);
        box.AddChild(_jackTitle);
        box.AddChild(_jackSub);
    }

    private static Label JackLabel(int size)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeConstantOverride("outline_size", 9);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        return l;
    }
}
