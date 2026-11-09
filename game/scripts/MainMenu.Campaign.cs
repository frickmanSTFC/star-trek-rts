using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

// The campaign's first two screens (docs/menu-contract.md A): the seven great powers (only the Federation can be
// picked) and the eras (only Dawn of Warp is open; every eras.json era shows after it, locked).
public partial class MainMenu
{
    /// <summary>The great powers in tools/faction_meta.py FACTIONS order, with a short name and a card colour.</summary>
    private static readonly (string Id, string Short, Color Colour)[] GreatPowers =
    {
        ("federation", "FEDERATION", new Color(0.45f, 0.7f, 1f)),
        ("klingon", "KLINGON", new Color(0.86f, 0.3f, 0.26f)),
        ("cardassian", "CARDASSIAN", new Color(0.82f, 0.6f, 0.3f)),
        ("romulan", "ROMULAN", new Color(0.36f, 0.78f, 0.5f)),
        ("dominion", "DOMINION", new Color(0.7f, 0.48f, 0.9f)),
        ("ferengi", "FERENGI", new Color(0.95f, 0.82f, 0.32f)),
        ("borg", "BORG", new Color(0.5f, 0.82f, 0.62f)),
    };

    /// <summary>The menu's own words for Dawn of Warp (eras.json's notes are design notes).</summary>
    private const string DawnBlurb = "The first years after First Contact. A small colony expedition reaches a nearby star: " +
                                     "pick a world, land, survey and build with what you brought.";

    /// <summary>One faction's record from its data file (factions/&lt;id&gt;.json; GameData.Sources has every file's text,
    /// from the JSON folders or the release pack).</summary>
    private JsonElement? FactionRecord(string id) => Record($"factions/{id}.json", "factions", id);

    private JsonElement? EraRecord(string id) => Record("eras/eras.json", "eras", id);

    private JsonElement? Record(string path, string table, string id)
    {
        if (Data is not { } data || !data.Sources.TryGetValue(path, out var text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty(table, out var list) || list.ValueKind != JsonValueKind.Array) return null;
            foreach (var r in list.EnumerateArray())
                if (r.TryGetProperty("id", out var rid) && rid.GetString() == id) return r.Clone();
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Main menu: {path}: {e.Message}");
        }
        return null;
    }

    private static string Field(JsonElement? record, string name, string sub = "")
    {
        if (record is not { } r || !r.TryGetProperty(name, out var v)) return "";
        if (sub.Length > 0) return v.ValueKind == JsonValueKind.Object && v.TryGetProperty(sub, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() ?? "" : "";
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    // ---------------------------------------------------------------- factions

    private Control BuildFactionPage()
    {
        var (root, column) = NewPage(0.6f);
        Header(column, "CAMPAIGN", "CHOOSE YOUR POWER", "Seven great powers. The Federation's campaign comes first; the others follow later.");
        column.AddChild(Spacer(false, 10 * _s));

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * _s));
        column.AddChild(row);
        var data = Data;
        foreach (var (id, shortName, colour) in GreatPowers)
            row.AddChild(FactionCard(data, id, shortName, colour));

        var bar = BottomBar(column);
        bar.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Title)));
        bar.AddChild(Spacer());
        bar.AddChild(HudKit.Label("Esc: back", 13 * _s, HudKit.Dim));
        return root;
    }

    private Control FactionCard(GameData? data, string id, string shortName, Color colour)
    {
        var open = id == CampaignFaction;
        var record = FactionRecord(id);
        var fullName = data?.Factions.FirstOrDefault(f => f.Id == id)?.Name ?? Field(record, "name");

        var card = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(120 * _s, 300 * _s),
            Disabled = !open,
            TooltipText = open ? "Start the Federation campaign" : $"{fullName}: not playable yet",
            ClipContents = true,
        };
        var r = 8 * _s;
        card.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.03f, 0.05f, 0.1f, 0.94f), open ? new Color(colour, 0.7f) : HudKit.Line, r, 0, 0, open ? 2 : 1));
        card.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(0.05f, 0.08f, 0.16f, 0.96f), HudKit.Accent, r, 0, 0, 2));
        card.AddThemeStyleboxOverride("pressed", HudKit.Box(HudKit.Pressed, HudKit.Accent, r, 0, 0, 2));
        card.AddThemeStyleboxOverride("disabled", HudKit.Box(new Color(0.02f, 0.03f, 0.06f, 0.92f), new Color(0.09f, 0.11f, 0.16f), r, 0, 0, 1));
        card.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (open) card.Pressed += () => ShowPage(Page.Era);

        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_right" }) pad.AddThemeConstantOverride(side, Mathf.RoundToInt(12 * _s));
        pad.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(14 * _s));
        pad.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(12 * _s));
        card.AddChild(pad);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        pad.AddChild(box);

        var shown = open ? colour : colour.Darkened(0.55f);
        box.AddChild(new FactionEmblem { Colour = shown, Letter = shortName[..1], CustomMinimumSize = new Vector2(0, 54 * _s), Dim = !open });
        var title = HudKit.Label(shortName, 21 * _s, open ? HudKit.Ink : HudKit.Dim, condensed: true, bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);
        var full = HudKit.Label(fullName, 12 * _s, open ? HudKit.Sky : new Color(HudKit.Dim, 0.7f), condensed: true);
        full.HorizontalAlignment = HorizontalAlignment.Center;
        full.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(full);
        box.AddChild(new ColorRect { Color = new Color(shown, open ? 0.6f : 0.25f), CustomMinimumSize = new Vector2(0, 2 * _s), MouseFilter = Control.MouseFilterEnum.Ignore });

        var style = Field(record, "playstyle");
        if (style.Length > 0)
        {
            var l = HudKit.Label(style.ToUpperInvariant(), 12 * _s, open ? HudKit.Accent : new Color(HudKit.Dim, 0.6f), condensed: true, bold: true);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(l);
        }
        var desc = Field(record, "signature", "desc");
        if (desc.Length > 0)
        {
            var l = HudKit.Label(desc, 12 * _s, open ? HudKit.Ink : new Color(HudKit.Dim, 0.55f));
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            l.ClipText = true;
            box.AddChild(l);
        }
        else box.AddChild(Spacer());

        var status = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        status.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        if (!open) status.AddChild(new LockIcon { Colour = HudKit.Dim, CustomMinimumSize = new Vector2(16 * _s, 16 * _s), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        status.AddChild(HudKit.Label(open ? "PLAY  ›" : "NOT PLAYABLE YET", 14 * _s, open ? HudKit.Good : HudKit.Dim, condensed: true, bold: true));
        box.AddChild(status);
        return card;
    }

    // ---------------------------------------------------------------- eras

    private Control BuildEraPage()
    {
        var (root, column) = NewPage(0.62f);
        Header(column, "FEDERATION CAMPAIGN", "CHOOSE AN ERA", "The campaign begins at the dawn of warp. Later eras open as it grows.");
        column.AddChild(Spacer(false, 6 * _s));

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        scroll.AddChild(list);

        foreach (var era in MenuEras()) list.AddChild(EraRow(era));

        var bar = BottomBar(column);
        bar.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Faction)));
        bar.AddChild(Spacer());
        bar.AddChild(HudKit.Label("Esc: back", 13 * _s, HudKit.Dim));
        return root;
    }

    private sealed record MenuEra(string Id, string Name, int From, int To, int TechCap, string Notes, IReadOnlyList<string> Playable, bool Optional);

    /// <summary>Dawn of Warp first (from eras.json; a menu-only stand-in if the data doesn't have it), then every other
    /// era by its first year.</summary>
    private List<MenuEra> MenuEras()
    {
        var list = new List<MenuEra>();
        var eras = Data?.Eras ?? (IReadOnlyList<EraDef>)System.Array.Empty<EraDef>();
        var dawn = eras.FirstOrDefault(e => e.Id == CampaignEra);
        list.Add(dawn != null
            ? new MenuEra(dawn.Id, dawn.Name, dawn.YearFrom, dawn.YearTo, dawn.TechCapTier, DawnBlurb, dawn.Playable, false)
            : new MenuEra(CampaignEra, "Dawn of Warp", 2063, 2150, 0, DawnBlurb, new[] { CampaignFaction }, false));
        foreach (var e in eras.Where(e => e.Id != CampaignEra).OrderBy(e => e.YearFrom))
            list.Add(new MenuEra(e.Id, e.Name, e.YearFrom, e.YearTo, e.TechCapTier, Field(EraRecord(e.Id), "notes"), e.Playable, e.Optional));
        return list;
    }

    private Control EraRow(MenuEra era)
    {
        var open = era.Id == CampaignEra;
        var b = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, (open ? 92 : 80) * _s),
            Disabled = !open,
            TooltipText = open ? "Begin: choose a star system" : "Locked: later in the campaign",
            ClipContents = true,
        };
        var r = 6 * _s;
        b.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.12f, 0.087f, 0.027f, 0.92f), HudKit.Accent, r, 0, 0, 2));
        b.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(0.2f, 0.14f, 0.04f, 0.95f), HudKit.Accent, r, 0, 0, 2));
        b.AddThemeStyleboxOverride("pressed", HudKit.Box(HudKit.Pressed, HudKit.Accent, r, 0, 0, 2));
        b.AddThemeStyleboxOverride("disabled", HudKit.Box(new Color(0.03f, 0.04f, 0.08f, 0.9f), new Color(0.11f, 0.14f, 0.2f), r, 0, 0, 1));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (open) b.Pressed += () => ShowPage(Page.System);

        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        pad.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(16 * _s));
        pad.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(16 * _s));
        pad.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(8 * _s));
        pad.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(8 * _s));
        b.AddChild(pad);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(18 * _s));
        pad.AddChild(row);

        var years = HudKit.Label($"{era.From}–{era.To}", (open ? 24 : 18) * _s, open ? HudKit.Accent : HudKit.Dim, condensed: true, bold: true);
        years.CustomMinimumSize = new Vector2(130 * _s, 0);
        years.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(years);

        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * _s));
        row.AddChild(text);
        var name = open ? $"{era.Name.ToUpperInvariant()}  ·  PRE-WARP" : era.Name.ToUpperInvariant() + (era.Optional ? "  ·  ALTERNATE TIMELINE" : "");
        text.AddChild(HudKit.Label(name, (open ? 22 : 17) * _s, open ? HudKit.Ink : HudKit.Dim, condensed: true, bold: true));
        var notes = era.Notes;
        if (notes.Length > 0)
        {
            var l = HudKit.Label(notes, 13 * _s, open ? HudKit.Ink : new Color(HudKit.Dim, 0.7f));
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.MaxLinesVisible = open ? 2 : 1;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.AddChild(l);
        }
        if (!open && era.Playable.Count > 0)
        {
            var who = era.Playable.Contains(CampaignFaction) ? "Federation playable" : "Federation not playable here";
            text.AddChild(HudKit.Label($"{who} · tech tier {era.TechCap}", 12 * _s, new Color(HudKit.Dim, 0.6f), condensed: true));
        }

        var status = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        status.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * _s));
        if (!open) status.AddChild(new LockIcon { Colour = HudKit.Dim, CustomMinimumSize = new Vector2(18 * _s, 18 * _s), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        status.AddChild(HudKit.Label(open ? "BEGIN  ›" : "LOCKED", (open ? 18 : 14) * _s, open ? HudKit.Good : HudKit.Dim, condensed: true, bold: true));
        row.AddChild(status);
        return b;
    }
}

/// <summary>A faction card's emblem: a ring with the faction's initial (placeholder art, not the canon insignia).</summary>
public partial class FactionEmblem : Control
{
    public Color Colour { get; set; } = HudKit.Accent;
    public string Letter { get; set; } = "";
    public bool Dim { get; set; }

    public FactionEmblem()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = Size / 2f;
        var radius = Mathf.Min(Size.X, Size.Y) * 0.46f;
        DrawCircle(c, radius, new Color(Colour, Dim ? 0.08f : 0.16f));
        DrawArc(c, radius, 0f, Mathf.Tau, 48, new Color(Colour, Dim ? 0.4f : 0.95f), Mathf.Max(1.5f, radius * 0.08f), true);
        var font = HudKit.Condensed(true);
        var size = Mathf.RoundToInt(radius * 1.1f);
        var text = font.GetStringSize(Letter, HorizontalAlignment.Left, -1, size);
        DrawString(font, new Vector2(c.X - text.X / 2f, c.Y + font.GetAscent(size) * 0.38f), Letter, HorizontalAlignment.Left, -1, size, new Color(Colour, Dim ? 0.5f : 1f));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }
}
