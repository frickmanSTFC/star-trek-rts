using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

// The campaign's Choose Your Ship page (docs/ship-pick-contract.md C1, design page docs/mockups/ship-pick): between the
// era and the star-system picker. Six cards across (GameData.ShipChoices, pick order) with the card art
// (game/textures/ships/<id>.jpg, a plain panel when it is missing), the name and a role tag; under them the selected
// ship's detail: the art, its class, length, crew and speed, what it brings (colonizers' start bonus), "Your mission:
// <title>" with the summary, whether it lands, and ‹ BACK / CHOOSE <NAME> ›. Left / Right pick, Enter chooses.
// The pick decides what the system page asks for (MainMenu.SystemPick.cs): a world to land on, or any body to start
// in orbit of. A ship the fit-out data lists (the colony ships) goes through Fit Out Your Ship first (MainMenu.FitOut.cs). With no ships in the data the page is skipped (era → system, the old start).
// A ship with a 3D model (docs/ship-model-contract.md: models/ships/<id>.glb, ShipModels) shows it live in the detail
// panel in place of the still picture: turning slowly, rings spinning, drag to turn it (ShipViewer). The six cards and
// every ship without a glb keep their flat pictures. Debug builds: Shift+F11 swaps a primitive stand-in in for a
// missing glb (ShipModels.DebugStandIn).
public partial class MainMenu
{
    /// <summary>The ship picked on this page (a ShipChoices id), kept while the menu is open; null = no pick yet.</summary>
    private string? _shipId;

    private static readonly Dictionary<string, Texture2D?> ShipArt = new();

    /// <summary>The ships the data offers, in pick order (empty when the data has none or didn't load).</summary>
    private IReadOnlyList<ShipChoiceDef> ShipChoices => Data?.ShipChoices ?? (IReadOnlyList<ShipChoiceDef>)System.Array.Empty<ShipChoiceDef>();

    /// <summary>The picked ship, or null (none picked, or no ships in the data).</summary>
    private ShipChoiceDef? PickedShip => _shipId == null ? null : Data?.ShipChoice(_shipId);

    /// <summary>The page after the era: the ship pick when the data has ships, else straight to the system.</summary>
    private void AfterEra()
    {
        if (ShipChoices.Count == 0)
        {
            _shipId = null;
            ShowPage(Page.System);
            return;
        }
        if (PickedShip == null) _shipId = ShipChoices[0].Id;
        ShowPage(Page.Ship);
    }

    /// <summary>The page before the system: the fit-out when the picked ship has one, else the ship pick, or the era
    /// when the data has no ships.</summary>
    private Page BeforeSystem => ShipChoices.Count == 0 ? Page.Era : FitOutApplies(PickedShip) ? Page.FitOut : Page.Ship;

    /// <summary>Card art by res:// path (cached; null = missing or not imported yet).</summary>
    private static Texture2D? ShipTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (ShipArt.TryGetValue(path, out var cached)) return cached;
        Texture2D? texture = null;
        if (ResourceLoader.Exists(path)) texture = GD.Load<Texture2D>(path);
        ShipArt[path] = texture;
        return texture;
    }

    /// <summary>The role's tag text and colour (COLONIZE for any ship that colonizes).</summary>
    private static (string Text, Color Colour) RoleTag(ShipChoiceDef ship) =>
        ship.Colonizes ? ("COLONIZE", HudKit.Good) : ship.Role switch
        {
            "survey" => ("SURVEY", HudKit.Sky),
            "courier" => ("COURIER", HudKit.Accent),
            "patrol" => ("PATROL", HudKit.Warn),
            "diplomacy" => ("DIPLOMACY", new Color(0.75f, 0.62f, 1f)),
            _ => (ship.Role.ToUpperInvariant(), HudKit.Dim),
        };

    private Control BuildShipPage()
    {
        var (root, column) = NewPage(0.62f, 40f);
        var ships = ShipChoices;
        Header(column, "CAMPAIGN  ·  FEDERATION  ·  DAWN OF WARP", "CHOOSE YOUR SHIP",
            "The ship decides the first mission. Colony ships land and build; every other ship stays in space with missions of its own.");
        if (ships.Count == 0 || Data == null)
        {
            column.AddChild(Spacer());
            var b = BottomBar(column);
            b.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Era)));
            return root;
        }
        var picked = PickedShip ?? ships[0];
        _shipId = picked.Id;
        foreach (var ship in ships) ShipModels.Preload(ship.Id); // 3D models load in the background (once), ready when picked

        // Six cards across; the art's height from the width each card gets (the page is rebuilt on a resize).
        var screen = GetViewport().GetVisibleRect().Size;
        var gap = 8f * _s;
        var cardWidth = Mathf.Max(80f, (screen.X - 2f * Mathf.Round(40f * _s) - gap * (ships.Count - 1)) / ships.Count);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
        column.AddChild(row);
        foreach (var ship in ships) row.AddChild(ShipCard(ship, ship.Id == picked.Id, cardWidth));

        column.AddChild(ShipDetail(picked));

        var bar = BottomBar(column);
        var hint = HudKit.Label("←  →  pick  ·  Enter: choose  ·  Esc: back", 13 * _s, HudKit.Dim);
        hint.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.AddChild(hint);
        return root;
    }

    /// <summary>One card: the art (16:9, a plain panel with the name when it's missing), the name and the role tag.</summary>
    private Control ShipCard(ShipChoiceDef ship, bool selected, float width)
    {
        var (tagText, tagColour) = RoleTag(ship);
        var card = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            TooltipText = $"{ship.Name}: {ship.ClassLabel}",
        };
        var r = 6 * _s;
        var edge = selected ? HudKit.Accent : HudKit.Line;
        card.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.03f, 0.05f, 0.1f, 0.94f), edge, r, 0, 0, selected ? 2 : 1));
        card.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(0.05f, 0.08f, 0.16f, 0.96f), selected ? HudKit.Accent : HudKit.HoverEdge, r, 0, 0, 2));
        card.AddThemeStyleboxOverride("pressed", HudKit.Box(HudKit.Pressed, HudKit.Accent, r, 0, 0, 2));
        card.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var id = ship.Id;
        card.Pressed += () => PickShip(id);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 0);
        card.AddChild(box);

        var artHeight = Mathf.Round(width * 9f / 16f);
        box.AddChild(ShipArtRect(ship, new Vector2(0, artHeight), 22f));

        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(8 * _s));
        pad.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(8 * _s));
        pad.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(5 * _s));
        pad.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(6 * _s));
        box.AddChild(pad);
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * _s));
        pad.AddChild(text);
        var name = HudKit.Label(ship.Name.ToUpperInvariant(), 16 * _s, selected ? HudKit.Ink : new Color(HudKit.Ink, 0.85f), condensed: true, bold: true);
        name.ClipText = true;
        text.AddChild(name);
        var tag = HudKit.Label(tagText, 12 * _s, tagColour, condensed: true, bold: true);
        tag.ClipText = true;
        text.AddChild(tag);

        // The card's height = art + text; the box fills the button, so the button needs that as its minimum.
        card.CustomMinimumSize = new Vector2(0, artHeight + 46f * _s);
        return card;
    }

    /// <summary>The card art covering a box of this minimum size, or a plain panel with the ship's name in it.</summary>
    private Control ShipArtRect(ShipChoiceDef ship, Vector2 minSize, float fallbackFont)
    {
        if (ShipTexture(ship.Card) is { } texture)
            return new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = minSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipContents = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
        var panel = new PanelContainer { CustomMinimumSize = minSize, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.02f, 0.03f, 0.06f, 1f), new Color(HudKit.Line, 0.6f), 0));
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(centre);
        centre.AddChild(HudKit.Label(ship.Name.ToUpperInvariant(), fallbackFont * _s, new Color(HudKit.Dim, 0.6f), condensed: true, bold: true));
        return panel;
    }

    /// <summary>The selected ship: art on the left; class, stats, mission, landing line and the buttons on the right.</summary>
    private Control ShipDetail(ShipChoiceDef ship)
    {
        var panel = HudKit.PanelBox(HudKit.Panel, HudKit.Line, 8 * _s, 12 * _s, 12 * _s);
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(16 * _s));
        panel.AddChild(row);

        var art = ShipModelView(ship) ?? ShipArtRect(ship, new Vector2(120 * _s, 80 * _s), 40f);
        art.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        art.SizeFlagsStretchRatio = 1.3f;
        art.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        row.AddChild(art);

        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        row.AddChild(right);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(scroll);
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        scroll.AddChild(text);

        text.AddChild(HudKit.Label($"{ship.Name.ToUpperInvariant()} CLASS", 28 * _s, HudKit.Ink, condensed: true, bold: true));
        var classLine = HudKit.Label(ship.ClassLabel, 15 * _s, HudKit.Sky, condensed: true, bold: true);
        classLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(classLine);

        var tags = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tags.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        var (tagText, tagColour) = RoleTag(ship);
        tags.AddChild(Chip(tagText, tagColour));
        tags.AddChild(ship.Colonizes ? Chip("LANDS A COLONY", HudKit.Good) : Chip("NEVER LANDS", HudKit.Warn));
        text.AddChild(tags);

        var rows = new List<(string, string)>
        {
            ("LENGTH", $"{ship.LengthM:N0} m"),
            ("CREW", ship.Crew),
            ("SPEED", SpeedText(ship.Speed.ToFloat())),
        };
        if (ship.StartBonus.Count > 0)
            rows.Add(("BRINGS", string.Join(", ", ship.StartBonus.Select(kv => $"+{kv.Value:N0} {ResourceName(kv.Key)}"))));
        if (ship.StartUnits.Count > 0)
            rows.Add(("LANDS WITH", string.Join(", ", ship.StartUnits.GroupBy(u => u).Select(g => $"{(g.Count() > 1 ? $"{g.Count()}× " : "")}{UnitName(g.Key)}"))));
        text.AddChild(Rows(rows));

        var mission = HudKit.Label($"YOUR MISSION: {ship.MissionTitle.ToUpperInvariant()}", 17 * _s, HudKit.Accent, condensed: true, bold: true);
        mission.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(mission);
        var summary = HudKit.Label(ship.MissionSummary, 14 * _s, HudKit.Ink);
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(summary);
        var lands = HudKit.Label(ship.Colonizes
                ? "Lands: its drop ships set the colony down on a world you pick next, and the colony mission, First Light, begins."
                : "Never lands. Its missions play out on the helm's system display; next, pick the body it starts in orbit of.",
            14 * _s, ship.Colonizes ? HudKit.Good : HudKit.Warn);
        lands.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(lands);
        if (ship.Blurb.Length > 0 && ship.Blurb != "Never lands.")
        {
            var blurb = HudKit.Label(ship.Blurb, 13 * _s, HudKit.Dim);
            blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(blurb);
        }

        var buttons = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        right.AddChild(buttons);
        buttons.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Era), 120f));
        var choose = Action($"CHOOSE {ship.Name.ToUpperInvariant()}  ›", true, ChooseShip, 220f);
        Sfx.Tag(choose, "confirm");
        buttons.AddChild(choose);
        return panel;
    }

    /// <summary>The live viewer in the detail panel (freed with the page it is on).</summary>
    private ShipViewer? _shipViewer;

    /// <summary>The ship's live 3D model for the detail panel, or null when it has none (the still picture stays).
    /// A rebuild for the same ship (a window resize) keeps the angle it had been turned to.</summary>
    private Control? ShipModelView(ShipChoiceDef ship)
    {
        var old = _shipViewer != null && IsInstanceValid(_shipViewer) ? _shipViewer : null;
        _shipViewer = null;
        if (!ShipModels.Has(ship.Id)) return null;
        // A colony ship carries its landers on the berths (Dawn of Warp's own; the game picks one of three per player).
        var viewer = ShipViewer.Create(ship.Id, ship.Colonizes ? DropShipSpec.Lander : null, PlanetDriver.ColorOf(0));
        if (viewer == null) return null;
        if (old != null && old.ShipId == ship.Id)
        {
            viewer.Yaw = old.Yaw;
            viewer.Pitch = old.Pitch;
        }
        viewer.CustomMinimumSize = new Vector2(120 * _s, 80 * _s);
        _shipViewer = viewer;
        return viewer;
    }

    private Control Chip(string text, Color colour)
    {
        var chip = HudKit.PanelBox(new Color(colour, 0.14f), new Color(colour, 0.8f), 4 * _s, 8 * _s, 2 * _s);
        chip.AddChild(HudKit.Label(text, 12 * _s, colour, condensed: true, bold: true));
        return chip;
    }

    private static string SpeedText(float speed) =>
        Mathf.IsEqualApprox(speed, 1f) ? "the system's normal impulse speed"
        : speed > 1f ? $"×{speed:0.##}: arrives in {100f / speed:0}% of the normal time"
        : $"×{speed:0.##}: slower than most ({100f / Mathf.Max(0.05f, speed):0}% of the normal time)";

    private string ResourceName(string id) => Data?.Resources.FirstOrDefault(r => r.Id == id)?.Name.ToLowerInvariant() ?? id.Replace('_', ' ');

    private string UnitName(string id) => Data?.GroundVehicleNames.GetValueOrDefault(id, id.Replace('_', ' ')) ?? id.Replace('_', ' ');

    private void PickShip(string id)
    {
        if (_shipId == id) return;
        _shipId = id;
        _dirty = true; // rebuilt next frame (the pressed card goes with the page)
    }

    /// <summary>Left / Right on the ship page.</summary>
    private void StepShip(int by)
    {
        var ships = ShipChoices;
        if (ships.Count == 0) return;
        var i = Mathf.Max(0, ships.ToList().FindIndex(s => s.Id == _shipId));
        PickShip(ships[(i + by + ships.Count) % ships.Count].Id);
        Sfx.Ui("hover");
    }

    /// <summary>Keys on the ship page: Left / Right (repeat allowed) pick, Enter chooses. True when the key was used.</summary>
    private bool ShipKey(Key key, bool echo)
    {
        switch (key)
        {
            case Key.Left or Key.A:
                StepShip(-1);
                return true;
            case Key.Right or Key.D:
                StepShip(1);
                return true;
            case Key.Enter or Key.KpEnter when !echo:
                Sfx.Ui("confirm");
                ChooseShip();
                return true;
            case Key.F11 when !echo && OS.IsDebugBuild() && Input.IsKeyPressed(Key.Shift):
                ShipModels.DebugStandIn = !ShipModels.DebugStandIn; // debug: a stand-in model for ships without a glb
                _dirty = true;
                return true;
        }
        return false;
    }

    private void ChooseShip()
    {
        if (PickedShip is not { } ship) return;
        // A colony ship is fitted out before it leaves (docs/fit-out-contract.md); every other ship, and any ship when
        // the data has no fit-out rules, goes straight to the system as before.
        ShowPage(FitOutApplies(ship) ? Page.FitOut : Page.System);
    }
}
