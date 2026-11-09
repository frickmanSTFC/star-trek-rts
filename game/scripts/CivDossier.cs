using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>Words and colours for civilizations (civilizations phase 1, docs/civilizations-contract.md "View"), shared by
/// the body card, the dossier, the Standing chips and the event lines. Placeholder texts from the First Contact mockup;
/// the numbers that matter come from the sim (CivReport, StandingOf, IncidentsOn).</summary>
public static class CivText
{
    public static readonly Color Rust = HelmScreen.Rust;

    /// <summary>L1..L6: short key, name, what it looks like, what it can see of a ship (mockup LEVELS).</summary>
    public static readonly (string Key, string Name, string About, string Sees)[] Levels =
    {
        ("L1", "Stone / Bronze", "villages, bronze tools, oral history", "nothing in orbit"),
        ("L2", "Iron / Medieval", "kingdoms, writing, sailing ships", "a 'new star' at best"),
        ("L3", "Industrial", "railways, steam, telegraph, cities of millions", "telescopes catch a lit hull at dusk"),
        ("L4", "Atomic / Information", "fission, rockets, radio, computers", "radar tracks a ship in low orbit"),
        ("L5", "Early warp", "first warp test flight this decade", "sensors find a ship anywhere in the system"),
        ("L6", "Spacefaring", "warp ships, outposts on their moons", "they hail you first"),
    };

    public static int N(CivLevel level) => Mathf.Clamp((int)level, 1, 6);

    public static string LevelName(CivLevel level) => $"{Levels[N(level) - 1].Key} {Levels[N(level) - 1].Name}";

    /// <summary>"L3 Industrial" once studied (or the range narrowed to one), else "L2–L4 (study narrows it)".</summary>
    public static string LevelText(CivReport r) => r.LevelMin == r.LevelMax || Studied(r)
        ? LevelName(Studied(r) ? r.LevelMax : r.LevelMin)
        : $"{Levels[N(r.LevelMin) - 1].Key}–{Levels[N(r.LevelMax) - 1].Key} · {Levels[N(r.LevelMin) - 1].Name} to {Levels[N(r.LevelMax) - 1].Name}";

    public static bool Studied(CivReport r) => r.Study.ToFloat() >= 0.999f;

    public static string Species(CivReport r) => string.IsNullOrEmpty(r.Species) ? "unknown species" : r.Species!;

    /// <summary>"the Tenari" (adds "the" unless the name has it).</summary>
    public static string The(CivReport r) => Species(r).StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? Species(r) : $"the {Species(r)}";

    public static string Population(long people) => people switch
    {
        <= 0 => "unknown",
        >= 1_000_000_000 => $"{people / 1e9:0.0} billion",
        >= 1_000_000 => $"{people / 1e6:0} million",
        >= 1_000 => $"{people / 1e3:0} thousand",
        _ => $"{people}",
    };

    public static string TemperamentText(Temperament t) => t switch
    {
        FinalFrontier.Sim.Space.Temperament.Peaceful => "peaceful",
        FinalFrontier.Sim.Space.Temperament.Wary => "wary",
        FinalFrontier.Sim.Space.Temperament.Aggressive => "aggressive",
        _ => "unknown (study them)",
    };

    public static Color TemperamentColour(Temperament t) => t switch
    {
        FinalFrontier.Sim.Space.Temperament.Peaceful => HelmScreen.Green,
        FinalFrontier.Sim.Space.Temperament.Wary => HelmScreen.Peach,
        FinalFrontier.Sim.Space.Temperament.Aggressive => HelmScreen.Red,
        _ => new Color(0.62f, 0.64f, 0.68f),
    };

    public static string UnityText(CivReport r) => r.Unity switch
    {
        CivUnity.Unified => "unified · one world government",
        CivUnity.Fractured => $"fractured · {Mathf.Max(2, r.Nations)} nations",
        _ => "unknown (study them)",
    };

    /// <summary>The contract's awareness bands: 0–19 / 20–49 / 50–79 / 80+.</summary>
    public static readonly (int From, string Name, string About)[] Bands =
    {
        (0, "Unaware", "no idea anyone is up here"),
        (20, "Rumours", "odd lights, stories, a crank or two"),
        (50, "Suspect", "their scientists suspect visitors"),
        (80, "They know", "aliens are real to them"),
    };

    public static int Band(int awareness) => awareness >= 80 ? 3 : awareness >= 50 ? 2 : awareness >= 20 ? 1 : 0;

    public static Color AwarenessColour(int awareness) => Band(awareness) switch
    {
        0 => HelmScreen.Sky,
        1 => HelmScreen.Cyan,
        2 => HelmScreen.Peach,
        _ => HelmScreen.Red,
    };

    /// <summary>The Standing ladder (rung 1..5 = the sim's StandingRung): name, when, what it means (design doc).</summary>
    public static readonly (string Name, string When, string About)[] Rungs =
    {
        ("Good standing", "70 and up", "Starfleet is satisfied"),
        ("Warning", "below 70", "logged on the captain's record"),
        ("Starfleet inquiry", "below 50", "the captain is called to account"),
        ("Captain relieved", "below 30", "a new captain takes over"),
        ("Admiralty sanctions", "below 10", "research cut, no reinforcements"),
        ("Recalled for good", "at 0, or a third incident on one world", "the ship is recalled"),
    };

    public static Color RungColour(int rung) => Mathf.Clamp(rung, 0, 5) switch
    {
        0 => HelmScreen.Green,
        1 => new Color(0.95f, 0.8f, 0.4f),
        2 => HelmScreen.Peach,
        3 => HelmScreen.Rust,
        4 => HelmScreen.Red,
        _ => new Color(0.75f, 0.22f, 0.2f),
    };

    /// <summary>The relations track (phase 2): Contact → Trade partner → Candidate → Allied world (index = ContactStage − 1).</summary>
    public static readonly (string Name, string About)[] Stages =
    {
        ("Contact", "talks under way; trade comes after a wait"),
        ("Trade partner", "a trickle of resources and latinum, a trade route"),
        ("Candidate", "applying for membership (needs Standing 60+)"),
        ("Allied world", "more trade, their ships stay with you; the world stays theirs"),
    };

    public static string StageName(ContactStage s) => s == ContactStage.None ? "no contact" : Stages[Mathf.Clamp((int)s - 1, 0, 3)].Name;

    public static Color StageColour(ContactStage s) => s switch
    {
        ContactStage.Contact => HelmScreen.Sky,
        ContactStage.TradePartner => HelmScreen.Cyan,
        ContactStage.Candidate => HelmScreen.Lavender,
        ContactStage.Allied => HelmScreen.Green,
        _ => new Color(0.62f, 0.64f, 0.68f),
    };

    /// <summary>The tooltip of a Standing chip: the five steps, the current one marked.</summary>
    public static string StandingTooltip(int standing, int rung)
    {
        var lines = new List<string> { $"Starfleet Standing {standing} / 100 (Prime Directive: General Order 1)", "" };
        for (var i = 1; i < Rungs.Length; i++)
            lines.Add($"{(i == rung ? "▶" : i < rung ? "✕" : "·")} {i}. {Rungs[i].Name}: {Rungs[i].When} ({Rungs[i].About})");
        lines.Add("");
        lines.Add(rung == 0 ? "No consequences: keep the natives unaware." : $"Now: {Rungs[Mathf.Clamp(rung, 0, 5)].Name}.");
        lines.Add("Contamination (a post found, being seen) costs Standing; it recovers slowly with clean conduct.");
        lines.Add("Click for the civilization dossier (C).");
        return string.Join("\n", lines);
    }
}

/// <summary>What the driver hands the dossier for one world (CivReport + context the report doesn't carry).</summary>
public sealed record DossierView(
    CivReport Report,
    string BodyName,
    string ClassName,
    string StudySource,
    string StudyEta,
    int? Standing,
    int Rung,
    int Incidents,
    float ResearchPerMinute,
    IReadOnlyList<(string BodyId, string Name)> Worlds,
    IReadOnlyList<HelmAction> Actions,
    DossierContact? Contact = null);

/// <summary>The dossier's relations strip (phase 2): the stage, trade income, their shared tech, the refusal countdown
/// (seconds), the running talks and what the next step needs. Empty strings = nothing to show.</summary>
public sealed record DossierContact(ContactStage Stage, string Income, string Tech, int RefusesSeconds, string Talks, string Next);

/// <summary>
/// The civilization dossier (docs/mockups/first-contact "Dossier", in the helm's look: black panels, flat colour title
/// bars, condensed caps): a centred panel over a dimmed screen (click outside, C or Esc closes; the game runs on).
/// Left: the development ladder L6..L1, the known range outlined in peach, the exact level filled once studied, below
/// the range dim. Middle: who they are (species, world, population, development, what they can see), temperament,
/// unity and nations (once studied), the study bar with its source (ship in orbit / satellite / hidden post) and
/// research from observing. Right: awareness meter with its four bands, the Starfleet Standing row (Federation in a
/// Prime Directive era), incidents on this world, the post's state. Top: one pill per known civilization world.
/// Rebuilt only when what it shows changes (<see cref="Show"/> compares a key).
/// </summary>
public partial class CivDossier : Control
{
    /// <summary>A button was pressed: a world pill ("civ:dossier:<body>") or an action (the card's ids).</summary>
    public event Action<string>? ActionPicked;

    public bool IsOpen => Visible;

    private string _key = "";
    private float _s = 1f;
    private Vector2 _builtFor;
    private PanelContainer? _panel;
    private DossierView? _view;

    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color Grey = new(0.55f, 0.57f, 0.62f);
    private static readonly Color Dim = new(0.24f, 0.25f, 0.29f);
    private static readonly Color Line = Color.FromHtml("#26324d");

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Close();
        };
        AddChild(dim);
    }

    public void Open() => Visible = true;

    public void Close()
    {
        Visible = false;
        _key = "";
    }

    /// <summary>Every frame while open: rebuilds when the content (or the screen size) changed.</summary>
    public void Show(DossierView view)
    {
        var size = GetViewportRect().Size;
        var key = KeyOf(view);
        if (key == _key && size == _builtFor && _panel != null) return;
        _key = key;
        _builtFor = size;
        _view = view;
        Rebuild(size);
    }

    private static string KeyOf(DossierView v)
    {
        var r = v.Report;
        return string.Join("|", r.BodyId, r.Signals, r.Charted, r.Living, r.Species, r.LevelMin, r.LevelMax, r.Temperament, r.Unity, r.Nations,
            r.Population, r.Awareness, Mathf.RoundToInt(r.Study.ToFloat() * 100), r.Live, r.PostHere, r.PostFound, v.BodyName, v.ClassName,
            v.StudySource, v.StudyEta, v.Standing, v.Rung, v.Incidents, Mathf.RoundToInt(v.ResearchPerMinute * 10),
            string.Join(",", v.Worlds.Select(w => w.BodyId + w.Name)), string.Join(",", v.Actions.Select(a => a.Id + a.Label + a.Enabled)), v.Contact);
    }

    // ------------------------------------------------------------------ build

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(float v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private void Rebuild(Vector2 screen)
    {
        if (_view == null) return;
        _panel?.QueueFree();
        _s = HudKit.ScaleFor(screen);
        var v = _view;
        var r = v.Report;
        var colour = !r.Charted ? HelmScreen.Peach : r.Living ? HelmScreen.Peach : CivText.Rust;

        var width = Mathf.Min(P(1000), screen.X - 32);
        var height = Mathf.Min(P(v.Contact != null ? 650 : 590), screen.Y - 32);
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.92f), BorderColor = colour };
        style.SetBorderWidthAll(1);
        style.BorderWidthLeft = P(5);
        style.ContentMarginLeft = P(14);
        style.ContentMarginRight = P(14);
        style.ContentMarginTop = P(12);
        style.ContentMarginBottom = P(12);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Size = new Vector2(width, height), Position = (screen - new Vector2(width, height)) / 2f, ClipContents = true };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);

        var root = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        root.AddThemeConstantOverride("separation", P(8));
        _panel.AddChild(root);

        // Title bar: who, then the world and the close pill.
        var title = !r.Charted ? "ARTIFICIAL SIGNALS" : r.Living ? CivText.The(r).ToUpperInvariant() : $"RUINS OF {CivText.The(r).ToUpperInvariant()}";
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(8));
        var bar = Bar(colour, $"{title} · DOSSIER", F(20));
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(bar);
        head.AddChild(Pill("civ:close", "CLOSE ×", HelmScreen.Lavender, true, P(12), P(12)));
        root.AddChild(head);
        var sub = Text($"{v.BodyName.ToUpperInvariant()} · {v.ClassName.ToUpperInvariant()} · {(!r.Charted ? "POSSIBLE CIVILIZATION" : r.Living ? "LIVING CIVILIZATION" : "DEAD CIVILIZATION")}{(r.Live ? " · LIVE" : "")}", F(14), HelmScreen.Lavender);
        root.AddChild(sub);

        // One pill per known civilization world (switches the dossier).
        if (v.Worlds.Count > 1)
        {
            var worlds = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
            worlds.AddThemeConstantOverride("h_separation", P(4));
            worlds.AddThemeConstantOverride("v_separation", P(4));
            for (var i = 0; i < v.Worlds.Count; i++)
            {
                var (id, name) = v.Worlds[i];
                worlds.AddChild(Pill($"civ:dossier:{id}", name.ToUpperInvariant(), id == r.BodyId ? HelmScreen.Peach : HelmScreen.Sky, true,
                    i == 0 ? P(10) : 0, i == v.Worlds.Count - 1 ? P(10) : 0));
            }
            root.AddChild(worlds);
        }

        var cols = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", P(14));
        root.AddChild(cols);
        cols.AddChild(Ladder(r));
        var middle = Middle(v);
        middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cols.AddChild(middle);
        cols.AddChild(Side(v));
        if (v.Contact != null) root.AddChild(Relations(v.Contact));

        // Actions (HIDDEN POST while the flagship orbits it) and the footer.
        var foot = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        foot.AddThemeConstantOverride("separation", P(4));
        for (var i = 0; i < v.Actions.Count; i++)
        {
            var a = v.Actions[i];
            foot.AddChild(Pill(a.Id, a.Key.Length > 0 ? $"{a.Key} {a.Label}" : a.Label, a.Colour ?? HelmScreen.Peach, a.Enabled, i == 0 ? P(12) : 0, i == v.Actions.Count - 1 ? P(12) : 0));
        }
        var hint = Text("C / ESC CLOSE · CLICK OUTSIDE TO CLOSE · THE GAME RUNS ON", F(12), Grey);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        foot.AddChild(hint);
        root.AddChild(foot);
    }

    /// <summary>The relations track (Contact → Trade partner → Candidate → Allied world; reached steps filled, the current
    /// one outlined bright) and one line of details: trade income, their tech, the refusal countdown, the talks, the next step.</summary>
    private Control Relations(DossierContact c)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", P(4));
        var track = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        track.AddThemeConstantOverride("separation", P(4));
        track.AddChild(Bar(CivText.StageColour(c.Stage == ContactStage.None ? ContactStage.Contact : c.Stage), "RELATIONS", F(14)));
        for (var i = 0; i < CivText.Stages.Length; i++)
        {
            var stage = (ContactStage)(i + 1);
            var reached = c.Stage >= stage;
            var chip = Chip(CivText.Stages[i].Name.ToUpperInvariant(), reached ? CivText.StageColour(stage) : Dim, !reached);
            chip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            chip.TooltipText = CivText.Stages[i].About;
            track.AddChild(chip);
            if (i < CivText.Stages.Length - 1) track.AddChild(Text("→", F(13), reached && c.Stage > stage ? HelmScreen.Ink : Grey, true));
        }
        box.AddChild(track);
        var parts = new List<string>();
        if (c.Talks.Length > 0) parts.Add(c.Talks);
        if (c.RefusesSeconds > 0) parts.Add($"they refuse contact · {c.RefusesSeconds / 60}:{c.RefusesSeconds % 60:00} left");
        if (c.Income.Length > 0) parts.Add($"trade {c.Income}");
        if (c.Tech.Length > 0) parts.Add($"their tech: {c.Tech}");
        parts.Add(c.Next);
        var line = Para(string.Join("  ·  ", parts), c.RefusesSeconds > 0 ? HelmScreen.Red : c.Stage == ContactStage.None ? HelmScreen.Ink : CivText.StageColour(c.Stage));
        box.AddChild(line);
        return box;
    }

    /// <summary>L6 at the top down to L1. Known range outlined, the exact level filled, below the range dim.</summary>
    private Control Ladder(CivReport r)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(230), 0) };
        box.AddThemeConstantOverride("separation", P(4));
        box.AddChild(Bar(HelmScreen.Sky, "DEVELOPMENT", F(14)));
        var known = r.Charted && r.Living;
        var studied = known && CivText.Studied(r);
        var lo = known ? CivText.N(r.LevelMin) : 0;
        var hi = known ? CivText.N(r.LevelMax) : 0;
        if (studied) lo = hi = CivText.N(r.LevelMax);
        for (var n = 6; n >= 1; n--)
        {
            var (key, name, about, _) = CivText.Levels[n - 1];
            var inRange = known && n >= lo && n <= hi;
            var exact = inRange && lo == hi;
            if (n == 5)
            {
                // the warp line: Federation first contact (phase 2) is for L5 and up
                var warp = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                warp.AddThemeConstantOverride("separation", P(4));
                var rule = new ColorRect { Color = HelmScreen.Sky, CustomMinimumSize = new Vector2(0, P(2)), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
                warp.AddChild(rule);
                warp.AddChild(Text("WARP", F(10), HelmScreen.Sky, true));
                box.AddChild(warp);
            }
            var bg = exact ? HelmScreen.Peach : new Color(0.03f, 0.04f, 0.07f);
            var style = new StyleBoxFlat { BgColor = bg, BorderColor = inRange ? HelmScreen.Peach : Line };
            style.SetBorderWidthAll(inRange ? P(2) : 1);
            style.ContentMarginLeft = P(8);
            style.ContentMarginRight = P(6);
            style.ContentMarginTop = P(3);
            style.ContentMarginBottom = P(3);
            var rung = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            rung.AddThemeStyleboxOverride("panel", style);
            var lines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 0);
            var ink = exact ? Black : inRange ? HelmScreen.Ink : known && n < lo ? HelmScreen.Lavender.Darkened(0.2f) : Grey;
            lines.AddChild(Text($"{key}  {name.ToUpperInvariant()}", F(14), ink, true));
            var small = Text(about, F(11), exact ? Black : inRange ? HelmScreen.Lavender : Grey);
            small.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lines.AddChild(small);
            rung.AddChild(lines);
            box.AddChild(rung);
        }
        var note = Text(!r.Charted ? "Unknown: orbit for a sensor sweep." : !r.Living ? "Dead: no living culture to rank."
            : studied ? "Exact: studied." : "Peach outline: what the scan allows. Study narrows it to one.", F(12), Grey);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(note);
        return box;
    }

    private Control Middle(DossierView v)
    {
        var r = v.Report;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", P(6));
        if (!r.Charted)
        {
            box.AddChild(Bar(HelmScreen.Peach, "WHAT THE SENSORS HEAR", F(14)));
            box.AddChild(Para("Artificial signals and structures: a possible civilization. Enter orbit and let the sensor sweep chart the world: species, a range for their development, their population and how aware they are of aliens.", HelmScreen.Ink));
            return box;
        }
        if (!r.Living)
        {
            box.AddChild(Bar(CivText.Rust, "A DEAD CIVILIZATION", F(14)));
            box.AddChild(Rows(new List<HelmRow>
            {
                new("WHO", $"{CivText.The(r)} (extinct)"),
                new("WORLD", v.BodyName),
                new("SITES", "ruin fields seen from orbit"),
            }, CivText.Rust));
            box.AddChild(Para("Nobody is left to notice us: no Prime Directive here. Digging ruins (artefacts, data cores, tech fragments, dangers) works on your own colony world with an Excavation Site; ruins on other worlds come later.", Grey));
            return box;
        }
        var studied = CivText.Studied(r);
        box.AddChild(Bar(HelmScreen.Peach, "WHO THEY ARE", F(14)));
        var level = CivText.N(studied ? r.LevelMax : r.LevelMin);
        var rows = new List<HelmRow>
        {
            new("SPECIES", CivText.Species(r)),
            new("WORLD", $"{v.BodyName} · {v.ClassName}"),
            new("LEVEL", CivText.LevelText(r)),
            new("PEOPLE", CivText.Population(r.Population)),
        };
        if (studied)
        {
            rows.Add(new HelmRow("WAY OF LIFE", CivText.Levels[level - 1].About));
            rows.Add(new HelmRow("SEES SHIPS", CivText.Levels[level - 1].Sees));
        }
        box.AddChild(Rows(rows, HelmScreen.Peach));

        box.AddChild(Bar(HelmScreen.Lavender, "TEMPERAMENT AND UNITY", F(14)));
        box.AddChild(Rows(new List<HelmRow>
        {
            new("TEMPERAMENT", CivText.TemperamentText(r.Temperament), CivText.TemperamentColour(r.Temperament)),
            new("UNITY", CivText.UnityText(r), r.Unity == CivUnity.Unknown ? Grey : null),
        }, HelmScreen.Lavender));
        if (r.Unity == CivUnity.Fractured && r.Nations > 1)
        {
            var nations = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
            nations.AddThemeConstantOverride("h_separation", P(4));
            nations.AddThemeConstantOverride("v_separation", P(4));
            Color[] tints = { HelmScreen.Sky, HelmScreen.Green, HelmScreen.Red, HelmScreen.Lavender, HelmScreen.Peach, HelmScreen.Cyan };
            for (var i = 0; i < Mathf.Min(r.Nations, 12); i++) nations.AddChild(Chip($"NATION {i + 1}", tints[i % tints.Length]));
            box.AddChild(nations);
        }
        if (!studied) box.AddChild(Para("Temperament and unity are known once the study is done.", Grey));

        box.AddChild(Bar(HelmScreen.Cyan, "STUDY", F(14)));
        var study = Mathf.Clamp(r.Study.ToFloat(), 0f, 1f);
        box.AddChild(Rows(new List<HelmRow>
        {
            new("PROGRESS", studied ? "complete: exact level, temperament and unity known" : $"{Mathf.RoundToInt(study * 100)} %{(v.StudyEta.Length > 0 ? $" · {v.StudyEta}" : "")}", HelmScreen.Cyan, study),
            new("SOURCE", v.StudySource),
            new("RESEARCH", v.ResearchPerMinute > 0f ? $"+{v.ResearchPerMinute:0.#} a minute from observing (all worlds)" : "none right now"),
        }, HelmScreen.Cyan));
        return box;
    }

    private Control Side(DossierView v)
    {
        var r = v.Report;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(290), 0) };
        box.AddThemeConstantOverride("separation", P(6));
        if (r.Charted && r.Living)
        {
            var colour = CivText.AwarenessColour(r.Awareness);
            box.AddChild(Bar(HelmScreen.Sky, "AWARENESS OF ALIENS · 0–100", F(14)));
            var big = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            big.AddThemeConstantOverride("separation", P(8));
            big.AddChild(Text($"{r.Awareness}", F(30), colour, true));
            var band = CivText.Band(r.Awareness);
            var bandText = Text($"/ 100 · {CivText.Bands[band].Name.ToUpperInvariant()}{(r.Live ? " · LIVE" : " · AT LAST SCAN")}", F(13), HelmScreen.Lavender);
            bandText.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            big.AddChild(bandText);
            box.AddChild(big);
            box.AddChild(new CivMeter { Fraction = r.Awareness / 100f, Colour = colour, Marks = new[] { 0.2f, 0.5f, 0.8f }, CustomMinimumSize = new Vector2(0, P(12)) });
            var bands = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            bands.AddThemeConstantOverride("separation", P(3));
            for (var i = 0; i < CivText.Bands.Length; i++)
            {
                var chip = Chip(CivText.Bands[i].Name.ToUpperInvariant(), i == band ? colour : Dim, i != band);
                chip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                chip.TooltipText = $"{CivText.Bands[i].From}+: {CivText.Bands[i].About}";
                bands.AddChild(chip);
            }
            box.AddChild(bands);
            box.AddChild(Para("Rises when they see something (a satellite, a post found, a glint); it never falls by itself.", Grey));
        }

        if (v.Standing is { } standing)
        {
            var colour = CivText.RungColour(v.Rung);
            box.AddChild(Bar(colour, "STARFLEET STANDING", F(14)));
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", P(8));
            row.AddChild(Text($"{standing}", F(26), colour, true));
            var rungText = Text($"/ 100 · {CivText.Rungs[Mathf.Clamp(v.Rung, 0, 5)].Name.ToUpperInvariant()}", F(13), HelmScreen.Lavender);
            rungText.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            row.AddChild(rungText);
            box.AddChild(row);
            box.AddChild(new CivMeter
            {
                Fraction = standing / 100f, Colour = colour, Marks = new[] { 0.1f, 0.3f, 0.5f, 0.7f }, CustomMinimumSize = new Vector2(0, P(12)),
                TooltipText = CivText.StandingTooltip(standing, v.Rung), MouseFilter = MouseFilterEnum.Stop,
            });
            box.AddChild(Rows(new List<HelmRow>
            {
                new("INCIDENTS", r.Charted && r.Living ? $"{v.Incidents} of 3 on this world{(v.Incidents >= 2 ? " · one more and you are recalled" : "")}" : "none possible here",
                    v.Incidents > 0 ? HelmScreen.Red : null),
            }, colour));
        }

        if (r.Charted && r.Living)
        {
            box.AddChild(Bar(HelmScreen.Lavender, "OBSERVATION POST", F(14)));
            var post = r.PostFound ? ("FOUND by the natives: awareness up, Standing down", HelmScreen.Red)
                : r.PostHere ? ("hidden, studying ×4 (may be found: the risk doubles above awareness 40)", HelmScreen.Green)
                : ("none: place one from orbit (HIDDEN POST) to study ×4 without the ship", HelmScreen.Ink);
            box.AddChild(Para(post.Item1, post.Item2));
        }
        return box;
    }

    // ------------------------------------------------------------------ bits

    private Label Text(string text, int size, Color colour, bool bold = false)
    {
        var label = new Label { Text = text.Replace("\r", ""), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeFontOverride("font", HudKit.Condensed(bold));
        return label;
    }

    private Label Para(string text, Color colour)
    {
        var l = Text(text, F(13), colour);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(P(120), 0);
        return l;
    }

    /// <summary>A flat colour title bar with black bold caps (the helm's section headers).</summary>
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

    private PanelContainer Chip(string text, Color colour, bool outline = false)
    {
        var style = new StyleBoxFlat { BgColor = outline ? new Color(0.03f, 0.04f, 0.07f) : colour, BorderColor = colour, ContentMarginLeft = P(6), ContentMarginRight = P(6), ContentMarginTop = P(1), ContentMarginBottom = P(1) };
        style.SetBorderWidthAll(1);
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        chip.AddThemeStyleboxOverride("panel", style);
        var l = Text(text, F(11), outline ? Grey : Black, true);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        chip.AddChild(l);
        return chip;
    }

    /// <summary>Label / value rows with a coloured left border (the helm's panel rows); a row's Bar draws a meter.</summary>
    private PanelContainer Rows(IReadOnlyList<HelmRow> rows, Color colour)
    {
        var style = new StyleBoxFlat { DrawCenter = false, BorderColor = colour, BorderWidthLeft = P(4), ContentMarginLeft = P(8) };
        var block = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        block.AddThemeStyleboxOverride("panel", style);
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", P(8));
        grid.AddThemeConstantOverride("v_separation", P(3));
        foreach (var row in rows)
        {
            var name = Text(row.Label.ToUpperInvariant(), F(13), colour);
            name.CustomMinimumSize = new Vector2(P(92), 0);
            name.VerticalAlignment = VerticalAlignment.Top;
            grid.AddChild(name);
            var value = Text(row.Value, F(14), row.ValueColour ?? HelmScreen.Ink);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (row.Bar is { } f)
            {
                var cell = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                cell.AddThemeConstantOverride("separation", P(2));
                cell.AddChild(value);
                cell.AddChild(new CivMeter { Fraction = f, Colour = row.ValueColour ?? HelmScreen.Cyan, CustomMinimumSize = new Vector2(0, P(7)) });
                grid.AddChild(cell);
            }
            else grid.AddChild(value);
        }
        block.AddChild(grid);
        return block;
    }

    private HelmPill Pill(string id, string text, Color colour, bool enabled, int roundLeft, int roundRight)
    {
        var pill = new HelmPill { Id = id };
        pill.AddThemeFontOverride("font", HudKit.Condensed(true));
        pill.AddThemeFontSizeOverride("font_size", F(14));
        pill.Set(text, colour, !enabled, roundLeft, roundRight, P(10), P(3));
        pill.Pressed += () =>
        {
            if (pill.Id == "civ:close") Close();
            else ActionPicked?.Invoke(pill.Id);
        };
        return pill;
    }
}

/// <summary>A flat meter with tick marks (awareness bands, Standing thresholds) in the helm's look.</summary>
public partial class CivMeter : Control
{
    private float _fraction;
    private Color _colour = HelmScreen.Cyan;
    public float[] Marks = Array.Empty<float>();

    public float Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Mathf.Clamp(value, 0f, 1f);
            QueueRedraw();
        }
    }

    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            QueueRedraw();
        }
    }

    public CivMeter() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.08f, 0.1f, 0.17f));
        if (_fraction > 0f) DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _fraction, Size.Y)), _colour);
        foreach (var m in Marks)
        {
            var x = Mathf.Round(Size.X * m);
            DrawLine(new Vector2(x, -2f), new Vector2(x, Size.Y + 2f), new Color(0.95f, 0.9f, 0.85f, 0.85f), 1f);
        }
    }
}
