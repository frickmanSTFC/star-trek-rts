using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>One division in the away-team picker: how many are picked, how many are on duty.</summary>
public sealed record AwayDivisionRow(string Id, string Name, int Picked, int OnDuty);

/// <summary>The picker before launch. Problem = why LAUNCH is greyed (the sim's AwayTeamProblem), Preview = who would
/// go, by name, when the sim can say (else empty).</summary>
public sealed record AwayPickerView(string SiteId, string SiteTitle, IReadOnlyList<AwayDivisionRow> Divisions, int Max, string? Problem,
    IReadOnlyList<string> Preview, string Note);

/// <summary>A running or finished away-team event as the panel shows it (from AwayTeamView). Result != null = done.</summary>
public sealed record AwayEventView(string SiteId, string Title, string Subtitle, int Step, int Steps, string StepTitle, float StepSecondsLeft,
    float StepSeconds, IReadOnlyList<AwayChoice> Choices, IReadOnlyList<string?> ChoiceProblems, int Chosen, IReadOnlyList<int> Picked,
    IReadOnlyList<AwayMember> Team, int Injury, int Loss, int Find, string? Outcome, string? Result, Color ResultColour);

/// <summary>
/// The away-team panel (space jobs phase 1, docs/space-jobs-contract.md "VIEW", the design page's screen 2), in the helm's
/// look like the first-contact panel (ContactPanel.cs): top-centre over the viewscreen, not modal (the game never
/// pauses). Two modes. The PICKER (before launch): one row per crew division with − / + and how many are on duty, the
/// team size against the ship's maximum, who goes by name when the sim can say, the Defense / medic note, LAUNCH (greyed
/// with the sim's AwayTeamProblem as its tooltip) and CANCEL. The EVENT: AWAY TEAM · site, STEP n OF 3 with the time
/// left (a bar that runs down; the first choice is taken when it runs out), the step's title, its choices as pills
/// with their hints and how each moves the odds (injury / loss per person, find chance, time), the live meters (injury
/// amber, loss red, good find green), the team by name; then the outcome and result line with × to dismiss. "–"
/// folds it to the title bar. Rebuilt only when what it shows changes; timers and meters every frame.
/// </summary>
public partial class AwayTeamPanel : Control
{
    /// <summary>Picker: a division's − (−1) or + (+1) was pressed.</summary>
    public event Action<int, int>? CrewChanged;

    public event Action? Launch;
    public event Action? Cancel;

    /// <summary>Event: a choice pill (index) for the site.</summary>
    public event Action<string, int>? ChoicePicked;

    /// <summary>The × on the result.</summary>
    public event Action? Dismissed;

    private PanelContainer? _panel;
    private string _key = "";
    private Vector2 _builtFor;
    private float _s = 1f;
    private bool _collapsed;
    private AwayPickerView? _picker;
    private AwayEventView? _event;
    private Label? _timer;
    private CivMeter? _stepMeter;
    private CivMeter? _injuryMeter, _lossMeter, _findMeter;
    private Label? _injuryText, _lossText, _findText;

    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color Grey = new(0.55f, 0.57f, 0.62f);
    private static readonly Color Amber = Color.FromHtml("#f5c04a");

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public bool IsOverUi(Vector2 mouse) => Visible && _panel != null && _panel.GetGlobalRect().HasPoint(mouse);

    /// <summary>Every frame: the picker, else the event, else hidden. Top edge at <paramref name="top"/>, centred on <paramref name="centreX"/>.</summary>
    public void Show(AwayPickerView? picker, AwayEventView? ev, float centreX, float top)
    {
        if (picker == null && ev == null)
        {
            if (Visible) Visible = false;
            _picker = null;
            _event = null;
            return;
        }
        Visible = true;
        var size = GetViewportRect().Size;
        _picker = picker;
        _event = picker == null ? ev : null;
        var key = picker != null ? PickerKey(picker) : EventKey(ev!);
        if (key != _key || size != _builtFor || _panel == null)
        {
            _key = key;
            _builtFor = size;
            Rebuild(size);
        }
        if (_panel != null)
        {
            var w = _panel.Size.X;
            _panel.Position = new Vector2(Mathf.Clamp(centreX - w / 2f, 8f, Mathf.Max(8f, size.X - w - 8f)), top);
        }
        if (_event != null) Tick(_event);
    }

    private string PickerKey(AwayPickerView p) => string.Join("|", "pick", p.SiteId, p.SiteTitle, p.Max, p.Problem ?? "", p.Note, _collapsed,
        string.Join(",", p.Divisions.Select(d => $"{d.Id}:{d.Picked}/{d.OnDuty}")), string.Join(",", p.Preview));

    private string EventKey(AwayEventView v) => string.Join("|", "event", v.SiteId, v.Title, v.Subtitle, v.Step, v.Steps, v.StepTitle, v.Chosen, v.Outcome ?? "",
        v.Result ?? "", _collapsed, string.Join(",", v.Picked), string.Join(",", v.Choices.Select(c => c.Label + c.Hint + c.Injury + c.Loss + c.Find + c.Seconds)),
        string.Join(",", v.ChoiceProblems.Select(p => p ?? "")), string.Join(",", v.Team.Select(m => m.Name)));

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(float v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private static string Clock(float seconds)
    {
        var s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private void Tick(AwayEventView v)
    {
        if (_timer != null)
            _timer.Text = v.Result != null ? "BACK ABOARD"
                : $"STEP {v.Step + 1} OF {v.Steps} · {Clock(v.StepSecondsLeft)} LEFT{(v.Chosen < 0 ? " (THEN THE FIRST CHOICE IS TAKEN)" : "")}";
        if (_stepMeter != null) _stepMeter.Fraction = v.StepSeconds > 0f ? v.StepSecondsLeft / v.StepSeconds : 0f;
        if (_injuryMeter != null) _injuryMeter.Fraction = v.Injury / 100f;
        if (_lossMeter != null) _lossMeter.Fraction = v.Loss / 100f;
        if (_findMeter != null) _findMeter.Fraction = v.Find / 100f;
        if (_injuryText != null) _injuryText.Text = $"INJURY, EACH {v.Injury} %";
        if (_lossText != null) _lossText.Text = $"LOSS, EACH {v.Loss} %";
        if (_findText != null) _findText.Text = $"GOOD FIND {v.Find} %";
    }

    private void Rebuild(Vector2 screen)
    {
        _panel?.QueueFree();
        _panel = null;
        _timer = null;
        _stepMeter = _injuryMeter = _lossMeter = _findMeter = null;
        _injuryText = _lossText = _findText = null;
        _s = HudKit.ScaleFor(screen);
        var colour = _event?.Result != null ? _event.ResultColour : HelmScreen.Peach;

        var width = Mathf.Min(P(600), screen.X - 32);
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
        if (_picker != null) BuildPicker(root, _picker);
        else if (_event != null) BuildEvent(root, _event);
    }

    // ------------------------------------------------------------------ picker

    private void BuildPicker(VBoxContainer root, AwayPickerView p)
    {
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(6));
        var bar = Bar(HelmScreen.Peach, $"AWAY TEAM · {p.SiteTitle.ToUpperInvariant()} · WHO GOES", F(17));
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(bar);
        head.AddChild(Pill("x", "×", HelmScreen.Lavender, true, () => Cancel?.Invoke(), "Cancel (Esc)"));
        root.AddChild(head);
        var total = p.Divisions.Sum(d => d.Picked);
        root.AddChild(Text($"TEAM {total} OF {p.Max} AT MOST · {p.Divisions.Sum(d => d.OnDuty)} ON DUTY", F(13), total > p.Max ? HelmScreen.Red : HelmScreen.Lavender, true));

        var grid = new GridContainer { Columns = 5, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", P(8));
        grid.AddThemeConstantOverride("v_separation", P(4));
        for (var i = 0; i < p.Divisions.Count; i++)
        {
            var d = p.Divisions[i];
            var index = i;
            var name = Text(d.Name.ToUpperInvariant(), F(14), DivisionColour(d.Id), true);
            name.CustomMinimumSize = new Vector2(P(130), 0);
            grid.AddChild(name);
            grid.AddChild(Pill($"minus:{i}", "−", HelmScreen.Lavender, d.Picked > 0, () => CrewChanged?.Invoke(index, -1)));
            var count = Text(d.Picked.ToString(), F(16), HelmScreen.Ink, true);
            count.CustomMinimumSize = new Vector2(P(24), 0);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            grid.AddChild(count);
            grid.AddChild(Pill($"plus:{i}", "+", HelmScreen.Lavender, d.Picked < d.OnDuty && total < p.Max, () => CrewChanged?.Invoke(index, 1)));
            grid.AddChild(Text($"of {d.OnDuty} on duty", F(13), Grey));
        }
        root.AddChild(grid);
        if (p.Preview.Count > 0)
        {
            var who = Text("GOING: " + string.Join(" · ", p.Preview), F(13), HelmScreen.Ink);
            who.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(who);
        }
        var note = Text(p.Note, F(12), Grey);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(note);
        if (p.Problem != null)
        {
            var why = Text(p.Problem, F(13), HelmScreen.Red);
            why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(why);
        }
        var buttons = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        buttons.AddThemeConstantOverride("separation", P(6));
        var launch = Pill("launch", "LAUNCH THE AWAY TEAM", HelmScreen.Peach, p.Problem == null, () => Launch?.Invoke(), p.Problem ?? "Three timed steps; the game doesn't pause");
        Sfx.Tag(launch, "confirm");
        buttons.AddChild(launch);
        var cancel = Pill("cancel", "CANCEL", HelmScreen.Lavender, true, () => Cancel?.Invoke(), "Esc");
        Sfx.Tag(cancel, "cancel");
        buttons.AddChild(cancel);
        root.AddChild(buttons);
    }

    // ------------------------------------------------------------------ event

    private void BuildEvent(VBoxContainer root, AwayEventView v)
    {
        var colour = v.Result != null ? v.ResultColour : HelmScreen.Peach;
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
            if (v.Outcome is { Length: > 0 } outcome) root.AddChild(Text(outcome.ToUpperInvariant(), F(15), v.ResultColour, true));
            var result = Text(v.Result, F(14), HelmScreen.Ink);
            result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(result);
            AddTeam(root, v);
            return;
        }

        _timer = Text("", F(13), HelmScreen.Ink, true);
        root.AddChild(_timer);
        _stepMeter = new CivMeter { Colour = HelmScreen.Peach, CustomMinimumSize = new Vector2(0, P(6)) };
        root.AddChild(_stepMeter);

        // The steps: done ones ticked, the current one named.
        var steps = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        steps.AddThemeConstantOverride("separation", P(14));
        for (var i = 0; i < v.Steps; i++)
        {
            var done = i < v.Step;
            var name = i == v.Step && v.StepTitle.Length > 0 ? $"{i + 1} · {v.StepTitle.ToUpperInvariant()}" : $"{i + 1}{(done ? " ✓" : "")}";
            steps.AddChild(Text(name, F(13), i == v.Step ? HelmScreen.Peach : done ? HelmScreen.Green : Grey, i == v.Step));
        }
        root.AddChild(steps);

        if (!_collapsed)
        {
            var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", P(10));
            grid.AddThemeConstantOverride("v_separation", P(5));
            for (var i = 0; i < v.Choices.Count; i++)
            {
                var c = v.Choices[i];
                var index = i;
                var picked = v.Chosen == i;
                var open = v.Chosen < 0;
                var problem = i < v.ChoiceProblems.Count ? v.ChoiceProblems[i] : null;
                var pill = Pill($"choice:{i}", $"{c.Label.ToUpperInvariant()}{(picked ? " ✓" : "")}", picked ? HelmScreen.Green : HelmScreen.Peach,
                    (open && problem == null) || picked, () => { if (open) ChoicePicked?.Invoke(v.SiteId, index); }, problem ?? c.Label);
                pill.CustomMinimumSize = new Vector2(P(210), 0);
                pill.Alignment = HorizontalAlignment.Left;
                pill.ClipText = true;
                grid.AddChild(pill);
                var hint = Text($"{c.Hint}{(c.Hint.Length > 0 ? " · " : "")}{OddsText(c)}", F(12), c.Loss > 0 || c.Injury > 0 ? Amber : c.Find > 0 ? HelmScreen.Green : Grey);
                hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                hint.CustomMinimumSize = new Vector2(P(220), 0);
                grid.AddChild(hint);
            }
            if (v.Choices.Count == 0) grid.AddChild(Text("The team is on its way…", F(13), Grey));
            root.AddChild(grid);
        }

        // The live odds.
        var meters = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        meters.AddThemeConstantOverride("h_separation", P(10));
        meters.AddThemeConstantOverride("v_separation", P(2));
        _injuryText = Text("", F(12), Amber, true);
        _lossText = Text("", F(12), HelmScreen.Red, true);
        _findText = Text("", F(12), HelmScreen.Green, true);
        foreach (var l in new[] { _injuryText, _lossText, _findText })
        {
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            meters.AddChild(l);
        }
        _injuryMeter = Meter(Amber);
        _lossMeter = Meter(HelmScreen.Red);
        _findMeter = Meter(HelmScreen.Green);
        meters.AddChild(_injuryMeter);
        meters.AddChild(_lossMeter);
        meters.AddChild(_findMeter);
        root.AddChild(meters);
        if (!_collapsed) AddTeam(root, v);
    }

    private CivMeter Meter(Color colour) => new()
    {
        Colour = colour, Marks = new[] { 0.25f, 0.5f, 0.75f }, CustomMinimumSize = new Vector2(P(150), P(7)), SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private void AddTeam(VBoxContainer root, AwayEventView v)
    {
        if (v.Team.Count == 0) return;
        var team = Text($"TEAM {v.Team.Count}: " + string.Join(" · ", v.Team.Select(m => m.Role.Length > 0 ? $"{m.Name}, {m.Role}" : m.Name)), F(12), HelmScreen.Ink);
        team.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(team);
    }

    /// <summary>"Injury +12 · Loss +3 · Time −20 s" (zero terms left out).</summary>
    public static string OddsText(AwayChoice c)
    {
        var parts = new List<string>();
        static string Signed(int n) => n > 0 ? $"+{n}" : n < 0 ? $"−{-n}" : "0";
        if (c.Injury != 0) parts.Add($"Injury {Signed(c.Injury)}");
        if (c.Loss != 0) parts.Add($"Loss {Signed(c.Loss)}");
        if (c.Find != 0) parts.Add($"Find {Signed(c.Find)}");
        if (c.Seconds != 0) parts.Add($"Time {Signed(c.Seconds)} s");
        return parts.Count == 0 ? "no change to the odds" : string.Join(" · ", parts);
    }

    public static Color DivisionColour(string id) => id switch
    {
        "exploration" => Color.FromHtml("#3e7fd6"),
        "engineering" => Color.FromHtml("#d9a23a"),
        "science" => Color.FromHtml("#4e9a5a"),
        "defense" => Color.FromHtml("#c8423a"),
        _ => HelmScreen.Sky,
    };

    // ------------------------------------------------------------------ bits (ContactPanel's look)

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

    private HelmPill Pill(string id, string text, Color colour, bool enabled, Action onPressed, string tip = "")
    {
        var pill = new HelmPill { Id = id, TooltipText = tip };
        pill.AddThemeFontOverride("font", HudKit.Condensed(true));
        pill.AddThemeFontSizeOverride("font_size", F(14));
        pill.Set(text, colour, !enabled, P(10), P(10), P(10), P(3));
        pill.Pressed += onPressed;
        return pill;
    }
}
