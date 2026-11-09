using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>One order in a fabricator's queue as the building panel shows it (Id = the sim's FabOrder.Id, for ×).</summary>
public sealed record FabOrderView(int Id, string Code, Color Colour, string Name, string Tag, Color TagColour);

/// <summary>
/// A fabricator's queue (Machine Shop, or a colony core's workshop) in the building panel's right column: a caption
/// with the count and time left, the running order with its bar (CurrentId 0 = nothing running: Empty says why), and
/// the next few orders with × (cancel; the sim refunds what it took).
/// </summary>
public sealed record FabView(
    string Caption,
    int CurrentId,
    string CurrentCode,
    Color CurrentColour,
    string CurrentName,
    float Progress,
    string ProgressText,
    IReadOnlyList<FabOrderView> Next,
    string More,
    string Empty);

// Parts economy on the HUD (docs/parts-contract.md "UI"): the fabricator queue column of the building panel, the
// WORKSHOP / INVENTORY chips at the right of the base tabs, command tiles made on demand (recipes), and a tooltip that
// can colour words (inputs red when short).
public partial class PlanetHud
{
    /// <summary>× on a queued or running order (FabOrder.Id).</summary>
    public event Action<int>? FabCancelPressed;

    /// <summary>The WORKSHOP [K] chip (a colony core's workshop view, and back).</summary>
    public event Action? WorkshopPressed;

    /// <summary>The INVENTORY [I] chip.</summary>
    public event Action? InventoryPressed;

    private Control _fabBox = null!;
    private Label _fabCaption = null!;
    private Control _fabCurrentRow = null!;
    private HudKit.CodeChip _fabCurrentChip = null!;
    private Label _fabCurrentName = null!;
    private Button _fabCurrentCancel = null!;
    private Bar _fabBar = null!;
    private Label _fabProgressText = null!;
    private Label _fabEmpty = null!;
    private readonly List<(Control Row, HudKit.CodeChip Chip, Label Name, Label Tag, Button Cancel)> _fabRows = new();
    private readonly int[] _fabRowIds = new int[FabRows];
    private int _fabCurrentId;
    private Label _fabMore = null!;
    private const int FabRows = 3;

    private Control _tabRow = null!;
    private HBoxContainer _tools = null!;
    private Button _workshopChip = null!;
    private Button _inventoryChip = null!;
    private RichTextLabel? _tooltipRich;

    // ---------------------------------------------------------------- fabricator queue

    private Control BuildFabBox()
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        box.AddThemeConstantOverride("separation", 3);
        _fabCaption = Caption("");
        _fabCaption.ClipText = true;
        box.AddChild(_fabCaption);

        var current = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        current.AddThemeConstantOverride("separation", 6);
        _fabCurrentChip = new HudKit.CodeChip(12);
        current.AddChild(_fabCurrentChip.Root);
        _fabCurrentName = MakeLabel("", 14, Ink);
        _fabCurrentName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _fabCurrentName.ClipText = true;
        current.AddChild(_fabCurrentName);
        _fabCurrentCancel = CancelButton("Cancel the running order (the inputs it took come back)");
        _fabCurrentCancel.Pressed += () =>
        {
            if (_fabCurrentId != 0) FabCancelPressed?.Invoke(_fabCurrentId);
        };
        current.AddChild(_fabCurrentCancel);
        _fabCurrentRow = current;
        box.AddChild(current);
        _fabBar = new Bar { Colour = new Color(0.31f, 0.71f, 1f), CustomMinimumSize = new Vector2(0, 7) };
        box.AddChild(_fabBar);
        _fabProgressText = MakeLabel("", 12, Dim);
        _fabProgressText.ClipText = true;
        box.AddChild(_fabProgressText);
        _fabEmpty = MakeLabel("", 12, Dim);
        _fabEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _fabEmpty.CustomMinimumSize = new Vector2(210, 0);
        box.AddChild(_fabEmpty);

        for (var i = 0; i < FabRows; i++)
        {
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            var chip = new HudKit.CodeChip(11);
            row.AddChild(chip.Root);
            var name = MakeLabel("", 13, Ink);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.ClipText = true;
            row.AddChild(name);
            var tag = MakeLabel("", 11, Dim);
            row.AddChild(tag);
            var cancel = CancelButton("Cancel this order");
            var index = i;
            cancel.Pressed += () =>
            {
                if (_fabRowIds[index] != 0) FabCancelPressed?.Invoke(_fabRowIds[index]);
            };
            row.AddChild(cancel);
            box.AddChild(row);
            _fabRows.Add((row, chip, name, tag, cancel));
        }
        _fabMore = MakeLabel("", 11, Dim);
        box.AddChild(_fabMore);
        _fabBox = box;
        return box;
    }

    private static Button CancelButton(string tip)
    {
        var b = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 20), TooltipText = tip };
        b.AddThemeFontSizeOverride("font_size", 13);
        StyleButton(b, Line, new Color(0.04f, 0.06f, 0.11f));
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            if (b.GetThemeStylebox(state) is StyleBoxFlat f)
            {
                f.ContentMarginLeft = 4;
                f.ContentMarginRight = 4;
                f.SetCornerRadiusAll(5);
            }
        return b;
    }

    /// <summary>Fills the queue column (null hides it; the condition / progress boxes come back).</summary>
    private void ShowFab(FabView? v)
    {
        if (_fabBox.Visible != (v != null)) _fabBox.Visible = v != null;
        if (v == null) return;
        _fabCaption.Text = v.Caption;
        _fabCurrentId = v.CurrentId;
        var running = v.CurrentId != 0;
        _fabCurrentRow.Visible = running;
        _fabBar.Visible = running;
        _fabProgressText.Visible = running;
        _fabEmpty.Visible = !running && v.Empty.Length > 0;
        if (running)
        {
            _fabCurrentChip.Set(v.CurrentCode, v.CurrentColour);
            _fabCurrentName.Text = v.CurrentName;
            _fabBar.Fraction = v.Progress;
            _fabBar.Colour = v.CurrentColour;
            _fabProgressText.Text = v.ProgressText;
        }
        else _fabEmpty.Text = v.Empty;
        for (var i = 0; i < _fabRows.Count; i++)
        {
            var r = _fabRows[i];
            var show = i < v.Next.Count;
            if (r.Row.Visible != show) r.Row.Visible = show;
            _fabRowIds[i] = show ? v.Next[i].Id : 0;
            if (!show) continue;
            var o = v.Next[i];
            r.Chip.Set(o.Code, o.Colour);
            r.Name.Text = o.Name;
            r.Tag.Text = o.Tag;
            r.Tag.AddThemeColorOverride("font_color", o.TagColour);
        }
        _fabMore.Visible = v.More.Length > 0;
        _fabMore.Text = v.More;
    }

    // ---------------------------------------------------------------- base tab row chips

    private Control BuildTabRow(HBoxContainer tabs)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(tabs);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _tools = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _tools.AddThemeConstantOverride("separation", 6);
        _workshopChip = ToolChip("WORKSHOP [K]", "machine_shop", "The colony core's workshop: spare kits from materials, half speed, one at a time (K)");
        _workshopChip.ToggleMode = true;
        _workshopChip.Pressed += () => WorkshopPressed?.Invoke();
        _tools.AddChild(_workshopChip);
        _inventoryChip = ToolChip("INVENTORY [I]", "parts", "Parts, spare kits and construction parts: stock, keep-in-stock targets, storage (I)");
        _inventoryChip.Pressed += () => InventoryPressed?.Invoke();
        _tools.AddChild(_inventoryChip);
        row.AddChild(_tools);
        _tabRow = row;
        return row;
    }

    private static Button ToolChip(string text, string icon, string tip)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30), TooltipText = tip, Icon = Icons.Get(icon) };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeConstantOverride("icon_max_width", 16);
        StyleButton(b, Line, new Color(0.04f, 0.07f, 0.13f));
        return b;
    }

    private bool _toolsWanted;

    /// <summary>The chips right of the base tabs: INVENTORY once landed, WORKSHOP while a base whose core has a workshop is selected (pressed while it shows).</summary>
    public void ShowTools(bool inventory, bool workshop, bool workshopOn)
    {
        _toolsWanted = inventory || workshop;
        if (_inventoryChip.Visible != inventory) _inventoryChip.Visible = inventory;
        if (_workshopChip.Visible != workshop) _workshopChip.Visible = workshop;
        _workshopChip.SetPressedNoSignal(workshopOn);
        UpdateTabRow();
    }

    private void UpdateTabRow()
    {
        if (_tools.Visible != _toolsWanted) _tools.Visible = _toolsWanted;
        var show = _tabs.Visible || _toolsWanted;
        if (_tabRow.Visible != show) _tabRow.Visible = show;
    }

    // ---------------------------------------------------------------- command tiles on demand

    /// <summary>A command-card tile for an id the menu didn't list at Setup (a fabricator's recipes, "fab:&lt;item&gt;"); an
    /// existing tile gets the new key label (the same kit is key 7 at a Machine Shop and key 1 at a core's workshop).</summary>
    public void EnsureCommand(string id, string name, string key)
    {
        if (!_commands.ContainsKey(id)) AddCommandButton(id, name, key);
        else if (_commandParts.TryGetValue(id, out var parts) && parts.Key.Text != key) parts.Key.Text = key;
    }

    // ---------------------------------------------------------------- coloured tooltip

    /// <summary>The hover tooltip with BBCode colours ([color=#ff9a6b]…[/color]); empty hides it. Escape '[' in plain
    /// text as [lb].</summary>
    public void ShowRichTooltip(string bbcode, Vector2 mouse)
    {
        _tooltip.Visible = bbcode.Length > 0;
        if (!_tooltip.Visible) return;
        if (_tooltipRich == null)
        {
            _tooltipRich = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                AutowrapMode = TextServer.AutowrapMode.Off,
                ScrollActive = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _tooltipRich.AddThemeFontSizeOverride("normal_font_size", 14);
            _tooltipRich.AddThemeColorOverride("default_color", Ink);
            _tooltip.AddChild(_tooltipRich);
        }
        if (_tooltipText.Visible) _tooltipText.Visible = false;
        if (!_tooltipRich.Visible) _tooltipRich.Visible = true;
        var text = bbcode.Replace("\r", "");
        if (_tooltipRich.Text != text) _tooltipRich.Text = text;
        PlaceTooltip(mouse);
    }
}
