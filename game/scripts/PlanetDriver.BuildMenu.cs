using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The build menu (owner 2026-11-06, docs/mockups/build-card): command card option C, the slide-up build drawer (B), and
// the large centre card. Flow for a building: a pick (drawer tile, recent tile on the command card, number key) opens
// the large card; PLACE / Enter / the same pick again arms placement, and the card shrinks to the placing strip; Shift
// skips the card and places at once. A Vehicle Bay order opens the same large card for the vehicle (Enter / a second
// pick orders it). Pictures are rendered in the game from the real models (ModelThumbnails). Research-gated buildings
// stay out of the drawer until their tech is known (ShowLockedBuildings turns on a greyed, locked tile instead).
public partial class PlanetDriver
{
    /// <summary>Show research-gated buildings greyed with a lock (true) or leave them out until researched (false, the owner's pick for now).</summary>
    private const bool ShowLockedBuildings = false;

    private ModelThumbnails _thumbs = null!;
    private BuildDrawer _drawer = null!;
    private BuildingCard _bigCard = null!;
    private PlacingStrip _strip = null!;
    private readonly List<string> _recent = new();
    private string? _cardKey;
    private bool _cardRange;
    private int _orderPictures = -1;
    private System.Func<int, Fix64, bool> _have = null!;

    private void SetupBuildMenu()
    {
        _thumbs = new ModelThumbnails((key, colour) =>
        {
            if (key.StartsWith("unit:")) return BuildingModels.Vehicle(_data.SurfaceUnit(key[5..]), colour);
            return BuildingModels.Build(key, colour);
        }, ColorOf(LocalPlayer));
        AddChild(_thumbs);

        _drawer = new BuildDrawer { Name = "BuildDrawer" };
        _hud.AddOverlay(_drawer);
        _strip = new PlacingStrip { Name = "PlacingStrip" };
        _hud.AddOverlay(_strip);
        _bigCard = new BuildingCard { Name = "BuildingCard" };
        _hud.AddOverlay(_bigCard);
        _bigCard.SetThumbnails(_thumbs);

        _drawer.Picked += id => PickBuilding(id, Input.IsKeyPressed(Key.Shift));
        _hud.RecentPicked += id => PickBuilding(id, Input.IsKeyPressed(Key.Shift));
        _hud.BuildButtonPressed += ToggleDrawer;
        _bigCard.ActionPressed += InfoAction;
        _bigCard.CloseRequested += () => _info = null;
        _strip.CloseRequested += StopPlacing;
        _strip.CardRequested += ReopenCard;
        _have = (r, amount) => _sim.Players[LocalPlayer].Stock[r] >= amount;
    }

    // ---------------------------------------------------------------- what is on the menu

    private bool KnowsTech(string techId) => _data.HasTech(techId) && Me.Known[_data.Tech(techId).Index];

    /// <summary>Research-gated and not yet researched.</summary>
    private bool Locked(string id) => _data.Building(id).RequiresTech is { Length: > 0 } t && !KnowsTech(t);

    /// <summary>The buildings on the menu now, in build_menu order (which is also number-key order).</summary>
    private List<string> MenuBuildings()
    {
        // Worked out once a frame (keys, tiles and hints all ask for it).
        var frame = Engine.GetProcessFrames();
        if (_menuFrame != frame || _menuData != _data)
        {
            _menuFrame = frame;
            _menuData = _data;
            _menu.Clear();
            foreach (var id in _data.BuildMenu)
                if (ShowLockedBuildings || !Locked(id)) _menu.Add(id);
        }
        return _menu;
    }

    private readonly List<string> _menu = new();
    private ulong _menuFrame = ulong.MaxValue;
    private GameData? _menuData;
    private string _drawerSignature = "";

    /// <summary>A building's number key ("1".."9"), or "" when it has none (hidden, or past 9).</summary>
    private string BuildKey(string id)
    {
        var i = MenuBuildings().IndexOf(id);
        return i is >= 0 and < 9 ? (i + 1).ToString() : "";
    }

    private bool IsBuildingCard(string? id) => id != null && !id.Contains(':') && _data.BuildMenu.Contains(id);

    /// <summary>Number keys pick buildings (not a bay's orders or a lab's techs) while the drawer, a building card or placement is up.</summary>
    private bool BuildingKeysActive => _drawer.IsOpen || IsBuildingCard(_info) || _placing != null;

    // ---------------------------------------------------------------- picking

    /// <summary>Number key n (0-based) on the building menu.</summary>
    private void PickBuildingByKey(int index, bool shift)
    {
        var menu = MenuBuildings();
        if (index < menu.Count) PickBuilding(menu[index], shift);
    }

    /// <summary>
    /// A building picked from the drawer, a recent tile or its key: the first pick opens the large card, the same pick
    /// again (or Enter / PLACE) arms placement; Shift arms it at once. Picking the building being placed stops placing.
    /// </summary>
    private void PickBuilding(string id, bool shift)
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (shift)
        {
            if (_placing != id) ArmPlacement(id, quiet: true);
            return;
        }
        if (_placing == id)
        {
            StopPlacing();
            return;
        }
        if (_info == id)
        {
            ArmPlacement(id, quiet: false);
            return;
        }
        OpenBuildingCard(id);
    }

    private void OpenBuildingCard(string id)
    {
        if (_placing != null) StopPlacing();
        StopFounding();
        _info = id;
    }

    /// <summary>The card's PLACE (or Enter, a second pick, Shift+pick): start placing. Quiet = Shift+pick, which arms
    /// placement even when the base can't afford it yet (the ghost shows red with the reason).</summary>
    private void ArmPlacement(string id, bool quiet)
    {
        if (!quiet && PlaceProblem(id) is { } why)
        {
            _hud.PushEvent($"Can't place the {BuildingName(id)}: {why}", new Color(1f, 0.7f, 0.42f));
            return;
        }
        if (Locked(id))
        {
            _hud.PushEvent($"{BuildingName(id)}: research {_data.Tech(_data.Building(id).RequiresTech).Name} first", new Color(1f, 0.7f, 0.42f));
            return;
        }
        _info = null;
        if (_placing != id) StartPlacing(id);
        CloseDrawer();
    }

    /// <summary>Why PLACE is off on the card (null = it can be placed somewhere).</summary>
    private string? PlaceProblem(string id)
    {
        if (Me.Phase != LandingPhase.Landed) return "land first";
        if (Locked(id)) return $"needs {_data.Tech(_data.Building(id).RequiresTech).Name} research at a Research Lab";
        if (!Surface.Bases(LocalPlayer).Any(b => b.State == StructureState.Complete)) return PlanetSurface.AssemblingProblem;
        var def = _data.Building(id);
        if (!_sim.Players[LocalPlayer].Stock.CanAfford(def.Cost) && !Surface.Players[LocalPlayer].FreeBuilding) return "not enough resources";
        return null;
    }

    /// <summary>I while placing (or the strip's CARD button): back to the large card.</summary>
    private void ReopenCard()
    {
        if (_placing == null) return;
        var id = _placing;
        StopPlacing();
        OpenBuildingCard(id);
    }

    private void ToggleDrawer()
    {
        if (Me.Phase != LandingPhase.Landed) return;
        if (_bigCard.IsOpen)
        {
            _info = null; // B over the card: back to the drawer
            _drawer.SetOpen(true);
            return;
        }
        _drawer.SetOpen(!_drawer.IsOpen);
    }

    private void CloseDrawer() => _drawer.SetOpen(false);

    /// <summary>A building was placed: it goes to the front of the command card's recent tiles.</summary>
    private void NoteRecent(string id)
    {
        _recent.Remove(id);
        _recent.Insert(0, id);
        while (_recent.Count > 3) _recent.RemoveAt(_recent.Count - 1);
    }

    private bool BuildMenuBlocksMouse(Vector2 mouse) => _bigCard.IsOpen || _drawer.IsOverUi(mouse) || _strip.IsOverUi(mouse);

    // ---------------------------------------------------------------- per frame

    /// <summary>Once a frame from UpdateHud: the drawer, the large card (building or vehicle), the placing strip and the
    /// base's build area while a building's card is open. In orbit all of it is hidden.</summary>
    private void UpdateBuildMenu()
    {
        var delta = (float)GetProcessDeltaTime();
        if (_inOrbit || Me.Phase != LandingPhase.Landed)
        {
            if (_drawer.IsOpen) CloseDrawer();
            _drawer.Refresh(delta, _have, null, false, _thumbs);
            if (_bigCard.IsOpen) _bigCard.Close();
            _cardKey = null;
            _strip.Display(null, null);
            return;
        }

        // Drawer: its tiles are rebuilt only when the list changes (research, data tuning).
        if (_drawer.IsOpen || _drawer.Visible)
        {
            var menu = MenuBuildings();
            var signature = string.Join(",", menu.Select(id => Locked(id) ? id + "!" : id)) + "|" + RuntimeHelpers.GetHashCode(_data);
            var title = $"BUILD AT {BaseName(_selectedBase).ToUpperInvariant()}  ·  ALL BUILDINGS";
            var keys = menu.Count == 0 ? "B OR ESC CLOSE" : $"1–{Mathf.Min(9, menu.Count)} CARD  ·  SHIFT PLACES AT ONCE  ·  B OR ESC CLOSE";
            if (signature != _drawerSignature)
            {
                _drawerSignature = signature;
                var groups = new List<DrawerGroup>();
                for (var g = 0; g <= HudKit.Groups.Length; g++)
                {
                    var tiles = menu.Where(id => HudKit.GroupOf(_data.Building(id).Category) == g).Select(DrawerTileFor).ToList();
                    if (tiles.Count > 0) groups.Add(new DrawerGroup(g < HudKit.Groups.Length ? HudKit.Groups[g].Name : "Other", tiles));
                }
                _drawer.SetContent(signature, title, keys, groups);
            }
            else _drawer.SetHead(title, keys);
        }
        _drawer.Refresh(delta, _have, _placing ?? (IsBuildingCard(_info) ? _info : null), true, _thumbs);

        // Large card: a building's (not while placing it) or a Vehicle Bay order's.
        var cardOpen = _info != null && (IsBuildingCard(_info) || _info.StartsWith("unit:"));
        if (cardOpen)
        {
            var key = $"{_info}|{Me.Known.Count(k => k)}|{_selectedBase}|{RuntimeHelpers.GetHashCode(_data)}|{SelectedBay()?.Production.Count}";
            if (key != _cardKey || !_bigCard.IsOpen)
            {
                _cardKey = key;
                _bigCard.Open(_info!.StartsWith("unit:") ? UnitCardView(_info[5..]) : BuildingCardView(_info));
            }
            _bigCard.SetLive(_info!.StartsWith("unit:") ? UnitLive(_info[5..]) : BuildingLive(_info));
        }
        else if (_bigCard.IsOpen)
        {
            _bigCard.Close();
            _cardKey = null;
        }

        // The base's build area while a building's card is open (placement shows it too).
        if (IsBuildingCard(_info) && !_cardRange && _placing == null)
        {
            var core = Surface.FindStructure(_selectedBase);
            if (core != null) _view.ShowBuildRange(core.Tile, _data.Building(core.DefId).BuildRadius);
            _cardRange = true;
        }
        else if (!IsBuildingCard(_info) && _cardRange)
        {
            _cardRange = false;
            if (_placing == null) _view.HideBuildRange();
        }

        _strip.Display(_placing != null ? StripFor(_placing) : null, _thumbs);
    }

    /// <summary>The command card in build mode: BUILD [B] and the last three builds (the first three on the menu until then).</summary>
    private void ShowBuildCommands(string title, bool enabled)
    {
        var menu = MenuBuildings();
        _recent.RemoveAll(id => !menu.Contains(id));
        var recent = _recent.Concat(menu.Where(id => !_recent.Contains(id))).Take(3).ToList();
        var tiles = recent.Select(id =>
        {
            var def = _data.Building(id);
            return new RecentTile(id, def.Name, BuildKey(id), ShortCost(def.Cost), _placing == id || _info == id);
        }).ToList();
        _hud.ShowBuildStrip(title, new BuildStripView($"[B]  ·  {menu.Count} buildings", tiles, enabled, _drawer.IsOpen), _thumbs);
    }

    /// <summary>Vehicle Bay orders get renders of their vehicles (asked for once, filled in as they arrive).</summary>
    private void ShowOrderPictures(BuildingDef bay)
    {
        if (_thumbs.Version == _orderPictures) return;
        _orderPictures = _thumbs.Version;
        foreach (var u in bay.Produces) _hud.SetCommandPicture($"unit:{u}", _thumbs.Thumb($"unit:{u}"));
    }

    /// <summary>Tooltip for a drawer tile, a recent tile or the BUILD button under the mouse (null = none).</summary>
    private string? BuildTooltip()
    {
        if (_inOrbit || _bigCard.IsOpen) return null;
        var id = _drawer.IsOpen && _drawer.HoveredId != null ? _drawer.HoveredId
            : _hud.BlocksMouse(GetViewport().GetMousePosition()) ? _hud.HoveredCommand : null; // a recent tile or the BUILD button
        if (id == "build:drawer") return "Build [B]\nEvery building with its picture, cost and key. Pick one for its card; Shift+pick places it at once.";
        if (!IsBuildingCard(id)) return null;
        var def = _data.Building(id!);
        var sb = new StringBuilder();
        var key = BuildKey(id!);
        sb.Append(def.Name).Append(key.Length > 0 ? $"  [{key}]" : "").Append('\n');
        sb.Append(Amounts(def.Cost, false)).Append("  ·  ").Append(def.BuildSeconds).Append(" s to build");
        var power = PowerText(def);
        if (power.Length > 0) sb.Append("  ·  ").Append(power);
        if (def.Effect.Length > 0) sb.Append('\n').Append(def.Effect);
        if (Locked(id!)) sb.Append("\nNeeds ").Append(_data.Tech(def.RequiresTech).Name).Append(" research");
        sb.Append("\nClick for the full card  ·  Shift+click to place it at once");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- text helpers

    private DrawerTile DrawerTileFor(string id)
    {
        var def = _data.Building(id);
        var locked = Locked(id);
        return new DrawerTile(id, def.Name, BuildKey(id), locked, locked ? _data.Tech(def.RequiresTech).Name : "",
            def.Cost.OrderByDescending(a => a.Amount).Select(a => new CostPart(_data.Resources[a.Resource].Id, a.Resource, a.Amount,
                $"{Hud.Whole(a.Amount)} {HudKit.Short(_data.Resources[a.Resource].Name)}")).ToList());
    }

    private List<(string ResourceId, string Text, bool Short)> ShortCost(IReadOnlyList<ResourceAmount> cost)
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        return cost.OrderByDescending(a => a.Amount).Select(a => (_data.Resources[a.Resource].Id,
            $"{Hud.Whole(a.Amount)} {HudKit.Short(_data.Resources[a.Resource].Name)}", stock[a.Resource] < a.Amount)).ToList();
    }

    /// <summary>"120 Duranium (340)": the cost with what you have, red when short.</summary>
    private List<(string ResourceId, string Text, bool Short)> LongCost(IReadOnlyList<ResourceAmount> cost)
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        return cost.OrderByDescending(a => a.Amount).Select(a => (_data.Resources[a.Resource].Id,
            $"{Hud.Whole(a.Amount)} {_data.Resources[a.Resource].Name}  ({Hud.Whole(stock[a.Resource])})", stock[a.Resource] < a.Amount)).ToList();
    }

    private string Shortfall(IReadOnlyList<ResourceAmount> cost)
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        var missing = cost.Where(a => stock[a.Resource] < a.Amount).Select(a => $"{Hud.Whole(a.Amount - stock[a.Resource])} more {_data.Resources[a.Resource].Name}").ToList();
        return missing.Count == 0 ? "" : $"You need {string.Join(" and ", missing)}. The button lights up as soon as the stock is there.";
    }

    private static string PowerText(BuildingDef def) =>
        def.PowerSupply > Fix64.Zero ? $"+{Hud.Whole(def.PowerSupply)} power" : def.PowerUse > Fix64.Zero ? $"−{Hud.Whole(def.PowerUse)} power" : "";

    private List<(string ResourceId, string Text)> Flow(IReadOnlyList<ResourceAmount> amounts, string suffix = "") =>
        amounts.Select(a => (_data.Resources[a.Resource].Id, $"{Hud.Whole(a.Amount)} {_data.Resources[a.Resource].Name}{suffix}")).ToList();

    /// <summary>Amounts with decimals kept ("0.3 Duranium"): Amounts floors to whole numbers.</summary>
    private string Exact(IReadOnlyList<ResourceAmount> amounts) =>
        string.Join(", ", amounts.Select(a => $"{Num(a.Amount.ToFloat())} {_data.Resources[a.Resource].Name}"));

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

    private static string Names(IEnumerable<string> names, int max = 5)
    {
        var list = names.Distinct().ToList();
        if (list.Count == 0) return "";
        return list.Count <= max ? Join(list) : Join(list.Take(max).Append($"{list.Count - max} more").ToList());
    }

    private static string Join(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    private string ResName(int resource) => _data.Resources[resource].Name;

    /// <summary>The level's stat totals: speed, yield, wear shares and whether drones maintain it.</summary>
    private static (float Speed, float Yield, float Wear, bool Auto) LevelStats(IReadOnlyList<Modifier> mods)
    {
        float speed = 0, yield = 0, wear = 0;
        var auto = false;
        foreach (var m in mods)
            switch (m.Stat)
            {
                case Modifier.Speed: speed += m.Add.ToFloat(); break;
                case Modifier.Yield: yield += m.Add.ToFloat(); break;
                case Modifier.Wear: wear += m.Add.ToFloat(); break;
                case Modifier.AutoMaintain: auto = true; break;
            }
        return (speed, yield, wear, auto);
    }

    private static string Num(float v) => Mathf.IsEqualApprox(v, Mathf.Round(v)) ? $"{Mathf.RoundToInt(v)}" : $"{v:0.#}";

    /// <summary>What a level makes a minute (cycles, ore, research) with its speed and yield totals.</summary>
    private static string RateAt(BuildingDef def, float speed, float yield)
    {
        if (def.IsConverter) return $"{Num(def.CyclesPerMinute.ToFloat() * (1 + speed))} cycles/min";
        if (def.IsMine) return $"{Num(def.ExtractPerMinute.ToFloat() * (1 + speed) * (1 + yield))}/min";
        if (def.IsLab) return $"{Num(def.ResearchPerMinute.ToFloat() * (1 + speed))} RP/min";
        if (def.OutputPerMinute.Count > 0) return string.Join(", ", def.OutputPerMinute.Select(a => $"{Num(a.Amount.ToFloat() * (1 + speed) * (1 + yield))}/min"));
        return speed != 0 ? $"speed ×{1 + speed:0.##}" : "";
    }

    // ---------------------------------------------------------------- the building card

    /// <summary>Everything about a building from the data: what it makes, levels, research, placement, links, wear, tips.</summary>
    private CardView BuildingCardView(string id)
    {
        var def = _data.Building(id);
        var key = BuildKey(id);
        var category = HudKit.CategoryNames.GetValueOrDefault(def.Category, def.Category);
        var levelLine = def.Levels.Count > 0 ? "Mk I → " + string.Join(" → ", def.Levels.Select(l => l.Name)) : "Mk I  ·  no upgrades";

        // Stat tiles.
        var stats = new List<CardStat> { new("Build", $"{def.BuildSeconds} s") };
        if (def.PowerSupply > Fix64.Zero) stats.Add(new("Power", $"+{Hud.Whole(def.PowerSupply)}", HudKit.Good));
        else if (def.PowerUse > Fix64.Zero) stats.Add(new("Power", $"−{Hud.Whole(def.PowerUse)}"));
        else stats.Add(new("Power", "none"));
        if (def.IsConverter) stats.Add(new("Cycles", $"{Hud.Whole(def.CyclesPerMinute)}/min"));
        if (def.IsMine) stats.Add(new("Digs", $"{Hud.Whole(def.ExtractPerMinute)}/min"));
        if (def.IsLab) stats.Add(new("Research", $"{Hud.Whole(def.ResearchPerMinute)}/min"));
        foreach (var o in def.OutputPerMinute.Take(2)) stats.Add(new("Makes", $"{Hud.Whole(o.Amount)} {HudKit.Short(ResName(o.Resource))}/min", HudKit.Good));
        if (def.IsFactory) stats.Add(new("Queue", $"{def.ProductionQueue} orders"));
        if (def.Storage > Fix64.Zero) stats.Add(new("Storage", $"+{Hud.Whole(def.Storage)}"));
        foreach (var u in def.UpkeepPerMinute.Take(1)) stats.Add(new("Upkeep", $"{Hud.Whole(u.Amount)} {HudKit.Short(ResName(u.Resource))}/min", HudKit.Warn));
        stats.Add(new("Wear", def.Wears ? $"{Hud.Whole(def.WearPerMinute)}/min" : "none"));
        if (stats.Count > 8) stats.RemoveRange(8, stats.Count - 8);

        var sections = new List<CardSection>
        {
            new("What it makes", MakesBlocks(def)),
        };
        if (def.Levels.Count > 0) sections.Add(new("Levels", LevelBlocks(def)));
        sections.Add(new("Research that changes it", ResearchBlocks(def)));
        sections.Add(new("Where it goes", PlacementBlocks(def)));
        var links = LinkBlocks(def);
        if (links.Count > 0) sections.Add(new("Feeds and fed by", links));
        sections.Add(new("Wear and upkeep", WearBlocks(def)));
        var tips = Tips(def);
        if (tips.Count > 0) sections.Add(new("Tips", new CardBlock[] { new CardBullets(tips) }));

        return new CardView(id, id, def.Name, def.Levels.Count > 0 ? def.MaxLevel : 0, 1, levelLine,
            key.Length > 0 ? $"{category}  ·  key {key}" : category, "LIVE MODEL", stats,
            def.Description.Length > 0 ? def.Description : def.Effect, sections);
    }

    private CardLive BuildingLive(string id)
    {
        var def = _data.Building(id);
        var problem = PlaceProblem(id);
        var warning = problem == null ? "" : problem == "not enough resources" ? Shortfall(def.Cost) : char.ToUpperInvariant(problem[0]) + problem[1..] + ".";
        var action = Locked(id) ? "RESEARCH AT A LAB" : "PLACE [ENTER]";
        var range = Surface.FindStructure(_selectedBase) is { } core ? $" (the blue area round {BaseName(core.Id)})" : "";
        return new CardLive(LongCost(def.Cost), $"·  {def.BuildSeconds} s to build", warning, action, problem == null,
            $"Then click a tile in the build area{range}  ·  R rotate  ·  Shift+click place more  ·  Shift+pick skips this card");
    }

    private List<CardBlock> MakesBlocks(BuildingDef def)
    {
        var blocks = new List<CardBlock>();
        foreach (var r in def.Recipes) blocks.Add(new CardFlow("", Flow(r.In), Flow(r.Out)));
        if (def.IsConverter)
        {
            var maxIn = def.Recipes.Max(r => r.In.Sum(a => a.Amount.ToFloat())) * def.CyclesPerMinute.ToFloat();
            var maxOut = def.Recipes.Max(r => r.Out.Sum(a => a.Amount.ToFloat())) * def.CyclesPerMinute.ToFloat();
            blocks.Add(new CardText($"{Hud.Whole(def.CyclesPerMinute)} cycles a minute" + (def.Recipes.Count > 1 ? ", shared between the recipes that have input" : "") +
                $": up to {Num(maxIn)} in → {Num(maxOut)} out a minute at Mk I. It takes its input straight from storage; no input → idle" + (def.PowerUse > Fix64.Zero ? ", no power." : ".")));
        }
        if (def.OutputPerMinute.Count > 0) blocks.Add(new CardFlow("Makes", Flow(def.OutputPerMinute, " a minute"), System.Array.Empty<(string, string)>()));
        if (def.UpkeepPerMinute.Count > 0) blocks.Add(new CardFlow("Burns", Flow(def.UpkeepPerMinute, " a minute"), System.Array.Empty<(string, string)>(), "(without it, it stops)"));
        if (def.IsMine)
        {
            blocks.Add(new CardText(def.RequiresDepleted
                ? $"Goes on a used-up deposit that hasn't been reopened, reopens it once at {Mathf.RoundToInt(def.ReopenShare.ToFloat() * 100)}% of its first size and digs {Hud.Whole(def.ExtractPerMinute)} a minute from it into storage."
                : $"Digs {Hud.Whole(def.ExtractPerMinute)} a minute from the surveyed deposit under it into storage until it runs dry. Ore needs an Ore Refinery; other deposits come out ready to use."));
            var deposits = DepositPairs();
            if (deposits.Count > 0) blocks.Add(new CardPairs(deposits));
        }
        if (def.Storage > Fix64.Zero)
        {
            var exempt = _data.StorageExempt.Select(r => r.Name.ToLowerInvariant()).ToList();
            blocks.Add(new CardText($"+{Hud.Whole(def.Storage)} storage for ores, metals, fuel and strategics" + (exempt.Count > 0 ? $" ({Join(exempt)} need none)." : ".")));
        }
        if (def.PowerSupply > Fix64.Zero)
        {
            var core = Surface.FindStructure(_selectedBase);
            var line = $"Supplies {Hud.Whole(def.PowerSupply)} power to its base";
            if (core != null)
            {
                var p = Surface.PowerOf(core.Id);
                line += $" ({BaseName(core.Id)}: {Hud.Whole(p.Use)} used of {Hud.Whole(p.Supply)} → {Hud.Whole(p.Supply + def.PowerSupply)})";
            }
            blocks.Add(new CardText(line + ". Power is a budget, not a stockpile; a worn reactor supplies less."));
        }
        if (def.IsLab) blocks.Add(new CardText($"{Hud.Whole(def.ResearchPerMinute)} research points a minute toward the tech you pick at the lab; more labs research the same tech faster."));
        if (def.IsFactory)
        {
            blocks.Add(new CardPairs(def.Produces.Select(u =>
            {
                var ud = _data.SurfaceUnit(u);
                return (_data.GroundVehicleNames.GetValueOrDefault(u, u), $"{Amounts(ud.Cost, false)}  ·  {ud.BuildSeconds} s");
            }).ToList()));
            blocks.Add(new CardText($"One at a time, up to {def.ProductionQueue} orders queued, paid when ordered; each rolls out of the bay door when done.", Dim: true));
        }
        if (blocks.Count == 0) blocks.Add(new CardText(def.Effect.Length > 0 ? def.Effect : "It doesn't make anything by itself."));
        return blocks;
    }

    /// <summary>Which deposits this planet has and on what ground (surfaces.json planets deposits).</summary>
    private List<(string, string)> DepositPairs()
    {
        var list = new List<(string, string)>();
        foreach (var d in Surface.Map.Def.Deposits)
        {
            var terrain = d.Terrain.Select(t => _data.Terrain.FirstOrDefault(x => x.Id == t)?.Name.ToLowerInvariant() ?? t).ToList();
            list.Add((_data.Resource(d.ResourceId).Name, $"on {Join(terrain)}" + (d.Coastal ? " by the water" : "") + $", {d.Min}–{d.Max} a deposit"));
        }
        return list;
    }

    private List<CardBlock> LevelBlocks(BuildingDef def)
    {
        var rows = new List<IReadOnlyList<string>>();
        var wear = def.WearPerMinute.ToFloat();
        string RateWear(float speed, float yield, float wearShare, bool auto)
        {
            var rate = RateAt(def, speed, yield);
            var w = def.Wears ? $"{Num(wear * (1 + wearShare))}/min wear" : "no wear";
            return (rate.Length > 0 ? rate + "\n" : "") + w + (auto ? " · drones" : "");
        }
        rows.Add(new[] { "Mk I", Amounts(def.Cost, false, shortNames: true).Replace(" · ", "\n"), $"{def.BuildSeconds} s", "base", RateWear(0, 0, 0, false) });
        foreach (var l in def.Levels)
        {
            var (speed, yield, wearShare, auto) = LevelStats(l.Modifiers);
            var effect = string.Join(", ", l.Modifiers.Select(ModifierText));
            var core = _data.Buildings.FirstOrDefault(b => b.IsCore && b.Level == l.RequiresLevel);
            if (l.RequiresLevel > 1) effect += $"\nneeds the Mk before it and a {(core?.Name ?? $"level {l.RequiresLevel} base")}";
            rows.Add(new[] { l.Name, Amounts(l.Cost, false, shortNames: true).Replace(" · ", "\n"), $"{l.BuildSeconds} s", effect, RateWear(speed, yield, wearShare, auto) });
        }
        return new List<CardBlock>
        {
            new CardTable(new[] { "Level", "Cost", "Build", "Effect", "Rate · wear" }, new[] { 0.12f, 0.17f, 0.1f, 0.33f, 0.28f }, rows),
            new CardText("Modifiers are totals for that level, not added to the one before. Upgrade a finished building with U (or its panel's button).", Dim: true),
        };
    }

    /// <summary>Techs that change this building (its stats, or every building's), and the one that unlocks it.</summary>
    private List<CardBlock> ResearchBlocks(BuildingDef def)
    {
        var pairs = new List<(string, string)>();
        foreach (var t in _data.Techs)
        {
            var applies = t.Modifiers.Any(m => m.Units.Count == 0 && (m.Buildings.Count == 0 ? def.Wears || (m.Stat != Modifier.Wear && m.Stat != Modifier.AutoMaintain) : m.Buildings.Contains(def.Id))
                && (m.Stat is not Modifier.Wear and not Modifier.AutoMaintain || def.Wears));
            var unlocks = def.RequiresTech == t.Id;
            if (!applies && !unlocks) continue;
            var done = KnowsTech(t.Id) ? "  (done)" : "";
            pairs.Add(($"{Hud.Whole(t.Cost)} RP", $"{t.Name}{done}  ·  {(unlocks ? "unlocks it. " : "")}{t.Effect}"));
        }
        if (pairs.Count == 0) return new List<CardBlock> { new CardText("No research changes it yet.", Dim: true) };
        return new List<CardBlock> { new CardPairs(pairs), new CardText("Research at a Research Lab: select the lab and pick the tech.", Dim: true) };
    }

    private List<CardBlock> PlacementBlocks(BuildingDef def)
    {
        var chips = _data.Terrain.Select(t => (t.Name, Color.HtmlIsValid(t.Color) ? Color.FromHtml(t.Color) : HudKit.Dim, t.Buildable)).ToList();
        var core = Surface.FindStructure(_selectedBase);
        var coreDef = core != null ? _data.Building(core.DefId) : _data.Buildings.FirstOrDefault(b => b.IsCore && b.Level == 1);
        var where = new StringBuilder("Needs a surveyed tile on buildable ground");
        if (coreDef != null) where.Append($" within {coreDef.BuildRadius} tiles of a finished {coreDef.Name} (its build radius");
        var bigger = coreDef != null && coreDef.UpgradesTo.Length > 0 ? _data.Building(coreDef.UpgradesTo) : null;
        if (bigger != null) where.Append($"; {bigger.BuildRadius} as {bigger.Name}");
        if (coreDef != null) where.Append(')');
        where.Append(" with nothing on it: no building, deployed crawler or native land.");
        if (def.RequiresDeposit) where.Append(" It must sit on a surveyed deposit.");
        if (def.RequiresDepleted) where.Append(" It must sit on a used-up deposit that hasn't been reopened.");
        if (def.RequiresLevel > 0)
        {
            var need = _data.Buildings.FirstOrDefault(b => b.IsCore && b.Level == def.RequiresLevel);
            where.Append($" Its base must be level {def.RequiresLevel}{(need != null ? $" ({need.Name})" : "")}.");
        }
        return new List<CardBlock>
        {
            new CardChips(chips),
            new CardText(where.ToString()),
            new CardText($"R turns it 60°; Shift+click places another. X cancels it unfinished (full refund) or salvages it finished ({Mathf.RoundToInt(_data.SalvageRefund.ToFloat() * 100)}% back).", Dim: true),
        };
    }

    /// <summary>What it takes from the economy and where that comes from; what it gives and who uses it.</summary>
    private List<CardBlock> LinkBlocks(BuildingDef def)
    {
        var menu = _data.BuildMenu.Select(_data.Building).ToList();
        var core = _data.Buildings.Where(b => b.IsCore).ToList();
        var crawlers = _data.SurfaceUnits.Where(u => u.CanMine).Select(u => _data.GroundVehicleNames.GetValueOrDefault(u.Id, u.Id)).ToList();
        var depositRes = Surface.Map.Def.Deposits.Select(d => _data.Resource(d.ResourceId).Index).ToHashSet();

        string Sources(int r)
        {
            var from = new List<string>();
            foreach (var b in menu.Concat(core.Take(1)))
                if (b.OutputPerMinute.Any(a => a.Resource == r) || b.Recipes.Any(x => x.Out.Any(a => a.Resource == r))) from.Add(b.Name);
            if (depositRes.Contains(r))
            {
                from.Add($"Mining Complexes on its deposits");
                from.AddRange(crawlers.Select(c => $"{c} cargo pods"));
            }
            return from.Count == 0 ? "nothing on the surface yet (drop-ship supplies)" : Names(from);
        }

        string Users(int r)
        {
            var to = new List<string>();
            foreach (var b in menu)
            {
                if (b.Id == def.Id) continue;
                if (b.Recipes.Any(x => x.In.Any(a => a.Resource == r)) || b.UpkeepPerMinute.Any(a => a.Resource == r)) to.Add(b.Name);
            }
            if (_data.SurfaceUnits.Any(u => u.Cost.Any(a => a.Resource == r)) && menu.Any(b => b.IsFactory)) to.Add("Vehicle Bay orders");
            var builds = menu.Where(b => b.Cost.Any(a => a.Resource == r)).Select(b => b.Name).ToList();
            if (builds.Count > 0) to.Add($"building {Names(builds, 3)}");
            var levels = menu.SelectMany(b => b.Levels.Where(l => l.Cost.Any(a => a.Resource == r)).Select(l => l.Name)).Distinct().ToList();
            if (levels.Count > 0) to.Add($"{Join(levels)} upgrades");
            foreach (var c in core)
                if (c.Cost.Any(a => a.Resource == r) && c.Level > 1) to.Add($"the {c.Name}");
            return to.Count == 0 ? "storage" : Names(to, 6);
        }

        var takes = new List<string>();
        var ins = def.Recipes.SelectMany(x => x.In).Select(a => a.Resource).Concat(def.UpkeepPerMinute.Select(a => a.Resource)).Distinct().ToList();
        foreach (var r in ins) takes.Add($"{ResName(r)} from storage: {Sources(r)}");
        if (def.IsMine) takes.Add(def.RequiresDepleted ? "a used-up deposit, once" : "the surveyed deposit under it, until it runs dry");
        if (def.PowerUse > Fix64.Zero) takes.Add($"{Hud.Whole(def.PowerUse)} power from its base (Fusion Reactors and the colony core)");
        if (def.IsFactory) takes.Add("each order's cost, paid when you order it");

        var gives = new List<string>();
        var outs = def.Recipes.SelectMany(x => x.Out).Select(a => a.Resource).Concat(def.OutputPerMinute.Select(a => a.Resource)).Distinct().ToList();
        foreach (var r in outs) gives.Add($"{ResName(r)} into storage: {Users(r)}");
        if (def.IsMine) gives.Add("ore into storage for the Ore Refinery; deuterium, dilithium and strategics ready to use");
        if (def.PowerSupply > Fix64.Zero) gives.Add($"{Hud.Whole(def.PowerSupply)} power for every working building at its base");
        if (def.Storage > Fix64.Zero) gives.Add("room for everything mined and refined; cargo pods land at the nearest building with storage");
        if (def.IsLab) gives.Add("research points toward the tech you pick");
        if (def.IsFactory) gives.Add(Join(def.Produces.Select(u => _data.GroundVehicleNames.GetValueOrDefault(u, u)).ToList()) + ", rolled out of the bay door");

        var pairs = new List<(string, string)>();
        if (takes.Count > 0) pairs.Add(("Takes", string.Join("\n", takes)));
        if (gives.Count > 0) pairs.Add(("Gives", string.Join("\n", gives)));
        return pairs.Count == 0 ? new List<CardBlock>() : new List<CardBlock> { new CardPairs(pairs) };
    }

    private List<CardBlock> WearBlocks(BuildingDef def)
    {
        var blocks = new List<CardBlock>();
        var m = _data.Maintenance;
        if (def.Wears && m != null)
        {
            var w = def.WearPerMinute.ToFloat();
            var toHalf = (100f - m.DeclineBelow.ToFloat()) / Mathf.Max(0.01f, w);
            blocks.Add(new CardText($"Loses {Hud.Whole(def.WearPerMinute)} condition a minute while working (about {Mathf.RoundToInt(toHalf)} min from new to {Hud.Whole(m.DeclineBelow)}%). " +
                $"Below {Hud.Whole(m.DeclineBelow)}% it slows, down to {Mathf.RoundToInt(m.SlowestOutput.ToFloat() * 100)}% output at 0, where it can break ({Mathf.RoundToInt(m.BreakChanceAtZero.ToFloat() * 100)}% a minute)."));
            var full = m.CostPerPoint.Select(c => new ResourceAmount(c.Resource, c.Amount * Fix64.FromInt(100))).ToList();
            var drones = def.Levels.FirstOrDefault(l => l.Modifiers.Any(x => x.Stat == Modifier.AutoMaintain));
            blocks.Add(new CardText($"Press M and a crew walks out from the base's core: {Num(m.RepairPerSecond.ToFloat())} points a second, {Exact(m.CostPerPoint)} a point ({Amounts(full, false)} for a full repair). " +
                $"Drones keep it above {Hud.Whole(m.AutoBelow)}%{(drones != null ? $" at {drones.Name}" : "")} or with Automated Maintenance research."));
        }
        else blocks.Add(new CardText("Never wears and needs no maintenance."));
        if (def.UpkeepPerMinute.Count > 0)
            blocks.Add(new CardText($"Upkeep: {Amounts(def.UpkeepPerMinute, true)}. Without it, it stops (no output, no power)."));
        return blocks;
    }

    private List<string> Tips(BuildingDef def)
    {
        var tips = new List<string>();
        if (def.IsConverter && def.Recipes.Count > 1) tips.Add("With every input in stock it splits its cycles between the recipes.");
        if (def.IsConverter) tips.Add("It takes its input from storage as soon as it is built (there is no pause yet).");
        if ((def.IsConverter || def.IsMine) && def.PowerUse > Fix64.Zero) tips.Add("Idle (no input, storage full, used up) it draws no power.");
        if (def.RequiresDeposit) tips.Add("It must sit on a surveyed deposit: send a rover first. Salvage a used-up mine (X) for half its cost and the free tile.");
        if (def.RequiresDepleted) tips.Add("Salvage the old mine first to free the tile.");
        if (def.PowerSupply > Fix64.Zero) tips.Add("Base over budget? Every power user there runs at supply ÷ use. A worn reactor supplies less: keep it above 50% (M).");
        if (def.UpkeepPerMinute.Count > 0) tips.Add($"Without {Join(def.UpkeepPerMinute.Select(u => ResName(u.Resource)).ToList())} it shuts down.");
        if (def.Storage > Fix64.Zero && !def.IsConverter && _data.Buildings.FirstOrDefault(b => b.IsCore && b.Level == 1) is { } hub)
            tips.Add($"The {hub.Name}'s vault holds {Hud.Whole(hub.Storage)} and fills fast: build storage early. Refining frees room (metal takes half the space of its ore).");
        if (def.IsFactory) tips.Add("Orders are paid when placed; Backspace cancels the last one with a full refund. Low power slows the assembly.");
        if (def.IsLab)
        {
            tips.Add("Select the lab to pick a tech: the command card shows the list.");
            foreach (var b in _data.BuildMenu.Select(_data.Building).Where(b => b.RequiresTech.Length > 0 && _data.HasTech(b.RequiresTech)))
                tips.Add($"{_data.Tech(b.RequiresTech).Name} ({Hud.Whole(_data.Tech(b.RequiresTech).Cost)} RP) unlocks the {b.Name}.");
        }
        foreach (var l in def.Levels)
            foreach (var c in l.Cost.Where(c => def.Cost.All(b => b.Resource != c.Resource)))
            {
                var maker = _data.BuildMenu.Select(_data.Building).FirstOrDefault(b => b.Recipes.Any(r => r.Out.Any(a => a.Resource == c.Resource)) || b.OutputPerMinute.Any(a => a.Resource == c.Resource));
                tips.Add($"{l.Name} needs {ResName(c.Resource)}" + (maker != null ? $" (from the {maker.Name})." : "."));
            }
        return tips.Distinct().Take(5).ToList();
    }

    // ---------------------------------------------------------------- the vehicle card

    /// <summary>A Vehicle Bay order's large card: what the vehicle does, its stats, damage and tyres, breakdowns, repair.</summary>
    private CardView UnitCardView(string unitId)
    {
        var u = _data.SurfaceUnit(unitId);
        var name = _data.GroundVehicleNames.GetValueOrDefault(unitId, unitId);
        var bay = SelectedBay();
        var keyIndex = bay != null ? _data.Building(bay.DefId).Produces.ToList().IndexOf(unitId) : -1;
        var plains = _data.Terrain.FirstOrDefault(t => t.Id == "plains")?.MoveCost ?? 2;
        var stats = new List<CardStat>
        {
            new("Build", $"{u.BuildSeconds} s"),
            new("Speed", $"{plains / Mathf.Max(0.01f, u.Speed.ToFloat()):0.#} s/tile"),
            new("Survey", $"{u.SurveyRadius} tile{(u.SurveyRadius == 1 ? "" : "s")}"),
        };
        if (u.CanMine)
        {
            stats.Add(new("Digs", $"{Hud.Whole(u.ExtractPerMinute)}/min"));
            stats.Add(new("Hopper", Hud.Whole(u.Hopper)));
            stats.Add(new("Unfurls", $"{u.DeploySeconds} s"));
        }
        if (u.Tyres is { } ty) stats.Add(new("Tyres", $"{ty.Wheels} · {ty.Spares} spare"));
        if (u.Damage is { } dm && dm.Terrain.Count > 0)
            stats.Add(new("Wear", $"{Num(dm.Terrain.Min(t => t.Wear.ToFloat()))}–{Num(dm.Terrain.Max(t => t.Wear.ToFloat()))}/min"));
        if (u.Found is { } fd) stats.Add(new("Founds", $"{fd.MinDistance}+ tiles out"));
        if (stats.Count > 8) stats.RemoveRange(8, stats.Count - 8);

        var sections = new List<CardSection>();
        var does = new List<CardBlock>();
        does.Add(new CardText($"Reveals deposits within {u.SurveyRadius} tile{(u.SurveyRadius == 1 ? "" : "s")} as it drives (about {plains / Mathf.Max(0.01f, u.Speed.ToFloat()):0.#} s a plains tile; hills and forest are slower, weather slows it too)."));
        if (u.CanMine)
        {
            does.Add(new CardText($"Right-click a surveyed deposit: it drives there, unfurls in {u.DeploySeconds} s and digs {Hud.Whole(u.ExtractPerMinute)} a minute into its {Hud.Whole(u.Hopper)} hopper. " +
                $"Each full hopper flies home as a cargo pod at {Hud.Whole(u.PodSpeed)} tiles a second to the nearest building with storage; it waits when storage is full and packs up when the deposit is used up."));
        }
        if (u.Found is { } found)
        {
            var hub = _data.Building(found.Building);
            does.Add(new CardText($"Founds a new {hub.Name} at least {found.MinDistance} tiles from your bases: it drives there, pays {Amounts(found.Cost, false)}, unfolds in {u.DeploySeconds} s and is used up; " +
                $"its exocomps build the hub in {hub.BuildSeconds} s, then it is a base of its own (build radius {hub.BuildRadius}, {hub.BuildQueues} build queue)."));
        }
        sections.Add(new("What it does", does));

        if (u.Damage is { } d)
        {
            var rows = d.Terrain.Select(t => (IReadOnlyList<string>)new[]
            {
                _data.Terrain.FirstOrDefault(x => x.Id == t.Terrain)?.Name ?? t.Terrain, $"{Num(t.Wear.ToFloat())}", $"{Num(t.Hazard.ToFloat() * 100)}%", $"{Num(t.Flat.ToFloat() * 100)}%",
            }).ToList();
            var blocks = new List<CardBlock>
            {
                new CardTable(new[] { "Ground", "Wear/min", "Hazard/min", "Flat/min" }, new[] { 0.34f, 0.2f, 0.23f, 0.23f }, rows),
                new CardText($"Toxic ground corrodes {Hud.Whole(d.ToxicPerMinute)} a minute; predators attack ({Num(d.WildlifeChancePerMinute.ToFloat() * 100)}% a minute, {d.WildlifeMin}–{d.WildlifeMax} damage). " +
                    $"Below {Hud.Whole(d.SlowBelow)}% it drives slower (down to {Mathf.RoundToInt(d.SlowestDrive.ToFloat() * 100)}%), below {Hud.Whole(d.SensorsBelow)}% its survey shrinks by {d.SensorLoss}, and at 0 it is disabled: only a repair order moves it, at a crawl."),
            };
            if (d.Hazards.Count > 0)
                blocks.Add(new CardPairs(d.Hazards.Select(h => (h.Name, $"{h.DamageMin}–{h.DamageMax} damage{(h.Flat ? ", punctures a tyre" : "")} · on " +
                    Join(h.Terrain.Select(t => _data.Terrain.FirstOrDefault(x => x.Id == t)?.Name.ToLowerInvariant() ?? t).ToList()))).ToList()));
            sections.Add(new("Damage on the ground", blocks));
        }
        if (u.Tyres is { } tyres)
            sections.Add(new("Tyres", new CardBlock[]
            {
                new CardText($"{tyres.Wheels} wheels, {tyres.Spares} spare{(tyres.Spares == 1 ? "" : "s")}. One flat drives at {Mathf.RoundToInt(tyres.FlatSpeed.ToFloat() * 100)}% speed, two or more at {Mathf.RoundToInt(tyres.TwoFlatSpeed.ToFloat() * 100)}%, and each flat adds {Mathf.RoundToInt(tyres.FlatWear.ToFloat() * 100)}% wear. " +
                    $"T: the crew change one with a spare in {tyres.ChangeSeconds} s parked; the repair pad fits new tyres and restocks the spares for {Exact(tyres.PadCost)} a tyre."),
            }));
        if (u.Breakdown is { } b)
        {
            var bonus = Surface.UnitResearchBonus(LocalPlayer, unitId, Modifier.Breakdown);
            var chance = b.ChancePerMinute * Fix64.Max(Fix64.Zero, Fix64.One + bonus);
            sections.Add(new("Breakdowns", new CardBlock[]
            {
                new CardText($"About {Mathf.RoundToInt(chance.ToFloat() * 100)}% a minute while digging" + (bonus != Fix64.Zero ? $" (research {Pct(bonus)})" : "") +
                    $"; a broken crawler folds up and drives slower (down to {Mathf.RoundToInt(b.SlowestDrive.ToFloat() * 100)}%)."),
                new CardPairs(b.Severities.Select(v => (v.Name, $"{v.DamageMin}–{v.DamageMax} damage{(v.LosesLoad ? ", loses the load" : "")} · weight {v.Weight}")).ToList()),
            }));
        }
        if (u.Repair is { } r)
            sections.Add(new("Repair", new CardBlock[]
            {
                new CardText($"R (or right-click the colony core) sends it to the repair pad: after {r.CrewSeconds} s for the crew to walk out it regains {Num(r.PerSecond.ToFloat())} points a second for {Exact(r.CostPerPoint)} a point. Parked on the pad it also gets washed."),
            }));
        var techs = _data.Techs.Where(t => t.Modifiers.Any(m => m.Units.Contains(unitId))).Select(t => ($"{Hud.Whole(t.Cost)} RP", $"{t.Name}{(KnowsTech(t.Id) ? "  (done)" : "")}  ·  {t.Effect}")).ToList();
        sections.Add(new("Research that changes it", techs.Count > 0 ? new CardBlock[] { new CardPairs(techs) } : new CardBlock[] { new CardText("No research changes it yet.", Dim: true) }));
        if (bay != null)
            sections.Add(new("Built at", new CardBlock[]
            {
                new CardText($"This {_data.Building(bay.DefId).Name}: {bay.Production.Count} of {_data.Building(bay.DefId).ProductionQueue} orders queued. Paid when ordered, built one at a time (low power slows it), then it drives out of the door."),
            }));
        var tips = new List<string>();
        if (u.CanMine) tips.Add("F unfurls it where it stands or packs it up; a move order packs it up first.");
        if (u.CanFound) tips.Add("Select it and press F (or FOUND OUTPOST), then click a green site outside the red ring.");
        if (u.CanFound) tips.Add("X on the unfinished hub refunds the founding and gives the rover back.");
        if (!u.CanMine && !u.CanFound) tips.Add("Rovers survey the ground you need before you can build on it.");
        if (u.Tyres != null) tips.Add("T changes a flat with the spare; R sends it to the repair pad. V cycles the wipers, L the headlights.");
        sections.Add(new("Tips", new CardBlock[] { new CardBullets(tips) }));

        var mark = u.Mark ?? 0;
        return new CardView($"unit:{unitId}", $"unit:{unitId}", name, mark > 0 ? 3 : 0, mark, mark > 0 ? $"Mk {Roman(mark)} of three marks" : "Ground vehicle",
            keyIndex >= 0 && keyIndex < 9 ? $"Vehicle Bay  ·  key {keyIndex + 1}" : "Vehicle Bay", "LIVE MODEL", stats, u.Description, sections);
    }

    private CardLive UnitLive(string unitId)
    {
        var u = _data.SurfaceUnit(unitId);
        var name = _data.GroundVehicleNames.GetValueOrDefault(unitId, unitId);
        var bay = SelectedBay();
        var problem = bay == null ? "select a Vehicle Bay" : Surface.ProductionProblem(_sim, LocalPlayer, bay.Id, unitId);
        var warning = problem == null ? "" : problem == "not enough resources" ? Shortfall(u.Cost) : char.ToUpperInvariant(problem[0]) + problem[1..] + ".";
        var queue = bay != null ? $"  ·  {bay.Production.Count} of {_data.Building(bay.DefId).ProductionQueue} queued" : "";
        return new CardLive(LongCost(u.Cost), $"·  {u.BuildSeconds} s to assemble", warning,
            problem == null ? $"ORDER A {name.ToUpperInvariant()} [ENTER]" : "CAN'T ORDER", problem == null,
            $"Paid when ordered  ·  Backspace cancels the last order for a full refund{queue}");
    }

    // ---------------------------------------------------------------- the placing strip

    private StripView StripFor(string id)
    {
        var def = _data.Building(id);
        var warning = "";
        if (_hoverTile >= 0)
        {
            var problem = Surface.CheckPlacement(_sim, LocalPlayer, id, _hoverTile);
            if (problem != PlacementProblem.None) warning = $"Can't build on this tile: {Reason(problem)}";
        }
        return new StripView(id, $"Placing {def.Name}", ShortCost(def.Cost), warning,
            "Click a tile in the blue area  ·  R rotate  ·  Shift+click place more  ·  I card  ·  Esc / right-click stop");
    }
}
