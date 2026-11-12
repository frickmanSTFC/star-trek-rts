using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Parts economy, view side (docs/parts-contract.md "UI"; mockup docs/mockups/parts B, C, D). Render-only: every change
// goes through commands (FabricateCommand, CancelFabricateCommand, StockTargetCommand, JuryRigCommand,
// JuryRigUnitsCommand) and everything shown comes from the sim's Fabrication API, so the panels just show less while the
// stubs return empty lists / null.
//  - A selected Machine Shop (IsFabricator, not IsHubWorkshop): the tabbed panel (FabPanel.cs, owner's option 2 of
//    docs/mockups/machine-shop-panel): name, status and buttons on the left; Overview / Queue · n / Recipes · n on the
//    right (the command card is hidden). Recipe tiles: click = the item's full card (ItemCard.cs), Shift+click +5,
//    Ctrl+click +1, right-click removes one; keys 1–9 and 0 queue (+1, Shift +5), Backspace cancels the last order;
//    anything you can't make right now is greyed out but still opens its card.
//  - A colony core's workshop: the selected base's WORKSHOP [K] chip (right of the base tabs) or K swaps the base panel
//    for the same tabbed panel (its four kits on the Recipes tab); K / the chip / Esc go back to the base.
//  - Inventory screen (InventoryScreen.cs): I (not while placing), the INVENTORY chip; − / + send StockTargetCommand.
//  - Repairs: kits rows, "waiting for kits" + ETA, JURY-RIG [J] on the building and rover panels, jury-rigged labels.
//  - Event lines: a repair starts waiting for kits, a fabricator finishes an order, something is jury-rigged.
public partial class PlanetDriver
{
    private InventoryScreen _inventory = null!;

    /// <summary>The selected base shows its core's workshop (K) instead of the base panel.</summary>
    private bool _workshop;

    private readonly Dictionary<string, (int Target, long Tick)> _pendingTargets = new();
    private readonly HashSet<int> _cancelledOrders = new();
    private readonly Dictionary<int, (int Id, string Recipe, float Progress, bool Auto)> _seenFab = new();
    private readonly Dictionary<int, (bool Waiting, bool Jury)> _seenStructureRepairs = new();
    private readonly Dictionary<int, (bool Waiting, bool Jury)> _seenUnitRepairs = new();

    private static readonly Color PartsWarn = new(1f, 0.7f, 0.42f);
    private static readonly Color PartsBlue = new(0.62f, 0.82f, 1f);

    /// <summary>Two-letter codes on the chips (the parts mockup's); other items get their initials.</summary>
    private static readonly Dictionary<string, string> ItemCodes = new()
    {
        ["structural_frame"] = "SF", ["hull_plating"] = "HP", ["mechanism"] = "ME", ["power_coupling"] = "PC",
        ["control_module"] = "CM", ["seal_kit"] = "SK", ["mechanical_spares"] = "MS", ["electrical_spares"] = "ES",
        ["rover_spares"] = "RS", ["environmental_spares"] = "EN", ["prefab_module"] = "PM", ["machinery_set"] = "MY",
    };

    /// <summary>What a kit is for when none of our buildings or vehicles uses it yet (the design doc's list).</summary>
    private static readonly Dictionary<string, string> KitUses = new()
    {
        ["mechanical_spares"] = "repairs of mines, crawlers, the refinery and most buildings",
        ["electrical_spares"] = "repairs of reactors and labs",
        ["rover_spares"] = "rover repairs on the pad (tyres, axles)",
        ["environmental_spares"] = "repairs of domes and life support",
        ["prefab_module"] = "new buildings (phase 4)",
        ["machinery_set"] = "new buildings and Mk II / III upgrades (phase 4)",
    };

    private void SetupParts()
    {
        var layer = new CanvasLayer { Name = "InventoryLayer", Layer = 4 };
        AddChild(layer);
        _inventory = new InventoryScreen { Name = "InventoryScreen" };
        layer.AddChild(_inventory);
        _inventory.Source = InventoryNow;
        _inventory.TargetChanged += SetStockTarget;
        _hud.FabCancelPressed += CancelOrder;
        _hud.WorkshopPressed += ToggleWorkshop;
        _hud.InventoryPressed += ToggleInventory;
        _hud.KitsShortPressed += () => { if (!_inventory.IsOpen) ToggleInventory(); };
        SetupItemCard();
    }

    // ---------------------------------------------------------------- items

    private Dictionary<string, ResourceDef> _itemDefs = new();
    private GameData? _itemData;
    private System.Reflection.PropertyInfo? _volumeProperty;

    private ResourceDef? Item(string id)
    {
        if (_itemData != _data)
        {
            _itemData = _data;
            _itemDefs = _data.Resources.ToDictionary(r => r.Id);
            // the sim agent may add a storage volume to ResourceDef; read it if it is there
            _volumeProperty = typeof(ResourceDef).GetProperty("Volume") ?? typeof(ResourceDef).GetProperty("StorageVolume");
        }
        return _itemDefs.GetValueOrDefault(id);
    }

    private string ItemName(string id) => Item(id)?.Name ?? id.Replace('_', ' ');
    private string ItemTier(string id) => Item(id)?.Tier ?? "part";
    private Color ItemColour(string id) => Icons.TierColour(ItemTier(id)) ?? Icons.PartColour;

    private string ItemCode(string id)
    {
        if (ItemCodes.TryGetValue(id, out var code)) return code;
        var words = ItemName(id).Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
            : words.Length == 1 ? words[0][..System.Math.Min(2, words[0].Length)].ToUpperInvariant() : "??";
    }

    private bool IsPartItem(int resource) => Icons.TierColour(_data.Resources[resource].Tier) != null;

    private int Have(string id) => Item(id) is { } r ? Mathf.FloorToInt(_sim.Players[LocalPlayer].Stock[r.Index].ToFloat()) : 0;

    /// <summary>Storage space one takes: the data's volume if the sim exposes one, else the contract's 1 per part, 2 per kit / construction part.</summary>
    private float VolumeOf(string id)
    {
        var def = Item(id);
        if (def != null && _volumeProperty?.GetValue(def) is { } v)
        {
            switch (v)
            {
                case Fix64 f when f > Fix64.Zero: return f.ToFloat();
                case int i when i > 0: return i;
                case float x when x > 0: return x;
                case double d when d > 0: return (float)d;
            }
        }
        return ItemTier(id) == "part" ? 1f : 2f;
    }

    private static string Clock(float seconds)
    {
        var s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"2 ME · 1 SF" / "4 DUR · 1 TRI": parts by their code, materials by their first three letters.</summary>
    private string InputsShort(IReadOnlyList<ResourceAmount> inputs) =>
        string.Join(" · ", inputs.Select(a => $"{Hud.Whole(a.Amount)} {(IsPartItem(a.Resource) ? ItemCode(_data.Resources[a.Resource].Id) : HudKit.Short(_data.Resources[a.Resource].Name))}"));

    /// <summary>What an item is used for: the recipes here that take it, or the buildings / vehicles of ours that repair with it.</summary>
    private string UsedFor(string id, SurfaceStructure? at)
    {
        var item = Item(id);
        var tier = ItemTier(id);
        if (tier == "part" && item != null)
        {
            var fab = at ?? Surface.Structures.FirstOrDefault(s => s.Owner == LocalPlayer && Surface.IsFabricator(s) && !Surface.IsHubWorkshop(s));
            if (fab != null)
            {
                var users = Surface.RecipesAt(fab).Where(r => Surface.InputsAt(fab, r).Any(a => a.Resource == item.Index)).Select(ItemName).ToList();
                if (users.Count > 0) return Names(users, 4);
            }
            return "spare kits and construction parts";
        }
        if (tier == "spare")
        {
            var users = Surface.Structures.Where(s => s.Owner == LocalPlayer && s.State == StructureState.Complete && Surface.KitOf(s) == id)
                .Select(s => _data.Building(s.DefId).Name)
                .Concat(Surface.Units.Where(u => u.Owner == LocalPlayer && Surface.KitOf(u) == id).Select(TypeName)).ToList();
            if (users.Count > 0) return "repairs of your " + Names(users, 4);
        }
        return KitUses.GetValueOrDefault(id, "");
    }

    /// <summary>The large building card's "What it makes" for a Machine Shop: the sim's def may carry a fabricator
    /// block (read by reflection until it does), else the contract's building id.</summary>
    private List<CardBlock> FabricatorCardBlocks(BuildingDef def)
    {
        var blocks = new List<CardBlock>();
        var fabricator = typeof(BuildingDef).GetProperty("Fabricator")?.GetValue(def) != null || def.Id == "machine_shop";
        if (!fabricator) return blocks;
        var items = Surface.PartItems;
        if (items.Count > 0)
        {
            var pairs = new List<(string, string)>();
            foreach (var (tier, label) in new[] { ("part", "Parts"), ("spare", "Spare kits"), ("construction", "Construction · phase 4") })
            {
                var names = items.Where(i => ItemTier(i) == tier).Select(ItemName).ToList();
                if (names.Count > 0) pairs.Add((label, string.Join(", ", names)));
            }
            blocks.Add(new CardPairs(pairs));
        }
        blocks.Add(new CardText("Turns refined metals into parts, and parts into spare kits and construction parts, one order at a time. Queue them at the shop, " +
                                "or set keep-in-stock targets in the inventory (I): below a target it queues one more by itself. Repairs use the kits."));
        blocks.Add(new CardText("Needs crew and power while it works, and wears like any building: its crew service it with its own Mechanical and Electrical Spares.", Dim: true));
        return blocks;
    }

    // ---------------------------------------------------------------- fabricator selection

    /// <summary>The fabricator whose panel and recipes show: a selected Machine Shop of ours, or the selected base's core in workshop view.</summary>
    private SurfaceStructure? SelectedFabricator()
    {
        if (_selectedStructure != 0)
        {
            var s = Surface.FindStructure(_selectedStructure);
            return s != null && s.Owner == LocalPlayer && Surface.IsFabricator(s) ? s : null;
        }
        return WorkshopShown() ? Surface.FindStructure(_selectedBase) : null;
    }

    /// <summary>The selected base's core has a workshop (finished, ours).</summary>
    private bool WorkshopAvailable()
    {
        if (_selectedBase == 0 || Me.Phase != LandingPhase.Landed) return false;
        var core = Surface.FindStructure(_selectedBase);
        return core != null && core.Owner == LocalPlayer && Surface.IsFabricator(core) && Surface.IsHubWorkshop(core);
    }

    /// <summary>The base panel is swapped for the core's workshop (K) and nothing else is selected.</summary>
    private bool WorkshopShown() =>
        _workshop && _selectedStructure == 0 && _view.SelectedUnits.Count == 0 && _selectedTile < 0 && WorkshopAvailable();

    /// <summary>K or the WORKSHOP chip: the selected base's core workshop, and back to the base.</summary>
    private void ToggleWorkshop()
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (!WorkshopAvailable())
        {
            _workshop = false;
            _hud.PushEvent(Surface.PartItems.Count == 0 ? "Workshops: the parts economy is not in this build yet" : "This base's colony core has no workshop (yet): it needs to be finished", PartsWarn);
            return;
        }
        var show = !WorkshopShown();
        _info = null;
        _view.SelectedUnits.Clear();
        _selectedTile = -1;
        _selectedStructure = 0;
        _workshop = show;
    }

    // ---------------------------------------------------------------- orders

    /// <summary>A recipe tile or its key: +1, or +5 with Shift.</summary>
    private void Fabricate(string recipe, int count)
    {
        var fab = SelectedFabricator();
        if (fab == null) return;
        var problem = Surface.FabricateProblem(LocalPlayer, fab.Id, recipe);
        if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new FabricateCommand(LocalPlayer, fab.Id, recipe, count));
        else _hud.PushEvent($"Can't queue {ItemName(recipe)}: {problem}", PartsWarn);
    }

    /// <summary>Number key n (0-based; key 0 = the tenth) on the selected fabricator's recipes.</summary>
    private void FabricateByKey(SurfaceStructure fab, int index, bool shift)
    {
        var recipes = Surface.RecipesAt(fab);
        if (index < recipes.Count) Fabricate(recipes[index], shift ? 5 : 1);
    }

    private void CancelOrder(int orderId)
    {
        var fab = SelectedFabricator();
        if (fab == null) return;
        _cancelledOrders.Add(orderId); // so its disappearance isn't reported as "done"
        _sim.Schedule(_sim.Tick + InputDelayTicks, new CancelFabricateCommand(LocalPlayer, fab.Id, orderId));
    }

    /// <summary>Backspace: the last order the player queued (keep-in-stock ones only when nothing else waits).</summary>
    private void CancelLastOrder(SurfaceStructure fab)
    {
        var q = fab.Fabrication;
        var last = q.LastOrDefault(o => !o.Started && !o.Auto) ?? q.LastOrDefault(o => !o.Started) ?? q.LastOrDefault();
        if (last == null) _hud.PushEvent("Nothing queued to cancel", PartsWarn);
        else CancelOrder(last.Id);
    }

    // ---------------------------------------------------------------- the tabbed panel's recipes + the item card

    /// <summary>A selected fabricator's recipes live on the panel's Recipes tab (FabPanel), so the command card is hidden
    /// while it shows; true = a fabricator is selected (nothing else goes on the command card).</summary>
    private bool ShowFabCommands() => SelectedFabricator() != null;

    /// <summary>The number key of a recipe here: "1"–"9", "0" for the 10th, "" after that.</summary>
    private static string RecipeKey(int index) => index < 9 ? $"{index + 1}" : index == 9 ? "0" : "";

    /// <summary>A recipe's inputs with what we have (Started orders took theirs already: never short).</summary>
    private List<FabCost> CostOf(SurfaceStructure fab, string recipe, bool taken = false)
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        return Surface.InputsAt(fab, recipe).Select(a =>
        {
            var r = _data.Resources[a.Resource];
            var need = Mathf.CeilToInt(a.Amount.ToFloat());
            var have = Mathf.FloorToInt(stock[a.Resource].ToFloat());
            return new FabCost(r.Id, r.Name, need, have, !taken && stock[a.Resource] < a.Amount);
        }).ToList();
    }

    /// <summary>"needs 2 Mechanism · have 0; needs 1 Hull Plating · have 0" (every short input), "" when none is short.</summary>
    private static string ShortReason(IEnumerable<FabCost> cost) =>
        string.Join("; ", cost.Where(c => c.Short).Select(c => $"needs {c.Amount} {c.Name} · have {c.Have}"));

    /// <summary>A recipe tile on the panel: 0 = open its card, 1 / 5 = queue that many, -1 = remove one queued order of it.</summary>
    private void OnFabRecipe(string recipe, int mode)
    {
        var fab = SelectedFabricator();
        if (fab == null) return;
        if (mode == 0)
        {
            _itemCardId = recipe;
            return;
        }
        if (mode > 0)
        {
            Fabricate(recipe, mode);
            return;
        }
        var q = fab.Fabrication;
        var last = q.LastOrDefault(o => o.RecipeId == recipe && !o.Started && !o.Auto) ?? q.LastOrDefault(o => o.RecipeId == recipe && !o.Started)
                   ?? q.LastOrDefault(o => o.RecipeId == recipe);
        if (last == null) _hud.PushEvent($"No {ItemName(recipe)} queued here", PartsWarn);
        else CancelOrder(last.Id);
    }

    private ItemCard _itemCard = null!;

    /// <summary>The item whose full card is open (a recipe tile was clicked), null = none.</summary>
    private string? _itemCardId;

    private void SetupItemCard()
    {
        _itemCard = new ItemCard { Name = "ItemCard" };
        _hud.AddOverlay(_itemCard); // over the map and the bottom panel, under F1 help and the tooltip
        _itemCard.QueuePressed += n =>
        {
            if (_itemCardId != null) Fabricate(_itemCardId, n);
        };
        _itemCard.LinkPressed += id => _itemCardId = id; // refilled next frame (UpdateItemCard), not inside the link's own signal
        _itemCard.CloseRequested += () => _itemCardId = null;
        _hud.FabRecipePressed += OnFabRecipe;
    }

    /// <summary>Once a frame: the open item card follows the stock; it closes when its fabricator is no longer shown.</summary>
    private void UpdateItemCard()
    {
        var fab = SelectedFabricator();
        if (_itemCardId != null && (fab == null || _inOrbit || !Surface.RecipesAt(fab).Contains(_itemCardId))) _itemCardId = null;
        if (_itemCardId == null)
        {
            _itemCard.Close();
            return;
        }
        _itemCard.Show(ItemCardViewOf(fab!, _itemCardId));
    }

    /// <summary>While the item card is open: Esc closes it, Enter queues one (Shift+Enter five); no other key reaches the game.</summary>
    private bool ItemCardInput(InputEvent e)
    {
        if (!_itemCard.IsOpen || e is not InputEventKey key) return false;
        if (key is not { Pressed: true, Echo: false }) return true;
        switch (key.Keycode)
        {
            case Key.Escape:
                Sfx.Ui("back");
                _itemCardId = null;
                _itemCard.Close();
                break;
            case Key.Enter or Key.KpEnter when _itemCardId != null:
                Fabricate(_itemCardId, key.ShiftPressed ? 5 : 1);
                break;
        }
        return true;
    }

    private static string KindName(string tier) => tier switch
    {
        "part" => "Part",
        "spare" => "Spare kit",
        "construction" => "Construction part",
        "drone" => "Drone",
        _ => tier,
    };

    private ItemCardView ItemCardViewOf(SurfaceStructure fab, string id)
    {
        var recipes = Surface.RecipesAt(fab);
        var index = recipes.ToList().IndexOf(id);
        var hub = Surface.IsHubWorkshop(fab);
        var cost = CostOf(fab, id);
        var problem = Surface.FabricateProblem(LocalPlayer, fab.Id, id) ?? "";
        var seconds = Surface.SecondsAt(fab, id).ToFloat();
        string times;
        if (hub) times = seconds > 0 ? $"Makes 1 in {Clock(seconds)} at this workshop (from materials, half speed)" : "";
        else
        {
            var def = _data.Building(fab.DefId);
            var mk2 = def.Levels.Count > 0 ? def.Levels[0].Modifiers.Where(m => m.Stat == Modifier.Speed).Sum(m => m.Add.ToFloat()) : 0f;
            times = $"Makes 1 in {Clock(seconds)} at Mk I" + (def.Levels.Count > 0 && mk2 > 0 ? $"  ·  {Clock(seconds / (1f + mk2))} at {def.Levels[0].Name}" : "")
                    + $"  ·  this shop: {Surface.LevelName(fab)}";
        }
        var needs = cost.Select(c => new ItemNeed(c.ResourceId, c.Name, c.Amount, c.Have, c.Short, recipes.Contains(c.ResourceId))).ToList();
        var here = fab.Fabrication.Count(o => o.RecipeId == id);
        var target = Surface.StockTarget(LocalPlayer, id);
        var stockLine = $"In stock {Have(id)}  ·  {here} in the queue here  ·  takes {Num(VolumeOf(id))} storage each" + (target > 0 ? $"  ·  keep {target} in stock" : "");
        var use = UsedFor(id, fab);
        var description = Item(id)?.Description is { Length: > 0 } d ? d : use.Length > 0 ? $"For {use}." : "";
        return new ItemCardView(id, ItemName(id), KindName(ItemTier(id)), index >= 0 ? RecipeKey(index) : "", ItemColour(id), times,
            cost.All(c => !c.Short) && problem.Length == 0, needs, description, stockLine, problem,
            use.Length > 0 && ItemTier(id) == "spare" ? $"Used for {use}." : "");
    }

    // ---------------------------------------------------------------- building panel

    /// <summary>
    /// For a finished fabricator (a Machine Shop, or a colony core in workshop view): the tabbed panel (FabPanel) with
    /// its name, status and buttons on the left and Overview / Queue / Recipes on the right. Null for anything else.
    /// </summary>
    private FabPanelView? FabPanelOf(SurfaceStructure s, string title, string subtitle, string status, Color statusColour, List<(string Label, string Value)> rows,
        string conditionText, string maintenance, float condition, float progress, string progressLabel, string progressText, List<ActionView> actions)
    {
        if (s.State != StructureState.Complete || !Surface.IsFabricator(s)) return null;
        var def = _data.Building(s.DefId);
        var hub = Surface.IsHubWorkshop(s);
        var queue = s.Fabrication;
        var current = queue.FirstOrDefault(o => o.Started);
        var power = Surface.PowerOf(s.BaseId);
        var powerShare = def.PowerUse > Fix64.Zero || hub ? power.Efficiency : Fix64.One;
        var rate = Surface.SpeedMultiplier(s) * Surface.ConditionFactor(s) * powerShare;
        var red = new Color(1f, 0.48f, 0.42f);
        switch (Surface.FabricatorStatus(s))
        {
            case FabStatus.Idle:
                (status, statusColour) = (hub ? "Workshop idle: pick a kit on the Recipes tab" : "Idle: queue empty (pick a recipe on the Recipes tab)", HudKit.Dim);
                break;
            case FabStatus.Working:
                (status, statusColour) = ($"Working: {(current != null ? ItemName(current.RecipeId) : "an order")}", RoverGood);
                break;
            case FabStatus.LowPower:
                (status, statusColour) = ($"Low power: running at {Mathf.RoundToInt(power.Efficiency.ToFloat() * 100)}%", PlanetHud.Accent);
                break;
            case FabStatus.NoInput:
                (status, statusColour) = ("Waiting: every order is short of inputs", PartsWarn);
                break;
            case FabStatus.StorageFull:
                (status, statusColour) = ("Waiting: no room in storage for the finished item", PartsWarn);
                break;
            // Broken, Paused, Building, None: the building's own status line stays
        }
        if (hub)
        {
            title = $"{BaseName(s.BaseId)} workshop";
            subtitle = $"{def.Name}  ·  spare kits  ·  K back to the base";
        }
        else subtitle = $"{subtitle}  ·  {BaseName(s.BaseId)}";

        // Queue rows (and the short ones for the Overview).
        var queueRows = new List<FabQueueView>();
        var shorts = new List<(string, string)>();
        foreach (var o in queue)
        {
            var cost = CostOf(s, o.RecipeId, o.Started);
            var seconds = Surface.SecondsAt(s, o.RecipeId).ToFloat();
            var reason = o.Started ? (Surface.OrderProblem(s, o) ?? "") : ShortReason(cost);
            if (!o.Started && reason.Length == 0 && Surface.OrderProblem(s, o) is { } other) reason = other;
            string time;
            var orderProgress = -1f;
            if (o.Started)
            {
                orderProgress = o.Progress.ToFloat();
                time = rate > Fix64.Zero && seconds > 0 ? $"{Mathf.FloorToInt(orderProgress * 100)}%  ·  {Clock(seconds * (1f - orderProgress) / rate.ToFloat())} left" : $"{Mathf.FloorToInt(orderProgress * 100)}%  ·  stopped";
            }
            else time = seconds > 0 ? Clock(seconds) : "";
            queueRows.Add(new FabQueueView(o.Id, o.RecipeId, ItemName(o.RecipeId), ItemColour(o.RecipeId), cost, reason, time, orderProgress,
                o.Started ? "MAKING" : o.Auto ? "KEEP IN STOCK" : ""));
            if (reason.Length > 0) shorts.Add((ItemName(o.RecipeId), reason));
        }

        // Overview: Making, Crew · power, Condition, Next level, then the building's other rows.
        var facts = new List<FabFactView>();
        var canStart = queue.Count(o => !o.Started && ShortReason(CostOf(s, o.RecipeId)).Length == 0);
        facts.Add(new FabFactView("Making", current != null
            ? $"{ItemName(current.RecipeId)}  ·  {Mathf.FloorToInt(current.Progress.ToFloat() * 100)}%" + (queue.Count > 1 ? $"  ·  {queue.Count - 1} more queued" : "")
            : queue.Count > 0 ? $"{queue.Count} queued  ·  " + (canStart > 0 ? $"{canStart} can start" : "none can start")
            : hub ? "nothing: spare kits from materials, one at a time" : "nothing queued", current == null && queue.Count > 0 && canStart == 0 ? red : null));
        var crew = new List<string>();
        if (hub) crew.Add("core workshop, half speed");
        else if (def.Crewed) crew.Add("Crewed");
        if (def.PowerUse > Fix64.Zero && !hub) crew.Add($"{Hud.Whole(def.PowerUse)} power");
        var pct = Mathf.RoundToInt(rate.ToFloat() * 100);
        crew.Add((pct == 100 ? "full speed" : $"runs at {pct}%") + (powerShare < Fix64.One ? $" (power {Mathf.RoundToInt(powerShare.ToFloat() * 100)}%)" : ""));
        facts.Add(new FabFactView("Crew · power", string.Join("  ·  ", crew)));
        if (def.Wears && conditionText.Length > 0)
            facts.Add(new FabFactView("Condition", conditionText + (maintenance.Length > 0 ? $"  ·  {maintenance}" : ""), condition >= 70 ? HudKit.Good : condition >= 50 ? PlanetHud.Accent : red));
        var next = rows.FirstOrDefault(r => r.Label == "Next").Value;
        facts.Add(new FabFactView("Next level", progress >= 0 && progressLabel.Length > 0 ? $"{progressLabel}: {progressText}"
            : next ?? (def.Levels.Count > 0 ? "top level" : "no upgrades")));
        foreach (var (label, value) in rows)
            if (label != "Next" && label.Length > 0 && value.Length > 0) facts.Add(new FabFactView(label, value));

        // Recipes.
        var recipes = Surface.RecipesAt(s);
        var tiles = new List<FabTileView>();
        for (var i = 0; i < recipes.Count; i++)
        {
            var r = recipes[i];
            var cost = CostOf(s, r);
            var problem = Surface.FabricateProblem(LocalPlayer, s.Id, r);
            var seconds = Surface.SecondsAt(s, r).ToFloat();
            var tip = new StringBuilder(ItemName(r)).Append(" · ").Append(KindName(ItemTier(r)));
            tip.Append("\nNeeds ").Append(string.Join(" + ", cost.Select(c => $"{c.Amount} {c.Name} (have {c.Have})")));
            if (seconds > 0) tip.Append($"\n{Clock(seconds)} at full speed");
            if (problem != null) tip.Append($"\nCan't queue: {problem}");
            tip.Append("\nClick: its card  ·  Shift+click: queue 5  ·  Ctrl+click: queue 1  ·  right-click: remove one");
            if (RecipeKey(i).Length > 0) tip.Append($"  ·  key {RecipeKey(i)} queues 1 (Shift 5)");
            tiles.Add(new FabTileView(r, ItemName(r), RecipeKey(i), ItemColour(r), cost, queue.Count(o => o.RecipeId == r), cost.All(c => !c.Short) && problem == null, tip.ToString()));
        }

        var left = Surface.QueueSecondsLeft(s).ToFloat();
        var queueNote = queue.Count == 0
            ? (hub ? "No kits queued. Click a kit on the Recipes tab for its card, or press its number key to queue one (Shift: 5); keep-in-stock targets (I) queue kits here while the base has no Machine Shop."
                : "Queue empty. Click a recipe on the Recipes tab for its card, or press its number key to queue one (Shift: 5); keep-in-stock targets (I) queue parts by themselves.")
            : (left > 0 ? $"{Clock(left)} left in all  ·  " : "") + "× removes an order (what it took comes back)  ·  Backspace removes the last one";
        var recipeNote = recipes.Count == 0 ? "No recipes: the parts economy is not in this build yet."
            : hub ? "The colony core's workshop makes spare kits straight from materials, one at a time, at half a Machine Shop's speed." : "";
        return new FabPanelView(title, subtitle, s.DefId, status, statusColour, actions, facts, shorts, queueRows, queueNote, tiles, recipeNote, hub);
    }

    // ---------------------------------------------------------------- repairs

    /// <summary>" · kits ready in ~1:10" (or that none are queued), "" when the thing has no kit.</summary>
    private string KitEta(string? kit)
    {
        if (kit == null) return "";
        return Surface.KitEtaSeconds(LocalPlayer, kit) is { } eta ? $" · kits ready in ~{Clock(eta.ToFloat())}" : " · none queued yet";
    }

    /// <summary>A building's kits row ("2 Mechanical Spares for a full repair (have 1)", jury-rigged first) and its
    /// waiting-for-kits line; nulls when there is nothing to say.</summary>
    private (string? Row, string? Wait) RepairInfo(SurfaceStructure s)
    {
        var kit = Surface.KitOf(s);
        var n = Surface.KitsNeeded(s);
        string? row = kit != null && n > 0 ? $"{n} {ItemName(kit)} for a full repair (have {Have(kit)})" : null;
        if (s.JuryRigged) row = "Jury-rigged: capped, wears faster" + (row != null ? $"  ·  {row}" : "  ·  a kit repair fixes it");
        var wait = Surface.RepairWaitReason(s);
        return (row, wait == null ? null : $"Waiting for kits: {wait}{KitEta(kit)}  ·  J jury-rig");
    }

    private (string? Row, string? Wait) RepairInfo(SurfaceUnit u)
    {
        var kit = Surface.KitOf(u);
        var n = Surface.KitsNeeded(u);
        string? row = kit != null && n > 0 ? $"on a colony core's pad: {n} {ItemName(kit)} (have {Have(kit)})" : null;
        if (u.JuryRigged) row = "Jury-rigged: capped, wears faster" + (row != null ? $"  ·  {row}" : "");
        var wait = Surface.RepairWaitReason(u);
        return (row, wait == null ? null : $"Waiting for kits: {wait}{KitEta(kit)}  ·  J jury-rig");
    }

    /// <summary>The MAINTAIN button's detail: the kits it takes (or the old metal cost while the sim still charges that).</summary>
    private string MaintainDetail(SurfaceStructure s)
    {
        var kit = Surface.KitOf(s);
        var n = Surface.KitsNeeded(s);
        if (kit != null && n > 0) return $"{n} {ItemCode(kit)} kit{(n == 1 ? "" : "s")} (have {Have(kit)})";
        var cost = Surface.MaintenanceCost(s);
        return cost.Count == 0 ? "crew time" : $"~{Amounts(cost, false, shortNames: true)}";
    }

    /// <summary>JURY-RIG [J] for a building: offered while it can be jury-rigged, or (greyed with the reason) while its repair waits for kits.</summary>
    private ActionView? JuryAction(SurfaceStructure s)
    {
        if (s.State != StructureState.Complete) return null;
        var problem = Surface.JuryRigProblem(LocalPlayer, s.Id);
        if (problem != null && Surface.RepairWaitReason(s) == null) return null;
        return new ActionView("juryrig", "JURY-RIG [J]", problem ?? "now, no kits: capped, wears faster", problem == null);
    }

    private ActionView? JuryAction(SurfaceUnit u)
    {
        var problem = Surface.JuryRigProblem(u);
        if (problem != null && Surface.RepairWaitReason(u) == null) return null;
        return new ActionView("juryrig", "JURY-RIG [J]", problem ?? "now, no kits: capped, wears faster", problem == null);
    }

    /// <summary>Adds JURY-RIG to a building's buttons; with all four slots taken it replaces SALVAGE (X still salvages).</summary>
    private void AddJuryAction(SurfaceStructure s, List<ActionView> actions)
    {
        if (JuryAction(s) is not { } jury) return;
        if (actions.Count >= 4) actions.RemoveAll(a => a.Id == "salvage");
        if (actions.Count < 4) actions.Add(jury);
    }

    /// <summary>Is J worth a hint for the selection?</summary>
    private bool JuryRelevant()
    {
        if (PanelStructure() is { } s) return JuryAction(s) != null;
        return SelectedUnitsOfOurs().Any(u => JuryAction(u) != null);
    }

    /// <summary>The building the bottom panel shows (a selected building, or the core in workshop view).</summary>
    private SurfaceStructure? PanelStructure() =>
        _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : WorkshopShown() ? Surface.FindStructure(_selectedBase) : null;

    private IEnumerable<SurfaceUnit> SelectedUnitsOfOurs() =>
        _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null && u.Owner == LocalPlayer).Select(u => u!);

    /// <summary>J / the JURY-RIG button: the building on the panel, else the selected vehicles that can be.</summary>
    private void JuryRig()
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (PanelStructure() is { } s)
        {
            var problem = Surface.JuryRigProblem(LocalPlayer, s.Id);
            if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new JuryRigCommand(LocalPlayer, s.Id));
            else _hud.PushEvent($"Can't jury-rig the {_data.Building(s.DefId).Name}: {problem}", PartsWarn);
            return;
        }
        var units = SelectedUnitsOfOurs().ToList();
        if (units.Count == 0) return;
        var ok = units.Where(u => Surface.JuryRigProblem(u) == null).Select(u => u.Id).ToList();
        if (ok.Count > 0) _sim.Schedule(_sim.Tick + InputDelayTicks, new JuryRigUnitsCommand(LocalPlayer, ok));
        else if (units.Count == 1) _hud.PushEvent($"Can't jury-rig {UnitName(units[0])}: {Surface.JuryRigProblem(units[0])}", PartsWarn);
        else _hud.PushEvent("None of the selected vehicles can be jury-rigged", PartsWarn);
    }

    // ---------------------------------------------------------------- events

    /// <summary>Event lines: a repair starts waiting for kits, something is jury-rigged, a fabricator finishes an order.</summary>
    private void PartsEvents()
    {
        foreach (var s in Surface.Structures)
        {
            if (s.Owner != LocalPlayer) continue;
            var wait = Surface.RepairWaitReason(s);
            var now = (Waiting: wait != null, Jury: s.JuryRigged);
            if (_seenStructureRepairs.TryGetValue(s.Id, out var was) && was != now)
            {
                var name = Surface.IsHubWorkshop(s) ? $"{BaseName(s.BaseId)}'s {_data.Building(s.DefId).Name}" : _data.Building(s.DefId).Name;
                if (now.Waiting && !was.Waiting) ColonyReport($"{name} repair waiting for kits: {wait}{KitEta(Surface.KitOf(s))} — J jury-rigs it", PartsWarn);
                if (now.Jury && !was.Jury) ColonyReport($"{name} jury-rigged: working again, capped and wearing faster until a kit repair", PlanetHud.Accent);
            }
            _seenStructureRepairs[s.Id] = now;
            if (s.State == StructureState.Complete && Surface.IsFabricator(s)) FabEvents(s);
        }
        foreach (var u in Surface.Units)
        {
            if (u.Owner != LocalPlayer) continue;
            var wait = Surface.RepairWaitReason(u);
            var now = (Waiting: wait != null, Jury: u.JuryRigged);
            if (_seenUnitRepairs.TryGetValue(u.Id, out var was) && was != now)
            {
                if (now.Waiting && !was.Waiting) ColonyReport($"{UnitName(u)} waiting for kits on the pad: {wait}{KitEta(Surface.KitOf(u))} — J jury-rigs it", PartsWarn);
                if (now.Jury && !was.Jury) ColonyReport($"{UnitName(u)} jury-rigged: back on the road, capped and wearing faster until a kit repair", PlanetHud.Accent);
            }
            _seenUnitRepairs[u.Id] = now;
        }
    }

    /// <summary>The running order of each fabricator, diffed each frame: gone (not cancelled by us) after it was past
    /// 60% = done. Keep-in-stock parts made for a kit are not reported (they would flood the feed).</summary>
    private void FabEvents(SurfaceStructure s)
    {
        FabOrder? current = null;
        foreach (var o in s.Fabrication)
            if (o.Started)
            {
                current = o;
                break;
            }
        if (_seenFab.TryGetValue(s.Id, out var was) && was.Id != 0 && current?.Id != was.Id)
        {
            var gone = true;
            foreach (var o in s.Fabrication)
                if (o.Id == was.Id) gone = false;
            if (gone && !_cancelledOrders.Remove(was.Id) && was.Progress >= 0.6f && (!was.Auto || ItemTier(was.Recipe) != "part"))
            {
                var who = Surface.IsHubWorkshop(s) ? $"{BaseName(s.BaseId)} workshop" : _data.Building(s.DefId).Name;
                ColonyReport($"{who}: {ItemName(was.Recipe)} done{(was.Auto ? " (keep in stock)" : "")} · {Have(was.Recipe)} in stock", ItemColour(was.Recipe));
            }
        }
        _seenFab[s.Id] = current == null ? (0, "", 0f, false) : (current.Id, current.RecipeId, current.Progress.ToFloat(), current.Auto);
    }

    // ---------------------------------------------------------------- inventory

    private void ToggleInventory()
    {
        if (_inOrbit || Me.Phase != LandingPhase.Landed) return;
        _inventory.Toggle();
    }

    /// <summary>While the inventory is open: I / Esc close it and no other key reaches the game. True = used.</summary>
    private bool InventoryInput(InputEvent e)
    {
        if (!_inventory.IsOpen || e is not InputEventKey key) return false;
        if (key is { Pressed: true, Echo: false } && key.Keycode is Key.Escape or Key.I) _inventory.Close();
        return true;
    }

    private void SetStockTarget(string itemId, int target)
    {
        _pendingTargets[itemId] = (target, _sim.Tick);
        _sim.Schedule(_sim.Tick + InputDelayTicks, new StockTargetCommand(LocalPlayer, itemId, target));
    }

    /// <summary>The target the player just set, until the sim shows it (or 3 s pass: refused).</summary>
    private int TargetOf(string itemId)
    {
        var sim = Surface.StockTarget(LocalPlayer, itemId);
        if (!_pendingTargets.TryGetValue(itemId, out var p)) return sim;
        if (sim == p.Target || _sim.Tick - p.Tick > 3 * Simulation.TickRate)
        {
            _pendingTargets.Remove(itemId);
            return sim;
        }
        return p.Target;
    }

    private InventoryView InventoryNow()
    {
        var stored = Me.Phase == LandingPhase.Landed ? (Surface.Stored(_sim, LocalPlayer) + Surface.InFlight(LocalPlayer)).ToFloat() : 0f;
        var capacity = Surface.StorageCapacity(LocalPlayer).ToFloat();
        var items = Surface.PartItems;
        if (items.Count == 0) return new InventoryView(System.Array.Empty<InventoryRow>(), stored, capacity, 0f, "Parts economy not in this build yet");
        var fab = Surface.Structures.FirstOrDefault(s => s.Owner == LocalPlayer && Surface.IsFabricator(s) && !Surface.IsHubWorkshop(s))
                  ?? Surface.Structures.FirstOrDefault(s => s.Owner == LocalPlayer && Surface.IsFabricator(s));
        var rows = new List<InventoryRow>();
        var parts = 0f;
        foreach (var id in items)
        {
            var volume = VolumeOf(id);
            var have = Have(id);
            parts += have * volume;
            var tip = new StringBuilder(ItemName(id));
            if (fab != null && Surface.RecipesAt(fab).Contains(id))
            {
                var inputs = Surface.InputsAt(fab, id);
                tip.Append("\nMade from ").Append(inputs.Count == 0 ? "nothing" : string.Join(" + ", inputs.Select(a => $"{Hud.Whole(a.Amount)} {_data.Resources[a.Resource].Name}")));
                var seconds = Surface.SecondsAt(fab, id).ToFloat();
                if (seconds > 0) tip.Append($" · {Clock(seconds)} at the {(Surface.IsHubWorkshop(fab) ? "core workshop" : _data.Building(fab.DefId).Name)}");
            }
            else tip.Append(ItemTier(id) == "spare" ? "\nMade at a Machine Shop, or a colony core's workshop" : "\nMade at a Machine Shop");
            var use = UsedFor(id, null);
            if (use.Length > 0) tip.Append("\nFor ").Append(use);
            rows.Add(new InventoryRow(id, ItemName(id), ItemCode(id), ItemTier(id), ItemColour(id), have, TargetOf(id), Surface.Queued(LocalPlayer, id), volume, tip.ToString()));
        }
        return new InventoryView(rows, stored, capacity, parts, "");
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>From UpdateHud: the inventory closes in orbit; the base-tab chips; the workshop view ends when its core goes.</summary>
    private void UpdateParts()
    {
        if (_inventory.IsOpen && (_inOrbit || _achScreen.IsOpen)) _inventory.Close(); // F3 opens achievements over it
        if (_workshop && !WorkshopAvailable()) _workshop = false;
        var landed = Me.Phase == LandingPhase.Landed && !_inOrbit;
        _hud.ShowTools(landed, landed && WorkshopAvailable(), WorkshopShown());
        UpdateKitsShort(landed);
        UpdateItemCard();
    }

    private double _kitsShortAt;

    /// <summary>Once a second: the red KITS SHORT chip while any of our repairs waits for kits (tooltip = what waits).</summary>
    private void UpdateKitsShort(bool landed)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _kitsShortAt) return;
        _kitsShortAt = now + 1;
        if (!landed || Surface.PartItems.Count == 0)
        {
            _hud.ShowKitsShort(null, "");
            return;
        }
        var lines = new List<string>();
        foreach (var s in Surface.Structures)
            if (s.Owner == LocalPlayer && Surface.RepairWaitReason(s) is { } why) lines.Add($"{BuildingName(s.DefId)}: {why}");
        foreach (var u in Surface.Units)
            if (u.Owner == LocalPlayer && Surface.RepairWaitReason(u) is { } why) lines.Add($"{UnitName(u)}: {why}");
        _hud.ShowKitsShort(lines.Count == 0 ? null : $"KITS SHORT · {lines.Count}",
            "Repairs waiting for spare kits (click: inventory, J: jury-rig the selected one)\n" + string.Join("\n", lines.Take(8)) + (lines.Count > 8 ? $"\n+ {lines.Count - 8} more" : ""));
    }
}
