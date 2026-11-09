using System;
using Godot;

namespace FinalFrontier;

/// <summary>One tutorial step as the card shows it (docs/menu-contract.md). <see cref="Anchor"/> = the screen rect to
/// point at (empty = none); <see cref="NextText"/> null when the step waits for the player to do something.</summary>
public sealed record TutorialStep(string Id, string Title, string Body, string StepLabel, Rect2 Anchor, string? NextText);

/// <summary>
/// The tutorial popup card in the HUD's own look (HudKit): a "TUTORIAL · 3 / 10" caption, title, body, an optional hint
/// line, GOT IT (when the step has <see cref="TutorialStep.NextText"/>) and a SKIP TUTORIAL link. With an anchor it sits
/// beside that rect and draws an arrow to it and a pulsing outline round it; without one it sits at the top middle of
/// <see cref="SetArea"/> (default: the whole screen). The card itself is a full-screen Control that ignores the mouse;
/// only its panel takes clicks, so the game underneath plays on. Sizes scale from 1280×720 (<see cref="HudKit.ScaleFor"/>).
/// </summary>
public partial class TutorialCard : Control
{
    public event Action? Next;
    public event Action? SkipAll;

    private TutorialStep? _step;
    private string? _hint;
    private Rect2 _anchor;
    private Rect2? _area;

    private PanelContainer? _panel;
    private Label? _caption, _count, _title, _body, _hintLabel, _waiting;
    private Button? _next, _skip;
    private float _s = -1f;
    private float _width;
    private bool _resize = true;
    private bool _placed;
    private Vector2 _pos;
    private float _fade;
    private double _time;

    /// <summary>Open (or re-use) the card under <paramref name="parent"/> showing <paramref name="step"/>.</summary>
    public static TutorialCard Open(Node parent, TutorialStep step)
    {
        foreach (var child in parent.GetChildren())
            if (child is TutorialCard existing && !existing.IsQueuedForDeletion())
            {
                existing.Show(step);
                return existing;
            }
        var card = new TutorialCard { Name = "TutorialCard" };
        parent.AddChild(card);
        card.Show(step);
        return card;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Show another step on this card (a new id fades it in again; the same id only updates the texts).</summary>
    public void Show(TutorialStep step)
    {
        var fresh = _step == null || _step.Id != step.Id;
        _step = step;
        _anchor = step.Anchor;
        if (fresh)
        {
            _fade = 0f;
            _placed = false;
        }
        Fill();
        Visible = true;
    }

    /// <summary>Move the arrow to a new anchor (e.g. after a resize). An empty rect = no arrow.</summary>
    public void SetAnchor(Rect2 anchor) => _anchor = anchor;

    /// <summary>A short extra line under the body (what to do next right now), or null.</summary>
    public void SetHint(string? hint)
    {
        if (hint == _hint) return;
        _hint = hint;
        Fill();
    }

    /// <summary>Where the card may sit when it has no anchor (screen rect; null = the whole screen).</summary>
    public void SetArea(Rect2? area) => _area = area;

    /// <summary>True over the card's panel (the rest of the screen stays the game's).</summary>
    public bool IsOverUi(Vector2 screenPos) =>
        IsVisibleInTree() && _panel != null && _panel.Visible && _panel.GetGlobalRect().HasPoint(screenPos);

    public void Close() => QueueFree();

    // ------------------------------------------------------------------ building

    private void Build(float s)
    {
        _s = s;
        if (_panel != null)
        {
            RemoveChild(_panel);
            _panel.QueueFree();
        }
        _width = Mathf.Round(340 * s);
        var padX = 16 * s;
        var padY = 13 * s;
        var box = HudKit.Box(HudKit.Panel, HudKit.Accent, 8 * s, padX, padY);
        box.BorderWidthLeft = Mathf.Max(2, Mathf.RoundToInt(4 * s));
        box.ShadowColor = new Color(0, 0, 0, 0.45f);
        box.ShadowSize = Mathf.RoundToInt(10 * s);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Name = "Panel" };
        _panel.AddThemeStyleboxOverride("panel", box);
        AddChild(_panel);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        _panel.AddChild(column);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _caption = HudKit.Label("TUTORIAL", 11 * s, HudKit.Accent, condensed: true, bold: true);
        head.AddChild(_caption);
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _count = HudKit.Label("", 11 * s, HudKit.Dim, condensed: true);
        head.AddChild(_count);
        column.AddChild(head);

        var inner = _width - 2 * padX - box.BorderWidthLeft;
        _title = HudKit.Label("", 20 * s, HudKit.Ink, bold: true);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.CustomMinimumSize = new Vector2(inner, 0);
        column.AddChild(_title);

        _body = HudKit.Label("", 13 * s, new Color(0.8f, 0.84f, 0.9f));
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.CustomMinimumSize = new Vector2(inner, 0);
        column.AddChild(_body);

        _hintLabel = HudKit.Label("", 13 * s, HudKit.Sky, condensed: true, bold: true);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hintLabel.CustomMinimumSize = new Vector2(inner, 0);
        column.AddChild(_hintLabel);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2 * s), MouseFilter = MouseFilterEnum.Ignore });
        var foot = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        foot.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        _skip = new Button { Text = "SKIP TUTORIAL", Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
        _skip.AddThemeFontOverride("font", HudKit.Condensed());
        _skip.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11 * s));
        _skip.AddThemeColorOverride("font_color", HudKit.Dim);
        _skip.AddThemeColorOverride("font_hover_color", HudKit.Ink);
        _skip.AddThemeColorOverride("font_pressed_color", HudKit.Accent);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" }) _skip.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        _skip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _skip.Pressed += () => SkipAll?.Invoke();
        foot.AddChild(_skip);
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _waiting = HudKit.Label("continues when you do it", 11 * s, HudKit.Dim, condensed: true);
        _waiting.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foot.AddChild(_waiting);
        _next = HudKit.ActionButton("GOT IT", true, s);
        _next.CustomMinimumSize = new Vector2(96 * s, 30 * s);
        _next.Pressed += () => Next?.Invoke();
        foot.AddChild(_next);
        column.AddChild(foot);
        _resize = true;
        _placed = false;
    }

    private void Fill()
    {
        if (_step == null || _title == null || _body == null || _count == null || _hintLabel == null || _next == null || _waiting == null) return;
        _count.Text = _step.StepLabel;
        _title.Text = _step.Title.ToUpperInvariant();
        _body.Text = _step.Body.Replace("\r", "");
        _hintLabel.Text = _hint ?? "";
        _hintLabel.Visible = !string.IsNullOrEmpty(_hint);
        var manual = !string.IsNullOrEmpty(_step.NextText);
        _next.Visible = manual;
        _next.Text = manual ? _step.NextText! : "";
        _waiting.Visible = !manual;
        _resize = true; // shrink to the new text
    }

    // ------------------------------------------------------------------ layout and drawing

    public override void _Process(double delta)
    {
        if (!Visible || _step == null) return;
        var screen = GetViewportRect().Size;
        var s = HudKit.ScaleFor(screen);
        if (!Mathf.IsEqualApprox(s, _s))
        {
            Build(s);
            Fill();
        }
        if (_panel == null) return;
        _time += delta;
        if (_resize)
        {
            _resize = false;
            _panel.Size = new Vector2(_width, 0); // grows back to the content's minimum height
        }
        var size = new Vector2(_width, Mathf.Max(_panel.Size.Y, _panel.GetCombinedMinimumSize().Y));
        var target = Place(size, screen);
        _pos = _placed ? _pos.Lerp(target, Mathf.Min(1f, (float)delta * 10f)) : target;
        _placed = true;
        _fade = Mathf.Min(1f, _fade + (float)delta * 5f);
        var slide = (1f - _fade) * 10f * _s;
        _panel.Position = _pos - GlobalPosition + new Vector2(0, slide);
        _panel.Modulate = new Color(1, 1, 1, _fade);
        QueueRedraw();
    }

    private bool HasAnchor => _anchor.Size.X >= 2f && _anchor.Size.Y >= 2f;

    /// <summary>Where the card goes (screen pixels): beside the anchor without covering it, else top middle of the area.</summary>
    private Vector2 Place(Vector2 size, Vector2 screen)
    {
        var margin = 12f * _s;
        var bounds = new Rect2(margin, margin, screen.X - 2 * margin, screen.Y - 2 * margin);
        if (!HasAnchor)
        {
            var area = _area is { } a && a.Size.X > size.X && a.Size.Y > size.Y ? a : new Rect2(Vector2.Zero, screen);
            var p = new Vector2(area.Position.X + (area.Size.X - size.X) / 2f, area.Position.Y + Mathf.Max(margin, area.Size.Y * 0.06f));
            return Clamp(p, size, bounds);
        }
        var gap = 40f * _s;
        var r = _anchor;
        var c = r.GetCenter();
        Vector2[] above = { new(c.X - size.X / 2f, r.Position.Y - gap - size.Y) };
        Vector2[] below = { new(c.X - size.X / 2f, r.End.Y + gap) };
        Vector2[] left = { new(r.Position.X - gap - size.X, c.Y - size.Y / 2f) };
        Vector2[] right = { new(r.End.X + gap, c.Y - size.Y / 2f) };
        var lower = c.Y > screen.Y * 0.5f;
        var rightSide = c.X > screen.X * 0.5f;
        var order = lower
            ? new[] { above, rightSide ? left : right, rightSide ? right : left, below }
            : new[] { below, rightSide ? left : right, rightSide ? right : left, above };
        var keepOff = r.Grow(6f * _s);
        foreach (var option in order)
        {
            var p = Clamp(option[0], size, bounds);
            if (!new Rect2(p, size).Intersects(keepOff)) return p;
        }
        return Clamp(order[0][0], size, bounds);
    }

    private static Vector2 Clamp(Vector2 p, Vector2 size, Rect2 bounds) => new(
        Mathf.Clamp(p.X, bounds.Position.X, Mathf.Max(bounds.Position.X, bounds.End.X - size.X)),
        Mathf.Clamp(p.Y, bounds.Position.Y, Mathf.Max(bounds.Position.Y, bounds.End.Y - size.Y)));

    public override void _Draw()
    {
        if (_panel == null || !HasAnchor || _fade <= 0.01f) return;
        var pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 4.2f);
        var accent = HudKit.Accent;
        var origin = GlobalPosition;
        var outline = new Rect2(_anchor.Position - origin, _anchor.Size).Grow((4f + 3f * pulse) * _s);
        var width = Mathf.Max(1.5f, 2f * _s);
        DrawRect(outline, accent with { A = 0.06f + 0.06f * pulse }, true);
        DrawRect(outline, accent with { A = (0.55f + 0.45f * pulse) * _fade }, false, width);
        DrawRect(outline.Grow(4f * _s), accent with { A = 0.18f * (1f - pulse) * _fade }, false, width);

        // Arrow from the card's nearest edge to the outline's nearest point.
        var card = new Rect2(_panel.Position, _panel.Size);
        var from = Nearest(card, outline.GetCenter());
        var to = Nearest(outline.Grow(3f * _s), from);
        var dir = to - from;
        var length = dir.Length();
        if (length < 14f * _s) return;
        dir /= length;
        var colour = accent with { A = 0.9f * _fade };
        var head = 11f * _s;
        var tip = to;
        var back = tip - dir * head;
        var side = new Vector2(-dir.Y, dir.X) * head * 0.55f;
        DrawLine(from, back, colour, Mathf.Max(2f, 2.5f * _s), true);
        DrawColoredPolygon(new[] { tip, back + side, back - side }, colour);
        DrawCircle(from, 3.2f * _s, colour);
    }

    private static Vector2 Nearest(Rect2 r, Vector2 p) =>
        new(Mathf.Clamp(p.X, r.Position.X, r.End.X), Mathf.Clamp(p.Y, r.Position.Y, r.End.Y));
}
