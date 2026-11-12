using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// Parts economy on the HUD (docs/parts-contract.md "UI"): a selected fabricator's tabbed panel (FabPanel.cs: it takes
// the bottom panel's centre and command card, and the bottom panel grows to fit it), the WORKSHOP / INVENTORY chips at
// the right of the base tabs, command tiles made on demand, and a tooltip that can colour words.
public partial class PlanetHud
{
    /// <summary>× on a queued or running order (FabOrder.Id).</summary>
    public event Action<int>? FabCancelPressed;

    /// <summary>The WORKSHOP [K] chip (a colony core's workshop view, and back).</summary>
    public event Action? WorkshopPressed;

    /// <summary>The INVENTORY [I] chip.</summary>
    public event Action? InventoryPressed;

    private Control _tabRow = null!;
    private HBoxContainer _tools = null!;
    private Button _workshopChip = null!;
    private Button _inventoryChip = null!;
    private RichTextLabel? _tooltipRich;

    // ---------------------------------------------------------------- fabricator panel

    /// <summary>A recipe tile on the fabricator panel: (item id, mode) — 0 = its card, 1 / 5 = queue that many, -1 = remove one.</summary>
    public event Action<string, int>? FabRecipePressed;

    private FabPanel _fabPanel = null!;
    private Control _centreBox = null!;
    private Control _commandBox = null!;

    /// <summary>The bottom panel's height right now (taller while the fabricator panel shows).</summary>
    public float BottomNow => _bottom == null ? BottomHeight : -_bottom.OffsetTop;

    private FabPanel BuildFabPanel()
    {
        _fabPanel = new FabPanel { Name = "FabPanel", Visible = false };
        _fabPanel.RecipePressed += (id, mode) => FabRecipePressed?.Invoke(id, mode);
        _fabPanel.CancelPressed += id => FabCancelPressed?.Invoke(id);
        _fabPanel.ActionPressed += id => _onAction?.Invoke(id);
        _fabPanel.InventoryPressed += () => InventoryPressed?.Invoke();
        _fabPanel.WorkshopPressed += () => WorkshopPressed?.Invoke();
        return _fabPanel;
    }

    /// <summary>A selected Machine Shop / core workshop: the tabbed panel in place of the centre and the command card.</summary>
    public void ShowFabPanel(FabPanelView v)
    {
        if (_centreBox.Visible) _centreBox.Visible = false;
        if (_commandBox.Visible) _commandBox.Visible = false;
        if (!_fabPanel.Visible) _fabPanel.Visible = true;
        var screen = GetViewport().GetVisibleRect().Size;
        _fabPanel.Show(v, screen);
        SetBottomHeight(Mathf.Min(_fabPanel.NeededHeight, screen.Y * 0.6f));
    }

    /// <summary>Back to the usual centre + command card (every other bottom-panel view calls this).</summary>
    private void HideFabPanel()
    {
        if (!_fabPanel.Visible) return;
        _fabPanel.Visible = false;
        _centreBox.Visible = true;
        _commandBox.Visible = true;
        SetBottomHeight(BottomHeight);
    }

    private void SetBottomHeight(float height)
    {
        if (!Mathf.IsEqualApprox(-_bottom.OffsetTop, height)) _bottom.OffsetTop = -height;
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
