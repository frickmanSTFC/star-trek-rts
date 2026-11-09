using Godot;

namespace FinalFrontier;

/// <summary>
/// The in-game pause card (docs/menu-contract.md A): Esc when nothing else wants it (PlanetDriver) opens it; it pauses the
/// scene tree (sim, view, animations) and offers RESUME, SETTINGS (the shared SettingsPanel), MAIN MENU and QUIT. Runs
/// with ProcessMode Always so it still gets input while the tree is paused; Esc closes the settings, then the card.
/// Lives on its own CanvasLayer above everything else (PlanetDriver puts it there).
/// </summary>
public partial class PauseMenu : Control
{
    private Control? _content;
    private bool _settings;

    /// <summary>A line under PAUSED (world, mode).</summary>
    public string Subtitle { get; set; } = "";

    public bool IsOpen => Visible;

    public PauseMenu()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop; // the dimmed backdrop eats clicks while open
        Visible = false;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Open()
    {
        if (Visible) return;
        Visible = true;
        _settings = false;
        GetTree().Paused = true;
        Sfx.Ui("open"); // the UI players run while paused (Sfx host: ProcessMode Always)
        Build();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Sfx.Ui("close");
        if (IsInsideTree()) GetTree().Paused = false;
        // Freed a frame later: RESUME's own pressed signal is what got us here.
        Callable.From(() =>
        {
            if (IsInstanceValid(this) && !Visible) ClearContent();
        }).CallDeferred();
    }

    public override void _ExitTree()
    {
        // Never leave the tree paused behind us (a scene change or reload while the card is open).
        if (Visible && GetTree() is { } tree) tree.Paused = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (_settings)
            {
                Sfx.Ui("back");
                ShowButtons();
            }
            else Close();
        }
        // While open, no key reaches the game (it is paused anyway; this keeps hotkeys out of other Always nodes).
        if (@event is InputEventKey) GetViewport().SetInputAsHandled();
    }

    private void ClearContent()
    {
        if (_content == null) return;
        RemoveChild(_content);
        _content.QueueFree();
        _content = null;
    }

    private void ShowButtons()
    {
        _settings = false;
        Callable.From(Build).CallDeferred(); // not inside the pressed signal of a button Build frees
    }

    private void ShowSettings()
    {
        _settings = true;
        Callable.From(Build).CallDeferred();
    }

    private void Build()
    {
        if (!IsInstanceValid(this) || !Visible) return;
        ClearContent();
        var s = HudKit.ScaleFor(GetViewportRect().Size);
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0.008f, 0.012f, 0.03f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(shade);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(centre);
        _content = root;
        AddChild(root);

        if (_settings)
        {
            var panel = new SettingsPanel();
            panel.Build(s, inGame: true);
            panel.Done += ShowButtons;
            centre.AddChild(panel);
            return;
        }

        var card = HudKit.PanelBox(HudKit.Panel, HudKit.Accent, 10 * s, 28 * s, 22 * s);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.CustomMinimumSize = new Vector2(320 * s, 0);
        centre.AddChild(card);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        card.AddChild(box);
        var title = HudKit.Label("PAUSED", 34 * s, HudKit.Ink, condensed: true, bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);
        if (Subtitle.Length > 0)
        {
            var sub = HudKit.Label(Subtitle, 13 * s, HudKit.Dim, condensed: true);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(sub);
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * s), MouseFilter = MouseFilterEnum.Ignore });
        var resume = Button("RESUME", true, s, Close);
        Sfx.Tag(resume, "none"); // Close plays "close"
        box.AddChild(resume);
        box.AddChild(Button("SETTINGS", false, s, ShowSettings));
        box.AddChild(Button("MAIN MENU", false, s, ToMenu));
        box.AddChild(Button("QUIT", false, s, () => GetTree().Quit()));
        var hint = HudKit.Label("Esc: resume  ·  the main menu leaves this game (it isn't saved)", 12 * s, HudKit.Dim);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(hint);
    }

    private static Button Button(string text, bool primary, float s, System.Action onPress)
    {
        var b = HudKit.ActionButton(text, primary, s);
        b.CustomMinimumSize = new Vector2(0, 42 * s);
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        b.Pressed += onPress;
        return b;
    }

    private void ToMenu()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(GameSetup.MenuScene);
    }
}
