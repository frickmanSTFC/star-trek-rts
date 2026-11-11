using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FinalFrontier;

/// <summary>One objective line: Progress "34 / 60" ("" for a yes/no objective), Fraction 0..1 (below 0 = no bar).</summary>
public sealed record MissionObjectiveLine(string Text, bool Done, bool Optional, string Progress, float Fraction);

/// <summary>One mission in the tracker. Tag "MAIN" / "SIDE" / "OFFER"; Timer "" or "1:24"; Hint one dim line ("" none);
/// Locatable = it has a marker the camera can fly to.</summary>
public sealed record TrackerEntry(string MissionId, string Tag, string Title, string Stage, IReadOnlyList<MissionObjectiveLine> Objectives,
    string Timer, string Hint, Color Colour, bool Locatable);

/// <summary>The incoming-transmission cue at the top of the tracker ("INCOMING · FLIGHT DIRECTOR MOREAU"), or null.</summary>
public sealed record TrackerBanner(string Text, string Detail, Color Colour);

/// <summary>
/// The objective tracker (docs/missions-contract.md C, the Quests doc's Presentation "Objective tracker"; render-only):
/// top right under the resource bar on the surface, hanging from the top of the viewscreen in orbit. The main mission
/// and the active side jobs (title, stage, objectives with progress, timer, one hint line), side offers waiting for an
/// answer, and above them the incoming-transmission cue. Click a mission = the driver flies the camera to its marker
/// (or opens its comms card); – collapses it to a chip, the chip expands it again. Built from HudKit's look; rebuilt only
/// when the text it shows changes (timers: once a second).
/// </summary>
public partial class MissionTracker : Control
{
    public event Action<string>? EntryPicked;
    public event Action? BannerPicked;
    public event Action? CollapseToggled;

    private PanelContainer? _panel;
    private string _sig = "";
    private float _s = 1f;
    private float _pulse;
    private Control? _bannerNode;

    public bool IsOverUi(Vector2 screenPos) => Visible && _panel != null && _panel.Visible && _panel.GetGlobalRect().HasPoint(screenPos);

    /// <summary>Where it is now (empty when hidden).</summary>
    public Rect2 PanelRect => Visible && _panel != null && _panel.Visible ? _panel.GetGlobalRect() : new Rect2();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        if (_bannerNode == null || !GodotObject.IsInstanceValid(_bannerNode)) return;
        _pulse += (float)delta;
        _bannerNode.Modulate = new Color(1, 1, 1, 0.78f + 0.22f * Mathf.Sin(_pulse * 4.2f));
    }

    /// <summary>Once a frame. Entries null or empty and no banner = hidden. Anchor = the point the panel hangs from (its
    /// top-right corner, or top-centre when <paramref name="centred"/>); maxHeight caps it; collapsed = the chip only.</summary>
    public void Refresh(IReadOnlyList<TrackerEntry>? entries, TrackerBanner? banner, Vector2 anchor, bool centred, float maxHeight,
        float scale, bool collapsed, string chip)
    {
        var any = (entries != null && entries.Count > 0) || banner != null;
        Visible = any;
        if (!any) return;
        var sig = Signature(entries, banner, collapsed, chip, scale);
        if (sig != _sig || _panel == null)
        {
            _sig = sig;
            _s = scale;
            Rebuild(entries ?? Array.Empty<TrackerEntry>(), banner, collapsed, chip);
        }
        if (_panel == null) return;
        _panel.Size = Vector2.Zero; // shrink to the content
        var size = _panel.Size;
        if (size.Y > maxHeight) size = _panel.Size = new Vector2(size.X, maxHeight);
        _panel.Position = centred ? new Vector2(anchor.X - size.X / 2f, anchor.Y) - GlobalPosition : new Vector2(anchor.X - size.X, anchor.Y) - GlobalPosition;
    }

    private static string Signature(IReadOnlyList<TrackerEntry>? entries, TrackerBanner? banner, bool collapsed, string chip, float scale)
    {
        var sb = new StringBuilder();
        sb.Append(collapsed).Append('|').Append(chip).Append('|').Append(scale.ToString("0.00")).Append('|');
        if (banner != null) sb.Append(banner.Text).Append(banner.Detail).Append(banner.Colour.ToHtml());
        if (entries != null)
            foreach (var e in entries)
            {
                sb.Append('#').Append(e.MissionId).Append(e.Tag).Append(e.Title).Append(e.Stage).Append(e.Timer).Append(e.Hint).Append(e.Locatable);
                foreach (var o in e.Objectives) sb.Append(';').Append(o.Text).Append(o.Done).Append(o.Optional).Append(o.Progress);
            }
        return sb.ToString();
    }

    private int F(float size) => Mathf.Max(8, Mathf.RoundToInt(size * _s));
    private float P(float px) => px * _s;

    private void Rebuild(IReadOnlyList<TrackerEntry> entries, TrackerBanner? banner, bool collapsed, string chip)
    {
        if (_panel != null)
        {
            RemoveChild(_panel);
            _panel.QueueFree();
        }
        _bannerNode = null;
        var width = collapsed ? 0f : P(300);
        _panel = HudKit.PanelBox(new Color(0.02f, 0.03f, 0.07f, 0.9f), HudKit.Line, P(8), P(10), P(8));
        _panel.MouseFilter = MouseFilterEnum.Stop; // clicks here never reach the map
        _panel.ClipContents = true;
        AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width, 0) };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(6)));
        _panel.AddChild(column);

        if (banner != null)
        {
            var b = new Button { FocusMode = FocusModeEnum.None, Flat = false, Alignment = HorizontalAlignment.Left, ClipText = true, TooltipText = banner.Detail };
            HudKit.StyleTile(b, P(5), P(6));
            b.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(banner.Colour, 0.16f), banner.Colour, P(5), P(8), P(4)));
            b.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(banner.Colour, 0.26f), banner.Colour.Lightened(0.2f), P(5), P(8), P(4)));
            b.AddThemeFontOverride("font", HudKit.Condensed(true));
            b.AddThemeFontSizeOverride("font_size", F(13));
            b.AddThemeColorOverride("font_color", banner.Colour.Lightened(0.35f));
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            b.Text = banner.Text;
            b.CustomMinimumSize = new Vector2(collapsed ? 0 : width, P(28));
            b.Pressed += () => BannerPicked?.Invoke();
            column.AddChild(b);
            _bannerNode = b;
        }

        if (collapsed)
        {
            if (entries.Count == 0) return;
            var c = new Button { FocusMode = FocusModeEnum.None, Text = chip, TooltipText = "Show the mission tracker" };
            HudKit.StyleTile(c, P(5), P(6));
            c.AddThemeFontOverride("font", HudKit.Condensed(true));
            c.AddThemeFontSizeOverride("font_size", F(13));
            c.Pressed += () => CollapseToggled?.Invoke();
            column.AddChild(c);
            return;
        }
        if (entries.Count == 0) return;

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var caption = HudKit.Label("MISSIONS", 12 * _s, HudKit.Dim, condensed: true, bold: true);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(caption);
        head.AddChild(HudKit.Label("N  LOG", 11 * _s, HudKit.Dim with { A = 0.7f }, condensed: true));
        var collapse = new Button { Text = "–", FocusMode = FocusModeEnum.None, Flat = true, TooltipText = "Collapse to a chip", CustomMinimumSize = new Vector2(P(22), P(18)) };
        collapse.AddThemeFontSizeOverride("font_size", F(14));
        collapse.AddThemeColorOverride("font_color", HudKit.Dim);
        collapse.Pressed += () => CollapseToggled?.Invoke();
        head.AddChild(collapse);
        column.AddChild(head);

        foreach (var e in entries) column.AddChild(EntryNode(e, width));
    }

    private Control EntryNode(TrackerEntry e, float width)
    {
        // A panel the size of its content that acts as a button: the click flies the camera (or opens the card).
        var normal = HudKit.Box(new Color(1, 1, 1, 0.025f), new Color(e.Colour, 0.35f), P(5), 0, 0);
        var hover = HudKit.Box(new Color(0.08f, 0.13f, 0.25f, 0.9f), HudKit.HoverEdge, P(5), 0, 0);
        var button = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        button.AddThemeStyleboxOverride("panel", normal);
        button.TooltipText = e.Locatable ? "Click: show it on the map" : "Click: its briefing";
        button.MouseEntered += () => button.AddThemeStyleboxOverride("panel", hover);
        button.MouseExited += () => button.AddThemeStyleboxOverride("panel", normal);
        var id = e.MissionId;
        button.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            Sfx.Ui("click");
            button.AcceptEvent();
            EntryPicked?.Invoke(id);
        };

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", Mathf.RoundToInt(P(8)));
        foreach (var side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", Mathf.RoundToInt(P(5)));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(2)));
        margin.AddChild(box);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(6)));
        var tag = HudKit.PanelBox(new Color(e.Colour, 0.18f), new Color(e.Colour, 0.6f), P(3), P(4), 0);
        tag.AddChild(HudKit.Label(e.Tag, 10 * _s, e.Colour.Lightened(0.3f), condensed: true, bold: true));
        top.AddChild(tag);
        var title = HudKit.Label(e.Title.ToUpperInvariant(), 14 * _s, HudKit.Ink, condensed: true, bold: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(title);
        if (e.Timer.Length > 0) top.AddChild(HudKit.Label(e.Timer, 12 * _s, HudKit.Warn, condensed: true, bold: true));
        box.AddChild(top);

        var inner = width - P(16);
        if (e.Stage.Length > 0) box.AddChild(Wrapped(e.Stage, 12 * _s, HudKit.Sky, inner));
        foreach (var o in e.Objectives)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(5)));
            row.AddChild(HudKit.Label(o.Done ? "✓" : "○", 12 * _s, o.Done ? HudKit.Good : HudKit.Dim));
            var progressWidth = o.Progress.Length > 0 ? P(58) : 0f;
            var text = Wrapped((o.Optional ? "(bonus) " : "") + o.Text, 12 * _s, o.Done ? HudKit.Dim : HudKit.Ink, inner - P(18) - progressWidth);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(text);
            if (o.Progress.Length > 0)
            {
                var p = HudKit.Label(o.Progress, 12 * _s, o.Done ? HudKit.Good : HudKit.Accent, condensed: true, bold: true);
                p.HorizontalAlignment = HorizontalAlignment.Right;
                p.CustomMinimumSize = new Vector2(progressWidth, 0);
                row.AddChild(p);
            }
            box.AddChild(row);
            if (o.Fraction >= 0f && !o.Done)
            {
                var back = new ColorRect { Color = new Color(1, 1, 1, 0.08f), CustomMinimumSize = new Vector2(inner, P(3)), MouseFilter = MouseFilterEnum.Ignore };
                back.AddChild(new ColorRect { Color = HudKit.Accent, Size = new Vector2(inner * Mathf.Clamp(o.Fraction, 0f, 1f), P(3)), MouseFilter = MouseFilterEnum.Ignore });
                box.AddChild(back);
            }
        }
        if (e.Hint.Length > 0) box.AddChild(Wrapped(e.Hint, 11 * _s, HudKit.Dim, inner));

        button.AddChild(margin);
        button.CustomMinimumSize = new Vector2(width, 0);
        return button;
    }

    private static Label Wrapped(string text, float size, Color colour, float width)
    {
        var l = HudKit.Label(text, size, colour);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(Mathf.Max(40f, width), 0);
        return l;
    }
}
