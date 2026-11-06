using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Shared look for the build drawer, the large building card and the placing strip: PlanetHud's colours (and the
/// mockup's, docs/mockups/build-card), a condensed face for titles and captions (Antonio as in the mockup if the system
/// has it, else Bahnschrift / Arial Narrow …, like HelmScreen), and helpers that size things from 1280×720 pixels.
/// </summary>
public static class HudKit
{
    public static readonly Color Ink = new(0.9f, 0.92f, 0.96f);
    public static readonly Color Dim = new(0.6f, 0.65f, 0.75f);
    public static readonly Color Line = new(0.15f, 0.19f, 0.29f);
    public static readonly Color Good = new(0.56f, 0.83f, 0.69f);
    public static readonly Color Warn = new(1f, 0.7f, 0.42f);
    public static readonly Color Accent = PlanetHud.Accent;
    public static readonly Color Sky = new(0.62f, 0.82f, 1f);
    public static readonly Color Panel = new(0.02f, 0.03f, 0.07f, 0.97f);
    public static readonly Color Chip = new(0.067f, 0.094f, 0.16f);
    public static readonly Color Btn = new(0.06f, 0.1f, 0.19f);
    public static readonly Color BtnEdge = new(0.17f, 0.23f, 0.36f);
    public static readonly Color HoverEdge = new(0.3f, 0.4f, 0.6f);
    public static readonly Color Pressed = new(0.24f, 0.17f, 0.05f);

    private static SystemFont? _condensed, _condensedBold;

    /// <summary>The condensed face (bold for titles).</summary>
    public static Font Condensed(bool bold = false)
    {
        string[] names = { "Antonio", "Bahnschrift", "Barlow Condensed", "Arial Narrow", "Roboto Condensed", "Liberation Sans Narrow", "DejaVu Sans Condensed", "Arial" };
        _condensed ??= new SystemFont { FontNames = names, FontWeight = 500, FontStretch = 75 };
        _condensedBold ??= new SystemFont { FontNames = names, FontWeight = 700, FontStretch = 75 };
        return bold ? _condensedBold : _condensed;
    }

    /// <summary>UI scale for a screen: 1 at 1280×720 (clamped 0.75–3).</summary>
    public static float ScaleFor(Vector2 screen) => Mathf.Clamp(Mathf.Min(screen.X / 1280f, screen.Y / 720f), 0.75f, 3f);

    public static Label Label(string text, float size, Color colour, bool condensed = false, bool bold = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", Mathf.Max(8, Mathf.RoundToInt(size)));
        l.AddThemeColorOverride("font_color", colour);
        if (condensed || bold) l.AddThemeFontOverride("font", Condensed(bold));
        return l;
    }

    public static StyleBoxFlat Box(Color bg, Color border, float radius, float padX = 0, float padY = 0, int borderWidth = 1)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(Mathf.RoundToInt(radius));
        return s;
    }

    public static PanelContainer PanelBox(Color bg, Color border, float radius, float padX, float padY)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(bg, border, radius, padX, padY));
        return p;
    }

    /// <summary>A tile button's boxes: normal, hover, pressed (active = accent edge on the dark amber fill), disabled.</summary>
    public static void StyleTile(Button b, float radius, float pad = 0)
    {
        b.AddThemeStyleboxOverride("normal", Box(Btn, BtnEdge, radius, pad, pad));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.08f, 0.13f, 0.25f), HoverEdge, radius, pad, pad));
        b.AddThemeStyleboxOverride("pressed", Box(Pressed, Accent, radius, pad, pad, 2));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.28f, 0.2f, 0.06f), Accent, radius, pad, pad, 2));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.04f, 0.05f, 0.09f), new Color(0.11f, 0.14f, 0.2f), radius, pad, pad));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_disabled_color", Dim);
    }

    /// <summary>An action button (PLACE / CLOSE): accent edge when primary.</summary>
    public static Button ActionButton(string text, bool primary, float s)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34 * s) };
        b.AddThemeFontOverride("font", Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
        var r = 6 * s;
        b.AddThemeStyleboxOverride("normal", Box(primary ? new Color(0.12f, 0.087f, 0.027f) : new Color(0.04f, 0.06f, 0.11f), primary ? Accent : Line, r, 12 * s, 4 * s));
        b.AddThemeStyleboxOverride("hover", Box(primary ? new Color(0.2f, 0.14f, 0.04f) : new Color(0.07f, 0.1f, 0.18f), primary ? Accent : HoverEdge, r, 12 * s, 4 * s));
        b.AddThemeStyleboxOverride("pressed", Box(Pressed, Accent, r, 12 * s, 4 * s));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.03f, 0.04f, 0.08f), new Color(0.11f, 0.14f, 0.2f), r, 12 * s, 4 * s));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Ink);
        b.AddThemeColorOverride("font_disabled_color", Dim);
        return b;
    }

    public static TextureRect Picture(Vector2 size)
    {
        return new TextureRect
        {
            CustomMinimumSize = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true,
        };
    }

    /// <summary>A short resource name for cost chips: the first three letters, upper case ("DUR", "TRI").</summary>
    public static string Short(string name) => name.Length > 3 ? name[..3].ToUpperInvariant() : name.ToUpperInvariant();

    /// <summary>One cost chip: the resource's icon and "120 DUR"; turns red when short.</summary>
    public sealed class CostChip
    {
        public readonly HBoxContainer Root;
        private readonly Label _text;
        private bool _short;

        public CostChip(string resourceId, string text, float size)
        {
            Root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            Root.AddThemeConstantOverride("separation", Mathf.RoundToInt(size * 0.25f));
            Root.AddChild(new TextureRect
            {
                Texture = Icons.Get(resourceId),
                CustomMinimumSize = new Vector2(size, size),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            _text = Label(text, size * 0.95f, Dim, condensed: true);
            Root.AddChild(_text);
        }

        public void Set(string text, bool isShort, Color normal)
        {
            if (_text.Text != text) _text.Text = text;
            if (isShort == _short && !_first) return;
            _first = false;
            _short = isShort;
            _text.AddThemeColorOverride("font_color", isShort ? Warn : normal);
        }

        private bool _first = true;
    }

    /// <summary>Removes and frees every child of a container.</summary>
    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            n.RemoveChild(c);
            c.QueueFree();
        }
    }

    /// <summary>Categories for the drawer: building category (buildings.json) → drawer group, in group order.</summary>
    public static readonly (string Id, string Name, string[] Categories)[] Groups =
    {
        ("resources", "Resources", new[] { "resource", "logistics" }),
        ("industry", "Industry", new[] { "industry", "production" }),
        ("power", "Power & crew", new[] { "power", "habitation" }),
        ("science", "Science", new[] { "research" }),
    };

    /// <summary>The drawer group of a building category; "other" for anything not listed.</summary>
    public static int GroupOf(string category)
    {
        for (var i = 0; i < Groups.Length; i++)
            foreach (var c in Groups[i].Categories)
                if (c == category) return i;
        return Groups.Length;
    }

    public static readonly Dictionary<string, string> CategoryNames = new()
    {
        ["logistics"] = "Logistics", ["industry"] = "Industry", ["resource"] = "Resources", ["power"] = "Power",
        ["habitation"] = "Habitation", ["production"] = "Production", ["research"] = "Research", ["core"] = "Colony core",
    };
}
