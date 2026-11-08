using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The achievement art shared by the toast, the screen and the trophy chip (docs/mockups/achievements): the hexagon
/// medal in the tier's metal with the achievement's line icon, drawn from the mockup's own SVG (gradient, inner hex,
/// 24×24 line icon, gold highlight) and rasterised once per look and size, like <see cref="Icons"/>.
/// </summary>
public static class Medals
{
    /// <summary>The mockup's line icons (24×24, stroked), by the data's icon names; "lock"/"question" for locked/hidden.</summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["uplink"] = "<path d='M12 21V11'/><path d='M8 7a5.6 5.6 0 0 1 8 0M5.2 4.4a9.6 9.6 0 0 1 13.6 0'/><circle cx='12' cy='10' r='1.4'/><path d='M8 21h8'/>",
        ["ingot"] = "<path d='M4 17l3-7h10l3 7z'/><path d='M7 10l2-3h6l2 3'/>",
        ["alloy"] = "<path d='M4 17l3-7h10l3 7z'/><path d='M9 13h6'/><path d='M12 4v3'/>",
        ["crown"] = "<path d='M4 18h16l-1.5-9-4 4L12 6l-2.5 7-4-4z'/>",
        ["capitol"] = "<path d='M4 20h16M6 20v-7M10 20v-7M14 20v-7M18 20v-7M4 13h16L12 6z'/>",
        ["bolt"] = "<path d='M13 3L6 13h5l-1 8 7-10h-5z'/>",
        ["chart"] = "<circle cx='12' cy='12' r='8'/><path d='M12 4v16M4 12h16'/><circle cx='12' cy='12' r='3'/>",
        ["satellite"] = "<rect x='9' y='9' width='6' height='6' rx='1'/><path d='M3 6l4 4M6 3l4 4M14 17l4 4M17 14l4 4M12 9V6M12 18v-3'/>",
        ["route"] = "<circle cx='6' cy='18' r='2'/><circle cx='18' cy='6' r='2'/><path d='M8 18c6 0 2-12 8-12' stroke-dasharray='2 2.5'/>",
        ["planet"] = "<circle cx='12' cy='12' r='5'/><ellipse cx='12' cy='12' rx='10' ry='3.4' transform='rotate(-18 12 12)'/>",
        ["hail"] = "<path d='M4 10a8 8 0 0 1 16 0'/><path d='M7.5 12a4.5 4.5 0 0 1 9 0'/><circle cx='12' cy='14' r='1.5'/><path d='M12 15.5V21'/>",
        ["hex"] = "<path d='M12 3l7.8 4.5v9L12 21l-7.8-4.5v-9z'/><path d='M12 8l3.5 2v4L12 16l-3.5-2v-4z'/>",
        ["gem"] = "<path d='M6 4h12l3 5-9 11L3 9z'/><path d='M3 9h18M9 4l3 16 3-16'/>",
        ["base"] = "<path d='M3 19h18'/><path d='M5 19v-5a7 7 0 0 1 14 0v5'/><path d='M10 19v-3h4v3'/>",
        ["drill"] = "<path d='M12 3v11'/><path d='M9 14h6l-3 7z'/><path d='M7 7h10'/>",
        ["rover"] = "<rect x='3' y='9' width='15' height='6' rx='1.5'/><path d='M18 11h2l1 2v2h-3'/><circle cx='7' cy='17' r='2'/><circle cx='15' cy='17' r='2'/>",
        ["tyre"] = "<circle cx='12' cy='12' r='8'/><circle cx='12' cy='12' r='3'/><path d='M12 4v5M12 15v5M4 12h5M15 12h5'/>",
        ["wrench"] = "<path d='M14.5 5.5a4 4 0 0 0 4.8 4.8L11 18.6a2 2 0 0 1-2.8-2.8l8.3-8.3'/>",
        ["fleet"] = "<rect x='2' y='12' width='9' height='4' rx='1'/><rect x='13' y='12' width='9' height='4' rx='1'/><rect x='7.5' y='6' width='9' height='4' rx='1'/>",
        ["blast"] = "<path d='M12 2l2 6 5-3-2 6 5 1-5 3 3 5-6-2-2 6-2-6-6 2 3-5-5-3 5-1-2-6 5 3z'/>",
        ["dome"] = "<path d='M3 19h18'/><path d='M5 19a7 7 0 0 1 14 0'/><path d='M12 12v7M8.5 13.2L10 19M15.5 13.2L14 19'/>",
        ["lungs"] = "<path d='M12 4v8'/><path d='M12 10c-2 0-3-2-5-2-2.5 0-3 4-3 8 0 2 1 3 3 3 3 0 5-3 5-7M12 10c2 0 3-2 5-2 2.5 0 3 4 3 8 0 2-1 3-3 3-3 0-5-3-5-7'/>",
        ["weight"] = "<path d='M7 9h10l3 11H4z'/><circle cx='12' cy='6' r='2.5'/>",
        ["storm"] = "<path d='M7 15a4 4 0 1 1 1-7.9A5 5 0 0 1 17.6 8 3.5 3.5 0 0 1 17 15z'/><path d='M12 15l-2 4h3l-2 3'/>",
        ["signal"] = "<path d='M4 18h2v-3H4zM9 18h2v-6H9zM14 18h2V9h-2zM19 18h2V5h-2z'/>",
        ["sos"] = "<circle cx='12' cy='12' r='8'/><path d='M12 7v6M12 16v.5'/>",
        ["bay"] = "<path d='M3 20V9l9-5 9 5v11'/><path d='M7 20v-7h10v7M7 16h10'/>",
        ["box"] = "<path d='M4 8l8-4 8 4v8l-8 4-8-4z'/><path d='M4 8l8 4 8-4M12 12v8'/><path d='M3 3l18 18'/>",
        ["flask"] = "<path d='M9 3h6M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4A2 2 0 0 0 19 18l-5-9V3'/><path d='M7.5 15h9'/>",
        ["anchor"] = "<circle cx='12' cy='5' r='2'/><path d='M12 7v14M5 13a7 7 0 0 0 14 0M8 11h8'/>",
        ["lock"] = "<rect x='6' y='11' width='12' height='9' rx='1.5'/><path d='M9 11V8a3 3 0 0 1 6 0v3'/>",
        ["question"] = "<path d='M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .9-1 1.7v.5'/><circle cx='12' cy='17' r='.8'/>",
        ["trophy"] = "<path d='M7 4h10v5a5 5 0 0 1-10 0z'/><path d='M7 6H4v1a3 3 0 0 0 3 3M17 6h3v1a3 3 0 0 1-3 3M12 14v3M8 20h8M10 17h4'/>",
    };

    public static readonly Color Bronze = Color.FromHtml("#cf8a52");
    public static readonly Color Silver = Color.FromHtml("#c3cedd");
    public static readonly Color Gold = Color.FromHtml("#f5a624");

    /// <summary>A tier's colour: the data's ("#cf8a52") when it parses, else the mockup's by id.</summary>
    public static Color TierColour(AchievementData? data, string tier)
    {
        if (data?.Tier(tier) is { } t && AchievementProfile.ParseColour(t.Colour) is { } c) return c;
        return tier switch { "bronze" => Bronze, "silver" => Silver, _ => Gold };
    }

    public static int Points(AchievementData? data, string tier) =>
        data?.Tier(tier)?.Points ?? tier switch { "bronze" => 10, "silver" => 25, _ => 50 };

    public static string TierName(AchievementData? data, string tier) => (data?.Tier(tier)?.Name ?? tier).ToUpperInvariant();

    /// <summary>The metal's three stops (highlight, body, shadow): the mockup's for the three tiers, else from the colour.</summary>
    private static (Color Hi, Color Mid, Color Lo) Metal(string tier, Color colour) => tier switch
    {
        "bronze" => (Color.FromHtml("#f0b27a"), Color.FromHtml("#cf8a52"), Color.FromHtml("#7a4524")),
        "silver" => (Color.FromHtml("#eef3fa"), Color.FromHtml("#c3cedd"), Color.FromHtml("#5d6a7e")),
        "gold" => (Color.FromHtml("#ffd27a"), Color.FromHtml("#f5a624"), Color.FromHtml("#8a5208")),
        _ => (colour.Lightened(0.4f), colour, colour.Darkened(0.55f)),
    };

    private static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>The medal as a texture about <paramref name="pixels"/> wide (rounded up to 16 px steps, cached).
    /// Locked: dark slate with a dim icon; hidden (locked and secret): a question mark.</summary>
    public static Texture2D Medal(string tier, Color colour, string icon, bool locked, bool hidden, float pixels)
    {
        var size = Mathf.Clamp(Mathf.CeilToInt(pixels / 16f) * 16, 16, 512);
        var iconName = hidden ? "question" : Paths.ContainsKey(icon) ? icon : "hex";
        var key = $"{tier}|{colour.ToHtml(false)}|{iconName}|{locked}|{size}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var (hi, mid, lo) = locked ? (Color.FromHtml("#3a4560"), Color.FromHtml("#2a3248"), Color.FromHtml("#161c2b")) : Metal(tier, colour);
        var innerFill = locked ? "#121828" : "#" + mid.ToHtml(false);
        var innerStroke = locked ? "#2a3248" : "#" + hi.ToHtml(false);
        var iconColour = locked ? "#5d6a85" : "#0b1220";
        var shine = tier == "gold" && !locked ? "<path d='M32 3l25 14.5' stroke='#ffffff' stroke-opacity='0.4' stroke-width='1.5' fill='none'/>" : "";
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>" +
                  "<defs><linearGradient id='m' x1='0' y1='0' x2='1' y2='1'>" +
                  $"<stop offset='0' stop-color='#{hi.ToHtml(false)}'/><stop offset='0.55' stop-color='#{mid.ToHtml(false)}'/><stop offset='1' stop-color='#{lo.ToHtml(false)}'/>" +
                  "</linearGradient></defs>" +
                  "<path d='M32 3l25 14.5v29L32 61 7 46.5v-29z' fill='url(#m)'/>" +
                  $"<path d='M32 9.5l19.4 11.2v22.6L32 54.5 12.6 43.3V20.7z' fill='{innerFill}' fill-opacity='{(locked ? "1" : "0.95")}' stroke='{innerStroke}' stroke-width='1.2'/>" +
                  $"<g transform='translate(18 18) scale(1.17)' fill='none' stroke='{iconColour}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{Paths[iconName]}</g>" +
                  shine + "</svg>";
        var image = new Image();
        var err = image.LoadSvgFromString(svg, size / 64f);
        if (err != Error.Ok) GD.PushWarning($"Achievements: medal svg failed ({err}) for {key}");
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    /// <summary>The medal of an achievement as seen now (earned or not; secret while hidden and not earned).</summary>
    public static Texture2D Of(AchievementData? data, AchievementDef a, bool earned, float pixels) =>
        Medal(a.Tier, TierColour(data, a.Tier), a.Icon, !earned, a.Hidden && !earned, pixels);

    /// <summary>A plain line icon (the trophy for the chip), stroked in a colour.</summary>
    public static Texture2D Line(string icon, Color colour, float pixels)
    {
        var size = Mathf.Clamp(Mathf.CeilToInt(pixels / 8f) * 8, 8, 256);
        var key = $"line|{icon}|{colour.ToHtml()}|{size}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' " +
                  $"stroke='#{colour.ToHtml(false)}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{Paths.GetValueOrDefault(icon, Paths["hex"])}</svg>";
        var image = new Image();
        image.LoadSvgFromString(svg, size / 24f);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    private static SystemFont? _mono;

    /// <summary>A monospaced face for points, dates and progress (JetBrains Mono as in the mockup, else the system's).</summary>
    public static Font Mono() => _mono ??= new SystemFont
    {
        FontNames = new[] { "JetBrains Mono", "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "monospace" },
        FontWeight = 500,
    };

    /// <summary>Colour a mixed toward another (CSS color-mix in srgb: share of a).</summary>
    public static Color Mix(Color a, Color b, float shareOfA) => b.Lerp(a, shareOfA);
}

/// <summary>What one toast shows.</summary>
public sealed record ToastView(string Id, string Name, string Description, string Tier, Color Colour, string Icon, bool Hidden, int Points, string TierName);

/// <summary>
/// The unlock badge (docs/mockups/achievements, part 1): own CanvasLayer above the HUD. Top centre under the top bar
/// (in orbit under the helm title: the driver sets <see cref="TopY"/> / <see cref="CentreX"/> each frame). Each toast:
/// medal (pops in with a short spin), kicker, name, description, TIER · points top right, a timer bar draining over 4.5 s
/// (gold 6 s), a light sweep on entry. Stacks up to 3 (newest at the bottom), the rest wait their turn. Hover holds it;
/// a click opens the screen (<see cref="Clicked"/>). While <see cref="Waiting"/> (F1 help, a large card, the world
/// chooser) nothing shows and the clocks stop.
/// </summary>
public partial class AchievementToasts : Control
{
    public const int MaxShown = 3;

    public event Action<string>? Clicked;

    /// <summary>Hold everything (hidden, timers stopped) while another screen is up.</summary>
    public bool Waiting { get; set; }

    /// <summary>Top of the stack in viewport pixels.</summary>
    public float TopY { get; set; } = PlanetHud.TopHeight + 16;

    /// <summary>Centre of the stack in viewport pixels; below 0 = the screen's centre.</summary>
    public float CentreX { get; set; } = -1;

    private readonly Queue<ToastView> _queue = new();
    private readonly List<AchievementToast> _shown = new();

    public int Pending => _queue.Count + _shown.Count;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Push(ToastView v) => _queue.Enqueue(v);

    /// <summary>True over a shown toast.</summary>
    public bool IsOverUi(Vector2 mouse)
    {
        if (Waiting || !Visible) return false;
        foreach (var t in _shown)
            if (t.Visible && t.GetGlobalRect().HasPoint(mouse)) return true;
        return false;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        foreach (var t in _shown) t.Visible = !Waiting;
        if (Waiting) return;
        while (_queue.Count > 0 && _shown.Count < MaxShown)
        {
            var t = new AchievementToast();
            t.Init(_queue.Dequeue());
            t.Clicked += id => Clicked?.Invoke(id);
            AddChild(t);
            _shown.Add(t);
        }
        var screen = GetViewportRect().Size;
        var s = HudKit.ScaleFor(screen);
        var mouse = GetViewport().GetMousePosition();
        var centre = CentreX >= 0 ? CentreX : screen.X / 2f;
        var y = TopY;
        for (var i = 0; i < _shown.Count; i++)
        {
            var t = _shown[i];
            t.Layout(s, screen);
            t.Fit();
            var hover = t.GetGlobalRect().HasPoint(mouse);
            t.Advance(dt, hover);
            var target = new Vector2(centre - t.Size.X / 2f, y);
            t.Slot = t.Slot == null ? target : t.Slot.Value.Lerp(target, Mathf.Min(1f, dt * 12f));
            t.Place();
            y += t.Size.Y + 10 * s;
        }
        for (var i = _shown.Count - 1; i >= 0; i--)
            if (_shown[i].Gone)
            {
                _shown[i].QueueFree();
                _shown.RemoveAt(i);
            }
    }
}

/// <summary>One badge. Draws its own rounded panel (tier tint fading out by 46% of the width, shadow and tier glow,
/// border, light sweep, timer bar) in vertical slices so the tint, sweep and bar stay inside the rounded corners.</summary>
public partial class AchievementToast : Control
{
    public event Action<string>? Clicked;
    public Vector2? Slot;
    public bool Gone => _leave >= Leave;

    private ToastView _v = null!;
    private float _life;
    private float _left, _age, _leave = -1f;
    private const float Enter = 0.42f, Leave = 0.35f;
    private float _s = -1f;
    private Vector2 _screen;
    private TextureRect _medal = null!;
    private Label _kicker = null!, _name = null!, _desc = null!, _meta = null!;
    private VBoxContainer _text = null!;
    private StyleBoxFlat _shadow = null!, _glow = null!;

    /// <summary>Builds the badge for one unlock (call once, before it enters the tree).</summary>
    public void Init(ToastView v)
    {
        _v = v;
        _life = _left = v.Tier == "gold" ? 6f : 4.5f;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Open the achievements (F3)";
        _medal = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_medal);
        _text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_text);
        _kicker = HudKit.Label(v.Hidden ? "HIDDEN ACHIEVEMENT UNLOCKED" : "ACHIEVEMENT UNLOCKED", 12, v.Colour, condensed: true, bold: true);
        _name = HudKit.Label(v.Name, 26, HudKit.Ink, condensed: true, bold: true);
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _desc = HudKit.Label(v.Description, 14, HudKit.Dim);
        _desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.AddChild(_kicker);
        _text.AddChild(_name);
        _text.AddChild(_desc);
        _meta = HudKit.Label($"{v.TierName} · {v.Points}", 11, v.Colour);
        _meta.AddThemeFontOverride("font", Medals.Mono());
        AddChild(_meta);
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _leave < 0)
            {
                Clicked?.Invoke(_v.Id);
                _left = 0; // it has done its job
                AcceptEvent();
            }
        };
    }

    /// <summary>Sizes and fonts for the screen (rebuilt only when the scale changes).</summary>
    public void Layout(float s, Vector2 screen)
    {
        if (Mathf.IsEqualApprox(s, _s) && screen == _screen) return;
        _s = s;
        _screen = screen;
        var width = Mathf.Min(screen.X - 32f, 540f * s);
        var padL = 14f * s;
        var padR = 22f * s;
        var medal = 74f * s;
        _medal.Texture = Medals.Medal(_v.Tier, _v.Colour, _v.Icon, false, false, medal * 1.5f);
        _medal.Size = new Vector2(medal, medal);
        _medal.PivotOffset = _medal.Size / 2f;
        _kicker.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * s));
        _name.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(26 * s));
        _desc.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
        _meta.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11 * s));
        _text.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        var textX = padL + medal + 18f * s;
        var textW = width - textX - padR;
        _desc.CustomMinimumSize = new Vector2(textW, 0);
        _name.CustomMinimumSize = new Vector2(textW - 70f * s, 0); // clear of TIER · points
        _width = width;
        _textX = textX;
        _medalSize = medal;
        Fit();
        var r = 11f * s;
        _shadow = new StyleBoxFlat { BgColor = new Color(0.027f, 0.043f, 0.086f, 0.96f), ShadowColor = new Color(0, 0, 0, 0.62f), ShadowSize = Mathf.RoundToInt(28 * s), ShadowOffset = new Vector2(0, 12 * s) };
        _shadow.SetCornerRadiusAll(Mathf.RoundToInt(r));
        _glow = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(_v.Colour, 0.22f), ShadowSize = Mathf.RoundToInt(24 * s) };
        _glow.SetCornerRadiusAll(Mathf.RoundToInt(r));
        QueueRedraw();
    }

    private float _width, _textX, _medalSize;

    /// <summary>Height from the text (wrapped description), positions of the medal, text and points.</summary>
    public void Fit()
    {
        var s = _s;
        var padY = 13f * s;
        var textW = _width - _textX - 22f * s;
        _text.Size = new Vector2(textW, 0);
        var textH = _text.GetCombinedMinimumSize().Y;
        var height = Mathf.Max(_medalSize + padY * 2f, textH + padY * 2f);
        if (Size != new Vector2(_width, height))
        {
            Size = new Vector2(_width, height);
            PivotOffset = Size / 2f;
            QueueRedraw();
        }
        _text.Position = new Vector2(_textX, (height - textH) / 2f);
        _medal.Position = new Vector2(14f * s, (height - _medalSize) / 2f);
        _meta.Position = new Vector2(_width - 14f * s - _meta.GetCombinedMinimumSize().X, 10f * s);
    }

    public void Advance(float dt, bool hover)
    {
        _age += dt;
        if (_leave >= 0) _leave += dt;
        else if (!hover || _left <= 0) _left -= dt; // hovering holds it
        if (_leave < 0 && _left <= 0) _leave = 0;
        QueueRedraw();
    }

    public void Place()
    {
        // drop in (0.42 s, slight overshoot), lift out (0.35 s); the medal pops with a spin from 0.12 s
        var dy = 0f;
        var alpha = 1f;
        var scale = 1f;
        if (_age < Enter)
        {
            var t = _age / Enter;
            var e = BackOut(t, 1.2f);
            dy = -24f * _s * (1f - e);
            scale = 0.96f + 0.04f * e;
            alpha = Mathf.Clamp(t * 2.2f, 0f, 1f);
        }
        if (_leave >= 0)
        {
            var t = Mathf.Clamp(_leave / Leave, 0f, 1f);
            var e = t * t;
            dy = -16f * _s * e;
            alpha = 1f - e;
        }
        Position = Slot!.Value + new Vector2(0, dy);
        Scale = new Vector2(scale, scale);
        Modulate = new Color(1, 1, 1, alpha);

        var p = Mathf.Clamp((_age - 0.12f) / 0.55f, 0f, 1f);
        float ms, rot;
        if (p < 0.7f)
        {
            var k = Ease(p / 0.7f);
            ms = Mathf.Lerp(0.4f, 1.12f, k);
            rot = Mathf.Lerp(-30f, 4f, k);
        }
        else
        {
            var k = Ease((p - 0.7f) / 0.3f);
            ms = Mathf.Lerp(1.12f, 1f, k);
            rot = Mathf.Lerp(4f, 0f, k);
        }
        _medal.Scale = new Vector2(ms, ms);
        _medal.RotationDegrees = rot;
        _medal.Modulate = new Color(1, 1, 1, Mathf.Clamp(p / 0.4f, 0f, 1f));
    }

    private static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp(t, 0f, 1f), 3f);

    private static float BackOut(float t, float k)
    {
        t = Mathf.Clamp(t, 0f, 1f) - 1f;
        return t * t * ((k + 1f) * t + k) + 1f;
    }

    public override void _Draw()
    {
        if (_s <= 0) return;
        var w = Size.X;
        var h = Size.Y;
        var r = 11f * _s;
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(_glow, rect);
        DrawStyleBox(_shadow, rect);

        var panel = new Color(0.027f, 0.043f, 0.086f, 0.96f);
        var tinted = Medals.Mix(_v.Colour, new Color(0.027f, 0.043f, 0.086f), 0.16f);
        var line = Medals.Mix(_v.Colour, HudKit.Line, 0.55f);
        // light sweep: a skewed band crossing from -30% to 120% over 1.1 s from 0.35 s
        var sweep = (_age - 0.35f) / 1.1f;
        var band = w * 0.22f;
        var bandX = Mathf.Lerp(-0.3f * w, 1.2f * w, sweep) + band / 2f;
        var skew = Mathf.Tan(Mathf.DegToRad(18f));
        var drain = Mathf.Clamp(_left / _life, 0f, 1f);

        const int slices = 72;
        for (var i = 0; i < slices; i++)
        {
            var x0 = w * i / slices;
            var x1 = w * (i + 1) / slices;
            var (t0, b0) = Edge(x0, w, h, r);
            var (t1, b1) = Edge(x1, w, h, r);
            var c0 = Fill(x0 / w, tinted, panel);
            var c1 = Fill(x1 / w, tinted, panel);
            var pts = new[] { new Vector2(x0, t0), new Vector2(x1, t1), new Vector2(x1, b1), new Vector2(x0, b0) };
            if (sweep is > 0f and < 1f)
            {
                var cols = new[]
                {
                    c0 + Shine(x0 + skew * (h / 2f - t0), bandX, band), c1 + Shine(x1 + skew * (h / 2f - t1), bandX, band),
                    c1 + Shine(x1 + skew * (h / 2f - b1), bandX, band), c0 + Shine(x0 + skew * (h / 2f - b0), bandX, band),
                };
                DrawPolygon(pts, cols);
            }
            else DrawPolygon(pts, new[] { c0, c1, c1, c0 });
            // timer bar: the bottom 2 px, from the left to the time left
            var barTop = h - 2f * Mathf.Max(1f, _s);
            var xe = Mathf.Min(x1, w * drain);
            if (xe > x0)
            {
                var (_, be) = Edge(xe, w, h, r);
                var bt0 = Mathf.Max(barTop, t0);
                var bte = Mathf.Max(barTop, Edge(xe, w, h, r).Top);
                if (b0 > bt0 && be > bte)
                    DrawPolygon(new[] { new Vector2(x0, bt0), new Vector2(xe, bte), new Vector2(xe, be), new Vector2(x0, b0) },
                        new[] { new Color(_v.Colour, 0.75f), new Color(_v.Colour, 0.75f), new Color(_v.Colour, 0.75f), new Color(_v.Colour, 0.75f) });
            }
        }
        DrawPolyline(Outline(w, h, r), line, 1f, true);
    }

    private static Color Fill(float u, Color tinted, Color panel) => u >= 0.46f ? panel : tinted.Lerp(panel, u / 0.46f);

    private static Color Shine(float x, float centre, float band)
    {
        var d = Mathf.Abs(x - centre) / (band / 2f);
        var k = d >= 1f ? 0f : 0.15f * (1f - d); // the mockup's #ffffff26 at the band's middle
        return new Color(k, k, k, 0f);
    }

    /// <summary>Top and bottom of the rounded rectangle at x.</summary>
    private static (float Top, float Bottom) Edge(float x, float w, float h, float r)
    {
        var dx = x < r ? r - x : x > w - r ? x - (w - r) : 0f;
        var dy = dx > 0 ? r - Mathf.Sqrt(Mathf.Max(0f, r * r - dx * dx)) : 0f;
        return (dy, h - dy);
    }

    private static Vector2[] Outline(float w, float h, float r)
    {
        var pts = new List<Vector2>();
        void Arc(Vector2 c, float from)
        {
            for (var k = 0; k <= 6; k++)
            {
                var a = Mathf.DegToRad(from + 90f * k / 6f);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }
        var o = 0.5f;
        Arc(new Vector2(r + o, r + o), 180f);
        Arc(new Vector2(w - r - o, r + o), 270f);
        Arc(new Vector2(w - r - o, h - r - o), 0f);
        Arc(new Vector2(r + o, h - r - o), 90f);
        pts.Add(pts[0]);
        return pts.ToArray();
    }
}
