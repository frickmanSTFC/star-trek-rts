using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FinalFrontier;

/// <summary>One line of the SATELLITES list: Key "sat:&lt;id&gt;" (the colony's) or "sys:&lt;body&gt;" (one at another body).</summary>
public sealed record SatListEntry(string Key, string Name, string Status, Color Colour);

/// <summary>One line of a satellite's findings log (newest first in the card). Tile -1 = nothing to locate.</summary>
public sealed record SatFindingLine(string Time, string Kind, string Text, Color Colour, bool First, int Tile);

/// <summary>What the satellite card shows (built by the driver, PlanetDriver.Satellites.cs). Colony = one of the colony
/// world's survey satellites (FOLLOW, RETASK, photos, findings); otherwise a satellite at another body (its live rows).</summary>
public sealed record SatCardView(string Key, string Title, string Subtitle, IReadOnlyList<HelmRow> Rows, bool Colony,
    bool Following, bool CanFollow, bool Retasking, string? RetaskProblem, IReadOnlyList<SatFindingLine> Findings, int FindingsTotal);

/// <summary>
/// The helm's SATELLITES window (docs/satellite-contract.md "Helm"; render-only): a list of our satellites (the colony
/// world's and those left at other bodies) and, for the picked one, its card: call sign, rows (model, band, status,
/// sweep, coverage, tiles surveyed, data downlinked…), FOLLOW / RETASK, the picture modes, a strip of its last pictures
/// (click = enlarge, with the stamp) and its findings log (click a line = show it on the globe; again = go down to it).
/// Drawn in the helm's look (black panel, peach left border, condensed caps, HelmPill buttons) at the top right of the
/// free viewscreen, left of the data panel. The driver feeds it every frame (<see cref="Refresh"/>); it rebuilds only
/// the parts whose content changed.
/// </summary>
public partial class SatelliteCard : Control
{
    public event Action<string>? Picked;
    public event Action? FollowToggled;
    public event Action? RetaskToggled;
    public event Action<int>? FindingPicked;
    public event Action? Closed;

    /// <summary>How the pictures are shown (the mode pills).</summary>
    public PhotoMode Mode { get; private set; }

    private static readonly Color GreyText = Color.FromHtml("#8d909a");

    private float _s = -1;
    private FontVariation _regular = new();
    private FontVariation _bold = new();
    private readonly ShaderMaterial[] _modeMaterials = new ShaderMaterial[3];

    private PanelContainer? _frame;
    private Label? _head;
    private VBoxContainer? _list;
    private VBoxContainer? _card;
    private Label? _cardTitle;
    private Label? _cardSubtitle;
    private GridContainer? _rows;
    private HBoxContainer? _buttons;
    private HFlowContainer? _strip;
    private Label? _stripCaption;
    private Label? _logCaption;
    private ScrollContainer? _logScroll;
    private VBoxContainer? _log;

    private PanelContainer? _enlarge;
    private TextureRect? _bigPicture;
    private Label? _bigTitle, _bigStamp, _bigCaption, _bigMode;
    private int _bigIndex = -1;

    private string _listSig = "", _rowsSig = "", _buttonsSig = "", _stripSig = "", _logSig = "";
    private readonly List<(Label Label, Label Value, ColorRect? Bar, ColorRect? BarBack)> _rowNodes = new();
    private IReadOnlyList<SatellitePhoto> _photos = Array.Empty<SatellitePhoto>();
    private string _photoTitle = "";

    public bool IsOverUi(Vector2 screenPos) =>
        Visible && ((_frame != null && _frame.GetGlobalRect().HasPoint(screenPos)) || (_enlarge != null && _enlarge.Visible && _enlarge.GetGlobalRect().HasPoint(screenPos)));

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
        string[] names = { "Antonio", "Bahnschrift", "Arial Narrow", "Roboto Condensed", "Liberation Sans Narrow", "DejaVu Sans Condensed", "Arial" };
        _regular = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 500, FontStretch = 75 } };
        _bold = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 700, FontStretch = 75 } };
        var shader = new Shader { Code = SatellitePhotos.PhotoShader };
        for (var i = 0; i < 3; i++)
        {
            _modeMaterials[i] = new ShaderMaterial { Shader = shader };
            _modeMaterials[i].SetShaderParameter("mode", i);
        }
    }

    /// <summary>Once a frame. Show = the window is wanted (in orbit, panel open); area = the free viewscreen (viewport
    /// pixels); collapsed = only the list (a deployment card is open). Photos = the picked satellite's pictures.</summary>
    public void Refresh(bool show, Rect2 area, float scale, IReadOnlyList<SatListEntry> list, string? selected, SatCardView? card,
        IReadOnlyList<SatellitePhoto> photos, bool collapsed)
    {
        Visible = show;
        if (!show)
        {
            if (_enlarge != null) _enlarge.Visible = false;
            return;
        }
        if (!Mathf.IsEqualApprox(scale, _s) || _frame == null) Build(scale);
        if (_head != null) _head.Text = $"SATELLITES · {list.Count}";

        UpdateList(list, selected);
        var showCard = card != null && !collapsed;
        _card!.Visible = showCard;
        _photos = card?.Colony == true ? photos : Array.Empty<SatellitePhoto>();
        _photoTitle = card?.Title ?? "";
        if (showCard)
        {
            _cardTitle!.Text = card!.Title.ToUpperInvariant();
            _cardSubtitle!.Text = card.Subtitle.ToUpperInvariant();
            UpdateRows(card.Rows);
            UpdateButtons(card);
            UpdateStrip(card);
            UpdateLog(card);
        }
        if (!showCard || _photos.Count == 0) _bigIndex = -1;
        Place(area);
        UpdateEnlarged(area);
    }

    // ================================================================== content

    private void UpdateList(IReadOnlyList<SatListEntry> list, string? selected)
    {
        var sig = new StringBuilder(selected ?? "-");
        foreach (var e in list) sig.Append('|').Append(e.Key).Append(e.Name).Append(e.Status).Append(e.Colour.ToHtml());
        var s = sig.ToString();
        if (s == _listSig) return;
        _listSig = s;
        Clear(_list!);
        if (list.Count == 0)
        {
            _list!.AddChild(Text("No satellites yet: SATELLITE on the strip puts one up over the band you click.", F(12), HelmScreen.Ink with { A = 0.7f }, wrap: true));
            return;
        }
        foreach (var e in list)
        {
            var row = new Button { Flat = true, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.None, TooltipText = e.Status };
            row.Text = $"{e.Name.ToUpperInvariant()}   {e.Status}";
            row.AddThemeFontOverride("font", e.Key == selected ? _bold : _regular);
            row.AddThemeFontSizeOverride("font_size", F(13));
            var colour = e.Key == selected ? HelmScreen.Peach : e.Colour;
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                row.AddThemeColorOverride(state, state == "font_hover_color" ? colour.Lightened(0.25f) : colour);
            var bg = new StyleBoxFlat { BgColor = e.Key == selected ? new Color(HelmScreen.Peach, 0.16f) : new Color(1, 1, 1, 0.03f), ContentMarginLeft = P(6), ContentMarginTop = P(1), ContentMarginBottom = P(1) };
            row.AddThemeStyleboxOverride("normal", bg);
            row.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color(HelmScreen.Sky, 0.18f), ContentMarginLeft = P(6), ContentMarginTop = P(1), ContentMarginBottom = P(1) });
            row.AddThemeStyleboxOverride("pressed", bg);
            var key = e.Key;
            row.Pressed += () => Picked?.Invoke(key);
            _list!.AddChild(row);
        }
    }

    private void UpdateRows(IReadOnlyList<HelmRow> rows)
    {
        var sig = new StringBuilder();
        foreach (var r in rows) sig.Append(r.Label).Append('|').Append(r.Bar.HasValue).Append(';');
        var s = sig.ToString();
        if (s != _rowsSig)
        {
            _rowsSig = s;
            Clear(_rows!);
            _rowNodes.Clear();
            foreach (var r in rows)
            {
                var label = Text(r.Label, F(12), HelmScreen.Sky, bold: true);
                var cell = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                cell.AddThemeConstantOverride("separation", P(1));
                var value = Text("", F(13), HelmScreen.Ink, wrap: true);
                cell.AddChild(value);
                ColorRect? bar = null, back = null;
                if (r.Bar.HasValue)
                {
                    back = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, P(4)), MouseFilter = MouseFilterEnum.Ignore };
                    bar = new ColorRect { Color = HelmScreen.Cyan, MouseFilter = MouseFilterEnum.Ignore };
                    back.AddChild(bar);
                    cell.AddChild(back);
                }
                _rows!.AddChild(label);
                _rows.AddChild(cell);
                _rowNodes.Add((label, value, bar, back));
            }
        }
        for (var i = 0; i < rows.Count && i < _rowNodes.Count; i++)
        {
            var (_, value, bar, back) = _rowNodes[i];
            value.Text = rows[i].Value;
            value.AddThemeColorOverride("font_color", rows[i].ValueColour ?? HelmScreen.Ink);
            if (bar != null && back != null)
            {
                var f = Mathf.Clamp(rows[i].Bar ?? 0f, 0f, 1f);
                bar.Size = new Vector2(back.Size.X * f, back.Size.Y);
                bar.Color = rows[i].ValueColour ?? HelmScreen.Cyan;
            }
        }
    }

    private void UpdateButtons(SatCardView card)
    {
        var sig = $"{card.Key}{card.Colony}{card.Following}{card.CanFollow}{card.Retasking}{card.RetaskProblem}{Mode}";
        if (sig == _buttonsSig) return;
        _buttonsSig = sig;
        Clear(_buttons!);
        if (!card.Colony)
        {
            _buttons!.AddChild(Text("Live data from orbit: no pictures from here.", F(12), HelmScreen.Ink with { A = 0.7f }));
            return;
        }
        _buttons!.AddChild(Pill(card.Following ? "FOLLOWING" : "FOLLOW", card.CanFollow ? "Keep it facing the camera (a drag stops it)" : "Only over the colony's world",
            card.Following ? HelmScreen.Cyan : HelmScreen.Sky, !card.CanFollow, P(10), 0, () => FollowToggled?.Invoke()));
        _buttons.AddChild(Pill(card.Retasking ? "PICK BAND…" : "RETASK", card.RetaskProblem is { } why ? $"Can't retask now: {why}" : "Move it over another band: click a latitude on the globe",
            card.Retasking ? HelmScreen.Peach : HelmScreen.Lavender, card.RetaskProblem != null && !card.Retasking, 0, 0, () => RetaskToggled?.Invoke()));
        foreach (var (mode, label, tip) in new[] { (PhotoMode.Visual, "VIS", "Pictures in visible light"), (PhotoMode.Infrared, "IR", "Pictures as infrared (warm = bright)"),
                     (PhotoMode.Terrain, "TERRAIN", "Pictures as terrain relief (contours)") })
        {
            var m = mode;
            _buttons.AddChild(Pill(label, tip, Mode == mode ? HelmScreen.Peach : GreyText, false, 0, mode == PhotoMode.Terrain ? P(10) : 0, () =>
            {
                Mode = m;
                _buttonsSig = "";
                _stripSig = "";
            }));
        }
    }

    private void UpdateStrip(SatCardView card)
    {
        var sig = $"{card.Key}{Mode}{_photos.Count}{(_photos.Count > 0 ? _photos[^1].Tick : 0)}";
        if (sig == _stripSig) return;
        _stripSig = sig;
        Clear(_strip!);
        _stripCaption!.Visible = card.Colony;
        _strip!.Visible = card.Colony;
        if (!card.Colony) return;
        _stripCaption.Text = _photos.Count == 0 ? "PICTURES · the first one comes with the next finding or in a few seconds" : $"PICTURES · {_photos.Count} · click to enlarge";
        for (var i = _photos.Count - 1; i >= 0; i--)
        {
            var index = i;
            var photo = _photos[i];
            var holder = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = $"{photo.Title}\n{photo.Stamp}", MouseDefaultCursorShape = CursorShape.PointingHand };
            holder.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Colors.Black,
                BorderColor = photo.Finding ? HelmScreen.Peach : new Color(HelmScreen.Sky, 0.5f),
                BorderWidthBottom = P(2), BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            var pic = new TextureRect
            {
                Texture = photo.Texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(P(62), P(39)),
                Material = _modeMaterials[(int)Mode],
                MouseFilter = MouseFilterEnum.Ignore,
            };
            holder.AddChild(pic);
            holder.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    _bigIndex = _bigIndex == index ? -1 : index;
                    Sfx.Ui("open");
                }
            };
            _strip.AddChild(holder);
        }
    }

    private void UpdateLog(SatCardView card)
    {
        var sig = $"{card.Key}{card.FindingsTotal}{card.Findings.Count}";
        if (sig == _logSig) return;
        _logSig = sig;
        Clear(_log!);
        _logCaption!.Visible = card.Colony;
        _logScroll!.Visible = card.Colony;
        if (!card.Colony) return;
        _logCaption.Text = card.FindingsTotal == 0 ? "FINDINGS · none yet" : card.FindingsTotal > card.Findings.Count
            ? $"FINDINGS · {card.FindingsTotal} (last {card.Findings.Count}) · click = show on the globe"
            : $"FINDINGS · {card.FindingsTotal} · click = show on the globe";
        foreach (var f in card.Findings)
        {
            var line = new Button { Flat = true, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.None,
                Text = $"{f.Time}  {f.Kind}  {f.Text}{(f.First ? "  ★" : "")}",
                TooltipText = f.Tile >= 0 ? "Show it on the globe (click again to go down to it once you have landed)" + (f.First ? "\n★ the first of its kind" : "") : "" };
            line.AddThemeFontOverride("font", f.First ? _bold : _regular);
            line.AddThemeFontSizeOverride("font_size", F(12));
            foreach (var state in new[] { "font_color", "font_pressed_color", "font_focus_color" }) line.AddThemeColorOverride(state, f.Colour);
            line.AddThemeColorOverride("font_hover_color", f.Colour.Lightened(0.3f));
            var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ContentMarginLeft = P(4), ContentMarginTop = 0, ContentMarginBottom = 0 };
            line.AddThemeStyleboxOverride("normal", flat);
            line.AddThemeStyleboxOverride("pressed", flat);
            line.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color(HelmScreen.Sky, 0.15f), ContentMarginLeft = P(4) });
            var tile = f.Tile;
            if (tile >= 0) line.Pressed += () => FindingPicked?.Invoke(tile);
            else line.Disabled = true;
            _log!.AddChild(line);
        }
    }

    // ================================================================== enlarged picture

    private void UpdateEnlarged(Rect2 area)
    {
        if (_enlarge == null) return;
        var on = _bigIndex >= 0 && _bigIndex < _photos.Count && _card!.Visible;
        _enlarge.Visible = on;
        if (!on) return;
        var photo = _photos[_bigIndex];
        _bigPicture!.Texture = photo.Texture;
        _bigPicture.Material = _modeMaterials[(int)Mode];
        _bigTitle!.Text = photo.Title.ToUpperInvariant();
        _bigStamp!.Text = photo.Stamp;
        _bigCaption!.Text = $"{_bigIndex + 1} / {_photos.Count}" + (photo.FromGlobe ? " · from the orbital map" : "");
        _bigMode!.Text = Mode switch { PhotoMode.Infrared => "IR", PhotoMode.Terrain => "TERRAIN", _ => "VIS" };
        _enlarge.Size = Vector2.Zero;
        var size = _enlarge.Size;
        var at = area.Position + (area.Size - size) * 0.5f - GlobalPosition;
        _enlarge.Position = new Vector2(Mathf.Max(area.Position.X - GlobalPosition.X, at.X), Mathf.Max(area.Position.Y - GlobalPosition.Y, at.Y));
    }

    private void Step(int by)
    {
        if (_photos.Count == 0) return;
        _bigIndex = (_bigIndex + by + _photos.Count) % _photos.Count;
    }

    // ================================================================== layout

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(int v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private void Build(float scale)
    {
        _s = scale;
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _listSig = _rowsSig = _buttonsSig = _stripSig = _logSig = "";
        _rowNodes.Clear();
        var spacing = Mathf.RoundToInt(0.6f * _s);
        _regular.SpacingGlyph = spacing;
        _bold.SpacingGlyph = spacing;
        Theme = new Theme { DefaultFont = _regular, DefaultFontSize = F(13) };
        var width = P(300);

        _frame = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        _frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.85f), BorderColor = HelmScreen.Cyan, BorderWidthLeft = P(4),
            ContentMarginLeft = P(8), ContentMarginRight = P(6), ContentMarginTop = P(6), ContentMarginBottom = P(6),
        });
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", P(4));
        _frame.AddChild(box);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(4));
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = HelmScreen.Cyan, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) });
        _head = Text("SATELLITES", F(14), Colors.Black, bold: true);
        bar.AddChild(_head);
        head.AddChild(bar);
        head.AddChild(Pill("×", "Close (SATS on the rail opens it again)", HelmScreen.Rust, false, 0, P(10), () => Closed?.Invoke()));
        box.AddChild(head);

        _list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", P(1));
        box.AddChild(_list);

        _card = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _card.AddThemeConstantOverride("separation", P(4));
        box.AddChild(_card);
        var titleBar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleBar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = HelmScreen.Peach, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) });
        var titleBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleBox.AddThemeConstantOverride("separation", 0);
        _cardTitle = Text("", F(16), Colors.Black, bold: true);
        _cardSubtitle = Text("", F(11), new Color(0, 0, 0, 0.75f));
        titleBox.AddChild(_cardTitle);
        titleBox.AddChild(_cardSubtitle);
        titleBar.AddChild(titleBox);
        _card.AddChild(titleBar);
        _rows = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("h_separation", P(8));
        _rows.AddThemeConstantOverride("v_separation", P(2));
        _card.AddChild(_rows);
        _buttons = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _buttons.AddThemeConstantOverride("separation", P(3));
        _card.AddChild(_buttons);
        _stripCaption = Text("PICTURES", F(11), HelmScreen.Sky, bold: true);
        _card.AddChild(_stripCaption);
        _strip = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        _strip.AddThemeConstantOverride("h_separation", P(3));
        _strip.AddThemeConstantOverride("v_separation", P(3));
        _card.AddChild(_strip);
        _logCaption = Text("FINDINGS", F(11), HelmScreen.Sky, bold: true);
        _card.AddChild(_logCaption);
        _logScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, P(120)) };
        _log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _log.AddThemeConstantOverride("separation", 0);
        _logScroll.AddChild(_log);
        _card.AddChild(_logScroll);
        AddChild(_frame);

        // The enlarged picture, over the middle of the viewscreen.
        _enlarge = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _enlarge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.9f), BorderColor = HelmScreen.Peach, BorderWidthLeft = P(4),
            ContentMarginLeft = P(8), ContentMarginRight = P(6), ContentMarginTop = P(6), ContentMarginBottom = P(6),
        });
        var big = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        big.AddThemeConstantOverride("separation", P(4));
        var bigHead = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bigHead.AddThemeConstantOverride("separation", P(4));
        var bigBar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bigBar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = HelmScreen.Peach, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) });
        _bigCaption = Text("", F(13), Colors.Black, bold: true);
        bigBar.AddChild(_bigCaption);
        bigHead.AddChild(bigBar);
        bigHead.AddChild(Pill("‹", "Older picture", HelmScreen.Sky, false, 0, 0, () => Step(-1)));
        bigHead.AddChild(Pill("›", "Newer picture", HelmScreen.Sky, false, 0, 0, () => Step(1)));
        bigHead.AddChild(Pill("×", "Close the picture", HelmScreen.Rust, false, 0, P(10), () => _bigIndex = -1));
        big.AddChild(bigHead);
        var video = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(560), P(350)), ClipContents = true };
        _bigPicture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        _bigPicture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        video.AddChild(_bigPicture);
        _bigTitle = Overlay(F(13), HelmScreen.Ink);
        _bigTitle.Position = new Vector2(P(8), P(5));
        video.AddChild(_bigTitle);
        _bigMode = Overlay(F(13), HelmScreen.Peach);
        _bigMode.SetAnchorsPreset(LayoutPreset.TopRight);
        _bigMode.GrowHorizontal = GrowDirection.Begin;
        _bigMode.OffsetLeft = -P(8);
        _bigMode.OffsetRight = -P(8);
        _bigMode.OffsetTop = P(5);
        video.AddChild(_bigMode);
        var strip = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        strip.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        strip.OffsetTop = -P(22);
        video.AddChild(strip);
        _bigStamp = Overlay(F(12), HelmScreen.Cyan);
        _bigStamp.ClipText = true;
        _bigStamp.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _bigStamp.OffsetTop = -P(20);
        _bigStamp.OffsetLeft = P(8);
        _bigStamp.OffsetRight = -P(8);
        video.AddChild(_bigStamp);
        big.AddChild(video);
        _enlarge.AddChild(big);
        AddChild(_enlarge);
    }

    /// <summary>Top right of the free viewscreen (left of the open data panel), no taller than it.</summary>
    private void Place(Rect2 area)
    {
        if (_frame == null || _logScroll == null) return;
        // The findings log takes what height is left (60..220 at 720p), so the window fits the viewscreen. Measured with
        // last frame's log height and only changed by more than a few pixels, so it settles instead of jittering.
        _frame.Size = Vector2.Zero;
        var height = _logScroll.CustomMinimumSize.Y;
        var spare = area.Size.Y - P(16) - _frame.Size.Y;
        var wanted = Mathf.Clamp(height + spare, P(60), P(220));
        if (Mathf.Abs(wanted - height) > P(3)) _logScroll.CustomMinimumSize = new Vector2(0, wanted);
        var size = _frame.Size;
        var at = new Vector2(area.End.X - P(10) - size.X, area.Position.Y + P(8)) - GlobalPosition;
        _frame.Position = new Vector2(Mathf.Max(area.Position.X - GlobalPosition.X, at.X), at.Y);
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Label Text(string text, int size, Color colour, bool bold = false, bool wrap = false)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(P(150), 0);
        }
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        if (bold) label.AddThemeFontOverride("font", _bold);
        return label;
    }

    private Label Overlay(int size, Color colour)
    {
        var label = Text("", size, colour, bold: true);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private HelmPill Pill(string text, string tip, Color colour, bool greyed, int roundLeft, int roundRight, Action onPress)
    {
        var pill = new HelmPill { Id = text, TooltipText = tip };
        pill.AddThemeFontOverride("font", _bold);
        pill.AddThemeFontSizeOverride("font_size", F(12));
        pill.Set(text, colour, greyed, roundLeft, roundRight, P(7), 0);
        pill.Pressed += onPress;
        return pill;
    }
}
