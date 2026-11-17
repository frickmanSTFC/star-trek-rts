using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

// The campaign's Fit Out Your Ship page (docs/fit-out-contract.md, design page docs/mockups/loadout "Screen"): between
// Choose Your Ship and the star-system picker, only for a ship the fit-out data lists (FitOutRules.Applies; phase 1 =
// the two colony ships). No fit-out data, or a ship it doesn't list: the page is skipped and the game starts as before.
// Layout: the title and the ship strip on top (crew to assign, hold, what comes with the ship); three columns: focus
// tiles + the perk + the crew split (stacked bar, one row per division with −, slider, +) | the hold by cargo group
// (− count + per line, group totals and floors, the hold bar; a line's description is its hover text) | the summary
// (landing stock and vehicles, every division's levers with their factor, notes); ‹ SHIP, RESET TO PRESET, LAUNCH.
// Rules as the mock's script: a focus tile loads that focus's preset (crew and cargo); a division can't go below its
// floor and can't rise while nobody is unassigned (the note says why); cargo can't pass the hold, a line's own minimum
// and maximum, or take a group below its floor; LAUNCH is off while crew are unassigned or FitOutRules.Problem objects.
// Every number shown comes from GameData.FitOut and FitOutRules (the sim uses the same functions), none is written here.
// The page is built once per visit (and on a resize); changes only update the labels (once a frame, when something
// changed). The choice is kept while the menu is open and handed to GameSetup.StartCampaign by the system page.
public partial class MainMenu
{
    /// <summary>The ship the fit-out below was made for (a ShipChoices id); null = none made yet.</summary>
    private string? _fitShip;
    private string _fitFocus = "";
    /// <summary>Heads per division, in the data's division order.</summary>
    private int[] _fitCrew = System.Array.Empty<int>();
    /// <summary>The least each division may hold under the current focus (FitOutRules.CrewFloor).</summary>
    private int[] _fitFloors = System.Array.Empty<int>();
    /// <summary>Count per cargo line (every line the data has, 0 included).</summary>
    private readonly Dictionary<string, int> _fitCargo = new();

    private FitWidgets? _fit;
    private bool _fitDirty;
    private bool _fitUpdating; // RefreshFitOut is moving sliders: their changed signals are not the player's
    private string _fitSay = "";
    private string _fitFlash = "";
    private double _fitFlashUntil;
    private readonly Dictionary<string, int> _fitGroupUsed = new();

    /// <summary>What RefreshFitOut writes to (valid while the page is up; rebuilt with it).</summary>
    private sealed class FitWidgets
    {
        public readonly List<(string Id, Button Tile, Color Colour)> Tiles = new();
        public string TilesFocus = "\n"; // the focus the tiles are styled for
        public Label FocusRule = null!, FocusTitle = null!, FocusPerk = null!;
        public StyleBoxFlat FocusBox = null!;
        public Label CrewTotal = null!, CrewNote = null!;
        public FitStackBar CrewBar = null!, HoldBar = null!;
        public readonly List<FitCrewRow> Crew = new();
        public Label HoldTotal = null!, CargoNote = null!;
        public readonly List<FitGroupRow> Groups = new();
        public readonly List<FitCargoRow> Cargo = new();
        public VBoxContainer Stock = null!, Notes = null!;
        public string StockKey = "\n", NotesKey = "\n";
        public readonly List<FitDivisionSummary> Divisions = new();
        public readonly List<(FitOutLever Lever, Label Value)> Levers = new();
        public Button Launch = null!;
        public Label Say = null!;
    }

    private sealed class FitCrewRow
    {
        public HSlider Slider = null!;
        public Button Down = null!, Up = null!;
        public Label Count = null!, Floor = null!, Strength = null!;
    }

    private sealed class FitGroupRow
    {
        public FitOutCargoGroup Group = null!;
        public Label Total = null!;
    }

    private sealed class FitCargoRow
    {
        public FitOutCargoLine Line = null!;
        public Button Down = null!, Up = null!;
        public Label Count = null!;
    }

    private sealed class FitDivisionSummary
    {
        public Label Head = null!, Strength = null!;
    }

    // ---------------------------------------------------------------- flow

    /// <summary>Whether this ship gets the page: the data has fit-out rules, lists the ship, and its suggested preset
    /// exists. Anything else (no data, a ship that never lands, rules that throw) = no page, the game as before.</summary>
    private bool FitOutApplies(ShipChoiceDef? ship)
    {
        if (ship == null || Data is not { FitOut: { } rules } data) return false;
        try
        {
            if (!FitOutRules.Applies(data, ship.Id) || rules.Ship(ship.Id) is not { } budget) return false;
            if (rules.Divisions.Count == 0 || rules.Focuses.Count == 0 || budget.Crew <= 0) return false;
            return FitOutRules.Preset(data, ship.Id, StartFocus(rules, budget)) != null;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Main menu: no fit-out for {ship.Id}: {e.Message}");
            return false;
        }
    }

    /// <summary>The focus a ship starts on: the one it suggests, else the data's first.</summary>
    private static string StartFocus(FitOutData rules, FitOutShip budget) => rules.Focus(budget.Suggest)?.Id ?? rules.Focuses[0].Id;

    /// <summary>The fit-out to start the campaign with: the page's choice for the picked ship, or null (no page for it).</summary>
    private FitOut? ChosenFitOut()
    {
        if (PickedShip is not { } ship || !FitOutApplies(ship) || Data is not { } data) return null;
        return EnsureFitOut(data, ship.Id) ? CurrentFitOut() : null;
    }

    /// <summary>"  ·  EXPLORER FIT-OUT" for the system page's heading; empty when the ship has no fit-out.</summary>
    private string FitOutTag(ShipChoiceDef ship) =>
        _fitShip == ship.Id && FitOutApplies(ship) && Data?.FitOut?.Focus(_fitFocus) is { } focus ? $"  ·  {focus.Name.ToUpperInvariant()} FIT-OUT" : "";

    private FitOut CurrentFitOut() => new(_fitFocus, (int[])_fitCrew.Clone(), new Dictionary<string, int>(_fitCargo));

    /// <summary>Makes sure the state is a fit-out for this ship: kept when it already is (back from the system page),
    /// else the suggested focus's preset. False when there is none to be had.</summary>
    private bool EnsureFitOut(GameData data, string shipId)
    {
        if (data.FitOut is not { } rules || rules.Ship(shipId) is not { } budget) return false;
        if (_fitShip == shipId && _fitCrew.Length == rules.Divisions.Count && rules.Focus(_fitFocus) != null) return true;
        return LoadFitPreset(data, shipId, StartFocus(rules, budget));
    }

    /// <summary>Replaces crew and cargo with a focus's preset (FitOutRules.Preset). False when there is none.</summary>
    private bool LoadFitPreset(GameData data, string shipId, string focusId)
    {
        if (data.FitOut is not { } rules) return false;
        FitOut? preset;
        try
        {
            preset = FitOutRules.Preset(data, shipId, focusId);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Main menu: no {focusId} preset for {shipId}: {e.Message}");
            preset = null;
        }
        if (preset == null) return false;
        _fitShip = shipId;
        _fitFocus = rules.Focus(preset.Focus)?.Id ?? focusId;
        _fitCrew = new int[rules.Divisions.Count];
        for (var i = 0; i < _fitCrew.Length; i++) _fitCrew[i] = i < preset.Crew.Count ? preset.Crew[i] : 0;
        _fitCargo.Clear();
        foreach (var line in rules.Cargo) _fitCargo[line.Id] = preset.Cargo.TryGetValue(line.Id, out var n) ? n : 0;
        _fitFloors = new int[rules.Divisions.Count];
        for (var i = 0; i < _fitFloors.Length; i++) _fitFloors[i] = FitOutRules.CrewFloor(data, shipId, _fitFocus, rules.Divisions[i].Id);
        return true;
    }

    /// <summary>The most division i may hold: the ship's crew less every other division's floor.</summary>
    private int CrewMaxOf(int i, int crew)
    {
        var max = crew;
        for (var k = 0; k < _fitFloors.Length; k++)
            if (k != i) max -= _fitFloors[k];
        return Mathf.Max(max, _fitFloors[i]);
    }

    private int CrewAssigned()
    {
        var sum = 0;
        foreach (var n in _fitCrew) sum += n;
        return sum;
    }

    /// <summary>The − and + buttons move this many heads (the slider moves one).</summary>
    private static int CrewStep(int crew) => crew >= 100 ? 5 : 1;

    private void LeaveFitOut() => _fit = null;

    // ---------------------------------------------------------------- the page

    private Control BuildFitOutPage()
    {
        var (root, column) = NewPage(0.66f, 32f);
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        _fit = null;
        var ship = PickedShip;
        if (Data is not { FitOut: { } rules } data || ship == null || rules.Ship(ship.Id) is not { } budget || !EnsureFitOut(data, ship.Id))
        {
            Header(column, "CAMPAIGN  ·  FEDERATION  ·  DAWN OF WARP", "FIT OUT YOUR SHIP", "This ship has no fit-out. Go back and carry on from the ship page.");
            column.AddChild(Spacer());
            var b = BottomBar(column);
            b.AddChild(Action("‹  SHIP", false, () => ShowPage(Page.Ship)));
            return root;
        }
        var w = new FitWidgets();

        // Title on the left, the ship strip beside it.
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", Mathf.RoundToInt(16 * _s));
        column.AddChild(top);
        var head = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        head.AddThemeConstantOverride("separation", 0);
        head.AddChild(HudKit.Label("CAMPAIGN  ·  FEDERATION  ·  DAWN OF WARP", 13 * _s, HudKit.Accent, condensed: true, bold: true));
        head.AddChild(HudKit.Label("FIT OUT YOUR SHIP", 30 * _s, HudKit.Ink, condensed: true, bold: true));
        top.AddChild(head);
        top.AddChild(FitShipStrip(ship, budget));

        var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * _s));
        column.AddChild(columns);

        // Left: focus, then the crew split (the column scrolls if a big UI scale makes it taller than the page).
        var (leftScroll, left) = FitScroll();
        leftScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.SizeFlagsStretchRatio = 1.05f;
        columns.AddChild(leftScroll);
        left.AddChild(FitFocusPanel(w, rules, budget));
        left.AddChild(FitCrewPanel(w, rules, budget));

        columns.AddChild(FitCargoPanel(w, rules));
        columns.AddChild(FitSummaryPanel(w, rules));

        var bar = BottomBar(column);
        bar.AddChild(FitFocusable(Action("‹  SHIP", false, () => ShowPage(Page.Ship), 120f)));
        var reset = FitFocusable(Action("RESET TO PRESET", false, ResetFitOut, 170f));
        reset.TooltipText = "Load this focus's crew split and cargo again";
        bar.AddChild(reset);
        w.Say = HudKit.Label("", 13 * _s, HudKit.Dim, condensed: true, bold: true);
        w.Say.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        w.Say.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        w.Say.HorizontalAlignment = HorizontalAlignment.Right;
        w.Say.ClipText = true;
        w.Say.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        bar.AddChild(w.Say);
        w.Launch = FitFocusable(Action("LAUNCH  ›", true, LaunchFitOut, 170f));
        Sfx.Tag(w.Launch, "confirm");
        bar.AddChild(w.Launch);

        _fit = w;
        _fitDirty = true;
        return root;
    }

    /// <summary>After the page is in the tree: fill it in and put the keyboard focus on LAUNCH (Enter takes the preset).</summary>
    private void AfterFitOutBuilt()
    {
        if (_fit == null) return;
        _fitDirty = false;
        RefreshFitOut();
        _fit.Launch.GrabFocus();
    }

    /// <summary>Once a frame while the page is up: the amber crew note times out, and anything changed is redrawn.</summary>
    private void UpdateFitOut()
    {
        if (_fit == null) return;
        if (_fitFlash.Length > 0 && _time >= _fitFlashUntil)
        {
            _fitFlash = "";
            _fitDirty = true;
        }
        if (!_fitDirty) return;
        _fitDirty = false;
        try
        {
            RefreshFitOut();
        }
        catch (System.Exception e)
        {
            GD.PushError($"Main menu: the fit-out page couldn't refresh: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- building blocks

    private (ScrollContainer Scroll, VBoxContainer Box) FitScroll()
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        scroll.AddChild(box);
        return (scroll, box);
    }

    private (PanelContainer Panel, VBoxContainer Box) FitPanel()
    {
        var panel = HudKit.PanelBox(HudKit.Panel, HudKit.Line, 8 * _s, 12 * _s, 9 * _s);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        panel.AddChild(box);
        return (panel, box);
    }

    /// <summary>A panel's heading: the caption on the left, a live figure on the right.</summary>
    private Label FitHeading(VBoxContainer box, string caption)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        row.AddChild(HudKit.Label(caption, 15 * _s, HudKit.Sky, condensed: true, bold: true));
        var figure = HudKit.Label("", 13 * _s, HudKit.Ink, condensed: true, bold: true);
        figure.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        figure.HorizontalAlignment = HorizontalAlignment.Right;
        figure.ClipText = true;
        row.AddChild(figure);
        box.AddChild(row);
        return figure;
    }

    private Label FitCaption(string text) => HudKit.Label(text, 12 * _s, HudKit.Dim, condensed: true, bold: true);

    private Label FitWrapped(string text, float size, Color colour)
    {
        var l = HudKit.Label(text, size * _s, colour);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    /// <summary>A ring round a control that has the keyboard focus.</summary>
    private StyleBoxFlat FitFocusRing(float radius)
    {
        var ring = new StyleBoxFlat { DrawCenter = false, BorderColor = HudKit.Sky };
        ring.SetBorderWidthAll(Mathf.Max(2, Mathf.RoundToInt(2 * _s)));
        ring.SetCornerRadiusAll(Mathf.RoundToInt(radius));
        ring.SetExpandMarginAll(Mathf.Round(2 * _s));
        return ring;
    }

    /// <summary>Lets Tab and the arrow keys reach a button (the menu's other pages don't use keyboard focus).</summary>
    private Button FitFocusable(Button b, float radius = 6f)
    {
        b.FocusMode = Control.FocusModeEnum.All;
        b.AddThemeStyleboxOverride("focus", FitFocusRing(radius * _s));
        return b;
    }

    /// <summary>A small − or + button.</summary>
    private Button FitSquare(string text, float width, float height)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width * _s, height * _s), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(16 * _s));
        HudKit.StyleTile(b, 5 * _s);
        return FitFocusable(b, 5f);
    }

    private static void FitSet(Label label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    private static void FitSet(Label label, string text, Color colour)
    {
        if (label.Text != text) label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }

    private static void FitSet(Button button, bool disabled, string tooltip)
    {
        if (button.Disabled != disabled) button.Disabled = disabled;
        if (button.TooltipText != tooltip) button.TooltipText = tooltip;
    }

    private static string Containers(int n) => n == 1 ? "1 container" : $"{n} containers";

    // ---------------------------------------------------------------- ship strip

    /// <summary>The picked ship: its picture, name and class, what there is to assign and load, and what comes with it.</summary>
    private Control FitShipStrip(ShipChoiceDef ship, FitOutShip budget)
    {
        var panel = HudKit.PanelBox(HudKit.Panel, HudKit.Line, 8 * _s, 10 * _s, 6 * _s);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * _s));
        panel.AddChild(row);

        var art = ShipArtRect(ship, new Vector2(96 * _s, 54 * _s), 11f);
        art.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        art.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(art);

        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * _s));
        row.AddChild(text);

        var names = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * _s));
        text.AddChild(names);
        names.AddChild(HudKit.Label(ship.Name.ToUpperInvariant(), 20 * _s, HudKit.Accent, condensed: true, bold: true));
        var cls = HudKit.Label($"{ship.ClassLabel}  ·  {ship.Crew}  ·  mission: {ship.MissionTitle}", 13 * _s, HudKit.Dim);
        cls.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cls.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        cls.ClipText = true;
        cls.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(cls);

        var tags = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tags.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        text.AddChild(tags);
        tags.AddChild(Chip($"CREW TO ASSIGN {budget.Crew:N0}", HudKit.Sky));
        tags.AddChild(Chip($"HOLD {Containers(budget.Hold).ToUpperInvariant()}", HudKit.Sky));
        var comes = new List<string>();
        comes.AddRange(ship.StartBonus.Select(kv => $"+{kv.Value:N0} {ResourceName(kv.Key)}"));
        comes.AddRange(ship.StartUnits.GroupBy(u => u).Select(g => $"{(g.Count() > 1 ? $"{g.Count()}× " : "")}{UnitName(g.Key)}"));
        if (comes.Count > 0)
        {
            var with = HudKit.Label($"With the ship: {string.Join(", ", comes)}", 13 * _s, HudKit.Ink);
            with.MouseFilter = Control.MouseFilterEnum.Pass; // the hover text has the whole list
            with.TooltipText = $"Comes with the {ship.Name} whatever you load:\n{string.Join("\n", comes)}";
            with.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            with.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            with.ClipText = true;
            with.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            tags.AddChild(with);
        }
        return panel;
    }

    // ---------------------------------------------------------------- focus

    private Control FitFocusPanel(FitWidgets w, FitOutData rules, FitOutShip budget)
    {
        var (panel, box) = FitPanel();
        w.FocusRule = FitHeading(box, "FOCUS");

        var grid = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * _s));
        grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(6 * _s));
        box.AddChild(grid);
        foreach (var focus in rules.Focuses)
        {
            var suggested = focus.Id == budget.Suggest;
            var colour = FitOutText.Fill(focus.Division);
            var division = rules.Divisions.FirstOrDefault(d => d.Id == focus.Division);
            var tile = new Button
            {
                FocusMode = Control.FocusModeEnum.All,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 40 * _s),
                ClipContents = true,
                TooltipText = focus.Line + (suggested ? "\nSuggested for this ship." : "") + (focus.PerkText.Length > 0 ? $"\nPerk: {focus.PerkText}" : ""),
            };
            var id = focus.Id;
            tile.Pressed += () => PickFocus(id);
            var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            lines.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            lines.AddThemeConstantOverride("separation", 0);
            tile.AddChild(lines);
            var name = HudKit.Label(focus.Name.ToUpperInvariant(), 15 * _s, FitOutText.Text(focus.Division), condensed: true, bold: true);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.ClipText = true;
            lines.AddChild(name);
            var sub = HudKit.Label(suggested ? "SUGGESTED" : division != null ? division.Name.ToUpperInvariant() : "NO LEAD DIVISION",
                11 * _s, suggested ? HudKit.Good : HudKit.Dim, condensed: true, bold: true);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            sub.ClipText = true;
            lines.AddChild(sub);
            grid.AddChild(tile);
            w.Tiles.Add((focus.Id, tile, colour));
        }

        // The picked focus in words, and its perk.
        var info = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        w.FocusBox = HudKit.Box(HudKit.Chip, HudKit.Line, 6 * _s, 9 * _s, 5 * _s);
        info.AddThemeStyleboxOverride("panel", w.FocusBox);
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * _s));
        info.AddChild(text);
        w.FocusTitle = FitWrapped("", 13, HudKit.Ink);
        w.FocusTitle.AddThemeFontOverride("font", HudKit.Condensed(true));
        text.AddChild(w.FocusTitle);
        w.FocusPerk = FitWrapped("", 13, HudKit.Ink);
        text.AddChild(w.FocusPerk);
        box.AddChild(info);
        return panel;
    }

    private void StyleFocusTile(Button tile, Color colour, bool selected)
    {
        var r = 5 * _s;
        tile.AddThemeStyleboxOverride("normal", HudKit.Box(selected ? HudKit.Btn.Lerp(colour, 0.24f) : HudKit.Btn, selected ? colour : new Color(colour, 0.45f), r, 0, 0, selected ? 2 : 1));
        tile.AddThemeStyleboxOverride("hover", HudKit.Box(HudKit.Btn.Lerp(colour, selected ? 0.3f : 0.12f), colour, r, 0, 0, 2));
        tile.AddThemeStyleboxOverride("pressed", HudKit.Box(HudKit.Btn.Lerp(colour, 0.36f), colour, r, 0, 0, 2));
        tile.AddThemeStyleboxOverride("focus", FitFocusRing(r));
    }

    /// <summary>A focus tile: that focus's preset replaces the crew split and the cargo.</summary>
    private void PickFocus(string focusId)
    {
        if (_fit == null || Data is not { FitOut: { } rules } data || _fitShip is not { } shipId) return;
        if (!LoadFitPreset(data, shipId, focusId)) return;
        _fitFlash = "";
        _fitSay = $"{rules.Focus(_fitFocus)?.Name ?? focusId} preset loaded.";
        _fitDirty = true;
    }

    private void ResetFitOut() => PickFocus(_fitFocus);

    // ---------------------------------------------------------------- crew

    private Control FitCrewPanel(FitWidgets w, FitOutData rules, FitOutShip budget)
    {
        var (panel, box) = FitPanel();
        w.CrewTotal = FitHeading(box, "CREW");
        w.CrewBar = new FitStackBar { CustomMinimumSize = new Vector2(0, 10 * _s) };
        box.AddChild(w.CrewBar);

        var step = CrewStep(budget.Crew);
        for (var i = 0; i < rules.Divisions.Count; i++)
        {
            var division = rules.Divisions[i];
            var index = i;
            var row = new FitCrewRow();
            var block = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            block.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * _s));
            box.AddChild(block);

            var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = division.Who }; // hover: who they are
            head.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
            block.AddChild(head);
            head.AddChild(HudKit.Label(division.Name.ToUpperInvariant(), 15 * _s, FitOutText.Text(division.Id), condensed: true, bold: true));
            row.Floor = HudKit.Label("", 11 * _s, HudKit.Dim, condensed: true, bold: true);
            row.Floor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.Floor.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.Floor.ClipText = true;
            head.AddChild(row.Floor);
            row.Count = HudKit.Label("", 13 * _s, HudKit.Ink, condensed: true, bold: true);
            row.Count.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            head.AddChild(row.Count);
            row.Strength = HudKit.Label("", 13 * _s, HudKit.Dim, condensed: true, bold: true);
            row.Strength.CustomMinimumSize = new Vector2(56 * _s, 0);
            row.Strength.HorizontalAlignment = HorizontalAlignment.Right;
            row.Strength.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            head.AddChild(row.Strength);

            var controls = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            controls.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
            block.AddChild(controls);
            row.Down = FitSquare("−", 28, 22);
            row.Down.Pressed += () => StepCrew(index, -step);
            controls.AddChild(row.Down);
            row.Slider = FitCrewSlider(division);
            row.Slider.ValueChanged += v => OnCrewSlider(index, Mathf.RoundToInt((float)v));
            controls.AddChild(row.Slider);
            row.Up = FitSquare("+", 28, 22);
            row.Up.Pressed += () => StepCrew(index, step);
            controls.AddChild(row.Up);
            w.Crew.Add(row);
        }

        w.CrewNote = FitWrapped("", 12, HudKit.Dim);
        box.AddChild(w.CrewNote);
        return panel;
    }

    private HSlider FitCrewSlider(FitOutDivision division)
    {
        var slider = new HSlider
        {
            Step = 1,
            Rounded = true,
            Scrollable = false, // the wheel scrolls the column, not the crew
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(60 * _s, 22 * _s),
            TooltipText = $"{division.Name} crew: drag, or Left / Right with the keyboard",
        };
        var colour = FitOutText.Fill(division.Id);
        var track = HudKit.Box(HudKit.Chip, HudKit.BtnEdge, 3 * _s);
        track.ContentMarginTop = track.ContentMarginBottom = 3 * _s;
        slider.AddThemeStyleboxOverride("slider", track);
        var fill = HudKit.Box(new Color(colour, 0.85f), new Color(0, 0, 0, 0), 3 * _s, 0, 0, 0);
        fill.ContentMarginTop = fill.ContentMarginBottom = 3 * _s;
        slider.AddThemeStyleboxOverride("grabber_area", fill);
        var lit = HudKit.Box(FitOutText.Text(division.Id), new Color(0, 0, 0, 0), 3 * _s, 0, 0, 0); // hovered or focused
        lit.ContentMarginTop = lit.ContentMarginBottom = 3 * _s;
        slider.AddThemeStyleboxOverride("grabber_area_highlight", lit);
        return slider;
    }

    private void OnCrewSlider(int i, int value)
    {
        if (_fitUpdating) return;
        SetCrew(i, value);
    }

    private void StepCrew(int i, int by)
    {
        if (i < 0 || i >= _fitCrew.Length) return;
        SetCrew(i, _fitCrew[i] + by);
    }

    /// <summary>Moves a division to a new head count inside its floor, its ceiling and the crew still unassigned; held
    /// back by the last of these, the crew note says why for a moment.</summary>
    private void SetCrew(int i, int want)
    {
        if (_fit == null || i < 0 || i >= _fitCrew.Length || i >= _fitFloors.Length) return;
        if (Data?.FitOut?.Ship(_fitShip) is not { } budget) return;
        var current = _fitCrew[i];
        var cap = current + Mathf.Max(0, budget.Crew - CrewAssigned());
        var value = Mathf.Clamp(want, _fitFloors[i], CrewMaxOf(i, budget.Crew));
        var blocked = value > cap;
        if (blocked) value = cap;
        _fitCrew[i] = value;
        _fitSay = "";
        if (blocked)
        {
            if (_fitFlash.Length == 0) Sfx.Refused(); // once per message, not on every step of a drag
            _fitFlash = cap > current ? $"Only {cap - current} unassigned. Lower another division for more." : "Nobody is unassigned. Lower another division first.";
            _fitFlashUntil = _time + 2.2;
        }
        else _fitFlash = "";
        _fitDirty = true;
        // A slider dragged past what is free snaps back at once (the rest is redrawn next frame).
        if (i < _fit.Crew.Count && Mathf.RoundToInt((float)_fit.Crew[i].Slider.Value) != value)
        {
            _fitUpdating = true;
            _fit.Crew[i].Slider.SetValueNoSignal(value);
            _fitUpdating = false;
        }
    }

    // ---------------------------------------------------------------- cargo

    private Control FitCargoPanel(FitWidgets w, FitOutData rules)
    {
        var (panel, box) = FitPanel();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        w.HoldTotal = FitHeading(box, "CARGO");
        w.HoldBar = new FitStackBar { CustomMinimumSize = new Vector2(0, 10 * _s) };
        box.AddChild(w.HoldBar);

        var (scroll, list) = FitScroll();
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(7 * _s));
        box.AddChild(scroll);
        foreach (var group in rules.Groups)
        {
            var lines = rules.Cargo.Where(l => l.Group == group.Id).ToList();
            if (lines.Count == 0) continue; // only what the data has
            list.AddChild(FitCargoGroup(w, group, group.Name, lines));
        }
        // Lines whose group the data doesn't list still show, under their own heading with no minimum.
        var stray = rules.Cargo.Where(l => rules.Groups.All(g => g.Id != l.Group)).ToList();
        if (stray.Count > 0) list.AddChild(FitCargoGroup(w, null, "OTHER CARGO", stray));

        w.CargoNote = FitWrapped("", 12, HudKit.Dim);
        box.AddChild(w.CargoNote);
        return panel;
    }

    private Control FitCargoGroup(FitWidgets w, FitOutCargoGroup? group, string name, List<FitOutCargoLine> lines)
    {
        var block = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        block.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * _s));
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddChild(HudKit.Label(name.ToUpperInvariant(), 13 * _s, FitOutText.Text(group?.Id), condensed: true, bold: true));
        var total = HudKit.Label("", 12 * _s, HudKit.Dim, condensed: true, bold: true);
        total.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        total.HorizontalAlignment = HorizontalAlignment.Right;
        total.ClipText = true;
        head.AddChild(total);
        block.AddChild(head);
        if (group != null) w.Groups.Add(new FitGroupRow { Group = group, Total = total });

        foreach (var line in lines)
        {
            var cargo = new FitCargoRow { Line = line };
            var row = new HBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Pass, // hover: what one gives
                TooltipText = $"{line.Name} ({Containers(line.Containers)} each)\n{line.Text}\nAt least {line.Min}, at most {line.Max}.",
            };
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
            var label = HudKit.Label(line.Name, 13 * _s, HudKit.Ink);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.AddChild(label);
            var size = HudKit.Label(Containers(line.Containers).ToUpperInvariant(), 11 * _s, HudKit.Dim, condensed: true, bold: true);
            size.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(size);
            cargo.Down = FitSquare("−", 26, 22);
            cargo.Down.Pressed += () => StepCargo(line, -1);
            row.AddChild(cargo.Down);
            cargo.Count = HudKit.Label("0", 15 * _s, HudKit.Ink, condensed: true, bold: true);
            cargo.Count.CustomMinimumSize = new Vector2(22 * _s, 0);
            cargo.Count.HorizontalAlignment = HorizontalAlignment.Center;
            cargo.Count.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(cargo.Count);
            cargo.Up = FitSquare("+", 26, 22);
            cargo.Up.Pressed += () => StepCargo(line, 1);
            row.AddChild(cargo.Up);
            block.AddChild(row);
            w.Cargo.Add(cargo);
        }
        return block;
    }

    private int CargoCount(string lineId) => _fitCargo.TryGetValue(lineId, out var n) ? n : 0;

    /// <summary>Containers each group's lines take (into the reused table).</summary>
    private void CountGroups(FitOutData rules)
    {
        _fitGroupUsed.Clear();
        foreach (var line in rules.Cargo)
            _fitGroupUsed[line.Group] = (_fitGroupUsed.TryGetValue(line.Group, out var n) ? n : 0) + CargoCount(line.Id) * line.Containers;
    }

    private static int GroupFloor(FitOutData rules, string groupId)
    {
        foreach (var g in rules.Groups)
            if (g.Id == groupId) return g.Floor;
        return 0;
    }

    /// <summary>One more or one fewer of a cargo line, if the line's own limits, the hold and the group's floor allow.</summary>
    private void StepCargo(FitOutCargoLine line, int by)
    {
        if (_fit == null || Data is not { FitOut: { } rules } data || rules.Ship(_fitShip) is not { } budget) return;
        var value = CargoCount(line.Id) + by;
        if (value < line.Min || value > line.Max) return;
        if (by > 0 && FitOutRules.ContainersUsed(data, CurrentFitOut()) + line.Containers > budget.Hold) return;
        CountGroups(rules);
        if (by < 0 && (_fitGroupUsed.TryGetValue(line.Group, out var used) ? used : 0) - line.Containers < GroupFloor(rules, line.Group)) return;
        _fitCargo[line.Id] = value;
        _fitSay = "";
        _fitDirty = true;
    }

    // ---------------------------------------------------------------- summary

    private Control FitSummaryPanel(FitWidgets w, FitOutData rules)
    {
        var (panel, box) = FitPanel();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(HudKit.Label("WHAT THIS GIVES YOU", 15 * _s, HudKit.Sky, condensed: true, bold: true));
        var (scroll, list) = FitScroll();
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * _s));
        box.AddChild(scroll);

        list.AddChild(FitCaption("AT LANDING"));
        w.Stock = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        w.Stock.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * _s));
        list.AddChild(w.Stock);

        list.AddChild(FitCaption("DURING PLAY"));
        foreach (var division in rules.Divisions)
        {
            var block = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            block.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * _s));
            list.AddChild(block);
            var colour = FitOutText.Text(division.Id);
            var (headRow, head, strength) = FitPair("", "", 13, colour, true);
            block.AddChild(headRow);
            w.Divisions.Add(new FitDivisionSummary { Head = head, Strength = strength });
            var any = false;
            foreach (var lever in rules.Levers.Where(l => l.Division == division.Id))
            {
                var (leverRow, _, value) = FitPair(lever.Name, "", 13, HudKit.Ink, false);
                block.AddChild(leverRow);
                w.Levers.Add((lever, value));
                any = true;
            }
            if (!any) block.AddChild(HudKit.Label("Nothing yet", 13 * _s, HudKit.Dim));
        }

        list.AddChild(FitCaption("NOTES"));
        w.Notes = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        w.Notes.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * _s));
        list.AddChild(w.Notes);
        return panel;
    }

    /// <summary>A summary row: a name that gives way (clipped) and a figure on the right.</summary>
    private (HBoxContainer Row, Label Name, Label Value) FitPair(string name, string value, float size, Color colour, bool heading)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        var left = HudKit.Label(name, size * _s, colour, condensed: heading, bold: heading);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.ClipText = true;
        left.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(left);
        var right = HudKit.Label(value, size * _s, colour, condensed: true, bold: true);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(right);
        return (row, left, right);
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Writes the whole state into the page: figures, bars, what each button may do, the summary, LAUNCH.</summary>
    private void RefreshFitOut()
    {
        if (_fit is not { } w || Data is not { FitOut: { } rules } data || PickedShip is not { } ship || rules.Ship(ship.Id) is not { } budget) return;
        if (_fitShip != ship.Id || _fitCrew.Length != rules.Divisions.Count || _fitFloors.Length != rules.Divisions.Count) return;
        var fit = CurrentFitOut();
        var focus = rules.Focus(_fitFocus);
        var crew = Mathf.Max(1, budget.Crew);
        var free = budget.Crew - CrewAssigned();
        _fitUpdating = true;
        try
        {
            RefreshFitControls(w, data, rules, ship, budget, fit, focus, crew, free);
        }
        finally
        {
            _fitUpdating = false;
        }
    }

    private void RefreshFitControls(FitWidgets w, GameData data, FitOutData rules, ShipChoiceDef ship, FitOutShip budget, FitOut fit, FitOutFocus? focus, int crew, int free)
    {
        // Focus: the tiles (restyled only when the pick changed), the rule, the perk.
        if (w.TilesFocus != _fitFocus)
        {
            w.TilesFocus = _fitFocus;
            foreach (var (id, tile, colour) in w.Tiles) StyleFocusTile(tile, colour, id == _fitFocus);
        }
        var lead = focus == null ? null : rules.Divisions.FirstOrDefault(d => d.Id == focus.Division);
        FitSet(w.FocusRule, lead != null
            ? $"{lead.Name.ToUpperInvariant()} AT LEAST {Mathf.RoundToInt(rules.FocusFloorShare.ToFloat() * 100f)}%"
            : $"EVERY DIVISION AT LEAST {Mathf.RoundToInt(rules.CrewFloorShare.ToFloat() * 100f)}%");
        var focusFill = FitOutText.Fill(focus?.Division);
        w.FocusBox.BgColor = HudKit.Chip.Lerp(focusFill, 0.1f);
        w.FocusBox.BorderColor = new Color(focusFill, 0.7f);
        FitSet(w.FocusTitle, focus == null ? "" : $"{focus.Name.ToUpperInvariant()}  ·  {focus.Line}", FitOutText.Text(focus?.Division));
        FitSet(w.FocusPerk, focus == null || focus.PerkText.Length == 0 ? "No perk." : $"Perk: {focus.PerkText}");

        // Crew.
        FitSet(w.CrewTotal, $"{budget.Crew - free:N0} OF {budget.Crew:N0} ASSIGNED", free != 0 ? HudKit.Warn : HudKit.Ink);
        var step = CrewStep(budget.Crew);
        w.CrewBar.Begin();
        for (var i = 0; i < rules.Divisions.Count && i < w.Crew.Count; i++)
        {
            var division = rules.Divisions[i];
            var row = w.Crew[i];
            var value = _fitCrew[i];
            var floor = _fitFloors[i];
            var max = CrewMaxOf(i, budget.Crew);
            var share = 100f * value / crew;
            var strength = Mathf.RoundToInt(FitOutRules.Strength(data, ship.Id, fit, division.Id).ToFloat() * 100f);
            FitSet(row.Count, $"{value:N0} · {Mathf.RoundToInt(share)}%");
            FitSet(row.Floor, $"AT LEAST {floor:N0}" + (focus?.Division == division.Id ? " · YOUR FOCUS" : ""));
            FitSet(row.Strength, FitOutText.Signed(strength), strength > 0 ? HudKit.Good : strength < 0 ? FitOutText.Bad : HudKit.Dim);
            FitSet(row.Down, value <= floor, value <= floor ? $"{division.Name} needs at least {floor:N0}" : $"{step} fewer in {division.Name}");
            FitSet(row.Up, free <= 0 || value >= max,
                free <= 0 ? "Nobody is unassigned. Lower another division first."
                : value >= max ? "The other divisions need their minimum."
                : $"{step} more in {division.Name}");
            row.Slider.MinValue = floor;
            row.Slider.MaxValue = max;
            row.Slider.SetValueNoSignal(value);
            w.CrewBar.Add(value / (float)crew, FitOutText.Fill(division.Id));
        }
        w.CrewBar.End(Mathf.Max(0, free) / (float)crew);
        if (_fitFlash.Length > 0) FitSet(w.CrewNote, _fitFlash, HudKit.Warn);
        else if (free > 0) FitSet(w.CrewNote, $"{free:N0} unassigned. Give them a division before launch.", HudKit.Warn);
        else FitSet(w.CrewNote, "Everyone has a division. To raise one, lower another first.", HudKit.Dim);

        // Cargo.
        var used = FitOutRules.ContainersUsed(data, fit);
        var left = budget.Hold - used;
        var hold = Mathf.Max(1, budget.Hold);
        CountGroups(rules);
        FitSet(w.HoldTotal, $"{used} OF {budget.Hold} CONTAINERS", left != 0 ? HudKit.Warn : HudKit.Ink);
        w.HoldBar.Begin();
        foreach (var g in w.Groups)
        {
            var inGroup = _fitGroupUsed.TryGetValue(g.Group.Id, out var n) ? n : 0;
            FitSet(g.Total, g.Group.Floor > 0 ? $"{inGroup} · AT LEAST {g.Group.Floor}" : $"{inGroup} · NO MINIMUM", inGroup < g.Group.Floor ? HudKit.Warn : HudKit.Dim);
            w.HoldBar.Add(inGroup / (float)hold, FitOutText.Fill(g.Group.Id));
        }
        w.HoldBar.End(Mathf.Max(0, left) / (float)hold);
        foreach (var c in w.Cargo)
        {
            var line = c.Line;
            var value = CargoCount(line.Id);
            var inGroup = _fitGroupUsed.TryGetValue(line.Group, out var n) ? n : 0;
            var floor = GroupFloor(rules, line.Group);
            FitSet(c.Count, value.ToString(), value > 0 ? HudKit.Ink : HudKit.Dim);
            var atMin = value <= line.Min;
            var underFloor = inGroup - line.Containers < floor;
            FitSet(c.Down, atMin || underFloor,
                atMin ? (line.Min > 0 ? $"At least {line.Min} must be aboard." : "None aboard.")
                : underFloor ? $"This group needs at least {Containers(floor)}."
                : $"One fewer: {line.Name}");
            var atMax = value >= line.Max;
            var noRoom = used + line.Containers > budget.Hold;
            FitSet(c.Up, atMax || noRoom,
                atMax ? $"At most {line.Max}."
                : noRoom ? (left <= 0 ? "The hold is full. Take something out first." : $"Needs {Containers(line.Containers)}; only {left} free.")
                : $"One more: {line.Name}");
        }
        FitSet(w.CargoNote, left > 0 ? $"{Containers(left)} free. The hold need not be full." : "The hold is full. To add something, take something out.");

        // LAUNCH and the line beside it.
        string? problem = free > 0 ? $"{free:N0} crew still unassigned." : free < 0 ? "More crew assigned than the ship has." : FitOutRules.Problem(data, ship.Id, fit);
        FitSet(w.Launch, problem != null, problem ?? "On to the star system");
        if (problem != null) FitSet(w.Say, problem, HudKit.Warn);
        else if (_fitSay.Length > 0) FitSet(w.Say, _fitSay, HudKit.Ink);
        else FitSet(w.Say, "Tab: next control  ·  Enter: press  ·  Esc: back to the ship", HudKit.Dim);

        RefreshFitSummary(w, data, rules, ship.Id, fit, budget.Crew, free, left, problem);
    }

    private void RefreshFitSummary(FitWidgets w, GameData data, FitOutData rules, string shipId, FitOut fit, int crew, int free, int left, string? problem)
    {
        // At landing: the stock in the data's resource order, then the vehicles. Rebuilt only when it reads differently.
        var stock = FitOutRules.LandingStock(data, shipId, fit);
        var units = FitOutRules.LandingUnits(data, shipId, fit);
        var rows = new List<(string Name, string Value)>();
        foreach (var r in data.Resources)
            if (stock.TryGetValue(r.Id, out var amount) && amount != 0) rows.Add((r.Name, amount.ToString("N0")));
        foreach (var kv in stock)
            if (kv.Value != 0 && data.Resources.All(r => r.Id != kv.Key)) rows.Add((kv.Key.Replace('_', ' '), kv.Value.ToString("N0")));
        var vehicles = units.GroupBy(u => u).Select(g => (Name: Title(UnitName(g.Key)), Value: g.Count().ToString())).ToList();
        var stockKey = string.Join("|", rows.Select(r => $"{r.Name}={r.Value}")) + "#" + string.Join("|", vehicles.Select(v => $"{v.Name}={v.Value}"));
        if (stockKey != w.StockKey)
        {
            w.StockKey = stockKey;
            HudKit.Clear(w.Stock);
            foreach (var (name, value) in rows) w.Stock.AddChild(FitPair(name, value, 13, HudKit.Ink, false).Row);
            if (vehicles.Count > 0)
            {
                w.Stock.AddChild(HudKit.Label("Vehicles", 13 * _s, HudKit.Dim));
                foreach (var (name, value) in vehicles) w.Stock.AddChild(FitPair(name, value, 13, HudKit.Ink, false).Row);
            }
            if (rows.Count == 0 && vehicles.Count == 0) w.Stock.AddChild(HudKit.Label("Nothing listed", 13 * _s, HudKit.Dim));
        }

        // During play: each division's share and strength, each lever's factor.
        for (var i = 0; i < rules.Divisions.Count && i < w.Divisions.Count; i++)
        {
            var division = rules.Divisions[i];
            var strength = Mathf.RoundToInt(FitOutRules.Strength(data, shipId, fit, division.Id).ToFloat() * 100f);
            FitSet(w.Divisions[i].Head, $"{division.Name.ToUpperInvariant()}  {FitOutText.SharePercentOf(fit.Crew[i], crew)}%");
            FitSet(w.Divisions[i].Strength, FitOutText.Signed(strength), strength > 0 ? HudKit.Good : strength < 0 ? FitOutText.Bad : HudKit.Dim);
        }
        foreach (var (lever, value) in w.Levers)
        {
            var percent = Mathf.RoundToInt((FitOutRules.Factor(data, shipId, fit, lever.Id).ToFloat() - 1f) * 100f);
            FitSet(value, FitOutText.Signed(percent), percent == 0 ? HudKit.Dim : (percent > 0) == lever.MoreIsBetter ? HudKit.Good : FitOutText.Bad);
        }

        // Notes.
        var notes = new List<(string Text, Color Colour)>();
        if (free > 0) notes.Add(($"{free:N0} crew still unassigned. They do nothing until they have a division.", HudKit.Warn));
        else if (problem != null) notes.Add((problem, HudKit.Warn));
        if (left > 0) notes.Add(($"{Containers(left)} {(left == 1 ? "is" : "are")} empty.", HudKit.Warn));
        if (FuelTrips(data, stock) is { } trips) notes.Add(($"The fuel covers {trips:N0} drop-ship {(trips == 1 ? "trip" : "trips")} at 1 g, if nothing else burns it.", HudKit.Ink));
        if (notes.Count == 0) notes.Add(("Ready to launch.", HudKit.Good));
        var notesKey = string.Join("|", notes.Select(n => n.Text));
        if (notesKey != w.NotesKey)
        {
            w.NotesKey = notesKey;
            HudKit.Clear(w.Notes);
            foreach (var (text, colour) in notes) w.Notes.AddChild(FitWrapped(text, 13, colour));
        }
    }

    /// <summary>Drop-ship trips the landing stock's fuel pays for at 1 g (the orbit rules' fuel a trip), or null when a
    /// trip costs nothing.</summary>
    private static int? FuelTrips(GameData data, IReadOnlyDictionary<string, int> stock)
    {
        if (data.Orbit is not { } orbit || orbit.ShuttleFuel.Count == 0) return null;
        int? trips = null;
        foreach (var fuel in orbit.ShuttleFuel)
        {
            var each = fuel.Amount.ToFloat();
            if (each <= 0f || fuel.Resource < 0 || fuel.Resource >= data.Resources.Count) continue;
            var have = stock.TryGetValue(data.Resources[fuel.Resource].Id, out var n) ? n : 0;
            var t = Mathf.FloorToInt(have / each);
            trips = trips is { } least ? Mathf.Min(least, t) : t;
        }
        return trips;
    }

    private static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // ---------------------------------------------------------------- launch

    /// <summary>LAUNCH: on to the star system, if everyone is assigned and the rules have no objection.</summary>
    private void LaunchFitOut()
    {
        if (Data is not { FitOut: { } rules } data || PickedShip is not { } ship || rules.Ship(ship.Id) is not { } budget) return;
        if (budget.Crew != CrewAssigned() || FitOutRules.Problem(data, ship.Id, CurrentFitOut()) != null)
        {
            Sfx.Refused();
            _fitDirty = true;
            return;
        }
        ShowPage(Page.System);
    }
}

/// <summary>A stacked bar for the fit-out page: coloured parts from the left (shares of the whole) and the unused rest
/// as a dim hatched part.</summary>
public partial class FitStackBar : Control
{
    private readonly List<(float Share, Color Colour)> _parts = new();
    private float _free;

    public FitStackBar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public void Begin() => _parts.Clear();

    public void Add(float share, Color colour) => _parts.Add((Mathf.Clamp(share, 0f, 1f), colour));

    public void End(float free)
    {
        _free = Mathf.Clamp(free, 0f, 1f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.11f, 0.12f, 0.16f));
        var x = 0f;
        foreach (var (share, colour) in _parts)
        {
            var width = share * size.X;
            if (width <= 0f) continue;
            DrawRect(new Rect2(x, 0, Mathf.Min(width, size.X - x), size.Y), colour);
            x += width;
            if (x >= size.X) return;
            DrawRect(new Rect2(x - 1f, 0, 1f, size.Y), new Color(0f, 0f, 0f, 0.55f)); // a hairline between parts
        }
        if (_free <= 0f) return;
        // The rest: slanted lines (clipped to the bar).
        var hatch = new Color(0.26f, 0.28f, 0.35f);
        var gap = Mathf.Max(5f, size.Y * 0.7f);
        for (var h = x - size.Y; h < size.X; h += gap)
            DrawLine(new Vector2(Mathf.Max(h, x), size.Y - Mathf.Max(0f, x - h)), new Vector2(h + size.Y, 0), hatch, 1.5f);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }
}
