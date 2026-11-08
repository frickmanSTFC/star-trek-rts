using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The achievements screen (docs/mockups/achievements, part 2): a full-screen overlay like <see cref="BuildingCard"/>
/// (dimmed world, click outside closes, the game runs on underneath). ACHIEVEMENTS tab: header (earned / total with a
/// bar, points, per-tier counts), the gate line when unlocks don't count in this game, the category rail with
/// earned / total, filters All / Earned / In progress / Locked, cards (medal, name, TIER · points, description, earned
/// date · world · game time, a progress bar from the sim, hidden ones "Hidden achievement" with the tier showing).
/// HANGAR tab: the cosmetics the earned achievements unlock, one pick per kind (or Standard); rover names and hull
/// numbers several, for the next new game. Sizes scale from 1280×720. The driver feeds <see cref="Data"/>,
/// <see cref="ProgressOf"/> and <see cref="Gate"/>.
/// </summary>
public partial class AchievementScreen : Control
{
    /// <summary>A HANGAR pick changed (the driver re-applies <see cref="Cosmetics"/>).</summary>
    public event Action? CosmeticsChanged;

    public AchievementData? Data { get; set; }

    /// <summary>Progress toward an achievement (sim, or the view's own counters), or null.</summary>
    public Func<string, AchievementProgress?> ProgressOf { get; set; } = _ => null;

    /// <summary>Why nothing unlocks in this game, or null.</summary>
    public string? Gate { get; set; }

    public bool IsOpen => Visible;

    private string _tab = "achievements";
    private string _cat = "all";
    private string _filter = "all";
    private string _builtKey = "";
    private double _refreshAt;
    private float _s = 1f;
    private readonly List<(string Id, Bar Bar, Label Text)> _bars = new();

    private static readonly (string Id, string Name)[] Filters = { ("all", "All"), ("earned", "Earned"), ("progress", "In progress"), ("locked", "Locked") };

    private static readonly Dictionary<string, (string Title, string Note)> KindText = new()
    {
        ["paint"] = ("HULL PAINT", "The stripe colour and pattern of your vehicles. Applies now."),
        ["finish"] = ("SPECIAL FINISH", "A finish for your vehicles and drop ships. Applies now."),
        ["dropship_name"] = ("DROP SHIP NAME", "Painted on your drop ships. Applies now."),
        ["beam_colour"] = ("EXOCOMP BEAMS", "The colour of your construction beams. Applies now."),
        ["dome_tint"] = ("DOME GLASS", "The tint of your domes' glass on hostile worlds. Applies now."),
        ["rover_name"] = ("ROVER NAMES", "Handed out first, in the order picked. Applies to your next new game."),
        ["hull_number"] = ("HULL NUMBERS", "Handed out first, in the order picked. Applies to your next new game."),
    };

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            // the dimmed backdrop: a click outside the panel closes it
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }) Close();
        };
    }

    public void Open(string? tab = null)
    {
        if (tab != null) _tab = tab;
        Visible = true;
        _builtKey = "";
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var screen = GetViewportRect().Size;
        var now = Time.GetTicksMsec() / 1000.0;
        var refresh = now >= _refreshAt;
        if (refresh) _refreshAt = now + 0.5;
        // progress changes which cards are "in progress": part of the key, checked twice a second
        var progressKey = refresh || _builtKey.Length == 0 ? ProgressKey() : _lastProgressKey;
        _lastProgressKey = progressKey;
        var key = $"{screen}|{AchievementProfile.Version}|{_tab}|{_cat}|{_filter}|{Gate}|{Data?.GetHashCode()}|{progressKey}";
        if (key != _builtKey)
        {
            _builtKey = key;
            Build(screen);
        }
        else if (refresh) UpdateBars();
    }

    private string _lastProgressKey = "";

    private string ProgressKey()
    {
        if (Data == null || _tab != "achievements" || _filter != "progress") return "";
        return string.Join(",", Data.List.Where(a => ProgressOf(a.Id) != null).Select(a => a.Id));
    }

    // ---------------------------------------------------------------- build

    private void Build(Vector2 screen)
    {
        _s = HudKit.ScaleFor(screen);
        var s = _s;
        HudKit.Clear(this);
        _bars.Clear();

        var dim = new ColorRect { Color = new Color(0.005f, 0.01f, 0.025f, 0.66f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var width = Mathf.Min(screen.X - 40 * s, 1120 * s);
        var height = Mathf.Min(screen.Y - 36 * s, 680 * s);
        var pad = 22 * s;
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Size = new Vector2(width, height), Position = (screen - new Vector2(width, height)) / 2f };
        panel.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.03f, 0.047f, 0.09f, 0.985f), HudKit.Line, 10 * s, pad, pad * 0.9f));
        AddChild(panel);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(14 * s));
        panel.AddChild(column);
        var inner = new Vector2(width - pad * 2, height - pad * 1.8f);

        column.AddChild(Header(s));
        if (Gate != null)
        {
            var gate = HudKit.Label($"Unlocks don't count in this game: {Gate}. Earned achievements and the hangar still show.", 13 * s, HudKit.Warn);
            gate.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(gate);
        }
        column.AddChild(new ColorRect { Color = HudKit.Line, CustomMinimumSize = new Vector2(0, Mathf.Max(1, s)), MouseFilter = MouseFilterEnum.Ignore });

        if (Data == null)
        {
            var none = HudKit.Label("No achievement data is loaded (data/achievements/ is missing), so there is nothing to show yet.", 15 * s, HudKit.Dim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(none);
        }
        else if (_tab == "hangar") column.AddChild(Hangar(s, inner.X));
        else column.AddChild(Body(s, inner.X));

        var hint = HudKit.Label("Esc or F3 closes  ·  kept on this machine between games", 11 * s, HudKit.Dim);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(hint);
    }

    private Control Header(float s)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(22 * s));
        var titles = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titles.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
        titles.AddChild(HudKit.Label("ACHIEVEMENTS", 30 * s, HudKit.Ink, condensed: true, bold: true));
        titles.AddChild(HudKit.Label("Commander profile · kept between games on this machine", 13 * s, HudKit.Dim));
        row.AddChild(titles);

        var tabs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkEnd };
        tabs.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        foreach (var (id, name) in new[] { ("achievements", "ACHIEVEMENTS"), ("hangar", "HANGAR") })
        {
            var b = PillButton(name, _tab == id, s);
            var pick = id;
            b.Pressed += () =>
            {
                _tab = pick;
                _builtKey = "";
            };
            tabs.AddChild(b);
        }
        row.AddChild(tabs);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        if (Data != null)
        {
            var list = Data.List;
            var earned = list.Where(a => AchievementProfile.IsEarned(a.Id)).ToList();
            var points = earned.Sum(a => Medals.Points(Data, a.Tier));
            var count = Stat($"{earned.Count} / {list.Count}", "EARNED", s);
            var bar = new Bar { Fraction = list.Count == 0 ? 0 : earned.Count / (float)list.Count, Colour = HudKit.Accent, CustomMinimumSize = new Vector2(140 * s, 4 * s) };
            count.AddChild(bar);
            row.AddChild(count);
            row.AddChild(Stat(points.ToString(), "POINTS", s));
            var tiers = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            tiers.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
            foreach (var t in Data.Tiers.OrderByDescending(t => t.Points))
            {
                var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass, TooltipText = $"{t.Name}: {Medals.Points(Data, t.Id)} points each" };
                box.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * s));
                var medal = HudKit.Picture(new Vector2(20 * s, 20 * s));
                medal.Texture = Medals.Medal(t.Id, Medals.TierColour(Data, t.Id), "hex", false, false, 40 * s);
                box.AddChild(medal);
                box.AddChild(HudKit.Label(earned.Count(a => a.Tier == t.Id).ToString(), 16 * s, HudKit.Ink, condensed: true, bold: true));
                box.AddChild(HudKit.Label($"/{list.Count(a => a.Tier == t.Id)}", 12 * s, HudKit.Dim));
                tiers.AddChild(box);
            }
            row.AddChild(tiers);
        }

        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, TooltipText = "Close (Esc)", SizeFlagsVertical = SizeFlags.ShrinkBegin };
        HudKit.StyleTile(close, 6 * s, 4 * s);
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        close.CustomMinimumSize = new Vector2(34 * s, 34 * s);
        close.Pressed += Close;
        row.AddChild(close);
        return row;
    }

    private static VBoxContainer Stat(string value, string label, float s)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkEnd };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
        box.AddChild(HudKit.Label(value, 26 * s, HudKit.Ink, condensed: true, bold: true));
        box.AddChild(HudKit.Label(label, 11 * s, HudKit.Dim, condensed: true, bold: true));
        return box;
    }

    /// <summary>A rail / filter / tab button: accent edge and text when on.</summary>
    private static Button PillButton(string text, bool on, float s, bool rail = false)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, Alignment = rail ? HorizontalAlignment.Left : HorizontalAlignment.Center };
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt((rail ? 15 : 13) * s));
        var r = 6 * s;
        var padX = 10 * s;
        var padY = (rail ? 8 : 6) * s;
        var normal = rail ? HudKit.Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), r, padX, padY) : HudKit.Box(HudKit.Chip, HudKit.BtnEdge, r, padX, padY);
        var lit = HudKit.Box(rail ? HudKit.Chip : new Color(0.12f, 0.087f, 0.027f), rail ? HudKit.BtnEdge : HudKit.Accent, r, padX, padY);
        var hover = HudKit.Box(new Color(0.08f, 0.12f, 0.21f), HudKit.HoverEdge, r, padX, padY);
        b.AddThemeStyleboxOverride("normal", on ? lit : normal);
        b.AddThemeStyleboxOverride("hover", on ? lit : hover);
        b.AddThemeStyleboxOverride("pressed", lit);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var ink = on ? (rail ? HudKit.Ink : HudKit.Accent) : rail ? HudKit.Dim : HudKit.Ink;
        b.AddThemeColorOverride("font_color", ink);
        b.AddThemeColorOverride("font_hover_color", on ? ink : HudKit.Ink);
        b.AddThemeColorOverride("font_pressed_color", ink);
        if (rail && on)
        {
            // the accent stripe on the left (the mockup's inset shadow)
            var stripe = new ColorRect { Color = HudKit.Accent, MouseFilter = MouseFilterEnum.Ignore };
            b.AddChild(stripe);
            stripe.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
            stripe.OffsetRight = Mathf.Max(3, 3 * s);
        }
        return b;
    }

    // ---------------------------------------------------------------- achievements tab

    private Control Body(float s, float innerWidth)
    {
        var data = Data!;
        var body = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        var gap = 20 * s;
        body.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));

        // category rail with earned / total
        var railWidth = 200 * s;
        var rail = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(railWidth, 0) };
        rail.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var cats = new List<(string Id, string Name)> { ("all", "All") };
        cats.AddRange(data.Categories.OrderBy(c => c.Order).Select(c => (c.Id, c.Name)));
        foreach (var (id, name) in cats)
        {
            var inCat = data.List.Where(a => id == "all" || a.Category == id).ToList();
            var b = PillButton(name, _cat == id, s, rail: true);
            b.CustomMinimumSize = new Vector2(railWidth, 0);
            var count = HudKit.Label($"{inCat.Count(a => AchievementProfile.IsEarned(a.Id))}/{inCat.Count}", 11 * s, HudKit.Dim);
            count.AddThemeFontOverride("font", Medals.Mono());
            b.AddChild(count);
            count.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
            count.OffsetLeft = -60 * s;
            count.OffsetRight = -10 * s;
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            var pick = id;
            b.Pressed += () =>
            {
                _cat = pick;
                _builtKey = "";
            };
            rail.AddChild(b);
        }
        body.AddChild(rail);

        var right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
        var filters = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        filters.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        foreach (var (id, name) in Filters)
        {
            var b = PillButton(name, _filter == id, s);
            var pick = id;
            b.Pressed += () =>
            {
                _filter = pick;
                _builtKey = "";
            };
            filters.AddChild(b);
        }
        right.AddChild(filters);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        right.AddChild(scroll);
        var gridWidth = innerWidth - railWidth - gap - 14 * s; // room for the scroll bar
        var cardGap = 10 * s;
        var columns = Mathf.Max(1, Mathf.FloorToInt((gridWidth + cardGap) / (250 * s + cardGap)));
        var cardWidth = (gridWidth - (columns - 1) * cardGap) / columns;
        var grid = new GridContainer { Columns = columns, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(cardGap));
        grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(cardGap));
        scroll.AddChild(grid);

        var catOrder = data.Categories.ToDictionary(c => c.Id, c => c.Order);
        var shown = data.List.Select((a, i) => (a, i))
            .Where(x => _cat == "all" || x.a.Category == _cat)
            .Where(x => Passes(x.a))
            .OrderBy(x => AchievementProfile.IsEarned(x.a.Id) ? 0 : 1)
            .ThenBy(x => catOrder.GetValueOrDefault(x.a.Category, 999))
            .ThenBy(x => x.i)
            .Select(x => x.a)
            .ToList();
        if (shown.Count == 0)
        {
            grid.Columns = 1;
            grid.AddChild(HudKit.Label("Nothing here yet.", 14 * s, HudKit.Dim));
        }
        foreach (var a in shown) grid.AddChild(Card(a, cardWidth, s));
        body.AddChild(right);
        return body;
    }

    private bool Passes(AchievementDef a)
    {
        var earned = AchievementProfile.IsEarned(a.Id);
        return _filter switch
        {
            "earned" => earned,
            "locked" => !earned,
            "progress" => !earned && !(a.Hidden) && ProgressOf(a.Id) != null,
            _ => true,
        };
    }

    private Control Card(AchievementDef a, float width, float s)
    {
        var data = Data!;
        var record = AchievementProfile.EarnedOf(a.Id);
        var earned = record != null;
        var secret = a.Hidden && !earned;
        var tier = Medals.TierColour(data, a.Tier);
        var bg = new Color(0.039f, 0.067f, 0.125f);
        var style = HudKit.Box(earned ? Medals.Mix(tier, bg, 0.09f) : bg, earned ? Medals.Mix(tier, HudKit.Line, 0.38f) : HudKit.Line, 8 * s, 12 * s, 12 * s);
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(width, 0) };
        card.AddThemeStyleboxOverride("panel", style);
        if (earned && record != null)
            card.TooltipText = $"{a.Name}: earned {record.Date} on {record.World} (seed {record.Seed}) at {AchievementProfile.GameTime(record.GameSeconds)} game time";
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
        card.AddChild(row);
        var medalSize = 56 * s;
        var medal = HudKit.Picture(new Vector2(medalSize, medalSize));
        medal.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        medal.Texture = Medals.Of(data, a, earned, medalSize * 1.5f);
        row.AddChild(medal);

        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        row.AddChild(text);
        var textWidth = width - 24 * s - medalSize - 12 * s;
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        var name = HudKit.Label(secret ? "Hidden achievement" : a.Name, 18 * s, earned ? HudKit.Ink : HudKit.Dim, condensed: true, bold: true);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(name);
        var pts = HudKit.Label($"{Medals.TierName(data, a.Tier)} · {Medals.Points(data, a.Tier)}", 11 * s, earned ? tier : HudKit.Dim);
        pts.AddThemeFontOverride("font", Medals.Mono());
        pts.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(pts);
        text.AddChild(top);
        var desc = HudKit.Label(secret ? "Keep playing to find out." : a.Description, 13 * s, HudKit.Dim);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(textWidth, 0);
        text.AddChild(desc);

        if (record != null)
        {
            var when = HudKit.Label($"Earned {record.Date} · {record.World} · {AchievementProfile.GameTime(record.GameSeconds)}", 11 * s, Medals.Mix(tier, HudKit.Ink, 0.75f));
            when.AddThemeFontOverride("font", Medals.Mono());
            when.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            when.CustomMinimumSize = new Vector2(textWidth, 0);
            text.AddChild(when);
            if (data.Cosmetic(a.Reward) is { } reward)
            {
                var unlocks = HudKit.Label($"Unlocks {reward.Name} (HANGAR)", 11 * s, HudKit.Dim);
                text.AddChild(unlocks);
            }
        }
        else if (!secret && ProgressOf(a.Id) is { } p)
        {
            var bar = new Bar { Colour = HudKit.Sky, CustomMinimumSize = new Vector2(0, 4 * s), Fraction = Fraction(p) };
            var label = HudKit.Label(ProgressText(p), 11 * s, HudKit.Dim);
            label.AddThemeFontOverride("font", Medals.Mono());
            var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 3 * s), MouseFilter = MouseFilterEnum.Ignore });
            box.AddChild(bar);
            box.AddChild(label);
            text.AddChild(box);
            _bars.Add((a.Id, bar, label));
        }
        else if (!secret && data.Cosmetic(a.Reward) is { } reward)
        {
            text.AddChild(HudKit.Label($"Reward: {reward.Name}", 11 * s, new Color(HudKit.Dim, 0.8f)));
        }
        return card;
    }

    private void UpdateBars()
    {
        foreach (var (id, bar, label) in _bars)
        {
            if (ProgressOf(id) is not { } p) continue;
            bar.Fraction = Fraction(p);
            var text = ProgressText(p);
            if (label.Text != text) label.Text = text;
        }
    }

    private static float Fraction(AchievementProgress p) => p.Target <= 0 ? 0f : Mathf.Clamp(p.Current / (float)p.Target, 0f, 1f);

    /// <summary>"62 of 100 hexes", "21 of 30 min" (seconds), "31 of 50 %".</summary>
    public static string ProgressText(AchievementProgress p) => p.Unit switch
    {
        "s" => $"{p.Current / 60} of {p.Target / 60} min",
        "%" => $"{p.Current} of {p.Target} %",
        "" => $"{p.Current} of {p.Target}",
        _ => $"{p.Current} of {p.Target} {p.Unit}",
    };

    // ---------------------------------------------------------------- hangar tab

    private Control Hangar(float s, float innerWidth)
    {
        var data = Data!;
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(16 * s));
        scroll.AddChild(list);
        var unlocked = AchievementProfile.Unlocked(data);
        var intro = HudKit.Label($"{unlocked.Count} of {data.Cosmetics.Count} cosmetics unlocked. Each achievement unlocks one; they change the look only, never the game.", 13 * s, HudKit.Dim);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(innerWidth - 20 * s, 0);
        list.AddChild(intro);

        foreach (var kind in AchievementProfile.Kinds)
        {
            var ofKind = data.Cosmetics.Where(c => c.Kind == kind).ToList();
            if (ofKind.Count == 0) continue;
            var (title, note) = KindText.GetValueOrDefault(kind, (kind.ToUpperInvariant(), ""));
            var section = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            section.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
            var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            head.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
            head.AddChild(HudKit.Label(title, 16 * s, HudKit.Accent, condensed: true, bold: true));
            var noteLabel = HudKit.Label(note, 12 * s, HudKit.Dim);
            noteLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            head.AddChild(noteLabel);
            section.AddChild(head);

            var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(innerWidth - 20 * s, 0) };
            flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
            flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(8 * s));
            var multi = AchievementProfile.IsMulti(kind);
            var active = AchievementProfile.ActivePicks(data, kind);
            var standard = Tile(multi ? "NONE EXTRA" : "STANDARD", null, active.Count == 0, true, s,
                multi ? "Only the usual shuffled names / numbers" : "The normal look");
            var k = kind;
            standard.Pressed += () => Pick(k, null);
            flow.AddChild(standard);
            foreach (var c in ofKind)
            {
                var isUnlocked = AchievementProfile.IsUnlocked(data, c.Id);
                var order = multi ? active.FindIndex(x => x.Id == c.Id) : -1;
                var label = kind is "rover_name" or "hull_number" or "dropship_name" ? Display(c) : c.Name.ToUpperInvariant();
                if (order >= 0) label = $"{order + 1} · {label}";
                var by = data.List.FirstOrDefault(a => a.Reward == c.Id);
                var how = by == null ? "No achievement unlocks this yet"
                    : isUnlocked ? $"Unlocked by {by.Name}"
                    : by.Hidden && !AchievementProfile.IsEarned(by.Id) ? "Earn a hidden achievement to unlock this" : $"Earn {by.Name} to unlock this: {by.Description}";
                var tip = $"{c.Name}{(kind is "rover_name" or "hull_number" or "dropship_name" ? $" ({Display(c)})" : "")}\n{how}";
                var tile = Tile(label, Swatch(c), active.Any(x => x.Id == c.Id), isUnlocked, s, tip);
                var id = c.Id;
                tile.Pressed += () => Pick(k, id);
                flow.AddChild(tile);
            }
            section.AddChild(flow);
            list.AddChild(section);
        }
        return scroll;
    }

    private static string Display(CosmeticDef c) => c.Kind == "hull_number" && int.TryParse(c.Value, out var n) ? n.ToString("00") : c.Value.ToUpperInvariant();

    private void Pick(string kind, string? id)
    {
        if (id == null) AchievementProfile.SetPick(kind, null);
        else if (AchievementProfile.IsMulti(kind)) AchievementProfile.TogglePick(kind, id);
        else AchievementProfile.SetPick(kind, AchievementProfile.IsPicked(kind, id) ? null : id);
        CosmeticsChanged?.Invoke();
    }

    private static Button Tile(string text, Texture2D? swatch, bool picked, bool enabled, float s, string tooltip)
    {
        var b = new Button
        {
            Text = text,
            Icon = enabled ? swatch : Medals.Line("lock", HudKit.Dim, 32 * s),
            FocusMode = FocusModeEnum.None,
            ToggleMode = true,
            ButtonPressed = picked,
            Disabled = !enabled,
            TooltipText = tooltip,
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(0, 36 * s),
        };
        HudKit.StyleTile(b, 6 * s, 9 * s);
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
        b.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
        b.AddThemeConstantOverride("icon_max_width", Mathf.RoundToInt(22 * s));
        b.AddThemeColorOverride("font_pressed_color", HudKit.Accent);
        b.AddThemeColorOverride("font_hover_pressed_color", HudKit.Accent);
        return b;
    }

    private static readonly Dictionary<string, Texture2D> Swatches = new();

    /// <summary>A small picture of a cosmetic: paint with its pattern, a finish's metal, a beam or tint colour; none for
    /// names and numbers.</summary>
    private static Texture2D? Swatch(CosmeticDef c)
    {
        if (c.Kind is "rover_name" or "hull_number" or "dropship_name") return null;
        if (Swatches.TryGetValue(c.Id, out var cached)) return cached;
        const int n = 32;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var a = AchievementProfile.ParseColour(c.Value) ?? FinishColour(c.Value).A;
        var b = AchievementProfile.ParseColour(c.Colour2) ?? (c.Kind == "finish" ? FinishColour(c.Value).B : a.Darkened(0.35f));
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var edge = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                var useB = c.Kind switch
                {
                    "paint" => c.Pattern switch
                    {
                        "band" => y is >= 12 and < 20,
                        "stripes" => (x / 5) % 2 == 1,
                        "chevrons" => ((x + Math.Abs(y - n / 2)) / 6) % 2 == 1,
                        _ => false,
                    },
                    "finish" => x + y > n + 6, // a highlight corner
                    _ => false,
                };
                var colour = edge ? new Color(0.02f, 0.03f, 0.06f) : useB ? b : a;
                if (c.Kind == "beam_colour" && !edge) colour = a.Lerp(Colors.White, Mathf.Max(0f, 1f - Mathf.Abs(y - n / 2f) / 6f) * 0.7f);
                image.SetPixel(x, y, colour);
            }
        var texture = ImageTexture.CreateFromImage(image);
        Swatches[c.Id] = texture;
        return texture;
    }

    /// <summary>Swatch colours for the finish ids the cosmetics agent knows.</summary>
    private static (Color A, Color B) FinishColour(string finish) => finish switch
    {
        "brushed_gold" => (Color.FromHtml("#c9a24a"), Color.FromHtml("#f3dc8a")),
        "chrome" => (Color.FromHtml("#aeb7c4"), Color.FromHtml("#f4f7fb")),
        "blue_white" => (Color.FromHtml("#e8eef8"), Color.FromHtml("#2f62c8")),
        "gunmetal" => (Color.FromHtml("#4a4f58"), Color.FromHtml("#7b828e")),
        "pearl" => (Color.FromHtml("#ece6dc"), Color.FromHtml("#fff9f0")),
        "rescue_white" => (Color.FromHtml("#f4f4f2"), Color.FromHtml("#e23a2a")),
        _ => (HudKit.Dim, HudKit.Ink),
    };
}
