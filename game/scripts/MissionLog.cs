using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FinalFrontier;

/// <summary>One mission in the log's list. Group 0 active (incl. offers), 1 completed, 2 failed, 3 declined; When =
/// "T+12:04" (started, or when it ended); Tag = MAIN / SIDE / OFFER.</summary>
public sealed record MissionLogEntry(string Id, string Title, string Tag, int Group, Color Colour, string When);

/// <summary>The picked mission's page: title, "MAIN · ACTIVE · FLIGHT DIRECTOR MOREAU", stage, briefing, the current
/// objectives, the debrief (once done), the rewards (Paid = already paid out) and every comms line about it.</summary>
public sealed record MissionLogDetail(string Id, string Title, string Subtitle, string Stage, string Briefing,
    IReadOnlyList<MissionObjectiveLine> Objectives, string Debrief, IReadOnlyList<string> Rewards, bool Paid,
    IReadOnlyList<(string Stamp, string Who, string Text)> Messages, bool Locatable);

/// <summary>
/// The mission log (N; docs/missions-contract.md C, the Quests doc's "Quest log"; render-only): a centred HudKit panel
/// with four tabs (ACTIVE / COMPLETED / FAILED / DECLINED), the missions of the picked tab on the left and the picked
/// mission's page on the right (briefing, objectives, debrief, rewards, its comms lines) to re-read; SHOW ON MAP flies
/// the camera to its marker. Esc / N / × close it. The game keeps running underneath.
/// </summary>
public partial class MissionLog : Control
{
    public event Action? Closed;
    public event Action<string>? Locate;

    private static readonly string[] Tabs = { "ACTIVE", "COMPLETED", "FAILED", "DECLINED" };

    private PanelContainer? _panel;
    private string _sig = "";
    private float _s = 1f;
    private int _tab;
    private string? _picked;

    public bool IsOpen => Visible;

    /// <summary>The mission whose page is shown (the driver builds its detail).</summary>
    public string? Picked => _picked;

    public bool IsOverUi(Vector2 screenPos) => Visible && _panel != null && _panel.GetGlobalRect().HasPoint(screenPos);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    /// <summary>Opens on the tab of <paramref name="group"/> (0–3) with that mission picked, or as it was left.</summary>
    public void Open(string? missionId = null, int group = -1)
    {
        Visible = true;
        if (missionId != null) _picked = missionId;
        if (group >= 0) _tab = Mathf.Clamp(group, 0, Tabs.Length - 1);
        _sig = "";
        Sfx.Ui("open");
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Sfx.Ui("close");
        Closed?.Invoke();
    }

    /// <summary>Once a frame while open: the list (every group) and the picked mission's page (null = none picked or gone).</summary>
    public void Refresh(IReadOnlyList<MissionLogEntry> entries, MissionLogDetail? detail, Rect2 area, float scale)
    {
        if (!Visible) return;
        // Keep a valid pick: the first of the tab when nothing (or something from another tab) is picked.
        var inTab = false;
        string? first = null;
        foreach (var e in entries)
        {
            if (e.Group != _tab) continue;
            first ??= e.Id;
            if (e.Id == _picked) inTab = true;
        }
        if (!inTab) _picked = first;

        var sig = Signature(entries, detail, scale);
        if (sig != _sig || _panel == null)
        {
            _sig = sig;
            _s = scale;
            Rebuild(entries, detail, area);
        }
        if (_panel == null) return;
        _panel.Size = Vector2.Zero;
        var size = _panel.Size;
        _panel.Position = new Vector2(area.Position.X + (area.Size.X - size.X) / 2f, area.Position.Y + Mathf.Max(0f, (area.Size.Y - size.Y) / 2f)) - GlobalPosition;
    }

    private string Signature(IReadOnlyList<MissionLogEntry> entries, MissionLogDetail? d, float scale)
    {
        var sb = new StringBuilder();
        sb.Append(_tab).Append('|').Append(_picked).Append('|').Append(scale.ToString("0.00"));
        foreach (var e in entries) sb.Append('#').Append(e.Id).Append(e.Tag).Append(e.Group).Append(e.When);
        if (d != null)
        {
            sb.Append('@').Append(d.Id).Append(d.Subtitle).Append(d.Stage).Append(d.Debrief).Append(d.Paid).Append(d.Messages.Count).Append(d.Locatable);
            foreach (var o in d.Objectives) sb.Append(';').Append(o.Text).Append(o.Done).Append(o.Progress);
        }
        return sb.ToString();
    }

    private int F(float size) => Mathf.Max(8, Mathf.RoundToInt(size * _s));
    private float P(float px) => px * _s;

    private void Rebuild(IReadOnlyList<MissionLogEntry> entries, MissionLogDetail? d, Rect2 area)
    {
        if (_panel != null)
        {
            RemoveChild(_panel);
            _panel.QueueFree();
        }
        var width = Mathf.Min(P(780), area.Size.X - P(24));
        var height = Mathf.Min(P(500), area.Size.Y - P(16));
        _panel = HudKit.PanelBox(new Color(0.02f, 0.03f, 0.07f, 0.97f), HudKit.Line, P(10), P(16), P(12));
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width, height) };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(8)));
        _panel.AddChild(column);

        // Header: caption, tabs, ×.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(6)));
        var caption = HudKit.Label("MISSION LOG", 18 * _s, HudKit.Accent, condensed: true, bold: true);
        caption.CustomMinimumSize = new Vector2(P(150), 0);
        head.AddChild(caption);
        for (var i = 0; i < Tabs.Length; i++)
        {
            var count = 0;
            foreach (var e in entries) if (e.Group == i) count++;
            var index = i;
            var tab = new Button { Text = $"{Tabs[i]} {count}", FocusMode = FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(0, P(28)) };
            HudKit.StyleTile(tab, P(5), P(6));
            tab.AddThemeFontOverride("font", HudKit.Condensed(true));
            tab.AddThemeFontSizeOverride("font_size", F(13));
            tab.SetPressedNoSignal(i == _tab);
            tab.Pressed += () =>
            {
                _tab = index;
                _picked = null;
                _sig = "";
            };
            head.AddChild(tab);
        }
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        head.AddChild(HudKit.Label("N / Esc close", 11 * _s, HudKit.Dim, condensed: true));
        var close = HudKit.ActionButton("×", false, _s);
        close.TooltipText = "Close (N / Esc)";
        Sfx.Tag(close, "back");
        close.Pressed += Close;
        head.AddChild(close);
        column.AddChild(head);
        column.AddChild(new ColorRect { Color = HudKit.Line, CustomMinimumSize = new Vector2(0, Mathf.Max(1f, P(1))), MouseFilter = MouseFilterEnum.Ignore });

        var body = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(12)));
        column.AddChild(body);

        // Left: the tab's missions.
        var listScroll = new ScrollContainer { CustomMinimumSize = new Vector2(P(230), 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(4)));
        listScroll.AddChild(list);
        body.AddChild(listScroll);
        var any = false;
        foreach (var e in entries)
        {
            if (e.Group != _tab) continue;
            any = true;
            var id = e.Id;
            var row = new Button { FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, ClipText = true, ToggleMode = true,
                Text = $"{e.Tag}  ·  {e.Title.ToUpperInvariant()}", TooltipText = $"{e.Title} · {e.When}", CustomMinimumSize = new Vector2(0, P(30)) };
            HudKit.StyleTile(row, P(5), P(6));
            row.AddThemeFontOverride("font", HudKit.Condensed(true));
            row.AddThemeFontSizeOverride("font_size", F(13));
            row.AddThemeColorOverride("font_color", e.Colour.Lightened(0.2f));
            row.SetPressedNoSignal(id == _picked);
            row.Pressed += () =>
            {
                _picked = id;
                _sig = "";
            };
            list.AddChild(row);
        }
        if (!any)
        {
            var none = HudKit.Label(_tab switch
            {
                0 => "No missions yet. Mission Control will be in touch.",
                1 => "Nothing completed yet.",
                2 => "Nothing failed.",
                _ => "Nothing declined.",
            }, 13 * _s, HudKit.Dim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            none.CustomMinimumSize = new Vector2(P(220), 0);
            list.AddChild(none);
        }

        // Right: the picked mission's page.
        var pageScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var page = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(6)));
        pageScroll.AddChild(page);
        body.AddChild(pageScroll);
        var inner = width - P(230) - P(12) - P(14); // the page column, less a scroll bar
        if (d == null || !any) return;

        page.AddChild(Wrapped(d.Title.ToUpperInvariant(), 20 * _s, HudKit.Ink, inner, true, true));
        page.AddChild(Wrapped(d.Subtitle, 12 * _s, HudKit.Sky, inner, true, true));
        if (d.Stage.Length > 0) page.AddChild(Wrapped(d.Stage, 13 * _s, HudKit.Accent, inner));
        if (d.Locatable)
        {
            var locate = HudKit.ActionButton("SHOW ON MAP", false, _s);
            locate.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            var id = d.Id;
            locate.Pressed += () => Locate?.Invoke(id);
            page.AddChild(locate);
        }
        if (d.Briefing.Length > 0)
        {
            page.AddChild(Caption("BRIEFING"));
            page.AddChild(Wrapped(d.Briefing, 14 * _s, HudKit.Ink, inner));
        }
        if (d.Objectives.Count > 0)
        {
            page.AddChild(Caption("OBJECTIVES"));
            foreach (var o in d.Objectives)
            {
                var text = (o.Done ? "✓ " : "○ ") + (o.Optional ? "(bonus) " : "") + o.Text + (o.Progress.Length > 0 ? $"   {o.Progress}" : "");
                page.AddChild(Wrapped(text, 13 * _s, o.Done ? HudKit.Good : HudKit.Ink, inner));
            }
        }
        if (d.Debrief.Length > 0)
        {
            page.AddChild(Caption("DEBRIEF"));
            page.AddChild(Wrapped(d.Debrief, 14 * _s, HudKit.Ink, inner));
        }
        if (d.Rewards.Count > 0)
        {
            page.AddChild(Caption(d.Paid ? "REWARDS PAID" : "REWARD"));
            page.AddChild(Wrapped(string.Join("  ·  ", d.Rewards), 13 * _s, HudKit.Good, inner));
        }
        if (d.Messages.Count > 0)
        {
            page.AddChild(Caption("COMMS"));
            foreach (var (stamp, who, text) in d.Messages)
                page.AddChild(Wrapped($"{stamp}  {who}: {text}", 12 * _s, HudKit.Dim, inner));
        }
    }

    private Label Caption(string text) => HudKit.Label(text, 11 * _s, HudKit.Dim, condensed: true, bold: true);

    private static Label Wrapped(string text, float size, Color colour, float width, bool condensed = false, bool bold = false)
    {
        var l = HudKit.Label(text, size, colour, condensed, bold);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(Mathf.Max(40f, width), 0);
        return l;
    }
}
