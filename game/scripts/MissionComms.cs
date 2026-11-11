using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FinalFrontier;

/// <summary>A button on the comms card: Id is handed back (a mission option id, "close", "next", "log"); Primary = the
/// accent edge (ACCEPT); Enabled false greys it with Tooltip saying why.</summary>
public sealed record CommsButton(string Id, string Text, bool Primary, bool Enabled = true, string Tooltip = "");

/// <summary>
/// What the comms card shows (built by the driver, PlanetDriver.Missions.cs). Kicker = "INCOMING TRANSMISSION" /
/// "MISSION BRIEFING" / "MISSION COMPLETE" / "MISSION FAILED" / "CHAPTER COMPLETE"; Giver = the speaker's name,
/// Initials + Colour = the portrait placeholder; Stamp = "T+12:04"; Title = the mission's; Body = the message, briefing or
/// debrief; Objectives = the current stage's (may be empty); Rewards = what it pays / paid ("" header when empty);
/// Stats = rows for a chapter debrief (Large = the wider card); Footer = one dim line ("2 more waiting · Y next").
/// </summary>
public sealed record CommsCardView(string Key, string Kicker, string Giver, string Initials, Color Colour, string Stamp,
    string Title, string Subtitle, string Body, IReadOnlyList<MissionObjectiveLine> Objectives, string RewardsCaption,
    IReadOnlyList<string> Rewards, IReadOnlyList<(string Label, string Value)> Stats, IReadOnlyList<CommsButton> Buttons,
    string Footer, bool Large);

/// <summary>
/// The incoming-transmission / briefing / debrief card (docs/missions-contract.md C, the Quests doc's "COMMS display" and
/// "Comms pop-over on the surface", trimmed; render-only). One HudKit panel centred in the area the driver gives (between
/// the top bar and the bottom panel on the surface, the helm's open viewscreen in orbit): a kicker line, the portrait
/// placeholder (the giver's initials in a framed square) with name and stamp, the mission title, the text, the stage's
/// objectives, rewards, a chapter debrief's stats, and the buttons (ACCEPT / DECLINE / branch choices / NEXT / CLOSE).
/// It never pauses the game and the map still takes clicks outside it. Rebuilt only when what it shows changes.
/// </summary>
public partial class MissionComms : Control
{
    public event Action<string, string>? ButtonPicked; // (card key, button id)

    private PanelContainer? _panel;
    private string _sig = "";
    private string _key = "";
    private float _s = 1f;
    private float _age;

    public bool IsOpen => Visible && _panel != null;

    public bool IsOverUi(Vector2 screenPos) => IsOpen && _panel!.GetGlobalRect().HasPoint(screenPos);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_panel == null || !Visible) return;
        // A short fade-in after each new card.
        _age += (float)delta;
        _panel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_age / 0.18f, 0f, 1f));
    }

    /// <summary>Once a frame: null hides it. Area = where it may sit (viewport pixels); it is centred there, nudged to
    /// the upper third.</summary>
    public void Refresh(CommsCardView? card, Rect2 area, float scale)
    {
        Visible = card != null;
        if (card == null)
        {
            _key = "";
            return;
        }
        var sig = Signature(card, scale);
        if (sig != _sig || _panel == null)
        {
            if (card.Key != _key) _age = 0f;
            _sig = sig;
            _key = card.Key;
            _s = scale;
            Rebuild(card, area);
        }
        if (_panel == null) return;
        _panel.Size = Vector2.Zero;
        var size = _panel.Size;
        var x = area.Position.X + (area.Size.X - size.X) / 2f;
        var y = area.Position.Y + Mathf.Max(P(12), (area.Size.Y - size.Y) * 0.35f);
        if (y + size.Y > area.End.Y) y = Mathf.Max(area.Position.Y, area.End.Y - size.Y);
        _panel.Position = new Vector2(x, y) - GlobalPosition;
    }

    private static string Signature(CommsCardView c, float scale)
    {
        var sb = new StringBuilder();
        sb.Append(c.Key).Append('|').Append(scale.ToString("0.00")).Append('|').Append(c.Kicker).Append(c.Giver).Append(c.Stamp)
            .Append(c.Title).Append(c.Subtitle).Append(c.Body).Append(c.RewardsCaption).Append(c.Footer).Append(c.Large);
        foreach (var o in c.Objectives) sb.Append(';').Append(o.Text).Append(o.Done).Append(o.Progress);
        foreach (var r in c.Rewards) sb.Append('$').Append(r);
        foreach (var (l, v) in c.Stats) sb.Append('%').Append(l).Append(v);
        foreach (var b in c.Buttons) sb.Append('!').Append(b.Id).Append(b.Text).Append(b.Enabled).Append(b.Tooltip);
        return sb.ToString();
    }

    private int F(float size) => Mathf.Max(8, Mathf.RoundToInt(size * _s));
    private float P(float px) => px * _s;

    private void Rebuild(CommsCardView c, Rect2 area)
    {
        if (_panel != null)
        {
            RemoveChild(_panel);
            _panel.QueueFree();
        }
        // Never wider than the area (a narrow helm viewscreen at 1280 with the data panel open).
        var width = Mathf.Min(P(c.Large ? 640 : 520), Mathf.Max(P(300), area.Size.X - P(24)));
        _panel = HudKit.PanelBox(new Color(0.02f, 0.03f, 0.07f, 0.96f), new Color(c.Colour, 0.85f), P(10), P(18), P(14));
        _panel.MouseFilter = MouseFilterEnum.Stop; // clicks on the card never reach the map or the globe
        AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width, 0) };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(8)));
        _panel.AddChild(column);
        var inner = width;

        // Kicker + stamp.
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var kicker = HudKit.Label(c.Kicker, 12 * _s, c.Colour.Lightened(0.25f), condensed: true, bold: true);
        kicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(kicker);
        if (c.Stamp.Length > 0) top.AddChild(HudKit.Label(c.Stamp, 12 * _s, HudKit.Dim, condensed: true));
        column.AddChild(top);

        // Portrait placeholder (initials) + giver + title.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(12)));
        var portrait = HudKit.PanelBox(new Color(c.Colour, 0.14f), c.Colour, P(6), 0, 0);
        portrait.CustomMinimumSize = new Vector2(P(c.Large ? 64 : 56), P(c.Large ? 64 : 56));
        var initials = HudKit.Label(c.Initials, (c.Large ? 24 : 21) * _s, c.Colour.Lightened(0.3f), condensed: true, bold: true);
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        initials.SizeFlagsVertical = SizeFlags.ExpandFill;
        portrait.AddChild(initials);
        head.AddChild(portrait);
        var who = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        who.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(1)));
        if (c.Giver.Length > 0) who.AddChild(HudKit.Label(c.Giver.ToUpperInvariant(), 13 * _s, HudKit.Sky, condensed: true, bold: true));
        var title = Wrapped(c.Title.ToUpperInvariant(), (c.Large ? 24 : 20) * _s, HudKit.Ink, inner - P(80), condensed: true, bold: true);
        who.AddChild(title);
        if (c.Subtitle.Length > 0) who.AddChild(Wrapped(c.Subtitle, 12 * _s, HudKit.Dim, inner - P(80)));
        head.AddChild(who);
        column.AddChild(head);

        column.AddChild(Rule());
        if (c.Body.Length > 0) column.AddChild(Wrapped(c.Body, 15 * _s, HudKit.Ink, inner));

        if (c.Objectives.Count > 0)
        {
            column.AddChild(HudKit.Label("OBJECTIVES", 11 * _s, HudKit.Dim, condensed: true, bold: true));
            foreach (var o in c.Objectives)
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", Mathf.RoundToInt(P(6)));
                row.AddChild(HudKit.Label(o.Done ? "✓" : "○", 13 * _s, o.Done ? HudKit.Good : HudKit.Dim));
                var progress = o.Progress.Length > 0 ? P(64) : 0f;
                var text = Wrapped((o.Optional ? "(bonus) " : "") + o.Text, 13 * _s, o.Done ? HudKit.Dim : HudKit.Ink, inner - P(24) - progress);
                text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(text);
                if (o.Progress.Length > 0)
                {
                    var p = HudKit.Label(o.Progress, 13 * _s, o.Done ? HudKit.Good : HudKit.Accent, condensed: true, bold: true);
                    p.HorizontalAlignment = HorizontalAlignment.Right;
                    p.CustomMinimumSize = new Vector2(progress, 0);
                    row.AddChild(p);
                }
                column.AddChild(row);
            }
        }

        if (c.Rewards.Count > 0)
        {
            column.AddChild(HudKit.Label(c.RewardsCaption.Length > 0 ? c.RewardsCaption : "REWARD", 11 * _s, HudKit.Dim, condensed: true, bold: true));
            var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(inner, 0) };
            flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(P(6)));
            flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(P(4)));
            foreach (var r in c.Rewards)
            {
                var chip = HudKit.PanelBox(new Color(HudKit.Good, 0.12f), new Color(HudKit.Good, 0.5f), P(4), P(7), P(2));
                chip.AddChild(HudKit.Label(r, 12 * _s, HudKit.Good, condensed: true, bold: true));
                flow.AddChild(chip);
            }
            column.AddChild(flow);
        }

        if (c.Stats.Count > 0)
        {
            column.AddChild(Rule());
            var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(P(16)));
            grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(P(3)));
            foreach (var (label, value) in c.Stats)
            {
                grid.AddChild(HudKit.Label(label.ToUpperInvariant(), 12 * _s, HudKit.Dim, condensed: true, bold: true));
                grid.AddChild(HudKit.Label(value, 13 * _s, HudKit.Ink));
            }
            column.AddChild(grid);
        }

        // Buttons, right-aligned: choices first, CLOSE last.
        var buttons = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = FlowContainer.AlignmentMode.End, CustomMinimumSize = new Vector2(inner, 0) };
        buttons.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(P(8)));
        buttons.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(P(6)));
        var key = c.Key;
        foreach (var b in c.Buttons)
        {
            var button = HudKit.ActionButton(b.Text, b.Primary, _s);
            button.Disabled = !b.Enabled;
            button.TooltipText = b.Tooltip;
            var id = b.Id;
            if (id == "close") Sfx.Tag(button, "back");
            else if (id is "accept") Sfx.Tag(button, "confirm");
            button.Pressed += () => ButtonPicked?.Invoke(key, id);
            buttons.AddChild(button);
        }
        column.AddChild(buttons);
        if (c.Footer.Length > 0)
        {
            var foot = Wrapped(c.Footer, 11 * _s, HudKit.Dim, inner);
            foot.HorizontalAlignment = HorizontalAlignment.Right;
            column.AddChild(foot);
        }
    }

    private ColorRect Rule() => new() { Color = HudKit.Line, CustomMinimumSize = new Vector2(0, Mathf.Max(1f, P(1))), MouseFilter = MouseFilterEnum.Ignore };

    private static Label Wrapped(string text, float size, Color colour, float width, bool condensed = false, bool bold = false)
    {
        var l = HudKit.Label(text, size, colour, condensed, bold);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(Mathf.Max(40f, width), 0);
        return l;
    }
}
