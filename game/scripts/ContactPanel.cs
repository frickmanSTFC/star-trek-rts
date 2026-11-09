using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>One option at the current step, as the panel shows it.</summary>
public sealed record ContactChoiceView(string Text, string Hint, int ShockShift);

/// <summary>What the driver hands the first-contact panel each frame. Result != null = the talks are over: only the
/// title and the result line show (until the driver stops sending it).</summary>
public sealed record ContactPanelView(
    string BodyId,
    string Title,
    string Subtitle,
    int Step,
    int Steps,
    float StepSecondsLeft,
    float StepSeconds,
    float EventSecondsLeft,
    IReadOnlyList<ContactChoiceView> Choices,
    int Chosen,
    int ShockChance,
    string? Result,
    Color ResultColour);

/// <summary>
/// The first-contact event panel (civilizations phase 2, docs/civilizations-phase2-contract.md "View"), in the helm's look:
/// a black panel with a flat peach title bar and condensed caps, top-centre over the viewscreen (or below the surface
/// top bar). Step N of M with its time left (a bar that runs down; the first choice is taken when it runs out), the
/// 2–3 choices as pills with their hints and how each moves the culture-shock chance, the shock-chance meter (red =
/// culture shock, green = a smooth start as a trade partner), and a result line once the talks end. "–" folds it to the
/// title bar. Not modal: the game runs on. Rebuilt only when the step, the choices or the pick change; the timers and
/// the meter update every frame.
/// </summary>
public partial class ContactPanel : Control
{
    /// <summary>A choice pill (index) was pressed for the body.</summary>
    public event Action<string, int>? ChoicePicked;

    /// <summary>The × on the result: the driver drops the result line.</summary>
    public event Action? Dismissed;

    private PanelContainer? _panel;
    private string _key = "";
    private Vector2 _builtFor;
    private float _s = 1f;
    private bool _collapsed;
    private ContactPanelView? _view;
    private Label? _timer;
    private CivMeter? _stepMeter;
    private CivMeter? _shockMeter;
    private Label? _shockText;

    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color Grey = new(0.55f, 0.57f, 0.62f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    /// <summary>True when the mouse is over the panel (the globe / map shouldn't react).</summary>
    public bool IsOverUi(Vector2 mouse) => Visible && _panel != null && _panel.GetGlobalRect().HasPoint(mouse);

    /// <summary>Every frame: null hides it. <paramref name="top"/> = where its top edge goes (screen px), centred on <paramref name="centreX"/>.</summary>
    public void Show(ContactPanelView? view, float centreX, float top)
    {
        if (view == null)
        {
            if (Visible) Visible = false;
            _view = null;
            return;
        }
        Visible = true;
        var size = GetViewportRect().Size;
        var key = KeyOf(view);
        if (key != _key || size != _builtFor || _panel == null)
        {
            _key = key;
            _builtFor = size;
            _view = view;
            Rebuild(size);
        }
        _view = view;
        if (_panel != null)
        {
            var w = _panel.Size.X;
            _panel.Position = new Vector2(Mathf.Clamp(centreX - w / 2f, 8f, Mathf.Max(8f, size.X - w - 8f)), top);
        }
        Tick(view);
    }

    private string KeyOf(ContactPanelView v) => string.Join("|", v.BodyId, v.Title, v.Subtitle, v.Step, v.Steps, v.Chosen, v.Result ?? "", _collapsed,
        string.Join(",", v.Choices.Select(c => c.Text + c.Hint + c.ShockShift)));

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(float v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private static string Clock(float seconds)
    {
        var s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Per frame: the timers, the step bar and the shock meter.</summary>
    private void Tick(ContactPanelView v)
    {
        if (_timer != null)
            _timer.Text = v.Result != null ? "TALKS OVER"
                : $"STEP {v.Step + 1} OF {v.Steps} · {Clock(v.StepSecondsLeft)} LEFT{(v.Chosen < 0 ? " (THEN THE FIRST IS TAKEN)" : "")} · TALKS END IN {Clock(v.EventSecondsLeft)}";
        if (_stepMeter != null) _stepMeter.Fraction = v.StepSeconds > 0f ? v.StepSecondsLeft / v.StepSeconds : 0f;
        if (_shockMeter != null) _shockMeter.Fraction = v.ShockChance / 100f;
        if (_shockText != null) _shockText.Text = $"CULTURE SHOCK {v.ShockChance} %  ·  SMOOTH START {100 - v.ShockChance} %";
    }

    private void Rebuild(Vector2 screen)
    {
        if (_view == null) return;
        _panel?.QueueFree();
        _timer = null;
        _stepMeter = null;
        _shockMeter = null;
        _shockText = null;
        _s = HudKit.ScaleFor(screen);
        var v = _view;
        var colour = v.Result != null ? v.ResultColour : HelmScreen.Peach;

        var width = Mathf.Min(P(560), screen.X - 32);
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.9f), BorderColor = colour };
        style.SetBorderWidthAll(1);
        style.BorderWidthLeft = P(5);
        style.ContentMarginLeft = P(12);
        style.ContentMarginRight = P(12);
        style.ContentMarginTop = P(8);
        style.ContentMarginBottom = P(10);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);

        var root = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        root.AddThemeConstantOverride("separation", P(6));
        _panel.AddChild(root);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(6));
        var bar = Bar(colour, v.Title, F(17));
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(bar);
        if (v.Result != null) head.AddChild(Pill("x", "×", HelmScreen.Lavender, true, () => Dismissed?.Invoke()));
        else head.AddChild(Pill("fold", _collapsed ? "+" : "–", HelmScreen.Lavender, true, () =>
        {
            _collapsed = !_collapsed;
            _key = "";
        }));
        root.AddChild(head);
        root.AddChild(Text(v.Subtitle, F(13), HelmScreen.Lavender));

        if (v.Result != null)
        {
            var result = Text(v.Result, F(14), v.ResultColour);
            result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(result);
            return;
        }

        _timer = Text("", F(13), HelmScreen.Ink, true);
        root.AddChild(_timer);
        _stepMeter = new CivMeter { Colour = HelmScreen.Peach, CustomMinimumSize = new Vector2(0, P(6)) };
        root.AddChild(_stepMeter);
        if (_collapsed)
        {
            _shockText = Text("", F(12), Grey);
            root.AddChild(_shockText);
            return;
        }

        // The choices: a pill each, its hint and how it moves the shock chance beside it.
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", P(10));
        grid.AddThemeConstantOverride("v_separation", P(5));
        for (var i = 0; i < v.Choices.Count; i++)
        {
            var c = v.Choices[i];
            var index = i;
            var picked = v.Chosen == i;
            var open = v.Chosen < 0;
            var pill = Pill($"choice:{i}", $"{c.Text.ToUpperInvariant()}{(picked ? " ✓" : "")}", picked ? HelmScreen.Green : HelmScreen.Peach, open || picked,
                () => { if (open) ChoicePicked?.Invoke(v.BodyId, index); });
            pill.CustomMinimumSize = new Vector2(P(210), 0);
            pill.Alignment = HorizontalAlignment.Left;
            pill.ClipText = true;
            pill.TooltipText = c.Text;
            grid.AddChild(pill);
            var shift = c.ShockShift == 0 ? "no change to the shock risk" : $"{(c.ShockShift > 0 ? "+" : "−")}{Mathf.Abs(c.ShockShift)} % culture shock";
            var hint = Text($"{c.Hint}{(c.Hint.Length > 0 ? " · " : "")}{shift}", F(12), c.ShockShift > 0 ? HelmScreen.Red : c.ShockShift < 0 ? HelmScreen.Green : Grey);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            hint.CustomMinimumSize = new Vector2(P(200), 0);
            grid.AddChild(hint);
        }
        if (v.Choices.Count == 0) grid.AddChild(Text("Waiting for their reply…", F(13), Grey));
        root.AddChild(grid);

        // The shock-chance meter: red share = culture shock, the rest a smooth start.
        _shockText = Text("", F(12), HelmScreen.Ink, true);
        root.AddChild(_shockText);
        _shockMeter = new CivMeter { Colour = HelmScreen.Red, Marks = new[] { 0.25f, 0.5f, 0.75f }, CustomMinimumSize = new Vector2(0, P(8)) };
        root.AddChild(_shockMeter);
        var note = Text("Smooth: a trade partner at once. Shock: talks go on (trade after a wait), the natives are shaken.", F(11), Grey);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(note);
    }

    // ------------------------------------------------------------------ bits (the dossier's look)

    private Label Text(string text, int size, Color colour, bool bold = false)
    {
        var label = new Label { Text = text.Replace("\r", ""), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeFontOverride("font", HudKit.Condensed(bold));
        return label;
    }

    private PanelContainer Bar(Color colour, string text, int size)
    {
        var style = new StyleBoxFlat { BgColor = colour, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) };
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", style);
        var label = Text(text, size, Black, true);
        label.ClipText = true;
        bar.AddChild(label);
        return bar;
    }

    private HelmPill Pill(string id, string text, Color colour, bool enabled, Action onPressed)
    {
        var pill = new HelmPill { Id = id };
        pill.AddThemeFontOverride("font", HudKit.Condensed(true));
        pill.AddThemeFontSizeOverride("font_size", F(14));
        pill.Set(text, colour, !enabled, P(10), P(10), P(10), P(3));
        pill.Pressed += onPressed;
        return pill;
    }
}
